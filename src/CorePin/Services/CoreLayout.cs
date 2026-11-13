using System.Collections.Generic;
using System.Linq;
using CorePin.Models;

namespace CorePin.Services;

public sealed record PhysicalCore(ushort Group, byte Index, bool IsPerformance, IReadOnlyList<CpuInfo> Threads);

public static class CoreLayout
{
    // Never join SMT siblings across processor groups, even when core indices match.
    public static IReadOnlyList<PhysicalCore> Group(CpuTopology topology) => topology.Cpus
        .GroupBy(c => new { c.Group, c.CoreIndex, Performance = c.EfficiencyClass == topology.HighestClass })
        .Select(g => new PhysicalCore(g.Key.Group, g.Key.CoreIndex, g.Key.Performance, g.OrderBy(c => c.LogicalIndex).ToArray()))
        .OrderByDescending(c => c.IsPerformance).ThenBy(c => c.Group).ThenBy(c => c.Index).ToArray();
}
