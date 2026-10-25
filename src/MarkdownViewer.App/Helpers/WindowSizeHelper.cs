using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace MarkdownViewer.App.Helpers;

/// <summary>
/// Enforces a minimum window size (in DIPs, DPI-aware) by answering WM_GETMINMAXINFO through a
/// comctl32 window subclass, and sizes the window initially.
/// </summary>
internal sealed class WindowSizeHelper : IDisposable
{
    private const uint WM_GETMINMAXINFO = 0x0024;
    private static readonly UIntPtr SubclassId = new(0x4D44); // "MD"

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly int _minWidth;
    private readonly int _minHeight;
    private readonly SubclassProc _proc; // Must stay referenced while the subclass is installed.
    private bool _installed;

    public WindowSizeHelper(Window window, int minWidth, int minHeight)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(window);
        _minWidth = minWidth;
        _minHeight = minHeight;
        _proc = WindowProc;
        _installed = SetWindowSubclass(_hwnd, _proc, SubclassId, UIntPtr.Zero);
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData);

    public double Scale
    {
        get
        {
            var dpi = GetDpiForWindow(_hwnd);
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }
    }

    /// <summary>Resizes to <paramref name="width"/>×<paramref name="height"/> DIPs, clamped to the work area.</summary>
    public void ResizeDips(int width, int height)
    {
        var scale = Scale;
        var w = (int)Math.Round(Math.Max(width, _minWidth) * scale);
        var h = (int)Math.Round(Math.Max(height, _minHeight) * scale);

        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(_window.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        if (area is not null)
        {
            w = Math.Min(w, area.WorkArea.Width);
            h = Math.Min(h, area.WorkArea.Height);
        }

        _window.AppWindow.Resize(new SizeInt32 { Width = w, Height = h });

        if (area is not null)
        {
            var x = area.WorkArea.X + ((area.WorkArea.Width - w) / 2);
            var y = area.WorkArea.Y + ((area.WorkArea.Height - h) / 2);
            _window.AppWindow.Move(new PointInt32 { X = x, Y = y });
        }
    }

    public void Dispose()
    {
        if (_installed)
        {
            RemoveWindowSubclass(_hwnd, _proc, SubclassId);
            _installed = false;
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, UIntPtr dwRefData)
    {
        var result = DefSubclassProc(hWnd, uMsg, wParam, lParam);
        if (uMsg == WM_GETMINMAXINFO && lParam != IntPtr.Zero)
        {
            var dpi = GetDpiForWindow(hWnd);
            var scale = dpi == 0 ? 1.0 : dpi / 96.0;
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = Math.Max(info.ptMinTrackSize.X, (int)Math.Round(_minWidth * scale));
            info.ptMinTrackSize.Y = Math.Max(info.ptMinTrackSize.Y, (int)Math.Round(_minHeight * scale));
            Marshal.StructureToPtr(info, lParam, fDeleteOld: false);
            return IntPtr.Zero;
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, UIntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
