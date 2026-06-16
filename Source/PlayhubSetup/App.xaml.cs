using System;
using System.Linq;
using System.Windows;

namespace PlayhubSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var uninstall = e.Args.Any(a =>
            a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        var silent = e.Args.Any(a =>
            a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/silent", StringComparison.OrdinalIgnoreCase));

        // Disinstallazione silenziosa (QuietUninstallString da "App installate").
        if (uninstall && silent)
        {
            RunSilentUninstall();
            return;
        }

        var window = new MainWindow(uninstall ? SetupMode.Uninstall : SetupMode.Install);
        MainWindow = window;
        window.Show();
    }

    private async void RunSilentUninstall()
    {
        try
        {
            await Installer.UninstallAsync(new Progress<(double, string)>());
        }
        catch
        {
        }
        Shutdown();
    }
}
