using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace MarkdownViewer.App.Services;

/// <summary>
/// Applies Light / Dark / System theme to the window root and pushes the accent into XAML resources.
/// Accent brushes live in Themes/Accent.xaml (theme dictionaries); their Color is mutated in place so
/// every control re-renders, then RequestedTheme is toggled to re-resolve SystemAccentColor* lookups.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    private enum Role { Fill, Text, OnFill, Tint, Mark }

    // Must match Themes/Accent.xaml.
    private static readonly (string Key, Role Role, double Opacity)[] BrushMap =
    {
        ("AccentFillColorDefaultBrush", Role.Fill, 1.0),
        ("AccentFillColorSecondaryBrush", Role.Fill, 0.9),
        ("AccentFillColorTertiaryBrush", Role.Fill, 0.8),
        ("AccentFillColorSelectedTextBackgroundBrush", Role.Fill, 1.0),
        ("AccentTextFillColorPrimaryBrush", Role.Text, 1.0),
        ("AccentTextFillColorSecondaryBrush", Role.Text, 1.0),
        ("AccentTextFillColorTertiaryBrush", Role.Fill, 1.0),
        ("TextOnAccentFillColorPrimaryBrush", Role.OnFill, 1.0),
        ("TextOnAccentFillColorSecondaryBrush", Role.OnFill, 0.7),
        ("SystemControlHighlightAccentBrush", Role.Fill, 1.0),
        ("SystemControlForegroundAccentBrush", Role.Text, 1.0),
        ("AccentButtonBackground", Role.Fill, 1.0),
        ("AccentButtonBackgroundPointerOver", Role.Fill, 0.9),
        ("AccentButtonBackgroundPressed", Role.Fill, 0.8),
        ("AccentButtonForeground", Role.OnFill, 1.0),
        ("AccentButtonForegroundPointerOver", Role.OnFill, 1.0),
        ("AccentButtonForegroundPressed", Role.OnFill, 0.7),
        ("ToggleButtonBackgroundChecked", Role.Fill, 1.0),
        ("ToggleButtonBackgroundCheckedPointerOver", Role.Fill, 0.9),
        ("ToggleButtonBackgroundCheckedPressed", Role.Fill, 0.8),
        ("ToggleButtonForegroundChecked", Role.OnFill, 1.0),
        ("ToggleButtonForegroundCheckedPointerOver", Role.OnFill, 1.0),
        ("ToggleButtonForegroundCheckedPressed", Role.OnFill, 0.7),
        ("AppBarToggleButtonBackgroundChecked", Role.Tint, 1.0),
        ("AppBarToggleButtonBackgroundCheckedPointerOver", Role.Tint, 1.0),
        ("AppBarToggleButtonBackgroundCheckedPressed", Role.Tint, 1.0),
        ("AppBarToggleButtonForegroundChecked", Role.Text, 1.0),
        ("AppBarToggleButtonForegroundCheckedPointerOver", Role.Text, 1.0),
        ("AppBarToggleButtonForegroundCheckedPressed", Role.Text, 1.0),
        ("MdvAccentFillBrush", Role.Fill, 1.0),
        ("MdvAccentTextBrush", Role.Text, 1.0),
        ("MdvAccentTintBrush", Role.Tint, 1.0),
        ("MdvAccentMarkBrush", Role.Mark, 1.0),
    };

    private static readonly Color White = ColorUtil.Opaque(0xFF, 0xFF, 0xFF);
    private static readonly Color Black = ColorUtil.Opaque(0x00, 0x00, 0x00);

    private readonly IWindowProvider _windows;
    private readonly IAccentService _accents;
    private readonly UISettings _uiSettings = new();
    private DispatcherQueue? _queue;
    private AppTheme _theme;
    private string _accentHex;
    private bool _isDark;
    private string? _windowsAccentHex;
    private bool _disposed;

    public ThemeService(IWindowProvider windows, IAccentService accents, ISettingsService settings)
    {
        _windows = windows;
        _accents = accents;
        _windowsAccentHex = ReadWindowsAccent();
        _theme = settings.Current.Theme;
        _accentHex = ResolveStartupAccent(settings.Current);
        _isDark = ComputeIsDark(_theme);
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public event EventHandler? EffectiveThemeChanged;

    public bool IsDark => _isDark;

    public string? WindowsAccentHex => _windowsAccentHex;

    /// <summary>Called once the main window exists; applies the persisted theme before first paint.</summary>
    public void Attach()
    {
        _queue = DispatcherQueue.GetForCurrentThread();
        ApplyCore(raise: false);
    }

    public void Apply(AppTheme theme, string accentHex)
    {
        _theme = theme;
        if (ColorUtil.TryParse(accentHex, out var parsed))
        {
            _accentHex = ColorUtil.ToHex(parsed);
        }

        RunOnUi(() => ApplyCore(raise: true));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
    }

    private void ApplyCore(bool raise)
    {
        var wasDark = _isDark;
        _isDark = ComputeIsDark(_theme);

        ApplyAccentResources();

        if (_windows.RootElement is { } root)
        {
            var target = _theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };

            // Flip to the opposite effective theme and back: forces {ThemeResource} re-evaluation so
            // resources derived from SystemAccentColor* pick up the new values.
            root.RequestedTheme = _isDark ? ElementTheme.Light : ElementTheme.Dark;
            root.RequestedTheme = target;
        }

        ApplyCaptionButtonColors();

        if (raise && wasDark != _isDark)
        {
            EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyAccentResources()
    {
        var baseColor = ColorUtil.Parse(_accentHex, ColorUtil.Opaque(0x54, 0x6D, 0xE5));
        var light = _accents.Compute(_accentHex, isDark: false);
        var dark = _accents.Compute(_accentHex, isDark: true);

        var resources = Application.Current.Resources;
        var lightFill = ColorUtil.Parse(light.Fill, baseColor);
        var lightText = ColorUtil.Parse(light.Text, baseColor);
        var darkFill = ColorUtil.Parse(dark.Fill, baseColor);
        var darkText = ColorUtil.Parse(dark.Text, baseColor);

        // WinUI maps light-theme accent fill to Dark1 / text to Dark2, dark-theme fill to Light2 / text to Light3.
        resources["SystemAccentColor"] = baseColor;
        resources["SystemAccentColorDark1"] = lightFill;
        resources["SystemAccentColorDark2"] = lightText;
        resources["SystemAccentColorDark3"] = ColorUtil.Mix(lightText, Black, 0.2);
        resources["SystemAccentColorLight1"] = ColorUtil.Mix(baseColor, White, 0.25);
        resources["SystemAccentColorLight2"] = darkFill;
        resources["SystemAccentColorLight3"] = darkText;

        var accentDictionary = FindAccentDictionary(resources);
        if (accentDictionary is null)
        {
            return;
        }

        UpdateThemeDictionary(accentDictionary, "Light", light, baseColor);
        UpdateThemeDictionary(accentDictionary, "Dark", dark, baseColor);
    }

    private static void UpdateThemeDictionary(ResourceDictionary accentDictionary, string themeKey, AccentShades shades, Color fallback)
    {
        if (!accentDictionary.ThemeDictionaries.TryGetValue(themeKey, out var value) || value is not ResourceDictionary theme)
        {
            return;
        }

        foreach (var (key, role, opacity) in BrushMap)
        {
            if (!theme.TryGetValue(key, out var brushObject) || brushObject is not SolidColorBrush brush)
            {
                continue;
            }

            var hex = role switch
            {
                Role.Fill => shades.Fill,
                Role.Text => shades.Text,
                Role.OnFill => shades.OnFill,
                Role.Tint => shades.Tint,
                Role.Mark => shades.Mark,
                _ => shades.Base,
            };

            brush.Color = ColorUtil.WithOpacity(ColorUtil.Parse(hex, fallback), opacity);
        }
    }

    private static ResourceDictionary? FindAccentDictionary(ResourceDictionary resources)
    {
        foreach (var dictionary in resources.MergedDictionaries)
        {
            var source = dictionary.Source?.OriginalString;
            if (source is not null && source.EndsWith("Themes/Accent.xaml", StringComparison.OrdinalIgnoreCase))
            {
                return dictionary;
            }

            if (dictionary.ThemeDictionaries.TryGetValue("Light", out var light)
                && light is ResourceDictionary lightDictionary
                && lightDictionary.ContainsKey("MdvAccentTintBrush"))
            {
                return dictionary;
            }
        }

        return null;
    }

    private void ApplyCaptionButtonColors()
    {
        var titleBar = _windows.AppWindow?.TitleBar;
        if (titleBar is null)
        {
            return;
        }

        var foreground = _isDark ? White : ColorUtil.Opaque(0x1B, 0x1B, 0x1B);
        var hover = _isDark ? ColorUtil.Argb(0x15, 0xFF, 0xFF, 0xFF) : ColorUtil.Argb(0x0F, 0x00, 0x00, 0x00);
        var pressed = _isDark ? ColorUtil.Argb(0x0B, 0xFF, 0xFF, 0xFF) : ColorUtil.Argb(0x0A, 0x00, 0x00, 0x00);
        var inactive = _isDark ? ColorUtil.Opaque(0x9A, 0x9A, 0x9A) : ColorUtil.Opaque(0x8A, 0x8A, 0x8A);

        titleBar.ButtonBackgroundColor = ColorUtil.Transparent;
        titleBar.ButtonInactiveBackgroundColor = ColorUtil.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonInactiveForegroundColor = inactive;
    }

    private void OnColorValuesChanged(UISettings sender, object args)
    {
        // Raised on a background thread.
        RunOnUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            var newAccent = ReadWindowsAccent();
            var accentChanged = !string.Equals(newAccent, _windowsAccentHex, StringComparison.OrdinalIgnoreCase);
            _windowsAccentHex = newAccent;

            var wasDark = _isDark;
            if (_theme == AppTheme.System)
            {
                ApplyCore(raise: false);
            }

            if (wasDark != _isDark || accentChanged)
            {
                EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void RunOnUi(Action action)
    {
        var queue = _queue;
        if (queue is null || queue.HasThreadAccess)
        {
            if (queue is null && _windows.RootElement is null)
            {
                // Not attached yet: Attach() applies the latest state.
                return;
            }

            action();
            return;
        }

        queue.TryEnqueue(() => action());
    }

    private bool ComputeIsDark(AppTheme theme) => theme switch
    {
        AppTheme.Light => false,
        AppTheme.Dark => true,
        _ => IsSystemDark(),
    };

    private bool IsSystemDark()
    {
        try
        {
            var background = _uiSettings.GetColorValue(UIColorType.Background);
            return background.R + background.G + background.B < 384;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string? ReadWindowsAccent()
    {
        try
        {
            return ColorUtil.ToHex(_uiSettings.GetColorValue(UIColorType.Accent));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Same precedence as the ViewModels (custom → Windows accent → palette) so startup matches later Apply calls.
    private string ResolveStartupAccent(AppSettings settings) =>
        AppearanceProvider.ResolveAccentHex(settings, _windowsAccentHex);
}
