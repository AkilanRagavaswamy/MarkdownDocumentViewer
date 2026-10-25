# Markdown Viewer - Simple — implementation contract

Every agent MUST follow this file. Shared types already exist in `src/MarkdownViewer.Core/Contracts/` — do not redefine or rename them; implement against them.

## Solution layout

```
MarkdownViewerSimple.sln
src/MarkdownViewer.Core/            net8.0 class library. NO WinUI / Windows references. Fully unit-testable.
  Contracts/                        (given) Models.cs, Abstractions.cs, Messages.cs
  Services/                         platform-neutral implementations (Agent 1)
  ViewModels/                       all ViewModels (Agent 2)
src/MarkdownViewer.App/             WinUI 3 packaged app, net8.0-windows10.0.19041.0 (Agents 3 + 4)
  App.xaml(.cs), MainWindow.xaml(.cs), Package.appxmanifest, app.manifest
  Views/                            DocumentView, SettingsPage, ShortcutsDialog
  Services/                         WinUI implementations of Core abstractions
  Converters/, Behaviors/, Themes/
  Assets/                           MSIX icons (Agent 4), Assets/Web/ viewer.html/css/js (Agent 4)
tests/MarkdownViewer.Core.Tests/    xUnit (Agent 1)
```

Root namespace per project = project name. Folders map to namespaces (`MarkdownViewer.Core.ViewModels`, `MarkdownViewer.App.Views`, ...).

## Packages (pin these versions)

