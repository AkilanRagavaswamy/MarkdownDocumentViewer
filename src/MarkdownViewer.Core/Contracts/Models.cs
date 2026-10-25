using System;
using System.Collections.Generic;

namespace MarkdownViewer.Core.Contracts;

public enum AppTheme { Light, Dark, System }

public enum DensityPreset { Comfortable, Compact, Ultra }

public enum ColumnMode { Auto, One, Two }

/// <summary>A named accent from the built-in palette.</summary>
public sealed record AccentColor(string Name, string Hex);

public static class AccentPalette
{
    public const string DefaultName = "Blue";

    public static IReadOnlyList<AccentColor> All { get; } = new[]
    {
        new AccentColor("Blue", "#546DE5"),
        new AccentColor("Lavender", "#9559ED"),
        new AccentColor("Green", "#2EAD6B"),
        new AccentColor("Pink", "#FF4081"),
        new AccentColor("Orange", "#F76831"),
        new AccentColor("Teal", "#11A3A3"),
        new AccentColor("Crimson", "#E5484D"),
        new AccentColor("Gold", "#E0A100"),
        new AccentColor("Graphite", "#6B7785"),
    };
}

/// <summary>Accent derived for one theme; all values are #RRGGBB.</summary>
public sealed record AccentShades(
    string Base,      // the chosen accent
    string Fill,      // button / selection fill
    string OnFill,    // text on Fill (white in light theme, black in dark theme)
    string Text,      // links and accent text on the reading surface (>= 4.5:1)
    string Tint,      // subtle selected background
    string Mark);     // find-match highlight background

/// <summary>Numeric reading metrics for a density preset (CSS px).</summary>
public sealed record DensityMetrics(
    double FontSize, double LineHeight, double Gap, double HeadingGap,
    double H1, double H2, double H3, double CodeSize,
    double PadVertical, double PadHorizontal,
    int MaxColumns, double TwoColumnMinWidth);

/// <summary>Everything the reading surface needs to style itself.</summary>
public sealed record ViewerAppearance(
    bool IsDark,
    AccentShades Accent,
    DensityPreset Density,
    ColumnMode Columns,
    bool WrapCodeLines,
    bool LoadRemoteContent);

/// <summary>Persisted user settings.</summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string AccentName { get; set; } = AccentPalette.DefaultName;
    public bool UseWindowsAccent { get; set; }
    public string? CustomAccentHex { get; set; }
    public DensityPreset Density { get; set; } = DensityPreset.Comfortable;
    public ColumnMode Columns { get; set; } = ColumnMode.One;
    public bool WrapCodeLines { get; set; }
    public bool LoadRemoteContent { get; set; }
    public bool IsOutlinePaneOpen { get; set; } = true;
    public bool DefaultAppBannerDismissed { get; set; }
    public List<string> RecentFiles { get; set; } = new();
    public Dictionary<string, double> ZoomByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

/// <summary>One heading in the document outline.</summary>
public sealed record OutlineNode(int Level, string Title, string AnchorId, IReadOnlyList<OutlineNode> Children);

/// <summary>Result of parsing a Markdown file.</summary>
public sealed record RenderResult(
    string BodyHtml,
    IReadOnlyList<OutlineNode> Outline,
    int WordCount,
    IReadOnlyDictionary<string, string>? FrontMatter);

/// <summary>Text read from disk plus detected encoding details.</summary>
public sealed record LoadedText(string Text, string EncodingLabel, string LineEndingLabel);

public sealed record ShortcutDefinition(string Category, string Label, IReadOnlyList<IReadOnlyList<string>> Combos);

/// <summary>A rendered piece of a shortcut: a key cap ("Ctrl") or a separator ("+", "or").</summary>
public sealed record KeyPart(string Text, bool IsKey);
