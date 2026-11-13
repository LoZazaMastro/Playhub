// Playhub Xbox Session. Distributed under GPL-3.0-only.
internal readonly record struct TritonSource(int Slot, ushort Product, ulong Handle)
{
    internal bool Valid => Slot is >= 0 and <= 3 && Product is 0x1302 or 0x1304 && Handle != 0;
}

internal readonly record struct XboxReport(ushort Buttons, byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY)
{
    internal XboxReport GameControlsOnly() => this with { Buttons = (ushort)(Buttons & 0xF3FF) };
    internal bool Released => Buttons == 0 && LeftTrigger < 10 && RightTrigger < 10
        && Math.Abs((int)LeftX) < 2500 && Math.Abs((int)LeftY) < 2500
        && Math.Abs((int)RightX) < 2500 && Math.Abs((int)RightY) < 2500;
}

internal readonly record struct RelayObservation(
    bool ExactSession, bool ExactGameForeground, bool OverlayOwnsInput, bool CanObserveOutput,
    bool SyntheticVisibleToSteam, TritonSource? Source, XboxReport? Report);

internal interface IXboxRelayOutput
{
    // Neutral is attempted through this exact owner even if enumeration proof
    // was lost. Connect is separate so an exception cannot lose the lease.
    bool TryConnectNeutral(Func<bool> canAttach);
    bool IsOwned { get; }
    void Submit(XboxReport report);
    bool Close();
}

internal enum RelayExit
{
    SessionEnded, FocusLost, OverlayOpened, OutputObservationUnavailable, SteamShadow,
    SourceUnavailable, SourceChanged, OutputUnavailable, Cancelled, Expired, Faulted
}

internal readonly record struct RelayResult(RelayExit Reason, bool CleanupConfirmed)
{
    internal string SubmitRejection { get; init; } = "not-recorded";
}

// These are local output-boundary results, never HTTP/caller authority.
internal sealed class ReaderForwardGuardRejected(bool paused) : InvalidOperationException
{
    internal bool PausedCurrent { get; } = paused;
}
internal sealed class NativeRelaySubmitFailed : InvalidOperationException;

internal static class SteamInputRelay
{
    private static readonly TimeSpan Cadence = TimeSpan.FromMilliseconds(15);
    private const long QuietMs = 900;

    internal static RelayResult Run(Func<RelayObservation> observe, Func<IXboxRelayOutput> createOutput,
        Func<long> monotonicMilliseconds, Action<TimeSpan> wait, CancellationToken cancellation,
        TimeSpan? maxDuration = null)
    {
        if (maxDuration is { } limit && (limit <= TimeSpan.FromMilliseconds(QuietMs) || limit > TimeSpan.FromMinutes(5)))
            throw new ArgumentOutOfRangeException(nameof(maxDuration));

        IXboxRelayOutput? output = null;
        RelayExit reason = RelayExit.Faulted;
        string submitRejection = "not-recorded";
        bool clean = true;
        try { reason = Relay(); }
        catch (ReaderForwardGuardRejected rejected)
        {
            reason = rejected.PausedCurrent ? RelayExit.OverlayOpened : RelayExit.Faulted;
            submitRejection = rejected.PausedCurrent ? "paused-current" : "no-longer-current";
        }
        catch (NativeRelaySubmitFailed) { reason = RelayExit.Faulted; submitRejection = "native-submit-failed"; }
        catch { reason = RelayExit.Faulted; }
        finally
        {
            if (output is not null)
            {
                try { clean = output.Close(); }
                catch { clean = false; }
            }
        }
        return new(reason, clean) { SubmitRejection = submitRejection };

        RelayExit Relay()
        {
            var started = monotonicMilliseconds();
            if (cancellation.IsCancellationRequested) return RelayExit.Cancelled;
            var initial = observe();
            if (Blocked(initial, null) is { } denied) return denied;
            if (initial.Report is null) return RelayExit.SourceUnavailable;
            var source = initial.Source!.Value;
            bool Expired() => maxDuration is { } duration && monotonicMilliseconds() - started >= duration.TotalMilliseconds;

            RelayExit? AttachVeto()
            {
                if (cancellation.IsCancellationRequested) return RelayExit.Cancelled;
                if (Expired()) return RelayExit.Expired;
                var current = observe();
                return Blocked(current, source) ?? (current.Report is null ? RelayExit.SourceUnavailable : null);
            }

            // The adapter contract makes construction inert.
            output = createOutput();
            RelayExit? veto = null;
            if (!output.TryConnectNeutral(() => (veto = AttachVeto()) is null))
                return veto ?? RelayExit.OutputUnavailable;

            var connected = monotonicMilliseconds();
            long quietMs = output is IExistingXboxRelayConnection { ExistingConnection: true } ? 0 : QuietMs;
            bool armed = false;
            XboxReport sent = default;
            while (true)
            {
                if (cancellation.IsCancellationRequested) return RelayExit.Cancelled;
                long now = monotonicMilliseconds();
                if (Expired()) return RelayExit.Expired;

                var observation = observe();
                if (Blocked(observation, source) is { } lost) return lost;
                if (!output.IsOwned) return RelayExit.OutputUnavailable;
                var report = observation.Report?.GameControlsOnly();

                if (report is null)
                {
                    // The exact source still exists but no fresh held input is
                    // available. Keep its output slot neutral until release.
                    armed = false;
                    if (sent != default) output.Submit(sent = default);
                }
                else if (!armed)
                {
                    // Let Steam see the neutral PDO first. Require the physical
                    // controls released after attach before accepting any gameplay.
                    armed = now - connected >= quietMs && report.Value.Released;
                }
                else if (report != sent)
                {
                    // Ownership and input focus can change during device queries.
                    var latest = observe();
                    if (Blocked(latest, source) is { } lateLoss) return lateLoss;
                    if (cancellation.IsCancellationRequested) return RelayExit.Cancelled;
                    if (Expired()) return RelayExit.Expired;
                    if (latest.Report is null) armed = false;
                    var latestReport = latest.Report?.GameControlsOnly() ?? default;
                    if (latestReport != sent)
                    {
                        output.Submit(latestReport);
                        sent = latestReport;
                    }
                }
                wait(Cadence);
            }
        }
    }

    private static RelayExit? Blocked(RelayObservation state, TritonSource? expected)
    {
        if (!state.ExactSession) return RelayExit.SessionEnded;
        if (!state.ExactGameForeground) return RelayExit.FocusLost;
        if (state.OverlayOwnsInput) return RelayExit.OverlayOpened;
        if (!state.CanObserveOutput) return RelayExit.OutputObservationUnavailable;
        if (state.SyntheticVisibleToSteam) return RelayExit.SteamShadow;
        if (state.Source is not { Valid: true } source) return RelayExit.SourceUnavailable;
        if (expected is { } prior && source != prior) return RelayExit.SourceChanged;
        return null;
    }
}
