using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;

namespace CorePin.Models;

public sealed class TargetRule : INotifyPropertyChanged
{
    private string _name = "";
    private bool _enabled = true;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get => _name; set { _name = value; Changed(nameof(Name)); Changed(nameof(DisplayName)); Changed(nameof(Note)); } }
    public bool Enabled { get => _enabled; set { _enabled = value; Changed(nameof(Enabled)); Changed(nameof(Note)); } }
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => Name.Equals("League of Legends.exe", StringComparison.OrdinalIgnoreCase) ? "游戏" :
        Name.StartsWith("LeagueClient", StringComparison.OrdinalIgnoreCase) ? "客户端（默认不监视）" : "自定义进程";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Note => Enabled ? "启用后按当前核心策略绑定" : "不会绑定到性能核";
    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public static string Normalize(string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\r', '\n' }) >= 0)
            throw new ArgumentException("请输入精确的进程文件名，例如 League of Legends.exe；不支持路径或通配符。");
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        return name;
    }

    public static List<TargetRule> Defaults() => new()
    {
        new() { Name = "League of Legends.exe", Enabled = true },
        new() { Name = "LeagueClient.exe", Enabled = false },
        new() { Name = "LeagueClientUx.exe", Enabled = false },
        new() { Name = "LeagueClientUxRender.exe", Enabled = false }
    };

    public static HashSet<string> EnabledNames(IEnumerable<TargetRule> rules) =>
        rules.Where(r => r.Enabled).Select(r => Normalize(r.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
