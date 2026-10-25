using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.ViewModels;

/// <summary>Backs the keyboard shortcuts dialog: category filter + free-text search.</summary>
public sealed partial class ShortcutsViewModel : ObservableObject
{
    public const string AllCategory = "All";

    private readonly IShortcutCatalog _catalog;

    [ObservableProperty]
    private string _selectedCategory = AllCategory;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private int _count;

    public ShortcutsViewModel(IShortcutCatalog catalog)
    {
        _catalog = catalog;
        Categories = new[] { AllCategory }.Concat(catalog.Categories).ToList();
        Rebuild();
    }

    public IReadOnlyList<string> Categories { get; }

    public ObservableCollection<ShortcutGroupViewModel> Groups { get; } = new();

    public bool IsEmpty => Count == 0;

    partial void OnSelectedCategoryChanged(string value) => Rebuild();

    partial void OnSearchTextChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var category = string.IsNullOrEmpty(SelectedCategory) ? AllCategory : SelectedCategory;
        var query = (SearchText ?? string.Empty).Trim();
        var keyQuery = string.Concat(query.Where(c => !char.IsWhiteSpace(c)));

        var matches = _catalog.All
            .Where(d => category == AllCategory || string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(d => query.Length == 0 || Matches(d, query, keyQuery))
            .ToList();

        // Keep catalog category order; unknown categories go last in first-seen order.
        var order = _catalog.Categories.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i, StringComparer.OrdinalIgnoreCase);

        Groups.Clear();
        foreach (var group in matches
                     .GroupBy(d => d.Category, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => order.TryGetValue(g.Key, out var i) ? i : int.MaxValue))
        {
            var items = group.Select(d => new ShortcutItemViewModel(d.Label, _catalog.ToParts(d))).ToList();
            Groups.Add(new ShortcutGroupViewModel(group.Key, items));
        }

        Count = matches.Count;
    }

    private static bool Matches(ShortcutDefinition definition, string query, string keyQuery)
    {
        if (definition.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (keyQuery.Length == 0)
        {
            return false;
        }

        return definition.Combos.Any(combo =>
            string.Join("+", combo).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Contains(keyQuery, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>A category heading with its shortcuts.</summary>
public sealed class ShortcutGroupViewModel
{
    public ShortcutGroupViewModel(string title, IReadOnlyList<ShortcutItemViewModel> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }

    public IReadOnlyList<ShortcutItemViewModel> Items { get; }
}

/// <summary>One shortcut row: label plus key caps / separators.</summary>
public sealed class ShortcutItemViewModel
{
    public ShortcutItemViewModel(string label, IReadOnlyList<KeyPart> parts)
    {
        Label = label;
        Parts = parts;
    }

    public string Label { get; }

    public IReadOnlyList<KeyPart> Parts { get; }

    /// <summary>Plain-text form for screen readers, e.g. "Find, Ctrl + F".</summary>
    public string AutomationName => $"{Label}, {string.Join(" ", Parts.Select(p => p.Text))}";
}
