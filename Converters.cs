using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PmLiteMonitor.Controls;
using WpfBrush  = System.Windows.Media.Brush;
using WpfColor  = System.Windows.Media.Color;

namespace PmLiteMonitor;

/// <summary>true → Green brush, false → Red brush</summary>
public class BoolToBrushConverter : IValueConverter
{
    public WpfBrush TrueBrush  { get; set; } = new SolidColorBrush(WpfColor.FromRgb(27,  94, 32));
    public WpfBrush FalseBrush { get; set; } = new SolidColorBrush(WpfColor.FromRgb(183, 28, 28));

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? TrueBrush : FalseBrush;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>true → Green IconState, false → Red IconState</summary>
public class BoolToIconStateConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? IconState.Green : IconState.Red;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>Inverts a bool (for binding IsEnabled when IsConnected)</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is bool b && !b;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>true → "● REC", false → "● IDLE"</summary>
public class RecordingLabelConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? "● REC" : "● IDLE";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>true → Visible, false → Collapsed</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}

/// <summary>Non-empty string → Visible, null/empty → Collapsed  (used for error labels)</summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        string.IsNullOrEmpty(value as string)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) =>
        throw new NotImplementedException();
}
