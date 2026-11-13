using System;
using System.IO;
using Microsoft.Win32;

namespace CorePin.Services;

// Tests use an in-memory implementation; no test opens the real Run key.
internal interface IStartupEntries
{
    string? Read(string name);
    void Write(string name, string command);
    void Delete(string name);
}

public static class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "CorePin";
    internal const string LegacyValueName = "LoLCorePin"; // Legacy autostart compatibility.

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key != null && IsEnabled(new RunKeyEntries(key));
    }

    internal static bool IsEnabled(IStartupEntries entries) =>
        !string.IsNullOrWhiteSpace(entries.Read(ValueName)) || !string.IsNullOrWhiteSpace(entries.Read(LegacyValueName));

    public static void SetEnabled(bool enabled)
    {
        string path = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定应用路径。");
        // Validate before opening a writable key, so a bad launch path cannot remove an existing entry.
        if (enabled) _ = Command(path);
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
            ?? throw new InvalidOperationException("无法打开当前用户的登录启动注册表项。");
        SetEnabled(new RunKeyEntries(key), enabled, path);
    }

    internal static void SetEnabled(IStartupEntries entries, bool enabled, string executablePath)
    {
        string? command = enabled ? Command(executablePath) : null;
        // Only explicit checkbox operations reach here. Remove the old entry first:
        // a failed new write may leave autostart off, but never adds a second program.
        entries.Delete(LegacyValueName);
        if (command != null) entries.Write(ValueName, command);
        else entries.Delete(ValueName);
    }

    private static string Command(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFileName(path), "CorePin.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请从 CorePin.exe 启动后再设置开机启动。");
        return $"\"{path}\" --tray";
    }

    private sealed class RunKeyEntries(RegistryKey key) : IStartupEntries
    {
        public string? Read(string name) => key.GetValue(name) as string;
        public void Write(string name, string command) => key.SetValue(name, command, RegistryValueKind.String);
        public void Delete(string name) => key.DeleteValue(name, false);
    }
}
