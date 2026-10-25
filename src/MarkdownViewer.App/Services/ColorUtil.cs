using System.Globalization;
using Windows.UI;

namespace MarkdownViewer.App.Services;

/// <summary>Small helpers for #RRGGBB strings and <see cref="Color"/> values.</summary>
internal static class ColorUtil
{
    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var s = hex.Trim().TrimStart('#');
        if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = Argb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }

        if (s.Length == 8 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            color = Argb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            return true;
        }

        return false;
    }

    public static Color Parse(string? hex, Color fallback) => TryParse(hex, out var c) ? c : fallback;

    public static Color WithOpacity(Color c, double opacity) =>
        Argb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte Lerp(byte x, byte y) => (byte)Math.Round(x + ((y - x) * amount));
        return Argb(0xFF, Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Opaque(byte r, byte g, byte b) => Argb(0xFF, r, g, b);

    public static Color Argb(byte a, byte r, byte g, byte b) => new() { A = a, R = r, G = g, B = b };

    public static Color Transparent => Argb(0, 0xFF, 0xFF, 0xFF);
}
