using System.Globalization;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

internal sealed partial class Win32OwnedAssociationCatalog
{
    // Positive ordinary receiver proof, not proof that a background game ended.
    internal Task<bool> ProveSteamUiForegroundAsync(CancellationToken cancellation)
    {
        try {
            if (disposed) return Task.FromResult(false);
            cancellation.ThrowIfCancellationRequested();
            using var source = runtime.OpenSteam();
            return Task.FromResult(!disposed && source?.Current == true && runtime.SteamUiForeground(source) && !disposed);
        } catch (Exception e) when (e is System.IO.IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException
            or OperationCanceledException or OverflowException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        { return Task.FromResult(false); }
    }
    // Negative evidence only. An authenticated retained wrapped launch always
    // vetoes this path, even while its latest private refresh is unknown.
    internal async Task<bool> ProveOrdinaryForegroundAsync(uint? observedApp, CancellationToken cancellation)
    {
        if (observedApp is >= 0x80000000 || disposed) return false;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation); bounded.CancelAfter(900);
        await gate.WaitAsync(bounded.Token).ConfigureAwait(false);
        try
        {
            if (disposed || runtime.Foreground() is not { } foreground) return false;
            using var game = runtime.Open(foreground.Pid);
            using var source = runtime.OpenSteam();
            if (game is null || source is null || !game.Current || !source.Current
                || !SameOwner(game.Identity, source.Identity)
                || entries.Any(e => e.Identity.GamePid == game.Identity.Pid && e.Identity.GameBirth == game.Identity.Birth && e.RetainedCurrent)) return false;
            var log = await runtime.ReadLogAsync(source.Root, bounded.Token).ConfigureAwait(false);
            if (log is null || log.Bytes.Length > 262144) return false;
            // A protocol-launched game may have no Steam child event. The
            // retained wrapper's private target is still a veto, never a grant.
            var wrapperApps = TrackedWrapperApps(log.Text).Distinct().Take(MaxCandidates + 1).ToArray();
            if (wrapperApps.Length > MaxCandidates) return false;
            foreach (uint app in wrapperApps)
            foreach (var tracked in Win32SteamSessionAssociation.Parse(log.Text, app))
            {
                using var wrapper = runtime.Open(tracked.Pid);
                if (wrapper?.Current != true || !SameOwner(wrapper.Identity, source.Identity)
                    || !wrapper.Identity.Image.Equals(runtime.WrapperImage, StringComparison.OrdinalIgnoreCase)) continue;
                var publication = await runtime.ReadHandoffAsync(wrapper.Identity.Pid, wrapper.Identity.Birth, bounded.Token).ConfigureAwait(false);
                if (publication is null || publication.ServerPid != wrapper.Identity.Pid) return false;
                if (publication.Publication.GamePid == game.Identity.Pid && publication.Publication.GameBirth == game.Identity.Birth) return false;
            }
            DateTime birth = DateTime.FromFileTimeUtc(game.Identity.Birth).ToLocalTime();
            // A newly rotated small file can also start at byte zero.
            // Coverage is mandatory for EVERY file before proving absence.
            {
                var first = Regex.Match(log.Text, @"\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\]", RegexOptions.CultureInvariant);
                if (!first.Success || !DateTime.TryParseExact(first.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var from) || from > birth.AddSeconds(-2)) return false;
            }
            bool nativeAtBirth = false; ulong? active = null;
            foreach (var row in Events(log.Text, foreground.Pid))
            {
                if (row.Time > runtime.LocalTime) return false;
                if (row.Time < birth.AddSeconds(-2)) continue;
                if (Original(row.Id)) return false; // direct shortcut is NOT a proven ordinary Steam launch
                if (row.Added)
                {
                    if (row.Id < 0x80000000 && (observedApp is null or 0 || row.Id == observedApp) && Math.Abs((row.Time - birth).TotalSeconds) <= 2) nativeAtBirth = true;
                    active = row.Id;
                }
                else if (row.Id == active) active = null;
            }
            return nativeAtBirth && active is > 0 and < 0x80000000 && (observedApp is null or 0 || active == observedApp) && game.Current && source.Current && !disposed
                && game.Current && source.Current && !disposed && runtime.Foreground() == foreground;
        }
        catch (Exception e) when (e is System.IO.IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException
            or OperationCanceledException or System.Text.Json.JsonException or OverflowException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        { return false; }
        finally { gate.Release(); }
    }
}
