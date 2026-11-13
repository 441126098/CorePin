using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CorePin.Controls;
using CorePin.Models;
using CorePin.Services;
using CorePin.ViewModels;
using Forms = System.Windows.Forms;

namespace CorePin;

public partial class MainWindow : Window
{
    private const string GameName = "League of Legends.exe";
    private readonly MainViewModel _vm = new();
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly bool _elevated = ErrorText.IsElevated;
    private readonly Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon? _appIcon;
    private readonly Forms.ToolStripMenuItem _autoMenu;
    private readonly Dictionary<string, ProcessReport> _previous = new();
    private readonly List<CoreChip> _chips = new();
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private CpuTopology? _topology;
    private ProcessWatcher? _watcher;
    private WatchSnapshot? _snapshot;
    private CpuRow? _inspected;
    private bool _ready, _exiting, _disposed, _closeExplained, _closingDialog;
    private string _lastWatchError = "", _lastBindingError = "";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _settings = _store.Load(out string warning);
        foreach (var rule in _settings.Targets) _vm.Targets.Add(new TargetRule { Name = rule.Name, Enabled = rule.Enabled });
        AutoPinBox.IsChecked = _settings.AutoPin;
        try { StartupBox.IsChecked = StartupService.IsEnabled(); }
        catch (Exception ex) { warning += " " + ErrorText.Describe(ex); }

