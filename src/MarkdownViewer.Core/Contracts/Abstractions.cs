using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MarkdownViewer.Core.Contracts;

// ---------- Platform-neutral (implemented in Core/Services by Agent 1) ----------

public interface IMarkdownRenderService
{
    /// <summary>Parses Markdown (CommonMark + GFM extensions), sanitizes HTML, adds heading ids, builds outline.</summary>
    RenderResult Render(string markdown);
}

public interface IHtmlDocumentBuilder
{
    /// <summary>Wraps rendered body HTML into a full page for WebView2 (base href, CSP, css/js links, initial appearance).</summary>
    string Build(RenderResult result, ViewerAppearance appearance, string title);

    /// <summary>Wraps raw source text in a read-only, escaped &lt;pre&gt; page ("View source").</summary>
    string BuildSource(string source, ViewerAppearance appearance, string title);

    /// <summary>JSON passed to mdv.setAppearance(...).</summary>
    string BuildAppearanceJson(ViewerAppearance appearance);
}

public interface IAccentService
{
    AccentShades Compute(string accentHex, bool isDark);
}

public interface IDensityService
{
    DensityMetrics Get(DensityPreset preset);
}

public interface IShortcutCatalog
{
    IReadOnlyList<string> Categories { get; }   // excludes "All"
    IReadOnlyList<ShortcutDefinition> All { get; }
    IReadOnlyList<KeyPart> ToParts(ShortcutDefinition definition);
}

public interface IFileWatcherService
{
    /// <summary>Raises onChanged (debounced 250 ms) when the file is modified or replaced. Dispose to stop.</summary>
    IDisposable Watch(string filePath, Action onChanged);
}

public interface ITextFileReader
{
    Task<LoadedText> ReadAsync(string path);
}

// ---------- Platform services (implemented in App/Services by Agent 3) ----------

public interface ISettingsService
{
    AppSettings Current { get; }
    void Update(Action<AppSettings> change);   // applies, persists, raises Changed
    event EventHandler<AppSettings>? Changed;
}

public interface IThemeService
{
    bool IsDark { get; }                       // effective theme
    string? WindowsAccentHex { get; }
    void Apply(AppTheme theme, string accentHex);
    event EventHandler? EffectiveThemeChanged; // system theme flip or Apply
}

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickMarkdownFilesAsync();
    Task<string?> PickFolderAsync();
    Task<string?> PickPdfSaveLocationAsync(string suggestedName);
}

public interface IFolderScanner
{
    IReadOnlyList<string> FindMarkdownFiles(string folder);
}

public interface IDefaultAppService
{
    bool IsDefaultForMarkdown();
    Task OpenDefaultAppsSettingsAsync();
}

public interface IDialogService
{
    Task ShowAboutAsync();
    Task ShowShortcutsAsync();
    Task ShowErrorAsync(string title, string message);
}

public interface ILauncherService
{
    Task OpenUriAsync(Uri uri);                // external links (http/https/mailto)
    Task OpenWithDefaultEditorAsync(string filePath);
}

public interface IClipboardService
{
    void SetText(string text);
}

public interface IDispatcherService
{
    void Enqueue(Action action);
}

public interface IDocumentTabFactory
{
    ViewModels.DocumentTabViewModel Create();
}

public static class MarkdownExtensions
{
    public static readonly IReadOnlyList<string> All = new[] { ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdx" };
}
