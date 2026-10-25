using CommunityToolkit.Mvvm.Messaging;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.ViewModels;

/// <summary>
/// Singleton that owns the current <see cref="ViewerAppearance"/>. Recomputes it when settings or the
/// effective theme change, applies theme/accent to the app shell and broadcasts <see cref="AppearanceChangedMessage"/>.
/// </summary>
public sealed class AppearanceProvider : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IAccentService _accents;
    private readonly object _gate = new();

    private AppTheme? _appliedTheme;
    private string? _appliedAccentHex;
    private bool _isApplying;

    public AppearanceProvider(ISettingsService settings, IThemeService theme, IAccentService accents)
    {
        _settings = settings;
        _theme = theme;
        _accents = accents;

        Current = Compute();

        _settings.Changed += OnSettingsChanged;
        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;
    }

    /// <summary>The appearance the reading surface should use right now.</summary>
    public ViewerAppearance Current { get; private set; }

    /// <summary>The resolved accent colour ("#RRGGBB") for the current settings.</summary>
    public string CurrentAccentHex => ResolveAccentHex(_settings.Current, _theme.WindowsAccentHex);

    /// <summary>Applies the persisted theme + accent to the shell (call once at startup; safe to call again).</summary>
    public void ApplyTheme()
    {
        _appliedTheme = null;
        _appliedAccentHex = null;
        ApplyThemeIfChanged(_settings.Current);
        Refresh();
    }

    /// <summary>Recomputes <see cref="Current"/> and broadcasts it if it changed.</summary>
    public void Refresh()
    {
        ViewerAppearance? changed = null;
        lock (_gate)
        {
            var next = Compute();
            if (!Equals(next, Current))
            {
                Current = next;
                changed = next;
            }
        }

        if (changed is not null)
        {
            WeakReferenceMessenger.Default.Send(new AppearanceChangedMessage(changed));
        }
    }

    /// <summary>Custom hex if valid, else the Windows accent (when enabled and available), else the palette entry, else Blue.</summary>
    public static string ResolveAccentHex(AppSettings settings, string? windowsAccentHex)
    {
        if (HexColor.TryNormalize(settings.CustomAccentHex, out var custom))
        {
            return custom;
        }

        if (settings.UseWindowsAccent && HexColor.TryNormalize(windowsAccentHex, out var windows))
        {
            return windows;
        }

        return ResolvePaletteAccent(settings.AccentName).Hex;
    }

    /// <summary>Palette entry by name (case-insensitive), falling back to the default Blue.</summary>
    public static AccentColor ResolvePaletteAccent(string? name) =>
        AccentPalette.All.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? AccentPalette.All.First(a => a.Name == AccentPalette.DefaultName);

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;
    }

    private ViewerAppearance Compute()
    {
        var s = _settings.Current;
        var isDark = _theme.IsDark;
        var accent = _accents.Compute(ResolveAccentHex(s, _theme.WindowsAccentHex), isDark);
        return new ViewerAppearance(isDark, accent, s.Density, s.Columns, s.WrapCodeLines, s.LoadRemoteContent);
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        ApplyThemeIfChanged(settings);
        Refresh();
    }

    private void OnEffectiveThemeChanged(object? sender, EventArgs e)
    {
        // A system accent change also arrives here; re-apply when the resolved accent moved.
        if (!_isApplying)
        {
            ApplyThemeIfChanged(_settings.Current);
        }

        Refresh();
    }

    private void ApplyThemeIfChanged(AppSettings settings)
    {
        var theme = settings.Theme;
        var accentHex = ResolveAccentHex(settings, _theme.WindowsAccentHex);
        if (_appliedTheme == theme && string.Equals(_appliedAccentHex, accentHex, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _appliedTheme = theme;
        _appliedAccentHex = accentHex;
        _isApplying = true;
        try
        {
            _theme.Apply(theme, accentHex);
        }
        finally
        {
            _isApplying = false;
        }
    }
}
