using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace LoLCorePin;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            _instance = new Mutex(true, @"Local\LoLCorePin.Desktop.v1", out _ownsMutex);
            if (!_ownsMutex)
            {
                MessageBox.Show("LoLCorePin 已在运行，请从通知区域的托盘图标打开。", "LoLCorePin", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            var window = new MainWindow();
            MainWindow = window;
            if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败：" + Services.ErrorText.Describe(ex), "LoLCorePin", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("程序发生错误，将退出以避免继续应用未知状态。\n" + Services.ErrorText.Describe(e.Exception),
            "LoLCorePin", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window) window.DisposeResources();
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
