using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;
using Windows.Graphics;

namespace MarkdownViewer.App.Helpers;

/// <summary>
/// Custom title bar plumbing: extends content into the title bar (Tall caption buttons), makes
/// <c>dragRegion</c> the caption area (Standard caption-button height, 32px), reserves the system caption-button insets in two grid columns,
/// and registers interactive title-bar elements as <see cref="NonClientRegionKind.Passthrough"/> so they
/// receive clicks. Rects are recomputed whenever size, layout or DPI changes.
/// </summary>
internal sealed class TitleBarHelper : IDisposable
{
    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly FrameworkElement _dragRegion;
    private readonly ColumnDefinition _leftInset;
    private readonly ColumnDefinition _rightInset;
    private readonly FrameworkElement[] _passthrough;
    private XamlRoot? _xamlRoot;
    private bool _disposed;

    public TitleBarHelper(
        Window window,
        FrameworkElement dragRegion,
        ColumnDefinition leftInset,
        ColumnDefinition rightInset,
        params FrameworkElement[] passthrough)
    {
        _window = window;
        _appWindow = window.AppWindow;
        _dragRegion = dragRegion;
        _leftInset = leftInset;
        _rightInset = rightInset;
        _passthrough = passthrough;

        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(dragRegion);

        var titleBar = _appWindow.TitleBar;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        _appWindow.Changed += OnAppWindowChanged;
        _dragRegion.SizeChanged += OnElementSizeChanged;
        _dragRegion.Loaded += OnDragRegionLoaded;
        foreach (var element in _passthrough)
        {
            element.SizeChanged += OnElementSizeChanged;
            element.LayoutUpdated += OnElementLayoutUpdated;
        }
    }

    public void Update()
    {
        if (_disposed)
        {
            return;
        }

        var xamlRoot = _dragRegion.XamlRoot;
        if (xamlRoot is null)
        {
            return;
        }

        var scale = xamlRoot.RasterizationScale;
        if (scale <= 0)
        {
            scale = 1;
        }

        var titleBar = _appWindow.TitleBar;
        _leftInset.Width = new GridLength(titleBar.LeftInset / scale);
        _rightInset.Width = new GridLength(titleBar.RightInset / scale);

        var rects = new List<RectInt32>(_passthrough.Length);
        foreach (var element in _passthrough)
        {
            if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0 || element.XamlRoot is null)
            {
                continue;
            }

            var bounds = element.TransformToVisual(null).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            rects.Add(new RectInt32
            {
                X = (int)Math.Round(bounds.X * scale),
                Y = (int)Math.Round(bounds.Y * scale),
                Width = (int)Math.Round(bounds.Width * scale),
                Height = (int)Math.Round(bounds.Height * scale),
            });
        }

        var source = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
        source.SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _appWindow.Changed -= OnAppWindowChanged;
        _dragRegion.SizeChanged -= OnElementSizeChanged;
        _dragRegion.Loaded -= OnDragRegionLoaded;
        foreach (var element in _passthrough)
        {
            element.SizeChanged -= OnElementSizeChanged;
            element.LayoutUpdated -= OnElementLayoutUpdated;
        }

        if (_xamlRoot is not null)
        {
            _xamlRoot.Changed -= OnXamlRootChanged;
        }
    }

    private void OnDragRegionLoaded(object sender, RoutedEventArgs e)
    {
        if (_xamlRoot is null && _dragRegion.XamlRoot is { } root)
        {
            _xamlRoot = root;
            _xamlRoot.Changed += OnXamlRootChanged;
        }

        Update();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Update();

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPresenterChange)
        {
            Update();
        }
    }

    private void OnElementSizeChanged(object sender, SizeChangedEventArgs e) => Update();

    private Rect _lastLayoutBounds;

    // Element positions can shift without their own size changing (e.g. tab count changes the TabView width).
    private void OnElementLayoutUpdated(object? sender, object e)
    {
        if (_passthrough.Length == 0 || _passthrough[0].XamlRoot is null)
        {
            return;
        }

        var first = _passthrough[0];
        var bounds = first.TransformToVisual(null).TransformBounds(new Rect(0, 0, first.ActualWidth, first.ActualHeight));
        if (!bounds.Equals(_lastLayoutBounds))
        {
            _lastLayoutBounds = bounds;
            Update();
        }
    }
}
