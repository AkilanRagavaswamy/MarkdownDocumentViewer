| Close find, Settings or focus mode | Esc |# Markdown Viewer - Simple

A fast, private, read-only Markdown viewer for Windows 10 and 11, built with WinUI 3 (Windows App SDK 1.6) and .NET 8.

- Tabs, a document outline, find in page, back and forward between linked documents
- Three reading densities (Comfortable, Compact, Ultra) and automatic multi-column layout on wide windows
- GitHub Flavored Markdown: tables, task lists, footnotes, alerts (`> [!NOTE]`), syntax-highlighted code
- Live reload when the file changes on disk, fold and unfold sections, print and export to PDF
- Light, dark or system theme with nine accent colors, the Windows accent color or a custom color
- Fully offline. No telemetry, no account, and remote images are blocked unless you turn them on

## Solution layout

```
MarkdownViewerSimple.sln
CONTRACT.md                         Binding contract between layers (ViewModel surface, WebView bridge, design tokens)
src/
  MarkdownViewer.Core/              net8.0 class library. No WinUI or Windows references; fully unit-testable
    Contracts/                      Shared models, service interfaces and messenger messages
    Services/                       Markdown rendering (Markdig + HtmlSanitizer), HTML page builder, accents,
                                    density metrics, shortcut catalog, file watcher, text reader
    ViewModels/                     MainViewModel, DocumentTabViewModel, OutlineItemViewModel,
                                    SettingsViewModel, AccentOptionViewModel, ShortcutsViewModel
  MarkdownViewer.App/               WinUI 3 packaged (MSIX) app
    App.xaml(.cs), Program.cs       Composition root (DI) and single-instance activation
    MainWindow.xaml(.cs)            Title bar with tabs, command bar, outline, status bar, keyboard accelerators
    Views/                          DocumentView (WebView2 host), SettingsPage, ShortcutsDialog
    Services/                       WinUI implementations of the Core abstractions
    Converters/, Behaviors/, Themes/
    Assets/                         MSIX icons and tiles
    Assets/Web/                     viewer.css, viewer.js, highlight.min.js, highlight.css (the reading surface)
    Package.appxmanifest
tests/
  MarkdownViewer.Core.Tests/        xUnit tests for Core services and ViewModels
```

## Prerequisites

