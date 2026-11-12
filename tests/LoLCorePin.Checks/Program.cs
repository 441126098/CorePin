using System.Diagnostics;
using System.Runtime.InteropServices;
using LoLCorePin.Models;
using LoLCorePin.Native;
using LoLCorePin.Services;

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
void Throws(Action action, string name)
{
    bool threw = false;
    try { action(); } catch (ArgumentException) { threw = true; } catch (InvalidOperationException) { threw = true; }
    Check(threw, name);
}
byte[] Entry(uint id, ushort group, byte logical, byte core, byte efficiency, int size = 32, uint type = 0)
{
    var data = new byte[size];
    BitConverter.GetBytes((uint)size).CopyTo(data, 0);
    BitConverter.GetBytes(type).CopyTo(data, 4);
    if (size >= 20)
    {
        BitConverter.GetBytes(id).CopyTo(data, 8);
        BitConverter.GetBytes(group).CopyTo(data, 12);
        data[14] = logical; data[15] = core; data[18] = efficiency;
    }
    return data;
}

try
{
    var bytes = Entry(100, 0, 0, 0, 8).Concat(Entry(101, 0, 1, 0, 8, 40))
        .Concat(Entry(102, 0, 2, 1, 4)).Concat(Entry(103, 0, 3, 2, 0)).ToArray();
    var topology = new CpuTopology(CpuTopologyService.Parse(bytes), 1);
    Check(topology.IsHybrid && topology.HighestClass == 8, "three-class topology chooses highest class");
    Check(topology.Recommended.Select(c => c.Id).SequenceEqual(new uint[] { 100, 101 }), "both SMT siblings retained; middle and lowest excluded");
    Check(topology.Cpus[1].CoreIndex == 0 && topology.Cpus.Count == 4, "variable-sized records preserve offsets");
    var homogeneous = new CpuTopology(topology.Cpus.Select(c => c with { EfficiencyClass = 0 }).ToArray(), 1);
    Check(!homogeneous.IsHybrid && homogeneous.Recommended.Count == 4, "non-hybrid falls back to all processors");
    Check(CpuTopology.BuildMask(topology.Recommended) == 3UL, "single-group mask");
    Check(CpuTopology.BuildMask(new[] { new CpuInfo(1, 0, 63, 1, 0, 0) }) == 0x8000000000000000UL, "mask bit 63 has no overflow or sign extension");
    Throws(() => CpuTopology.BuildMask(Array.Empty<CpuInfo>()), "empty mask rejected");
    Throws(() => CpuTopology.BuildMask(new[] { topology.Cpus[0], topology.Cpus[1] with { Group = 1 } }), "cross-group mask rejected");
    Throws(() => CpuTopology.BuildMask(new[] { new CpuInfo(1, 0, 64, 1, 0, 0) }), "invalid shift rejected");
    Throws(() => CpuTopologyService.Parse(bytes[..^1]), "truncated record rejected");
    Throws(() => CpuTopologyService.Parse(new byte[8]), "zero record size rejected");
    Throws(() => CpuTopologyService.Parse(Entry(1, 0, 0, 0, 0, 16)), "undersized CPU Set rejected");
    Check(CpuTopologyService.Parse(Entry(1, 0, 0, 0, 0, 8, 99).Concat(bytes).ToArray()).Count == 4, "unknown future record types skipped");
    Throws(() => new CpuTopology(Array.Empty<CpuInfo>(), 1), "missing topology rejected");
    Check(TargetRule.Normalize(" League of Legends ") == "League of Legends.exe", "target normalization preserves spaces");
    Check(TargetRule.EnabledNames(TargetRule.Defaults()).SetEquals(new[] { "LEAGUE OF LEGENDS.EXE" }), "only game enabled by default; case insensitive");
    Throws(() => TargetRule.Normalize(@"D:\game\League.exe"), "paths rejected");
    Throws(() => TargetRule.Normalize("League*.exe"), "wildcard targeting rejected");
    Check(new CpuInfo(1, 0, 1, 0, 0, 1).IsParked, "parked flag decoded");
    Check(new CpuInfo(1, 0, 1, 0, 0, 2).IsAllocated, "reservation flag decoded");

    if (args.Contains("--native", StringComparer.OrdinalIgnoreCase))
    {
        Check(Environment.Is64BitProcess, "native checks run as x64");
        var actual = new CpuTopologyService().Read();
        Check(actual.Cpus.Count > 0, "live CPU topology enumeration");
        var self = ProcessCatalog.Enumerate().Single(p => p.Id == (uint)Environment.ProcessId);
        using var handle = NativeMethods.OpenProcess(NativeMethods.QueryLimitedInformation | NativeMethods.SetInformation | NativeMethods.SetLimitedInformation, false, self.Id);
        Check(!handle.IsInvalid, "open this test process only");
        uint[] originalIds = ProcessTuner.ReadCpuSets(handle);
        NativeMethods.GetProcessAffinityMask(handle, out var originalMask, out _);
        var originalPower = new NativeMethods.PowerThrottlingState { Version = 1 };
        bool hasPower = NativeMethods.GetProcessInformation(handle, NativeMethods.ProcessPowerThrottling, ref originalPower, 12);
        // Avoid CPU Sets reserved for a different process. Do not tune any game/system process.
        var forSelf = new CpuTopologyService().ReadForProcess(handle.DangerousGetHandle());
        var selected = forSelf.Recommended.Where(c => !c.IsAllocated || (c.Flags & 4) != 0).Take(1).ToArray();
        Check(selected.Length == 1, "one usable recommended processor for self-test");
        ProcessWatcher? watcher = null;
        try
        {
            var tuner = new ProcessTuner();
            var result = tuner.Inspect(self, actual, selected, true);
            Check(result.IsCompliant, "self affinity / CPU Sets / QoS set and verified: " + result.Detail);
            var unchanged = tuner.Inspect(self, actual, selected, true);
            Check(unchanged.IsCompliant && !unchanged.Changed, "compliant state avoids redundant writes");
            var wrongIdentity = tuner.Inspect(self with { Name = "different.exe" }, actual, selected, true);
            Check(!wrongIdentity.IsCompliant && !wrongIdentity.Changed, "process identity mismatch prevents writes");
            using var repaired = new ManualResetEventSlim();
            watcher = new ProcessWatcher(new WatchConfiguration(actual, selected, new[] { self.Name }, true), self.Id);
            watcher.Updated += snapshot => { if (snapshot.Processes.Any(p => p.Id == self.Id && p.Changed && p.IsCompliant)) repaired.Set(); };
            watcher.Start();
            NativeMethods.SetProcessDefaultCpuSets(handle, Array.Empty<uint>(), 0);
            if (actual.GroupCount == 1) NativeMethods.SetProcessAffinityMask(handle, originalMask);
            Check(repaired.Wait(TimeSpan.FromSeconds(5)), "500ms watcher repairs deliberately reset settings on this process");
        }
        finally
        {
            if (watcher != null) { watcher.Dispose(); watcher.Completion.GetAwaiter().GetResult(); }
            if (actual.GroupCount == 1 && originalMask != UIntPtr.Zero) NativeMethods.SetProcessAffinityMask(handle, originalMask);
            NativeMethods.SetProcessDefaultCpuSets(handle, originalIds, (uint)originalIds.Length);
            if (hasPower) NativeMethods.SetProcessInformation(handle, NativeMethods.ProcessPowerThrottling, ref originalPower, 12);
        }
    }
    Console.WriteLine($"{passed} checks passed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
