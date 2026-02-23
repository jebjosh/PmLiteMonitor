using System.Windows;
using PmLiteMonitor.ViewModels;
using PmLiteMonitor.Views;

namespace PmLiteMonitor.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private ConfigWindow? _configWindow;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Auto-scroll log to bottom whenever text changes
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.EventLog))
                Dispatcher.BeginInvoke(() => LogScrollViewer.ScrollToBottom());
        };
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        if (_configWindow is { IsVisible: true })
        {
            _configWindow.Activate();
            return;
        }

        var configVm = new ConfigViewModel(_vm.Client, _vm);
        _configWindow = new ConfigWindow { DataContext = configVm, Owner = this };
        _configWindow.Show();
    }
}
