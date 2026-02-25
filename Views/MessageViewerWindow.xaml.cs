using System.Windows;
using PmLiteMonitor.ViewModels;

namespace PmLiteMonitor.Views;

public partial class MessageViewerWindow : Window
{
    public MessageViewerViewModel Vm { get; } = new();

    public MessageViewerWindow()
    {
        InitializeComponent();
        DataContext = Vm;
    }

    /// <summary>Open with a file pre-loaded (called from MainWindow menu).</summary>
    public MessageViewerWindow(string filePath) : this()
    {
        Vm.LoadFile(filePath);
    }
}
