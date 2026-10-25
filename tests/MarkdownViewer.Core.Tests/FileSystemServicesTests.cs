using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.Services;

namespace MarkdownViewer.Core.Tests;

public sealed class FileSystemServicesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mdv-tests-" + Guid.NewGuid().ToString("N"));

    public FileSystemServicesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void FolderScanner_FindsMarkdown_SkipsBuildFolders_AndSorts()
    {
        Touch("b.md");
        Touch("A.markdown");
        Touch("notes.txt");
        Touch("docs/guide.MD");
        Touch(".git/ignored.md");
        Touch("node_modules/pkg/readme.md");
        Touch("bin/Debug/x.md");
        Touch("obj/y.md");

        var files = new FolderScanner().FindMarkdownFiles(_root)
            .Select(f => Path.GetRelativePath(_root, f).Replace('\\', '/'))
            .ToList();

        Assert.Equal(new[] { "A.markdown", "b.md", "docs/guide.MD" }, files);
    }

    [Fact]
    public void FolderScanner_MissingFolder_ReturnsEmpty()
    {
        Assert.Empty(new FolderScanner().FindMarkdownFiles(Path.Combine(_root, "missing")));
    }

    [Fact]
    public async Task FileWatcher_RaisesDebouncedChange()
    {
        var path = Touch("watched.md");
        var raised = 0;
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using (new FileWatcherService().Watch(path, () =>
               {
                   Interlocked.Increment(ref raised);
                   signal.TrySetResult();
               }))
        {
            // A burst of writes should collapse into one notification.
            for (var i = 0; i < 3; i++)
            {
                await File.AppendAllTextAsync(path, $"line {i}\n");
            }

            var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(signal.Task, completed);
            await Task.Delay(600);
        }

        Assert.Equal(1, Volatile.Read(ref raised));
    }

    [Fact]
    public async Task FileWatcher_DetectsReplaceViaRename()
    {
        var path = Touch("atomic.md");
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var subscription = new FileWatcherService().Watch(path, () => signal.TrySetResult());
        var temp = Path.Combine(_root, "atomic.md.tmp");
        await File.WriteAllTextAsync(temp, "# New\n");
        File.Move(temp, path, overwrite: true);

        var completed = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(signal.Task, completed);
    }

    [Fact]
    public async Task FileWatcher_NoCallbackAfterDispose()
    {
        var path = Touch("disposed.md");
        var raised = false;

        new FileWatcherService().Watch(path, () => raised = true).Dispose();
        await File.AppendAllTextAsync(path, "more\n");
        await Task.Delay(600);

        Assert.False(raised);
    }

    [Fact]
    public void Registry_ListsEveryCoreServiceOnce()
    {
        var pairs = CoreServiceRegistry.Singletons().ToList();

        Assert.Equal(pairs.Count, pairs.Select(p => p.Service).Distinct().Count());
        Assert.All(pairs, p => Assert.True(p.Service.IsAssignableFrom(p.Implementation)));
        Assert.Contains(pairs, p => p.Service == typeof(IMarkdownRenderService));
        Assert.Contains(pairs, p => p.Service == typeof(IFolderScanner));
    }

    private string Touch(string relative)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "# x\n");
        return full;
    }
}
