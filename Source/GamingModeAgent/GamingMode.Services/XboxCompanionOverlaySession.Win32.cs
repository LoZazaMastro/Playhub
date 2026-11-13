using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace GamingMode.Services;

internal static partial class XboxCompanionOverlaySession
{
    private static async Task<Decision> CheckWin32Async(uint appId, uint overlayPid, bool keyboard, bool menu,
        bool requestOpen, bool ownershipOnly, CancellationToken cancellation, Action<string> diagnostic)
    {
        Decision Refuse(string reason) { diagnostic($"Win32 Steam companion refused: appId={appId}, check={reason}."); return new(false); }
        using var association = await Win32SteamSessionAssociation.FindLeaseAsync(appId, cancellation);
        if (association is null
            || UwpSteamSessionAssociation.ReadInstalledImages() is not { } images
            || SteamInstallationPath.FindRoot() is not { } root) return Refuse("session");
        var identity = association.Identity;
        using var wrapper = Process.GetProcessById(checked((int)identity.WrapperPid));
        using var game = Process.GetProcessById(checked((int)identity.GamePid));
        bool Current() => !wrapper.HasExited && !game.HasExited
            && wrapper.StartTime.ToFileTimeUtc() == identity.WrapperBirth && game.StartTime.ToFileTimeUtc() == identity.GameBirth
            && association.Current;
        if (!Current()) return Refuse("source-lifetime");
        string name = FormattableString.Invariant($"Playhub.XboxCompanion.{identity.WrapperPid}.{identity.WrapperBirth}");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(250);
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(bounded.Token);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint companionPid)) return Refuse("status-server");
        using var companion = Process.GetProcessById(checked((int)companionPid));
        if (!IsProcess(companion, images.Companion, wrapper.SessionId) || companion.StartTime < wrapper.StartTime) return Refuse("companion-image");
        byte[] bytes = new byte[512];
        int length = 0;
        while (length < bytes.Length)
        {
            int read = await pipe.ReadAsync(bytes.AsMemory(length), bounded.Token);
            if (read == 0) break;
            length += read;
        }
        if (length == 0 || length == bytes.Length) return Refuse("status-size");
        var status = JsonSerializer.Deserialize<State>(bytes.AsSpan(0, length));
        if (status is null || status.GameId != identity.GameId || status.GamePid != identity.GamePid
            || status.GameBirth != identity.GameBirth || !Current()) return Refuse("status-identity");
        var host = NativeSteamOverlayHost.Find(root, companionPid, companion.StartTime.ToFileTimeUtc(), identity.GameId, wrapper.SessionId);
        if (host is null) return Refuse("renderer-record");
        var valve = new ValveSession(host.Pid, companionPid, identity.GameId);
        if (companion.HasExited
            || (overlayPid != 0 && overlayPid != valve.OverlayPid)) return Refuse("renderer-record");
        using var overlay = Process.GetProcessById(checked((int)valve.OverlayPid));
        if (!IsProcess(overlay, Path.Combine(root, "gameoverlayui64.exe"), wrapper.SessionId)
            || overlay.StartTime.ToFileTimeUtc() != host.Birth || overlay.StartTime < companion.StartTime) return Refuse("renderer-image");
        int Scope()
        {
            nint foreground = OverlayWindowTools.GetForegroundWindowHandle();
            if (!OverlayWindowTools.IsPresentGameSurface(foreground) || GetWindowThreadProcessId(foreground, out uint owner) == 0) return 0;
            int scope = 0;
            if (owner == identity.GamePid) scope = 1;
            else if (owner == companionPid)
            {
                var type = new StringBuilder(64);
                if (GetClassName(foreground, type, type.Capacity) > 0 && type.ToString() == "PlayhubSteamCompanion") scope = 2;
            }
            return OverlayWindowTools.GetForegroundWindowHandle() == foreground ? scope : 0;
        }
        bool verificationOnly = ownershipOnly || (overlayPid == 0 && !requestOpen);
        int scope = Scope();
        if (!MatchesForegroundScope(status.Scope, scope, verificationOnly)
            || (!verificationOnly && !AllowsRequest(status.Scope, status.OverlayOpen, status.KeyboardAt,
                Environment.TickCount64, keyboard, menu))) return Refuse($"foreground-scope({status.Scope}/{scope}, verify={verificationOnly})");
        if (requestOpen)
        {
            using var open = new NamedPipeClientStream(".", name + ".Open", PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await open.ConnectAsync(bounded.Token);
            if (!GetNamedPipeServerProcessId(open.SafePipeHandle.DangerousGetHandle(), out uint server)
                || server != companionPid || !Current() || companion.HasExited || overlay.HasExited || Scope() != 1) return Refuse("open-scope");
            await open.WriteAsync(new byte[] { 1 }, bounded.Token);
            diagnostic($"Win32 Steam companion open request sent: appId={appId}.");
        }
        if (!Current() || companion.HasExited || overlay.HasExited
            || overlay.StartTime.ToFileTimeUtc() != host.Birth) return Refuse("final-lifetime");
        string sessionIdentity = SessionIdentity(identity.GameId, identity.WrapperPid, identity.WrapperBirth, identity.GamePid, identity.GameBirth,
            companionPid, companion.StartTime.ToFileTimeUtc(), host.Pid, host.Birth);
        if (ownershipOnly)
        {
            scope = Scope();
            if (!MatchesForegroundScope(status.Scope, scope, true) || wrapper.HasExited || game.HasExited
                || companion.HasExited || overlay.HasExited) return Refuse("snapshot-final-scope");
        }
        return new(true, identity.GameId.ToString(CultureInfo.InvariantCulture), companionPid,
            keyboard || requestOpen ? status.KeyboardAt : 0, valve.OverlayPid, scope, status.OverlayOpen,
                sessionIdentity);
    }

}
