// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Xbox's profile chooser is a Microsoft Gaming Services child of the exact
// already retained game. It shares that game's input lifetime, never its PID.
internal sealed class SteamXboxProfileWindow(IRetainedSteamGameTarget target,Action<string>? report=null) : IDisposable
{
    internal readonly record struct Identity(uint Pid,long Birth,uint Parent,uint Session,string Family,string Image,string PackageRoot);
    internal static bool Accepts(Identity ui,uint game,long birth,uint session)
    {
        if(ui.Pid==0||ui.Pid==game||game==0||birth<=0||ui.Birth<birth||ui.Parent!=game||ui.Session!=session
            ||ui.Family!="Microsoft.GamingServices_8wekyb3d8bbwe"||!Path.IsPathFullyQualified(ui.PackageRoot))return false;
        return string.Equals(Path.GetFullPath(ui.Image),Path.Combine(Path.GetFullPath(ui.PackageRoot),"GamingServicesTcui","GamingServicesTcui.exe"),StringComparison.OrdinalIgnoreCase);
    }
    SafeProcessHandle? process;
    FileStream? imagePin;
    uint pid;
    long birth;
    bool disposed;
    public bool Matches(nint window,bool requireForeground=true)
    {
        try
        {
            if(disposed||window==0||!target.IsCurrent()||(requireForeground&&GetForegroundWindow()!=window)
                ||GetAncestor(window,2)!=window||!IsWindowVisible(window)||!IsWindowEnabled(window))return false;
            uint thread=GetWindowThreadProcessId(window,out uint owner);
            var name=new StringBuilder(256);
            if(thread==0||owner==0||GetClassName(window,name,256)==0||!name.ToString().StartsWith("HwndWrapper[GamingServicesTcui;",StringComparison.Ordinal))return false;
            if(pid!=owner||!Current())
            {
                Clear();
                process=OpenProcess(0x101000,false,owner);
                if(process.IsInvalid||!GetProcessTimes(process,out long created,out _,out _,out _)
                    ||NtQueryInformationProcess(process,0,out var basic,Marshal.SizeOf<Basic>(),out _)!=0
                    ||!ProcessIdToSessionId(owner,out uint session))return Refuse();
                var image=new StringBuilder(32768);uint size=(uint)image.Capacity;
                if(!QueryFullProcessImageName(process,0,image,ref size))return Refuse();
                string family=AppValue(process,GetPackageFamilyName),fullName=AppValue(process,GetPackageFullName);
                if(fullName.Length==0)return Refuse();
                uint length=0;if(GetPackagePathByFullName(fullName,ref length,null)!=122||length is <2 or >32768)return Refuse();
                var root=new StringBuilder((int)length);if(GetPackagePathByFullName(fullName,ref length,root)!=0)return Refuse();
                var identity=new Identity(owner,created,(uint)(nuint)basic.Parent,session,family,image.ToString(),root.ToString());
                if(!Accepts(identity,target.Pid,target.Creation,(uint)System.Diagnostics.Process.GetCurrentProcess().SessionId))return Refuse();
                imagePin=new FileStream(identity.Image,FileMode.Open,FileAccess.Read,FileShare.Read);
                pid=owner;birth=created;
                if(!Current()||!target.IsCurrent())return Refuse();
                report?.Invoke($"Xbox profile input scope verified: ui={pid}; game={target.Pid}; same-game-session=True.");
            }
            return Current()&&target.IsCurrent()&&(!requireForeground||GetForegroundWindow()==window)
                &&GetWindowThreadProcessId(window,out uint confirmed)==thread&&confirmed==owner&&owner==pid;
        }
        catch { return Refuse(); }
    }
    bool Current()=>process is {IsInvalid:false,IsClosed:false}&&WaitForSingleObject(process,0)==258
        &&GetProcessTimes(process,out long actual,out _,out _,out _)&&actual==birth;
    bool Refuse(){Clear();return false;}
    void Clear(){imagePin?.Dispose();imagePin=null;process?.Dispose();process=null;pid=0;birth=0;}
    public void Dispose(){disposed=true;Clear();}
    internal static uint ReadParent(uint owner)
    {
        using var handle=OpenProcess(0x1000,false,owner);
        return !handle.IsInvalid&&NtQueryInformationProcess(handle,0,out var data,Marshal.SizeOf<Basic>(),out _)==0?(uint)(nuint)data.Parent:0;
    }
    delegate int AppQuery(SafeProcessHandle process,ref uint length,StringBuilder? value);
    static string AppValue(SafeProcessHandle process,AppQuery query){uint n=0;if(query(process,ref n,null)!=122||n is <2 or >32768)return "";var text=new StringBuilder((int)n);return query(process,ref n,text)==0?text.ToString():"";}
    [StructLayout(LayoutKind.Sequential)]struct Basic{internal nint Reserved1,Peb,Reserved2,Reserved3,Pid,Parent;}
    [DllImport("ntdll.dll")]static extern int NtQueryInformationProcess(SafeProcessHandle process,int kind,out Basic data,int size,out int written);
    [DllImport("kernel32.dll")]static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(SafeProcessHandle process,uint delay);
    [DllImport("kernel32.dll")]static extern bool GetProcessTimes(SafeProcessHandle process,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll")]static extern bool ProcessIdToSessionId(uint pid,out uint session);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="QueryFullProcessImageNameW")]static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder text,ref uint size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int GetPackageFamilyName(SafeProcessHandle process,ref uint size,StringBuilder? text);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int GetPackageFullName(SafeProcessHandle process,ref uint size,StringBuilder? text);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int GetPackagePathByFullName(string package,ref uint size,StringBuilder? text);
    [DllImport("user32.dll")]static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]static extern nint GetAncestor(nint window,uint flag);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetClassNameW")]static extern int GetClassName(nint window,StringBuilder value,int size);
}
