using MarkdownViewer.Core.Services;

namespace MarkdownViewer.Core.Tests;

public class MarkdownRenderServiceTests
{
    private readonly MarkdownRenderService _service = new();

    [Fact]
    public void Outline_NestsByLevel_AndExcludesH5()
    {
        const string md = "# Alpha\n\n## Beta\n\n### Gamma\n\n## Delta\n\n# Epsilon\n\n#### Zeta\n\n##### Too deep\n";

        var outline = _service.Render(md).Outline;

        Assert.Equal(2, outline.Count);
        Assert.Equal("Alpha", outline[0].Title);
        Assert.Equal(new[] { "Beta", "Delta" }, outline[0].Children.Select(c => c.Title));
        Assert.Equal("Gamma", Assert.Single(outline[0].Children[0].Children).Title);
        Assert.Empty(outline[0].Children[1].Children);

        var epsilon = outline[1];
        Assert.Equal("Epsilon", epsilon.Title);
        var zeta = Assert.Single(epsilon.Children);
        Assert.Equal(4, zeta.Level);
        Assert.Empty(zeta.Children);
    }

    [Fact]
    public void Outline_IdsMatchGeneratedHeadingIds_GitHubStyle()
    {
        const string md = "# Hello World\n\n## Hello World\n\n## Set up `dotnet` & tools\n";

        var result = _service.Render(md);
        var ids = Flatten(result.Outline).Select(n => n.AnchorId).ToList();

        Assert.Equal("hello-world", ids[0]);
        Assert.Equal("hello-world-1", ids[1]);
        Assert.Equal("Set up dotnet & tools", Flatten(result.Outline).Last().Title);
        foreach (var id in ids)
        {
            Assert.Contains($"id=\"{id}\"", result.BodyHtml);
        }
    }

    [Fact]
    public void FrontMatter_ParsesTopLevelKeys_AndIsNotRendered()
    {
        const string md = "---\ntitle: Release notes\nauthor: \"Jane Doe\"\nversion: '1.2'\ntags:\n  - a\n  - b\nnested:\n  key: value\n---\n\n# Body\n";

        var result = _service.Render(md);

        Assert.NotNull(result.FrontMatter);
        var fm = result.FrontMatter!;
        Assert.Equal("Release notes", fm["title"]);
        Assert.Equal("Jane Doe", fm["author"]);
        Assert.Equal("1.2", fm["version"]);
        Assert.False(fm.ContainsKey("tags"));
        Assert.False(fm.ContainsKey("key"));
        Assert.DoesNotContain("Release notes", result.BodyHtml);
        Assert.Contains("<h1", result.BodyHtml);
    }

    [Fact]
    public void FrontMatter_IsNullWhenAbsent()
    {
        Assert.Null(_service.Render("# Just a heading\n").FrontMatter);
    }

    [Fact]
    public void Sanitizer_StripsScriptTags()
    {
        var html = _service.Render("Hello\n\n<script>alert('x')</script>\n\nWorld").BodyHtml;

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert(", html);
        Assert.Contains("Hello", html);
        Assert.Contains("World", html);
    }

    [Fact]
    public void Sanitizer_StripsEventHandlerAttributes()
    {
        var html = _service.Render("<img src=\"pic.png\" onerror=\"alert(1)\" alt=\"pic\">").BodyHtml;

        Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pic.png", html);
    }

    [Fact]
    public void Sanitizer_StripsJavascriptUrls()
    {
        var html = _service.Render("[click](javascript:alert(1)) and <a href=\"JavaScript:alert(2)\">raw</a>").BodyHtml;

        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("click", html);
    }

    [Fact]
    public void Sanitizer_StripsIframeStyleAndObject()
    {
        var html = _service.Render("<iframe src=\"https://x\"></iframe>\n\n<style>body{}</style>\n\n<object data=\"x\"></object>\n\n<embed src=\"x\">").BodyHtml;

        Assert.DoesNotContain("<iframe", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<object", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<embed", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitizer_KeepsSafeLinksAndImages()
    {
        var html = _service.Render("[site](https://example.com/page \"Example\") ![logo](img/logo.png)").BodyHtml;

        Assert.Contains("href=\"https://example.com/page\"", html);
        Assert.Contains("title=\"Example\"", html);
        Assert.Contains("src=\"img/logo.png\"", html);
        Assert.Contains("alt=\"logo\"", html);
    }

    [Fact]
    public void TaskList_CheckboxSurvivesSanitizer()
    {
        var html = _service.Render("- [x] done\n- [ ] todo\n").BodyHtml;

        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("checked", html);
        Assert.Contains("disabled", html);
        Assert.Contains("done", html);
    }

    [Fact]
    public void RawTextInput_IsNeutralizedToReadOnlyCheckbox()
    {
        var html = _service.Render("<input type=\"text\" value=\"secret\">").BodyHtml;

        Assert.DoesNotContain("type=\"text\"", html);
        Assert.DoesNotContain("secret", html);
    }

    [Fact]
    public void CodeBlock_KeepsLanguageClass()
    {
        var html = _service.Render("```csharp\nvar x = 1;\n```\n").BodyHtml;

        Assert.Contains("class=\"language-csharp\"", html);
        Assert.Contains("<pre>", html);
    }

    [Fact]
    public void Tables_FootnotesAndDetails_Survive()
    {
        const string md = "| A | B |\n|:--|--:|\n| 1 | 2 |\n\nText[^1]\n\n[^1]: Note.\n\n<details><summary>More</summary>Hidden</details>\n";

        var html = _service.Render(md).BodyHtml;

        Assert.Contains("<table", html);
        Assert.Contains("text-align", html);
        Assert.Contains("id=\"fn:1\"", html);
        Assert.Contains("<details>", html);
        Assert.Contains("<summary>", html);
    }

    [Fact]
    public void AlertBlocks_KeepMarkdownAlertClass()
    {
        var html = _service.Render("> [!NOTE]\n> Useful information.\n").BodyHtml;

        Assert.Contains("markdown-alert", html);
        Assert.Contains("Useful information", html);
        Assert.DoesNotContain("<svg", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WordCount_CountsVisibleWords()
    {
        var result = _service.Render("---\ntitle: Ignored words here\n---\n# Two words\n\nThree more **words**.\n");

        Assert.Equal(5, result.WordCount);
    }

    private static IEnumerable<Contracts.OutlineNode> Flatten(IEnumerable<Contracts.OutlineNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
}
