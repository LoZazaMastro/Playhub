namespace GamingMode.Services;

internal static partial class XboxCompanionOverlaySession
{
    internal readonly record struct ValveSession(uint OverlayPid, uint CompanionPid, ulong GameId);

    internal static ValveSession? ParseCompanionValveSession(string text, uint companionPid, ulong gameId)
    {
        // Startup records have no per-row host PID. Accept only a single,
        // ordered header; overlapping starts/connections cannot identify a host.
        uint overlay = 0, target = 0;
        bool header = false, overlap = false;
        ValveSession? found = null;
        foreach (string row in text.Split('\n'))
        {
            int split = row.IndexOf(" - ", StringComparison.Ordinal);
            if (split < 0) continue;
            var message = row.AsSpan(split + 3).Trim();
            const string started = "GameOverlay process started: pid: ";
            const string connected = "GameOverlay process connecting to: pid: ";
            const string identified = "Got gameid on commandline: ";
            if (message.StartsWith(started, StringComparison.Ordinal))
            {
                overlap = header;
                header = true;
                uint.TryParse(message[started.Length..], out overlay);
                target = 0;
            }
            else if (message.StartsWith(connected, StringComparison.Ordinal))
            {
                if (target != 0) overlap = true;
                uint.TryParse(message[connected.Length..], out target);
            }
            else if (message.StartsWith(identified, StringComparison.Ordinal))
            {
                if (header && !overlap && ulong.TryParse(message[identified.Length..], out ulong id)
                    && target == companionPid && id == gameId && overlay != 0) found = new(overlay, target, id);
                header = false;
            }
            else if (message is "Game Exit Event signaled." or "Shutting down overlay")
            {
                if (target == companionPid) found = null;
                header = false;
            }
        }
        return found;
    }
}
