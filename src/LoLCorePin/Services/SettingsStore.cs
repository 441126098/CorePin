using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using LoLCorePin.Models;

namespace LoLCorePin.Services;

public sealed class AppSettings
{
    public bool AutoPin { get; set; } = true;
    public bool UseManualSelection { get; set; }
    public string TopologyFingerprint { get; set; } = "";
    public List<string> SelectedCpuKeys { get; set; } = new();
    public List<TargetRule> Targets { get; set; } = TargetRule.Defaults();
}

public sealed class SettingsStore
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoLCorePin");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public AppSettings Load(out string warning)
    {
        warning = "";
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? throw new InvalidDataException("设置为空。");
            if (settings.Targets == null || settings.SelectedCpuKeys == null) throw new InvalidDataException("设置列表为空。");
            foreach (var rule in settings.Targets) rule.Name = TargetRule.Normalize(rule.Name);
            settings.Targets = settings.Targets.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            return settings;
        }
        catch (Exception ex)
        {
            warning = "设置读取失败，已使用默认值并暂停自动绑定；原文件保留，保存时才覆盖。" + ErrorText.Describe(ex);
            return new AppSettings { AutoPin = false };
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
