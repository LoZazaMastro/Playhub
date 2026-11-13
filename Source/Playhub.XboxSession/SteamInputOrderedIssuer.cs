// Playhub Xbox Session. Distributed under GPL-3.0-only.
// TEMP patch B core. Only the existing synchronous layout worker may call it.
internal sealed class SteamInputOrderedIssuer(
    SteamRawInputRelayTrial.SessionBinding session, SteamInputOrderedIssuer.InputRoute route,
    Func<bool> steamCurrent, Func<bool> gameCurrent, Func<bool> controllerCurrent, Func<long> clock,
    Func<SteamInputOrderedIssuer.MirrorLease, bool> presentationCurrent,
    Func<bool> neutralHeld, SteamInputOrderedIssuer.IChannel channel,
    Func<SteamInputOrderedIssuer.NativeOverlayInputLease,bool>? nativePresentationCurrent = null)
{
    internal enum InputRoute { OwnedSessionPad, SteamOrdinary }
    internal readonly record struct Receipt(object Identity, uint Layout);
    internal enum Acknowledgement { Pending, Acknowledged, Failed, Unknown }
    internal interface IChannel
    {
        // Identity is the exact retained launcher Process object, not PID alone.
        Receipt? Start(uint layout, Receipt? expectedPrior, Func<bool> finalGuard);
        Acknowledgement Read(Receipt receipt);
    }
    internal interface IInputLease
    {
        Guid Owner {get;} string SessionId {get;} ulong Generation {get;} ulong GameId {get;}
        long ExpiresAt {get;} SteamRawInputRelayTrial.SessionBinding Session {get;}
        long Epoch {get;} SteamInputSessionOutput.ReaderBinding? Reader {get;}
    }
    internal sealed record MirrorLease(Guid Owner, string SessionId, ulong Generation, ulong GameId,
        long ExpiresAt, SteamRawInputRelayTrial.SessionBinding Session, long Epoch,
        SteamInputSessionOutput.ReaderBinding? Reader = null) : IInputLease;
    // An authenticated original game-host input lease, never a presentation/scope2 proof.
    internal sealed record NativeOverlayInputLease(Guid Owner,string SessionId,ulong Generation,ulong GameId,
        long ExpiresAt,SteamRawInputRelayTrial.SessionBinding Session,long Epoch,
        SteamInputSessionOutput.ReaderBinding? Reader,uint HostPid,long HostBirth,uint BrowserId,
        uint NativeAppId,ulong ObservedGameId,ulong AdmissionSerial) : IInputLease;
    internal enum Phase { Game, WaitingForReader, WaitingForPriorCommand, WaitingForMenuAck, Menu,
        Returning, WaitingForAutomaticAck, ResumeAllowed, Uncertain }
    private readonly int worker = Environment.CurrentManagedThreadId;
    private Receipt? pending, latest;
    private IInputLease? presentation;
    private long retiredEpoch;
    private ulong authority;
    private ulong menuAuthority;
    internal Phase State { get; private set; } = Phase.Game;
    private bool OutputReady() => route == InputRoute.SteamOrdinary || neutralHeld();
    internal bool UiInputAllowed
    {
        get
        {
            var held=presentation;ulong stamp=authority;
            return State==Phase.Menu && held is not null && StillOwned() && CurrentPresentation() && StillOwned()
                && State==Phase.Menu && ReferenceEquals(presentation,held) && authority==stamp;
        }
    }
    internal bool ResumeAllowed
    {
        get
        {
            ulong stamp=authority;var prior=pending;
            return State==Phase.ResumeAllowed && prior is null && AutomaticSettled && steamCurrent() && gameCurrent() && controllerCurrent() && OutputReady() && StillOwned()
                && State==Phase.ResumeAllowed && pending is null && presentation is null && authority==stamp;
        }
    }
    internal bool AutomaticSettled => State==Phase.ResumeAllowed && pending is null
        && (latest is null || AutomaticAcknowledged) && State==Phase.ResumeAllowed && pending is null;
    internal bool AutomaticAcknowledged => State==Phase.ResumeAllowed && pending is null
        && latest is {} held && held.Layout==0 && channel.Read(held)==Acknowledgement.Acknowledged && StillOwned() && State==Phase.ResumeAllowed && pending is null;
    private void Worker() { if (Environment.CurrentManagedThreadId != worker) throw new InvalidOperationException("Issuer belongs to layout worker."); }
    private bool StillOwned()
    {
        if(latest is not {} held) return true;
        ulong epoch=authority;
        var result=channel.Read(held);
        return result is Acknowledgement.Acknowledged or Acknowledgement.Pending or Acknowledgement.Failed
            && latest is {} now && ReferenceEquals(now.Identity,held.Identity) && now.Layout==held.Layout
            && authority==epoch && steamCurrent();
    }
    private bool CurrentPresentation()
    {
        var held = presentation;
        if (held is null || !Valid(held) || !SameSession(held.Session)) return false;
        bool trusted = ReadPresentation(held);
        return trusted && ReferenceEquals(presentation, held) && Valid(held) && steamCurrent() && gameCurrent() && controllerCurrent()
            && ReadPresentation(held) && ReferenceEquals(presentation, held) && clock() < held.ExpiresAt;
    }
    private bool ReadPresentation(IInputLease held) => held switch
    {
        MirrorLease m => presentationCurrent(m),
        NativeOverlayInputLease n => nativePresentationCurrent?.Invoke(n) == true,
        _ => false
    };
    private static bool Valid(IInputLease m) => m.Owner != Guid.Empty && m.Generation > 0
        && m.SessionId is {Length:64} && m.SessionId.All(Uri.IsHexDigit) && m.Session.Valid && m.Epoch > 0
        && (m.Reader is null || m.Reader is {} reader && reader.Valid && reader.Session==m.Session && reader.Epoch==m.Epoch)
        && m.GameId == m.Session.GameId && m.ExpiresAt > 0
        && (m is not NativeOverlayInputLease n || n.HostPid>0 && n.HostBirth>0
            && n.HostPid!=m.Session.GamePid && n.HostPid!=m.Session.CompanionPid
            && n.BrowserId>0 && n.ObservedGameId>0 && n.AdmissionSerial>0);
    private bool SameSession(SteamRawInputRelayTrial.SessionBinding binding) => session.Valid
        && session.SameRetainedSession(binding) && session.Source == binding.Source;
    internal bool Begin(MirrorLease lease) => BeginLease(lease);
    internal bool BeginNative(NativeOverlayInputLease lease)
    {
        Worker();
        return route==InputRoute.OwnedSessionPad && lease.Reader is not null && BeginLease(lease);
    }
    private bool BeginLease(IInputLease lease)
    {
        Worker();
        if (State is not (Phase.Game or Phase.ResumeAllowed) || presentation is not null || !Valid(lease) || !SameSession(lease.Session)
            || route==InputRoute.OwnedSessionPad && lease.Reader is null || route==InputRoute.SteamOrdinary && lease.Reader is not null) return false;
        if(State==Phase.ResumeAllowed&&!AutomaticSettled)return false;
        presentation = lease;
        if (!CurrentPresentation()) { if(ReferenceEquals(presentation,lease)) Close(lease.Owner); return false; }
        State = Phase.WaitingForReader; return true;
    }
    internal bool Renew(MirrorLease lease) => RenewLease(lease);
    internal bool RenewNative(NativeOverlayInputLease lease) => RenewLease(lease);
    private bool RenewLease(IInputLease lease)
    {
        Worker();
        var old = presentation;
        if (old is null || old.GetType()!=lease.GetType() || !SameNativeIdentity(old,lease) || old.Owner != lease.Owner || old.Generation != lease.Generation || old.SessionId != lease.SessionId
            || old.Reader != lease.Reader || old.Session != lease.Session || old.Epoch != lease.Epoch || old.GameId != lease.GameId || lease.ExpiresAt <= old.ExpiresAt || !Valid(lease)) return false;
        presentation = lease;
        if (CurrentPresentation()) return true;
        if(ReferenceEquals(presentation,lease)) Close(lease.Owner); return false;
    }
    private static bool SameNativeIdentity(IInputLease before,IInputLease after) => before is not NativeOverlayInputLease a
        || after is NativeOverlayInputLease b && a.HostPid==b.HostPid && a.HostBirth==b.HostBirth
            && a.BrowserId==b.BrowserId && a.NativeAppId==b.NativeAppId && a.ObservedGameId==b.ObservedGameId
            && a.AdmissionSerial==b.AdmissionSerial;
    internal bool ReaderRetired(SteamInputSessionOutput.ReaderBinding binding, SteamRawInputRelayTrial.ReaderOutcome outcome)
    {
        Worker();
        if (route!=InputRoute.OwnedSessionPad || State != Phase.WaitingForReader || presentation is not { Reader: {} expected } lease || binding != expected || !outcome.CanResume
            || outcome.ReaderPid != binding.ReaderPid || outcome.ReaderBirth != binding.ReaderBirth || outcome.Epoch != binding.Epoch
            || presentation is NativeOverlayInputLease && outcome.EndReason!=RelayExit.OverlayOpened
            || !OutputReady() || !CurrentPresentation()) return false;
        retiredEpoch = binding.Epoch; State = Phase.WaitingForPriorCommand; return true;
    }
    internal bool OrdinarySourceReady()
    {
        Worker();
        if(route!=InputRoute.SteamOrdinary || State!=Phase.WaitingForReader || presentation is not {Reader:null} lease
            || !controllerCurrent() || !CurrentPresentation()) return false;
        retiredEpoch=lease.Epoch;State=Phase.WaitingForPriorCommand;return true;
    }
    internal bool ReturnAutomatic()
    {
        Worker();presentation=null;
        if(!StillOwned()){State=Phase.Uncertain;return false;}
        menuAuthority=latest is {} own && own.Layout!=0 ? authority:0;
        State=Phase.Returning;return true;
    }
    internal bool Close(Guid owner)
    {
        Worker();
        if (presentation is not { } m || owner != m.Owner) return false;
        // Revoke UI capability before any cleanup command. Foreign game focus
        // does not prohibit cleanup of this issuer's own override.
        return ReturnAutomatic();
    }
    internal void Tick()
    {
        Worker();
        if (!steamCurrent() || !StillOwned()) { presentation = null; State = Phase.Uncertain; return; }
        var active = presentation;
        if (active is not null && (!controllerCurrent() || !CurrentPresentation())) Close(active.Owner);
        if (State == Phase.WaitingForPriorCommand)
        {
            if (!Settled()) return;
            if (!OutputReady() || !CurrentPresentation()) { if(presentation is not null) Close(presentation.Owner); return; }
            // The same channel is used for game/0/769; no parallel URL writer.
            var started = channel.Start(769, latest, () => State == Phase.WaitingForPriorCommand && OutputReady() && CurrentPresentation());
            if (started is null) { presentation = null; State = Phase.Uncertain; return; }
            pending = latest = started; menuAuthority = ++authority;
            State = presentation is not null && CurrentPresentation() ? Phase.WaitingForMenuAck : Phase.Returning;
        }
        if (State == Phase.WaitingForMenuAck && Settled())
            State = CurrentPresentation() ? Phase.Menu : Phase.Returning;
        if (State == Phase.Returning)
        {
            // Failed delivery still belongs to our exact launcher and may have
            // applied. It permits ONLY ordered automatic cleanup, never UI/game readiness.
            if (!SettledForCleanup()) return;
            if (menuAuthority == 0 || menuAuthority != authority)
            {
                if(latest is {} previous&&previous.Layout==0&&channel.Read(previous)!=Acknowledgement.Acknowledged)
                    {State=Phase.Uncertain;return;}
                State = Phase.ResumeAllowed; return;
            }
            var own = menuAuthority;
            var started = channel.Start(0, latest, () => State == Phase.Returning && menuAuthority == own && authority == own && steamCurrent());
            if (started is null) { State = Phase.Uncertain; return; }
            pending = latest = started; ++authority; menuAuthority = 0; State = Phase.WaitingForAutomaticAck;
        }
        if (State == Phase.WaitingForAutomaticAck && Settled()) State = Phase.ResumeAllowed;
    }
    private bool SettledForCleanup()
    {
        if(pending is not {} p)return true;
        var result=channel.Read(p);
        if(pending is not {} current||!ReferenceEquals(current.Identity,p.Identity)||current.Layout!=p.Layout
            ||result==Acknowledgement.Unknown||!steamCurrent()){State=Phase.Uncertain;return false;}
        if(result==Acknowledgement.Pending)return false;
        pending=null;return true;
    }
    private bool Settled()
    {
        if (pending is not { } p) return true;
        var result = channel.Read(p);
        // A channel callback can reenter or replace the command; never adopt an
        // acknowledgment for a different exact retained process identity.
        if (pending is not { } now || !ReferenceEquals(now.Identity,p.Identity) || now.Layout != p.Layout) { State = Phase.Uncertain; return false; }
        if (result == Acknowledgement.Pending) return false;
        if (result != Acknowledgement.Acknowledged || !steamCurrent()) { presentation = null; State = Phase.Uncertain; return false; }
        pending = null; return true;
    }
    internal bool SendGame(uint app, long nextEpoch, Func<bool> scope)
    {
        Worker();
        if (State is not (Phase.Game or Phase.ResumeAllowed) || app != (uint)(session.GameId >> 32) || app < 0x80000000
            || !session.Valid || nextEpoch <= 0 || !Settled() || !steamCurrent() || !gameCurrent() || !controllerCurrent() || !scope()) return false;
        // The game layout must be selected BEFORE Steam creates the next
        // native reader. A193 independently binds/rechecks its new PID/birth
        // and two fresh released frames before forwarding.
        if (nextEpoch <= retiredEpoch || State==Phase.ResumeAllowed&&!AutomaticSettled) return false;
        if(route==InputRoute.SteamOrdinary){State=Phase.Game;return true;}
        var started=channel.Start(app,latest,()=>State is Phase.Game or Phase.ResumeAllowed && steamCurrent() && gameCurrent() && controllerCurrent() && scope());
        if (started is null) {State=Phase.Uncertain;return false;}
        pending=latest=started;++authority;menuAuthority=0;State=presentation is null ? Phase.Game : Phase.WaitingForReader;return true;
    }
    internal bool GameCommandAcknowledged(uint app)
    {
        Worker();return State==Phase.Game && latest is {} held && held.Layout==app && Settled() && StillOwned();
    }
    // Adapter must import the actual prior owned Process/layout, including an
    // already-dispatched automatic0, before this issuer starts its first command.
    internal bool AdoptPrior(Receipt receipt)
    {
        Worker(); if(pending is not null || presentation is not null || State!=Phase.Game || receipt.Identity is null || receipt.Layout is not 0 && receipt.Layout != (uint)(session.GameId>>32)) return false;
        pending=latest=receipt; ++authority;return true;
    }
}
