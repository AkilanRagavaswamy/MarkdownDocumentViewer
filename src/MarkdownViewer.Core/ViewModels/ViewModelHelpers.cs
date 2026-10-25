using System.Globalization;

namespace MarkdownViewer.Core.ViewModels;

/// <summary>Tiny, self-contained colour helpers used by the ViewModels (kept separate from Services/ColorMath on purpose).</summary>
internal static class HexColor
{
    /// <summary>Accepts 6 hex digits with an optional leading '#'. Returns "#RRGGBB" (upper case).</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var span = input.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length != 6)
        {
            return false;
        }

        foreach (var c in span)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        normalized = "#" + span.ToString().ToUpperInvariant();
        return true;
    }

    /// <summary>WCAG relative luminance of a "#RRGGBB" colour (0 = black, 1 = white).</summary>
    public static double RelativeLuminance(string hex)
    {
        if (!TryNormalize(hex, out var n))
        {
            return 0;
        }

        static double Channel(string h, int offset)
        {
            var v = int.Parse(h.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(n, 1)) + (0.7152 * Channel(n, 3)) + (0.0722 * Channel(n, 5));
    }

    /// <summary>"#FFFFFF" or "#000000", whichever has the higher contrast ratio against <paramref name="backgroundHex"/>.</summary>
    public static string ContrastingGlyph(string backgroundHex)
    {
        var l = RelativeLuminance(backgroundHex);
        var contrastWithWhite = 1.05 / (l + 0.05);
        var contrastWithBlack = (l + 0.05) / 0.05;
        return contrastWithWhite >= contrastWithBlack ? "#FFFFFF" : "#000000";
    }
}

/// <summary>Converts XAML command parameters (boxed enum, string name, or int) into enum values.</summary>
internal static class EnumParameter
{
    public static bool TryParse<T>(object? value, out T result)
        where T : struct, Enum
    {
        if (value is T typed)
        {
            result = typed;
            return true;
        }

        if (value is string text && Enum.TryParse(text.Trim(), ignoreCase: true, out T parsed) && Enum.IsDefined(parsed))
        {
            result = parsed;
            return true;
        }

        if (value is int number && Enum.IsDefined(typeof(T), number))
        {
            result = (T)Enum.ToObject(typeof(T), number);
            return true;
        }

        result = default;
        return false;
    }
}

internal static class PathUtil
{
    public static bool PathEquals(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static bool IsMarkdownFile(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext)
            && Contracts.MarkdownExtensions.All.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
