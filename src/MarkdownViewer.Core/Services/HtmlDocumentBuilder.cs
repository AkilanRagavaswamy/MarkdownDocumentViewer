using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>Payload of <c>mdv.setAppearance(json)</c>, serialized in camelCase.</summary>
public sealed class ViewerAppearanceDto
{
    /// <summary>"light" or "dark".</summary>
    public string Theme { get; init; } = "light";

    /// <summary>Whether code blocks wrap long lines.</summary>
    public bool Wrap { get; init; }

    /// <summary>Whether remote (https:) images may load.</summary>
    public bool LoadRemote { get; init; }

    /// <summary>"comfortable", "compact" or "ultra".</summary>
    public string Density { get; init; } = "compact";

    /// <summary>"auto", "1", "2" or "3".</summary>
    public string Columns { get; init; } = "auto";

    /// <summary>Largest column count Auto mode may use for the current density.</summary>
    public int MaxColumns { get; init; }

    /// <summary>Viewport width (CSS px) from which Auto mode uses two columns.</summary>
    public double TwoColumnMinWidth { get; init; }

    /// <summary>Viewport width (CSS px) from which Auto mode uses three columns.</summary>

    /// <summary>CSS custom properties (name includes the leading "--") and their CSS values.</summary>
    public IReadOnlyDictionary<string, string> Vars { get; init; } = new Dictionary<string, string>();
}

/// <summary>Builds the WebView2 pages (document and "View source") and the appearance JSON.</summary>
public sealed class HtmlDocumentBuilder : IHtmlDocumentBuilder
{
    /// <summary>Virtual host that serves the app's web assets.</summary>
    public const string AppAssetsOrigin = "https://appassets.mdv";

    /// <summary>Virtual host that serves the open document's folder.</summary>
    public const string DocumentOrigin = "https://doc.mdv";

