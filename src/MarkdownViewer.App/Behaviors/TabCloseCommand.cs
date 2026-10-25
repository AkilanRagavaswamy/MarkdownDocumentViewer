using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarkdownViewer.App.Behaviors;

/// <summary>Executes a command with the tab's data item when <see cref="TabView.TabCloseRequested"/> fires.</summary>
public static class TabCloseCommand
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(TabCloseCommand),
        new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(TabView tabView) => (ICommand?)tabView.GetValue(CommandProperty);

    public static void SetCommand(TabView tabView, ICommand? value) => tabView.SetValue(CommandProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TabView tabView)
        {
            return;
        }

        tabView.TabCloseRequested -= OnTabCloseRequested;
        if (e.NewValue is ICommand)
        {
            tabView.TabCloseRequested += OnTabCloseRequested;
        }
    }

    private static void OnTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        var command = GetCommand(sender);
        var item = args.Item;
        if (command is not null && command.CanExecute(item))
        {
            command.Execute(item);
        }
    }
}
