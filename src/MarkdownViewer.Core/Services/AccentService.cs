using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>Derives accessible accent shades for the light and dark themes.</summary>
public sealed class AccentService : IAccentService
{
    private const double MinContrast = 4.5;
    private const int MaxSteps = 12;
    private const double StepSize = 0.07;

    private static readonly RgbColor White = new(0xFF, 0xFF, 0xFF);
    private static readonly RgbColor Black = new(0x00, 0x00, 0x00);

    /// <summary>
    /// Computes Fill/OnFill/Text/Tint/Mark for <paramref name="accentHex"/>. Invalid input falls back to the default palette accent.
    /// </summary>
    public AccentShades Compute(string accentHex, bool isDark)
    {
        if (!ColorMath.TryNormalizeHex(accentHex, out var normalized))
        {
            normalized = AccentPalette.All.First(a => a.Name == AccentPalette.DefaultName).Hex;
        }

        var acc = ColorMath.Parse(normalized);
        var tokens = ThemeTokens.For(isDark);
        var layer = ColorMath.Parse(tokens.Layer);
        var reading = ColorMath.Parse(tokens.Reading);

        RgbColor fill, onFill, text, tint, mark;
        if (isDark)
        {
            fill = Reach(acc, Black, White);
            onFill = Black;
            text = Reach(acc, reading, White);
            tint = ColorMath.Mix(layer, acc, 0.22);
            mark = ColorMath.Mix(reading, acc, 0.35);
        }
        else
        {
            fill = Reach(acc, White, Black);
            onFill = White;
            text = Reach(acc, reading, Black);
            tint = ColorMath.Mix(layer, acc, 0.12);
            mark = ColorMath.Mix(reading, acc, 0.25);
        }

        return new AccentShades(
            Base: normalized,
            Fill: ColorMath.ToHex(fill),
            OnFill: ColorMath.ToHex(onFill),
            Text: ColorMath.ToHex(text),
            Tint: ColorMath.ToHex(tint),
            Mark: ColorMath.ToHex(mark));
    }

    /// <summary>Moves <paramref name="c0"/> toward <paramref name="toward"/> in 7% steps until it reaches 4.5:1 against <paramref name="bg"/>.</summary>
    internal static RgbColor Reach(RgbColor c0, RgbColor bg, RgbColor toward)
    {
        var c = c0;
        for (var i = 1; i <= MaxSteps && ColorMath.Contrast(c, bg) < MinContrast; i++)
        {
            c = ColorMath.Mix(c0, toward, i * StepSize);
        }

        return c;
    }
}
