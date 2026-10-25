using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.ViewModels;

/// <summary>Shell view model: tabs, command bar, title bar and global commands.</summary>
public sealed partial class MainViewModel : ObservableRecipient
{
    public const int MaxRecentFiles = 15;
    public const int MaxFolderFiles = 20;

    public static IReadOnlyList<double> ZoomSteps { get; } =
        new[] { 0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 };

    private const double ZoomEpsilon = 0.001;

    private readonly IDocumentTabFactory _tabFactory;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly AppearanceProvider _appearance;
    private readonly IFilePickerService _filePicker;
    private readonly IFolderScanner _folderScanner;
    private readonly IDefaultAppService _defaultApp;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly ILauncherService _launcher;
    private readonly IDispatcherService _dispatcher;

    private readonly Stack<string> _closedTabs = new();
    private bool _isSyncingSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTab), nameof(ZoomLabel), nameof(IsFindOpen),
        nameof(CanSelectedTabGoBack), nameof(CanSelectedTabGoForward), nameof(SelectedFilePath))]
    private DocumentTabViewModel? _selectedTab;

    [ObservableProperty]
    private bool _isBannerVisible;

    [ObservableProperty]
    private bool _isOutlinePaneOpen = true;

    [ObservableProperty]
    private bool _isFocusMode;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocumentVisible), nameof(IsStartScreenVisible))]
    private bool _isSettingsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDocumentVisible), nameof(IsStartScreenVisible))]
    private bool _isHomeOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DensityLabel))]
    private DensityPreset _density;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColumnsLabel))]
    private ColumnMode _columnMode;

    public MainViewModel(
        IDocumentTabFactory tabFactory,
        ISettingsService settings,
        IThemeService theme,
        AppearanceProvider appearance,
        IFilePickerService filePicker,
        IFolderScanner folderScanner,
        IDefaultAppService defaultApp,
        IDialogService dialogs,
        IClipboardService clipboard,
        ILauncherService launcher,
        IDispatcherService dispatcher)
    {
        _tabFactory = tabFactory;
        _settings = settings;
        _theme = theme;
        _appearance = appearance;
        _filePicker = filePicker;
        _folderScanner = folderScanner;
        _defaultApp = defaultApp;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _launcher = launcher;
        _dispatcher = dispatcher;

        SyncFromSettings(_settings.Current);
        IsDarkTheme = _theme.IsDark;

        Tabs.CollectionChanged += OnTabsCollectionChanged;
        _settings.Changed += OnSettingsChanged;
        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;

        IsActive = true;
    }

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = new();

    public bool HasTabs => Tabs.Count > 0;

    public bool HasSelectedTab => SelectedTab is not null;

    /// <summary>The reading surface: a tab is open and neither Settings nor the start screen is showing.</summary>
    public bool IsDocumentVisible => HasTabs && !IsSettingsOpen && !IsHomeOpen;

    /// <summary>The "open a file" start screen: shown when no tab is open, and whenever Home is pressed.</summary>
    public bool IsStartScreenVisible => !IsSettingsOpen && (!HasTabs || IsHomeOpen);

    public bool HasMultipleTabs => Tabs.Count > 1;

    public bool CanReopenClosedTab => _closedTabs.Count > 0;

    public bool CanSelectedTabGoBack => SelectedTab?.CanGoBack == true;

    public bool CanSelectedTabGoForward => SelectedTab?.CanGoForward == true;

    /// <summary>Full path of the selected document (breadcrumb), or empty.</summary>
    public string SelectedFilePath => SelectedTab?.FilePath ?? string.Empty;

    public string DensityLabel => Density switch
    {
        DensityPreset.Comfortable => "Comfortable",
        DensityPreset.Compact => "Compact",
        DensityPreset.Ultra => "Ultra",
        _ => Density.ToString(),
    };

    public string ColumnsLabel => ColumnMode switch
    {
        ColumnMode.Auto => "Auto columns",
        ColumnMode.One => "1 column",
        ColumnMode.Two => "2 columns",
        _ => ColumnMode.ToString(),
    };

    /// <summary>e.g. "100%" — follows the selected tab.</summary>
    public string ZoomLabel => $"{Math.Round((SelectedTab?.ZoomFactor ?? DocumentTabViewModel.DefaultZoom) * 100):0}%";

    /// <summary>Proxy to <see cref="DocumentTabViewModel.IsFindOpen"/> of the selected tab.</summary>
    public bool IsFindOpen
    {
        get => SelectedTab?.IsFindOpen ?? false;
        set
        {
            if (SelectedTab is { } tab && tab.IsFindOpen != value)
            {
                tab.IsFindOpen = value; // raises IsFindOpen via the tab's PropertyChanged
            }
        }
    }

    // ------------------------------------------------------------------ startup / opening

    public Task InitializeAsync()
    {
        _appearance.ApplyTheme();
        IsDarkTheme = _theme.IsDark;
        RefreshBanner();
        return Task.CompletedTask;
    }

    /// <summary>Re-evaluates whether the "make default app" banner should show (e.g. after the window is re-activated).</summary>
    public void RefreshBanner()
    {
        bool isDefault;
        try
        {
            isDefault = _defaultApp.IsDefaultForMarkdown();
        }
        catch (Exception)
        {
            isDefault = true; // unknown: don't nag
        }

        IsBannerVisible = !_settings.Current.DefaultAppBannerDismissed && !isDefault;
    }

    /// <summary>Opens files (activation, drag-drop, command line, pickers). Folders are expanded to their Markdown files.</summary>
    public async Task OpenPathsAsync(IEnumerable<string> paths)
    {
        IsHomeOpen = false;

        try
        {
            foreach (var path in ExpandPaths(paths))
            {
                var existing = Tabs.FirstOrDefault(t => PathUtil.PathEquals(t.FilePath, path));
                if (existing is not null)
                {
                    SelectedTab = existing;
                    AddRecentFile(path);
                    continue;
                }

                var tab = _tabFactory.Create();
                Tabs.Add(tab);
                SelectedTab = tab;
                await tab.LoadAsync(path);
                AddRecentFile(path);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't open file", ex.Message);
        }
    }

    private IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim().Trim('"'));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (File.Exists(full))
            {
                yield return full;
            }
            else if (Directory.Exists(full))
            {
                foreach (var file in _folderScanner.FindMarkdownFiles(full).Take(MaxFolderFiles))
                {
                    yield return file;
                }
            }
        }
    }

    private void AddRecentFile(string path)
    {
        _settings.Update(s =>
        {
            s.RecentFiles.RemoveAll(p => PathUtil.PathEquals(p, path));
            s.RecentFiles.Insert(0, path);
            if (s.RecentFiles.Count > MaxRecentFiles)
            {
                s.RecentFiles.RemoveRange(MaxRecentFiles, s.RecentFiles.Count - MaxRecentFiles);
            }
        });
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        try
        {
            var files = await _filePicker.PickMarkdownFilesAsync();
            if (files.Count > 0)
            {
                await OpenPathsAsync(files);
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't open file", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        try
        {
            var folder = await _filePicker.PickFolderAsync();
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            var files = _folderScanner.FindMarkdownFiles(folder);
            if (files.Count == 0)
            {
                await ShowErrorAsync("No Markdown files", $"No Markdown files were found in \"{folder}\".");
                return;
            }

            await OpenPathsAsync(files.Take(MaxFolderFiles));
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't open folder", ex.Message);
        }
    }

    // ------------------------------------------------------------------ tabs

    [RelayCommand]
    private void CloseTab(DocumentTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        if (ReferenceEquals(SelectedTab, tab))
        {
            // Pick the neighbour before removing so a TwoWay-bound TabView doesn't null the selection first.
            SelectedTab = Tabs.Count > 1 ? Tabs[index == Tabs.Count - 1 ? index - 1 : index + 1] : null;
        }

        Tabs.Remove(tab);

        if (!string.IsNullOrEmpty(tab.FilePath))
        {
            _closedTabs.Push(tab.FilePath);
            OnPropertyChanged(nameof(CanReopenClosedTab));
            ReopenClosedTabCommand.NotifyCanExecuteChanged();
        }

        tab.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanReopenClosedTab))]
    private async Task ReopenClosedTabAsync()
    {
        while (_closedTabs.Count > 0)
        {
            var path = _closedTabs.Pop();
            if (File.Exists(path))
            {
                await OpenPathsAsync(new[] { path });
                break;
            }
        }

        OnPropertyChanged(nameof(CanReopenClosedTab));
        ReopenClosedTabCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasMultipleTabs))]
    private void NextTab() => MoveSelection(+1);

    [RelayCommand(CanExecute = nameof(HasMultipleTabs))]
    private void PreviousTab() => MoveSelection(-1);

    private void MoveSelection(int delta)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        var index = SelectedTab is null ? -1 : Tabs.IndexOf(SelectedTab);
        var next = ((index + delta) % Tabs.Count + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[next];
    }

    private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(HasMultipleTabs));
        OnPropertyChanged(nameof(IsDocumentVisible));
        OnPropertyChanged(nameof(IsStartScreenVisible));
        NextTabCommand.NotifyCanExecuteChanged();
        PreviousTabCommand.NotifyCanExecuteChanged();

        if (SelectedTab is not null && !Tabs.Contains(SelectedTab))
        {
            SelectedTab = Tabs.FirstOrDefault();
        }
    }

    partial void OnSelectedTabChanged(DocumentTabViewModel? oldValue, DocumentTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedTabPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedTabPropertyChanged;
            IsHomeOpen = false;
        }

        RefreshTabCommandStates();
    }

    private void OnSelectedTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DocumentTabViewModel.ZoomFactor):
                OnPropertyChanged(nameof(ZoomLabel));
                break;
            case nameof(DocumentTabViewModel.IsFindOpen):
                OnPropertyChanged(nameof(IsFindOpen));
                break;
            case nameof(DocumentTabViewModel.FilePath):
                OnPropertyChanged(nameof(SelectedFilePath));
                break;
            case nameof(DocumentTabViewModel.CanGoBack):
                OnPropertyChanged(nameof(CanSelectedTabGoBack));
                GoBackCommand.NotifyCanExecuteChanged();
                break;
            case nameof(DocumentTabViewModel.CanGoForward):
                OnPropertyChanged(nameof(CanSelectedTabGoForward));
                GoForwardCommand.NotifyCanExecuteChanged();
                break;
        }
    }

    private void RefreshTabCommandStates()
    {
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        ResetZoomCommand.NotifyCanExecuteChanged();
        OpenFindCommand.NotifyCanExecuteChanged();
        CloseFindCommand.NotifyCanExecuteChanged();
        FindNextCommand.NotifyCanExecuteChanged();
        FindPreviousCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged();
        ExportPdfCommand.NotifyCanExecuteChanged();
        ToggleSourceCommand.NotifyCanExecuteChanged();
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        CollapseAllCommand.NotifyCanExecuteChanged();
        ExpandAllCommand.NotifyCanExecuteChanged();
        CopyFilePathCommand.NotifyCanExecuteChanged();
        EditExternallyCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------ theme / layout

    [RelayCommand]
    private void ToggleTheme()
    {
        var target = _theme.IsDark ? AppTheme.Light : AppTheme.Dark;
        _settings.Update(s => s.Theme = target); // AppearanceProvider applies it
    }

    [RelayCommand]
    private void SetDensity(object? density)
    {
        if (EnumParameter.TryParse<DensityPreset>(density, out var value))
        {
            Density = value; // persisted in OnDensityChanged
        }
    }

    [RelayCommand]
    private void SetColumns(object? columns)
    {
        if (EnumParameter.TryParse<ColumnMode>(columns, out var value))
        {
            ColumnMode = value; // persisted in OnColumnModeChanged
        }
    }

    partial void OnDensityChanged(DensityPreset value) => Persist(s => s.Density = value);

    partial void OnColumnModeChanged(ColumnMode value) => Persist(s => s.Columns = value);

    partial void OnIsOutlinePaneOpenChanged(bool value) => Persist(s => s.IsOutlinePaneOpen = value);

    [RelayCommand]
    private void ToggleOutline() => IsOutlinePaneOpen = !IsOutlinePaneOpen;

    [RelayCommand]
    private void ToggleFocusMode() => IsFocusMode = !IsFocusMode;

    /// <summary>
    /// A key the reading surface forwarded (viewer.js): XAML KeyboardAccelerators never fire while the
    /// WebView has focus, so focus mode and Esc would otherwise be unreachable from inside a document.
    /// </summary>
    public void OnShellKey(string key)
    {
        switch (key)
        {
            case "F11":
                ToggleFocusMode();
                break;
            case "Escape":
                Escape();
                break;
        }
    }

    // ------------------------------------------------------------------ zoom

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void ZoomIn()
    {
        if (SelectedTab is { } tab)
        {
            var next = ZoomSteps.FirstOrDefault(z => z > tab.ZoomFactor + ZoomEpsilon, ZoomSteps[^1]);
            SetZoom(tab, next);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void ZoomOut()
    {
        if (SelectedTab is { } tab)
        {
            var next = ZoomSteps.LastOrDefault(z => z < tab.ZoomFactor - ZoomEpsilon, ZoomSteps[0]);
            SetZoom(tab, next);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void ResetZoom()
    {
        if (SelectedTab is { } tab)
        {
            SetZoom(tab, DocumentTabViewModel.DefaultZoom);
        }
    }

    private void SetZoom(DocumentTabViewModel tab, double zoom)
    {
        tab.ZoomFactor = zoom;
        var path = tab.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var stored = tab.ZoomFactor; // clamped
        _settings.Update(s =>
        {
            if (Math.Abs(stored - DocumentTabViewModel.DefaultZoom) < ZoomEpsilon)
            {
                s.ZoomByFile.Remove(path);
            }
            else
            {
                s.ZoomByFile[path] = stored;
            }
        });
    }

    // ------------------------------------------------------------------ find

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void OpenFind()
    {
        if (SelectedTab is { } tab)
        {
            tab.IsFindOpen = true;
            Messenger.Send(new FocusFindBoxMessage(tab.Id));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void CloseFind() => SelectedTab?.CloseFind();

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void FindNext() => SelectedTab?.FindNext();

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void FindPrevious() => SelectedTab?.FindPrevious();

    // ------------------------------------------------------------------ document actions

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void Print()
    {
        if (SelectedTab is { } tab)
        {
            Messenger.Send(new PrintMessage(tab.Id));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private async Task ExportPdfAsync()
    {
        if (SelectedTab is not { } tab)
        {
            return;
        }

        try
        {
            var name = string.IsNullOrEmpty(tab.FilePath)
                ? "Document.pdf"
                : Path.ChangeExtension(Path.GetFileName(tab.FilePath), ".pdf");
            var target = await _filePicker.PickPdfSaveLocationAsync(name);
            if (!string.IsNullOrEmpty(target))
            {
                Messenger.Send(new ExportPdfMessage(tab.Id, target));
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't export PDF", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void ToggleSource()
    {
        if (SelectedTab is { } tab)
        {
            tab.ShowSource = !tab.ShowSource;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSelectedTabGoBack))]
    private Task GoBackAsync() => SelectedTab?.GoBackAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanSelectedTabGoForward))]
    private Task GoForwardAsync() => SelectedTab?.GoForwardAsync() ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void CollapseAll()
    {
        if (SelectedTab is { } tab)
        {
            Messenger.Send(new CollapseSectionsMessage(tab.Id, true));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void ExpandAll()
    {
        if (SelectedTab is { } tab)
        {
            Messenger.Send(new CollapseSectionsMessage(tab.Id, false));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void CopyFilePath()
    {
        if (SelectedTab is { FilePath.Length: > 0 } tab)
        {
            _clipboard.SetText(tab.FilePath);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private async Task EditExternallyAsync()
    {
        if (SelectedTab is not { FilePath.Length: > 0 } tab)
        {
            return;
        }

        try
        {
            await _launcher.OpenWithDefaultEditorAsync(tab.FilePath);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't open editor", ex.Message);
        }
    }

    // ------------------------------------------------------------------ settings / dialogs / banner

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    /// <summary>Returns from the start screen to the open document (clicking the already-selected tab raises no SelectionChanged).</summary>
    public void LeaveStartScreen()
    {
        if (HasTabs)
        {
            IsHomeOpen = false;
        }
    }

    /// <summary>Home: leaves Settings and focus mode and shows the start screen, whatever is open.</summary>
    [RelayCommand]
    private void GoHome()
    {
        IsSettingsOpen = false;
        IsFocusMode = false;
        IsHomeOpen = true;
    }

    /// <summary>Esc: closes whatever is on top — find, then Settings, focus mode and the start screen.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (IsFindOpen)
        {
            SelectedTab?.CloseFind();
        }
        else if (IsSettingsOpen)
        {
            IsSettingsOpen = false;
        }
        else if (IsFocusMode)
        {
            IsFocusMode = false;
        }
        else if (IsHomeOpen && HasTabs)
        {
            IsHomeOpen = false;
        }
    }

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    partial void OnIsSettingsOpenChanged(bool value) => Messenger.Send(new SettingsVisibilityMessage(value));

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        try
        {
            await _dialogs.ShowAboutAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't show About", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ShowShortcutsAsync()
    {
        try
        {
            await _dialogs.ShowShortcutsAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't show shortcuts", ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenDefaultAppsAsync()
    {
        try
        {
            await _defaultApp.OpenDefaultAppsSettingsAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Couldn't open Default apps", ex.Message);
        }
    }

    [RelayCommand]
    private void DismissBanner()
    {
        IsBannerVisible = false;
        _settings.Update(s => s.DefaultAppBannerDismissed = true);
    }

    // ------------------------------------------------------------------ plumbing

    private void Persist(Action<AppSettings> change)
    {
        if (!_isSyncingSettings)
        {
            _settings.Update(change);
        }
    }

    private void SyncFromSettings(AppSettings settings)
    {
        _isSyncingSettings = true;
        try
        {
            Density = settings.Density;
            ColumnMode = settings.Columns;
            IsOutlinePaneOpen = settings.IsOutlinePaneOpen;
        }
        finally
        {
            _isSyncingSettings = false;
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) =>
        _dispatcher.Enqueue(() => SyncFromSettings(settings));

    private void OnEffectiveThemeChanged(object? sender, EventArgs e) =>
        _dispatcher.Enqueue(() => IsDarkTheme = _theme.IsDark);

    private async Task ShowErrorAsync(string title, string message)
    {
        try
        {
            await _dialogs.ShowErrorAsync(title, message);
        }
        catch (Exception)
        {
            // Nothing more we can do if the dialog itself fails.
        }
    }
}
