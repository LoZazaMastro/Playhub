// Playhub Xbox Session. Distributed under GPL-3.0-only.
// Managed-only command mailbox. Publish is called only by the authenticated
// Agent pipe transport. Worker facts are written only by the layout worker.
internal static class SteamNativeOverlayInputPresentationBridge
{
    internal sealed record Command(string Action,string OwnerToken,ulong Generation,string SessionId,
        ulong FullGameId,ulong ObservedGameId,uint WrapperPid,long WrapperBirth,uint GamePid,long GameBirth,
        uint SteamPid,long SteamBirth,uint HostPid,long HostBirth,uint BrowserId,uint NativeAppId,
        ulong Serial,long ExpiresAt);
    internal sealed record Snapshot(Command? Command,string Phase,bool ReaderStopped,bool NeutralHeld,
        bool MenuAcknowledged,bool AutomaticAcknowledged,bool InputReady,bool CleanupComplete,
        bool ResumeAllowed,long WorkerObservedAt,ulong Revision,bool NativeActiveSeen,bool ReadyEver,
        string InputMode,bool OrdinaryReady,bool NoOwnedOverride,bool Aborted,bool MenuLayoutReady=false);
    private sealed record Configuration(uint WrapperPid,long WrapperBirth,ulong GameId,uint GamePid,
        long GameBirth,Func<long> Clock,Func<Command,bool>? Authority);
    private static readonly object gate=new();
    private static Configuration? configured;
    private static Command? command;
    private static string phase="idle";
    private static bool stopped,neutral,menuAck,automaticAck,resume,nativeActive,readyEver,revoked;
    private static bool ordinary,noOverride,ordinaryMenu;
    private static long workerObserved;
    private static int workerThread;
    private static ulong revision,lastGeneration;
    internal static IDisposable Configure(uint wrapperPid,long wrapperBirth,ulong fullGameId,uint gamePid,
        long gameBirth,Func<long>? clock=null,Func<Command,bool>? authorityCurrent=null)
    {
        if(wrapperPid==0||wrapperBirth<=0||gamePid==0||gameBirth<=0||wrapperPid==gamePid
            ||(uint)fullGameId!=0x02000000||(fullGameId>>32)<0x80000000)
            throw new ArgumentException("Exact canonical owned game configuration required.");
        lock(gate)
        {
            if(configured is not null)throw new InvalidOperationException("Native input mailbox already configured.");
            var own=new Configuration(wrapperPid,wrapperBirth,fullGameId,gamePid,gameBirth,clock??(()=>Environment.TickCount64),authorityCurrent);
            configured=own;command=null;phase="idle";stopped=neutral=menuAck=automaticAck=resume=nativeActive=readyEver=revoked=false;
            ordinary=noOverride=ordinaryMenu=false;workerObserved=0;workerThread=0;lastGeneration=0;++revision;return new Scope(own);
        }
    }
    private sealed class Scope(Configuration own):IDisposable
    {
        public void Dispose(){lock(gate){if(!ReferenceEquals(configured,own))return;configured=null;command=null;phase="idle";++revision;}}
    }
    internal static bool Available {get{lock(gate)return configured is not null;}}
    private static bool SameOwner(Command a,Command b)=>a.OwnerToken==b.OwnerToken&&a.Generation==b.Generation
        &&a.SessionId==b.SessionId&&a.FullGameId==b.FullGameId&&a.ObservedGameId==b.ObservedGameId
        &&a.WrapperPid==b.WrapperPid&&a.WrapperBirth==b.WrapperBirth&&a.GamePid==b.GamePid&&a.GameBirth==b.GameBirth
        &&a.SteamPid==b.SteamPid&&a.SteamBirth==b.SteamBirth&&a.HostPid==b.HostPid&&a.HostBirth==b.HostBirth
        &&a.BrowserId==b.BrowserId&&a.NativeAppId==b.NativeAppId;
    private static bool Valid(Command c,Configuration target)=>Guid.TryParseExact(c.OwnerToken,"N",out var id)&&id!=Guid.Empty
        &&c.Generation>0&&c.Serial>0&&c.SessionId is {Length:64}&&c.SessionId.All(Uri.IsHexDigit)
        &&c.FullGameId==target.GameId&&c.GamePid==target.GamePid&&c.GameBirth==target.GameBirth
        &&c.WrapperPid==target.WrapperPid&&c.WrapperBirth==target.WrapperBirth
        &&c.SteamPid>0&&c.SteamBirth>0&&c.HostPid>0&&c.HostBirth>0&&c.HostPid!=c.GamePid
        &&c.HostPid!=c.WrapperPid&&c.HostPid!=c.SteamPid&&c.ObservedGameId>0&&c.BrowserId>0;
    internal static bool Publish(Command next)
    {
        lock(gate)
        {
            if(configured is not {} target||!Valid(next,target)||next.Action is not("prepare" or "active" or "renew" or "closed" or "cancel" or "abort"))return false;
            long now=target.Clock();if(!ReferenceEquals(configured,target))return false;
            if(command is {} old&&old.ExpiresAt<=now)revoked=true;
            bool cleanup=next.Action is "closed" or "cancel" or "abort";
            if(!cleanup&&(next.ExpiresAt<=now||next.ExpiresAt-now>5000))return false;
            if(next.Action=="prepare")
            {
                if(next.Generation<=lastGeneration||command is not null&&!(phase is "closed" or "aborted"&&(automaticAck||ordinary&&noOverride)&&resume))return false;
                lastGeneration=next.Generation;nativeActive=readyEver=revoked=false;phase="preparing";
                stopped=neutral=menuAck=automaticAck=resume=ordinary=noOverride=ordinaryMenu=false;workerObserved=0;
            }
            else
            {
                if(command is not {} previous||!SameOwner(previous,next)||next.Serial<=previous.Serial)return false;
                if(phase=="aborted"&&next.Action=="abort"&&(automaticAck||ordinary&&noOverride)&&resume&&!nativeActive)
                {command=previous with{Serial=next.Serial};++revision;return true;}
                if(next.Action=="abort"&&(nativeActive||previous.Action!="prepare"||phase is not("preparing" or "menu" or "ordinary")))return false;
                if(phase=="closed"&&(automaticAck||ordinary&&noOverride)
                    &&(next.Action=="cancel"||next.Action=="closed"&&previous.Action=="closed"))
                {command=previous with{Serial=next.Serial};++revision;return true;}
                if(next.Action=="renew"&&(!nativeActive||revoked||previous.ExpiresAt<=now||next.ExpiresAt<=previous.ExpiresAt))return false;
                if(next.Action=="closed"&&!nativeActive&&!revoked)return false;
                if(next.Action=="active")nativeActive=true;
                if(next.Action is "cancel" or "closed" or "abort")revoked=true;
                if(next.Action is "closed" or "cancel" or "abort")phase="returning";
                if(next.Action=="active"&&revoked)
                {next=next with{Action="revoked",ExpiresAt=previous.ExpiresAt};phase="returning";resume=false;}
            }
            command=next;++revision;return true;
        }
    }
    internal static Snapshot Read()
    {
        lock(gate)
        {
            var target=configured;long now=target?.Clock()??0;
            if(target is null||!ReferenceEquals(configured,target))return new(null,"idle",false,false,false,false,false,false,false,0,revision,false,false,"owned-relay",false,false,false);
            bool ready=!revoked&&command is {} c&&c.Action is "prepare" or "active" or "renew"&&c.ExpiresAt>now
                &&phase=="menu"&&stopped&&neutral&&menuAck;
            bool ordinaryReady=ordinary&&noOverride&&!revoked&&command is {} o&&o.ExpiresAt>now
                &&o.Action is "prepare" or "active" or "renew"&&phase=="ordinary";
            bool menuReady=ordinaryMenu&&menuAck&&!revoked&&command is {} m&&m.ExpiresAt>now
                &&m.Action is "prepare" or "active" or "renew"&&phase=="menu";
            return new(command,phase,stopped,neutral,menuAck,automaticAck,ready,phase is "closed" or "aborted"&&(automaticAck||ordinary&&noOverride),
                resume,workerObserved,revision,nativeActive,readyEver,ordinaryMenu?"ordinary-menu":ordinary?"ordinary":"owned-relay",ordinaryReady||menuReady,noOverride,phase=="aborted"&&resume,menuReady);
        }
    }
    // Worker-only admission/renew proof; Read() never invokes this native provider.
    internal static bool AuthorityCurrent(Command expected)
    {
        Configuration target;
        lock(gate)
        {
            if(configured is not {} held||command is not {} c||!SameOwner(c,expected)
                ||c.Serial<expected.Serial||revoked||c.Action is not("prepare" or "active" or "renew"))return false;
            target=held;long now=target.Clock();if(!ReferenceEquals(configured,target))return false;
            if(c.ExpiresAt<=now){revoked=true;return false;}
        }
        if(target.Authority?.Invoke(expected)!=true)return false;
        lock(gate)
        {
            long now=target.Clock();
            return ReferenceEquals(configured,target)&&command is {} c&&SameOwner(c,expected)
                &&c.Serial>=expected.Serial&&!revoked&&c.Action is "prepare" or "active" or "renew"&&c.ExpiresAt>now;
        }
    }
    // Cleanup-only source lifetime for an already observed real native close.
    // This never grants menu/input readiness and ignores the expired active lease.
    internal static bool FenceClosedSourceCurrent(Command expected)
    {
        Configuration held;ulong stamp;
        lock(gate)
        {
            if(configured is not {} target||command!=expected||expected.Action!="closed"||!nativeActive)return false;
            held=target;stamp=revision;
        }
        if(held.Authority?.Invoke(expected)!=true)return false;
        lock(gate)return ReferenceEquals(configured,held)&&command==expected&&expected.Action=="closed"&&nativeActive&&revision==stamp;
    }
    internal static bool AbortAuthorityCurrent(Command expected)
    {
        Configuration target; ulong stamp;
        lock(gate)
        {
            if(configured is not {} held||command is not {} c||!SameOwner(c,expected)||c.Action!="abort"||nativeActive
                ||c.Serial<expected.Serial)return false;
            target=held;stamp=revision;
        }
        if(target.Authority?.Invoke(expected)!=true)return false;
        lock(gate)return ReferenceEquals(configured,target)&&command is {} c&&SameOwner(c,expected)
            &&c.Action=="abort"&&!nativeActive&&c.Serial>=expected.Serial&&revision==stamp;
    }
    internal static void Report(string ownerToken,ulong generation,string nextPhase,bool readerStopped,
        bool neutralHeld,bool menuAcknowledged,bool automaticAcknowledged,bool resumeAllowed=false)
    {
        Command? abortExpected=null;
        if(nextPhase=="aborted") { var observed=Read();abortExpected=observed.Command;
            if(abortExpected is null||!AbortAuthorityCurrent(abortExpected))return; }
        lock(gate)
        {
            if(configured is not {} target||command is not {} c||c.OwnerToken!=ownerToken||c.Generation!=generation)return;
            if(nextPhase=="aborted"&&(abortExpected is null||c!=abortExpected||nativeActive||c.Action!="abort"))return;
            if(workerThread!=0&&workerThread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Only the layout worker publishes native input facts.");
            if(nextPhase is not("preparing" or "menu" or "returning" or "closed" or "uncertain" or "aborted"))throw new ArgumentException("Unknown native input worker phase.");
            long now=target.Clock();if(!ReferenceEquals(configured,target))return;
            workerThread=Environment.CurrentManagedThreadId;phase=nextPhase;stopped=readerStopped;neutral=neutralHeld;
            menuAck=menuAcknowledged;automaticAck=automaticAcknowledged;workerObserved=now;
            if(!revoked&&nextPhase=="menu"&&readerStopped&&neutralHeld&&menuAcknowledged&&c.ExpiresAt>now
                &&c.Action is "prepare" or "active" or "renew")readyEver=true;
            resume=resumeAllowed&&automaticAcknowledged
                &&(nextPhase=="closed"&&(c.Action=="closed"&&(nativeActive||revoked)||c.Action=="cancel"&&!readyEver)
                    ||nextPhase=="aborted"&&c.Action=="abort"&&!nativeActive&&readerStopped&&neutralHeld);
            ++revision;
        }
    }
    internal static void ReportOrdinary(string ownerToken,ulong generation,bool closed=false,bool aborted=false)
    {
        Command? abortExpected=null;
        if(aborted){abortExpected=Read().Command;if(abortExpected is null||!AbortAuthorityCurrent(abortExpected))return;}
        lock(gate)
        {
            if(configured is not {} target||command is not {} c||c.OwnerToken!=ownerToken||c.Generation!=generation)return;
            if(aborted&&(c!=abortExpected||nativeActive||c.Action!="abort"))return;
            long now=target.Clock();if(!ReferenceEquals(configured,target))return;
            ordinary=noOverride=true;stopped=neutral=menuAck=automaticAck=false;phase=aborted?"aborted":closed?"closed":"ordinary";
            workerObserved=now;
            if(!closed&&!revoked&&c.ExpiresAt>now)readyEver=true;
            resume=aborted&&c.Action=="abort"&&!nativeActive||closed&&(c.Action=="closed"&&(nativeActive||revoked)||c.Action=="cancel"&&!readyEver);
            ++revision;
        }
    }
    internal static void ReportOrdinaryMenu(string ownerToken,ulong generation,bool menuAcknowledged,
        bool automaticAcknowledged=false,bool closed=false,bool aborted=false)
    {
        var expected=Read().Command;
        if(aborted&&(expected is null||!AbortAuthorityCurrent(expected)))return;
        lock(gate)
        {
            if(configured is not {} target||command is not {} c||c.OwnerToken!=ownerToken||c.Generation!=generation)return;
            if(aborted&&(c!=expected||nativeActive||c.Action!="abort"))return;
            if(workerThread!=0&&workerThread!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Only the layout worker publishes native input facts.");
            long now=target.Clock();if(!ReferenceEquals(configured,target))return;
            workerThread=Environment.CurrentManagedThreadId;ordinaryMenu=true;ordinary=false;
            stopped=neutral=false;menuAck=menuAcknowledged;automaticAck=automaticAcknowledged;noOverride=automaticAcknowledged;
            phase=aborted?"aborted":closed?"closed":menuAcknowledged?"menu":!readyEver&&c.Action=="prepare"?"preparing":"returning";workerObserved=now;
            if(menuAcknowledged&&!revoked&&c.ExpiresAt>now&&c.Action is "prepare" or "active" or "renew")readyEver=true;
            resume=automaticAcknowledged&&(aborted&&c.Action=="abort"&&!nativeActive
                ||closed&&(c.Action=="closed"&&(nativeActive||revoked)||c.Action=="cancel"&&!readyEver));
            ++revision;
        }
    }
}
