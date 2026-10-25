using MarkdownViewer.Core.Contracts;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MarkdownViewer.App.Services;

/// <summary>WinRT pickers parented to the main window (required for desktop apps).</summary>
public sealed class FilePickerService : IFilePickerService
{
    private readonly IWindowProvider _windows;

    public FilePickerService(IWindowProvider windows)
    {
        _windows = windows;
    }

    public async Task<IReadOnlyList<string>> PickMarkdownFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        foreach (var extension in MarkdownExtensions.All)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializeWithWindow.Initialize(picker, _windows.Hwnd);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return Array.Empty<string>();
        }

        return files.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, _windows.Hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return string.IsNullOrEmpty(folder?.Path) ? null : folder.Path;
    }

    public async Task<string?> PickPdfSaveLocationAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = string.IsNullOrWhiteSpace(suggestedName)
                ? "Document"
                : Path.GetFileNameWithoutExtension(suggestedName),
            DefaultFileExtension = ".pdf",
        };
        picker.FileTypeChoices.Add("PDF document", new List<string> { ".pdf" });

        InitializeWithWindow.Initialize(picker, _windows.Hwnd);

        var file = await picker.PickSaveFileAsync();
        return string.IsNullOrEmpty(file?.Path) ? null : file.Path;
    }
}
