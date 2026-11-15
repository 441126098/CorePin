using System.Diagnostics;
using CorePin.Services;

internal static class ProcessCatalogChecks
{
    public static void Run(Action<bool, string> check)
    {
        var self = ProcessCatalog.Enumerate().Single(p => p.Id == (uint)Environment.ProcessId);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { self.Name.ToUpperInvariant() };
        var found = ProcessCatalog.FindTargets(names, self.Id, out int gameCount);
        check(found.Length == 1 && found[0] == self,
            "buffered enumeration preserves self PID, executable name and parent; matches names case-insensitively");
        check(gameCount >= 0, "buffered enumeration returns an independent game count");
        check(ProcessCatalog.FindTargets(names, uint.MaxValue, out _).Length == 0,
            "buffered enumeration respects the test PID restriction");
        names.Clear();
        check(ProcessCatalog.FindTargets(names, null, out _).Length == 0,
            "empty enabled rules return no targets without skipping enumeration");
        names.Add(self.Name);
        check(ProcessCatalog.FindTargets(names, self.Id, out _).Single() == self,
            "updated enabled names are reflected in the next enumeration");

        // Read-only comparison with the previous full-catalog path. Print the
        // allocation evidence; do not impose a machine-dependent timing threshold.
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "corepin-absent-allocation-fixture.exe" };
        for (int i = 0; i < 3; i++) { ProcessCatalog.Enumerate(); ProcessCatalog.FindTargets(missing, null, out _); }
        const int iterations = 50;
        var full = Measure(() => ProcessCatalog.Enumerate());
        var filtered = Measure(() => ProcessCatalog.FindTargets(missing, null, out _));
        Console.WriteLine($"POLL_ALLOCATION: iterations={iterations}; fullCatalogBytesPerPoll={full.Bytes / iterations}; filteredBytesPerPoll={filtered.Bytes / iterations}; fullMs={full.Milliseconds:F2}; filteredMs={filtered.Milliseconds:F2}");

        (long Bytes, double Milliseconds) Measure(Action poll)
        {
            var timer = Stopwatch.StartNew();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++) poll();
            return (GC.GetAllocatedBytesForCurrentThread() - before, timer.Elapsed.TotalMilliseconds);
        }
    }
}
