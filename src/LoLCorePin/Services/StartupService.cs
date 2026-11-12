using System;
using System.IO;
using Microsoft.Win32;

namespace LoLCorePin.Services;

public static class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LoLCorePin";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is string value && string.Equals(value, Command(), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
            ?? throw new InvalidOperationException("无法打开当前用户的登录启动注册表项。");
        if (enabled) key.SetValue(ValueName, Command(), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }

    private static string Command()
    {
        string path = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定应用路径。");
        if (!string.Equals(Path.GetFileNameWithoutExtension(path), "LoLCorePin", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请从 LoLCorePin.exe 启动后再设置开机启动。");
        return $"\"{path}\" --tray";
    }
}
