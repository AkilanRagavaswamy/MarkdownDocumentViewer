using System;
using System.Globalization;
using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel;
using Windows.UI;

namespace MarkdownViewer.App.Views;

/// <summary>
/// Settings page. Code-behind holds only the ViewModel reference and pure, static
/// view-conversion helpers used by x:Bind functions; all behavior lives in <see cref="SettingsViewModel"/>.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = SharedViewModel;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.RefreshDefaultAppStatus();
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>
    /// The singleton SettingsViewModel, exposed statically so DataTemplates (which cannot reach the page)
    /// can bind commands with x:Bind static paths.
    /// </summary>
    public static SettingsViewModel SharedViewModel =>
        global::MarkdownViewer.App.App.Services.GetRequiredService<SettingsViewModel>();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshDefaultAppStatus();
    }

    // ---------------- Pure view-conversion helpers ----------------

    public static bool Not(bool value) => !value;

    public static Visibility NotVisible(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static double EnabledOpacity(bool disabled) => disabled ? 0.45 : 1.0;

    /// <summary>Parses #RGB, #RRGGBB or #AARRGGBB; invalid input yields a transparent brush.</summary>
    public static SolidColorBrush HexToBrush(string? hex) => new(HexToColor(hex));

    public static Color HexToColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Colors.Transparent;
        }

        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3)
        {
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        }

        if (s.Length == 6)
        {
            s = "FF" + s;
        }

        if (s.Length != 8 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return Colors.Transparent;
        }

        return ColorHelper.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb);
    }

    public static string ColorToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    public static string ColumnModeLabel(ColumnMode mode) => mode switch
    {
        ColumnMode.Auto => "Auto (up to 2)",
        ColumnMode.One => "1 column",
        ColumnMode.Two => "2 columns",
        _ => mode.ToString(),
    };

    public static string VersionText
    {
        get
        {
            try
            {
                var v = Package.Current.Id.Version;
                return string.Create(CultureInfo.InvariantCulture, $"Version {v.Major}.{v.Minor}.{v.Build}.{v.Revision}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                // Running unpackaged (no package identity).
                return "Version (development build)";
            }
        }
    }

    // Live preview metrics mirror the approved design's scaled-down density samples.
    public static double PreviewFontSize(DensityPreset density) => density switch
    {
        DensityPreset.Comfortable => 14,
        DensityPreset.Ultra => 11.5,
        _ => 12.5,
    };

    public static double PreviewHeadingSize(DensityPreset density) => density switch
    {
        DensityPreset.Comfortable => 18,
        DensityPreset.Ultra => 13.5,
        _ => 15,
    };

    public static double PreviewGap(DensityPreset density) => density switch
    {
        DensityPreset.Comfortable => 10,
        DensityPreset.Ultra => 4,
        _ => 6,
    };

    public static string PreviewCaption(DensityPreset density) => "Preview · " + density;
}

/// <summary>Item text for the Columns ComboBox; delegates to <see cref="SettingsPage.ColumnModeLabel"/>.</summary>
public sealed partial class ColumnModeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        ColumnMode mode => SettingsPage.ColumnModeLabel(mode),
        int i when Enum.IsDefined(typeof(ColumnMode), i) => SettingsPage.ColumnModeLabel((ColumnMode)i),
        null => string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
