using MarkdownViewer.Core.Contracts;
using Microsoft.UI.Xaml;
using Windows.UI.Text;

namespace MarkdownViewer.App.Converters;

/// <summary>Pure functions for x:Bind function bindings that combine several ViewModel values.</summary>
public static class BindingFunctions
{
    public static Visibility VisibleIf(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility CollapsedIf(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Visible when <paramref name="first"/> is true and <paramref name="second"/> is false.</summary>
    public static Visibility VisibleIfFirstAndNotSecond(bool first, bool second) => first && !second ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visible when both flags are false.</summary>
    public static Visibility VisibleIfNeither(bool first, bool second) => !first && !second ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>"C:\docs\README.md" → ["C:", "docs", "README.md"] for BreadcrumbBar.</summary>
    public static IReadOnlyList<string> Breadcrumb(string? path) => PathToBreadcrumbConverter.Split(path);

    public static Visibility VisibleIfText(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Visible when <paramref name="text"/> has content and <paramref name="other"/> is empty.</summary>
    public static Visibility VisibleIfTextAndEmpty(string? text, string? other) => !string.IsNullOrWhiteSpace(text) && string.IsNullOrEmpty(other) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True when both strings have content.</summary>
    public static bool HasTextAndContent(string? text, string? other) => !string.IsNullOrWhiteSpace(text) && !string.IsNullOrEmpty(other);

    public static FontWeight HeadingWeight(bool isCurrent, int level) => isCurrent || level <= 1 ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public static bool IsComfortable(DensityPreset density) => density == DensityPreset.Comfortable;

    public static bool IsCompact(DensityPreset density) => density == DensityPreset.Compact;

    public static bool IsUltra(DensityPreset density) => density == DensityPreset.Ultra;

    public static bool IsColumnsAuto(ColumnMode mode) => mode == ColumnMode.Auto;

    public static bool IsColumnsOne(ColumnMode mode) => mode == ColumnMode.One;

    public static bool IsColumnsTwo(ColumnMode mode) => mode == ColumnMode.Two;
}