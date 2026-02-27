using System.Windows;

namespace PmLiteMonitor.Views;

public partial class PathSettingsWindow : Window
{
    public PathSettingsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
