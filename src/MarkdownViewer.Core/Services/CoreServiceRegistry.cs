using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>
/// Lists the platform-neutral Core services so the App can register them with its DI container
/// without Core referencing Microsoft.Extensions.DependencyInjection.
/// </summary>
/// <example>
/// <code>
/// foreach (var (service, implementation) in CoreServiceRegistry.Singletons())
///     services.AddSingleton(service, implementation);
/// </code>
/// </example>
public static class CoreServiceRegistry
{
    /// <summary>Service/implementation pairs to register as singletons.</summary>
    public static IEnumerable<(Type Service, Type Implementation)> Singletons()
    {
        yield return (typeof(IMarkdownRenderService), typeof(MarkdownRenderService));
        yield return (typeof(IHtmlDocumentBuilder), typeof(HtmlDocumentBuilder));
        yield return (typeof(IAccentService), typeof(AccentService));
        yield return (typeof(IDensityService), typeof(DensityService));
        yield return (typeof(IShortcutCatalog), typeof(ShortcutCatalog));
        yield return (typeof(IFileWatcherService), typeof(FileWatcherService));
        yield return (typeof(ITextFileReader), typeof(TextFileReader));
        yield return (typeof(IFolderScanner), typeof(FolderScanner));
    }
}
