using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

// Explicitly use WPF UserControl to avoid conflict with Windows.Forms.UserControl
using UserControl = System.Windows.Controls.UserControl;
using WpfColor    = System.Windows.Media.Color;

namespace PmLiteMonitor.Controls;

public enum IconState { Gray, Red, Green, Blue }

public partial class StatusIcon : UserControl
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(IconState), typeof(StatusIcon),
            new PropertyMetadata(IconState.Gray, OnStateChanged));

    public IconState State
    {
        get => (IconState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public StatusIcon() => InitializeComponent();

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusIcon icon) icon.UpdateVisual();
    }

    private void UpdateVisual()
    {
        FillRect.Fill = State switch
        {
            IconState.Red   => new SolidColorBrush(WpfColor.FromRgb(200, 40,  40)),
            IconState.Green => new SolidColorBrush(WpfColor.FromRgb(40,  190, 70)),
            IconState.Blue  => new SolidColorBrush(WpfColor.FromRgb(40,  110, 210)),
            _               => new SolidColorBrush(WpfColor.FromRgb(80,  80,  80))
        };
        StripeOverlay.Visibility = State == IconState.Gray
            ? Visibility.Visible : Visibility.Collapsed;
    }
}
