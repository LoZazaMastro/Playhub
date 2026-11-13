// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Authenticated wrapper historical witness. Not an HTTP/client authorization API.
using System;using System.IO;using System.Linq;using System.Collections.Generic;
using System.ComponentModel;using System.Diagnostics;using System.Runtime.InteropServices;
using System.Security.Cryptography;using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;using System.Text;using System.Text.Json;using System.Text.RegularExpressions;
using Microsoft.Win32;using Microsoft.Win32.SafeHandles;
namespace Playhub.Shared;

internal sealed record EpicProcessIdentity(uint Pid,long Birth,string Image,uint ParentPid,string Sid,uint Session);
internal enum EpicProcessState { Alive,Exited,Unknown }
internal interface IEpicRetainedProcess:IDisposable {
 EpicProcessIdentity Identity {get;} bool Current {get;}
 EpicProcessState State => Current?EpicProcessState.Alive:EpicProcessState.Unknown;
 bool IdentityRetained => Current;
 long ExitAt => 0;
}
internal interface IEpicTrustedImage:IDisposable {string Image{get;}string Sha256{get;}bool Current{get;}}
internal interface IEpicLaunchSystem {
 long Now{get;} EpicInstalledLaunch? Resolve(EpicInstalledLaunch expected); EpicLaunchPayload? Payload(EpicInstalledLaunch launch);
 string? RegisteredLauncher();IEpicTrustedImage? Trust(string image);IEpicRetainedProcess? Open(uint pid);
 IReadOnlyList<uint> LauncherCandidates(string image);bool GameWindow(long window,uint gamePid);
}
internal static class EpicLaunchWindows {
 internal static bool SamePath(string a,string b)=>string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
 internal static bool SameUser(EpicProcessIdentity a,EpicProcessIdentity b)=>!string.IsNullOrWhiteSpace(a.Sid)&&a.Sid==b.Sid&&a.Session==b.Session;
 internal static bool Expected(Exception e)=>EpicLaunchCatalog.Expected(e)||e is InvalidOperationException;
 internal static bool Hash(string? s)=>s is {Length:64}&&s.All(Uri.IsHexDigit);
 internal static EpicCaptureScope? Capture(EpicInstalledLaunch launch,uint wrapperPid,long wrapperBirth)=>Capture(launch,wrapperPid,wrapperBirth,new EpicNativeLaunchSystem());
 internal static EpicCaptureScope? Capture(EpicInstalledLaunch launch,uint wrapperPid,long wrapperBirth,IEpicLaunchSystem system){
  IEpicRetainedProcess? wrapper=null,launcher=null;IEpicTrustedImage? trusted=null;
  try {
   if(wrapperPid==0||wrapperBirth<=0||system.Resolve(launch)!=launch||system.Payload(launch) is not {} payload
      ||system.RegisteredLauncher() is not {} path)return null;
   trusted=system.Trust(path);wrapper=system.Open(wrapperPid);
   if(trusted?.Current!=true||wrapper?.Current!=true||wrapper.Identity.Birth!=wrapperBirth||wrapper.Identity.Pid!=wrapperPid||!SamePath(trusted.Image,path))return null;
   var ids=system.LauncherCandidates(path);if(ids.Count>32)return null;
   foreach(uint pid in ids.Distinct()){
    var value=system.Open(pid);try{
     if(value?.Current!=true||!SamePath(value.Identity.Image,path)||!SameUser(value.Identity,wrapper.Identity))continue;
     if(launcher is not null)return null;launcher=value;value=null;
    }finally{value?.Dispose();}
   }
   if(launcher is null||launcher.Identity.Pid==wrapperPid)return null;
   var result=new EpicCaptureScope(launch,payload,wrapper,launcher,trusted,system);wrapper=launcher=null;trusted=null;
   if(result.Current)return result;result.Dispose();return null;
  }catch(Exception e)when(Expected(e)){return null;}finally{wrapper?.Dispose();launcher?.Dispose();trusted?.Dispose();}
 }
 // PRECONDITION: caller authenticated a fresh private publication from the
 // installed, Steam-tracked wrapper generation (or exact parent/stdin + tracking).
 // Never pass an HTTP request's claimed witness directly to this method.
 internal static EpicCaptureScope? RetainFromAuthenticatedWrapper(EpicLaunchProvenance p,uint wrapperPid,long wrapperBirth)
  =>RetainFromAuthenticatedWrapper(p,wrapperPid,wrapperBirth,new EpicNativeLaunchSystem());
 internal static EpicCaptureScope? RetainFromAuthenticatedWrapper(EpicLaunchProvenance p,uint wrapperPid,long wrapperBirth,IEpicLaunchSystem system){
  IEpicRetainedProcess? wrapper=null,game=null;
  try{
   var chain=ReadWitness(p,wrapperPid,wrapperBirth);if(chain is null)return null;
   wrapper=system.Open(wrapperPid);game=system.Open(p.GamePid);
   if(wrapper?.Current!=true||game?.Current!=true||wrapper.Identity.Birth!=wrapperBirth||wrapper.Identity.Pid!=wrapperPid
      ||!SameUser(game.Identity,wrapper.Identity)||game.Identity!=chain[0]||!system.GameWindow(p.GameWindow,p.GamePid)
      ||!wrapper.Current||!game.Current)return null;
   var result=new EpicCaptureScope(wrapper,game,p,system);wrapper=game=null;
   if(result.Current)return result;result.Dispose();return null;
  }catch(Exception e)when(Expected(e)){return null;}finally{wrapper?.Dispose();game?.Dispose();}
 }
 internal static EpicProcessIdentity[]? ReadWitness(EpicLaunchProvenance p,uint wrapper,long wrapperBirth){
  if(p is null||p.Protocol!=1||p.WrapperPid!=wrapper||p.WrapperBirth!=wrapperBirth||wrapper==0||wrapperBirth<=0
   ||new[]{p.Namespace,p.CatalogItemId,p.AppName,p.ManifestPath,p.ManifestIdentitySha256,p.InstallDirectory,p.GameExecutable,p.LauncherImage,p.LauncherSha256}.Any(string.IsNullOrWhiteSpace)
   ||!Guid.TryParseExact(p.Ticket,"N",out var ticket)||ticket==Guid.Empty||p.BeforeTargetCount!=0
   ||p.GamePid==0||p.GamePid==wrapper||p.GamePid==p.LauncherPid||p.GameBirth<p.StartedAt||p.StartedAt<wrapperBirth||p.GameWindow<=0
   ||!EpicLaunchCatalog.Identifier(p.Namespace)||!EpicLaunchCatalog.Identifier(p.CatalogItemId)||!EpicLaunchCatalog.Identifier(p.AppName)
   ||!Hash(p.ManifestIdentitySha256)||!Hash(p.LauncherSha256)||p.RecordedLaunchExecutable is not {} recorded
   ||!EpicLaunchCatalog.LocalAbsolute(recorded)||!EpicLaunchCatalog.LocalAbsolute(p.GameExecutable)||!EpicLaunchCatalog.LocalAbsolute(p.InstallDirectory)
   ||!EpicLaunchCatalog.LocalAbsolute(p.ManifestPath)||!EpicLaunchCatalog.LocalAbsolute(p.LauncherImage)
   ||!Path.GetFileName(p.LauncherImage).Equals("EpicGamesLauncher.exe",StringComparison.OrdinalIgnoreCase)
   ||p.ParentChain is not {Length:>0 and <=32768})return null;
  string root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.InstallDirectory))+Path.DirectorySeparatorChar;
  if(!Path.GetFullPath(recorded).StartsWith(root,StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(p.GameExecutable).StartsWith(root,StringComparison.OrdinalIgnoreCase)
   ||!Path.GetExtension(recorded).Equals(".exe",StringComparison.OrdinalIgnoreCase)||!Path.GetExtension(p.GameExecutable).Equals(".exe",StringComparison.OrdinalIgnoreCase))return null;
  if((p.PayloadConfigurationPath is null)!=(p.PayloadConfigurationSha256 is null)
   ||!SamePath(recorded,p.GameExecutable)&&(p.PayloadConfigurationPath is null||!Hash(p.PayloadConfigurationSha256)))return null;
  if(p.PayloadConfigurationPath is {} config&&(!Hash(p.PayloadConfigurationSha256)||!EpicLaunchCatalog.LocalAbsolute(config)
    ||!Path.GetFullPath(config).StartsWith(root,StringComparison.OrdinalIgnoreCase)))return null;
  string id=string.Join("\n",p.Namespace,p.CatalogItemId,p.AppName,Path.GetFullPath(p.ManifestPath).ToUpperInvariant(),Path.TrimEndingDirectorySeparator(Path.GetFullPath(p.InstallDirectory)).ToUpperInvariant(),Path.GetFullPath(recorded).ToUpperInvariant());
  if(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))!=p.ManifestIdentitySha256)return null;
  var chain=JsonSerializer.Deserialize<EpicProcessIdentity[]>(p.ParentChain);
  if(chain is null||chain.Length is <2 or >8||chain.Any(x=>x is null)||JsonSerializer.Serialize(chain)!=p.ParentChain||chain.Select(x=>x.Pid).Distinct().Count()!=chain.Length)return null;
  var first=chain[0];var last=chain[^1];
  if(first.Pid!=p.GamePid||first.Birth!=p.GameBirth||!SamePath(first.Image,p.GameExecutable)||last.Pid!=p.LauncherPid||last.Birth!=p.LauncherBirth||!SamePath(last.Image,p.LauncherImage))return null;
  bool recordedSeen=false;
  for(int n=0;n<chain.Length;n++){
   var item=chain[n];if(item.Pid==0||item.Pid==wrapper||item.Birth<=0||!SameUser(item,first)||!EpicLaunchCatalog.LocalAbsolute(item.Image))return null;
   if(n<chain.Length-1){var parent=chain[n+1];if(!Path.GetFullPath(item.Image).StartsWith(root,StringComparison.OrdinalIgnoreCase)
      ||item.ParentPid!=parent.Pid||parent.Birth>item.Birth||item.Birth<p.StartedAt)return null;}
   if(SamePath(item.Image,recorded)&&n<chain.Length-1){if(recordedSeen)return null;recordedSeen=true;}
  }
  return recordedSeen?chain:null;
 }
}
internal sealed class EpicCaptureScope:IDisposable {
 readonly object gate=new();readonly IEpicLaunchSystem system;readonly IEpicRetainedProcess wrapper;
 readonly EpicProcessIdentity w;readonly EpicInstalledLaunch? launch;readonly EpicLaunchPayload? payload;
 readonly IEpicRetainedProcess? launcher;readonly EpicProcessIdentity? l;readonly IEpicTrustedImage? trusted;
 readonly string ticket=Guid.NewGuid().ToString("N");readonly List<(IEpicRetainedProcess Process,EpicProcessIdentity Identity,long ChildBirth)> ancestors=new();
 IEpicRetainedProcess? game;EpicLaunchProvenance? proof;bool disposed,binding;long revision;
 internal EpicCaptureScope(EpicInstalledLaunch launch,EpicLaunchPayload payload,IEpicRetainedProcess wrapper,IEpicRetainedProcess launcher,IEpicTrustedImage trusted,IEpicLaunchSystem system){
  this.launch=launch;this.payload=payload;this.wrapper=wrapper;this.launcher=launcher;this.trusted=trusted;this.system=system;w=wrapper.Identity;l=launcher.Identity;
 }
 internal EpicCaptureScope(IEpicRetainedProcess wrapper,IEpicRetainedProcess game,EpicLaunchProvenance proof,IEpicLaunchSystem system){this.wrapper=wrapper;this.game=game;this.proof=proof;this.system=system;w=wrapper.Identity;}
 bool BaseCurrent()=>!disposed&&wrapper.Identity==w&&wrapper.Current&&!disposed;
 bool SealedCurrent(){
  if(!BaseCurrent()||proof is not {} p||game is null||!game.Current||game.Identity.Pid!=p.GamePid||game.Identity.Birth!=p.GameBirth
    ||!EpicLaunchWindows.SamePath(game.Identity.Image,p.GameExecutable)||!EpicLaunchWindows.SameUser(game.Identity,w))return false;
  foreach(var item in ancestors){
   if(item.Process.Identity!=item.Identity||!item.Process.IdentityRetained)return false;
   var state=item.Process.State;
   if(state==EpicProcessState.Unknown||state==EpicProcessState.Exited&&item.Process.ExitAt<item.ChildBirth)return false;
  }
  return BaseCurrent()&&game.Current&&!disposed;
 }
 public bool Current {get{lock(gate)try{
  long stamp=revision;if(proof is not null)return SealedCurrent()&&revision==stamp;
  return BaseCurrent()&&launcher?.Current==true&&launcher.Identity==l&&trusted?.Current==true
   &&EpicLaunchWindows.SamePath(system.RegisteredLauncher()??"",l!.Image)&&system.Resolve(launch!)==launch&&system.Payload(launch!)==payload
   &&BaseCurrent()&&launcher.Current&&launcher.Identity==l&&trusted.Current&&revision==stamp&&!disposed;
 }catch(Exception e)when(EpicLaunchWindows.Expected(e)){return false;}}}
 internal bool RetainedCurrent {get{lock(gate)try{long stamp=revision;return proof is not null&&SealedCurrent()&&revision==stamp;}catch(Exception e)when(EpicLaunchWindows.Expected(e)){return false;}}}
 internal bool Retains(EpicLaunchProvenance expected){lock(gate)return proof==expected&&RetainedCurrent&&proof==expected&&!disposed;}
 internal bool FastCurrent=>RetainedCurrent;
 internal EpicLaunchProvenance? CreateProvenance(uint gamePid,long gameBirth,long window,long startedAt,int beforeTargetCount){
  lock(gate){
   if(disposed||binding||proof is not null||beforeTargetCount!=0||startedAt<w.Birth||gameBirth<startedAt||gameBirth>system.Now||system.Now-gameBirth>TimeSpan.FromSeconds(15).Ticks||!Current)return null;
   binding=true;long stamp=revision;var opened=new List<IEpicRetainedProcess>();
   try{
    var facts=new List<EpicProcessIdentity>();var seen=new HashSet<uint>();uint next=gamePid;long childBirth=long.MaxValue;
    for(int n=0;n<8;n++){
     if(next==0||!seen.Add(next))return null;
     if(next==l!.Pid){
      if(launcher?.Current!=true||launcher.Identity!=l||l.Birth>childBirth)return null;facts.Add(l);break;
     }
     var process=system.Open(next);if(process is null)return null;opened.Add(process);var value=process.Identity;
     if(!process.Current||!EpicLaunchWindows.SameUser(value,w)||value.Pid!=next||value.Birth<startedAt||value.Birth>childBirth)return null;
     facts.Add(value);childBirth=value.Birth;next=value.ParentPid;
    }
    if(facts.Count<2||facts[^1]!=l||facts[0].Pid!=gamePid||facts[0].Birth!=gameBirth||!EpicLaunchWindows.SamePath(facts[0].Image,payload!.Executable))return null;
    var p=new EpicLaunchProvenance(1,ticket,launch!.Namespace,launch.CatalogItemId,launch.AppName,launch.ManifestPath,launch.ManifestIdentitySha256,
     launch.InstallDirectory,payload.Executable,w.Pid,w.Birth,l.Pid,l.Birth,l.Image,trusted!.Sha256,startedAt,gamePid,gameBirth,window,0,
     launch.GameExecutable,JsonSerializer.Serialize(facts.ToArray()),payload.ConfigurationPath,payload.ConfigurationSha256);
    if(EpicLaunchWindows.ReadWitness(p,w.Pid,w.Birth)is null||!system.GameWindow(window,gamePid)||!Current||opened.Any(x=>!x.Current)
      ||disposed||revision!=stamp||proof is not null)return null;
    // Seal while every witnessed process is current. These exact retained
    // ancestor objects may subsequently exit normally, never be replaced.
    game=opened[0];for(int n=1;n<opened.Count;n++)ancestors.Add((opened[n],facts[n],facts[n-1].Birth));
    ancestors.Add((launcher!,l,facts[^2].Birth));proof=p;revision++;opened.Clear();
    trusted.Dispose(); // Signature/hash is historical; do not lock out updates.
    return SealedCurrent()?p:null;
   }catch(Exception e)when(EpicLaunchWindows.Expected(e)){return null;}
   finally{foreach(var value in opened)value.Dispose();binding=false;}
  }
 }
 public void Dispose(){lock(gate){if(disposed)return;disposed=true;revision++;game?.Dispose();foreach(var item in ancestors)if(!ReferenceEquals(item.Process,launcher))item.Process.Dispose();launcher?.Dispose();trusted?.Dispose();wrapper.Dispose();}}
}

