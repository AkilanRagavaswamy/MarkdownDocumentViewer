using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.Services;

namespace MarkdownViewer.Core.Tests;

public class AccentServiceTests
{
    private readonly AccentService _service = new();

    public static IEnumerable<object[]> PaletteInBothThemes() =>
        AccentPalette.All.SelectMany(a => new[] { new object[] { a.Hex, false }, new object[] { a.Hex, true } });

    [Theory]
    [MemberData(nameof(PaletteInBothThemes))]
    public void Fill_AgainstOnFill_MeetsAa(string hex, bool isDark)
    {
        var shades = _service.Compute(hex, isDark);

        Assert.True(ColorMath.Contrast(shades.Fill, shades.OnFill) >= 4.5,
            $"{hex} dark={isDark}: fill {shades.Fill} on {shades.OnFill} = {ColorMath.Contrast(shades.Fill, shades.OnFill):0.00}");
    }

    [Theory]
    [MemberData(nameof(PaletteInBothThemes))]
    public void Text_AgainstReadingSurface_MeetsAa(string hex, bool isDark)
    {
        var shades = _service.Compute(hex, isDark);
        var reading = ThemeTokens.For(isDark).Reading;

        Assert.True(ColorMath.Contrast(shades.Text, reading) >= 4.5,
            $"{hex} dark={isDark}: text {shades.Text} on {reading} = {ColorMath.Contrast(shades.Text, reading):0.00}");
    }

    [Fact]
    public void OnFill_IsWhiteInLight_BlackInDark()
    {
        Assert.Equal("#FFFFFF", _service.Compute("#546DE5", isDark: false).OnFill);
        Assert.Equal("#000000", _service.Compute("#546DE5", isDark: true).OnFill);
    }

    [Fact]
    public void Tint_AndMark_UseDesignMixRatios()
    {
        var acc = ColorMath.Parse("#546DE5");
        var light = _service.Compute("#546DE5", isDark: false);
        var dark = _service.Compute("#546DE5", isDark: true);

        Assert.Equal(ColorMath.ToHex(ColorMath.Mix(ColorMath.Parse("#FAFAFA"), acc, 0.12)), light.Tint);
        Assert.Equal(ColorMath.ToHex(ColorMath.Mix(ColorMath.Parse("#FFFFFF"), acc, 0.25)), light.Mark);
        Assert.Equal(ColorMath.ToHex(ColorMath.Mix(ColorMath.Parse("#2B2B2B"), acc, 0.22)), dark.Tint);
        Assert.Equal(ColorMath.ToHex(ColorMath.Mix(ColorMath.Parse("#1C1C1C"), acc, 0.35)), dark.Mark);
    }

    [Fact]
    public void AlreadyAccessibleAccent_IsUnchanged()
    {
        // #1B1B1B already has > 4.5:1 against white, so reach() must not move it.
        var shades = _service.Compute("#1B1B1B", isDark: false);

        Assert.Equal("#1B1B1B", shades.Fill);
        Assert.Equal("#1B1B1B", shades.Text);
    }

    [Fact]
    public void Compute_NormalizesBase_AndFallsBackOnInvalidInput()
    {
        Assert.Equal("#F76831", _service.Compute("f76831", false).Base);
        Assert.Equal("#546DE5", _service.Compute("not a color", false).Base);
    }

    [Theory]
    [InlineData("f76831", "#F76831")]
    [InlineData("#F76831", "#F76831")]
    [InlineData("  #f76831  ", "#F76831")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("ABC", "#AABBCC")]
    public void TryNormalizeHex_AcceptsValidInput(string input, string expected)
    {
        Assert.True(ColorMath.TryNormalizeHex(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    [InlineData("##F76831")]
    public void TryNormalizeHex_RejectsInvalidInput(string? input)
    {
        Assert.False(ColorMath.TryNormalizeHex(input, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void Contrast_MatchesKnownValues()
    {
        Assert.Equal(21.0, ColorMath.Contrast("#000000", "#FFFFFF"), 3);
        Assert.Equal(1.0, ColorMath.Contrast("#777777", "#777777"), 3);
        Assert.Equal(4.48, ColorMath.Contrast("#777777", "#FFFFFF"), 2);
    }

    [Fact]
    public void Mix_InterpolatesPerChannel()
    {
        var mid = ColorMath.Mix(ColorMath.Parse("#000000"), ColorMath.Parse("#FFFFFF"), 0.5);
        Assert.Equal("#808080", ColorMath.ToHex(mid));
        Assert.Equal("#102030", ColorMath.ToHex(ColorMath.Mix(ColorMath.Parse("#102030"), ColorMath.Parse("#FFFFFF"), 0)));
    }
}
