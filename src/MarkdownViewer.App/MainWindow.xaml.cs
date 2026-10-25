using MarkdownViewer.App.Helpers;
using MarkdownViewer.App.Views;
using MarkdownViewer.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace MarkdownViewer.App;

/// <summary>
/// Shell window. Code-behind only wires view-only concerns: title bar regions, minimum size,
/// hosting the Settings page, and Ctrl+G focus. Everything else is bound to <see cref="MainViewModel"/>.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int MinWidthDips = 800;
    private const int MinHeightDips = 500;

    private readonly TitleBarHelper _titleBar;
    private readonly WindowSizeHelper _sizeHelper;

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();

        _titleBar = new TitleBarHelper(this, DragRegion, LeftInsetColumn, RightInsetColumn, TitleBarButtons, TitleBarLeading);
        _sizeHelper = new WindowSizeHelper(this, MinWidthDips, MinHeightDips);
        _sizeHelper.ResizeDips(1280, 800);

        // Open maximized; the size above is what Restore returns to.
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        // Taskbar / Alt+Tab icon (the packaged tile icons come from Package.appxmanifest).
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        SettingsFrame.Navigate(typeof(SettingsPage));

        Closed += OnClosed;
    }

    public MainViewModel ViewModel { get; }

    private void OnFocusOutlineInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.HasTabs || ViewModel.IsSettingsOpen)
        {
            return;
        }

        args.Handled = true;
        if (!ViewModel.IsOutlinePaneOpen && ViewModel.ToggleOutlineCommand.CanExecute(null))
        {
            ViewModel.ToggleOutlineCommand.Execute(null);
        }

        // Focus after the pane has been laid out.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => DocView.FocusOutline());
    }

    // Tapping the already-selected tab raises no SelectionChanged, so the tap itself has to leave the start screen.
    private void OnTabStripTapped(object sender, TappedRoutedEventArgs e) => ViewModel.LeaveStartScreen();

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _titleBar.Dispose();
        _sizeHelper.Dispose();
    }
}
