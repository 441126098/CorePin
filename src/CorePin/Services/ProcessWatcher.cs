using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CorePin.Models;

namespace CorePin.Services;

public sealed record WatchConfiguration(CpuTopology Topology, CpuInfo[] Selected, string[] Names, bool AutoPin);
public sealed record WatchSnapshot(DateTime Time, ProcessReport[] Processes, int GameProcessCount, string Error, bool AppliedManually);

public sealed class ProcessWatcher : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _gate = new();
    private readonly uint? _onlyProcessId;
    private WatchConfiguration _configuration;
    private Task? _worker;
    private bool _manualRequested;
    private int _disposed;
    public event Action<WatchSnapshot>? Updated;
    public Task Completion => _worker ?? Task.CompletedTask;

    public ProcessWatcher(WatchConfiguration configuration) => _configuration = configuration;
    // Native checks can restrict the real polling engine to their own PID.
    internal ProcessWatcher(WatchConfiguration configuration, uint onlyProcessId) : this(configuration) => _onlyProcessId = onlyProcessId;
    public void Start() => _worker ??= Task.Run(RunAsync);

    public void Configure(WatchConfiguration configuration)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_gate) _configuration = configuration;
        Wake();
    }

    public void ApplyNow()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_gate) _manualRequested = true;
        Wake();
    }

    private void Wake()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
    }

    private async Task RunAsync()
    {
        var tuner = new ProcessTuner();
        while (!_stop.IsCancellationRequested)
        {
            WatchConfiguration config;
            bool manual;
            lock (_gate)
            {
                config = _configuration;
                manual = _manualRequested;
                _manualRequested = false;
            }
            try
            {
                var names = new HashSet<string>(config.Names, StringComparer.OrdinalIgnoreCase);
                var all = ProcessCatalog.Enumerate();
                var targets = all.Where(p => names.Contains(p.Name) && (_onlyProcessId == null || p.Id == _onlyProcessId));
                var results = targets.Select(p => tuner.Inspect(p, config.Topology, config.Selected, config.AutoPin || manual)).ToArray();
                Updated?.Invoke(new WatchSnapshot(DateTime.Now, results,
                    all.Count(p => p.Name.Equals("League of Legends.exe", StringComparison.OrdinalIgnoreCase)), "", manual));
            }
            catch (Exception ex) { Updated?.Invoke(new WatchSnapshot(DateTime.Now, Array.Empty<ProcessReport>(), 0, ErrorText.Describe(ex), manual)); }
            try { await _wake.WaitAsync(500, _stop.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        // The worker holds no persistent process handles. Avoid blocking the UI at exit.
        // Synchronization primitives stay alive until the already-running worker finishes.
        if (_worker != null) _ = _worker.ContinueWith(_ => { _wake.Dispose(); _stop.Dispose(); }, TaskScheduler.Default);
        else { _wake.Dispose(); _stop.Dispose(); }
    }
}
