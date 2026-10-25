using System.Diagnostics;
using MarkdownViewer.Core.Contracts;
using Windows.Storage;
using Windows.System;
using WinRT.Interop;

namespace MarkdownViewer.App.Services;

public sealed class LauncherService : ILauncherService
{
    private readonly IWindowProvider _windows;

    public LauncherService(IWindowProvider windows)
    {
        _windows = windows;
    }

    public async Task OpenUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        await Launcher.LaunchUriAsync(uri);
    }

    public async Task OpenWithDefaultEditorAsync(string filePath)
    {
        var file = await StorageFile.GetFileFromPathAsync(filePath);
        var options = new LauncherOptions { DisplayApplicationPicker = true };

        // Desktop apps must parent the "Open with" picker to their window.
        if (_windows.Hwnd != 0)
        {
            try
            {
                InitializeWithWindow.Initialize(options, _windows.Hwnd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LauncherOptions does not accept a window handle: {ex.Message}");
            }
        }

        await Launcher.LaunchFileAsync(file, options);
    }
}
