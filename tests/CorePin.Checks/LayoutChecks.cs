using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using CorePin.Controls;
using CorePin.Models;
using CorePin.ViewModels;

// Runs the real layout and chip templates on an STA thread, without constructing
// App/MainWindow, showing a Window/Popup, acquiring the mutex, or starting a watcher.
internal static class LayoutChecks
{
    public static void Run(Action<bool, string> check, string[] args)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { Verify(check, args); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException("Headless WPF layout check failed.", failure);
    }

    private static void Verify(Action<bool, string> check, string[] args)
    {
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        string source = Path.Combine(AppContext.BaseDirectory, "LayoutSource");
        var window = XElement.Load(Path.Combine(source, "MainWindow.xaml"));
        check(new[] { "Width", "MinWidth" }.All(a => (string?)window.Attribute(a) == "920") &&
            new[] { "Height", "MinHeight" }.All(a => (string?)window.Attribute(a) == "740") &&
            (string?)window.Attribute("SizeToContent") == "Manual", "main window explicitly uses Manual 920 x 740 and matching minima");
        string handlers = File.ReadAllText(Path.Combine(source, "MainWindow.xaml.cs"));
        check(!handlers.Contains("SizeToContent =") && !handlers.Contains("FitContent"),
            "interaction handlers cannot restore content-driven window sizing");

        var markup = new XElement(window.Element(p + "Grid")!);
        markup.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        markup.SetAttributeValue(XNamespace.Xmlns + "shell", "clr-namespace:System.Windows.Shell;assembly=PresentationFramework");
        var resources = XElement.Load(Path.Combine(source, "App.xaml")).Element(p + "Application.Resources")!;
        markup.AddFirst(new XElement(p + "Grid.Resources", resources.Elements().Select(e => new XElement(e))));
        // Event wiring is compiled by the app build. The layout fixture intentionally
        // has no MainWindow instance and therefore removes only its event attributes.
        var events = new HashSet<string> { "Click", "Checked", "Unchecked" };
        foreach (var attribute in markup.DescendantsAndSelf().Attributes().Where(a => events.Contains(a.Name.LocalName)).ToArray())
            attribute.Remove();
        var root = (Grid)XamlReader.Parse(markup.ToString());
        root.Background = new SolidColorBrush(Color.FromRgb(243, 244, 247));
        TextElementFont(root);
        var vm = new MainViewModel
        {
            Status = "已绑定到 12 个性能核", Running = "● 运行中",
            Policy = "当前策略：全部性能核（推荐）",
            Summary = "只监视游戏，不监视客户端  ·  使用全部 12 个性能核"
        };
        foreach (var target in TargetRule.Defaults()) vm.Targets.Add(target);
        for (int i = 0; i < 250; i++) vm.Logs.Add(new LogEntry("21:52:01", $"布局验证记录 {i + 1}：已将游戏绑定到性能核。"));
        root.DataContext = vm;
        T Named<T>(string name) where T : FrameworkElement => (T)root.FindName(name);
        var chips = new List<CoreChip>();
        // Representative hybrid topology is test data, never an application default.
        for (int i = 0; i < 6; i++)
        {
            var group = new StackPanel();
            group.Children.Add(new TextBlock { Text = $"核心 {i}", Height = 12, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center });
            var pair = new StackPanel { Orientation = Orientation.Horizontal };
            for (int j = 0; j < 2; j++) pair.Children.Add(Chip(i * 2 + j, true));
            group.Children.Add(pair);
            Named<WrapPanel>("PerformanceMap").Children.Add(new Border
            {
                Child = group, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                BorderBrush = Brushes.PaleGoldenrod, Background = Brushes.FloralWhite, Margin = new Thickness(0, 0, 6, 0)
            });
        }
        for (int i = 12; i < 20; i++) Named<WrapPanel>("EfficiencyMap").Children.Add(Chip(i, false));
        Named<TextBlock>("PerformanceTitle").Text = "性能核 · 12 个逻辑处理器（6 个核心，每核 2 个线程）";
        Named<TextBlock>("EfficiencyTitle").Text = "能效核 · 8 个逻辑处理器（默认不纳入）";
        Named<TextBlock>("CoreDetailTitle").Text = "逻辑处理器 0 · 性能核 · 已纳入策略（保存后生效）";
        Named<TextBlock>("CoreDetailText").Text = "物理核心：0（处理器组 0）   逻辑编号：0   EfficiencyClass：1   CPU Set ID：256\n可用状态：可用。推荐将同一物理核心的线程一并纳入。";
        Named<TextBlock>("ErrorTextBlock").Text = "无法应用绑定。需要管理员权限。";
        Named<TextBlock>("CheckText").Text = "最近检查 21:52:01 · 管理员\n每 500ms 检查并重新应用 CPU Sets 与亲和性。";
        Named<CheckBox>("AutoPinBox").IsChecked = Named<CheckBox>("StartupBox").IsChecked = true;

        CoreChip Chip(int index, bool performance)
        {
            var row = new CpuRow(new CpuInfo((uint)(256 + index), 0, (byte)index, (byte)(index / 2), (byte)(performance ? 1 : 0), 0), performance, performance);
            var chip = new CoreChip(row, false);
            chips.Add(chip);
            return chip;
        }
        void Layout(double width = 920, double height = 740)
        {
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
        }
        void Dock(int mode)
        {
            Named<Border>("DefaultPanel").Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
            Named<Border>("ProgramsPanel").Visibility = mode == 1 ? Visibility.Visible : Visibility.Collapsed;
            Named<Border>("LogsPanel").Visibility = mode == 2 ? Visibility.Visible : Visibility.Collapsed;
            Named<Button>("ProgramsButton").Tag = mode == 1 ? "Open" : null;
            Named<Button>("LogsButton").Tag = mode == 2 ? "Open" : null;
        }
        bool Near(double a, double b) => Math.Abs(a - b) < 0.1;
        Layout();
        double[] heights = { 48, 12, 88, 10, 308, 10, 40, 212, 12 };
        check(root.RowDefinitions.Select((row, i) => Near(row.ActualHeight, heights[i])).All(v => v),
            "measured WPF rows exactly fill the 740-DIP workspace");
        check(Near(Named<Grid>("StatusBar").ActualWidth, 888) && Near(Named<Border>("CoreCanvas").ActualWidth, 888),
            "status and canvas measure 888 DIPs with 16-DIP side margins");
        double chipWidth = chips[0].ActualWidth, chipHeight = chips[0].ActualHeight;
        var canvasPosition = Named<Border>("CoreCanvas").TranslatePoint(new Point(), root);
        for (int mode = 0; mode < 3; mode++)
        for (int slot = 0; slot < 3; slot++)
        for (int detail = 0; detail < 2; detail++)
        for (int numbers = 0; numbers < 2; numbers++)
        {
            Dock(mode);
            Named<Border>("SummarySlot").Visibility = slot == 2 ? Visibility.Collapsed : Visibility.Visible;
            Named<Button>("ElevateButton").Visibility = slot == 1 ? Visibility.Visible : Visibility.Collapsed;
            Named<Border>("ErrorBar").Visibility = slot == 2 ? Visibility.Visible : Visibility.Collapsed;
            Named<Border>("CoreDetail").Visibility = detail == 1 ? Visibility.Visible : Visibility.Collapsed;
            Named<Border>("CoreHint").Visibility = detail == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var chip in chips) chip.ShowNumbers(numbers == 1);
            Layout();
            check(Near(root.ActualWidth, 920) && Near(root.ActualHeight, 740) &&
                Near(Named<Border>("CoreCanvas").ActualHeight, 308) &&
                Near(Named<Grid>("DockBody").ActualHeight, 212) &&
                Near(Named<Grid>("CoreDetailSlot").ActualHeight, 60) &&
                Near(Named<Grid>("StatusBar").RowDefinitions[1].ActualHeight, 32) &&
                Named<Border>("CoreCanvas").TranslatePoint(new Point(), root) == canvasPosition &&
                Near(chips[0].ActualWidth, chipWidth) && Near(chips[0].ActualHeight, chipHeight),
                $"dock {mode}, status slot {slot}, detail {detail}, numbers {numbers}: workspace and reserved geometry remain stable");
        }
        var popup = Named<Popup>("TechnicalPopup");
        check(!popup.IsOpen && popup.Placement == PlacementMode.Bottom && popup.PlacementTarget == Named<Button>("TechnicalButton") && !popup.StaysOpen,
            "technical details are an anchored dismissible popup, not an in-flow panel (never opened by checks)");
        check(window.Descendants().Any(e => (string?)e.Attribute("Click") == "RefreshTopology_Click" && e.Ancestors(p + "Popup").Any()),
            "refresh topology action remains inside technical overlay");
        Layout(1120, 940);
        check(Named<Border>("CoreCanvas").ActualHeight > 308 && Named<Grid>("DockBody").ActualHeight > 212 &&
            Near(chips[0].ActualWidth, chipWidth) && Near(chips[0].ActualHeight, chipHeight),
            "enlarging workspace expands both canvas and dock while chip size stays fixed");
        Dock(1);
        Layout();
        var monitorSwitch = FindVisual<CheckBox>(Named<ScrollViewer>("ProgramsScroll"))!;
        var track = (Border)monitorSwitch.Template.FindName("Track", monitorSwitch);
        check(track.TranslatePoint(new Point(), monitorSwitch).X + track.ActualWidth <= monitorSwitch.ActualWidth + 0.1,
            "monitor switches with no label fit their 40-DIP column without clipping");
        for (int i = 0; i < 30; i++) vm.Targets.Add(new TargetRule { Name = $"fixture{i}.exe", Enabled = true });
        Layout();
        check(Named<ScrollViewer>("ProgramsScroll").ScrollableHeight > 0 && Near(Named<Grid>("DockBody").ActualHeight, 212),
            "long monitor lists scroll within the fixed dock");
        while (vm.Targets.Count > 2) vm.Targets.RemoveAt(vm.Targets.Count - 1);
        Dock(2);
        Layout();
        check(FindVisual<ScrollViewer>(Named<ListBox>("LogList")) is { ScrollableHeight: > 0 } && Near(Named<Grid>("DockBody").ActualHeight, 212),
            "250 log records scroll within the fixed dock");

