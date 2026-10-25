using MarkdownViewer.Core.Contracts;

namespace MarkdownViewer.Core.Services;

/// <summary>
/// The list of keyboard shortcuts shown in the shortcuts dialog. Keep in sync with the
/// KeyboardAccelerators declared on the MainWindow root.
/// </summary>
public sealed class ShortcutCatalog : IShortcutCatalog
{
    /// <summary>Category for file and tab commands.</summary>
    public const string FilesAndTabs = "Files and tabs";

    /// <summary>Category for view commands.</summary>
    public const string View = "View";

    /// <summary>Category for navigation commands.</summary>
    public const string Navigate = "Navigate";

    /// <summary>Category for find and help commands.</summary>
    public const string FindAndHelp = "Find and help";

    private static readonly IReadOnlyList<string> CategoryList = new[] { FilesAndTabs, View, Navigate, FindAndHelp };

    private static readonly IReadOnlyList<ShortcutDefinition> Definitions = new[]
    {
        Def(FilesAndTabs, "Open file", "Ctrl+O"),
        Def(FilesAndTabs, "Open folder", "Ctrl+Shift+O"),
        Def(FilesAndTabs, "Close tab", "Ctrl+W"),
        Def(FilesAndTabs, "Reopen closed tab", "Ctrl+Shift+T"),
        Def(FilesAndTabs, "Next tab", "Ctrl+Tab"),
        Def(FilesAndTabs, "Previous tab", "Ctrl+Shift+Tab"),
        Def(FilesAndTabs, "Print", "Ctrl+P"),
        Def(FilesAndTabs, "Export to PDF", "Ctrl+Shift+E"),
        Def(FilesAndTabs, "Edit in external editor", "Ctrl+E"),
        Def(FilesAndTabs, "Copy file path", "Ctrl+Shift+C"),

        Def(View, "Comfortable density", "Ctrl+1"),
        Def(View, "Compact density", "Ctrl+2"),
        Def(View, "Ultra density", "Ctrl+3"),
        Def(View, "Zoom in", "Ctrl+="),
        Def(View, "Zoom out", "Ctrl+-"),
        Def(View, "Reset zoom", "Ctrl+0"),
        Def(View, "Show or hide outline", "Ctrl+B"),
        Def(View, "Focus mode", "F11"),
        Def(View, "View source", "Ctrl+U"),
        Def(View, "Switch light or dark", "Ctrl+Shift+D"),
        Def(View, "Collapse all sections", "Ctrl+Shift+["),
        Def(View, "Expand all sections", "Ctrl+Shift+]"),

        Def(Navigate, "Scroll down", "J", "↓"),
        Def(Navigate, "Scroll up", "K", "↑"),
        Def(Navigate, "Page down", "Space", "PgDn"),
        Def(Navigate, "Page up", "Shift+Space", "PgUp"),
        Def(Navigate, "Go to top", "Home"),
        Def(Navigate, "Go to bottom", "End"),
        Def(Navigate, "Go to heading", "Ctrl+G"),
        Def(Navigate, "Back", "Alt+←"),
        Def(Navigate, "Forward", "Alt+→"),

        Def(FindAndHelp, "Find in document", "Ctrl+F"),
        Def(FindAndHelp, "Next match", "F3", "Enter"),
        Def(FindAndHelp, "Previous match", "Shift+F3"),
        Def(FindAndHelp, "Close find, Settings or focus mode", "Esc"),
        Def(FindAndHelp, "Settings", "Ctrl+,"),
        Def(FindAndHelp, "Keyboard shortcuts", "Ctrl+/", "F1"),
    };

    /// <inheritdoc />
    public IReadOnlyList<string> Categories => CategoryList;

    /// <inheritdoc />
    public IReadOnlyList<ShortcutDefinition> All => Definitions;

    /// <summary>
    /// Splits a definition into key caps and separators, e.g. "Ctrl+/ | F1" becomes
    /// [Ctrl] + [/] or [F1].
    /// </summary>
    public IReadOnlyList<KeyPart> ToParts(ShortcutDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var parts = new List<KeyPart>();
        for (var c = 0; c < definition.Combos.Count; c++)
        {
            if (c > 0)
            {
                parts.Add(new KeyPart("or", false));
            }

            var keys = definition.Combos[c];
            for (var k = 0; k < keys.Count; k++)
            {
                if (k > 0)
                {
                    parts.Add(new KeyPart("+", false));
                }

                parts.Add(new KeyPart(keys[k], true));
            }
        }

        return parts;
    }

    private static ShortcutDefinition Def(string category, string label, params string[] combos) =>
        new(category, label, combos.Select(SplitCombo).ToArray());

    /// <summary>Splits "Ctrl+Shift+O" into keys; a trailing "+" (as in "Ctrl++") is treated as the plus key.</summary>
    private static IReadOnlyList<string> SplitCombo(string combo)
    {
        var keys = new List<string>();
        var start = 0;
        for (var i = 0; i < combo.Length; i++)
        {
            if (combo[i] == '+' && i > start)
            {
                keys.Add(combo[start..i]);
                start = i + 1;
            }
        }

        if (start < combo.Length)
        {
            keys.Add(combo[start..]);
        }

        return keys;
    }
}
