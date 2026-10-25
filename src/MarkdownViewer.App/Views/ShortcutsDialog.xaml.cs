using System.Globalization;
using MarkdownViewer.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarkdownViewer.App.Views;

/// <summary>
/// Keyboard shortcuts dialog. The caller (IDialogService) sets XamlRoot and calls ShowAsync.
/// Code-behind holds only the ViewModel and pure static view-conversion helpers.
/// </summary>
public sealed partial class ShortcutsDialog : ContentDialog
{
    public ShortcutsDialog(ShortcutsViewModel vm)
    {
        ViewModel = vm;
        InitializeComponent();
    }

    public ShortcutsViewModel ViewModel { get; }

    // ---------------- Pure view-conversion helpers ----------------

    public static Visibility NotVisible(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string CountText(int count) =>
        count == 1 ? "1 shortcut" : string.Create(CultureInfo.InvariantCulture, $"{count} shortcuts");

    public static string EmptyText(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? "No shortcuts in this category."
            : $"No shortcuts match “{query.Trim()}”.";
}
