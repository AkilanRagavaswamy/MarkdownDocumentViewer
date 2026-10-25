using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>Recursively finds Markdown files below a folder, skipping build and VCS folders.</summary>
public sealed class FolderScanner : IFolderScanner
{
    /// <summary>Maximum number of files returned.</summary>
    public const int MaxFiles = 2000;

    private static readonly HashSet<string> SkippedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", "bin", "obj" };

    private static readonly HashSet<string> Extensions =
        new(MarkdownExtensions.All, StringComparer.OrdinalIgnoreCase);

    private static readonly EnumerationOptions FileOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.System,
    };

    private static readonly EnumerationOptions DirectoryOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
    };

    /// <summary>Returns up to <see cref="MaxFiles"/> Markdown file paths, sorted case-insensitively.</summary>
    public IReadOnlyList<string> FindMarkdownFiles(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(folder));

        while (pending.Count > 0 && results.Count < MaxFiles)
        {
            var current = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current, "*", FileOptions).Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (Extensions.Contains(Path.GetExtension(file)))
                    {
                        results.Add(file);
                        if (results.Count >= MaxFiles)
                        {
                            break;
                        }
                    }
                }

                // Push in reverse order so subfolders are visited alphabetically.
                var subdirectories = Directory.EnumerateDirectories(current, "*", DirectoryOptions)
                    .Where(d => !SkippedDirectories.Contains(Path.GetFileName(d)))
                    .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase);
                foreach (var sub in subdirectories)
                {
                    pending.Push(sub);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Skip folders that vanish or cannot be read.
            }
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }
}
