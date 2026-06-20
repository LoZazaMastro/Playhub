using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;

namespace Playhub.Services;

public static class WindowsToastService
{
    private static bool _registered;
    private static string _lastUpdateVersion = "";

    public static void ShowPlayhubUpdate(string? version)
    {
        var cleanVersion = (version ?? "").Trim().TrimStart('v', 'V');
        if (string.IsNullOrWhiteSpace(cleanVersion) ||
            string.Equals(_lastUpdateVersion, cleanVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (!_registered)
            {
                AppNotificationManager.Default.Register();
                _registered = true;
            }

            var notification = new AppNotificationBuilder()
                .AddText($"Playhub {cleanVersion}")
                .AddText("New update available")
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
            _lastUpdateVersion = cleanVersion;
        }
        catch
        {
            // La notifica in-app resta disponibile anche sui sistemi che
            // bloccano o non supportano le toast di Windows.
        }
    }
}
