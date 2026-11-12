using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LoLCorePin.Services;

namespace LoLCorePin;

public partial class ProcessPickerWindow : Window
{
    private IReadOnlyList<ProcessItem> _items = Array.Empty<ProcessItem>();
    public string? SelectedProcessName { get; private set; }
    public ProcessPickerWindow() { InitializeComponent(); Refresh(); }

    private void Refresh()
    {
        try { _items = ProcessCatalog.Enumerate(); Filter(); }
        catch (Exception ex) { StatusText.Text = ErrorText.Describe(ex); }
    }
    private void Filter()
    {
        if (ProcessGrid == null) return;
        string search = SearchBox.Text.Trim();
        var items = _items.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Id.ToString().Contains(search) || p.ParentId.ToString().Contains(search))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Id).ToArray();
        ProcessGrid.ItemsSource = items;
        StatusText.Text = $"{items.Length} 个进程；双击可选择。";
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Search_Changed(object sender, TextChangedEventArgs e) => Filter();
    private void Choose_Click(object sender, RoutedEventArgs e) => Choose();
    private void Process_DoubleClick(object sender, MouseButtonEventArgs e) => Choose();
    private void Choose()
    {
        if (ProcessGrid.SelectedItem is not ProcessItem item) { StatusText.Text = "请先选中一个进程。"; return; }
        SelectedProcessName = item.Name;
        DialogResult = true;
    }
}
