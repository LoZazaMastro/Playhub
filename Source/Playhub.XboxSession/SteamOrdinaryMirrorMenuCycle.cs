// Playhub Xbox Session. Distributed under GPL-3.0-only.
// Menu-only ordinary-controller lease. No physical SC, reader or owned PDO proof.
internal sealed class SteamOrdinaryMirrorMenuCycle(ulong gameId,uint gamePid,long gameBirth)
{
    internal readonly record struct Result(bool MenuCommandStarted,bool MenuLauncherAcknowledged,
        bool AutomaticCleanup,string EndReason);
    private readonly int worker=Environment.CurrentManagedThreadId;
    private string? retiredSession;
    private ulong retiredGeneration;
    private bool Target(SteamMirrorInputPresentation p)=>p.GameId==gameId&&p.GamePid==gamePid&&p.GameBirth==gameBirth
        &&gamePid>0&&gameBirth>0&&(uint)gameId==0x02000000&&gameId>>32>=0x80000000
        &&p.OwnerPid==(uint)Environment.ProcessId&&p.OwnerBirth>0&&p.OwnerPid!=p.GamePid
        &&p.SourceWindow!=0&&p.DestinationWindow!=0&&p.SourceWindow!=p.DestinationWindow
        &&p.SourceCookie>0&&p.DestinationCookie>0&&p.Generation>0
        &&p.SessionId is {Length:64}&&p.SessionId.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    // Prepared can become Shown between native/window getters. Wait without
    // dispatching or retiring this generation until Shown is stably attested.
    private bool Fresh(SteamMirrorInputPresentation? p,long now)=>p is not null&&Target(p)&&p.Shown(now)
        &&now-p.ReadAt<=250;
    private bool Retired(SteamMirrorInputPresentation p)=>p.SessionId==retiredSession&&p.Generation<=retiredGeneration;
    private void Retire(SteamMirrorInputPresentation p)
    {
        retiredGeneration=p.SessionId==retiredSession?Math.Max(retiredGeneration,p.Generation):p.Generation;
        retiredSession=p.SessionId;
    }

    internal Result? Run(Func<SteamMirrorInputPresentation?> read,Func<SteamMirrorInputPresentation?> raw,
        Func<bool> noOwnedOverride,Func<bool> current,Func<long> clock,Action<int> wait,
        CancellationToken cancellation,SteamNativeOverlayInputCycle.OrdinaryChannel channel,Action<string>? trace=null)
    {
        if(Environment.CurrentManagedThreadId!=worker)throw new InvalidOperationException("Mirror menu lease belongs to its layout worker.");
        if(cancellation.IsCancellationRequested||!current()||!noOwnedOverride())return null;
        var initial=read();
        if(!Fresh(initial,clock())||Retired(initial!))return null;
        SteamMirrorInputPresentation admitted=initial!;
        if(!current()||!noOwnedOverride())return null;
        var checkedAdmission=read();
        if(!Fresh(checkedAdmission,clock())||!admitted.SamePresentation(checkedAdmission!))return null;
        var latest=channel.ReadCurrent();
        if(latest is {Layout:not 0}||!noOwnedOverride()||!current())return null;
        bool started=false,ready=false,cleaned=false;
        string reason="session-ended";
        bool Grant()
        {
            if(cancellation.IsCancellationRequested||!current())return false;
            var first=read();
            if(!Fresh(first,clock())||!admitted.SamePresentation(first!))return false;
            if(!current())return false;
            var last=read();
            return Fresh(last,clock())&&admitted.SamePresentation(last!)&&current()
                &&!cancellation.IsCancellationRequested&&Fresh(last,clock());
        }
        bool Cleanup()
        {
            if(cleaned)return true;
            // A refused/unknown dispatch has no transferable receipt. Never
            // synthesize one or issue an unowned automatic/game layout reset.
            if(!started)return false;
            var zero=channel.Channel.Start(0,latest,()=>true);
            if(zero is null)return false;
            latest=zero;
            long until=clock()+1500;
            for(int sample=0;sample<100&&clock()<=until;sample++)
            {
                var status=channel.Channel.Read(zero.Value);
                if(status==SteamInputOrderedIssuer.Acknowledgement.Acknowledged)return cleaned=true;
                if(status!=SteamInputOrderedIssuer.Acknowledgement.Pending)return false;
                wait(15);
            }
            return false;
        }
        bool automatic=false;
        // No channel invocation has happened yet. A fresh pre-dispatch refusal
        // may be sampled again without retiring a command that never existed.
        if(!Grant())return null;
        try
        {
            var menu=channel.Channel.Start(769,latest,Grant);
            if(menu is null){reason="menu-dispatch-refused";return new(false,false,false,reason);}
            latest=menu;started=true;trace?.Invoke("menu-command-started");
            while(!cancellation.IsCancellationRequested&&current())
            {
                if(!Grant())
                {
                    var closed=raw();
                    reason=closed is not null&&closed.SessionId==admitted.SessionId&&closed.Generation==admitted.Generation
                        &&closed.GameId==gameId&&closed.GamePid==gamePid&&closed.GameBirth==gameBirth
                        &&closed.OwnerPid==admitted.OwnerPid&&closed.OwnerBirth==admitted.OwnerBirth
                        &&closed.State==0&&closed.CleanupComplete?"presentation-closed":"presentation-no-longer-current";
                    break;
                }
                if(!ready)
                {
                    var status=channel.Channel.Read(latest!.Value);
                    if(status is SteamInputOrderedIssuer.Acknowledgement.Unknown or SteamInputOrderedIssuer.Acknowledgement.Failed)
                    {reason="menu-launcher-unknown";break;}
                    ready=status==SteamInputOrderedIssuer.Acknowledgement.Acknowledged;
                    if(ready)trace?.Invoke("menu-launcher-receipt");
                }
                wait(15);
            }
            if(cancellation.IsCancellationRequested)reason="cancelled";
        }
        finally
        {
            // Retirement is sticky for this exact generation, even if a late
            // provider/foreground recovery makes it look current again.
            Retire(admitted);
            automatic=Cleanup();
            trace?.Invoke(automatic?"automatic-cleanup-confirmed":"automatic-cleanup-unknown");
        }
        return new(started,ready,automatic,reason);
    }
}
