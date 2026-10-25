using MarkdownViewer.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MarkdownViewer.App;

/// <summary>
/// Custom entry point (DISABLE_XAML_GENERATED_MAIN) so the app can redirect activations to the
/// already-running instance before any XAML is created — the documented WinUI 3 single-instance pattern.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (ActivationService.TryRedirectToMainInstance())
        {
            // Another instance owns the "main" key and has received our activation (files / arguments).
            return 0;
        }

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }
}
