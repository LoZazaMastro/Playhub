using System;
using System.IO;
using System.Threading.Tasks;

namespace Playhub.Services;

internal static class RestartSequence
{
    internal static async Task SteamAsync(string? executable, Func<string, bool> exists, Func<bool> running,
        Action<string> requestExit, Action<string> start, Func<int, Task> delay)
    {
        if (string.IsNullOrWhiteSpace(executable) || !exists(executable))
            throw new FileNotFoundException("Steam executable was not found.");
        if (running())
        {
            requestExit(executable);
            for (var attempt = 0; attempt < 60 && running(); attempt++) await delay(500);
            if (running()) throw new TimeoutException("Steam did not close. Finish any active game or Steam dialog and try again.");
        }
        start(executable);
        for (var attempt = 0; attempt < 30 && !running(); attempt++) await delay(500);
        if (!running()) throw new TimeoutException("Steam did not start.");
    }

    internal static async Task<bool> SteamAndDeckyAsync(bool installed, Action stopDecky, Func<Task> restartSteam,
        Func<bool> startDecky, Func<int, Task> delay, Action<Exception> failed)
    {
        if (!installed) return false;
        var restartDeckyNeeded = false;
        try
        {
            restartDeckyNeeded = true;
            stopDecky();
            await restartSteam();
            await delay(1200);
            return startDecky();
        }
        catch (Exception error)
        {
            failed(error);
            if (restartDeckyNeeded)
            {
                try { startDecky(); } catch (Exception restoreError) { failed(restoreError); }
            }
            return false;
        }
    }
}
