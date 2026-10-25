using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.ViewModels;

/// <summary>One heading in the outline tree.</summary>
public sealed partial class OutlineItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isExpanded;

    public OutlineItemViewModel(string title, int level, string anchorId)
    {
        Title = title;
        Level = level;
        AnchorId = anchorId;
        IsExpanded = level <= 2;
    }

    public string Title { get; }

    public int Level { get; }

    public string AnchorId { get; }

    public ObservableCollection<OutlineItemViewModel> Children { get; } = new();

    public bool HasChildren => Children.Count > 0;

    /// <summary>Builds a view-model subtree from a rendered outline node.</summary>
    /// <param name="expandedState">Previous expansion state by anchor id (kept across live reloads).</param>
    public static OutlineItemViewModel FromNode(OutlineNode node, IReadOnlyDictionary<string, bool>? expandedState = null)
    {
        var item = new OutlineItemViewModel(node.Title, node.Level, node.AnchorId);
        if (expandedState is not null && expandedState.TryGetValue(node.AnchorId, out var expanded))
        {
            item.IsExpanded = expanded;
        }

        foreach (var child in node.Children)
        {
            item.Children.Add(FromNode(child, expandedState));
        }

        return item;
    }

    /// <summary>This item followed by all of its descendants (depth-first).</summary>
    public IEnumerable<OutlineItemViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var d in child.SelfAndDescendants())
            {
                yield return d;
            }
        }
    }
}
