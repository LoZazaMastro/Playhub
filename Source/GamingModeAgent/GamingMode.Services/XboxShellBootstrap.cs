// SPDX-License-Identifier: GPL-3.0-only
namespace GamingMode.Services;

// A ticket grants only the possible retirement of the exact shell started here.
// It never grants activation, foreground, game input, or another Explorer process.
internal sealed class XboxShellBootstrap : IDisposable
{
    internal sealed record Caller(uint AppId, uint WrapperPid, long WrapperBirth, string Aumid, string Executable);
    internal sealed record ShellIdentity(uint Pid, long Birth, uint Session, string Image);
    internal interface IOwnedShell : IDisposable
    {
        ShellIdentity Identity { get; }
        bool Current { get; }
        bool OwnsDesktopShell { get; }
        bool NoUserExplorer { get; }
        bool StopExact(Func<bool> finalCurrent);
    }
    internal interface ITarget : IDisposable
    {
        Caller Caller { get; }
        uint GamePid { get; }
        long GameBirth { get; }
        bool Current { get; }
        bool CheapCurrent { get; }
        // Only a signaled retained game-process handle proves exit. A missing
        // window, suspension, an invalid handle, or !Current does not.
        bool GameExited { get; }
    }
    internal sealed record BeginResult(string Ticket);
    internal enum Completion { Abandoned, Pending, Retired, Hosted }
    internal enum ExitCleanup { Refused, Pending, Ready }
    sealed class Lease(string ticket, Caller caller, IOwnedShell shell, long started, long deadline)
    {
        internal readonly string Ticket=ticket;
        internal readonly Caller Caller=caller;
        internal readonly IOwnedShell Shell=shell;
        internal readonly long Started=started, Deadline=deadline;
        internal uint GamePid;
        internal long GameBirth;
        internal ITarget? Target;
        internal IDisposable? Expiry;
        internal long ExitObservedAt, ExitCleanupDeadline;
        internal bool ExitCleanupStarted;
    }
    readonly object gate=new();
    readonly Func<Caller, bool> callerCurrent;
    readonly Func<bool> noExplorer;
    readonly Func<IOwnedShell?> start;
    readonly Func<Func<bool>, bool> withGaming;
    readonly Func<Caller,uint,CancellationToken,Task<ITarget?>> resolve;
    readonly Func<ITarget,Func<bool>,bool> withCurtainReleased;
    readonly Func<ITarget,Func<bool>,ExitCleanup> cleanupAfterExit;
    readonly Func<long> clock;
    readonly Func<int,Action,IDisposable> schedule;
    readonly Action<string> report;
    Lease? active;
    bool disposed;
    internal XboxShellBootstrap(Func<Caller,bool> callerCurrent,Func<bool> noExplorer,Func<IOwnedShell?> start,
        Func<Func<bool>,bool> withGaming,Func<Caller,uint,CancellationToken,Task<ITarget?>> resolve,
        Func<ITarget,Func<bool>,bool> withCurtainReleased,Action<string> report,Func<long>? clock=null,
        Func<int,Action,IDisposable>? schedule=null,
        Func<ITarget,Func<bool>,ExitCleanup>? cleanupAfterExit=null)
    {
        (this.callerCurrent,this.noExplorer,this.start,this.withGaming,this.resolve,this.withCurtainReleased,this.report)=
            (callerCurrent,noExplorer,start,withGaming,resolve,withCurtainReleased,report);
        this.cleanupAfterExit=cleanupAfterExit??((target,action)=>this.withCurtainReleased(target,action)?ExitCleanup.Ready:ExitCleanup.Refused);
        this.clock=clock??(()=>Environment.TickCount64);
        this.schedule=schedule??((ms,action)=>new Timer(_=>action(),null,ms,Timeout.Infinite));
    }
    internal BeginResult? Begin(Caller caller)
    {
        BeginResult? result=null;
        withGaming(()=>{
            lock(gate){
                if(disposed)return false;
                long now=clock();
                if(active is {} prior){
                    if(prior.Caller==caller&&Valid(prior,now)&&(prior.Target is not null||callerCurrent(caller))){
                        result=new(prior.Ticket);return true;
                    }
                    // A second launch cannot inherit or close a shared shell.
                    Abandon(prior,"launch-replaced");return false;
                }
                if(caller.AppId<0x80000000||caller.WrapperPid==0||caller.WrapperBirth<=0||string.IsNullOrEmpty(caller.Aumid)
                    ||string.IsNullOrEmpty(caller.Executable)||!callerCurrent(caller)||!noExplorer())return false;
                long before=clock();
                var shell=start();
                if(shell is null)return false;
                if(!shell.Current||shell.Identity.Pid==0||shell.Identity.Birth<=caller.WrapperBirth||!callerCurrent(caller)
                    ||clock()<before||clock()>=checked(before+45000)){
                    shell.Dispose();report("bootstrap-start-unowned");return false;
                }
                var held=new Lease(Guid.NewGuid().ToString("N"),caller,shell,before,checked(before+45000));
                active=held;
                held.Expiry=schedule(checked((int)(held.Deadline-clock())),()=>Expire(held));
                result=new(held.Ticket);report("bootstrap-owned");return true;
            }
        });
        return result;
    }
    internal async Task<Completion> CompleteAsync(string ticket,uint appId,uint gamePid,CancellationToken cancellation)
    {
        Lease held;
        lock(gate){
            if(disposed||active is not {} found||found.Ticket!=ticket||found.Caller.AppId!=appId)return Completion.Abandoned;
            held=found;
            if(held.Target is not null){
                if(held.GamePid==gamePid&&Valid(held,clock()))return Completion.Hosted;
                Abandon(held,"bootstrap-target-replaced");return Completion.Abandoned;
            }
            if(gamePid==0||!Valid(held,clock())){Abandon(held,"bootstrap-expired");return Completion.Abandoned;}
        }
        var target=await resolve(held.Caller,gamePid,cancellation).ConfigureAwait(false);
        if(target is null)return StillPending(held);
        bool retained=false;
        try{
        Completion completion=Completion.Pending;
        bool modeEntered=false;
        withGaming(()=>{
            modeEntered=true;
            lock(gate){
                if(!ReferenceEquals(active,held)||!Valid(held,clock())){
                    Abandon(held,"bootstrap-expired");completion=Completion.Abandoned;return false;
                }
                // Concurrent completion requests may resolve independently;
                // only the first may transfer a target into this lease.
                if(held.Target is not null){
                    retained=ReferenceEquals(held.Target,target);
                    completion=held.GamePid==gamePid?Completion.Hosted:Completion.Abandoned;return false;
                }
                if(target.Caller!=held.Caller||target.GamePid!=gamePid||target.GameBirth<held.Shell.Identity.Birth
                    ||held.GamePid!=0&&(held.GamePid!=target.GamePid||held.GameBirth!=target.GameBirth)){
                    Abandon(held,"bootstrap-target-replaced");completion=Completion.Abandoned;return false;
                }
                if(!target.Current)return false;
                held.GamePid=target.GamePid;held.GameBirth=target.GameBirth;
                bool Current()=>ReferenceEquals(active,held)&&!disposed&&withGaming(()=>true)
                    &&held.Shell.OwnsDesktopShell&&held.Shell.NoUserExplorer&&Valid(held,clock())&&target.CheapCurrent
                    &&ReferenceEquals(active,held)&&!disposed;
                if(!Current()){
                    Abandon(held,"bootstrap-shell-or-user-changed");completion=Completion.Abandoned;return false;
                }
                // The hosted UWP may still need its shell after LC completes.
                // Transfer the exact target handle to this lease; 45 seconds is
                // admission only, never a deadline that can kill a live host.
                held.Target=target;retained=true;
                held.Expiry?.Dispose();held.Expiry=null;
                held.Expiry=schedule(250,()=>ObserveExit(held));
                completion=Completion.Hosted;report("bootstrap-hosted");return true;
            }
        });
        if(!modeEntered&&completion==Completion.Pending){
            // Busy/changed mode is cancellation, never a stop queued behind a
            // Desktop transition. A nonreleased LC may retry in the same mode.
            lock(gate){Abandon(held,"bootstrap-mode-unavailable");}return Completion.Abandoned;
        }
        return completion;
        }finally{if(!retained)target.Dispose();}
    }
    Completion StillPending(Lease held){lock(gate){
        if(ReferenceEquals(active,held)&&Valid(held,clock()))return Completion.Pending;
        Abandon(held,"bootstrap-expired");return Completion.Abandoned;
    }}
    bool Valid(Lease held,long now)=>now>=held.Started&&(held.Target is not null||now<held.Deadline)&&held.Shell.Current;
    void Expire(Lease held){lock(gate){if(ReferenceEquals(active,held)&&held.Target is null)Abandon(held,"bootstrap-timeout-abandoned");}}
    void ObserveExit(Lease held)
    {
        try{ObserveExitCore(held);}
        catch(Exception error)when(error is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.ComponentModel.Win32Exception){
            // Background cleanup must fail closed just like the HTTP admission
            // path. A provider failure never grants a process-stop operation.
            lock(gate)Abandon(held,"bootstrap-exit-check-unavailable");
        }
    }
    void ObserveExitCore(Lease held)
    {
        lock(gate){
            if(!ReferenceEquals(active,held)||disposed||held.Target is not {} target)return;
            held.Expiry?.Dispose();held.Expiry=null;
            if(!held.Shell.Current){Abandon(held,"bootstrap-host-shell-lost");return;}
            bool exited=target.GameExited;
            if(!ReferenceEquals(active,held)||disposed||!ReferenceEquals(held.Target,target))return;
            if(!exited){held.Expiry=schedule(250,()=>ObserveExit(held));return;}
            if(!held.ExitCleanupStarted){
                held.ExitCleanupStarted=true;held.ExitObservedAt=clock();
                held.ExitCleanupDeadline=checked(held.ExitObservedAt+2000);
            }
        }
        bool entered=false;
        withGaming(()=>{
            entered=true;
            lock(gate){
                if(!ReferenceEquals(active,held)||disposed||held.Target is not {} target)return false;
                bool Same()=>ReferenceEquals(active,held)&&!disposed&&ReferenceEquals(held.Target,target)
                    &&held.GamePid==target.GamePid&&held.GameBirth==target.GameBirth;
                bool Current()=>Same()&&clock()>=held.Started&&clock()>=held.ExitObservedAt&&clock()<held.ExitCleanupDeadline
                    &&withGaming(()=>true)&&held.Shell.OwnsDesktopShell&&held.Shell.NoUserExplorer
                    &&held.Shell.Current&&target.GameExited&&Same()&&withGaming(()=>true)&&Same()
                    &&clock()>=held.ExitObservedAt&&clock()<held.ExitCleanupDeadline;
                // Foreground/association/window predicates cannot be current
                // after exit. Re-use only the retained, signaled game handle;
                // LC still serializes its real terminal cleanup with this stop.
                if(!Current()){Abandon(held,"bootstrap-exit-cleanup-abandoned");return false;}
                bool stopped=false;
                var state=cleanupAfterExit(target,()=>stopped=Current()&&held.Shell.StopExact(Current));
                if(state==ExitCleanup.Pending&&Current()){
                    // Await only the authenticated cleanup still in flight. The
                    // first retained exit fixes this bound; retries never renew it.
                    long remaining=held.ExitCleanupDeadline-clock();
                    if(remaining<=0){Abandon(held,"bootstrap-exit-cleanup-abandoned");return false;}
                    held.Expiry=schedule((int)Math.Min(250,remaining),()=>ObserveExit(held));
                    return false;
                }
                stopped=stopped&&state==ExitCleanup.Ready;
                Abandon(held,stopped?"bootstrap-retired-after-exit":"bootstrap-exit-cleanup-abandoned");
                return stopped;
            }
        });
        if(!entered)lock(gate)Abandon(held,"bootstrap-exit-mode-unavailable");
    }
    void Abandon(Lease held,string reason){
        if(!ReferenceEquals(active,held))return;
        active=null;held.Expiry?.Dispose();held.Target?.Dispose();held.Shell.Dispose();report(reason);
    }
    internal void Invalidate(){lock(gate){if(active is {} held)Abandon(held,"bootstrap-canceled");}}
    public void Dispose(){lock(gate){disposed=true;if(active is {} held)Abandon(held,"bootstrap-disposed");}}
}