- Core: `Markdig` 0.38.0, `CommunityToolkit.Mvvm` 8.3.2, `HtmlSanitizer` 8.1.870
- App: `Microsoft.WindowsAppSDK` 1.6.250108002, `Microsoft.Windows.SDK.BuildTools` 10.0.26100.1742, `Microsoft.Extensions.DependencyInjection` 8.0.1, `CommunityToolkit.Mvvm` 8.3.2, `Microsoft.Web.WebView2` comes with WindowsAppSDK
- Tests: `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.11.1

## Strict MVVM rules

1. ViewModels live only in Core, depend only on Contracts abstractions + CommunityToolkit.Mvvm. No `Microsoft.UI.*`, no `Windows.*` types in Core.
2. Use `[ObservableProperty]`, `[RelayCommand]` source generators; ViewModels are `partial` classes deriving from `ObservableObject` (or `ObservableRecipient` when they receive messages).
3. Views bind with `x:Bind` (Mode=OneWay/TwoWay). Code-behind only: InitializeComponent, `ViewModel` property, and wiring view-only controls (WebView2, TitleBar drag regions) to the ViewModel through the bridge in rule 4. No business logic in code-behind.
4. ViewModel → WebView actions go through `WeakReferenceMessenger.Default` messages in `Contracts/Messages.cs` with `TabId` token checks. WebView → ViewModel events call public ViewModel methods listed below (`OnHeadingChanged`, `OnFindResult`, `OnLinkActivated`, `OnViewerReady`).
5. Keyboard shortcuts are `KeyboardAccelerator`s in XAML bound to ViewModel commands (MainWindow root). The shortcut LIST shown in the dialog comes from `IShortcutCatalog` — keep both in sync with `ShortcutCatalog`.
6. DI: `Microsoft.Extensions.DependencyInjection`, composed in `App.xaml.cs`. Services singletons; `MainViewModel`, `SettingsViewModel` singletons; `ShortcutsViewModel` transient; `DocumentTabViewModel` created through `IDocumentTabFactory`.

## ViewModel surface (Agent 2 implements; Agents 3/4 bind to exactly these names)

### MainViewModel : ObservableRecipient
Properties: `ObservableCollection<DocumentTabViewModel> Tabs`, `DocumentTabViewModel? SelectedTab`, `bool IsBannerVisible`, `bool IsOutlinePaneOpen` (default true), `bool IsFocusMode`, `bool IsDarkTheme`, `bool IsSettingsOpen`, `bool IsHomeOpen`, `bool IsDocumentVisible`, `bool IsStartScreenVisible`, `DensityPreset Density`, `string DensityLabel`, `ColumnMode ColumnMode`, `string ColumnsLabel`, `string ZoomLabel` ("100%"), `bool HasTabs`, `bool IsFindOpen` (proxy to SelectedTab).
Commands (RelayCommand names → generated `XxxCommand`): `OpenFile`, `OpenFolder`, `CloseTab(DocumentTabViewModel?)`, `ReopenClosedTab`, `NextTab`, `PreviousTab`, `ToggleTheme`, `ShowShortcuts`, `OpenSettings`, `CloseSettings`, `GoHome`, `Escape`, `ShowAbout`, `SetDensity(DensityPreset)`, `SetColumns(ColumnMode)`, `ZoomIn`, `ZoomOut`, `ResetZoom`, `ToggleOutline`, `ToggleFocusMode`, `OpenFind`, `CloseFind`, `FindNext`, `FindPrevious`, `Print`, `ExportPdf`, `ToggleSource`, `GoBack`, `GoForward`, `CollapseAll`, `ExpandAll`, `OpenDefaultApps`, `DismissBanner`, `CopyFilePath`, `EditExternally`.
Methods: `Task OpenPathsAsync(IEnumerable<string> paths)` (activation / drag-drop / command line), `Task InitializeAsync()`.

### DocumentTabViewModel : ObservableRecipient
`Guid Id`, `string FilePath`, `string Title`, `string Html`, `string SourceText`, `bool ShowSource`, `ObservableCollection<OutlineItemViewModel> Outline`, `string? CurrentHeadingId`, `string CurrentSectionTitle`, `int WordCount`, `string StatsText` ("612 words · 3 min read"), `string EncodingLabel`, `string LineEndingLabel`, `double ZoomFactor` (0.5–3.0), `bool IsFindOpen`, `string FindQuery`, `string FindStatus` ("2 of 14"), `bool IsLoading`, `string? ErrorMessage`, `bool CanGoBack`, `bool CanGoForward`, `string DocumentFolder`.
Commands: `Reload`, `FindNext`, `FindPrevious`, `CloseFind`, `NavigateToHeading(OutlineItemViewModel)`, `GoBack`, `GoForward`.
Methods called by view: `OnViewerReady()`, `OnHeadingChanged(string anchorId)`, `OnFindResult(int current, int total)`, `OnLinkActivated(string href)`, `Task LoadAsync(string path)`.

### OutlineItemViewModel : ObservableObject
`string Title`, `int Level`, `string AnchorId`, `ObservableCollection<OutlineItemViewModel> Children`, `bool IsCurrent`, `bool IsExpanded`.

### SettingsViewModel : ObservableRecipient
`AppTheme SelectedTheme` (+ `bool IsLightSelected/IsDarkSelected/IsSystemSelected` two-way for RadioButtons), `ObservableCollection<AccentOptionViewModel> AccentOptions`, `bool UseWindowsAccent`, `string CustomAccentHex`, `DensityPreset Density` (+ `IsComfortable/IsCompact/IsUltra`), `ColumnMode ColumnMode`, `IReadOnlyList<ColumnMode> ColumnModes`, `bool WrapCodeLines`, `bool LoadRemoteContent`, `bool IsDefaultApp`, `string DefaultAppStatus`, `AccentShades PreviewShades`.
Commands: `SelectAccent(AccentOptionViewModel)`, `ApplyCustomAccent(string hex)`, `OpenDefaultApps`, `ShowShortcuts`, `ResetToDefaults`.

### AccentOptionViewModel : ObservableObject
`string Name`, `string Hex`, `bool IsSelected`, `string CheckGlyphColorHex`, `string AutomationName` ("Blue #546DE5").

### ShortcutsViewModel : ObservableObject
`IReadOnlyList<string> Categories` ("All", ...), `string SelectedCategory`, `string SearchText`, `ObservableCollection<ShortcutGroupViewModel> Groups`, `int Count`, `bool IsEmpty`.
`ShortcutGroupViewModel { string Title; IReadOnlyList<ShortcutItemViewModel> Items }`, `ShortcutItemViewModel { string Label; IReadOnlyList<KeyPart> Parts }` — KeyPart in Contracts.

## WebView bridge (Agent 3 host side, Agent 4 JS side)

Virtual hosts: `https://appassets.mdv/` → `Assets/Web`; `https://doc.mdv/` → the open document's folder (per WebView). `IHtmlDocumentBuilder.Build` emits `<base href="https://doc.mdv/">` and links `https://appassets.mdv/viewer.css`, `viewer.js`, `highlight.min.js`, `highlight.css`.

