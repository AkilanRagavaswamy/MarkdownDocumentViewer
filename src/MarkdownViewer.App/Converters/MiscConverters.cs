using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MarkdownViewer.App.Converters;

/// <summary>
/// Pass-through converter. x:Bind TwoWay between an <c>object</c> property (e.g. TabView.SelectedItem)
/// and a typed ViewModel property needs a converter so the generated code casts on ConvertBack.
/// </summary>
public sealed class IdentityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => value;
}

/// <summary>true → SemiBold, false → Normal.</summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool b && b ? FontWeights.SemiBold : FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible, null/empty → Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
