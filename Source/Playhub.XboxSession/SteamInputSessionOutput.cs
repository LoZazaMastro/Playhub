// Playhub Xbox Session. Distributed under GPL-3.0-only.
internal interface IGuardedXboxRelayOutput : IXboxRelayOutput
{
    // Slow destination proof must precede the last source/lease guard.
    bool TrySubmitCurrent(XboxReport report, Func<bool> current);
}
internal interface IExistingXboxRelayConnection { bool ExistingConnection { get; } }
// Owned by the synchronous layout worker. Packet tasks never mutate this owner.
// Provider getters may reenter, so every update still checks authority afterward.
internal sealed class SteamInputSessionOutput(Func<IGuardedXboxRelayOutput> create) : IDisposable
{
    internal readonly record struct ReaderBinding(SteamRawInputRelayTrial.SessionBinding Session, int ReaderPid, long ReaderBirth, long Epoch)
    {
        internal bool Valid => Session.Valid && ReaderPid > 0 && ReaderBirth > 0 && Epoch > 0;
    }
    private IGuardedXboxRelayOutput? output;
    private SteamRawInputRelayTrial.SessionBinding? original;
    private ReaderLease? current;
    private long lastEpoch;
    private (int Pid, long Birth)? lastReader;
    private bool disposed, neutral = true;
    internal bool CleanupConfirmed { get; private set; } = true;
    internal bool NeutralHeld
    {
        get
        {
            var held = output;
            if (held is null || !held.IsOwned) return false;
            return !disposed && ReferenceEquals(output, held) && neutral;
        }
    }
    internal ReaderLease? Borrow(ReaderBinding binding, Func<bool> canForward, Func<RelayExit?>? readSubmitPause = null)
    {
        if (disposed || !binding.Valid || current is { ReleaseConfirmed: false } || binding.Epoch <= lastEpoch
            || lastReader == (binding.ReaderPid, binding.ReaderBirth)) return null;
        if (original is { } held && (!held.SameRetainedSession(binding.Session) || held.Source != binding.Session.Source)) return null;
        var previous = current;
        long previousEpoch = lastEpoch;
        if (output is not null && !output.IsOwned) return null;
        // A provider can pump/reenter while querying its PDO. Do not overwrite
        // a newer reader, even though this worker normally runs serially.
        if (disposed || !ReferenceEquals(current, previous) || lastEpoch != previousEpoch) return null;
        original ??= binding.Session;
        lastEpoch = binding.Epoch;
        lastReader = (binding.ReaderPid, binding.ReaderBirth);
        return current = new ReaderLease(this, binding, canForward, readSubmitPause);
    }
    private bool Connect(ReaderLease lease, Func<bool> canAttach)
    {
        if (!ReferenceEquals(current, lease) || lease.Released || lease.Connected || disposed) return false;
        lease.Connected = true;
        if (output is not null)
        {
            bool owned = output.IsOwned;
            if (!owned || !Owns(lease) || !canAttach() || !Owns(lease)) return false;
            return lease.ExistingConnection = true;
        }
        CleanupConfirmed = false; output = create();
        if (!output.TryConnectNeutral(canAttach)) return false;
        neutral = true;
        bool connected = output.IsOwned;
        return connected && Owns(lease);
    }
    private bool Release(ReaderLease lease)
    {
        if (lease.Released) return lease.ReleaseConfirmed;
        lease.Released = true;
        if (!ReferenceEquals(current, lease)) return lease.ReleaseConfirmed = true;
        try
        {
            if (output is null || !output.TrySubmitCurrent(default, () => Owns(lease, releasing: true))) return false;
            neutral = true; return lease.ReleaseConfirmed = true;
        }
        catch { neutral = false; return false; }
    }
    private bool Owns(ReaderLease lease, bool releasing = false) => !disposed
        && ReferenceEquals(current, lease) && (releasing || !lease.Released);
    internal sealed class ReaderLease(SteamInputSessionOutput owner, ReaderBinding binding, Func<bool> canForward, Func<RelayExit?>? readSubmitPause)
        : IXboxRelayOutput, IExistingXboxRelayConnection
    {
        internal bool Released, ReleaseConfirmed, Connected;
        private bool neutralOnly;
        public bool ExistingConnection { get; internal set; }
        internal ReaderBinding Binding => binding;
        public bool IsOwned => owner.output is { IsOwned: true } && owner.Owns(this);
        public bool TryConnectNeutral(Func<bool> canAttach) => owner.Connect(this, () => IsCurrent() && canAttach());
        // This reader can attest/retire a neutral PDO while an authenticated
        // native prepare waits. It can never acquire gameplay forwarding.
        internal bool TryHoldNeutral(Func<bool> current)
        {
            if(neutralOnly||Connected||Released)return false;
            neutralOnly=true;
            return owner.Connect(this,()=>owner.Owns(this)&&current()&&owner.Owns(this));
        }
        private bool IsCurrent() => !neutralOnly && !Released && !owner.disposed && ReferenceEquals(owner.current, this) && canForward();
        public void Submit(XboxReport report)
        {
            bool neutralReport = report == default(XboxReport);
            bool guardDenied = false;
            bool Guard()
            {
                if (!owner.Owns(this)) return false;
                if (neutralReport) return true;
                bool valid = IsCurrent();
                guardDenied = !valid;
                return valid;
            }
            var held = owner.output;
            if (held is null || !held.TrySubmitCurrent(report, Guard))
            {
                // Only an actually evaluated forwarding guard can become an
                // authenticated opening pause. Native ownership short-circuit,
                // neutral-only/released leases and update failures stay fatal.
                if (guardDenied && !neutralOnly && !Released && owner.Owns(this)
                    && ReferenceEquals(owner.output, held))
                {
                    bool owned = held!.IsOwned;
                    var pause = owned ? readSubmitPause?.Invoke() : null;
                    bool exact = owned && !neutralOnly && !Released && owner.Owns(this)
                        && ReferenceEquals(owner.output, held);
                    throw new ReaderForwardGuardRejected(exact && pause == RelayExit.OverlayOpened);
                }
                throw new InvalidOperationException("Reader scope changed at the owned output update.");
            }
            owner.neutral = neutralReport;
        }
        // This retires ONLY the reader's forwarding lease, not the session device.
        public bool Close() => owner.Release(this);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (current is { } lease) lease.Released = true;
        if (output is not null) { try { CleanupConfirmed = output.Close(); } catch { CleanupConfirmed = false; } }
    }
}
internal sealed class FreshReleasedReaderFrames
{
    private uint previous;
    private int samples;
    internal bool Ready { get; private set; }
    internal bool Observe(uint sequence, XboxReport report, bool fresh)
    {
        if (Ready) return true;
        if (!fresh || !report.Released) { samples = 0; previous = sequence; return false; }
        if (samples == 0 || sequence <= previous) { samples = 1; previous = sequence; return false; }
        previous = sequence; return Ready = ++samples >= 2;
    }
}
