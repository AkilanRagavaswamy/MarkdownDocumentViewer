using System.Diagnostics;
using MarkdownViewer.Core.Contracts;
using Windows.ApplicationModel.DataTransfer;

namespace MarkdownViewer.App.Services;

public sealed class ClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text ?? string.Empty);
        try
        {
            Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process; failing to copy must not crash the app.
            Debug.WriteLine($"Clipboard.SetContent failed: {ex.Message}");
        }
    }
}
