using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CorePin.Models;

namespace CorePin.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CpuRow : ObservableObject
{
    public CpuInfo Cpu { get; }
    public bool IsPerformance { get; }
    private bool _selected;
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Notify(); } }
    public CpuRow(CpuInfo cpu, bool selected, bool isPerformance) { Cpu = cpu; _selected = selected; IsPerformance = isPerformance; }
}

public sealed record LogEntry(string Time, string Message);

public sealed class MainViewModel : ObservableObject
{
    public ObservableCollection<CpuRow> Cpus { get; } = new();
    public ObservableCollection<TargetRule> Targets { get; } = new();
    public ObservableCollection<ProcessReport> Processes { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();
    private string _status = "正在读取处理器…", _policy = "", _summary = "", _running = "未运行";
    public string Status { get => _status; set { if (_status == value) return; _status = value; Notify(); } }
    public string Policy { get => _policy; set { if (_policy == value) return; _policy = value; Notify(); } }
    public string Summary { get => _summary; set { if (_summary == value) return; _summary = value; Notify(); } }
    public string Running { get => _running; set { if (_running == value) return; _running = value; Notify(); } }
}