    /// <summary>Every CSS custom property set by <c>mdv.setAppearance</c>, in contract order.</summary>
    public static IReadOnlyList<string> CssVariableNames { get; } = new[]
    {
        "--mdv-bg", "--mdv-fg", "--mdv-fg2", "--mdv-border", "--mdv-code-bg",
        "--mdv-accent-fill", "--mdv-on-accent", "--mdv-accent-text", "--mdv-accent-tint", "--mdv-mark",
        "--mdv-font-size", "--mdv-line-height", "--mdv-gap", "--mdv-h1", "--mdv-h2", "--mdv-h3",
        "--mdv-code-size", "--mdv-pad", "--mdv-columns",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    private readonly IDensityService _density;

    /// <summary>Creates the builder.</summary>
    /// <param name="density">Source of the density metrics.</param>
    public HtmlDocumentBuilder(IDensityService density)
    {
        _density = density ?? throw new ArgumentNullException(nameof(density));
    }

    /// <inheritdoc />
    public string Build(RenderResult result, ViewerAppearance appearance, string title)
    {
        ArgumentNullException.ThrowIfNull(result);
        return BuildPage(appearance, title, "mdv-markdown", result.BodyHtml);
    }

    /// <inheritdoc />
    public string BuildSource(string source, ViewerAppearance appearance, string title)
    {
        var body = "<pre class=\"mdv-source\">" + WebUtility.HtmlEncode(source ?? string.Empty) + "</pre>";
        return BuildPage(appearance, title, "mdv-source-view", body);
    }

    /// <inheritdoc />
    public string BuildAppearanceJson(ViewerAppearance appearance) =>
        JsonSerializer.Serialize(CreateDto(appearance), JsonOptions);

    /// <summary>Creates the appearance payload (also embedded in the page for the first paint).</summary>
    public ViewerAppearanceDto CreateDto(ViewerAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        var metrics = _density.Get(appearance.Density);

        return new ViewerAppearanceDto
        {
            Theme = appearance.IsDark ? "dark" : "light",
            Wrap = appearance.WrapCodeLines,
            LoadRemote = appearance.LoadRemoteContent,
            Density = appearance.Density switch
            {
                DensityPreset.Comfortable => "comfortable",
                DensityPreset.Ultra => "ultra",
                _ => "compact",
            },
            Columns = appearance.Columns switch
            {
                ColumnMode.One => "1",
                ColumnMode.Two => "2",
                _ => "auto",
            },
            MaxColumns = metrics.MaxColumns,
            TwoColumnMinWidth = metrics.TwoColumnMinWidth,
            Vars = BuildVars(appearance, metrics),
        };
    }

    /// <summary>
    /// CSS custom properties for the appearance. In Auto column mode <c>--mdv-columns</c> is "1";
    /// the page's inline media queries and viewer.js raise it by viewport width up to MaxColumns.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildVars(ViewerAppearance appearance, DensityMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(metrics);

        var tokens = ThemeTokens.For(appearance.IsDark);
        var accent = appearance.Accent;
        var fallbackAccent = appearance.IsDark ? "#FFFFFF" : "#000000";

        var columns = appearance.Columns switch
        {
            ColumnMode.Two => 2,
            _ => 1,
        };

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--mdv-bg"] = tokens.Reading,
            ["--mdv-fg"] = tokens.Text,
            ["--mdv-fg2"] = tokens.Text2,
            ["--mdv-border"] = tokens.Stroke,
            ["--mdv-code-bg"] = tokens.Code,
            ["--mdv-accent-fill"] = SafeColor(accent?.Fill, fallbackAccent),
            ["--mdv-on-accent"] = SafeColor(accent?.OnFill, appearance.IsDark ? "#000000" : "#FFFFFF"),
            ["--mdv-accent-text"] = SafeColor(accent?.Text, fallbackAccent),
            ["--mdv-accent-tint"] = SafeColor(accent?.Tint, tokens.Layer),
            ["--mdv-mark"] = SafeColor(accent?.Mark, tokens.Layer),
            ["--mdv-font-size"] = Px(metrics.FontSize),
            ["--mdv-line-height"] = Num(metrics.LineHeight),
            ["--mdv-gap"] = Px(metrics.Gap),
            ["--mdv-h1"] = Px(metrics.H1),
            ["--mdv-h2"] = Px(metrics.H2),
            ["--mdv-h3"] = Px(metrics.H3),
            ["--mdv-code-size"] = Px(metrics.CodeSize),
            ["--mdv-pad"] = Px(metrics.PadVertical) + " " + Px(metrics.PadHorizontal),
            ["--mdv-columns"] = columns.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The Content-Security-Policy for the document page.</summary>
    public static string BuildContentSecurityPolicy(bool loadRemoteContent)
    {
        var img = $"img-src {DocumentOrigin} {AppAssetsOrigin} data:" + (loadRemoteContent ? " https:" : string.Empty);
        return string.Join(
            "; ",
            "default-src 'none'",
            $"style-src {AppAssetsOrigin} 'unsafe-inline'",
            $"script-src {AppAssetsOrigin}",
            img,
            $"font-src {AppAssetsOrigin}");
    }

    private string BuildPage(ViewerAppearance appearance, string title, string articleClass, string bodyHtml)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        var dto = CreateDto(appearance);
        var theme = dto.Theme;
        var wrap = appearance.WrapCodeLines ? "true" : "false";

        var sb = new StringBuilder(bodyHtml.Length + 4096);
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\" data-theme=\"").Append(theme).Append("\" data-wrap=\"").Append(wrap).Append("\">\n");
        sb.Append("<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"")
          .Append(BuildContentSecurityPolicy(appearance.LoadRemoteContent))
          .Append("\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<meta name=\"color-scheme\" content=\"").Append(theme).Append("\">\n");
        sb.Append("<base href=\"").Append(DocumentOrigin).Append("/\">\n");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(title ?? string.Empty)).Append("</title>\n");
        sb.Append("<link rel=\"stylesheet\" href=\"").Append(AppAssetsOrigin).Append("/viewer.css\">\n");
        sb.Append("<link rel=\"stylesheet\" href=\"").Append(AppAssetsOrigin).Append("/highlight.css\">\n");
        AppendInitialStyle(sb, dto);
        sb.Append("<script src=\"").Append(AppAssetsOrigin).Append("/highlight.min.js\" defer></script>\n");
        sb.Append("<script src=\"").Append(AppAssetsOrigin).Append("/viewer.js\" defer></script>\n");
        sb.Append("</head>\n");
        sb.Append("<body>\n");
        // Non-executable data block (allowed by CSP): viewer.js may read the initial appearance from it.
        sb.Append("<script type=\"application/json\" id=\"mdv-appearance\">")
          .Append(JsonSerializer.Serialize(dto, JsonOptions))
          .Append("</script>\n");
        sb.Append("<article id=\"mdv-content\" class=\"").Append(articleClass).Append("\">\n");
        sb.Append(bodyHtml);
        sb.Append("\n</article>\n");
        sb.Append("</body>\n");
        sb.Append("</html>\n");
        return sb.ToString();
    }

    private static void AppendInitialStyle(StringBuilder sb, ViewerAppearanceDto dto)
    {
        sb.Append("<style id=\"mdv-initial\">\n:root{color-scheme:").Append(dto.Theme).Append(';');
        foreach (var name in CssVariableNames)
        {
            if (dto.Vars.TryGetValue(name, out var value))
            {
                sb.Append(name).Append(':').Append(value).Append(';');
            }
        }

        sb.Append("}\n");

        if (dto.Columns == "auto")
        {
            if (dto.MaxColumns >= 2)
            {
                sb.Append("@media (min-width:").Append(Num(dto.TwoColumnMinWidth)).Append("px){:root{--mdv-columns:2;}}\n");
            }
        }

        sb.Append("html,body{background:var(--mdv-bg);color:var(--mdv-fg);}\n");
        sb.Append("</style>\n");
    }

    /// <summary>Accepts only #RRGGBB / #RGB so arbitrary text can never reach the inline style block.</summary>
    private static string SafeColor(string? value, string fallback) =>
        ColorMath.TryNormalizeHex(value, out var normalized) ? normalized : fallback;

    private static string Px(double value) => Num(value) + "px";

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
