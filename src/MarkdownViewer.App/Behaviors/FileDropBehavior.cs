using System.Diagnostics;
using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.ViewModels;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace MarkdownViewer.App.Behaviors;

/// <summary>
/// Accepts Markdown files and folders dropped on an element and forwards them to
/// <see cref="MainViewModel.OpenPathsAsync"/>.
/// Usage: &lt;Grid behaviors:FileDropBehavior.Target="{x:Bind ViewModel, Mode=OneTime}"&gt;
/// </summary>
public static class FileDropBehavior
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target",
        typeof(MainViewModel),
        typeof(FileDropBehavior),
        new PropertyMetadata(null, OnTargetChanged));

    public static MainViewModel? GetTarget(UIElement element) => (MainViewModel?)element.GetValue(TargetProperty);

    public static void SetTarget(UIElement element, MainViewModel? value) => element.SetValue(TargetProperty, value);

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.DragOver -= OnDragOver;
        element.Drop -= OnDrop;

        if (e.NewValue is MainViewModel)
        {
            element.AllowDrop = true;
            element.DragOver += OnDragOver;
            element.Drop += OnDrop;
        }
        else
        {
            element.AllowDrop = false;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is { } ui)
        {
            ui.Caption = "Open";
            ui.IsCaptionVisible = true;
            ui.IsContentVisible = true;
        }

        e.Handled = true;
    }

    private static async void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not UIElement element || GetTarget(element) is not { } target)
        {
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        var paths = new List<string>();
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Path))
                {
                    continue;
                }

                if (item is StorageFolder || IsMarkdown(item.Path))
                {
                    paths.Add(item.Path);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Reading dropped items failed: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }

        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            await target.OpenPathsAsync(paths);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Opening dropped files failed: {ex}");
        }
    }

    private static bool IsMarkdown(string path)
    {
        var extension = Path.GetExtension(path);
        return MarkdownExtensions.All.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
    }
}
