using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Playhub.Services;

/// <summary>Headless entry point for the Decky button; uses the app's exact collector.</summary>
public static class SupportReportCommand
{
    public static async Task RunAsync(Guid requestId)
    {
        var folder = Path.Combine(AppPaths.LocalDataRoot, "DiagnosticsRequests");
        Directory.CreateDirectory(folder);
        var receipt = Path.Combine(folder, requestId.ToString("N") + ".json");
        object result;
        try
        {
            var path = await new DiagnosticsService(new GamingModeService()).CreateReportAsync(AppPaths.SettingsFile);
            result = new { ok = true, path };
        }
        catch (Exception error)
        {
            Diag.Crash("Diagnostic report command", error);
            result = new { ok = false, message = error.Message };
        }
        var temporary = receipt + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(result));
        File.Move(temporary, receipt, true);
    }
}
