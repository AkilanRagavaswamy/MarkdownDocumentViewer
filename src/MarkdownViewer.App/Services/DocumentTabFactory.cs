using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MarkdownViewer.App.Services;

/// <summary>Creates tab ViewModels through the container so their dependencies are injected.</summary>
public sealed class DocumentTabFactory : IDocumentTabFactory
{
    private readonly IServiceProvider _services;

    public DocumentTabFactory(IServiceProvider services)
    {
        _services = services;
    }

    public DocumentTabViewModel Create() => _services.GetRequiredService<DocumentTabViewModel>();
}
