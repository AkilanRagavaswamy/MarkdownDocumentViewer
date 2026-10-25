using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace MarkdownViewer.App.Services;

/// <summary>
/// Owns all WebView2 interaction for one <see cref="DocumentTabViewModel"/>:
/// environment + virtual hosts, navigation of the rendered HTML, the JS ↔ host message protocol
/// (CONTRACT.md "WebView bridge"), and ViewModel → view messages filtered by TabId.
/// Created by DocumentView whenever its ViewModel changes; disposed when it changes again or unloads.
/// </summary>
public sealed class WebViewBridge : IDisposable
{
    private const string AppAssetsHost = "appassets.mdv";
    private const string DocumentHost = "doc.mdv";
    private const string RenderHost = "render.mdv";
    private const string DocumentOrigin = "https://doc.mdv/";
    private const string RenderOrigin = "https://render.mdv/";
    private const string AppAssetsOrigin = "https://appassets.mdv/";

    // NavigateToString is limited to 2 MB; larger documents are written to LocalCache\render and served from render.mdv.
    private const int MaxInlineHtmlChars = 1_500_000;

    private static readonly HashSet<string> s_allowedContextMenuItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "copy",
        "copyLinkLocation",
        "selectAll",
    };

    private static readonly object s_environmentGate = new();
    private static Task<CoreWebView2Environment>? s_environment;

    // EnsureCoreWebView2Async must not overlap on the same control (DocumentView reuses its WebView2 across tabs).
    private static readonly ConditionalWeakTable<WebView2, Task> s_initialization = new();

    // Per-tab scroll position so switching tabs or live reload restores the reading position.
    private static readonly ConcurrentDictionary<Guid, ScrollState> s_scrollByTab = new();

    private readonly WebView2 _webView;
    private readonly DocumentTabViewModel _vm;
    private readonly IHtmlDocumentBuilder _builder;
    private readonly IThemeService _theme;
    private readonly IDialogService _dialogs;
    private readonly Action _focusFindBox;
    private readonly Func<IReadOnlyList<string>, Task>? _openPaths;
    private readonly Action<string>? _shellKey;
    private readonly DispatcherQueue _dispatcher;

    private CoreWebView2? _core;
    private bool _disposed;
    private bool _ready;
    private string? _mappedDocumentFolder;
    private string? _renderFile;
    private double? _pendingScrollY;
    private string? _pendingAnchor;
    private ViewerAppearance? _pendingAppearance;
    private int _navigationVersion;

    public WebViewBridge(
        WebView2 webView,
        DocumentTabViewModel viewModel,
        IHtmlDocumentBuilder builder,
        IThemeService theme,
        IDialogService dialogs,
        Action focusFindBox,
        Func<IReadOnlyList<string>, Task>? openPaths = null,
        Action<string>? shellKey = null)
    {
        _webView = webView;
        _vm = viewModel;
        _builder = builder;
        _theme = theme;
        _dialogs = dialogs;
        _focusFindBox = focusFindBox;
        _openPaths = openPaths;
        _shellKey = shellKey;
        _dispatcher = webView.DispatcherQueue;

        // Paint the reading colour before the first frame to avoid a white flash in dark theme.
        _webView.DefaultBackgroundColor = ReadingColor(_theme.IsDark);
    }

    public async Task InitializeAsync()
    {
        try
        {
            var environment = await GetEnvironmentAsync();
            if (_disposed)
            {
                return;
            }

            await EnsureCoreAsync(_webView, environment);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 initialization failed: {ex}");
            ResetEnvironmentIfFaulted();
            if (!_disposed)
            {
                await _dialogs.ShowErrorAsync(
                    "Can't display the document",
                    "The Microsoft Edge WebView2 Runtime is missing or failed to start. Install or repair it, then reopen the file.\n\n" + ex.Message);
            }

            return;
        }

        if (_disposed || _webView.CoreWebView2 is null)
        {
            return;
        }

        _core = _webView.CoreWebView2;
        ConfigureSettings(_core);
        MapStaticHosts(_core);

        _core.NavigationStarting += OnNavigationStarting;
        _core.FrameNavigationStarting += OnFrameNavigationStarting;
        _core.NewWindowRequested += OnNewWindowRequested;
        _core.WebMessageReceived += OnWebMessageReceived;
        _core.ContextMenuRequested += OnContextMenuRequested;
        _core.ProcessFailed += OnProcessFailed;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        RegisterMessages();

        await NavigateAsync();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _vm.PropertyChanged -= OnViewModelPropertyChanged;

        if (_core is not null)
        {
            _core.NavigationStarting -= OnNavigationStarting;
            _core.FrameNavigationStarting -= OnFrameNavigationStarting;
            _core.NewWindowRequested -= OnNewWindowRequested;
            _core.WebMessageReceived -= OnWebMessageReceived;
            _core.ContextMenuRequested -= OnContextMenuRequested;
            _core.ProcessFailed -= OnProcessFailed;
        }

        DeleteRenderFile();
    }

    // ---------------------------------------------------------------- Environment & settings

    private static Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        lock (s_environmentGate)
        {
            return s_environment ??= CreateEnvironmentAsync();
        }
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(AppPaths.LocalFolder, "WebView2");
        Directory.CreateDirectory(userDataFolder);
        return await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions());
    }

    private static void ResetEnvironmentIfFaulted()
    {
        lock (s_environmentGate)
        {
            if (s_environment is { IsFaulted: true } or { IsCanceled: true })
            {
                s_environment = null;
            }
        }
    }

    private static Task EnsureCoreAsync(WebView2 webView, CoreWebView2Environment environment)
    {
        if (webView.CoreWebView2 is not null)
        {
            return Task.CompletedTask;
        }

        lock (s_initialization)
        {
            if (s_initialization.TryGetValue(webView, out var pending) && !pending.IsFaulted && !pending.IsCanceled)
            {
                return pending;
            }

            var task = webView.EnsureCoreWebView2Async(environment).AsTask();
            s_initialization.AddOrUpdate(webView, task);
            return task;
        }
    }

    private static void ConfigureSettings(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.IsScriptEnabled = true;
        settings.IsWebMessageEnabled = true;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsStatusBarEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;

        // Zoom is owned by the ViewModel (Ctrl+= / Ctrl+- / Ctrl+0) and applied via CSS zoom.
        settings.IsZoomControlEnabled = false;

        // Let Ctrl+F / Ctrl+P / F3 etc. reach the window's KeyboardAccelerators instead of the browser.
        settings.AreBrowserAcceleratorKeysEnabled = false;

        // Default menus stay on, but OnContextMenuRequested strips everything except copy.
        settings.AreDefaultContextMenusEnabled = true;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
    }

    private static void MapStaticHosts(CoreWebView2 core)
    {
        var assets = AppPaths.WebAssetsFolder;
        if (Directory.Exists(assets))
        {
            core.SetVirtualHostNameToFolderMapping(AppAssetsHost, assets, CoreWebView2HostResourceAccessKind.DenyCors);
        }
        else
        {
            Debug.WriteLine($"Viewer assets folder missing: {assets}");
        }

        core.SetVirtualHostNameToFolderMapping(RenderHost, RenderFolder, CoreWebView2HostResourceAccessKind.DenyCors);
    }

    private static string RenderFolder
    {
        get
        {
            var folder = Path.Combine(AppPaths.LocalCacheFolder, "render");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private void EnsureDocumentMapping()
    {
        if (_core is null)
        {
            return;
        }

        var folder = _vm.DocumentFolder;
        if (string.Equals(folder, _mappedDocumentFolder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            _core.ClearVirtualHostNameToFolderMapping(DocumentHost);
        }
        catch (Exception)
        {
            // Not mapped yet.
        }

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            _core.SetVirtualHostNameToFolderMapping(DocumentHost, folder, CoreWebView2HostResourceAccessKind.Allow);
            _mappedDocumentFolder = folder;
        }
        else
        {
            _mappedDocumentFolder = null;
        }
    }

    // ---------------------------------------------------------------- Navigation

    private async Task NavigateAsync()
    {
        if (_core is null || _disposed)
        {
            return;
        }

        var html = _vm.Html;
        if (string.IsNullOrEmpty(html))
        {
            // Still loading (or failed): keep whatever is shown; the next Html change navigates.
            return;
        }

        var version = ++_navigationVersion;
        _ready = false;
        _pendingScrollY = s_scrollByTab.TryGetValue(_vm.Id, out var state)
            && string.Equals(state.FilePath, _vm.FilePath, StringComparison.OrdinalIgnoreCase)
            && state.ShowSource == _vm.ShowSource
                ? state.Y
                : null;

        EnsureDocumentMapping();

        try
        {
            if (html.Length < MaxInlineHtmlChars)
            {
                DeleteRenderFile();
                _core.NavigateToString(html);
                return;
            }

            var file = Path.Combine(RenderFolder, $"{_vm.Id:N}.html");
            await File.WriteAllTextAsync(file, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (_disposed || version != _navigationVersion || _core is null)
            {
                return;
            }

            _renderFile = file;
            _core.Navigate($"{RenderOrigin}{_vm.Id:N}.html?v={version.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            Debug.WriteLine($"Navigation failed: {ex.Message}");
        }
    }

    private static bool IsInternalNavigation(string uri) =>
        uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith(RenderOrigin, StringComparison.OrdinalIgnoreCase);

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (IsInternalNavigation(args.Uri))
        {
            return;
        }

        // Every link click / redirect leaves the reading page: cancel and let the ViewModel decide
        // (scroll to anchor, open another Markdown file, or launch the browser).
        args.Cancel = true;

        // A file dropped onto the WebView navigates to file:///...; open it like a drop on the window.
        if (TryGetLocalFile(args.Uri, out var localPath))
        {
            if (_openPaths is not null)
            {
                _ = OpenDroppedAsync(localPath);
            }

            return;
        }

        RouteLink(args.Uri);
    }

    private static bool TryGetLocalFile(string? uri, out string path)
    {
        path = string.Empty;
        if (uri is null
            || !uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || !parsed.IsFile)
        {
            return false;
        }

        path = parsed.LocalPath;
        return true;
    }

    private async Task OpenDroppedAsync(string path)
    {
        try
        {
            await _openPaths!(new[] { path });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Opening dropped file failed: {ex.Message}");
        }
    }

    private void OnFrameNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var uri = args.Uri;
        if (IsInternalNavigation(uri)
            || uri.StartsWith(AppAssetsOrigin, StringComparison.OrdinalIgnoreCase)
            || uri.StartsWith(DocumentOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        args.Cancel = true;
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        RouteLink(args.Uri);
    }

    private void RouteLink(string? uri)
    {
        var href = NormalizeHref(uri);
        if (href is not null)
        {
            _vm.OnLinkActivated(href);
        }
    }

    /// <summary>Maps https://doc.mdv/&lt;rel&gt; back to the document-relative href the author wrote.</summary>
    private static string? NormalizeHref(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        if (uri.StartsWith(AppAssetsOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (uri.StartsWith(DocumentOrigin, StringComparison.OrdinalIgnoreCase))
        {
            var relative = uri.Substring(DocumentOrigin.Length);
            return relative.Length == 0 ? null : relative;
        }

        return uri;
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        if (args.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            _ = NavigateAsync();
        }
    }

    private void OnContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        var items = args.MenuItems;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!s_allowedContextMenuItems.Contains(items[i].Name))
            {
                items.RemoveAt(i);
            }
        }

        if (items.Count == 0)
        {
            args.Handled = true;
        }
    }

    // ---------------------------------------------------------------- JS → host

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;

            // Accept both postMessage(obj) and postMessage(JSON.stringify(obj)).
            if (root.ValueKind == JsonValueKind.String)
            {
                using var inner = JsonDocument.Parse(root.GetString() ?? "{}");
                HandleMessage(inner.RootElement);
            }
            else
            {
                HandleMessage(root);
            }
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"Ignoring malformed web message: {ex.Message}");
        }
    }

    private void HandleMessage(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("type", out var typeElement)
            || typeElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        switch (typeElement.GetString())
        {
            case "ready":
                OnPageReady();
                break;

            case "heading":
                if (GetString(message, "id") is { Length: > 0 } id)
                {
                    _vm.OnHeadingChanged(id);
                }

                break;

            case "find":
                _vm.OnFindResult(GetInt(message, "current"), GetInt(message, "total"));
                break;

            case "link":
                if (GetString(message, "href") is { Length: > 0 } href)
                {
                    _vm.OnLinkActivated(NormalizeHref(href) ?? href);
                }

                break;

            case "key":
                // Forwarded by viewer.js because XAML accelerators do not fire while the WebView has focus.
                if (GetString(message, "key") is { Length: > 0 } shellKey)
                {
                    // Enqueued, not called inline: the handler flips window chrome, and doing that inside a
                    // WebView2 event callback re-enters the layout pass. An escaping exception there fail-fasts.
                    _dispatcher.TryEnqueue(() =>
                    {
                        try
                        {
                            _shellKey?.Invoke(shellKey);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Shell key {shellKey} failed: {ex}");
                        }
                    });
                }

                break;

            case "scroll":
                if (message.TryGetProperty("y", out var y) && y.ValueKind == JsonValueKind.Number)
                {
                    s_scrollByTab[_vm.Id] = new ScrollState(_vm.FilePath, _vm.ShowSource, y.GetDouble());
                }

                break;
        }
    }

    private void OnPageReady()
    {
        _ready = true;
        ApplyZoom();

        if (_pendingAppearance is { } appearance)
        {
            _pendingAppearance = null;
            Execute($"mdv.setAppearance({_builder.BuildAppearanceJson(appearance)})");
        }

        if (_pendingAnchor is { } anchor)
        {
            _pendingAnchor = null;
            _pendingScrollY = null;
            Execute($"mdv.scrollToAnchor({JsonSerializer.Serialize(anchor)})");
        }
        else if (_pendingScrollY is { } scrollY && scrollY > 0)
        {
            _pendingScrollY = null;
            Execute($"mdv.setScrollY({scrollY.ToString("R", CultureInfo.InvariantCulture)})");
        }

        _vm.OnViewerReady();
    }

    private static string? GetString(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;

    // ---------------------------------------------------------------- ViewModel → view

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(DocumentTabViewModel.Html):
                    _ = NavigateAsync();
                    break;

                case nameof(DocumentTabViewModel.DocumentFolder):
                    EnsureDocumentMapping();
                    break;

                case nameof(DocumentTabViewModel.ZoomFactor):
                    ApplyZoom();
                    break;
            }
        });
    }

    private void RegisterMessages()
    {
        var messenger = WeakReferenceMessenger.Default;

        messenger.Register<WebViewBridge, ScrollToAnchorMessage>(this, static (r, m) => r.OnScrollToAnchor(m));
        messenger.Register<WebViewBridge, FindMessage>(this, static (r, m) => r.OnFind(m));
        messenger.Register<WebViewBridge, ClearFindMessage>(this, static (r, m) => r.OnClearFind(m));
        messenger.Register<WebViewBridge, CollapseSectionsMessage>(this, static (r, m) => r.OnCollapseSections(m));
        messenger.Register<WebViewBridge, PrintMessage>(this, static (r, m) => r.OnPrint(m));
        messenger.Register<WebViewBridge, ExportPdfMessage>(this, static (r, m) => r.OnExportPdf(m));
        messenger.Register<WebViewBridge, FocusFindBoxMessage>(this, static (r, m) => r.OnFocusFindBox(m));
        messenger.Register<WebViewBridge, AppearanceChangedMessage>(this, static (r, m) => r.OnAppearanceChanged(m));
    }

    private bool IsMine(Guid tabId) => !_disposed && tabId == _vm.Id;

    private void OnScrollToAnchor(ScrollToAnchorMessage message)
    {
        if (!IsMine(message.TabId))
        {
            return;
        }

        RunOnUi(() =>
        {
            if (_ready)
            {
                Execute($"mdv.scrollToAnchor({JsonSerializer.Serialize(message.AnchorId)})");
            }
            else
            {
                _pendingAnchor = message.AnchorId;
            }
        });
    }

    private void OnFind(FindMessage message)
    {
        if (IsMine(message.TabId))
        {
            RunOnUi(() => Execute(
                $"mdv.find({JsonSerializer.Serialize(message.Query)}, {(message.Backwards ? "true" : "false")})"));
        }
    }

    private void OnClearFind(ClearFindMessage message)
    {
        if (IsMine(message.TabId))
        {
            RunOnUi(() => Execute("mdv.clearFind()"));
        }
    }

    private void OnCollapseSections(CollapseSectionsMessage message)
    {
        if (IsMine(message.TabId))
        {
            RunOnUi(() => Execute(message.Collapse ? "mdv.collapseAll()" : "mdv.expandAll()"));
        }
    }

    private void OnPrint(PrintMessage message)
    {
        if (!IsMine(message.TabId))
        {
            return;
        }

        RunOnUi(() =>
        {
            try
            {
                _core?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowPrintUI failed: {ex.Message}");
            }
        });
    }

    private void OnExportPdf(ExportPdfMessage message)
    {
        if (!IsMine(message.TabId))
        {
            return;
        }

        RunOnUi(async () =>
        {
            if (_core is null)
            {
                return;
            }

            bool succeeded;
            try
            {
                succeeded = await _core.PrintToPdfAsync(message.TargetPath, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrintToPdfAsync failed: {ex.Message}");
                succeeded = false;
            }

            if (!succeeded && !_disposed)
            {
                await _dialogs.ShowErrorAsync("Export failed", $"Couldn't save the PDF to {message.TargetPath}.");
            }
        });
    }

    private void OnFocusFindBox(FocusFindBoxMessage message)
    {
        if (IsMine(message.TabId))
        {
            RunOnUi(_focusFindBox);
        }
    }

    private void OnAppearanceChanged(AppearanceChangedMessage message)
    {
        if (_disposed)
        {
            return;
        }

        RunOnUi(() =>
        {
            _webView.DefaultBackgroundColor = ReadingColor(message.Appearance.IsDark);
            if (_ready)
            {
                Execute($"mdv.setAppearance({_builder.BuildAppearanceJson(message.Appearance)})");
            }
            else
            {
                _pendingAppearance = message.Appearance;
            }
        });
    }

    // ---------------------------------------------------------------- Helpers

    private void ApplyZoom()
    {
        if (!_ready)
        {
            return;
        }

        // WinUI's WebView2 does not expose the controller ZoomFactor, so zoom is applied as CSS zoom.
        var factor = Math.Clamp(_vm.ZoomFactor <= 0 ? 1.0 : _vm.ZoomFactor, 0.5, 3.0);
        Execute($"document.documentElement.style.zoom = '{factor.ToString("0.###", CultureInfo.InvariantCulture)}'", guardMdv: false);
    }

    private void Execute(string script, bool guardMdv = true)
    {
        if (_core is null || _disposed)
        {
            return;
        }

        var guarded = guardMdv ? $"window.mdv && {script};" : script + ";";
        _ = ExecuteAsync(_core, guarded);
    }

    private static async Task ExecuteAsync(CoreWebView2 core, string script)
    {
        try
        {
            await core.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ExecuteScriptAsync failed: {ex.Message}");
        }
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.TryEnqueue(() => action());
        }
    }

    private void DeleteRenderFile()
    {
        var file = _renderFile;
        _renderFile = null;
        if (file is null)
        {
            return;
        }

        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not delete {file}: {ex.Message}");
        }
    }

    private static Color ReadingColor(bool isDark) =>
        isDark ? ColorUtil.Opaque(0x1C, 0x1C, 0x1C) : ColorUtil.Opaque(0xFF, 0xFF, 0xFF);

    private sealed record ScrollState(string FilePath, bool ShowSource, double Y);
}
