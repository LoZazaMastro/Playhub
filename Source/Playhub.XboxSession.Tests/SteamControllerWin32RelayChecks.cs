// Playhub Steam Session. Distributed under GPL-3.0-only.
using Microsoft.Win32.SafeHandles;
internal static class SteamControllerWin32RelayChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const uint pid=37; const long born=134389429512941331;
        using var handle=new SafeProcessHandle((nint)123,false);int reads=0;
        Win32InputProcessFacts facts=new(pid,born,true,false);
        using(var target=new RetainedWin32InputTarget(handle,pid,born,_=>{reads++;return facts;}))
        {
            check(ReferenceEquals(target.ProcessHandle,handle)&&target.Pid==pid&&target.Creation==born,"Win32 input borrows the exact verified retained handle and birth64");
            check(target.Aumid.Length==0&&!SteamControllerUwpFrameWindow.Matches((nint)123,target.Aumid),"Win32 has no invented package/AUMID or accepted foreign frame");
            check(target.IsCurrent()&&!target.HasExited&&reads==2,"running exact target permits currentness, not exit");
            facts=facts with{Birth=born+1};check(!target.IsCurrent()&&!target.HasExited,"PID reuse with adjacent birth64 never grants current or exit");
            facts=new(pid+1,born,true,false);check(!target.IsCurrent()&&!target.HasExited,"replacement PID is not original process");
            facts=default;check(!target.IsCurrent()&&!target.HasExited,"unavailable process facts are unknown, not exit");
            facts=new(pid,born,true,true);check(!target.IsCurrent()&&!target.HasExited,"contradictory running/exited facts never grant authority");
            facts=new(pid,born,false,true);check(!target.IsCurrent()&&target.HasExited,"exact positively observed process exit is distinct from unknown");
            facts=new(pid,born,true,false);check(target.IsCurrent(),"process facts remain exact after a harmless unavailable sample");
            target.Dispose();int last=reads;check(!target.IsCurrent()&&!target.HasExited&&reads==last,"disposed input lease never performs a later process read");
            target.Dispose();check(!handle.IsClosed,"retiring input borrow does not close caller-owned process handle");
        }
        RetainedWin32InputTarget? current=null;
        using(var h=new SafeProcessHandle((nint)124,false))
        {
            current=new(h,pid,born,_=>{current!.Dispose();return new(pid,born,true,false);});
            check(!current.IsCurrent(),"reentrant dispose during slow current getter prevents stale current result");current.Dispose();
        }
        using(var h=new SafeProcessHandle((nint)125,false))
        {
            current=new(h,pid,born,_=>{current!.Dispose();return new(pid,born,false,true);});
            check(!current.HasExited,"reentrant dispose during exit getter cannot invent final exit receipt");current.Dispose();
        }
        using(var h=new SafeProcessHandle((nint)126,false))
        using(var target=new RetainedWin32InputTarget(h,pid,born,_=>throw new ObjectDisposedException("fake retained process")))
            check(!target.IsCurrent()&&!target.HasExited,"concurrent handle retirement fails closed without PID reopen");
        using(var h=new SafeProcessHandle((nint)127,false))
        {
            bool denied=false;try{using var invalid=new RetainedWin32InputTarget(h,0,born,_=>default);}catch(ArgumentException){denied=true;}
            check(denied,"zero PID never creates input target");
            denied=false;try{using var invalid=new RetainedWin32InputTarget(h,pid,0,_=>default);}catch(ArgumentException){denied=true;}
            check(denied,"missing birth never creates input target");
        }
        using(var h=new SafeProcessHandle(0,false))
        {
            bool denied=false;try{using var invalid=new RetainedWin32InputTarget(h,pid,born,_=>default);}catch(ArgumentException){denied=true;}
            check(denied,"invalid handle is not reopened from PID");
        }
    }
}
