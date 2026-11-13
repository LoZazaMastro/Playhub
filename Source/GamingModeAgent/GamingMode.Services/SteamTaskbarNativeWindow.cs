// GPL-3.0-only. Retains only the verified Steam main and Explorer identities.
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Playhub.DeckyHost;
namespace GamingMode.Services;
internal sealed class SteamTaskbarNativeWindow:IDisposable
{
    internal readonly SteamTaskbarKey Key;private readonly Process steam=null!,shell=null!,client=null!;private readonly string root,sid,desktop;
    private readonly int session;private readonly FileStream pin=null!;internal bool WindowRetired,ShellRetired;
    private SteamTaskbarNativeWindow(QamWindowIdentity captured,string steamRoot)
    {
        root=steamRoot;
        try
        {
        steam=Process.GetProcessById((int)captured.OwnerPid);_ =steam.SafeHandle;
        uint thread=GetWindowThreadProcessId(captured.Handle,out uint pid);
        nint shellWindow=GetShellWindow();uint shellThread=GetWindowThreadProcessId(shellWindow,out uint shellPid);
        shell=Process.GetProcessById((int)shellPid);_=shell.SafeHandle;
        using var self=Process.GetCurrentProcess();using var user=WindowsIdentity.GetCurrent();session=self.SessionId;sid=user.User?.Value??"";
        string steamImage=steam.MainModule?.FileName??"";
        pin=new FileStream(steamImage,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        if(thread==0||pid!=captured.OwnerPid||steam.StartTime.ToFileTimeUtc()!=captured.OwnerBirth||steam.HasExited
            ||!AllowedSteamImage(steamImage)||!ValveImageTrust.Verify(steamImage)||!SameUser(steam)||!SameUser(shell)
            ||steam.SessionId!=session||shell.SessionId!=session||shellThread==0||Class(shellWindow)is not("Progman" or "WorkerW")
            ||!string.Equals(shell.MainModule?.FileName,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Steam/shell taskbar scope unavailable.");
        var c=ClientFor(steam.Id,steam.StartTime.ToFileTimeUtc())??throw new InvalidOperationException("Steam client lineage unavailable.");
        client=Process.GetProcessById(c.Pid);_=client.SafeHandle;
        if(client.StartTime.ToFileTimeUtc()!=c.Birth||client.HasExited)throw new InvalidOperationException("Steam client changed.");
        desktop=DesktopName(GetThreadDesktop(thread));
        if(desktop.Length==0||desktop!=DesktopName(GetThreadDesktop(shellThread)))throw new InvalidOperationException("Steam/shell desktop differs.");
        Key=new(captured.Handle,pid,captured.OwnerBirth,thread,shellWindow,shellPid,shell.StartTime.ToFileTimeUtc());
        }
        catch {pin?.Dispose();client?.Dispose();shell?.Dispose();steam?.Dispose();throw;}
    }
    internal static SteamTaskbarNativeWindow? Capture(string? configuredPath)
    {
        try
        {
            if(SteamInstallationPath.FindRoot(configuredPath)is not {} root||WindowsQamFocusAdapter.CaptureSteam(root)is not {} window
                ||GetForegroundWindow()!=window.Handle)return null;
            return new(window,root);
        }
        catch(Exception e)when(e is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){return null;}
    }
    private bool AllowedSteamImage(string p)=>string.Equals(p,Path.Combine(root,"steam.exe"),StringComparison.OrdinalIgnoreCase)
        ||string.Equals(p,Path.Combine(root,"bin","cef","cef.win64","steamwebhelper.exe"),StringComparison.OrdinalIgnoreCase);
    private bool SameUser(Process p){if(!OpenProcessToken(p.SafeHandle,8,out var token))return false;using(token)using(var identity=new WindowsIdentity(token.DangerousGetHandle()))return sid.Length>0&&identity.User?.Value==sid;}
    private (int Pid,long Birth)? ClientFor(int pid,long birth)
    {
        for(int depth=0;depth<16;depth++)
        {
            var row=DeckyProcessTree.Read(pid);if(row is null||row.Birth!=birth||row.Session!=session||row.Sid!=sid)return null;
            if(string.Equals(row.Path,Path.Combine(root,"steam.exe"),StringComparison.OrdinalIgnoreCase))return ValveImageTrust.Verify(row.Path)?(row.Pid,row.Birth):null;
            if(row.ParentPid<=0||row.ParentPid==pid||DeckyProcessTree.Read(row.ParentPid)is not {} parent||parent.Birth>birth)return null;
            pid=parent.Pid;birth=parent.Birth;
        }
        return null;
    }
    internal long? VerifyReaderOwner(int pid)
    {
        try
        {
            using var process=Process.GetProcessById(pid);long birth=process.StartTime.ToFileTimeUtc();
            if(process.HasExited||client.HasExited||process.SessionId!=session||!SameUser(process)||!AllowedSteamImage(process.MainModule?.FileName??""))return null;
            return ClientFor(pid,birth)==(client.Id,client.StartTime.ToFileTimeUtc())&&ValveImageTrust.Verify(process.MainModule!.FileName)?birth:null;
        }
        catch(Exception e)when(e is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception){return null;}
    }
    internal SteamTaskbarObservation Read(long epoch,bool allowed,SteamTaskbarMainProof? proof)
    {
        bool exact=false,ended=false,parentGone=false,visible=false,cloaked=true,full=false,edge=false,fg=false,viewport=false;
        try
        {
            ended=WindowRetired||ShellRetired||steam.HasExited||shell.HasExited||!IsWindow(Key.Window)||!IsWindow(Key.Shell);
            parentGone=client.HasExited;
            uint ownerThread=GetWindowThreadProcessId(Key.Window,out uint pid);
            nint currentShell=GetShellWindow();
            uint shellThread=GetWindowThreadProcessId(Key.Shell,out uint spid);
            ended|=(ownerThread!=0&&(ownerThread!=Key.Thread||pid!=Key.Pid))
                ||(currentShell!=0&&currentShell!=Key.Shell)||(shellThread!=0&&spid!=Key.ShellPid);
            exact=!ended&&steam.StartTime.ToFileTimeUtc()==Key.Birth&&shell.StartTime.ToFileTimeUtc()==Key.ShellBirth
                &&ownerThread==Key.Thread&&pid==Key.Pid&&Class(Key.Window)=="SDL_app"
                &&GetAncestor(Key.Window,2)==Key.Window&&GetWindow(Key.Window,4)==0&&currentShell==Key.Shell
                &&shellThread!=0&&spid==Key.ShellPid
                &&(Class(Key.Shell)is "Progman" or "WorkerW")&&DesktopName(GetThreadDesktop(Key.Thread))==desktop;
            visible=IsWindowVisible(Key.Window)&&!IsIconic(Key.Window);
            cloaked=DwmInt(Key.Window,14,out int cloak,4)!=0||cloak!=0;
            var monitor=new Monitor{Size=Marshal.SizeOf<Monitor>()};
            Rect d=default;
            bool bounds=GetWindowRect(Key.Window,out var w)&&GetMonitorInfoW(MonitorFromWindow(Key.Window,0),ref monitor)
                &&DwmRect(Key.Window,9,out d,Marshal.SizeOf<Rect>())==0;
            if(bounds)
            {
                viewport=proof?.MatchesBounds(w.Box)==true;
                long style=GetWindowLongPtrW(Key.Window,-16).ToInt64();
                full=SteamTaskbarGeometry.Covers(w.Box,monitor.Bounds.Box,d.Box,style);
                var bar=new Appbar{Size=Marshal.SizeOf<Appbar>(),Edge=3,Bounds=monitor.Bounds};
                nint bottom=(nint)SHAppBarMessage(11,ref bar); // ABM_GETAUTOHIDEBAREX, read only.
                GetWindowThreadProcessId(bottom,out uint barPid);
                bool autoHideBottom=bottom!=0&&barPid==Key.ShellPid&&(Class(bottom)is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
                edge=SteamTaskbarGeometry.BottomPixel(w.Box,monitor.Bounds.Box,d.Box,style,autoHideBottom);
            }
            var gui=new Gui{Size=Marshal.SizeOf<Gui>()};
            bool clean=GetGUIThreadInfo(Key.Thread,ref gui)&&(gui.Flags&0x1e)==0&&gui.Capture==0&&gui.MenuOwner==0&&gui.MoveSize==0;
            fg=GetForegroundWindow()==Key.Window&&gui.Active==Key.Window&&clean&&InputDesktopCurrent();
        }
        catch(Exception e)when(e is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception){exact=false;}
        bool main=proof is {} p&&p.MainBigPicture&&viewport&&p.MatchesOwner(Key.Pid)&&p.Epoch==epoch;
        // All slow reads precede the final retained-window/foreground boundary.
        exact=exact&&!WindowRetired&&!ShellRetired
            &&GetWindowThreadProcessId(Key.Window,out uint finalPid)==Key.Thread&&finalPid==Key.Pid
            &&GetShellWindow()==Key.Shell&&GetWindowThreadProcessId(Key.Shell,out uint finalShellPid)!=0&&finalShellPid==Key.ShellPid;
        fg=fg&&exact&&GetForegroundWindow()==Key.Window;
        return new(Key,epoch,exact,allowed&&!parentGone,main,fg,visible,cloaked,full,edge,
            main&&Environment.TickCount64-proof!.ObservedAt is >=0 and <=750,ended);
    }
    private bool InputDesktopCurrent(){nint input=OpenInputDesktop(0,false,1);if(input==0)return false;try{return DesktopName(input)==desktop;}finally{CloseDesktop(input);}}
    internal static string Class(nint w){var s=new StringBuilder(128);return GetClassNameW(w,s,128)>0?s.ToString():"";}
    private static string DesktopName(nint h){var s=new StringBuilder(128);return h!=0&&GetUserObjectInformationW(h,2,s,256,out _)?s.ToString():"";}
    public void Dispose(){pin.Dispose();client.Dispose();shell.Dispose();steam.Dispose();}
    [StructLayout(LayoutKind.Sequential)]private struct Rect{internal int Left,Top,Right,Bottom;internal SteamTaskbarBounds Box=>new(Left,Top,Right,Bottom);}
    [StructLayout(LayoutKind.Sequential)]private struct Monitor{internal int Size;internal Rect Bounds,Work;internal uint Flags;}
    [StructLayout(LayoutKind.Sequential)]private struct Appbar{internal int Size;internal nint Window;internal uint Callback,Edge;internal Rect Bounds;internal nint Param;}
    [StructLayout(LayoutKind.Sequential)]private struct Gui{internal int Size;internal uint Flags;internal nint Active,Focus,Capture,MenuOwner,MoveSize,Caret;internal Rect CaretRect;}
    [DllImport("user32.dll")]internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern nint GetShellWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint h,out uint p);
    [DllImport("user32.dll")]private static extern nint GetAncestor(nint h,uint flags);
    [DllImport("user32.dll")]private static extern nint GetWindow(nint h,uint relation);
    [DllImport("user32.dll")]private static extern nint GetWindowLongPtrW(nint h,int index);
    [DllImport("user32.dll")]private static extern bool IsWindow(nint h);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")]private static extern bool IsIconic(nint h);
    [DllImport("user32.dll")]private static extern bool GetWindowRect(nint h,out Rect r);
    [DllImport("user32.dll")]private static extern nint MonitorFromWindow(nint h,uint flags);
    [DllImport("user32.dll")]private static extern bool GetMonitorInfoW(nint h,ref Monitor m);
    [DllImport("user32.dll")]private static extern bool GetGUIThreadInfo(uint t,ref Gui g);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassNameW(nint h,StringBuilder s,int n);
    [DllImport("user32.dll")]private static extern nint GetThreadDesktop(uint t);
    [DllImport("user32.dll")]private static extern nint OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")]private static extern bool CloseDesktop(nint d);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetUserObjectInformationW(nint h,int i,StringBuilder s,int bytes,out int needed);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]private static extern int DwmRect(nint h,int a,out Rect r,int bytes);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]private static extern int DwmInt(nint h,int a,out int v,int bytes);
    [DllImport("shell32.dll")]private static extern nuint SHAppBarMessage(uint message,ref Appbar data);
    [DllImport("advapi32.dll")]private static extern bool OpenProcessToken(SafeProcessHandle p,uint access,out SafeAccessTokenHandle token);
}
