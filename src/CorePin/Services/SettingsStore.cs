using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CorePin.Models;

namespace CorePin.Services;

public sealed class AppSettings
{
    public bool AutoPin { get; set; } = true;
    public bool UseManualSelection { get; set; }
    public bool RememberCloseToTray { get; set; }
    public string TopologyFingerprint { get; set; } = "";
    public List<string> SelectedCpuKeys { get; set; } = new();
    public List<TargetRule> Targets { get; set; } = TargetRule.Defaults();
}

public sealed class SettingsStore
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    private string LegacyFilePath { get; }

    // Supplying a root keeps migration tests entirely outside the real user profile.
    public SettingsStore(string? localApplicationData = null)
    {
        string root = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DirectoryPath = Path.Combine(root, "CorePin");
        LegacyFilePath = Path.Combine(root, "LoLCorePin", "settings.json"); // Legacy settings compatibility.
    }

    public AppSettings Load(out string warning)
    {
        warning = "";
        try
        {
            bool migrate = !File.Exists(FilePath) && File.Exists(LegacyFilePath);
            string source = migrate ? LegacyFilePath : FilePath;
            if (!File.Exists(source)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(source, Utf8)) ?? throw new InvalidDataException("设置为空。");
            if (settings.Targets == null || settings.SelectedCpuKeys == null) throw new InvalidDataException("设置列表为空。");
            foreach (var rule in settings.Targets) rule.Name = TargetRule.Normalize(rule.Name);
            settings.Targets = settings.Targets.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            // Keep the legacy file intact. Never replace an existing new location during migration.
            if (migrate) Save(settings, false);
            return settings;
        }
        catch (Exception ex)
        {
            warning = "设置读取失败，已使用默认值并暂停自动绑定；原文件保留，保存时才覆盖。" + ErrorText.Describe(ex);
            return new AppSettings { AutoPin = false };
        }
    }

    public void Save(AppSettings settings) => Save(settings, true);

    private void Save(AppSettings settings, bool overwrite)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), Utf8);
            File.Move(temporary, FilePath, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
