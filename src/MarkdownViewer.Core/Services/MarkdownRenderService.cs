using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.Tables;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>
/// Markdown to sanitized HTML using Markdig (CommonMark + GFM extensions) and Ganss.Xss.
/// Also extracts YAML front matter, the H1–H4 outline and a word count.
/// </summary>
public sealed partial class MarkdownRenderService : IMarkdownRenderService
{
    /// <summary>Deepest heading level included in the outline.</summary>
    public const int MaxOutlineLevel = 4;

    private readonly MarkdownPipeline _pipeline;
    private readonly MarkdownPipeline _withoutPipeTables;
    private readonly HtmlSanitizer _sanitizer;

    /// <summary>Creates the service with the shared Markdig pipeline and sanitizer configuration.</summary>
    public MarkdownRenderService()
    {
        _pipeline = CreatePipeline();
        _withoutPipeTables = CreatePipelineWithoutPipeTables();
        _sanitizer = CreateSanitizer();
    }

    /// <summary>
    /// Shown when blank lines between a |---| row and the rows beneath it had to be removed. Those
    /// blank lines end the block, so the rows are not a table in any renderer — just a paragraph of
    /// pipe characters, and a wide one also trips Markdig's 128-delimiter nesting cap.
    /// </summary>
    internal const string RepairedTableNotice =
        "This document has blank lines between table |---| rows and the rows beneath them, which breaks "
        + "those tables. They are shown here as tables anyway; delete the blank lines to fix the file.";

    /// <summary>Shown when even the repair was not enough and pipe tables had to be switched off.</summary>
    internal const string MalformedTableNotice =
        "Part of this document nests too deeply for the Markdown parser, so tables are shown as plain text.";

    /// <inheritdoc />
    public RenderResult Render(string markdown)
    {
        markdown ??= string.Empty;

        try
        {
            return Render(markdown, _pipeline, notice: null);
        }
        catch (ArgumentException)
        {
            // Markdig caps inline nesting at 128 delimiters per paragraph. Nothing legitimate reaches
            // that, but an orphaned table body does, so try the document again with the orphans
            // re-attached, and only if that still fails give up the tables to keep the document.
            var repaired = RepairOrphanedTableRows(markdown);
            if (!ReferenceEquals(repaired, markdown))
            {
                try
                {
                    return Render(repaired, _pipeline, RepairedTableNotice);
                }
                catch (ArgumentException)
                {
                    // Fall through: something else in the document is too deeply nested.
                }
            }

            // If this throws too, the caller sees the original failure.
            return Render(markdown, _withoutPipeTables, MalformedTableNotice);
        }
    }

    /// <summary>
    /// Removes blank lines sitting between a table delimiter row and the rows that follow it. A
    /// delimiter row only ever belongs to a table, so rows separated from one by nothing but blank
    /// lines were meant to be its body. Returns the original string when there is nothing to repair.
    /// </summary>
    internal static string RepairOrphanedTableRows(string markdown)
    {
        if (string.IsNullOrEmpty(markdown) || !markdown.Contains('|'))
        {
            return markdown;
        }

        var lines = markdown.Split('\n');
        var kept = new List<string>(lines.Length);
        var repaired = false;

        for (var i = 0; i < lines.Length; i++)
        {
            kept.Add(lines[i]);
            if (!TableDelimiterRow().IsMatch(lines[i].TrimEnd('\r')))
            {
                continue;
            }

            // Look past the blank lines: only drop them when table rows actually follow.
            var next = i + 1;
            while (next < lines.Length && lines[next].Trim().Length == 0)
            {
                next++;
            }

            if (next > i + 1 && next < lines.Length && lines[next].TrimStart().StartsWith('|'))
            {
                i = next - 1;
                repaired = true;
            }
        }

        return repaired ? string.Join("\n", kept) : markdown;
    }

    /// <summary>Matches a GFM table delimiter row, e.g. <c>|---|:--:|</c>.</summary>
    [GeneratedRegex(@"^\s*\|(?:\s*:?-+:?\s*\|)+\s*$")]
    private static partial Regex TableDelimiterRow();

    private RenderResult Render(string markdown, MarkdownPipeline pipeline, string? notice)
    {
        var document = Markdown.Parse(markdown, pipeline);

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        var rawHtml = writer.ToString();
        string safeHtml;
        lock (_sanitizer)
        {
            safeHtml = _sanitizer.Sanitize(rawHtml);
        }

        var frontMatter = ExtractFrontMatter(document);
        var outline = BuildOutline(document);
        var words = CountWords(safeHtml);

        return new RenderResult(safeHtml, outline, words, frontMatter, notice);
    }

    /// <summary>Builds the Markdig pipeline. Auto identifiers are registered first so the GitHub style wins.</summary>
    internal static MarkdownPipeline CreatePipeline() => PipelineBuilder().Build();