internal sealed class EpicNativeLaunchSystem : IEpicLaunchSystem
{
    public long Now => DateTime.UtcNow.ToFileTimeUtc();
    public EpicInstalledLaunch? Resolve(EpicInstalledLaunch p) => EpicLaunchCatalog.ResolveInstalled(p.Namespace, p.CatalogItemId, p.AppName, p.GameExecutable);
    public string? RegisteredLauncher()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(@"com.epicgames.launcher\shell\open\command", false);
        string? command = key?.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        string? image = ParseRegisteredLauncherCommand(command);
        return image is not null && EpicLaunchCatalog.PlainExistingPath(image) ? image : null;
    }
    internal static string? ParseRegisteredLauncherCommand(string? command)
    {
        if (command is null || command.Length > 32768) return null;
        // Epic registers both quoted and unquoted URI placeholders. The image
        // remains one quoted absolute executable, with no additional switches.
        var match = Regex.Match(command, "^\\s*\"(?<exe>[^\"]+)\"\\s+(?:\"%1\"|%1)\\s*$", RegexOptions.CultureInvariant);
        string value = match.Groups["exe"].Value;
        return match.Success && EpicLaunchCatalog.LocalAbsolute(value)
            && Path.GetFileName(value).Equals("EpicGamesLauncher.exe", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(value) : null;
    }
    public EpicLaunchPayload? Payload(EpicInstalledLaunch launch) => EpicLaunchPayloadResolver.Resolve(launch);
    public IEpicTrustedImage? Trust(string image) => EpicSignedLauncher.Open(image);
    public IEpicRetainedProcess? Open(uint pid) => EpicNativeProcess.Open(pid);
    public IReadOnlyList<uint> LauncherCandidates(string image)
    {
        var result = new List<uint>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(image)))
            using (process) { if (result.Count <= 32) result.Add((uint)process.Id); }
        return result;
    }
    public bool GameWindow(long raw, uint pid)
    {
        nint hwnd = (nint)raw;
        if (raw <= 0 || GetWindowThreadProcessId(hwnd, out uint owner) == 0 || owner != pid
            || GetAncestor(hwnd, 2) != hwnd || !IsWindowVisible(hwnd) || IsIconic(hwnd)
            || DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) != 0 || cloaked != 0
            || !GetClientRect(hwnd, out var rect) || rect.Right - rect.Left < 128 || rect.Bottom - rect.Top < 128) return false;
        return GetWindowThreadProcessId(hwnd, out uint finalOwner) != 0 && finalOwner == pid && GetAncestor(hwnd, 2) == hwnd;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint kind);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
}

