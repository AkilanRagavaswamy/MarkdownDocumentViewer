using MarkdownViewer.App.Services;
using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MarkdownViewer.App.Views;

/// <summary>
/// Outline pane + reading card for the selected tab. Code-behind only owns the <see cref="WebViewBridge"/>
/// (recreated whenever <see cref="ViewModel"/> changes) and focus helpers; all behaviour is in the ViewModels.
/// </summary>
public sealed partial class DocumentView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(DocumentTabViewModel),
        typeof(DocumentView),
        new PropertyMetadata(null, OnViewModelChanged));

    public static readonly DependencyProperty IsOutlineVisibleProperty = DependencyProperty.Register(
        nameof(IsOutlineVisible),
        typeof(bool),
        typeof(DocumentView),
        new PropertyMetadata(true));

    private WebViewBridge? _bridge;

    public DocumentView()
    {
        Shell = App.Services.GetRequiredService<MainViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The shell ViewModel (for pane-level commands such as Collapse all).</summary>
    public MainViewModel Shell { get; }

    public DocumentTabViewModel? ViewModel
    {
        get => (DocumentTabViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Bound from MainWindow to MainViewModel.IsOutlinePaneOpen.</summary>
    public bool IsOutlineVisible
    {
        get => (bool)GetValue(IsOutlineVisibleProperty);
        set => SetValue(IsOutlineVisibleProperty, value);
    }

    /// <summary>Moves keyboard focus into the outline (Ctrl+G). Returns false when the outline is hidden.</summary>
    public bool FocusOutline()
    {
        if (!IsOutlineVisible || ViewModel is null)
        {
            return false;
        }

        return OutlineTree.Focus(FocusState.Keyboard);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DocumentView)d).RecreateBridge();

    private void OnLoaded(object sender, RoutedEventArgs e) => RecreateBridge();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _bridge?.Dispose();
        _bridge = null;
    }

    private void RecreateBridge()
    {
        _bridge?.Dispose();
        _bridge = null;

        if (!IsLoaded || ViewModel is not { } viewModel)
        {
            return;
        }

        _bridge = new WebViewBridge(
            Web,
            viewModel,
            App.Services.GetRequiredService<IHtmlDocumentBuilder>(),
            App.Services.GetRequiredService<IThemeService>(),
            App.Services.GetRequiredService<IDialogService>(),
            FocusFindBox,
            paths => Shell.OpenPathsAsync(paths),
            key => Shell.OnShellKey(key));
        _ = _bridge.InitializeAsync();
    }

    private void FocusFindBox()
    {
        // The find bar becomes visible in the same tick (IsFindOpen); focus after layout.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (FindBox.Focus(FocusState.Programmatic))
            {
                FindBox.SelectAll();
            }
        });
    }
}
