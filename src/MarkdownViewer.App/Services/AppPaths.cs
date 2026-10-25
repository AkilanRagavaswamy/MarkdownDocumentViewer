using Windows.Storage;

namespace MarkdownViewer.App.Services;

/// <summary>Per-user storage folders. Uses the package's ApplicationData, falling back to %LOCALAPPDATA% when unpackaged.</summary>
internal static class AppPaths
{
    private static readonly Lazy<string> s_local = new(() => Resolve(() => ApplicationData.Current.LocalFolder.Path, "Local"));
    private static readonly Lazy<string> s_cache = new(() => Resolve(() => ApplicationData.Current.LocalCacheFolder.Path, "LocalCache"));

    public static string LocalFolder => s_local.Value;

    public static string LocalCacheFolder => s_cache.Value;

    public static string WebAssetsFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "Web");

    private static string Resolve(Func<string> packaged, string fallbackName)
    {
        string path;
        try
        {
            path = packaged();
        }
        catch (Exception)
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarkdownViewerSimple",
                fallbackName);
        }

        Directory.CreateDirectory(path);
        return path;
    }
}