internal sealed class EpicNativeProcess : IEpicRetainedProcess
{
    private readonly SafeProcessHandle handle;
    public EpicProcessIdentity Identity { get; }
    private EpicNativeProcess(SafeProcessHandle handle, EpicProcessIdentity identity) => (this.handle, Identity) = (handle, identity);
    internal static EpicNativeProcess? Open(uint pid)
    {
        SafeProcessHandle? handle = null;
        try
        {
            handle = OpenProcess(0x101000, false, pid);
            if (handle.IsInvalid || WaitForSingleObject(handle, 0) != 258 || GetProcessId(handle) != pid
                || !GetProcessTimes(handle, out long birth, out long exit, out _, out _) || birth <= 0 || exit != 0
                || !ProcessIdToSessionId(pid, out uint session)) return null;
            var image = new StringBuilder(32768); uint length = (uint)image.Capacity;
            if (!QueryFullProcessImageName(handle, 0, image, ref length) || !OpenProcessToken(handle, 8, out var token)) return null;
            string? sid;
            using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            {
                sid = identity.User?.Value;
                if (!GetTokenInformation(token, 29, out int container, sizeof(int), out _) || container != 0) return null;
            }
            if (string.IsNullOrEmpty(sid)) return null;
            uint parent = Parent(pid);
            if (parent == 0) return null;
            var result = new EpicNativeProcess(handle, new(pid, birth, Path.GetFullPath(image.ToString()), parent, sid, session));
            handle = null;
            if (result.Current) return result;
            result.Dispose(); return null;
        }
        finally { handle?.Dispose(); }
    }
    public EpicProcessState State {
        get {
            if(handle.IsClosed || handle.IsInvalid || GetProcessId(handle)!=Identity.Pid
                || !GetProcessTimes(handle,out long birth,out long exit,out _,out _) || birth!=Identity.Birth) return EpicProcessState.Unknown;
            uint wait=WaitForSingleObject(handle,0);
            return wait==258 && exit==0 ? EpicProcessState.Alive : wait==0 && exit>=Identity.Birth ? EpicProcessState.Exited : EpicProcessState.Unknown;
        }
    }
    public bool Current => State==EpicProcessState.Alive;
    public bool IdentityRetained => State!=EpicProcessState.Unknown;
    public long ExitAt => !handle.IsClosed && !handle.IsInvalid && GetProcessTimes(handle,out long birth,out long exit,out _,out _) && birth==Identity.Birth ? exit : 0;
    public void Dispose() => handle.Dispose();
    private static uint Parent(uint pid)
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) return 0;
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        for (bool next = Process32First(snapshot, ref entry); next; next = Process32Next(snapshot, ref entry))
            if (entry.Pid == pid) return entry.Parent;
        return 0;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessEntry
    {
        internal uint Size, Usage, Pid; internal nuint Heap; internal uint Module, Threads, Parent; internal int Priority;
        internal uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Exe;
    }
    [DllImport("kernel32.dll")] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle process, uint timeout);
    [DllImport("kernel32.dll")] private static extern uint GetProcessId(SafeProcessHandle process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long birth, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder image, ref uint length);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int info, out int value, int size, out int used);
    [DllImport("kernel32.dll")] private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}