- Windows 10 version 1809 (build 17763) or later; Windows 11 recommended
- Visual Studio 2022 version 17.10 or later with these workloads:
  - **.NET desktop development**
  - **Windows application development** (includes the WinUI application development tools and the
    Windows App SDK C# templates)
- .NET 8 SDK
- Windows App SDK 1.6 (restored automatically from NuGet: `Microsoft.WindowsAppSDK` 1.6.250108002)
- WebView2 Runtime (included in Windows 11 and current Windows 10 builds)

## Build and run

1. Open `MarkdownViewerSimple.sln` in Visual Studio.
2. Right-click **MarkdownViewer.App** and choose **Set as Startup Project**.
3. Choose the **x64** platform (or **ARM64** on an Arm PC) and the **MarkdownViewer.App (Package)** launch profile.
4. Press **F5**. Visual Studio restores NuGet packages, deploys the MSIX package locally and starts the app.

To open a file from a terminal after the package is deployed, use the execution alias:

```powershell
mdviewer README.md
```

## Run the tests

The Core library has no Windows dependencies, so the tests run anywhere the .NET 8 SDK is installed:

```powershell
dotnet test tests/MarkdownViewer.Core.Tests/MarkdownViewer.Core.Tests.csproj
```

## Make it the default app for Markdown

Windows does not let apps make themselves the default; the user confirms it in Settings.

1. Install the app (F5 from Visual Studio, a sideloaded package or the Microsoft Store).
2. In the app, select **Open Default apps** on the banner, or go to **Settings > Default app > Open Default apps**.
3. In Windows Settings, find **Markdown Viewer - Simple** and set it as the default for `.md`
   (and any of `.markdown`, `.mdown`, `.mkd`, `.mkdn`, `.mdwn`, `.mdx`).
4. Return to the app. The Settings page re-checks the association each time it is shown.

The registered extensions come from `MarkdownExtensions.All` in `src/MarkdownViewer.Core/Contracts/Abstractions.cs`
and are listed in `Package.appxmanifest`; keep the two in sync.

## Package for the Microsoft Store

1. Reserve the name **Markdown Viewer - Simple** in [Partner Center](https://partner.microsoft.com/dashboard).
2. In Visual Studio, right-click **MarkdownViewer.App** and choose **Package and Publish > Associate App with the Store**.
   Sign in and pick the reserved name. This replaces the placeholder `Identity` (`Name`, `Publisher="CN=YourPublisher"`)
   and `PublisherDisplayName` in `Package.appxmanifest` with your Partner Center values.
3. Update `Version` in `Package.appxmanifest` (the Store requires the fourth part to be 0, for example `1.0.1.0`).
4. Choose **Package and Publish > Create App Packages**, select **Microsoft Store as ...**, tick **x64** and **ARM64**
   (and x86 if you want it) in **Release** configuration, and generate an `.msixupload`.
5. Let the wizard run the **Windows App Certification Kit (WACK)** on the package and fix any failures.
6. Upload the `.msixupload` in Partner Center, complete the listing (screenshots, description, privacy policy URL)
   and submit for certification.

The `runFullTrust` restricted capability is standard for WinUI 3 desktop apps and does not need extra justification.

## Architecture

```
  +---------------------------- MarkdownViewer.App (WinUI 3) ----------------------------+
  |                                                                                      |
  |  MainWindow / SettingsPage / ShortcutsDialog          DocumentView                   |
  |   x:Bind (OneWay / TwoWay)  KeyboardAccelerators       WebView2 <-> viewer.js        |
  |            |                          |                  |   ^                       |
  |            v                          v                  |   | postMessage           |
  |  +----------------------------------------------+       |   | {ready, heading,       |
  |  |  ViewModels (Core)                           |<------+   |  find, link, scroll}  |
  |  |  MainViewModel, DocumentTabViewModel, ...    |  calls     |                        |
  |  +----------------------------------------------+  OnXxx()   |                        |
  |            |  WeakReferenceMessenger messages ------------> ExecuteScriptAsync        |
  |            |  (ScrollToAnchor, Find, Print, AppearanceChanged ...)  mdv.find(...) ... |
  |            v                                                                         |
  |  Contracts (interfaces) <---- App/Services: settings, theme, pickers, dialogs,       |
  |            ^                  launcher, clipboard, default app, dispatcher           |
  +------------|-------------------------------------------------------------------------+
               |
  +------------+------------- MarkdownViewer.Core (net8.0) ------------------------------+
  |  Services: MarkdownRenderService (Markdig + HtmlSanitizer) -> HtmlDocumentBuilder     |
  |            AccentService, DensityService, ShortcutCatalog, FileWatcher, TextFileReader|
  +--------------------------------------------------------------------------------------+
```

The document is rendered to sanitized HTML in Core, wrapped in a page that links `https://appassets.mdv/viewer.css`
and friends, and shown in WebView2. `https://appassets.mdv/` maps to `Assets/Web` and `https://doc.mdv/` maps to the
open document's folder, so relative images and links work without giving the page file-system access.

## MVVM rules

- ViewModels live only in Core and depend only on Contracts and CommunityToolkit.Mvvm. No `Microsoft.UI.*` or `Windows.*`.
- ViewModels are `partial` classes using `[ObservableProperty]` and `[RelayCommand]`.
- Views bind with `x:Bind` and an explicit `Mode`. Code-behind contains only `InitializeComponent`, the `ViewModel`
  property, pure static view-conversion helpers (for example hex string to brush) and the WebView2 bridge wiring.
- ViewModel to WebView actions travel as `WeakReferenceMessenger` messages tagged with the tab id; WebView events call
  public ViewModel methods (`OnViewerReady`, `OnHeadingChanged`, `OnFindResult`, `OnLinkActivated`).
- Keyboard shortcuts are `KeyboardAccelerator`s bound to ViewModel commands. The list shown in the shortcuts dialog
  comes from `ShortcutCatalog`; keep both in sync.
- Services are registered with `Microsoft.Extensions.DependencyInjection` in `App.xaml.cs`.

## Keyboard shortcuts

| Action | Shortcut |
| --- | --- |
| Open file | Ctrl+O |
| Open folder | Ctrl+Shift+O |
| Close tab | Ctrl+W |
| Reopen closed tab | Ctrl+Shift+T |
| Next / previous tab | Ctrl+Tab / Ctrl+Shift+Tab |
| Print | Ctrl+P |
| Export to PDF | Ctrl+Shift+E |
| Edit in external editor | Ctrl+E |
| Copy file path | Ctrl+Shift+C |
| Comfortable / Compact / Ultra density | Ctrl+1 / Ctrl+2 / Ctrl+3 |
| Zoom in / out / reset | Ctrl+= / Ctrl+- / Ctrl+0 |
| Show or hide outline | Ctrl+B |
| Focus mode | F11 |
| View source | Ctrl+U |
| Switch light or dark | Ctrl+Shift+D |
| Collapse / expand all sections | Ctrl+Shift+[ / Ctrl+Shift+] |
| Scroll down / up | J or Down / K or Up |
| Page down / up | Space or PgDn / Shift+Space or PgUp |
| Go to top / bottom | Home / End |
| Go to heading | Ctrl+G |
| Back / forward | Alt+Left / Alt+Right |
| Find in document | Ctrl+F |
| Next / previous match | F3 or Enter / Shift+F3 |
| Close find, Settings or focus mode | Esc |
| Settings | Ctrl+, |
| Keyboard shortcuts | Ctrl+/ or F1 |

In multi-column layouts the document moves sideways: J, K and the arrow keys move one column, Space and PgDn/PgUp
move a full page, and the mouse wheel moves column by column.

## Replacing the bundled highlighter with highlight.js

`Assets/Web/highlight.min.js` is a small built-in tokenizer that covers JavaScript/TypeScript, JSON, C# and other
C-like languages, Python, Bash/PowerShell, HTML/XML, CSS, YAML/TOML, SQL, Markdown and diff. It uses the same entry
point (`window.hljs.highlightAll()`) and CSS class names as highlight.js, so it can be swapped for the official build:

1. Download the highlight.js 11.x browser build (BSD-3-Clause) from <https://highlightjs.org/download>,
   choosing the languages you need.
2. Replace `src/MarkdownViewer.App/Assets/Web/highlight.min.js` with the downloaded `highlight.min.js`.
3. Keep `highlight.css` (it already styles the highlight.js classes for light and dark themes), or replace it with a
   highlight.js theme scoped under `html[data-theme="light"]` and `html[data-theme="dark"]`.
4. Add the highlight.js license to the app's third-party notices.

No other change is needed: `viewer.js` calls `hljs.highlightAll()` when the page loads.

## Privacy

- Everything runs locally. The app has no telemetry and makes no network requests of its own.
- The reading surface uses a strict Content-Security-Policy: scripts and styles load only from the app package,
  and images only from the document's folder.
- Remote (https) images are **off by default** so that opening a document cannot reveal your IP address or that you
  read it. Turn on **Settings > Reading > Load remote images** to allow them.
- Links to websites open in your default browser; links to other Markdown files open in the app (Alt+Left goes back).
- Raw HTML in documents is sanitized and scripts in documents never run.

## Known limitations

- Math (KaTeX) and diagrams (Mermaid) are not rendered yet; they show as code blocks. Both are planned for v1.x.
- The bundled syntax highlighter is intentionally small; see the section above to switch to highlight.js.
- The app is a viewer. Use **Edit in external editor** (Ctrl+E) to change a document; the view reloads when you save.
- Very large documents (tens of megabytes) render more slowly because the whole file is rendered at once.
