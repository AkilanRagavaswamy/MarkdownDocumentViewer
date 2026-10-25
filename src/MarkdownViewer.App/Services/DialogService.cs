using MarkdownViewer.App.Views;
using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarkdownViewer.App.Services;

/// <summary>Shows ContentDialogs in the main window. Only one dialog can be open per XamlRoot.</summary>
public sealed class DialogService : IDialogService
{
    private readonly IWindowProvider _windows;
    private readonly IServiceProvider _services;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DialogService(IWindowProvider windows, IServiceProvider services)
    {
        _windows = windows;
        _services = services;
    }

    public async Task ShowAboutAsync()
    {
        var root = _windows.XamlRoot;
        if (root is null)
        {
            return;
        }

        // Clicking the logo again while the dialog is up must not queue a second one.
        if (!await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var dialog = new AboutDialog { XamlRoot = root };
            MatchTheme(dialog);
            await dialog.ShowAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ShowShortcutsAsync()
    {
        var root = _windows.XamlRoot;
        if (root is null)
        {
            return;
        }

        // Pressing Ctrl+/ again while the dialog is up must not queue a second one.
        if (!await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var dialog = new ShortcutsDialog(_services.GetRequiredService<ShortcutsViewModel>())
            {
                XamlRoot = root,
            };
            MatchTheme(dialog);
            await dialog.ShowAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var root = _windows.XamlRoot;
        if (root is null)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
            };

            if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style) && style is Style s)
            {
                dialog.Style = s;
            }

            MatchTheme(dialog);
            await dialog.ShowAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void MatchTheme(FrameworkElement dialog)
    {
        // Dialogs live in the popup layer and do not inherit the window root's RequestedTheme.
        if (_windows.RootElement is { } root)
        {
            dialog.RequestedTheme = root.ActualTheme;
        }
    }
}
