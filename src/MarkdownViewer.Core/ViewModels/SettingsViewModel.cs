using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.ViewModels;

/// <summary>Settings page. Every two-way property writes straight back through <see cref="ISettingsService.Update"/>.</summary>
public sealed partial class SettingsViewModel : ObservableRecipient
{
    private const string DefaultAppName = "Markdown Viewer - Simple";

    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IAccentService _accents;
    private readonly IDefaultAppService _defaultApp;
    private readonly IDialogService _dialogs;

    private bool _isSyncing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLightSelected), nameof(IsDarkSelected), nameof(IsSystemSelected))]
    private AppTheme _selectedTheme;

    [ObservableProperty]
    private bool _useWindowsAccent;

    [ObservableProperty]
    private string _customAccentHex = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomAccentError))]
    private string? _customAccentError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComfortable), nameof(IsCompact), nameof(IsUltra))]
    private DensityPreset _density;

    [ObservableProperty]
    private ColumnMode _columnMode;

    [ObservableProperty]
    private bool _wrapCodeLines;

    [ObservableProperty]
    private bool _loadRemoteContent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefaultAppStatus))]
    private bool _isDefaultApp;

    [ObservableProperty]
    private AccentShades _previewShades = null!; // assigned in the constructor

    public SettingsViewModel(
        ISettingsService settings,
        IThemeService theme,
        IAccentService accents,
        IDefaultAppService defaultApp,
        IDialogService dialogs)
    {
        _settings = settings;
        _theme = theme;
        _accents = accents;
        _defaultApp = defaultApp;
        _dialogs = dialogs;

        AccentOptions = new ObservableCollection<AccentOptionViewModel>(AccentPalette.All.Select(a => new AccentOptionViewModel(a)));

        SyncFromSettings(_settings.Current);
        RefreshDefaultAppStatus();

        _settings.Changed += OnSettingsChanged;
        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;

        IsActive = true;
    }

    // ------------------------------------------------------------------ theme

    public bool IsLightSelected
    {
        get => SelectedTheme == AppTheme.Light;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.Light;
            }
        }
    }

    public bool IsDarkSelected
    {
        get => SelectedTheme == AppTheme.Dark;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.Dark;
            }
        }
    }

    public bool IsSystemSelected
    {
        get => SelectedTheme == AppTheme.System;
        set
        {
            if (value)
            {
                SelectedTheme = AppTheme.System;
            }
        }
    }

    partial void OnSelectedThemeChanged(AppTheme value) => Write(s => s.Theme = value);

    // ------------------------------------------------------------------ accent

    public ObservableCollection<AccentOptionViewModel> AccentOptions { get; }

    public bool IsWindowsAccentAvailable => HexColor.TryNormalize(_theme.WindowsAccentHex, out _);

    public bool HasCustomAccentError => !string.IsNullOrEmpty(CustomAccentError);

    partial void OnUseWindowsAccentChanged(bool value) => Write(s =>
    {
        s.UseWindowsAccent = value;
        if (value)
        {
            s.CustomAccentHex = null; // custom beats Windows accent, so clear it
        }
    });

    [RelayCommand]
    private void SelectAccent(AccentOptionViewModel? option)
    {
        if (option is null)
        {
            return;
        }

        CustomAccentError = null;
        _settings.Update(s =>
        {
            s.AccentName = option.Name;
            s.UseWindowsAccent = false;
            s.CustomAccentHex = null;
        });
    }

    [RelayCommand]
    private void ApplyCustomAccent(string? hex)
    {
        var input = string.IsNullOrWhiteSpace(hex) ? CustomAccentHex : hex;
        if (!HexColor.TryNormalize(input, out var normalized))
        {
            CustomAccentError = "Enter a colour as 6 hex digits, for example #546DE5.";
            return;
        }

        CustomAccentError = null;
        CustomAccentHex = normalized;
        _settings.Update(s =>
        {
            s.CustomAccentHex = normalized;
            s.UseWindowsAccent = false;
        });
    }

    // ------------------------------------------------------------------ reading

    public bool IsComfortable
    {
        get => Density == DensityPreset.Comfortable;
        set
        {
            if (value)
            {
                Density = DensityPreset.Comfortable;
            }
        }
    }

    public bool IsCompact
    {
        get => Density == DensityPreset.Compact;
        set
        {
            if (value)
            {
                Density = DensityPreset.Compact;
            }
        }
    }

    public bool IsUltra
    {
        get => Density == DensityPreset.Ultra;
        set
        {
            if (value)
            {
                Density = DensityPreset.Ultra;
            }
        }
    }

    public IReadOnlyList<ColumnMode> ColumnModes { get; } = Enum.GetValues<ColumnMode>();

    partial void OnDensityChanged(DensityPreset value) => Write(s => s.Density = value);

    partial void OnColumnModeChanged(ColumnMode value) => Write(s => s.Columns = value);

    partial void OnWrapCodeLinesChanged(bool value) => Write(s => s.WrapCodeLines = value);

    partial void OnLoadRemoteContentChanged(bool value) => Write(s => s.LoadRemoteContent = value);

    // ------------------------------------------------------------------ default app

    public string DefaultAppStatus => IsDefaultApp
        ? $"{DefaultAppName} is your default for .md files"
        : $"{DefaultAppName} is not the default for .md yet";

    /// <summary>Re-checks the file association (call when the page is shown or the window is re-activated).</summary>
    public void RefreshDefaultAppStatus()
    {
        try
        {
            IsDefaultApp = _defaultApp.IsDefaultForMarkdown();
        }
        catch (Exception)
        {
            IsDefaultApp = false;
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

        RefreshDefaultAppStatus();
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
    private void ResetToDefaults()
    {
        var d = new AppSettings();
        CustomAccentError = null;
        _settings.Update(s =>
        {
            s.Theme = d.Theme;
            s.AccentName = d.AccentName;
            s.UseWindowsAccent = d.UseWindowsAccent;
            s.CustomAccentHex = d.CustomAccentHex;
            s.Density = d.Density;
            s.Columns = d.Columns;
            s.WrapCodeLines = d.WrapCodeLines;
            s.LoadRemoteContent = d.LoadRemoteContent;
        });
    }

    // ------------------------------------------------------------------ plumbing

    private void Write(Action<AppSettings> change)
    {
        if (!_isSyncing)
        {
            _settings.Update(change);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => SyncFromSettings(settings);

    private void OnEffectiveThemeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsWindowsAccentAvailable));
        UpdateAccentState(_settings.Current);
    }

    private void SyncFromSettings(AppSettings s)
    {
        _isSyncing = true;
        try
        {
            SelectedTheme = s.Theme;
            UseWindowsAccent = s.UseWindowsAccent;
            CustomAccentHex = s.CustomAccentHex ?? string.Empty;
            Density = s.Density;
            ColumnMode = s.Columns;
            WrapCodeLines = s.WrapCodeLines;
            LoadRemoteContent = s.LoadRemoteContent;
        }
        finally
        {
            _isSyncing = false;
        }

        UpdateAccentState(s);
    }

    private void UpdateAccentState(AppSettings s)
    {
        var windowsAccent = _theme.WindowsAccentHex;
        var paletteActive = !HexColor.TryNormalize(s.CustomAccentHex, out _)
            && !(s.UseWindowsAccent && HexColor.TryNormalize(windowsAccent, out _));
        var selectedName = AppearanceProvider.ResolvePaletteAccent(s.AccentName).Name;

        foreach (var option in AccentOptions)
        {
            option.IsSelected = paletteActive && string.Equals(option.Name, selectedName, StringComparison.OrdinalIgnoreCase);
        }

        PreviewShades = _accents.Compute(AppearanceProvider.ResolveAccentHex(s, windowsAccent), _theme.IsDark);
    }

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