internal sealed class EpicSignedLauncher : IEpicTrustedImage
{
    private readonly FileStream file;
    public string Image { get; }
    public string Sha256 { get; }
    private EpicSignedLauncher(FileStream file, string image, string hash) => (this.file, Image, Sha256) = (file, image, hash);
    public bool Current => !file.SafeFileHandle.IsClosed && !file.SafeFileHandle.IsInvalid;
    public void Dispose() => file.Dispose();
    internal static EpicSignedLauncher? Open(string image)
    {
        FileStream? file = null;
        try
        {
            if (!EpicLaunchCatalog.PlainExistingPath(image)) return null;
            file = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!Verify(image)) return null;
            string hash = Convert.ToHexString(SHA256.HashData(file));
            var result = new EpicSignedLauncher(file, image, hash); file = null; return result;
        }
        finally { file?.Dispose(); }
    }
    // Based on Playhub's existing ValveImageTrust WinVerifyTrust lifecycle.
    private static bool Verify(string path)
    {
        var info = new FileInfoNative { Size = (uint)Marshal.SizeOf<FileInfoNative>(), FilePath = path };
        nint storage = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfoNative>());
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = storage, StateAction = 1, Flags = 0x1000 };
        Guid action = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            Marshal.StructureToPtr(info, storage, false);
            if (WinVerifyTrust(new nint(-1), ref action, ref data) != 0) return false;
            using var signer = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
            return signer.GetNameInfo(X509NameType.SimpleName, false) == "Epic Games Inc.";
        }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new nint(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfoNative>(storage); Marshal.FreeHGlobal(storage);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileInfoNative
    { internal uint Size; [MarshalAs(UnmanagedType.LPWStr)] internal string FilePath; internal nint File, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    {
        internal uint Size; internal nint Callback, Client; internal uint UiChoice, Revocation, UnionChoice;
        internal nint File; internal uint StateAction; internal nint State, Url; internal uint Flags, Context; internal nint SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
}
