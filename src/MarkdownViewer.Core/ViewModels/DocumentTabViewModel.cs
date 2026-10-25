using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.ViewModels;

/// <summary>One open document (one tab): content, outline, find state, in-document navigation and live reload.</summary>
public sealed partial class DocumentTabViewModel : ObservableRecipient, IDisposable
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 3.0;
    public const double DefaultZoom = 1.0;
    public const string DocumentHost = "doc.mdv";
    private const int WordsPerMinute = 230;

    private static readonly Uri DocumentBaseUri = new("https://" + DocumentHost + "/");

    private readonly IMarkdownRenderService _renderer;
    private readonly IHtmlDocumentBuilder _builder;
    private readonly ITextFileReader _reader;
    private readonly IFileWatcherService _watcher;
    private readonly ISettingsService _settings;
    private readonly ILauncherService _launcher;
    private readonly IDispatcherService _dispatcher;

    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    private volatile ViewerAppearance _appearance;
    private RenderResult? _lastResult;
    private IDisposable? _watchHandle;
    private int _loadVersion;
    private string? _pendingAnchor;
    private bool _disposed;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _html = string.Empty;

    [ObservableProperty]
    private string _sourceText = string.Empty;

    [ObservableProperty]
    private bool _showSource;

    [ObservableProperty]
    private string? _currentHeadingId;

    [ObservableProperty]
    private string _currentSectionTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsText), nameof(ReadMinutes))]
    private int _wordCount;

    [ObservableProperty]
    private string _encodingLabel = string.Empty;

    [ObservableProperty]
    private string _lineEndingLabel = string.Empty;

    [ObservableProperty]
    private double _zoomFactor = DefaultZoom;

    [ObservableProperty]
    private bool _isFindOpen;

    [ObservableProperty]
    private string _findQuery = string.Empty;

    [ObservableProperty]
    private string _findStatus = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoForwardCommand))]
    private bool _canGoForward;

    [ObservableProperty]
    private string _documentFolder = string.Empty;

    public DocumentTabViewModel(
        IMarkdownRenderService renderer,
        IHtmlDocumentBuilder builder,
        ITextFileReader reader,
        IFileWatcherService watcher,
        ISettingsService settings,
        AppearanceProvider appearance,
        ILauncherService launcher,
        IDispatcherService dispatcher)
    {
        _renderer = renderer;
        _builder = builder;
        _reader = reader;
        _watcher = watcher;
        _settings = settings;
        _launcher = launcher;
        _dispatcher = dispatcher;
        _appearance = appearance.Current;

        Outline.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasOutline));

        IsActive = true;
    }

    /// <summary>Token used by views to filter messenger messages.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public ObservableCollection<OutlineItemViewModel> Outline { get; } = new();

    public bool HasOutline => Outline.Count > 0;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public int ReadMinutes => WordCount <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(WordCount / (double)WordsPerMinute));

    /// <summary>e.g. "612 words · 3 min read".</summary>
    public string StatsText
    {
        get
        {
            var words = WordCount.ToString("N0", CultureInfo.CurrentCulture);
            var unit = WordCount == 1 ? "word" : "words";
            return $"{words} {unit} · {ReadMinutes} min read";
        }
    }

    /// <summary>The appearance the next Html build will use (kept current via <see cref="AppearanceChangedMessage"/>).</summary>
    public ViewerAppearance Appearance => _appearance;

    // ------------------------------------------------------------------ loading

    /// <summary>Loads (or re-targets this tab to) a Markdown file. Never throws; failures set <see cref="ErrorMessage"/>.</summary>
    public async Task LoadAsync(string path)
    {
        if (_disposed)
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ErrorMessage = $"\"{path}\" is not a valid file path.";
            return;
        }

        var isNewFile = !PathUtil.PathEquals(fullPath, FilePath);
        if (isNewFile)
        {
            FilePath = fullPath;
            Title = Path.GetFileName(fullPath);
            DocumentFolder = Path.GetDirectoryName(fullPath) ?? string.Empty;
            ZoomFactor = _settings.Current.ZoomByFile.TryGetValue(fullPath, out var zoom) ? zoom : DefaultZoom;
            CurrentHeadingId = null;
            CurrentSectionTitle = string.Empty;
            FindStatus = string.Empty;
            StartWatching(fullPath);
        }

        await LoadContentAsync(isLiveReload: false);
    }

    [RelayCommand]
    public Task ReloadAsync() =>
        string.IsNullOrEmpty(FilePath) ? Task.CompletedTask : LoadContentAsync(isLiveReload: false);

    private async Task LoadContentAsync(bool isLiveReload)
    {
        var version = ++_loadVersion;
        var path = FilePath;
        if (!isLiveReload)
        {
            IsLoading = true;
        }

        try
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(null, path);
            }

            var loaded = await _reader.ReadAsync(path);
            var result = await Task.Run(() => _renderer.Render(loaded.Text));

            if (version != _loadVersion || _disposed)
            {
                return;
            }

            ApplyDocument(loaded, result);
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            if (version != _loadVersion || _disposed)
            {
                return;
            }

            // Editors often hold the file locked mid-save; keep showing the last good render.
            if (isLiveReload && ex is IOException and not FileNotFoundException and not DirectoryNotFoundException)
            {
                return;
            }

            ErrorMessage = DescribeError(ex, path);
            if (!isLiveReload)
            {
                ClearDocument();
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private void ApplyDocument(LoadedText loaded, RenderResult result)
    {
        _lastResult = result;
        SourceText = loaded.Text;
        EncodingLabel = loaded.EncodingLabel;
        LineEndingLabel = loaded.LineEndingLabel;
        WordCount = result.WordCount;
        RebuildOutline(result.Outline);
        RebuildHtml();
    }

    private void ClearDocument()
    {
        _lastResult = null;
        SourceText = string.Empty;
        EncodingLabel = string.Empty;
        LineEndingLabel = string.Empty;
        WordCount = 0;
        Outline.Clear();
        Html = string.Empty;
    }

    private void RebuildOutline(IReadOnlyList<OutlineNode> nodes)
    {
        var expanded = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in AllOutlineItems())
        {
            expanded.TryAdd(item.AnchorId, item.IsExpanded);
        }

        Outline.Clear();
        foreach (var node in nodes)
        {
            Outline.Add(OutlineItemViewModel.FromNode(node, expanded.Count > 0 ? expanded : null));
        }

        MarkCurrent(CurrentHeadingId);
    }

    private void RebuildHtml()
    {
        if (_lastResult is null)
        {
            return;
        }

        Html = ShowSource
            ? _builder.BuildSource(SourceText, _appearance, Title)
            : _builder.Build(_lastResult, _appearance, Title);
    }

    partial void OnShowSourceChanged(bool value) => RebuildHtml();

    partial void OnZoomFactorChanged(double value)
    {
        var clamped = double.IsNaN(value) ? DefaultZoom : Math.Clamp(value, MinZoom, MaxZoom);
        if (clamped != value)
        {
            ZoomFactor = clamped;
        }
    }

    private static string DescribeError(Exception ex, string path)
    {
        var name = Path.GetFileName(path);
        return ex switch
        {
            FileNotFoundException or DirectoryNotFoundException => $"\"{name}\" could not be found. It may have been moved, renamed or deleted.",
            UnauthorizedAccessException => $"Access to \"{name}\" was denied.",
            IOException io => $"\"{name}\" could not be read: {io.Message}",
            _ => $"\"{name}\" could not be opened: {ex.Message}",
        };
    }

    // ------------------------------------------------------------------ live reload

    private void StartWatching(string path)
    {
        _watchHandle?.Dispose();
        _watchHandle = null;
        try
        {
            _watchHandle = _watcher.Watch(path, OnWatchedFileChanged);
        }
        catch (Exception)
        {
            // Live reload is best-effort (e.g. the folder does not exist or cannot be watched).
        }
    }

    private void OnWatchedFileChanged()
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.Enqueue(() =>
        {
            if (!_disposed && !string.IsNullOrEmpty(FilePath))
            {
                _ = LoadContentAsync(isLiveReload: true);
            }
        });
    }

    // ------------------------------------------------------------------ view callbacks

    /// <summary>The WebView finished loading the current <see cref="Html"/>.</summary>
    public void OnViewerReady()
    {
        if (_pendingAnchor is { Length: > 0 } anchor)
        {
            _pendingAnchor = null;
            ScrollTo(anchor);
        }

        if (IsFindOpen && !string.IsNullOrEmpty(FindQuery))
        {
            Messenger.Send(new FindMessage(Id, FindQuery, false));
        }
    }

    /// <summary>The heading at the top of the viewport changed (scroll spy).</summary>
    public void OnHeadingChanged(string anchorId)
    {
        if (string.Equals(anchorId, CurrentHeadingId, StringComparison.Ordinal))
        {
            return;
        }

        MarkCurrent(anchorId);
    }

    /// <summary>Find results reported by the viewer.</summary>
    public void OnFindResult(int current, int total)
    {
        if (string.IsNullOrEmpty(FindQuery))
        {
            FindStatus = string.Empty;
        }
        else if (total <= 0)
        {
            FindStatus = "No results";
        }
        else
        {
            FindStatus = $"{Math.Clamp(current, 1, total)} of {total}";
        }
    }

    /// <summary>A link was clicked in the reading surface.</summary>
    public void OnLinkActivated(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return;
        }

        href = href.Trim();
        if (href.StartsWith('#'))
        {
            var anchor = Uri.UnescapeDataString(href[1..]);
            if (anchor.Length > 0)
            {
                ScrollTo(anchor);
            }

            return;
        }

        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || uri.IsFile)
        {
            if (!Uri.TryCreate(DocumentBaseUri, href, out uri))
            {
                return;
            }
        }

        if (uri.Scheme == Uri.UriSchemeHttps && string.Equals(uri.Host, DocumentHost, StringComparison.OrdinalIgnoreCase))
        {
            HandleLocalLink(uri);
        }
        else if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto)
        {
            _ = OpenExternalAsync(uri);
        }
    }

    private void HandleLocalLink(Uri uri)
    {
        var relative = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        var fragment = uri.Fragment.Length > 1 ? Uri.UnescapeDataString(uri.Fragment[1..]) : null;

        if (relative.Length == 0)
        {
            if (fragment is { Length: > 0 })
            {
                ScrollTo(fragment);
            }

            return;
        }

        if (string.IsNullOrEmpty(DocumentFolder))
        {
            return;
        }

        string target;
        try
        {
            target = Path.GetFullPath(Path.Combine(DocumentFolder, relative.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (!PathUtil.IsMarkdownFile(target))
        {
            return; // only Markdown documents are navigated in-app
        }

        if (PathUtil.PathEquals(target, FilePath))
        {
            if (fragment is { Length: > 0 })
            {
                ScrollTo(fragment);
            }

            return;
        }

        _ = NavigateToFileAsync(target, fragment);
    }

    private async Task OpenExternalAsync(Uri uri)
    {
        try
        {
            await _launcher.OpenUriAsync(uri);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't open \"{uri}\": {ex.Message}";
        }
    }

    // ------------------------------------------------------------------ in-tab navigation

    private async Task NavigateToFileAsync(string path, string? anchor)
    {
        if (!string.IsNullOrEmpty(FilePath))
        {
            _backStack.Push(FilePath);
        }

        _forwardStack.Clear();
        UpdateNavigationState();
        _pendingAnchor = anchor;
        await LoadAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    public async Task GoBackAsync()
    {
        if (_backStack.Count == 0)
        {
            return;
        }

        _forwardStack.Push(FilePath);
        var target = _backStack.Pop();
        UpdateNavigationState();
        _pendingAnchor = null;
        await LoadAsync(target);
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    public async Task GoForwardAsync()
    {
        if (_forwardStack.Count == 0)
        {
            return;
        }

        _backStack.Push(FilePath);
        var target = _forwardStack.Pop();
        UpdateNavigationState();
        _pendingAnchor = null;
        await LoadAsync(target);
    }

    private void UpdateNavigationState()
    {
        CanGoBack = _backStack.Count > 0;
        CanGoForward = _forwardStack.Count > 0;
    }

    // ------------------------------------------------------------------ outline

    [RelayCommand]
    public void NavigateToHeading(OutlineItemViewModel? item)
    {
        if (item is null || string.IsNullOrEmpty(item.AnchorId))
        {
            return;
        }

        ScrollTo(item.AnchorId);
    }

    private void ScrollTo(string anchorId)
    {
        Messenger.Send(new ScrollToAnchorMessage(Id, anchorId));
        MarkCurrent(anchorId);
    }

    private void MarkCurrent(string? anchorId)
    {
        CurrentHeadingId = anchorId;
        OutlineItemViewModel? current = null;
        foreach (var item in AllOutlineItems())
        {
            var isCurrent = anchorId is not null && string.Equals(item.AnchorId, anchorId, StringComparison.Ordinal);
            item.IsCurrent = isCurrent;
            if (isCurrent)
            {
                current ??= item;
            }
        }

        CurrentSectionTitle = current?.Title ?? string.Empty;
    }

    private IEnumerable<OutlineItemViewModel> AllOutlineItems() => Outline.SelectMany(i => i.SelfAndDescendants());

    // ------------------------------------------------------------------ find

    partial void OnFindQueryChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            FindStatus = string.Empty;
            Messenger.Send(new ClearFindMessage(Id));
        }
        else
        {
            Messenger.Send(new FindMessage(Id, value, false));
        }
    }

    partial void OnIsFindOpenChanged(bool value)
    {
        if (!value)
        {
            FindStatus = string.Empty;
            Messenger.Send(new ClearFindMessage(Id));
        }
        else if (!string.IsNullOrEmpty(FindQuery))
        {
            Messenger.Send(new FindMessage(Id, FindQuery, false));
        }
    }

    [RelayCommand]
    public void FindNext() => SendFind(backwards: false);

    [RelayCommand]
    public void FindPrevious() => SendFind(backwards: true);

    [RelayCommand]
    public void CloseFind()
    {
        if (IsFindOpen)
        {
            IsFindOpen = false; // OnIsFindOpenChanged clears highlights
        }
        else
        {
            FindStatus = string.Empty;
            Messenger.Send(new ClearFindMessage(Id));
        }
    }

    private void SendFind(bool backwards)
    {
        if (!string.IsNullOrEmpty(FindQuery))
        {
            Messenger.Send(new FindMessage(Id, FindQuery, backwards));
        }
    }

    // ------------------------------------------------------------------ messaging / lifetime

    protected override void OnActivated()
    {
        Messenger.Register<DocumentTabViewModel, AppearanceChangedMessage>(
            this,
            static (recipient, message) => recipient._appearance = message.Appearance);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadVersion++;
        _watchHandle?.Dispose();
        _watchHandle = null;
        IsActive = false; // unregisters all messages
    }
}
