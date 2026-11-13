using System.Windows;

namespace CorePin;

public partial class CloseToTrayWindow : Window
{
    public bool ExitRequested { get; private set; }
    public bool Remember => RememberBox.IsChecked == true;
    public CloseToTrayWindow() => InitializeComponent();
    private void Exit_Click(object sender, RoutedEventArgs e) { ExitRequested = true; DialogResult = true; }
    private void Tray_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
