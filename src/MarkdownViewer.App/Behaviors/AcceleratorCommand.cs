using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace MarkdownViewer.App.Behaviors;

/// <summary>
/// Attached Command / CommandParameter for <see cref="KeyboardAccelerator"/>, which has no Command property.
/// Usage: &lt;KeyboardAccelerator Key="O" Modifiers="Control" behaviors:AcceleratorCommand.Command="{x:Bind ViewModel.OpenFileCommand, Mode=OneTime}" /&gt;
/// </summary>
public static class AcceleratorCommand
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(AcceleratorCommand),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.RegisterAttached(
        "CommandParameter",
        typeof(object),
        typeof(AcceleratorCommand),
        new PropertyMetadata(null));

    public static ICommand? GetCommand(KeyboardAccelerator accelerator) => (ICommand?)accelerator.GetValue(CommandProperty);

    public static void SetCommand(KeyboardAccelerator accelerator, ICommand? value) => accelerator.SetValue(CommandProperty, value);

    public static object? GetCommandParameter(KeyboardAccelerator accelerator) => accelerator.GetValue(CommandParameterProperty);

    public static void SetCommandParameter(KeyboardAccelerator accelerator, object? value) => accelerator.SetValue(CommandParameterProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not KeyboardAccelerator accelerator)
        {
            return;
        }

        // Static handler: removing before adding keeps exactly one subscription.
        accelerator.Invoked -= OnInvoked;
        if (e.NewValue is ICommand)
        {
            accelerator.Invoked += OnInvoked;
        }
    }

    private static void OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        var command = GetCommand(sender);
        var parameter = GetCommandParameter(sender);
        if (command is not null && command.CanExecute(parameter))
        {
            command.Execute(parameter);
            args.Handled = true;
        }
    }
}
