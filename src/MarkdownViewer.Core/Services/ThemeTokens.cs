namespace MarkdownViewer.Core.Services;

/// <summary>Base surface and text colors for one theme (from the approved design).</summary>
/// <param name="Mica">Window backdrop color.</param>
/// <param name="Layer">Chrome layer (command bar, panes).</param>
/// <param name="Reading">Reading surface (document background).</param>
/// <param name="Stroke">Borders and dividers.</param>
/// <param name="Text">Primary text.</param>
/// <param name="Text2">Secondary text.</param>
/// <param name="Code">Code block background.</param>
public sealed record ThemePalette(
    string Mica, string Layer, string Reading, string Stroke, string Text, string Text2, string Code);

/// <summary>Light and dark design tokens.</summary>
public static class ThemeTokens
{
    /// <summary>Light theme tokens.</summary>
    public static ThemePalette Light { get; } = new(
        Mica: "#F3F3F3", Layer: "#FAFAFA", Reading: "#FFFFFF", Stroke: "#E3E3E3",
        Text: "#1B1B1B", Text2: "#5C5C5C", Code: "#F5F6F8");

    /// <summary>Dark theme tokens.</summary>
    public static ThemePalette Dark { get; } = new(
        Mica: "#202020", Layer: "#2B2B2B", Reading: "#1C1C1C", Stroke: "#3B3B3B",
        Text: "#FFFFFF", Text2: "#C5C5C5", Code: "#262626");

    /// <summary>Returns the tokens for the requested theme.</summary>
    public static ThemePalette For(bool isDark) => isDark ? Dark : Light;
}