        int outputArg = Array.IndexOf(args, "--layout-output");
        if (outputArg >= 0 && outputArg + 1 < args.Length)
        {
            string output = Path.GetFullPath(args[outputArg + 1]);
            Directory.CreateDirectory(output);
            Named<Border>("ErrorBar").Visibility = Visibility.Collapsed;
            Named<Border>("SummarySlot").Visibility = Visibility.Visible;
            Named<Button>("ElevateButton").Visibility = Visibility.Collapsed;
            Named<Border>("CoreDetail").Visibility = Visibility.Collapsed;
            Named<Border>("CoreHint").Visibility = Visibility.Visible;
            foreach (var chip in chips) chip.ShowNumbers(false);
            Named<Border>("SummarySlot").Background = new SolidColorBrush(Color.FromRgb(230, 244, 237));
            Named<TextBlock>("SummaryText").Foreground = Brushes.SeaGreen;
            Named<TextBlock>("StatusGlyph").Text = "✓";
            for (int mode = 0; mode < 3; mode++)
            {
                Dock(mode);
                Layout();
                var bitmap = new RenderTargetBitmap(920, 740, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, $"wpf-layout-{mode}.png"));
                png.Save(stream);
            }
            Console.WriteLine("OFFSCREEN_LAYOUT_PREVIEWS: " + output + " (synthetic data; no window shown)");
        }
    }

    private static void TextElementFont(Grid root)
    {
        System.Windows.Documents.TextElement.SetFontFamily(root, new FontFamily("Segoe UI Variable, Microsoft YaHei UI, Microsoft YaHei, Segoe UI"));
        System.Windows.Documents.TextElement.SetFontSize(root, 12);
    }

    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindVisual<T>(child) is T nested) return nested;
        }
        return null;
    }
}
