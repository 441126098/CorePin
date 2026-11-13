using System;
using System.Windows;
using System.Windows.Controls;
using CorePin.ViewModels;

namespace CorePin.Controls;

public partial class CoreChip : UserControl
{
    public event Action<CpuRow>? Inspected;
    public CoreChip(CpuRow row, bool numbers)
    {
        InitializeComponent();
        DataContext = row;
        ShowNumbers(numbers);
        ToolTip = $"物理核心 {row.Cpu.CoreIndex} · {row.Cpu.LogicalLabel} · {row.Cpu.Availability}";
    }
    public void ShowNumbers(bool show) => Number.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    private void Chip_Click(object sender, RoutedEventArgs e) => Inspected?.Invoke((CpuRow)DataContext);
}
