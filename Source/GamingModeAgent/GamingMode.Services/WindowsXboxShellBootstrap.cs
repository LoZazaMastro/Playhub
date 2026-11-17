// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Playhub.Shared;
namespace GamingMode.Services;

internal static class WindowsXboxShellBootstrap
{
    static readonly string Explorer=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe");
    internal static XboxShellBootstrap.Caller? Caller(uint appId,uint wrapperPid,long wrapperBirth)
    {
        var pending=UwpSteamSessionAssociation.FindPendingForApp(appId)
            .Where(p=>p.WrapperPid==wrapperPid&&p.WrapperBirth==wrapperBirth).Take(2).ToArray();
        return pending is [var p]?new(appId,wrapperPid,wrapperBirth,p.Aumid,p.Executable):null;
    }
    internal static bool CallerCurrent(XboxShellBootstrap.Caller caller)=>Caller(caller.AppId,caller.WrapperPid,caller.WrapperBirth)==caller;
    internal static bool NoExplorer()=>N.GetShellWindow()==0&&ExplorerInventory(0);
    static bool ExplorerInventory(uint owned)
    {
        using var self=Process.GetCurrentProcess();
        var processes=Process.GetProcessesByName("explorer");
        try{
            int count=0;
            foreach(var process in processes){
                if(process.HasExited||process.SessionId!=self.SessionId)continue;
                if(owned==0||(uint)process.Id!=owned)return false;
                ++count;
            }
            return owned==0?count==0:count==1;
        }catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){return false;}
        finally{foreach(var process in processes)process.Dispose();}
    }
    internal static XboxShellBootstrap.IOwnedShell? Start()
    {
        // Must be checked again immediately before creating the one owned shell.
        if(!NoExplorer())return null;
        var process=Process.Start(new ProcessStartInfo(Explorer){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        if(process is null)return null;
        try{return new OwnedShell(process);}catch{process.Dispose();throw;}
    }
    sealed class OwnedShell : XboxShellBootstrap.IOwnedShell
    {
        readonly Process process;
        readonly SafeProcessHandle handle;
        bool disposed,stopAttempted;
        public XboxShellBootstrap.ShellIdentity Identity {get;}
        internal OwnedShell(Process process)
        {
            this.process=process;handle=process.SafeHandle;
            using var self=Process.GetCurrentProcess();
            Identity=new((uint)process.Id,process.StartTime.ToFileTimeUtc(),(uint)process.SessionId,Image(handle));
            if(Identity.Session!=(uint)self.SessionId||!Identity.Image.Equals(Explorer,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Created shell identity differs.");
        }
        public bool Current=>!disposed&&!stopAttempted&&!handle.IsClosed&&!handle.IsInvalid
            &&N.WaitForSingleObject(handle,0)==258&&N.GetProcessId(handle)==Identity.Pid
            &&N.GetProcessTimes(handle,out long birth,out _,out _,out _)&&birth==Identity.Birth
            &&N.ProcessIdToSessionId(Identity.Pid,out uint session)&&session==Identity.Session
            &&Image(handle).Equals(Identity.Image,StringComparison.OrdinalIgnoreCase);
        public bool OwnsDesktopShell=>Current&&N.GetShellWindow() is var shell&&shell!=0
            &&N.GetWindowThreadProcessId(shell,out uint pid)!=0&&pid==Identity.Pid;
        public bool NoUserExplorer
        {
            get{
                if(!Current||!ExplorerInventory(Identity.Pid))return false;
                bool safe=true;
                bool enumerated=N.EnumWindows((hwnd,_)=>{
                    N.GetWindowThreadProcessId(hwnd,out uint pid);
                    if(pid!=Identity.Pid)return true;
                    if(!SafeOwnedExplorerWindow(hwnd,Identity.Pid))safe=false;
                    return safe;
                },0);
                return enumerated&&safe&&Current&&ExplorerInventory(Identity.Pid);
            }
        }
        public bool StopExact(Func<bool> finalCurrent)
        {
            if(!Current||!OwnsDesktopShell||!NoUserExplorer||!Current||!finalCurrent())return false;
            stopAttempted=true;
            // This handle belongs to Process.Start above. No PID re-open, tree,
            // global Explorer termination, replacement pursuit, or policy write.
            return N.TerminateProcess(handle,0)&&N.WaitForSingleObject(handle,100)==0
                &&(N.GetShellWindow()==0||N.GetWindowThreadProcessId(N.GetShellWindow(),out uint pid)!=0&&pid!=Identity.Pid);
        }
        public void Dispose(){if(disposed)return;disposed=true;process.Dispose();}
    }
    static bool SafeOwnedExplorerWindow(nint hwnd,uint ownedPid)
    {
        uint thread=N.GetWindowThreadProcessId(hwnd,out uint pid);
        if(ownedPid==0||thread==0||pid!=ownedPid)return false;
        string name=Class(hwnd);
        if(name.Length==0||name is "CabinetWClass" or "ExploreWClass")return false;
        bool shell=name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
        bool listener=false;
        if(name=="DummyDWMListenerWindow")
        {
            if(!N.GetWindowRect(hwnd,out var rect)||rect.Left!=rect.Right||rect.Top!=rect.Bottom)return false;
            long style=IntPtr.Size==8?N.GetWindowLongPtr(hwnd,-20).ToInt64():N.GetWindowLong(hwnd,-20);
            const long required=0x08000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            if((style&required)!=required)return false;
            listener=true;
        }
        if(name=="EdgeUiInputTopWndClass")
        {
            // Shell edge-input infrastructure, never a generic tool-window rule.
            // Unknown shapes remain a veto even when this private shell is owned.
            if(!N.GetWindowRect(hwnd,out var rect))return false;
            nint monitor=N.MonitorFromWindow(hwnd,0);
            var info=new N.MONITORINFO{Size=(uint)Marshal.SizeOf<N.MONITORINFO>()};
            if(monitor==0||!N.GetMonitorInfo(monitor,ref info))return false;
            long height=(long)rect.Bottom-rect.Top,width=(long)rect.Right-rect.Left;
            long monitorWidth=(long)info.Monitor.Right-info.Monitor.Left;
            if(height<1||height>4||monitorWidth<=0||info.Monitor.Bottom<=info.Monitor.Top
                ||rect.Top!=info.Monitor.Top||rect.Right!=info.Monitor.Right
                ||rect.Left<info.Monitor.Left||rect.Bottom>info.Monitor.Bottom||width<=0||width<monitorWidth/2)return false;
            long style=IntPtr.Size==8?N.GetWindowLongPtr(hwnd,-20).ToInt64():N.GetWindowLong(hwnd,-20);
            const long required=0x08200080; // NOACTIVATE | NOREDIRECTIONBITMAP | TOOLWINDOW
            if((style&required)!=required||(style&0x00040000)!=0)return false; // no APPWINDOW
            // Re-read all classification facts after the monitor/style providers.
            var finalInfo=new N.MONITORINFO{Size=(uint)Marshal.SizeOf<N.MONITORINFO>()};
            if(N.MonitorFromWindow(hwnd,0)!=monitor||!N.GetMonitorInfo(monitor,ref finalInfo)
                ||finalInfo.Monitor.Left!=info.Monitor.Left||finalInfo.Monitor.Top!=info.Monitor.Top
                ||finalInfo.Monitor.Right!=info.Monitor.Right||finalInfo.Monitor.Bottom!=info.Monitor.Bottom
                ||!N.GetWindowRect(hwnd,out var finalRect)
                ||finalRect.Left!=rect.Left||finalRect.Top!=rect.Top||finalRect.Right!=rect.Right||finalRect.Bottom!=rect.Bottom
                ||(IntPtr.Size==8?N.GetWindowLongPtr(hwnd,-20).ToInt64():N.GetWindowLong(hwnd,-20))!=style)return false;
            listener=true;
        }
        // Recheck exact class/PID/thread after the Win32 geometry/style reads.
        return (!N.IsWindowVisible(hwnd)||shell||listener)&&Class(hwnd)==name
            &&N.GetWindowThreadProcessId(hwnd,out uint finalPid)==thread&&finalPid==ownedPid;
    }
    internal static async Task<XboxShellBootstrap.ITarget?> ResolveAsync(XboxShellBootstrap.Caller caller,uint gamePid,
        LivePerformanceSourceResolver resolver,CancellationToken cancellation)
    {
        ILivePerformanceSource? source=null;
        Target? result=null;
        try{
            var identity=UwpSteamSessionAssociation.Find(gamePid,caller.AppId);
            if(identity is null||!Matches(caller,identity)||!UwpSteamSessionAssociation.IsOnlyRunningInstance(identity))return null;
            source=await resolver.ResolveAsync(caller.AppId,cancellation).ConfigureAwait(false);
            if(source is null||!source.Current||source.Target.ProcessId!=gamePid||source.Target.CreationFileTimeUtc!=identity.GameBirth
                ||!source.TryGetBounds(out _)||source.ForegroundProof is not {} frame)return null;
            result=new(caller,identity,source,frame);source=null;
            if(!result.Current){result.Dispose();return null;}
            var accepted=result;result=null;return accepted;
        }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {return null;}
        finally{source?.Dispose();result?.Dispose();}
    }
    static bool Matches(XboxShellBootstrap.Caller caller,UwpSessionIdentity identity)=>identity.WrapperPid==caller.WrapperPid
        &&identity.WrapperBirth==caller.WrapperBirth&&identity.Aumid.Equals(caller.Aumid,StringComparison.OrdinalIgnoreCase)
        &&Path.GetFileName(identity.Executable).Equals(Path.GetFileName(caller.Executable),StringComparison.OrdinalIgnoreCase);
    sealed class Target : XboxShellBootstrap.ITarget
    {
        readonly UwpSessionIdentity identity;
        readonly ILivePerformanceSource source;
        readonly LivePerformanceHudForegroundProof frame;
        readonly OverlayWindowTools.ProcessLease game,wrapper,host;
        readonly uint thread;
        readonly string windowClass;
        bool disposed;
        public XboxShellBootstrap.Caller Caller {get;}
        public uint GamePid=>identity.GamePid;
        public long GameBirth=>identity.GameBirth;
        // ProcessLease.Exited is WaitForSingleObject(retainedHandle, 0) == 0.
        // Suspension, missing AFH/CoreWindow, and failed/unknown waits are false.
        public bool GameExited=>!disposed&&game.CreatedAt==identity.GameBirth&&game.Exited;
        internal Target(XboxShellBootstrap.Caller caller,UwpSessionIdentity identity,ILivePerformanceSource source,LivePerformanceHudForegroundProof frame)
        {
            (Caller,this.identity,this.source,this.frame)=(caller,identity,source,frame);
            OverlayWindowTools.ProcessLease? g=null,w=null,h=null;
            try{
                g=OverlayWindowTools.ProcessLease.TryOpen((int)identity.GamePid)??throw new InvalidOperationException();
                w=OverlayWindowTools.ProcessLease.TryOpen((int)identity.WrapperPid)??throw new InvalidOperationException();
                h=OverlayWindowTools.ProcessLease.TryOpen(frame.ProcessId)??throw new InvalidOperationException();
                thread=N.GetWindowThreadProcessId(frame.WindowHandle,out _);windowClass=Class(frame.WindowHandle);
                (game,wrapper,host)=(g,w,h);
            }catch{h?.Dispose();w?.Dispose();g?.Dispose();throw;}
        }
        public bool CheapCurrent=>!disposed&&game.Alive&&game.CreatedAt==identity.GameBirth&&wrapper.Alive&&wrapper.CreatedAt==identity.WrapperBirth
            &&host.Alive&&host.CreatedAt==frame.ProcessBirth&&thread!=0&&windowClass.Length>0
            &&N.GetForegroundWindow()==frame.WindowHandle&&N.GetWindowThreadProcessId(frame.WindowHandle,out uint owner)==thread&&owner==frame.ProcessId
            &&N.GetAncestor(frame.WindowHandle,2)==frame.WindowHandle&&Class(frame.WindowHandle)==windowClass
            &&OverlayWindowTools.IsPresentGameSurface(frame.WindowHandle)&&N.GetForegroundWindow()==frame.WindowHandle;
        public bool Current
        {
            get{
                if(!CheapCurrent||!source.Current||source.ForegroundProof!=frame)return false;
                if(UwpSteamSessionAssociation.Find(GamePid,Caller.AppId)!=identity||!UwpSteamSessionAssociation.IsOnlyRunningInstance(identity))return false;
                if(frame.ProcessId!=GamePid&&!UwpFrameWindow.Matches(frame.WindowHandle,identity.Aumid))return false;
                // PKEY/association calls can be slow; final barrier is local OS
                // handle/window identity and actual foreground, never new time.
                return CheapCurrent;
            }
        }
        public void Dispose(){if(disposed)return;disposed=true;host.Dispose();wrapper.Dispose();game.Dispose();source.Dispose();}
    }
    static string Image(SafeProcessHandle handle){var text=new StringBuilder(32768);uint size=(uint)text.Capacity;
        return N.QueryFullProcessImageName(handle,0,text,ref size)?text.ToString():"";}
    static string Class(nint hwnd){var text=new StringBuilder(256);return N.GetClassName(hwnd,text,text.Capacity)>0?text.ToString():"";}
    static class N
    {
        internal delegate bool EnumProc(nint hwnd,nint value);
        [DllImport("user32.dll")]internal static extern bool EnumWindows(EnumProc callback,nint value);
        [DllImport("user32.dll")]internal static extern nint GetShellWindow();
        [DllImport("user32.dll")]internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
        [DllImport("user32.dll")]internal static extern nint GetAncestor(nint hwnd,uint flags);
        [DllImport("user32.dll")]internal static extern bool IsWindowVisible(nint hwnd);
        [StructLayout(LayoutKind.Sequential)]internal struct RECT{internal int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]internal static extern bool GetWindowRect(nint hwnd,out RECT rect);
        [StructLayout(LayoutKind.Sequential)]internal struct MONITORINFO{internal uint Size;internal RECT Monitor,Work;internal uint Flags;}
        [DllImport("user32.dll")]internal static extern nint MonitorFromWindow(nint hwnd,uint flags);
        [DllImport("user32.dll",EntryPoint="GetMonitorInfoW")]internal static extern bool GetMonitorInfo(nint monitor,ref MONITORINFO info);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]internal static extern nint GetWindowLongPtr(nint hwnd,int index);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)]internal static extern int GetWindowLong(nint hwnd,int index);
        [DllImport("user32.dll",EntryPoint="GetClassNameW",CharSet=CharSet.Unicode)]internal static extern int GetClassName(nint hwnd,StringBuilder value,int count);
        [DllImport("kernel32.dll")]internal static extern uint WaitForSingleObject(SafeProcessHandle handle,uint timeout);
        [DllImport("kernel32.dll")]internal static extern uint GetProcessId(SafeProcessHandle handle);
        [DllImport("kernel32.dll")]internal static extern bool ProcessIdToSessionId(uint pid,out uint session);
        [DllImport("kernel32.dll")]internal static extern bool GetProcessTimes(SafeProcessHandle handle,out long birth,out long exit,out long kernel,out long user);
        [DllImport("kernel32.dll")]internal static extern bool TerminateProcess(SafeProcessHandle handle,uint code);
        [DllImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode)]internal static extern bool QueryFullProcessImageName(SafeProcessHandle handle,uint flags,StringBuilder value,ref uint length);
    }
}
