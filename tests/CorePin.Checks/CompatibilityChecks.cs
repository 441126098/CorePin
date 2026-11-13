using System.Text;
using System.Text.Json;
using CorePin.Models;
using CorePin.Services;

internal static class CompatibilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CorePin.Checks"));
        string root = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        if (!root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Temporary test path escaped its root.");
        Directory.CreateDirectory(root);
        try
        {
            string oldPath = Path.Combine(root, "LoLCorePin", "settings.json"); // Legacy fixture only.
            var store = new SettingsStore(root);
            check(store.Load(out string warning).AutoPin && warning.Length == 0 && !File.Exists(store.FilePath),
                "fresh profile uses defaults without writing settings");
            Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
            var saved = new AppSettings
            {
                AutoPin = false, UseManualSelection = true, RememberCloseToTray = true,
                TopologyFingerprint = "test-topology", SelectedCpuKeys = new() { "0:3" },
                Targets = new() { new TargetRule { Name = "中文游戏.exe", Enabled = true } }
            };
            string legacyJson = JsonSerializer.Serialize(saved);
            File.WriteAllText(oldPath, legacyJson, new UTF8Encoding(false));
            var migrated = store.Load(out warning);
            check(warning.Length == 0 && !migrated.AutoPin && migrated.UseManualSelection && migrated.RememberCloseToTray &&
                migrated.SelectedCpuKeys.SequenceEqual(saved.SelectedCpuKeys) && migrated.TopologyFingerprint == saved.TopologyFingerprint &&
                migrated.Targets.Single().Name == "中文游戏.exe" && File.Exists(store.FilePath),
                "legacy settings migrate to CorePin with custom rules and UI preferences intact");
            check(File.ReadAllText(oldPath) == legacyJson, "migration leaves legacy settings unchanged");
            migrated.AutoPin = true;
            store.Save(migrated);
            check(store.Load(out warning).AutoPin && warning.Length == 0,
                "existing CorePin settings take precedence over legacy settings");
            File.WriteAllText(store.FilePath, "{broken");
            check(!store.Load(out warning).AutoPin && warning.Length > 0 && File.ReadAllText(store.FilePath) == "{broken",
                "corrupt new settings pause binding without falling back or overwriting");
            File.Delete(store.FilePath);
            File.WriteAllText(oldPath, "{broken");
            check(!store.Load(out warning).AutoPin && warning.Length > 0 && !File.Exists(store.FilePath) && File.ReadAllText(oldPath) == "{broken",
                "corrupt legacy settings pause binding without creating a migrated file");
            File.WriteAllBytes(oldPath, new byte[] { 0xff, 0xfe, 0xff });
            check(!store.Load(out warning).AutoPin && warning.Length > 0 && !File.Exists(store.FilePath),
                "invalid encoded legacy settings fail safely");
            File.WriteAllText(oldPath, legacyJson);
            Directory.Delete(store.DirectoryPath);
            File.WriteAllText(store.DirectoryPath, "blocks directory creation");
            check(!store.Load(out warning).AutoPin && warning.Length > 0 && File.ReadAllText(oldPath) == legacyJson,
                "failed migration pauses binding and retains the original file");
        }
        finally { Directory.Delete(root, true); }

        const string executable = @"C:\CorePin Test\CorePin.exe";
        var entries = new MemoryEntries();
        entries.Values[StartupService.LegacyValueName] = @"C:\Legacy Tool\LoLCorePin.exe --tray"; // Legacy fixture only.
        entries.Values["UnrelatedApp"] = "keep";
        check(StartupService.IsEnabled(entries) && entries.Operations.Count == 0,
            "reading legacy autostart reports enabled without migrating it");
        StartupService.SetEnabled(entries, true, executable);
        check(entries.Values[StartupService.ValueName] == "\"" + executable + "\" --tray" &&
            !entries.Values.ContainsKey(StartupService.LegacyValueName) && entries.MaximumToolEntries == 1,
            "explicit enable migrates autostart without scheduling two programs");
        check(entries.Values["UnrelatedApp"] == "keep", "autostart migration preserves unrelated entries");
        entries.Values[StartupService.LegacyValueName] = "legacy";
        StartupService.SetEnabled(entries, false, executable);
        check(!StartupService.IsEnabled(entries) && entries.Values.Count == 1, "explicit disable removes both tool entries");
        entries.Values[StartupService.LegacyValueName] = "legacy";
        entries.FailWrite = true;
        try { StartupService.SetEnabled(entries, true, executable); }
        catch (IOException) { }
        check(!StartupService.IsEnabled(entries), "failed new autostart write leaves no duplicate or stale legacy launch");
        entries.Values[StartupService.LegacyValueName] = "legacy";
        entries.FailDelete = true;
        entries.FailWrite = false;
        try { StartupService.SetEnabled(entries, true, executable); }
        catch (IOException) { }
        check(!entries.Values.ContainsKey(StartupService.ValueName), "failed legacy removal never schedules the new program");
        entries.FailDelete = false;
        int operations = entries.Operations.Count;
        try { StartupService.SetEnabled(entries, true, @"C:\dotnet\dotnet.exe"); }
        catch (InvalidOperationException) { }
        check(entries.Operations.Count == operations && entries.Values.ContainsKey(StartupService.LegacyValueName),
            "invalid host path is rejected before any autostart mutation");
    }

    private sealed class MemoryEntries : IStartupEntries
    {
        internal Dictionary<string, string> Values { get; } = new();
        internal List<string> Operations { get; } = new();
        internal int MaximumToolEntries;
        internal bool FailWrite, FailDelete;
        public string? Read(string name) => Values.GetValueOrDefault(name);
        public void Write(string name, string command)
        {
            if (FailWrite) throw new IOException("Simulated write failure");
            Values[name] = command;
            Record("write " + name);
        }
        public void Delete(string name)
        {
            if (FailDelete) throw new IOException("Simulated delete failure");
            Values.Remove(name);
            Record("delete " + name);
        }
        private void Record(string operation)
        {
            Operations.Add(operation);
            MaximumToolEntries = Math.Max(MaximumToolEntries, Values.Keys.Count(k => k is StartupService.ValueName or StartupService.LegacyValueName));
        }
    }
}
