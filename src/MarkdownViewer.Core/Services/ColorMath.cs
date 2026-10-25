using System.Globalization;

namespace MarkdownViewer.Core.Services;

/// <summary>An sRGB color with 8-bit channels.</summary>
/// <param name="R">Red channel.</param>
/// <param name="G">Green channel.</param>
/// <param name="B">Blue channel.</param>
public readonly record struct RgbColor(byte R, byte G, byte B);

/// <summary>Small color helpers used to derive accessible accent shades (WCAG 2.x math).</summary>
public static class ColorMath
{
    /// <summary>Parses "#RRGGBB", "RRGGBB", "#RGB" or "RGB" (case-insensitive).</summary>
    /// <exception cref="FormatException">The value is not a valid hex color.</exception>
    public static RgbColor Parse(string hex)
    {
        if (!TryNormalizeHex(hex, out var normalized))
        {
            throw new FormatException($"'{hex}' is not a valid hex color.");
        }

        return new RgbColor(
            byte.Parse(normalized.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(normalized.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(normalized.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>Formats a color as upper-case "#RRGGBB".</summary>
    public static string ToHex(RgbColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    /// <summary>
    /// Normalizes user input such as "f76831", "#F76831", " #f76831 " or "#f63" to "#F76831".
    /// </summary>
    /// <param name="input">The raw text.</param>
    /// <param name="normalized">Upper-case "#RRGGBB" on success; empty string otherwise.</param>
    /// <returns><c>true</c> when the input is a valid 3- or 6-digit hex color.</returns>
    public static bool TryNormalizeHex(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim();
        if (s.StartsWith('#'))
        {
            s = s[1..];
        }

        if (s.Length != 3 && s.Length != 6)
        {
            return false;
        }

        foreach (var ch in s)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        if (s.Length == 3)
        {
            s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
        }

        normalized = "#" + s.ToUpperInvariant();
        return true;
    }

    /// <summary>Linear per-channel mix: <c>x + (y - x) * k</c>, rounded to the nearest integer.</summary>
    /// <param name="x">Start color (k = 0).</param>
    /// <param name="y">End color (k = 1).</param>
    /// <param name="k">Mix amount, clamped to [0, 1].</param>
    public static RgbColor Mix(RgbColor x, RgbColor y, double k)
    {
        k = Math.Clamp(k, 0d, 1d);
        return new RgbColor(MixChannel(x.R, y.R, k), MixChannel(x.G, y.G, k), MixChannel(x.B, y.B, k));
    }

    /// <summary>WCAG relative luminance in [0, 1].</summary>
    public static double Luminance(RgbColor color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    /// <summary>WCAG contrast ratio between two colors, in [1, 21].</summary>
    public static double Contrast(RgbColor a, RgbColor b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>WCAG contrast ratio between two hex colors.</summary>
    public static double Contrast(string a, string b) => Contrast(Parse(a), Parse(b));

    private static byte MixChannel(byte a, byte b, double k) =>
        (byte)Math.Clamp(Math.Round(a + ((b - a) * k), MidpointRounding.AwayFromZero), 0, 255);

    private static double Linear(byte channel)
    {
        var c = channel / 255d;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
