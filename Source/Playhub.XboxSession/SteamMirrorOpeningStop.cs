// GPL-3.0-only. STOP evidence only; never grants 769 or UI input authority.
internal sealed class SteamMirrorOpeningStop(ulong gameId,uint gamePid,long gameBirth)
{
    private SteamMirrorInputPresentation? admitted;
    private bool closeQueued,requiresNativeClose;
    internal bool NativeClosePending => requiresNativeClose && admitted is {} pending
        && (pending.State is 0 or 4 || !pending.PreparedCurrent || !pending.ShownCurrent);
    internal bool Blocking => admitted is not null;
    internal bool CancellationUnconfirmed {get;private set;}
    internal SteamMirrorInputPresentation? Admission => admitted;
    private bool Game(SteamMirrorInputPresentation m)=>m.GameId==gameId&&m.GamePid==gamePid&&m.GameBirth==gameBirth;
    private static bool SameOwner(SteamMirrorInputPresentation a,SteamMirrorInputPresentation b)=>
        a.SessionId==b.SessionId&&a.Generation==b.Generation&&a.GameId==b.GameId&&a.GamePid==b.GamePid&&a.GameBirth==b.GameBirth
        &&a.OwnerPid==b.OwnerPid&&a.OwnerBirth==b.OwnerBirth&&a.SourceWindow==b.SourceWindow&&a.SourceCookie==b.SourceCookie;
    internal void Observe(SteamMirrorInputPresentation? value,long now)
    {
        // The caller reads the process-local bridge: its parser already binds
        // owner PID/birth and the retained game. No frontend proof is accepted.
        if(value is null||!Game(value))return;
        if(admitted is {} held)
        {
            if(!SameOwner(held,value))return;
            if(value.OwnedForegroundPhase==SteamMirrorOwnedForegroundPhase.AwaitingNativeClose)requiresNativeClose=true;
            if(requiresNativeClose&&value.OwnedForegroundPhase==SteamMirrorOwnedForegroundPhase.None)return;
            if(value.State==0&&value.CleanupComplete&&value.DestinationWindow==0
                &&(!requiresNativeClose||value.OwnedForegroundPhase==SteamMirrorOwnedForegroundPhase.NativeClosed)&&(value.DestinationCookie==0||value.DestinationCookie==held.DestinationCookie)
                &&!value.PreparedCurrent&&!value.ShownCurrent&&!value.DestinationShown&&!value.DwmAttached)
            {admitted=null;closeQueued=false;requiresNativeClose=false;CancellationUnconfirmed=false;return;}
            if(value.State is 0 or 4 &&requiresNativeClose
                &&value.OwnedForegroundPhase==SteamMirrorOwnedForegroundPhase.AwaitingNativeClose){admitted=value;return;}
            if(value.State is 1 or 2 or 3 or 4 &&held.SamePresentation(value))admitted=value;
            return;
        }
        if((value.State is 0 or 4 ||value.State==3&&(!value.PreparedCurrent||!value.ShownCurrent))
            &&value.Generation>0&&value.OwnerPid>0&&value.OwnerBirth>0
            &&value.SourceWindow!=0&&value.SourceCookie>0
            &&value.OwnedForegroundPhase==SteamMirrorOwnedForegroundPhase.AwaitingNativeClose)
        {admitted=value;requiresNativeClose=true;closeQueued=false;return;}
        if(value.State is 1 or 2 or 3 &&value.Generation>0&&value.OwnerPid>0&&value.OwnerBirth>0
            &&value.SourceWindow!=0&&value.SourceCookie>0&&value.DestinationWindow!=0&&value.DestinationCookie>0
            &&value.ReadAt<=now&&now-value.ReadAt<=250&&value.ExpiresAt>now)
        {admitted=value;requiresNativeClose=value.OwnedForegroundPhase==SteamMirrorOwnedForegroundPhase.AwaitingNativeClose;closeQueued=false;CancellationUnconfirmed=false;}
    }
    internal bool Expired(long now)=>admitted is {} held&&now>=held.ExpiresAt;
    internal bool GiveUpWaiting(long now)
    {
        if(NativeClosePending)return false;
        if(admitted is not {} held||now<=held.ExpiresAt||now-held.ExpiresAt<=1500)return false;
        CancellationUnconfirmed=true;return true; // fail closed, never optimistic resume
    }
    internal void RequestExpiredClose(long now,Func<byte[]?> readWire,Func<byte[],bool> queue)
    {
        if(closeQueued||!Expired(now)||admitted is not {} held)return;
        var wire=readWire();if(wire is null)return;
        var current=SteamMirrorInputPresentation.Parse(wire,held.OwnerPid,held.OwnerBirth,held.GamePid,held.GameBirth,held.GameId);
        if(current is null||!held.SamePresentation(current)||current.State is not(1 or 2 or 3))return;
        closeQueued=queue(SteamMirrorStatus.Control(wire,3));
    }
}
