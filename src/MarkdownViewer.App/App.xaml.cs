using System.Diagnostics;
using MarkdownViewer.App.Services;
using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.Services;
using MarkdownViewer.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MarkdownViewer.App;

public partial class App : Application
{
    private MainWindow? _window;

    /// <summary>Composition root. Resolved by views (MainWindow, DocumentView, SettingsPage).</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices(DispatcherQueue.GetForCurrentThread());
        UnhandledException += OnUnhandledException;

        // Exceptions that never reach the XAML handler: background threads and fire-and-forget tasks.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("AppDomain", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => CrashLog.Write("UnobservedTask", e.Exception);
    }

    private static IServiceProvider ConfigureServices(DispatcherQueue uiQueue)
    {
        var services = new ServiceCollection();

        // Platform-neutral Core services (Agent 1).
        foreach (var (service, implementation) in CoreServiceRegistry.Singletons())
        {
            services.AddSingleton(service, implementation);
        }

        services.AddSingleton<AppearanceProvider>();

        // Platform services.
        services.AddSingleton<WindowProvider>();
        services.AddSingleton<IWindowProvider>(sp => sp.GetRequiredService<WindowProvider>());
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<ThemeService>());
        services.AddSingleton<IDispatcherService>(new DispatcherService(uiQueue));
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IDefaultAppService, DefaultAppService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ILauncherService, LauncherService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDocumentTabFactory, DocumentTabFactory>();
        services.AddSingleton<ActivationService>();

        // ViewModels.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ShortcutsViewModel>();
        services.AddTransient<DocumentTabViewModel>();

        return services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        Services.GetRequiredService<WindowProvider>().Attach(_window);
        Services.GetRequiredService<ThemeService>().Attach();
        _window.Closed += OnMainWindowClosed;
        _window.Activate();

        try
        {
            await Services.GetRequiredService<ActivationService>().StartAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            await Services.GetRequiredService<IDialogService>()
                .ShowErrorAsync("Couldn't open the document", ex.Message);
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        Services.GetRequiredService<ActivationService>().Stop();
        Services.GetRequiredService<SettingsService>().Flush();
        Services.GetRequiredService<ThemeService>().Dispose();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Debug.WriteLine($"Unhandled exception: {e.Exception}");
        CrashLog.Write("UnhandledException", e.Exception);
        try
        {
            Services.GetRequiredService<SettingsService>().Flush();
        }
        catch
        {
            // Never let the crash path throw.
        }
    }
}
