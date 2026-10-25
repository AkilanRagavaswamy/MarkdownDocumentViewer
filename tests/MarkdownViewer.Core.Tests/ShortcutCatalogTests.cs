using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.Services;

namespace MarkdownViewer.Core.Tests;

public class ShortcutCatalogTests
{
    private readonly ShortcutCatalog _catalog = new();

    [Fact]
    public void Categories_AreInDesignOrder_AndExcludeAll()
    {
        Assert.Equal(new[] { "Files and tabs", "View", "Navigate", "Find and help" }, _catalog.Categories);
    }

    [Fact]
    public void EveryShortcut_BelongsToAKnownCategory()
    {
        Assert.All(_catalog.All, s => Assert.Contains(s.Category, _catalog.Categories));
        Assert.Equal(10, _catalog.All.Count(s => s.Category == "Files and tabs"));
        Assert.Equal(12, _catalog.All.Count(s => s.Category == "View"));
        Assert.Equal(9, _catalog.All.Count(s => s.Category == "Navigate"));
        Assert.Equal(6, _catalog.All.Count(s => s.Category == "Find and help"));
    }

    [Fact]
    public void ToParts_JoinsKeysWithPlus()
    {
        var parts = _catalog.ToParts(Find("Open folder"));

        Assert.Equal(
            new[] { new KeyPart("Ctrl", true), new KeyPart("+", false), new KeyPart("Shift", true), new KeyPart("+", false), new KeyPart("O", true) },
            parts);
    }

    [Fact]
    public void ToParts_SeparatesAlternativesWithOr()
    {
        var parts = _catalog.ToParts(Find("Keyboard shortcuts"));

        Assert.Equal(
            new[] { new KeyPart("Ctrl", true), new KeyPart("+", false), new KeyPart("/", true), new KeyPart("or", false), new KeyPart("F1", true) },
            parts);
    }

    [Theory]
    [InlineData("Zoom in", "=")]
    [InlineData("Zoom out", "-")]
    [InlineData("Settings", ",")]
    [InlineData("Collapse all sections", "[")]
    [InlineData("Expand all sections", "]")]
    public void PunctuationKeys_AreKeptAsKeys(string label, string lastKey)
    {
        var parts = _catalog.ToParts(Find(label));

        Assert.Equal(new KeyPart(lastKey, true), parts[^1]);
        Assert.All(parts.Where(p => !p.IsKey), p => Assert.Contains(p.Text, new[] { "+", "or" }));
    }

    [Fact]
    public void ToParts_HandlesPlusKey()
    {
        var def = new ShortcutDefinition("View", "Test", new[] { (IReadOnlyList<string>)new[] { "Ctrl", "+" } });

        Assert.Equal(new[] { new KeyPart("Ctrl", true), new KeyPart("+", false), new KeyPart("+", true) }, _catalog.ToParts(def));
    }

    [Fact]
    public void SingleKeyAlternatives_HaveNoPlus()
    {
        var parts = _catalog.ToParts(Find("Scroll down"));

        Assert.Equal(new[] { new KeyPart("J", true), new KeyPart("or", false), new KeyPart("↓", true) }, parts);
    }

    private ShortcutDefinition Find(string label) => _catalog.All.Single(s => s.Label == label);
}
