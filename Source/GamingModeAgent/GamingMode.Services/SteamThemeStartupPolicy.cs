using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

internal enum SteamThemeStartupResult { Waiting, Ready, UserLeft, TimedOut, SteamExited, ContextReplaced, Cancelled }

// A readiness receipt describes the current document's resolved styles, not a
// screenshot or a frame that Windows has already presented behind the curtain.
internal sealed record SteamThemeReceipt(int BrowserId, string DocumentId, string Generation, long Serial, string State, string Proof,
    int PhysicalWidth = 0, int PhysicalHeight = 0)
{
    internal static SteamThemeReceipt? Parse(JsonElement result)
    {
        try
        {
            if (!result.GetProperty("main").GetBoolean()) return null;
            var receipt = result.GetProperty("receipt");
            if (receipt.GetProperty("protocol").GetInt32() != 1) return null;
            int browser = result.GetProperty("browserId").GetInt32();
            string document = result.GetProperty("documentId").GetString() ?? "";
            if (browser <= 0 || receipt.GetProperty("browserId").GetInt32() != browser
                || receipt.GetProperty("documentId").GetString() != document
                || !Regex.IsMatch(document, "^[a-f0-9]{32}$")) return null;
            string generation = receipt.GetProperty("generation").GetString() ?? "";
            long serial = receipt.GetProperty("serial").GetInt64();
            if (serial < 1 || !Regex.IsMatch(generation, "^g-[a-f0-9]{32}$")) return null;
            int width = result.GetProperty("physicalWidth").GetInt32(), height = result.GetProperty("physicalHeight").GetInt32();
            if (width is < 100 or > 32768 || height is < 100 or > 32768) return null;
            return new(browser, document, generation, serial, receipt.GetProperty("state").GetString() ?? "",
                receipt.GetProperty("proof").GetString() ?? "", width, height);
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException or FormatException or OverflowException) { return null; }
    }
    internal bool Applied => State == "applied" && Proof is "style-resolved" or "frame-confirmed";
}

internal sealed class SteamThemeStartupPolicy
{
    private readonly long _started;
    private readonly int _minimumMs;
    private readonly int _timeoutMs;
    private bool _seenMain;
    internal long DeadlineTick => _started + _timeoutMs;
    internal SteamThemeStartupResult Result { get; private set; } = SteamThemeStartupResult.Waiting;
    internal SteamThemeStartupPolicy(long started, int minimumMs, int timeoutMs)
    { _started = started; _minimumMs = Math.Clamp(minimumMs, 0, 120000); _timeoutMs = Math.Clamp(timeoutMs, 1000, 120000); }

    internal SteamThemeStartupResult Observe(long now, bool originalForeground, bool verifiedSteamForeground,
        bool exactMainForeground, bool steamAlive, bool contextCurrent, bool fullscreen, SteamThemeReceipt? receipt)
    {
        if (Result != SteamThemeStartupResult.Waiting) return Result;
        if (!steamAlive) return Result = SteamThemeStartupResult.SteamExited;
        if (!contextCurrent) return Result = SteamThemeStartupResult.ContextReplaced;
        if (exactMainForeground) _seenMain = true;
        else if ((_seenMain && !exactMainForeground) || (!originalForeground && !verifiedSteamForeground))
            return Result = SteamThemeStartupResult.UserLeft;
        long elapsed = now - _started;
        if (elapsed >= _timeoutMs) return Result = SteamThemeStartupResult.TimedOut;
        if (_seenMain && exactMainForeground && fullscreen && receipt?.Applied == true && elapsed >= _minimumMs)
            return Result = SteamThemeStartupResult.Ready;
        return Result;
    }
}