Host → JS (`ExecuteScriptAsync`): `mdv.setAppearance(json)` with `ViewerAppearanceDto`, `mdv.scrollToAnchor(id)`, `mdv.find(query, backwards)`, `mdv.clearFind()`, `mdv.collapseAll()`, `mdv.expandAll()`, `mdv.getScrollY()` / `mdv.setScrollY(y)` (keep position on live reload).
JS → Host (`window.chrome.webview.postMessage(obj)`): `{type:"ready"}`, `{type:"heading", id}`, `{type:"find", current, total}`, `{type:"link", href}`, `{type:"scroll", y}`, `{type:"key", key}` (F11 / Escape forwarded because XAML accelerators do not fire while the WebView has focus).
CSS custom properties set by `setAppearance`: `--mdv-bg --mdv-fg --mdv-fg2 --mdv-border --mdv-code-bg --mdv-accent-fill --mdv-on-accent --mdv-accent-text --mdv-accent-tint --mdv-mark --mdv-font-size --mdv-line-height --mdv-gap --mdv-h1 --mdv-h2 --mdv-h3 --mdv-code-size --mdv-pad --mdv-columns`; `<html data-theme="light|dark" data-wrap="true|false">`.

## Design tokens (from the approved UI design)

Accents: Blue #546DE5, Lavender #9559ED, Green #2EAD6B, Pink #FF4081, Orange #F76831, Teal #11A3A3, Crimson #E5484D, Gold #E0A100, Graphite #6B7785 (+ Windows accent, + custom). Shades: fill = accent darkened (light theme) / lightened (dark theme) until 4.5:1 against white/black text; text = accent adjusted to 4.5:1 on reading surface; tint = 12% (light) / 22% (dark) mix into layer.
Light: mica #F3F3F3, layer #FAFAFA, reading #FFFFFF, stroke #E3E3E3, text #1B1B1B, text2 #5C5C5C, code #F5F6F8. Dark: mica #202020, layer #2B2B2B, reading #1C1C1C, stroke #3B3B3B, text #FFFFFF, text2 #C5C5C5, code #262626.
Density: Comfortable 16px/1.6/gap 12/h1 30/h2 22/h3 17/code 14/pad 32×48/1 col; Compact 14/1.42/7/21/16.5/14.5/12.5/18×28/auto 2 (≥1400px); Ultra 12.5/1.32/5/18/14.5/13/11.5/12×20/auto 2 (≥1400px). Columns: Auto, 1 or 2.
Title bar (32px, ExtendsContentIntoTitleBar, Standard caption-button height): app logo button (About) and Home button, TabView tabs (the add button shows only once a tab is open), then right side: Theme toggle (moon in light, sun in dark), Keyboard shortcuts, Settings, then system caption buttons (reserve right inset). CommandBar row 44px: Outline toggle, Back, Forward, breadcrumb path, Density DropDownButton, Columns DropDownButton, Zoom −/100%/+, Find, Focus mode, More (Print, Export PDF, View source, Copy path, Edit in…, Open folder). InfoBar for default app. Body: Outline TreeView 236px + reading WebView2 card (top-left radius 8). Status bar 28px.
