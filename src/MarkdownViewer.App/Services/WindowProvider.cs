using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace MarkdownViewer.App.Services;

/// <summary>Gives platform services access to the main window (HWND for pickers, XamlRoot for dialogs).</summary>
public interface IWindowProvider
{
    Window? Window { get; }

    nint Hwnd { get; }

    AppWindow? AppWindow { get; }

    FrameworkElement? RootElement { get; }

    XamlRoot? XamlRoot { get; }
}

public sealed class WindowProvider : IWindowProvider
{
    public Window? Window { get; private set; }

    public nint Hwnd { get; private set; }

    public AppWindow? AppWindow => Window?.AppWindow;

    public FrameworkElement? RootElement => Window?.Content as FrameworkElement;

    public XamlRoot? XamlRoot => Window?.Content?.XamlRoot;

    public void Attach(Window window)
    {
        Window = window;
        Hwnd = WindowNative.GetWindowHandle(window);
    }
}
