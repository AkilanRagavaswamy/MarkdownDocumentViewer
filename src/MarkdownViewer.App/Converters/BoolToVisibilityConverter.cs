using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MarkdownViewer.App.Converters;

/// <summary>true → Visible, false → Collapsed (or the reverse when <see cref="IsInverted"/>).</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public bool IsInverted { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        return flag ^ IsInverted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility v && v == Visibility.Visible;
        return visible ^ IsInverted;
    }
}

/// <summary>true → Collapsed, false → Visible.</summary>
public sealed class InverseBoolToVisibilityConverter : BoolToVisibilityConverter
{
    public InverseBoolToVisibilityConverter()
    {
        IsInverted = true;
    }
}