    /// <summary>The same pipeline without pipe tables, for documents the full one cannot parse.</summary>
    internal static MarkdownPipeline CreatePipelineWithoutPipeTables()
    {
        var builder = PipelineBuilder();
        builder.Extensions.RemoveAll(extension => extension is PipeTableExtension);
        return builder.Build();
    }

    private static MarkdownPipelineBuilder PipelineBuilder() =>
        new MarkdownPipelineBuilder()
            // Must precede UseAdvancedExtensions: extensions are added only if not already present.
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseAdvancedExtensions()
            .UseAlertBlocks()
            .UseYamlFrontMatter()
            .UseEmojiAndSmiley();

    /// <summary>Creates an allow-list sanitizer suited to Markdig output.</summary>
    internal static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "a", "abbr", "b", "blockquote", "br", "caption", "cite", "code", "col", "colgroup",
                     "dd", "del", "details", "dfn", "div", "dl", "dt", "em", "figcaption", "figure",
                     "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "img", "input", "ins", "kbd",
                     "li", "mark", "ol", "p", "pre", "q", "s", "samp", "small", "span", "strike",
                     "strong", "sub", "summary", "sup", "table", "tbody", "td", "tfoot", "th",
                     "thead", "tr", "u", "ul", "var",
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[]
                 {
                     "id", "class", "href", "title", "src", "alt", "width", "height",
                     "type", "checked", "disabled", "open",
                     "align", "colspan", "rowspan", "scope", "start", "reversed", "value",
                     "lang", "dir", "style",
                 })
        {
            sanitizer.AllowedAttributes.Add(attribute);
        }

        // Only pipe-table alignment survives in style attributes.
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedCssProperties.Add("text-align");

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement element && string.Equals(element.LocalName, "input", StringComparison.OrdinalIgnoreCase))
            {
                // Only read-only checkboxes (task lists) are rendered. Any other input type is
                // neutralized in place rather than removed, so the node tree is not restructured
                // while the sanitizer is iterating it.
                element.SetAttribute("type", "checkbox");
                element.SetAttribute("disabled", "disabled");
                element.RemoveAttribute("value");
            }
        };

        return sanitizer;
    }

    /// <summary>Reads top-level <c>key: value</c> pairs from the YAML front matter block, if any.</summary>
    internal static IReadOnlyDictionary<string, string>? ExtractFrontMatter(MarkdownDocument document)
    {
        var block = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (block is null)
        {
            return null;
        }

        return ParseFrontMatter(block.Lines.ToString());
    }

    /// <summary>Parses top-level <c>key: value</c> lines; nested, list and empty-valued keys are ignored.</summary>
    internal static IReadOnlyDictionary<string, string> ParseFrontMatter(string yaml)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(yaml);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line[0] is '#' or '-' or '.')
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0 || value is "|" or ">" or "|-" or ">-")
            {
                continue;
            }

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            result[key] = value;
        }

        return result;
    }

    /// <summary>Builds a nested outline of H1–H4 headings using the generated heading ids.</summary>
    internal static IReadOnlyList<OutlineNode> BuildOutline(MarkdownDocument document)
    {
        var roots = new List<OutlineBuilder>();
        var stack = new Stack<OutlineBuilder>();

        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            if (heading.Level < 1 || heading.Level > MaxOutlineLevel)
            {
                continue;
            }

            var id = heading.GetAttributes().Id;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var title = CollapseWhitespace(GetInlineText(heading.Inline));
            var node = new OutlineBuilder(heading.Level, title, id);

            while (stack.Count > 0 && stack.Peek().Level >= heading.Level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                roots.Add(node);
            }
            else
            {
                stack.Peek().Children.Add(node);
            }

            stack.Push(node);
        }

        return roots.Select(r => r.ToNode()).ToArray();
    }

    /// <summary>Counts words in the visible text of an HTML fragment.</summary>
    internal static int CountWords(string html)
    {
        var text = WebUtility.HtmlDecode(TagRegex().Replace(html, " "));
        var count = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
        }

        return count;
    }

    private static string GetInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        AppendInlineText(container, sb);
        return sb.ToString();
    }

    private static void AppendInlineText(Inline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline literal:
                sb.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case HtmlEntityInline entity:
                sb.Append(entity.Transcoded.ToString());
                break;
            case AutolinkInline autolink:
                sb.Append(autolink.Url);
                break;
            case LineBreakInline:
                sb.Append(' ');
                break;
            case ContainerInline container:
                foreach (var child in container)
                {
                    AppendInlineText(child, sb);
                }

                break;
        }
    }

    private static string CollapseWhitespace(string value) => WhitespaceRegex().Replace(value, " ").Trim();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    private sealed class OutlineBuilder
    {
        public OutlineBuilder(int level, string title, string id)
        {
            Level = level;
            Title = title;
            Id = id;
        }

        public int Level { get; }

        public string Title { get; }

        public string Id { get; }

        public List<OutlineBuilder> Children { get; } = new();

        public OutlineNode ToNode() => new(Level, Title, Id, Children.Select(c => c.ToNode()).ToArray());
    }
}
