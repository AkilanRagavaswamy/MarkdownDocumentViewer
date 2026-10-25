using System.Diagnostics;
using System.Text;

namespace MarkdownViewer.App.Services;

/// <summary>
/// Appends unhandled exceptions to LocalFolder\crash.log, next to settings.json. Debug.WriteLine only
/// reaches an attached debugger, so without this a crash outside Visual Studio leaves nothing to read.
/// Writing never throws: a failure here must not stand in for the exception that caused it.
/// </summary>
internal static class CrashLog
{
    /// <summary>Past this size the file starts over, so a crash loop cannot fill the disk.</summary>
    private const long MaxBytes = 64 * 1024;

    private static readonly object s_gate = new();

    public static void Write(string source, object? error)
    {
        try
        {
            var path = Path.Combine(AppPaths.LocalFolder, "crash.log");
            lock (s_gate)
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Delete(path);
                }

                File.AppendAllText(
                    path,
                    $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {source}{Environment.NewLine}"
                        + $"{error}{Environment.NewLine}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not write the crash log: {ex.Message}");
        }
    }
}
