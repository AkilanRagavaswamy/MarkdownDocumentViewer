using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarkdownViewer.App.Behaviors;

/// <summary>Executes a command with the invoked data item when <see cref="TreeView.ItemInvoked"/> fires.</summary>
public static class TreeViewInvokeCommand
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(TreeViewInvokeCommand),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(TreeView treeView) => (ICommand?)treeView.GetValue(CommandProperty);

    public static void SetCommand(TreeView treeView, ICommand? value) => treeView.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView treeView)
        {
            return;
        }

        treeView.ItemInvoked -= OnItemInvoked;
        if (e.NewValue is ICommand)
        {
            treeView.ItemInvoked += OnItemInvoked;
        }
    }

    private static void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var command = GetCommand(sender);
        var item = args.InvokedItem;
        if (command is not null && command.CanExecute(item))
        {
            command.Execute(item);

            // Navigating to a heading should not also toggle its expansion; the chevron still does.
            args.Handled = true;
        }
    }
}
