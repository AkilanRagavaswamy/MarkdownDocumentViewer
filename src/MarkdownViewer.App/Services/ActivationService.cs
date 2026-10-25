using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MarkdownViewer.Core.Contracts;
using MarkdownViewer.Core.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace MarkdownViewer.App.Services;

/// <summary>
/// Single-instance activation. <see cref="TryRedirectToMainInstance"/> runs in Program.Main before XAML starts;
/// redirected activations (file activation, `mdviewer.exe file.md`) are buffered until <see cref="StartAsync"/>
/// attaches, then forwarded on the UI thread to <see cref="MainViewModel.OpenPathsAsync"/>.
/// </summary>
public sealed class ActivationService
{
    private const string MainInstanceKey = "main";

    private static readonly object s_gate = new();
    private static readonly Queue<AppActivationArguments> s_pending = new();
    private static Action<AppActivationArguments>? s_sink;

    private readonly MainViewModel _main;
    private readonly IDispatcherService _dispatcher;
    private readonly IWindowProvider _windows;

    public ActivationService(MainViewModel main, IDispatcherService dispatcher, IWindowProvider windows)
    {
        _main = main;
        _dispatcher = dispatcher;
        _windows = windows;
    }

    // ---------------------------------------------------------------- Program.Main (before XAML)

    /// <summary>Returns true when this process redirected its activation to the main instance and must exit.</summary>
    public static bool TryRedirectToMainInstance()
    {
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);

        if (mainInstance.IsCurrent)
        {
            mainInstance.Activated += OnRedirectedActivation;
            return false;
        }

        RedirectAndWait(args, mainInstance);
        return true;
    }

    private static void OnRedirectedActivation(object? sender, AppActivationArguments args)
    {
        Action<AppActivationArguments>? sink;
        lock (s_gate)
        {
            sink = s_sink;
            if (sink is null)
            {
                s_pending.Enqueue(args);
                return;
            }
        }

        sink(args);
    }

    /// <summary>
    /// RedirectActivationToAsync must not block the STA, so it runs on a worker while this thread pumps COM
    /// in CoWaitForMultipleObjects (documented pattern for WinUI 3 single-instancing).
    /// </summary>
    private static void RedirectAndWait(AppActivationArguments args, AppInstance target)
    {
        var redirected = CreateEvent(IntPtr.Zero, bManualReset: true, bInitialState: false, lpName: null);
        try
        {
            Task.Run(() =>
            {
                try
                {
                    target.RedirectActivationToAsync(args).AsTask().Wait();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Activation redirect failed: {ex.Message}");
                }
                finally
                {
                    SetEvent(redirected);
                }
            });

            const uint CWMO_DEFAULT = 0;
            const uint INFINITE = 0xFFFFFFFF;
            _ = CoWaitForMultipleObjects(CWMO_DEFAULT, INFINITE, 1, new[] { redirected }, out _);
        }
        finally
        {
            CloseHandle(redirected);
        }

        // This process still holds foreground rights, so it can hand them to the main window.
        try
        {
            var process = Process.GetProcessById((int)target.ProcessId);
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                SetForegroundWindow(process.MainWindowHandle);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not foreground main instance: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- Main instance (after XAML)

    /// <summary>Initializes the shell, opens the paths this process was launched with, then accepts redirects.</summary>
    public async Task StartAsync()
    {
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();

        await _main.InitializeAsync();

        var paths = ExtractPaths(args, includeProcessCommandLine: true);
        if (paths.Count > 0)
        {
            await _main.OpenPathsAsync(paths);
        }

        List<AppActivationArguments> backlog;
        lock (s_gate)
        {
            s_sink = OnActivatedFromOtherInstance;
            backlog = s_pending.ToList();
            s_pending.Clear();
        }

        foreach (var pending in backlog)
        {
            OnActivatedFromOtherInstance(pending);
        }
    }

    public void Stop()
    {
        lock (s_gate)
        {
            s_sink = null;
        }
    }

    private void OnActivatedFromOtherInstance(AppActivationArguments args)
    {
        // AppInstance.Activated fires on a background thread.
        var paths = ExtractPaths(args, includeProcessCommandLine: false);
        _dispatcher.Enqueue(async () =>
        {
            BringToFront();
            if (paths.Count == 0)
            {
                return;
            }

            try
            {
                await _main.OpenPathsAsync(paths);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Opening redirected paths failed: {ex}");
            }
        });
    }

    private void BringToFront()
    {
        if (_windows.AppWindow?.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        _windows.Window?.Activate();
    }

    // ---------------------------------------------------------------- Path extraction

    public static IReadOnlyList<string> ExtractPaths(AppActivationArguments args, bool includeProcessCommandLine)
    {
        var result = new List<string>();

        switch (args.Kind)
        {
            case ExtendedActivationKind.File when args.Data is IFileActivatedEventArgs fileArgs:
                foreach (var item in fileArgs.Files)
                {
                    if (!string.IsNullOrEmpty(item.Path))
                    {
                        result.Add(item.Path);
                    }
                }

                break;

            case ExtendedActivationKind.Launch when args.Data is ILaunchActivatedEventArgs launchArgs:
                result.AddRange(PathsFromArguments(SplitCommandLine(launchArgs.Arguments)));
                break;
        }

        if (result.Count == 0 && includeProcessCommandLine && args.Kind == ExtendedActivationKind.Launch)
        {
            // App execution alias (mdviewer.exe <path>) on the first instance: the process command line is authoritative.
            result.AddRange(PathsFromArguments(Environment.GetCommandLineArgs().Skip(1)));
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> PathsFromArguments(IEnumerable<string> tokens)
    {
        var first = true;
        foreach (var raw in tokens)
        {
            var token = raw.Trim().Trim('"');
            var isFirst = first;
            first = false;

            if (token.Length == 0 || token.StartsWith('-') || token.StartsWith('/'))
            {
                continue;
            }

            // Some launch paths include the executable itself as the first token.
            if (isFirst && token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string candidate;
            if (Uri.TryCreate(token, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                candidate = uri.LocalPath;
            }
            else
            {
                try
                {
                    candidate = Path.GetFullPath(token);
                }
                catch (Exception)
                {
                    continue;
                }
            }

            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>Splits a Windows command line using the CommandLineToArgvW quoting rules.</summary>
    internal static IReadOnlyList<string> SplitCommandLine(string? commandLine)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return args;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        var i = 0;
        while (i < commandLine.Length)
        {
            var c = commandLine[i];
            if (c == '\\')
            {
                var slashes = 0;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    slashes++;
                    i++;
                }

                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', slashes / 2);
                    if (slashes % 2 == 1)
                    {
                        current.Append('"');
                        i++;
                    }
                }
                else
                {
                    current.Append('\\', slashes);
                }

                hasToken = true;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                i++;
                continue;
            }

            current.Append(c);
            hasToken = true;
            i++;
        }

        if (hasToken)
        {
            args.Add(current.ToString());
        }

        return args;
    }

    // ---------------------------------------------------------------- Interop

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(uint dwFlags, uint dwMilliseconds, uint nHandles, IntPtr[] pHandles, out uint dwIndex);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
