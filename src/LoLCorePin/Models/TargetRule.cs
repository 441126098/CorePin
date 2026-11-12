using System;
using System.Collections.Generic;
using System.Linq;

namespace LoLCorePin.Models;

public sealed class TargetRule
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

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
