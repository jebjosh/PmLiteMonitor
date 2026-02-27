using System.Windows;
using PmLiteMonitor.ViewModels;
using PmLiteMonitor.Views;

// Prevent Windows.Forms conflicts
using Window = System.Windows.Window;
using RoutedEventArgs = System.Windows.RoutedEventArgs;

namespace PmLiteMonitor.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel    _vm;
    private readonly ConfigViewModel  _configVm;   // created once, survives window close
    private ConfigWindow?        _configWindow;
    private AboutWindow?         _aboutWindow;
    private MessageViewerWindow? _viewerWindow;
    private PathSettingsWindow?  _pathSettingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        _vm       = new MainViewModel();
        _configVm = new ConfigViewModel(_vm.Client, _vm);  // single instance
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

        // Reuse the same ConfigViewModel — all field values are preserved
        _configWindow = new ConfigWindow { DataContext = _configVm, Owner = this };
        _configWindow.Show();
    }

    private void OpenMessageViewer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewerWindow is { IsVisible: true })
        {
            _viewerWindow.Activate();
            return;
        }
        // Each open creates a fresh viewer — user can have multiple instances
        _viewerWindow = new MessageViewerWindow { Owner = this };
        _viewerWindow.Show();
    }

    private void OpenPathSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_pathSettingsWindow is { IsVisible: true })
        {
            _pathSettingsWindow.Activate();
            return;
        }
        // Shares the same DataContext as MainWindow so all bindings work automatically
        _pathSettingsWindow = new PathSettingsWindow { DataContext = _vm, Owner = this };
        _pathSettingsWindow.Show();
    }

    private void OpenAbout_Click(object sender, RoutedEventArgs e)
    {
        if (_aboutWindow is { IsVisible: true })
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow { DataContext = _vm.AboutVm, Owner = this };
        _aboutWindow.Show();
    }
}
