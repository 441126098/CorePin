using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using LoLCorePin.Models;
using LoLCorePin.Services;
using LoLCorePin.ViewModels;
using Forms = System.Windows.Forms;

namespace LoLCorePin;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon? _appIcon;
    private readonly Forms.ToolStripMenuItem _autoMenu;
    private CpuTopology? _topology;
    private ProcessWatcher? _watcher;
    private bool _ready, _exiting, _disposed;
    private string _lastSignature = "";
    private readonly Dictionary<string, string> _previous = new();

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
        menu.Items.Add("打开 LoLCorePin", null, (_, _) => Dispatcher.Invoke(ShowMain));
        _autoMenu = new Forms.ToolStripMenuItem("自动绑定") { Checked = _settings.AutoPin, CheckOnClick = false };
        _autoMenu.Click += (_, _) => Dispatcher.Invoke(() => AutoPinBox.IsChecked = !(AutoPinBox.IsChecked == true));
        menu.Items.Add(_autoMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _appIcon = LoadAppIcon();
        if (_appIcon != null)
        {
            try
            {
                var image = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    _appIcon.Handle,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                image.Freeze();
                Icon = image;
            }
            catch { }
        }
        _tray = new Forms.NotifyIcon { Icon = _appIcon ?? System.Drawing.SystemIcons.Application, Text = "LoLCorePin · 启动中", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);
        _ready = true;
        LoadTopology();
        if (!string.IsNullOrWhiteSpace(warning)) Notify(warning);
        AddLog(ErrorText.IsElevated ? "当前以管理员运行。" : "当前以普通用户运行；默认不请求管理员权限。");
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
                Notify("CPU 拓扑与已保存的手动配置不同，已恢复推荐选择并暂停自动绑定。确认后保存核心策略，再开启自动绑定。");
            }
            _vm.TopologyText = _topology.Summary + $" · {_topology.GroupCount} 个处理器组";
            _vm.ManualMode = _settings.UseManualSelection;
            var selected = SelectedCpus();
            _vm.Cpus.Clear();
            foreach (var cpu in _topology.Cpus)
                _vm.Cpus.Add(new CpuRow(cpu, selected.Any(c => c.Id == cpu.Id), !_topology.IsHybrid ? "同一等级" : cpu.EfficiencyClass == _topology.HighestClass ? "性能核 P（最高等级）" : "较低性能等级"));
            if (selected.Length == 0)
            {
                AutoPinBox.IsChecked = false;
                Notify("保存的手动选择为空，自动绑定已暂停。请选择至少一个逻辑处理器并保存。");
            }
            ApplyButton.IsEnabled = true;
            AutoPinBox.IsEnabled = true;
            _autoMenu.Enabled = true;
            if (_watcher == null)
            {
                _watcher = new ProcessWatcher(Configuration());
                _watcher.Updated += Watcher_Updated;
                _watcher.Start();
            }
            else _watcher.Configure(Configuration());
            UpdatePolicy();
            AddLog("CPU 拓扑读取完成：" + _topology.Summary);
        }
        catch (Exception ex)
        {
            if (_watcher != null) { _watcher.Updated -= Watcher_Updated; _watcher.Dispose(); }
            _watcher = null;
            _topology = null;
            ApplyButton.IsEnabled = false;
            AutoPinBox.IsEnabled = false;
            _autoMenu.Enabled = false;
            _vm.Status = "CPU 拓扑不可用，未进行绑定";
            _vm.Policy = "监控已停止。请检查错误后刷新拓扑。";
            Notify(ErrorText.Describe(ex));
        }
    }

    private CpuInfo[] SelectedCpus() => _topology == null ? Array.Empty<CpuInfo>() : _settings.UseManualSelection
        ? _topology.Cpus.Where(c => _settings.SelectedCpuKeys.Contains(c.Key)).ToArray()
        : _topology.Recommended.ToArray();

    private WatchConfiguration Configuration() => new(_topology!, SelectedCpus(), TargetRule.EnabledNames(_settings.Targets).ToArray(), _settings.AutoPin);

    private void UpdatePolicy()
    {
        string mode = _settings.AutoPin ? "自动绑定开启" : "自动绑定暂停（仅监视）";
        _vm.Policy = $"{mode} · {(_settings.UseManualSelection ? "手动选择" : "自动推荐")} {SelectedCpus().Length} 个逻辑处理器 · 500ms 轮询 · " +
            (_topology?.GroupCount == 1 ? "CPU Sets + 硬亲和性" : "跨组 CPU Sets（软亲和性）");
        _vm.SelectedSummary = $"已保存：{SelectedCpus().Length} 个逻辑处理器。此处修改须保存后生效。";
        _autoMenu.Checked = _settings.AutoPin;
    }

    private void Watcher_Updated(WatchSnapshot snapshot)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => UpdateSnapshot(snapshot)));
    }

    private void UpdateSnapshot(WatchSnapshot snapshot)
    {
        if (_disposed || _watcher == null) return;
        _vm.LastCheck = $"最近检查 {snapshot.Time:HH:mm:ss} · {(ErrorText.IsElevated ? "管理员" : "普通用户")} · 关闭窗口隐藏到托盘";
        _vm.Status = snapshot.Error.Length > 0 ? "监控失败：" + snapshot.Error : snapshot.GameProcessCount > 0
            ? $"LOL 正在运行 · {snapshot.GameProcessCount} 个游戏进程 · {snapshot.Processes.Count(p => p.IsCompliant)} / {snapshot.Processes.Length} 个目标符合选择"
            : snapshot.Processes.Length > 0 ? $"默认 LOL 游戏进程未运行 · 已发现 {snapshot.Processes.Length} 个其他配置目标"
            : "等待游戏启动 · 当前没有匹配的已启用目标";
        _tray.Text = snapshot.Error.Length > 0 ? "LoLCorePin · 监控异常" : $"LoLCorePin · {snapshot.Processes.Length} 个目标 · {(_settings.AutoPin ? "自动绑定" : "已暂停")}";
        string signature = snapshot.Error + string.Join("\n", snapshot.Processes.Select(p => p.Signature));
        if (_lastSignature != signature)
        {
            string? selection = (StatusGrid.SelectedItem as ProcessReport)?.Identity;
            _vm.Processes.Clear();
            foreach (var report in snapshot.Processes) _vm.Processes.Add(report);
            StatusGrid.SelectedItem = _vm.Processes.FirstOrDefault(p => p.Identity == selection) ?? _vm.Processes.FirstOrDefault();
            foreach (var report in snapshot.Processes)
            {
                if (!_previous.TryGetValue(report.Identity, out string? previous) || previous != report.Signature)
                    AddLog($"{report.Name} [{report.Id}] {report.Status} · {report.Qos}" + (report.Detail.Length > 0 ? " · " + report.Detail : ""));
            }
            foreach (var ended in _previous.Keys.Except(snapshot.Processes.Select(p => p.Identity))) AddLog($"目标 {ended.Split(':')[0]} 已退出或不再匹配。");
            _previous.Clear();
            foreach (var report in snapshot.Processes) _previous[report.Identity] = report.Signature;
            if (snapshot.Error.Length > 0) AddLog("监控错误：" + snapshot.Error);
            _lastSignature = signature;
        }
        foreach (var report in snapshot.Processes.Where(p => p.Changed)) AddLog($"已应用 / 修复 {report.Name} [{report.Id}]，读回结果：{report.Status}。");
        if (snapshot.AppliedManually) Notify(snapshot.Processes.Length == 0 ? "当前没有匹配的目标，未修改任何进程。" : "手动应用已完成，请查看各进程的验证结果。", false);
    }

    private bool Persist()
    {
        try { _store.Save(_settings); return true; }
        catch (Exception ex) { Notify("设置在本次运行中生效，但未能保存：" + ErrorText.Describe(ex)); return false; }
    }

    private void AutoPin_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.AutoPin = AutoPinBox.IsChecked == true;
        Persist();
        if (_topology != null) _watcher?.Configure(Configuration());
        UpdatePolicy();
    }

    private void Apply_Click(object sender, RoutedEventArgs e) => _watcher?.ApplyNow();
    private void RefreshTopology_Click(object sender, RoutedEventArgs e) => LoadTopology();

    private void ManualMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _topology == null || ManualBox.IsChecked == true) return;
        foreach (var row in _vm.Cpus) row.Selected = row.Cpu.EfficiencyClass == _topology.HighestClass;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        _vm.ManualMode = true;
        foreach (var row in _vm.Cpus) row.Selected = true;
    }

    private void Recommended_Click(object sender, RoutedEventArgs e)
    {
        _vm.ManualMode = false;
        if (_topology != null) foreach (var row in _vm.Cpus) row.Selected = row.Cpu.EfficiencyClass == _topology.HighestClass;
    }

    private void SaveCores_Click(object sender, RoutedEventArgs e)
    {
        if (_topology == null) return;
        var keys = _vm.Cpus.Where(c => c.Selected).Select(c => c.Cpu.Key).ToList();
        if (keys.Count == 0) { Notify("至少选择一个逻辑处理器；空选择不会被保存或应用。"); return; }
        _settings.UseManualSelection = _vm.ManualMode;
        _settings.SelectedCpuKeys = keys;
        _settings.TopologyFingerprint = _topology.Fingerprint;
        bool saved = Persist();
        _watcher?.Configure(Configuration());
        UpdatePolicy();
        if (saved) Notify("核心策略已保存。自动绑定开启时将在下一次检查应用。", false);
    }

    private void AddTarget(string name)
    {
        name = TargetRule.Normalize(name);
        if (_vm.Targets.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { Notify("监视列表中已有此文件名。"); return; }
        _vm.Targets.Add(new TargetRule { Name = name, Enabled = true });
        Notify("已添加到编辑列表，请点击“保存监视列表”使其生效。", false);
    }

    private void AddTarget_Click(object sender, RoutedEventArgs e)
    {
        try { AddTarget(NewTargetBox.Text); NewTargetBox.Clear(); } catch (Exception ex) { Notify(ErrorText.Describe(ex)); }
    }
    private void RemoveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (TargetGrid.SelectedItem is TargetRule rule) _vm.Targets.Remove(rule);
    }
    private void Discover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProcessPickerWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedProcessName is string name)
            try { AddTarget(name); } catch (Exception ex) { Notify(ErrorText.Describe(ex)); }
    }
    private void SaveTargets_Click(object sender, RoutedEventArgs e)
    {
        TargetGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
        TargetGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        try
        {
            var rules = _vm.Targets.Select(t => new TargetRule { Name = TargetRule.Normalize(t.Name), Enabled = t.Enabled }).ToList();
            if (rules.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new ArgumentException("列表存在重复进程名，请移除重复项。");
            _settings.Targets = rules;
            bool saved = Persist();
            if (_topology != null) _watcher?.Configure(Configuration());
            if (saved) Notify($"监视列表已保存，启用 {rules.Count(t => t.Enabled)} 个文件名。", false);
        }
        catch (Exception ex) { Notify(ErrorText.Describe(ex)); }
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        try { StartupService.SetEnabled(StartupBox.IsChecked == true); Notify("当前用户的登录启动设置已更新。", false); }
        catch (Exception ex)
        {
            _ready = false;
            StartupBox.IsChecked = !(StartupBox.IsChecked == true);
            _ready = true;
            Notify("开机启动设置失败：" + ErrorText.Describe(ex));
        }
    }

    private void Notify(string text, bool error = true)
    {
        _vm.Notice = text;
        AddLog((error ? "提示：" : "") + text);
    }
    private void AddLog(string text)
    {
        _vm.Logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (_vm.Logs.Count > 250) _vm.Logs.RemoveAt(_vm.Logs.Count - 1);
    }
    private void ShowMain() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        Hide();
    }
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
        if (_watcher != null) { _watcher.Updated -= Watcher_Updated; _watcher.Dispose(); }
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _appIcon?.Dispose();
    }
}