        Forms.Application.EnableVisualStyles();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => Dispatcher.Invoke(ShowMain));
        _autoMenu = new Forms.ToolStripMenuItem("自动绑定") { Checked = _settings.AutoPin, CheckOnClick = false };
        _autoMenu.Click += (_, _) => Dispatcher.Invoke(() => AutoPinBox.IsChecked = !(AutoPinBox.IsChecked == true));
        menu.Items.Add(_autoMenu);
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _appIcon = LoadAppIcon();
        if (_appIcon != null)
        {
            try
            {
                var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(_appIcon.Handle,
                    Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                Icon = image;
            }
            catch { /* The executable's associated icon is optional; never parse the ICO in XAML. */ }
        }
        _tray = new Forms.NotifyIcon
        {
            Icon = _appIcon ?? System.Drawing.SystemIcons.Application,
            Text = "CorePin · 正在启动", ContextMenuStrip = menu, Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            CoresToast.Visibility = TargetsToast.Visibility = Visibility.Collapsed;
            FitContent();
        };
        _ready = true;
        // Elevation is shown before the first watcher result, even with no running game.
        UpdateMainStatus();
        LoadTopology();
        if (!string.IsNullOrWhiteSpace(warning)) ShowError(warning);
        AddLog(_elevated ? "CorePin 已以管理员身份启动。" : "CorePin 需要管理员权限，请手动重新启动。");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        MaxHeight = SystemParameters.WorkArea.Height;
        MainScroll.MaxHeight = Math.Max(250, MaxHeight - 100);
        StateChanged += (_, _) =>
        {
            MainScroll.MaxHeight = WindowState == WindowState.Maximized ? double.PositiveInfinity : Math.Max(250, MaxHeight - 100);
        };
    }

    private void LoadTopology()
    {
        try
        {
            _topology = new CpuTopologyService().Read();
            if (_settings.UseManualSelection && _settings.TopologyFingerprint != _topology.Fingerprint)
            {
                _settings.UseManualSelection = false;
                _settings.AutoPin = false;
                AutoPinBox.IsChecked = false;
                ShowError("处理器配置已变化，已恢复推荐并暂停自动绑定。请确认核心策略后重新开启。");
            }
            var selected = SelectedCpus();
            _snapshot = null;
            _vm.Cpus.Clear();
            foreach (var cpu in _topology.Cpus)
                _vm.Cpus.Add(new CpuRow(cpu, selected.Any(c => c.Id == cpu.Id), cpu.EfficiencyClass == _topology.HighestClass));
            if (selected.Length == 0)
            {
                AutoPinBox.IsChecked = false;
                ShowError("核心策略为空，自动绑定已暂停。请至少选择一个逻辑处理器并保存。");
            }
            _inspected = null;
            CoreDetail.Visibility = Visibility.Collapsed;
            DrawCoreMap();
            ApplyButton.IsEnabled = AutoPinBox.IsEnabled = _autoMenu.Enabled = true;
            if (_watcher == null)
            {
                _watcher = new ProcessWatcher(Configuration());
                _watcher.Updated += Watcher_Updated;
                _watcher.Start();
            }
            else _watcher.Configure(Configuration());
            UpdatePolicy();
        }
        catch (Exception ex)
        {
            if (_watcher != null) { _watcher.Updated -= Watcher_Updated; _watcher.Dispose(); }
            _watcher = null;
            _topology = null;
            _vm.Cpus.Clear();
            PerformanceMap.Children.Clear();
            EfficiencyMap.Children.Clear();
            PerformanceTitle.Text = "处理器拓扑暂不可用";
            EfficiencySection.Visibility = Visibility.Collapsed;
            CoreDetail.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = AutoPinBox.IsEnabled = _autoMenu.Enabled = false;
            ShowError("无法读取处理器配置。请在技术详情中刷新拓扑。");
            CheckText.Text = ErrorText.Describe(ex);
            UpdateMainStatus();
        }
    }

    private CpuInfo[] SelectedCpus() => _topology == null ? Array.Empty<CpuInfo>() : _settings.UseManualSelection
        ? _topology.Cpus.Where(c => _settings.SelectedCpuKeys.Contains(c.Key)).ToArray()
        : _topology.Recommended.ToArray();

    private WatchConfiguration Configuration() => new(_topology!, SelectedCpus(),
        TargetRule.EnabledNames(_settings.Targets).ToArray(), _settings.AutoPin);

    private void DrawCoreMap()
    {
        PerformanceMap.Children.Clear();
        EfficiencyMap.Children.Clear();
        _chips.Clear();
        if (_topology == null) return;
        var groups = CoreLayout.Group(_topology);
        var performance = groups.Where(g => g.IsPerformance).ToArray();
        var threads = performance.Select(g => g.Threads.Count).Distinct().ToArray();
        string smt = threads.Length == 1 ? $"，每核 {threads[0]} 个线程" : "，线程数不等";
        PerformanceTitle.Text = $"性能核 · {_topology.Recommended.Count} 个逻辑处理器（{performance.Length} 个核心{smt}）";
        int efficiency = _topology.Cpus.Count - _topology.Recommended.Count;
        EfficiencyTitle.Text = $"能效核 · {efficiency} 个逻辑处理器（默认不纳入）";
        EfficiencySection.Visibility = efficiency == 0 ? Visibility.Collapsed : Visibility.Visible;
        TopologyNote.Visibility = !_topology.IsHybrid ? Visibility.Visible : Visibility.Collapsed;
        TopologyNote.Text = "此处理器未报告性能等级差异，推荐使用全部逻辑处理器。";
        foreach (var group in groups)
        {
            var body = new StackPanel();
            if (group.IsPerformance)
                body.Children.Add(new TextBlock
                {
                    Text = _topology.GroupCount > 1 ? $"G{group.Group} 核心 {group.Index}" : $"核心 {group.Index}",
                    FontSize = 9, Foreground = Brush("#A77A08"), HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 2)
                });
            var siblings = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var cpu in group.Threads)
            {
                var row = _vm.Cpus.Single(c => c.Cpu.Id == cpu.Id);
                var chip = new CoreChip(row, NumbersBox.IsChecked == true);
                chip.Inspected += InspectCore;
                _chips.Add(chip);
                siblings.Children.Add(chip);
            }
            body.Children.Add(siblings);
            var surface = new Border
            {
                Child = body, CornerRadius = new CornerRadius(6),
                BorderBrush = group.IsPerformance ? Brush("#FFF0C2") : Brushes.Transparent,
                Background = group.IsPerformance ? Brush("#FFFAEB") : Brushes.Transparent,
                BorderThickness = new Thickness(group.IsPerformance ? 1 : 0),
                Padding = new Thickness(0, group.IsPerformance ? 1 : 0, 0, group.IsPerformance ? 3 : 0),
                Margin = new Thickness(0, 0, 2, 3)
            };
            (group.IsPerformance ? PerformanceMap : EfficiencyMap).Children.Add(surface);
        }
        FitContent();
    }

    private void InspectCore(CpuRow row)
    {
        _inspected = row;
        CoreDetail.Visibility = Visibility.Visible;
        CoreDetailTitle.Text = $"{(row.IsPerformance ? "性能核" : "能效核")} · {row.Cpu.LogicalLabel}";
        CoreDetailText.Text = $"物理核心：{row.Cpu.CoreIndex}（处理器组 {row.Cpu.Group}）   逻辑编号：{row.Cpu.LogicalIndex}\n" +
            $"EfficiencyClass：{row.Cpu.EfficiencyClass}   CPU Set ID：{row.Cpu.Id}\n" +
            $"可用状态：{row.Cpu.Availability}   {(row.Selected ? "已纳入策略" : "未纳入策略")}（保存后生效）";
        FitContent();
    }

    private void UpdatePolicy()
    {
        var selected = SelectedCpus();
        bool recommended = _topology != null && selected.Select(c => c.Id).Order().SequenceEqual(_topology.Recommended.Select(c => c.Id).Order());
        _vm.Policy = recommended ? "当前策略：全部性能核（推荐）" : $"当前策略：自定义 {selected.Length} 个逻辑处理器";
        _autoMenu.Checked = _settings.AutoPin;
        UpdateMainStatus();
    }

    private void UpdateMainStatus()
    {
        int count = SelectedCpus().Length;
        string color = "#4B5E89", tint = "#EEF0F5", glyph = "•••";
        bool running = _snapshot?.GameProcessCount > 0;
        _vm.Running = running ? "● 运行中" : "● 未运行";
        RunningText.Foreground = Brush(running ? "#0F7A5A" : "#838D9B");
        ElevateButton.Visibility = _elevated ? Visibility.Collapsed : Visibility.Visible;
        if (!_elevated)
        {
            _vm.Status = "需要管理员权限";
            _vm.Summary = "不提权就无法完成绑定。这不会修改游戏文件，只是允许调整处理器分配。";
            color = "#925A0C"; tint = "#FFF5E3"; glyph = "!";
        }
        else if (_topology == null)
        {
            _vm.Status = "无法读取处理器配置";
            _vm.Summary = "请展开技术详情，检查错误后刷新拓扑。";
            color = "#925A0C"; tint = "#FFF5E3"; glyph = "!";
        }
        else if (_snapshot?.Error.Length > 0)
        {
            _vm.Status = "暂时无法监视游戏";
            _vm.Summary = "请查看技术详情，下一次检查会自动重试。";
            color = "#925A0C"; tint = "#FFF5E3"; glyph = "!";
        }
        else if (running)
        {
            var games = _snapshot!.Processes.Where(p => p.Name.Equals(GameName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (games.Length > 0 && games.All(p => p.IsCompliant))
            {
                bool allPerformance = SelectedCpus().All(c => c.EfficiencyClass == _topology.HighestClass);
                _vm.Status = $"已绑定到 {count} 个{(allPerformance ? "性能核" : "逻辑处理器")}";
                bool onlyGame = TargetRule.EnabledNames(_settings.Targets).SetEquals(new[] { GameName });
                bool recommended = SelectedCpus().Select(c => c.Id).Order().SequenceEqual(_topology.Recommended.Select(c => c.Id).Order());
                _vm.Summary = (onlyGame ? "只监视游戏，不监视客户端" : "按监视列表监视已启用的进程") + "\n" +
                    (recommended ? $"使用全部 {count} 个性能核" : $"使用 {count} 个自定义逻辑处理器");
                color = "#0F7A5A"; tint = "#E6F4ED"; glyph = "✓";
            }
            else
            {
                _vm.Status = _settings.AutoPin ? "绑定需要处理" : "自动绑定已暂停";
                _vm.Summary = !_settings.AutoPin ? "当前仅监视游戏。开启自动绑定，或点击立即应用。" :
                    games.Any(p => p.Detail.Length > 0) ? BindingFailure(games.First(p => p.Detail.Length > 0).Detail) : "正在应用核心策略，请稍候。";
                color = "#925A0C"; tint = "#FFF5E3"; glyph = "!";
            }
        }
        else
        {
            _vm.Status = "未检测到游戏";
            string policy = _settings.UseManualSelection ? "核心策略已就绪" : "推荐策略已就绪";
            string selection = SelectedCpus().All(c => c.EfficiencyClass == _topology.HighestClass) ? $"{count} 个性能核" : $"{count} 个逻辑处理器";
            _vm.Summary = _settings.AutoPin
                ? $"{policy}，自动绑定已开启。\n打开游戏后会自动绑定到 {selection}。"
                : $"{policy}，自动绑定已暂停。\n打开游戏后可点击立即应用。";
        }
        StatusStripe.Background = StatusCircle.BorderBrush = StatusGlyph.Foreground = Brush(color);
        StatusCircle.Background = Brush(tint);
        StatusGlyph.Text = glyph;
        string tooltip = "CorePin · " + _vm.Status;
        _tray.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    private string BindingFailure(string detail) => detail.Contains("访问被拒绝", StringComparison.Ordinal)
        ? _elevated ? "无法应用绑定。进程保护或反作弊仍可能阻止访问，请查看技术详情。" : "无法应用绑定。需要管理员权限。"
        : "无法应用绑定。请查看技术详情中的原因后重试。";

    private void Watcher_Updated(WatchSnapshot snapshot)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => UpdateSnapshot(snapshot)));
    }

    private void UpdateSnapshot(WatchSnapshot snapshot)
    {
        if (_disposed || _watcher == null) return;
        _snapshot = snapshot;
        CheckText.Text = $"最近检查 {snapshot.Time:HH:mm:ss} · {(_elevated ? "管理员" : "普通用户")}\n每 500ms 检查并重新应用" +
            (_topology?.GroupCount > 1 ? " · 跨组 CPU Sets（软亲和性）" : " · CPU Sets + 亲和性") +
            (snapshot.Error.Length > 0 ? "\n错误详情：" + snapshot.Error : "");
        bool changed = !_vm.Processes.Select(p => p.Signature).SequenceEqual(snapshot.Processes.Select(p => p.Signature));
        if (changed)
        {
            _vm.Processes.Clear();
            foreach (var report in snapshot.Processes) _vm.Processes.Add(report);
        }
        foreach (var report in snapshot.Processes)
        {
            bool known = _previous.TryGetValue(report.Identity, out var old);
            if (!known) AddLog($"检测到{(report.Name.Equals(GameName, StringComparison.OrdinalIgnoreCase) ? "游戏" : report.Name)}，PID {report.Id}。");
            if (report.IsCompliant && (!known || !old!.IsCompliant))
                AddLog($"已将{(report.Name.Equals(GameName, StringComparison.OrdinalIgnoreCase) ? "游戏" : report.Name)}绑定到 {SelectedCpus().Length} 个{(SelectedCpus().All(c => c.EfficiencyClass == _topology!.HighestClass) ? "性能核" : "逻辑处理器")}。");
            else if (report.IsCompliant && report.Changed) AddLog("检测到处理器分配变化，已重新应用核心策略。");
            else if (!report.IsCompliant && report.Detail.Length > 0 && (!known || old!.Detail != report.Detail))
                AddLog(BindingFailure(report.Detail));
        }
        foreach (var ended in _previous.Values.Where(p => !snapshot.Processes.Any(n => n.Identity == p.Identity)))
            AddLog(ended.Name.Equals(GameName, StringComparison.OrdinalIgnoreCase) ? "游戏已退出，继续等待下一次启动。" : $"{ended.Name} 已退出或不再监视。");
        _previous.Clear();
        foreach (var report in snapshot.Processes) _previous[report.Identity] = report;
        if (snapshot.Error.Length > 0 && snapshot.Error != _lastWatchError) ShowError("暂时无法检查游戏进程，将自动重试。请查看技术详情。");
        _lastWatchError = snapshot.Error;
        string failure = string.Join("|", snapshot.Processes.Where(p => !p.IsCompliant).Select(p => p.Detail).Where(d => d.Length > 0));
        if (failure.Length > 0 && failure != _lastBindingError && _settings.AutoPin) ShowError(BindingFailure(failure), !_elevated);
        _lastBindingError = failure;
        if (snapshot.AppliedManually)
        {
            if (snapshot.Processes.Length == 0) ShowToast("当前未检测到目标进程，策略已就绪。");
            else if (snapshot.Processes.All(p => p.IsCompliant)) ShowToast("核心策略已应用。");
            else ShowError(BindingFailure(failure), !_elevated);
        }
        UpdateMainStatus();
    }

    private bool Persist()
    {
        try { _store.Save(_settings); return true; }
        catch (Exception ex)
        {
            ShowError("设置在本次运行中生效，但未能保存。请检查设置文件权限。");
            AddLog("设置保存失败：" + ErrorText.Describe(ex));
            return false;
        }
    }

    private void AutoPin_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.AutoPin = AutoPinBox.IsChecked == true;
        Persist();
        if (_topology != null) _watcher?.Configure(Configuration());
        AddLog(_settings.AutoPin ? "自动绑定已开启。" : "自动绑定已暂停。");
        UpdatePolicy();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!_elevated) { ShowError("无法应用绑定。需要管理员权限。", true); return; }
        _watcher?.ApplyNow();
    }
    private void RefreshTopology_Click(object sender, RoutedEventArgs e) => LoadTopology();
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _vm.Cpus) row.Selected = true;
        if (_inspected != null) InspectCore(_inspected);
    }
    private void Recommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _vm.Cpus) row.Selected = row.IsPerformance;
        if (_inspected != null) InspectCore(_inspected);
    }
    private void SaveCores_Click(object sender, RoutedEventArgs e)
    {
        if (_topology == null) return;
        var selected = _vm.Cpus.Where(c => c.Selected).Select(c => c.Cpu).ToArray();
        if (selected.Length == 0) { ShowError("请至少选择一个逻辑处理器，核心策略尚未保存。"); return; }
        _settings.UseManualSelection = !selected.Select(c => c.Id).Order().SequenceEqual(_topology.Recommended.Select(c => c.Id).Order());
        _settings.SelectedCpuKeys = selected.Select(c => c.Key).ToList();
        _settings.TopologyFingerprint = _topology.Fingerprint;
        bool saved = Persist();
        // A new policy must be read back before a previous success can be shown again.
        _snapshot = null;
        _watcher?.Configure(Configuration());
        UpdatePolicy();
        if (saved) ShowToast("核心策略已保存。");
    }

    private void AddTarget(string name)
    {
        name = TargetRule.Normalize(name);
        if (_vm.Targets.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("监视列表中已有此文件名。");
        _vm.Targets.Add(new TargetRule { Name = name, Enabled = true });
        ShowToast("已加入列表，请点击保存监视列表。", true);
    }
    private void AddTarget_Click(object sender, RoutedEventArgs e)
    {
        try { AddTarget(NewTargetBox.Text); NewTargetBox.Clear(); }
        catch (Exception ex) { ShowError(ErrorText.Describe(ex)); }
    }
    private void RemoveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TargetRule rule }) _vm.Targets.Remove(rule);
        FitContent();
    }
    private void Discover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProcessPickerWindow { Owner = this, Icon = Icon };
        if (dialog.ShowDialog() == true && dialog.SelectedProcessName is string name)
            try { AddTarget(name); } catch (Exception ex) { ShowError(ErrorText.Describe(ex)); }
    }
    private void SaveTargets_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // TextBox bindings update on each keystroke; validation remains at the save boundary.
            var rules = _vm.Targets.Select(t => new TargetRule { Name = TargetRule.Normalize(t.Name), Enabled = t.Enabled }).ToList();
            if (rules.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new ArgumentException("列表存在重复进程名，请移除重复项。");
            _settings.Targets = rules;
            for (int index = 0; index < rules.Count; index++) _vm.Targets[index].Name = rules[index].Name;
            bool saved = Persist();
            if (_topology != null) _watcher?.Configure(Configuration());
            UpdatePolicy();
            if (saved) ShowToast("监视列表已保存。", true);
        }
        catch (Exception ex) { ShowError(ErrorText.Describe(ex)); }
    }
    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        try { StartupService.SetEnabled(StartupBox.IsChecked == true); ShowToast("启动设置已保存。", true); }
        catch (Exception ex)
        {
            _ready = false;
            // Migration can remove the legacy entry before a new write fails; reflect actual state.
            try { StartupBox.IsChecked = StartupService.IsEnabled(); }
            catch { StartupBox.IsChecked = false; }
            _ready = true;
            ShowError("开机启动设置失败：" + ErrorText.Describe(ex));
        }
    }

    private void Programs_Click(object sender, RoutedEventArgs e) => TogglePanel(ProgramsPanel);
    private void Logs_Click(object sender, RoutedEventArgs e) => TogglePanel(LogsPanel);
    private void TogglePanel(Border panel)
    {
        bool open = panel.Visibility != Visibility.Visible;
        ProgramsPanel.Visibility = LogsPanel.Visibility = Visibility.Collapsed;
        if (open) panel.Visibility = Visibility.Visible;
        ProgramsButton.Tag = ProgramsPanel.Visibility == Visibility.Visible ? "Open" : null;
        LogsButton.Tag = LogsPanel.Visibility == Visibility.Visible ? "Open" : null;
        FitContent();
    }
    private void Technical_Click(object sender, RoutedEventArgs e)
    {
        TechnicalPanel.Visibility = TechnicalPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        FitContent();
    }
    private void Numbers_Changed(object sender, RoutedEventArgs e)
    {
        foreach (var chip in _chips) chip.ShowNumbers(NumbersBox.IsChecked == true);
        FitContent();
    }
    private void CloseCoreDetail_Click(object sender, RoutedEventArgs e)
    {
        _inspected = null;
        CoreDetail.Visibility = Visibility.Collapsed;
        FitContent();
    }
    private void FitContent()
    {
        if (!_ready || WindowState != WindowState.Normal) return;
        SizeToContent = SizeToContent.Height;
    }
    private void ShowError(string text, bool offerElevation = false)
    {
        ErrorTextBlock.Text = text;
        ErrorElevateButton.Visibility = offerElevation && !_elevated ? Visibility.Visible : Visibility.Collapsed;
        ErrorBar.Visibility = Visibility.Visible;
        AddLog(text);
        FitContent();
    }
    private void DismissError_Click(object sender, RoutedEventArgs e) { ErrorBar.Visibility = Visibility.Collapsed; FitContent(); }
    private void ShowToast(string text, bool targets = false)
    {
        if (targets) { TargetsToastText.Text = text; TargetsToast.Visibility = Visibility.Visible; }
        else { CoresToastText.Text = text; CoresToast.Visibility = Visibility.Visible; }
        _toastTimer.Stop();
        _toastTimer.Start();
        AddLog(text);
        FitContent();
    }
    private void AddLog(string text)
    {
        _vm.Logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (_vm.Logs.Count > 250) _vm.Logs.RemoveAt(_vm.Logs.Count - 1);
    }

    private void Elevate_Click(object sender, RoutedEventArgs e)
    {
        if (_elevated) return;
        try
        {
            string path = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
            if (!System.IO.Path.GetFileName(path).Equals("CorePin.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请先从 CorePin.exe 启动，再以管理员身份重新启动。");
            var child = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true, Verb = "runas", Arguments = "--elevated-restart",
                WorkingDirectory = AppContext.BaseDirectory
            });
            if (child == null) throw new InvalidOperationException("管理员实例未能启动，请重试。");
            child.Dispose();
            // Only shut down after runas returns successfully. UAC cancellation keeps this instance alive.
            ExitApplication();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            ShowToast("已取消管理员重启，继续在当前窗口运行。");
        }
        catch (Exception ex) { ShowError("无法以管理员身份重新启动：" + ErrorText.Describe(ex)); }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ShowMain() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        if (_closingDialog) return;
        if (!_settings.RememberCloseToTray && !_closeExplained)
        {
            _closingDialog = true;
            var dialog = new CloseToTrayWindow { Owner = this, Icon = Icon };
            bool? result = dialog.ShowDialog();
            _closingDialog = false;
            if (result != true) return;
            _closeExplained = true;
            if (dialog.Remember) { _settings.RememberCloseToTray = true; Persist(); }
            if (dialog.ExitRequested) { Dispatcher.BeginInvoke(new Action(ExitApplication)); return; }
        }
        Hide();
    }

    private static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path)) return System.Drawing.Icon.ExtractAssociatedIcon(path);
        }
        catch { }
        return null;
    }
    private void ExitApplication() { _exiting = true; DisposeResources(); Application.Current.Shutdown(); }

    internal void DisposeResources()
    {
        if (_disposed) return;
        _disposed = true;
        _toastTimer.Stop();
        if (_watcher != null) { _watcher.Updated -= Watcher_Updated; _watcher.Dispose(); }
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _appIcon?.Dispose();
    }
}
