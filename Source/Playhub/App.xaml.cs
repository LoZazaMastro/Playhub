using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Playhub.Services;
using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Playhub;

public partial class App : Application
{
    private Window? _window;
    private readonly SingleInstanceService _singleInstance = new();
    private bool _launchStarted;

    public App()
    {
        // Playhub is a dark application. Set the application theme before
        // loading XAML resources or constructing any page; otherwise Windows
        // light mode can materialize light card brushes that remain in place
        // even after the window itself is switched to dark mode.
        Diag.Step("App ctor begin");
        // Handler globali: catturano le eccezioni gestite da QUALUNQUE thread (non
        // solo il thread UI) e i Task non osservati. I crash NATIVI non sono
        // intercettabili da .NET: per quelli servono i breadcrumb di Diag.Step.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Diag.Crash("AppDomain.UnhandledException (terminating=" + e.IsTerminating + ")", e.ExceptionObject);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diag.Crash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        RequestedTheme = ApplicationTheme.Dark;
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            Diag.Crash("Application.UnhandledException", args.Exception);
            // Intenzionale: l'app non deve crashare per un'eccezione UI non gestita.
            args.Handled = true;
        };
        Diag.Step("App ctor end");
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_launchStarted)
        {
            ActivateMainWindow();
            return;
        }
        _launchStarted = true;
        if (await TryHandleCommandLineLaunch(args.Arguments))
        {
            Environment.Exit(0);
            return;
        }

        var dispatcher = DispatcherQueue.GetForCurrentThread();
#if PLAYHUB_UI_REVIEW
        const string instanceKey = "Playhub.UiReview";
#else
        const string instanceKey = "Playhub.MainWindow";
#endif
        try
        {
            if (!await _singleInstance.RegisterAsync(instanceKey, (_, _) =>
                dispatcher.TryEnqueue(ActivateMainWindow)))
            {
                Exit();
                return;
            }
        }
        catch (Exception ex)
        {
            Diag.Crash("Single-instance activation", ex);
            Exit();
            return;
        }

        _window = new MainWindow();
        _window.Closed += (_, _) => _singleInstance.Dispose();
        _window.Activate();
#if PLAYHUB_UI_REVIEW
        await ((MainWindow)_window).RunUiReviewAsync();
#endif
    }

    private void ActivateMainWindow()
    {
        if (_window == null) return;
        if (_window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        _window.Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_window));
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    private static async Task<bool> TryHandleCommandLineLaunch(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        var parsed = CommandLine.Parse(arguments);
        if (parsed.Count >= 3 && string.Equals(parsed[0], "uwp-launch", StringComparison.OrdinalIgnoreCase))
        {
            var extraArgs = parsed.Count > 3 ? string.Join(' ', parsed.GetRange(3, parsed.Count - 3)) : string.Empty;
            await UwpLauncher.LaunchAsync(parsed[1], extraArgs);
            return true;
        }

        if (parsed.Count < 2 || !parsed[0].Contains('!', StringComparison.Ordinal))
        {
            return false;
        }

        var uwpHookCompatibleExtraArgs = parsed.Count > 2 ? string.Join(' ', parsed.GetRange(2, parsed.Count - 2)) : string.Empty;
        await UwpLauncher.LaunchAsync(parsed[0], uwpHookCompatibleExtraArgs);
        return true;
    }
}
