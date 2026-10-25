using Microsoft.UI.Xaml.Data;

namespace MarkdownViewer.App.Converters;

/// <summary>"C:\Projects\orbit-sync\README.md" → ["C:", "Projects", "orbit-sync", "README.md"] for BreadcrumbBar.</summary>
public sealed class PathToBreadcrumbConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Split(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    public static IReadOnlyList<string> Split(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<string>();
        }

        var segments = new List<string>();
        string remainder;
        string? root;
        try
        {
            root = Path.GetPathRoot(path);
        }
        catch (ArgumentException)
        {
            root = null;
        }

        if (!string.IsNullOrEmpty(root))
        {
            // "C:\" → "C:", "\\server\share\" → "\\server\share"
            segments.Add(root.TrimEnd('\\', '/'));
            remainder = path.Substring(root.Length);
        }
        else
        {
            remainder = path;
        }

        segments.AddRange(remainder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries));
        return segments;
    }
}
