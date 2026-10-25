using System.Text.Json;
using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.Services;

namespace MarkdownViewer.Core.Tests;

public class HtmlDocumentBuilderTests
{
    // Copied from CONTRACT.md ("CSS custom properties set by setAppearance").
    private static readonly string[] ContractCssVars =
    {
        "--mdv-bg", "--mdv-fg", "--mdv-fg2", "--mdv-border", "--mdv-code-bg", "--mdv-accent-fill",
        "--mdv-on-accent", "--mdv-accent-text", "--mdv-accent-tint", "--mdv-mark", "--mdv-font-size",
        "--mdv-line-height", "--mdv-gap", "--mdv-h1", "--mdv-h2", "--mdv-h3", "--mdv-code-size",
        "--mdv-pad", "--mdv-columns",
    };

    private readonly HtmlDocumentBuilder _builder = new(new DensityService());
    private readonly AccentService _accents = new();

    private ViewerAppearance Appearance(
        bool dark = false,
        DensityPreset density = DensityPreset.Compact,
        ColumnMode columns = ColumnMode.Auto,
        bool wrap = false,
        bool remote = false) =>
        new(dark, _accents.Compute("#546DE5", dark), density, columns, wrap, remote);

    [Fact]
    public void AppearanceJson_ContainsExactlyTheContractCssVars()
    {
        using var json = JsonDocument.Parse(_builder.BuildAppearanceJson(Appearance()));
        var vars = json.RootElement.GetProperty("vars");

        var names = vars.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(ContractCssVars.OrderBy(n => n, StringComparer.Ordinal), names.OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(vars.EnumerateObject(), p => Assert.False(string.IsNullOrWhiteSpace(p.Value.GetString())));
    }

    [Fact]
    public void AppearanceJson_UsesCamelCaseAndStringColumns()
    {
        using var json = JsonDocument.Parse(_builder.BuildAppearanceJson(Appearance(dark: true, columns: ColumnMode.Two, wrap: true, remote: true)));
        var root = json.RootElement;

        Assert.Equal("dark", root.GetProperty("theme").GetString());
        Assert.True(root.GetProperty("wrap").GetBoolean());
        Assert.True(root.GetProperty("loadRemote").GetBoolean());
        Assert.Equal("2", root.GetProperty("columns").GetString());
        Assert.Equal(2, root.GetProperty("maxColumns").GetInt32());
        Assert.Equal(1400, root.GetProperty("twoColumnMinWidth").GetDouble());
    }

    [Fact]
    public void AppearanceJson_VarsReflectDensityAndTheme()
    {
        using var json = JsonDocument.Parse(_builder.BuildAppearanceJson(Appearance(density: DensityPreset.Compact)));
        var vars = json.RootElement.GetProperty("vars");

        Assert.Equal("14px", vars.GetProperty("--mdv-font-size").GetString());
        Assert.Equal("1.42", vars.GetProperty("--mdv-line-height").GetString());
        Assert.Equal("18px 28px", vars.GetProperty("--mdv-pad").GetString());
        Assert.Equal("16.5px", vars.GetProperty("--mdv-h2").GetString());
        Assert.Equal("#FFFFFF", vars.GetProperty("--mdv-bg").GetString());
        Assert.Equal("#1B1B1B", vars.GetProperty("--mdv-fg").GetString());
        Assert.Equal("auto", json.RootElement.GetProperty("columns").GetString());
    }

    [Fact]
    public void Build_EmitsShellPerContract()
    {
        var result = new RenderResult("<h1 id=\"t\">T</h1>", Array.Empty<OutlineNode>(), 1, null);

        var html = _builder.Build(result, Appearance(dark: true, wrap: true), "A <b> title");

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("data-theme=\"dark\"", html);
        Assert.Contains("data-wrap=\"true\"", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
        Assert.Contains("<base href=\"https://doc.mdv/\">", html);
        Assert.Contains("href=\"https://appassets.mdv/viewer.css\"", html);
        Assert.Contains("href=\"https://appassets.mdv/highlight.css\"", html);
        Assert.Contains("<article id=\"mdv-content\"", html);
        Assert.Contains("<h1 id=\"t\">T</h1>", html);
        Assert.Contains("<title>A &lt;b&gt; title</title>", html);
        Assert.Contains("--mdv-bg:#1C1C1C;", html);

        var highlight = html.IndexOf("https://appassets.mdv/highlight.min.js\" defer", StringComparison.Ordinal);
        var viewer = html.IndexOf("https://appassets.mdv/viewer.js\" defer", StringComparison.Ordinal);
        Assert.True(highlight > 0 && viewer > highlight, "highlight.min.js must load before viewer.js");
    }

    [Fact]
    public void Csp_IsStrict_AndAllowsRemoteImagesOnlyWhenEnabled()
    {
        var local = HtmlDocumentBuilder.BuildContentSecurityPolicy(loadRemoteContent: false);
        var remote = HtmlDocumentBuilder.BuildContentSecurityPolicy(loadRemoteContent: true);

        Assert.Equal(
            "default-src 'none'; style-src https://appassets.mdv 'unsafe-inline'; script-src https://appassets.mdv; img-src https://doc.mdv https://appassets.mdv data:; font-src https://appassets.mdv",
            local);
        Assert.Contains("img-src https://doc.mdv https://appassets.mdv data: https:;", remote);

        var html = _builder.Build(new RenderResult("", Array.Empty<OutlineNode>(), 0, null), Appearance(), "t");
        Assert.Contains($"content=\"{local}\"", html);
    }

    [Fact]
    public void BuildSource_EscapesSource()
    {
        var html = _builder.BuildSource("<script>alert(1)</script> & co", Appearance(), "src");

        Assert.Contains("<pre class=\"mdv-source\">&lt;script&gt;alert(1)&lt;/script&gt; &amp; co</pre>", html);
        Assert.DoesNotContain("<script>alert", html);
    }

    [Fact]
    public void InvalidAccentValues_DoNotReachInlineStyle()
    {
        var bad = new AccentShades("#546DE5", "red;}</style><script>", "#FFFFFF", "#000000", "#FAFAFA", "#FFFFFF");
        var appearance = new ViewerAppearance(false, bad, DensityPreset.Comfortable, ColumnMode.One, false, false);

        var html = _builder.Build(new RenderResult("", Array.Empty<OutlineNode>(), 0, null), appearance, "t");

        Assert.DoesNotContain("red;}", html);
        Assert.Contains("--mdv-columns:1;", html);
    }
}
