// Playhub Steam Session. Distributed under GPL-3.0-only.
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal readonly record struct Win32InputProcessFacts(uint Pid, long Birth, bool Running, bool Exited)
{
    internal bool Same(uint pid,long birth) => pid!=0 && birth>0 && Pid==pid && Birth==birth;
}

// Borrows only the handle already verified by Win32SteamCompanion.Inspect.
// The borrow retains the handle through worker teardown; no PID reopen, package
// claim, environment change or game discovery occurs here.
internal sealed class RetainedWin32InputTarget : IRetainedSteamGameTarget, IDisposable
{
    private readonly SafeProcessHandle process;
    private readonly Func<SafeProcessHandle,Win32InputProcessFacts> read;
    private int disposed;
    private bool retained;
    public uint Pid { get; }
    public long Creation { get; }
    public string Aumid => string.Empty; // Win32 accepts only its actual PID-owned game window.
    public SafeProcessHandle ProcessHandle => process;

    internal RetainedWin32InputTarget(SafeProcessHandle process,uint pid,long birth,
        Func<SafeProcessHandle,Win32InputProcessFacts>? read = null)
    {
        if(pid==0 || birth<=0 || process.IsClosed || process.IsInvalid) throw new ArgumentException("Exact retained Win32 process required.");
        this.process=process;Pid=pid;Creation=birth;this.read=read??ReadNative;
        process.DangerousAddRef(ref retained);
    }
    private bool Available => Volatile.Read(ref disposed)==0 && !process.IsClosed && !process.IsInvalid;
    public bool IsCurrent()
    {
        if(!Available)return false;
        try { var facts=read(process);return Available && facts.Same(Pid,Creation) && facts.Running && !facts.Exited; }
        catch(ObjectDisposedException){return false;}
    }
    public bool HasExited
    {
        get
        {
            if(!Available)return false;
            try { var facts=read(process);return Available && facts.Same(Pid,Creation) && facts.Exited && !facts.Running; }
            catch(ObjectDisposedException){return false;}
        }
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        if(retained){retained=false;process.DangerousRelease();}
    }
    private static Win32InputProcessFacts ReadNative(SafeProcessHandle process)
    {
        uint first=WaitForSingleObject(process,0);
        if(first is not (0 or 258))return default;
        uint pid=GetProcessId(process);
        if(pid==0 || !GetProcessTimes(process,out long birth,out _,out _,out _))return default;
        uint last=WaitForSingleObject(process,0);
        return last==first?new(pid,birth,last==258,last==0):default;
    }
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process,uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(SafeProcessHandle process,out long birth,out long exit,out long kernel,out long user);
}
