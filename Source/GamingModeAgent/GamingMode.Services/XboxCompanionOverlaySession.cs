using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Playhub.DeckyHost;
using Playhub.Shared;

namespace GamingMode.Services;

internal static partial class XboxCompanionOverlaySession
{
    private sealed record State(ulong GameId, uint GamePid, long GameBirth, int Scope, bool OverlayOpen, long KeyboardAt);
    internal sealed record Decision(bool Ok, string? GameId = null, uint CompanionPid = 0, long KeyboardAt = 0,
        uint OverlayPid = 0, int ForegroundScope = 0, bool OverlayOpen = false, string? SessionId = null, SteamMirrorDto? Mirror = null);

    internal static Task<Decision> CheckAsync(uint appId, uint overlayPid, bool keyboard, bool menu, CancellationToken cancellation,
        Action<string> diagnostic) => CheckAsync(appId, overlayPid, keyboard, menu, false, false, cancellation, diagnostic);

    internal static async Task<Decision> RequestOpenAsync(uint appId, CancellationToken cancellation, Action<string> diagnostic)
    {
        var mirror = await TryRequestMirrorPreparationAsync(appId, cancellation, diagnostic);
        return mirror.Handled ? mirror.Result : await CheckAsync(appId, 0, false, false, true, false, cancellation, diagnostic);
    }

    internal sealed record SessionSnapshot(bool Ok, string? GameId = null, uint CompanionPid = 0,
        uint OverlayPid = 0, int ForegroundScope = 0, bool OverlayOpen = false, string? SessionId = null)
    {
        public string Kind => "owned-session";
    }

    internal static async Task<SessionSnapshot> ReadSnapshotAsync(uint appId, uint expectedOverlayPid,
        CancellationToken cancellation, Action<string> diagnostic)
    {
        if (expectedOverlayPid == 0) return new(false);
        var proof = await CheckAsync(appId, expectedOverlayPid, false, false, false, true, cancellation, diagnostic);
        return proof.Ok ? new(true, proof.GameId, proof.CompanionPid, proof.OverlayPid, proof.ForegroundScope, proof.OverlayOpen, proof.SessionId)
            : new(false);
    }

