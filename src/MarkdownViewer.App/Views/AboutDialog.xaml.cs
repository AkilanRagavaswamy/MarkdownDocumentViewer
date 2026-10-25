using Microsoft.UI.Xaml.Controls;

namespace MarkdownViewer.App.Views;

/// <summary>
/// About dialog, shown from the app logo in the title bar. The caller (IDialogService) sets
/// XamlRoot and calls ShowAsync.
/// </summary>
public sealed partial class AboutDialog : ContentDialog
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    /// <summary>Package version, or a development-build note when running without package identity.</summary>
    public static string VersionText => SettingsPage.VersionText;
}
