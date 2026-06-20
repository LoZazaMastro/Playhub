using Microsoft.UI.Xaml;
using Playhub.Services;
using System;
using System.Threading.Tasks;

namespace Playhub;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        // Playhub is a dark application. Set the application theme before
        // loading XAML resources or constructing any page; otherwise Windows
        // light mode can materialize light card brushes that remain in place
        // even after the window itself is switched to dark mode.
        RequestedTheme = ApplicationTheme.Dark;
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            try
            {
                var path = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "playhub_crash.txt");
                System.IO.File.AppendAllText(path, DateTime.Now + "\n" + args.Exception + "\n\n");
            }
            catch
            {
            }

            args.Handled = true;
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (await TryHandleCommandLineLaunch(args.Arguments))
        {
            Environment.Exit(0);
            return;
        }

        _window = new MainWindow();
        _window.Activate();
    }

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
