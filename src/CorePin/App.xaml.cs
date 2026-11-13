using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace CorePin;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Headless release verification exits before the mutex, profile, registry, tray or watcher.
        if (e.Args.Length > 0 && e.Args[0].Equals("--package-check", StringComparison.OrdinalIgnoreCase))
        {
            Shutdown(Services.PackageCheck.Run(e.Args));
            return;
        }
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            // Deliberately shared with the legacy app: two versions must never tune concurrently.
            try { _instance = new Mutex(false, @"Local\LoLCorePin.Desktop.v1"); }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("CorePin 或旧版 LoLCorePin 已在另一权限级别运行，请从托盘打开已有实例；切换版本前请从托盘退出旧版。", "CorePin", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            // runas returns to the old instance after UAC acceptance; that instance then
            // exits and releases this mutex. The elevated child waits for that handoff.
            bool handoff = e.Args.Contains("--elevated-restart", StringComparer.OrdinalIgnoreCase);
            try { _ownsMutex = _instance.WaitOne(handoff ? TimeSpan.FromSeconds(10) : TimeSpan.Zero); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
            if (!_ownsMutex)
            {
                MessageBox.Show("CorePin 或旧版 LoLCorePin 已在运行，请从托盘打开已有实例；切换版本前请从托盘退出旧版。", "CorePin", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            var window = new MainWindow();
            MainWindow = window;
            if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase)) window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败：" + Services.ErrorText.Describe(ex), "CorePin", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("程序发生错误，将退出以避免继续应用未知状态。\n" + Services.ErrorText.Describe(e.Exception),
            "CorePin", MessageBoxButton.OK, MessageBoxImage.Error);
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