    private static async Task<Decision> CheckAsync(uint appId, uint overlayPid, bool keyboard, bool menu,
        bool requestOpen, bool ownershipOnly, CancellationToken cancellation, Action<string> diagnostic)
    {
        try
        {
            if (ownershipOnly && (requestOpen || keyboard || menu || overlayPid == 0)) return new(false);
            if (UwpSteamSessionAssociation.ReadInstalledImages() is not { } images) return new(false);
            var sessions = UwpSteamSessionAssociation.FindForApp(appId, images);
            if (sessions is not [var game] || !UwpSteamSessionAssociation.IsOnlyRunningInstance(game))
                return await CheckWin32Async(appId, overlayPid, keyboard, menu, requestOpen, ownershipOnly, cancellation, diagnostic);
            if (SteamInstallationPath.FindRoot() is not { } steamRoot) return new(false);
            using var wrapper = Process.GetProcessById(checked((int)game.WrapperPid));
            int sessionId = wrapper.SessionId;
            DateTime wrapperStarted = wrapper.StartTime;
            if (wrapper.HasExited || wrapperStarted.ToFileTimeUtc() != game.WrapperBirth) return new(false);

            string name = FormattableString.Invariant($"Playhub.XboxCompanion.{game.WrapperPid}.{game.WrapperBirth}");
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            bounded.CancelAfter(200);
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.In,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(bounded.Token);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint serverPid)) return new(false);
            using var companion = Process.GetProcessById(checked((int)serverPid));
            if (!IsProcess(companion, images.Companion, sessionId) || companion.StartTime < wrapperStarted
                || DeckyProcessTree.Read(companion.Id)?.ParentPid != wrapper.Id) return new(false);
            byte[] bytes = new byte[512];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = await pipe.ReadAsync(bytes.AsMemory(length), bounded.Token);
                if (read == 0) break;
                length += read;
            }
            if (length == 0 || length == bytes.Length) return new(false);
            var status = JsonSerializer.Deserialize<State>(bytes.AsSpan(0, length));
            if (status is null || !MatchesUwpStatus(status.GameId, status.GamePid, status.GameBirth, appId, game.GamePid, game.GameBirth)
                || companion.HasExited
                || UwpSteamSessionAssociation.Find(game.GamePid, appId) != game) return new(false);
            var host = NativeSteamOverlayHost.Find(steamRoot, serverPid, companion.StartTime.ToFileTimeUtc(), status.GameId, sessionId);
            if (host is null || (overlayPid != 0 && overlayPid != host.Pid)) return new(false);
            using var overlay = Process.GetProcessById(checked((int)host.Pid));
            if (!IsProcess(overlay, Path.Combine(steamRoot, "gameoverlayui64.exe"), sessionId)
                || overlay.StartTime.ToFileTimeUtc() != host.Birth || overlay.StartTime < companion.StartTime) return new(false);

            bool verificationOnly = ownershipOnly || (overlayPid == 0 && !requestOpen);
            int currentScope = ReadForegroundScope(game, serverPid);
            if (!MatchesForegroundScope(status.Scope, currentScope, verificationOnly))
            {
                diagnostic($"Xbox Steam companion scope refused: appId={appId}, reported={status.Scope}, foreground={currentScope}, verifyOnly={verificationOnly}, openRequest={requestOpen}.");
                return new(false);
            }
            if (verificationOnly && status.Scope != currentScope)
                diagnostic($"Xbox Steam companion foreground handoff confirmed: appId={appId}, reported={status.Scope}, foreground={currentScope}.");
            if (!verificationOnly && !AllowsRequest(status.Scope, status.OverlayOpen, status.KeyboardAt, Environment.TickCount64, keyboard, menu))
            {
                if (requestOpen)
                    diagnostic($"Xbox Steam companion open request no longer applies: appId={appId}, scope={status.Scope}, alreadyOpen={status.OverlayOpen}.");
                return new(false);
            }

            if (requestOpen)
            {
                using var open = new NamedPipeClientStream(".", name + ".Open", PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await open.ConnectAsync(bounded.Token);
                if (!GetNamedPipeServerProcessId(open.SafePipeHandle.DangerousGetHandle(), out uint openPid)
                    || openPid != serverPid || companion.HasExited || overlay.HasExited
                    || UwpSteamSessionAssociation.Find(game.GamePid, appId) != game
                    || ReadForegroundScope(game, serverPid) != 1)
                {
                    diagnostic($"Xbox Steam companion open request target changed before delivery: appId={appId}.");
                    return new(false);
                }
                await open.WriteAsync(new byte[] { 1 }, bounded.Token);
                diagnostic($"Xbox Steam companion open request sent: appId={appId}.");
            }

            if (wrapper.HasExited || companion.HasExited || overlay.HasExited || wrapper.StartTime.ToFileTimeUtc() != game.WrapperBirth
                || overlay.StartTime.ToFileTimeUtc() != host.Birth || UwpSteamSessionAssociation.Find(game.GamePid, appId) != game) return new(false);
            string sessionIdentity = SessionIdentity(status.GameId, game.WrapperPid, game.WrapperBirth, game.GamePid, game.GameBirth,
                serverPid, companion.StartTime.ToFileTimeUtc(), host.Pid, host.Birth);
            if (ownershipOnly)
            {
                // Lifetime/tracking reads above can outlive a foreground edge.
                // The observer gets a fresh scope after those slower checks.
                currentScope = ReadForegroundScope(game, serverPid);
                if (!MatchesForegroundScope(status.Scope, currentScope, true)
                    || wrapper.HasExited || companion.HasExited || overlay.HasExited) return new(false);
            }
            return new(true, status.GameId.ToString(CultureInfo.InvariantCulture), serverPid,
                keyboard || requestOpen ? status.KeyboardAt : 0, host.Pid, currentScope, status.OverlayOpen,
                sessionIdentity);
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or
            OverflowException or UnauthorizedAccessException or OperationCanceledException or JsonException or System.ComponentModel.Win32Exception)
        {
            return new(false);
        }
    }

    // An identity label for observers, never an authorization token. Every
    // snapshot still authenticates the live handles, images and pipe owner.
    internal static string SessionIdentity(ulong gameId, uint wrapperPid, long wrapperBirth, uint gamePid, long gameBirth,
        uint companionPid, long companionBirth, uint overlayPid, long overlayBirth)
    {
        string tuple = FormattableString.Invariant($"v1:{gameId}:{wrapperPid}:{wrapperBirth}:{gamePid}:{gameBirth}:{companionPid}:{companionBirth}:{overlayPid}:{overlayBirth}");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(tuple)));
    }

    internal static ValveSession? ParseValveSession(string text)
    {
        uint overlay = 0, companion = 0;
        ulong gameId = 0;
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
                uint.TryParse(message[started.Length..], out overlay);
                companion = 0;
                gameId = 0;
            }
            else if (message.StartsWith(connected, StringComparison.Ordinal))
                uint.TryParse(message[connected.Length..], out companion);
            else if (message.StartsWith(identified, StringComparison.Ordinal))
                ulong.TryParse(message[identified.Length..], out gameId);
            else if (message is "Game Exit Event signaled." or "Shutting down overlay")
                overlay = 0;
        }
        return overlay > 0 && companion > 0 && gameId > 0 ? new(overlay, companion, gameId) : null;
    }

    private static bool IsProcess(Process process, string image, int sessionId) =>
        !process.HasExited && process.SessionId == sessionId
        && string.Equals(process.MainModule?.FileName, image, StringComparison.OrdinalIgnoreCase);

    private static int ReadForegroundScope(UwpSessionIdentity game, uint companionPid)
    {
        nint foreground = OverlayWindowTools.GetForegroundWindowHandle();
        if (!OverlayWindowTools.IsVisibleGameSurface(foreground) || GetWindowThreadProcessId(foreground, out uint owner) == 0)
            return 0;
        int scope = 0;
        if (owner == companionPid)
        {
            var className = new StringBuilder(64);
            if (GetClassName(foreground, className, className.Capacity) > 0 && className.ToString() == "PlayhubSteamCompanion")
                scope = 2;
        }
        else if (owner == game.GamePid || UwpFrameWindow.Matches(foreground, game.Aumid)) scope = 1;
        return OverlayWindowTools.GetForegroundWindowHandle() == foreground ? scope : 0;
    }

    [DllImport("kernel32.dll")] private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint pid);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int count);
}
