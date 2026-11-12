using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LoLCorePin.Models;

namespace LoLCorePin.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CpuRow : ObservableObject
{
    public CpuInfo Cpu { get; }
    public string Kind { get; }
    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value; Notify(); } }
    public CpuRow(CpuInfo cpu, bool selected, string kind) { Cpu = cpu; _selected = selected; Kind = kind; }
}

public sealed class MainViewModel : ObservableObject
{
    public ObservableCollection<CpuRow> Cpus { get; } = new();
    public ObservableCollection<TargetRule> Targets { get; } = new();
    public ObservableCollection<ProcessReport> Processes { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();
    private string _topology = "正在读取 CPU 拓扑…", _status = "正在启动监控…", _policy = "", _lastCheck = "", _notice = "", _selectedSummary = "";
    private bool _manualMode;
    public string TopologyText { get => _topology; set { _topology = value; Notify(); } }
    public string Status { get => _status; set { _status = value; Notify(); } }
    public string Policy { get => _policy; set { _policy = value; Notify(); } }
    public string LastCheck { get => _lastCheck; set { _lastCheck = value; Notify(); } }
    public string Notice { get => _notice; set { _notice = value; Notify(); } }
    public string SelectedSummary { get => _selectedSummary; set { _selectedSummary = value; Notify(); } }
    public bool ManualMode { get => _manualMode; set { _manualMode = value; Notify(); } }
}
