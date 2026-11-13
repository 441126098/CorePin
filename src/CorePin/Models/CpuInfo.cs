using System;
using System.Collections.Generic;
using System.Linq;

namespace CorePin.Models;

public sealed record CpuInfo(uint Id, ushort Group, byte LogicalIndex, byte CoreIndex,
    byte EfficiencyClass, byte Flags)
{
    // CPU Set IDs may change after a reboot. Persist group/index pairs instead.
    public string Key => $"{Group}:{LogicalIndex}";
    public string LogicalLabel => $"G{Group} / CPU {LogicalIndex}";
    public bool IsParked => (Flags & 1) != 0;
    public bool IsAllocated => (Flags & 2) != 0;
    public string Availability => IsAllocated ? "系统预留，应用时检查目标权限" : IsParked ? "已停驻（可唤醒）" : "可用";
}

public sealed class CpuTopology
{
    public IReadOnlyList<CpuInfo> Cpus { get; }
    public ushort GroupCount { get; }
    public byte HighestClass { get; }
    public bool IsHybrid { get; }
    public IReadOnlyList<CpuInfo> Recommended { get; }
    public string Fingerprint => string.Join(";", Cpus.Select(c => $"{c.Key}/{c.CoreIndex}/{c.EfficiencyClass}"));
    public string Summary => IsHybrid
        ? $"检测到 {Cpus.Select(c => c.EfficiencyClass).Distinct().Count()} 个性能等级 · 最高 EfficiencyClass {HighestClass} · {Recommended.Count} 个性能核逻辑处理器（含 SMT）"
        : $"未报告 P/E 差异（EfficiencyClass 均为 {HighestClass}），默认使用全部 {Cpus.Count} 个逻辑处理器。可手动选择。";

    public CpuTopology(IReadOnlyList<CpuInfo> cpus, ushort groupCount)
    {
        if (cpus.Count == 0) throw new InvalidOperationException("Windows 没有返回可用的 CPU Set，已停止自动绑定。");
        Cpus = cpus.OrderBy(c => c.Group).ThenBy(c => c.LogicalIndex).ToArray();
        GroupCount = groupCount;
        HighestClass = Cpus.Max(c => c.EfficiencyClass);
        IsHybrid = Cpus.Select(c => c.EfficiencyClass).Distinct().Skip(1).Any();
        // Evaluate all logical processors. Do not discard a sibling SMT thread.
        Recommended = Cpus.Where(c => c.EfficiencyClass == HighestClass).ToArray();
    }

    public static ulong BuildMask(IEnumerable<CpuInfo> cpus)
    {
        var selected = cpus.ToArray();
        if (selected.Length == 0 || selected.Select(c => c.Group).Distinct().Count() != 1)
            throw new ArgumentException("亲和性掩码必须包含同一处理器组的至少一个逻辑处理器。");
        ulong mask = 0;
        foreach (var cpu in selected)
        {
            if (cpu.LogicalIndex >= 64) throw new ArgumentOutOfRangeException(nameof(cpus));
            mask |= 1UL << cpu.LogicalIndex;
        }
        return mask;
    }
}
