using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GamingMode.Services;

// Gaming Services' profile chooser is a temporary layer above its caller.
// This observer is independent of input hardware, store and desktop/gaming mode.
internal sealed class XboxProfileForegroundService : IDisposable
{
    readonly CancellationTokenSource stop=new();
    readonly Task observer;
    readonly Action<string> log;
    readonly int session;
    readonly Dictionary<(int Pid,long Birth,nint Window),Scope> scopes=new();
    internal XboxProfileForegroundService(Action<string> report){log=report;using var self=Process.GetCurrentProcess();session=self.SessionId;observer=Task.Run(Observe);}
    async Task Observe()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                foreach(var process in Process.GetProcessesByName("GamingServicesTcui"))
                using(process)
                try
                {
                    if(process.HasExited||process.SessionId!=session)continue;
                    nint window=ProfileWindow((uint)process.Id);
                    if(window==0)continue;
                    var key=(process.Id,process.StartTime.ToFileTimeUtc(),window);
                    if(scopes.ContainsKey(key)||scopes.Count>=8)continue;
                    uint parent=SteamXboxProfileWindow.ReadParent((uint)process.Id);
                    var target=Parent.Open(parent);
                    if(target is null)continue;
                    var proof=new SteamXboxProfileWindow(target);
                    if(!proof.Matches(window,requireForeground:false)){proof.Dispose();target.Dispose();continue;}
                    var scope=new Scope(target,proof,window);scopes.Add(key,scope);
                    // Consume once: Alt-Tab after this presentation is respected.
                    bool focused=WindowsQamFocusAdapter.ActivateWindow(window,()=>!stop.IsCancellationRequested&&proof.Matches(window,requireForeground:false));
                    scope.WasForeground=GetForegroundWindow()==window;
                    log($"Xbox profile presented: ui={process.Id}; parent={parent}; foreground={focused}.");
                }
                catch(Exception error)when(error is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or IOException){ }
                foreach(var entry in scopes.ToArray())
                {
                    var scope=entry.Value;
                    if(scope.Proof.Matches(scope.Window,requireForeground:false))
                    {scope.WasForeground=GetForegroundWindow()==scope.Window;continue;}
                    scopes.Remove(entry.Key);
                    try
                    {
                        // Restore only this exact live caller after its modal UI
                        // closes. No activation if the user switched to another app.
                        nint foreground=GetForegroundWindow();
                        if(scope.WasForeground&&scope.Target.IsCurrent()&&CanReturn(foreground,scope))
                        {
                            nint game=scope.Target.GameWindow();
                            if(game!=0)
                            {
                                bool focused=WindowsQamFocusAdapter.ActivateWindow(game,()=>!stop.IsCancellationRequested&&scope.Target.IsCurrent()&&scope.Target.GameWindow()==game);
                                log($"Xbox profile closed: parent={scope.Target.Pid}; game-foreground={focused}.");
                            }
                        }
                    }
                    finally{scope.Dispose();}
                }
                await Task.Delay(150,stop.Token).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){ }
        finally{foreach(var scope in scopes.Values)scope.Dispose();scopes.Clear();}
    }
    static bool CanReturn(nint foreground,Scope scope)
    {
        if(foreground==0||foreground==scope.Window||foreground==scope.Target.GameWindow())return true;
        GetWindowThreadProcessId(foreground,out uint owner);
        if(owner==scope.Target.Pid)return true;
        try{using var process=Process.GetProcessById((int)owner);return process.ProcessName.Equals("steamwebhelper",StringComparison.OrdinalIgnoreCase)
            &&string.Equals(process.MainModule?.FileName,Path.Combine(SteamInstallationPath.FindRoot()??"",@"bin\cef\cef.win64\steamwebhelper.exe"),StringComparison.OrdinalIgnoreCase);}
        catch{return false;}
    }
    static nint ProfileWindow(uint process)
    {
        nint found=0;
        EnumWindows((window,_)=>{if(!IsWindowVisible(window)||GetWindowThreadProcessId(window,out uint owner)==0||owner!=process)return true;
            var name=new StringBuilder(256);if(GetClassName(window,name,256)==0||!name.ToString().StartsWith("HwndWrapper[GamingServicesTcui;",StringComparison.Ordinal))return true;
            found=window;return false;},0);
        return found;
    }
    sealed class Scope(Parent target,SteamXboxProfileWindow proof,nint window):IDisposable
    {internal readonly Parent Target=target;internal readonly SteamXboxProfileWindow Proof=proof;internal readonly nint Window=window;internal bool WasForeground;public void Dispose(){Proof.Dispose();Target.Dispose();}}
    sealed class Parent(Process process,long birth):IRetainedSteamGameTarget,IDisposable
    {
        public uint Pid=>(uint)process.Id;
        public long Creation=>birth;
        public string Aumid {get;}=ReadAumid(process);
        public SafeProcessHandle ProcessHandle=>process.SafeHandle;
        public bool HasExited=>process.HasExited;
        public bool IsCurrent(){try{return !process.HasExited&&process.StartTime.ToFileTimeUtc()==birth;}catch{return false;}}
        internal static Parent? Open(uint id)
        {try{var p=Process.GetProcessById(checked((int)id));if(p.HasExited){p.Dispose();return null;}return new(p,p.StartTime.ToFileTimeUtc());}catch{return null;}}
        internal nint GameWindow(){try{
            process.Refresh();nint w=process.MainWindowHandle;
            if(IsCurrent()&&w!=0&&IsWindowVisible(w)&&GetWindowThreadProcessId(w,out uint id)!=0&&id==Pid)return w;
            if(!IsCurrent()||string.IsNullOrEmpty(Aumid))return 0;
            nint frame=0;EnumWindows((window,_)=>{if(IsWindowVisible(window)&&UwpFrameWindow.Matches(window,Aumid)){frame=window;return false;}return true;},0);
            return IsCurrent()?frame:0;
        }catch{return 0;}}
        static string ReadAumid(Process process){uint n=0;if(GetApplicationUserModelId(process.SafeHandle,ref n,null)!=122||n is <2 or >32768)return "";var text=new StringBuilder((int)n);return GetApplicationUserModelId(process.SafeHandle,ref n,text)==0?text.ToString():"";}
        public void Dispose()=>process.Dispose();
    }
    public void Dispose(){stop.Cancel();try{observer.Wait(1500);}catch(AggregateException){}if(observer.IsCompleted)stop.Dispose();}
    [DllImport("user32.dll")]static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint pid);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(nint window);
    delegate bool EnumWindow(nint window,nint context);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumWindow callback,nint context);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="GetClassNameW")]static extern int GetClassName(nint window,StringBuilder text,int size);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]static extern int GetApplicationUserModelId(SafeProcessHandle process,ref uint size,StringBuilder? text);
}
