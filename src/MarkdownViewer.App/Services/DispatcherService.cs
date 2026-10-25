using MarkdownViewer.Core.Contracts;
using Microsoft.UI.Dispatching;

namespace MarkdownViewer.App.Services;

/// <summary>Marshals work to the UI thread's <see cref="DispatcherQueue"/>.</summary>
public sealed class DispatcherService : IDispatcherService
{
    private readonly DispatcherQueue _queue;

    public DispatcherService(DispatcherQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    public void Enqueue(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.TryEnqueue(() => action());
    }
}
