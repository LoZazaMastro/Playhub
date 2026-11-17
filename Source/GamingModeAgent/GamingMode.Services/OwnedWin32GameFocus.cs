// One launch handoff or an explicit Steam Resume; never a foreground watchdog.
using System.Globalization;
using System.Runtime.InteropServices;
namespace GamingMode.Services;

internal sealed record Win32LaunchFocusRequest(uint AppId,uint WrapperPid,long WrapperBirth,uint GamePid,long GameBirth);
internal interface IOwnedGameFocusLease:IDisposable {
 Win32SteamSessionAssociation.Identity Identity{get;} bool Current{get;}
 IReadOnlyList<QamWindowIdentity> Windows();
}
internal interface IOwnedGameFocusIntent:IDisposable { bool Current{get;} void Admit(IOwnedGameFocusLease lease); }
internal interface IOwnedGameFocusRuntime {
 long Now{get;} long Tick{get;}
 IOwnedGameFocusIntent? CaptureSteamIntent();
 Task<IOwnedGameFocusLease?> Find(uint app,CancellationToken stop);
 Task Delay(int ms,CancellationToken stop);
 bool Activate(QamWindowIdentity target,Func<bool> current);
}
internal sealed class OwnedWin32GameFocus {
 readonly IOwnedGameFocusRuntime runtime;readonly object gate=new();readonly Dictionary<Win32LaunchFocusRequest,long> launches=new();
 readonly SemaphoreSlim resume=new(1,1);long epoch;int transitioning;
 internal void BeginModeTransition(){Interlocked.Exchange(ref transitioning,1);Invalidate();}
 internal void EndModeTransition(bool gaming){Invalidate();Interlocked.Exchange(ref transitioning,0);}
 internal void Invalidate()=>Interlocked.Increment(ref epoch);
 IOwnedGameFocusIntent? CaptureIntent(){
  if(Volatile.Read(ref transitioning)!=0)return null;
  long stamp=Volatile.Read(ref epoch);var intent=runtime.CaptureSteamIntent();
  if(intent is null)return null;
  return new EpochIntent(intent,()=>Volatile.Read(ref transitioning)==0&&Volatile.Read(ref epoch)==stamp);
 }
 sealed class EpochIntent(IOwnedGameFocusIntent inner,Func<bool> epochCurrent):IOwnedGameFocusIntent{
  public bool Current=>epochCurrent()&&inner.Current&&epochCurrent();
  public void Admit(IOwnedGameFocusLease lease){if(Current)inner.Admit(lease);}
  public void Dispose()=>inner.Dispose();
 }
 internal OwnedWin32GameFocus(WindowsQamFocusAdapter windows,string? steamPath):this(new WindowsOwnedGameFocusRuntime(windows,steamPath)){}
 internal OwnedWin32GameFocus(IOwnedGameFocusRuntime runtime)=>this.runtime=runtime;
 internal async Task<bool> Startup(Win32LaunchFocusRequest request,CancellationToken stop) {
  long now=runtime.Now;
  if(request.AppId<0x80000000||request.WrapperPid==0||request.GamePid==0||request.WrapperPid==request.GamePid
    ||request.WrapperBirth<=0||request.GameBirth<request.WrapperBirth||request.GameBirth>now
    ||now-request.GameBirth>100000000||now-request.WrapperBirth>1800000000)return false;
  lock(gate){
   foreach(var old in launches.Where(x=>x.Value<now).Select(x=>x.Key).ToArray())launches.Remove(old);
   // Consume before I/O: a timeout/replayed wrapper request cannot steal focus later.
   if(launches.ContainsKey(request)||launches.Count>=16)return false;
   launches.Add(request,checked(request.WrapperBirth+1800000000));
  }
  using var intent=CaptureIntent();if(intent?.Current!=true)return false;
  long started=runtime.Tick;
  while(!stop.IsCancellationRequested&&intent.Current&&runtime.Tick>=started&&runtime.Tick-started<=3000){
   using var owned=await runtime.Find(request.AppId,stop).ConfigureAwait(false);
   if(!intent.Current||runtime.Tick<started||runtime.Tick-started>3000)return false;
   if(owned is not null){
    var id=owned.Identity;
    if(id.WrapperPid!=request.WrapperPid||id.WrapperBirth!=request.WrapperBirth||id.GamePid!=request.GamePid||id.GameBirth!=request.GameBirth)return false;
    return Handoff(request.AppId,owned,intent,stop);
   }
   await runtime.Delay(50,stop).ConfigureAwait(false);
  }
  return false;
 }
 internal async Task<bool> Resume(uint app,CancellationToken stop){
  if(app<0x80000000||!await resume.WaitAsync(0,stop).ConfigureAwait(false))return false;
  try{
   using var intent=CaptureIntent();if(intent?.Current!=true)return false;
   // Give Steam's own user-requested RaiseWindowForGame the first opportunity.
   await runtime.Delay(200,stop).ConfigureAwait(false);if(!intent.Current)return false;
   using var owned=await runtime.Find(app,stop).ConfigureAwait(false);
   return owned is not null&&intent.Current&&Handoff(app,owned,intent,stop);
  }finally{resume.Release();}
 }
 bool Handoff(uint app,IOwnedGameFocusLease owned,IOwnedGameFocusIntent intent,CancellationToken stop){
  if(stop.IsCancellationRequested||!intent.Current||!owned.Current)return false;
  var id=owned.Identity;
  if(id.GameId!=((ulong)app<<32|0x02000000UL)||id.GamePid==0||id.GameBirth<=0)return false;
  var windows=owned.Windows();if(windows.Count!=1)return false;
  var target=windows[0];
  if(target.OwnerPid!=id.GamePid||target.OwnerBirth!=id.GameBirth||target.GamePid!=id.GamePid||target.GameBirth!=id.GameBirth)return false;
  intent.Admit(owned);
  bool Current()=>!stop.IsCancellationRequested&&intent.Current&&owned.Current
    &&owned.Identity==id&&owned.Windows() is [var same]&&same==target;
  if(!Current())return false;
  // Exactly one call; this adapter reports actual OS foreground, never a sent request.
  return runtime.Activate(target,Current)&&Current();
 }
}
internal sealed class WindowsOwnedGameFocusRuntime(WindowsQamFocusAdapter events,string? steamPath):IOwnedGameFocusRuntime {
 public long Now=>DateTime.UtcNow.ToFileTimeUtc();public long Tick=>Environment.TickCount64;
 public Task Delay(int ms,CancellationToken stop)=>Task.Delay(ms,stop);
 public IOwnedGameFocusIntent? CaptureSteamIntent(){
  if(WindowsQamFocusAdapter.CaptureSteam(steamPath) is not {} steam||!SteamCurrent(steam))return null;
  var intent=new Intent(events,steam);return intent.Current?intent:Dispose(intent);
 }
 static IOwnedGameFocusIntent? Dispose(Intent intent){intent.Dispose();return null;}
 public async Task<IOwnedGameFocusLease?> Find(uint app,CancellationToken stop){
  var lease=await Win32SteamSessionAssociation.FindLeaseAsync(app,stop).ConfigureAwait(false);
  return lease is null?null:new Lease(lease);
 }
 public bool Activate(QamWindowIdentity target,Func<bool> current)=>WindowsQamFocusAdapter.ActivateWindow(target.Handle,
  ()=>current()&&WindowsQamFocusAdapter.IsWindowOwnerCurrent(target));
 static bool SteamCurrent(QamWindowIdentity steam){
  if(!WindowsQamFocusAdapter.IsWindowOwnerCurrent(steam)||OverlayWindowTools.GetForegroundWindowHandle()!=steam.Handle
    ||!N.IsWindowEnabled(steam.Handle)||N.GetLastActivePopup(steam.Handle)!=steam.Handle)return false;
  var info=new N.GuiThreadInfo{Size=(uint)Marshal.SizeOf<N.GuiThreadInfo>()};
  return N.GetGUIThreadInfo(0,ref info)&&(info.Flags&0x1E)==0&&info.MenuOwner==0&&info.Capture==0;
 }
 sealed class Intent:IOwnedGameFocusIntent {
  readonly WindowsQamFocusAdapter events;readonly QamWindowIdentity steam;volatile IOwnedGameFocusLease? admitted;
  int revoked,disposed;readonly Action changed;
  internal Intent(WindowsQamFocusAdapter events,QamWindowIdentity steam){this.events=events;this.steam=steam;changed=Observe;events.Changed+=changed;Observe();}
  bool Allowed(){
   if(SteamCurrent(steam))return true;
   var owned=admitted;if(owned?.Current!=true)return false;
   var actual=OverlayWindowTools.GetForegroundWindowHandle();var targets=owned.Windows();
   return targets.Count==1&&targets[0].Handle==actual&&WindowsQamFocusAdapter.IsWindowOwnerCurrent(targets[0]);
  }
  void Observe(){if(!Allowed())Interlocked.Exchange(ref revoked,1);}
  public bool Current=>Volatile.Read(ref disposed)==0&&Volatile.Read(ref revoked)==0&&Allowed()&&Volatile.Read(ref revoked)==0;
  public void Admit(IOwnedGameFocusLease lease){if(Current&&lease.Current)admitted=lease;}
  public void Dispose(){Interlocked.Exchange(ref disposed,1);events.Changed-=changed;admitted=null;}
 }
 sealed class Lease(Win32OwnedAssociationCatalog.Lease owned):IOwnedGameFocusLease {
  public Win32SteamSessionAssociation.Identity Identity=>owned.Identity;public bool Current=>owned.Current;
  public IReadOnlyList<QamWindowIdentity> Windows(){
   if(!Current)return Array.Empty<QamWindowIdentity>();var id=Identity;
   var result=new List<QamWindowIdentity>();
   var handles=OverlayWindowTools.EnumerateTopLevelHandles().Take(4097).ToArray();
   if(handles.Length>4096)return Array.Empty<QamWindowIdentity>();
   foreach(nint hwnd in handles){
    if(OverlayWindowTools.WindowOwnerProcessId(hwnd)!=id.GamePid||!N.IsWindowVisible(hwnd))continue;
    // A modal/owned/disabled game window is a prompt, not a launch surface.
    if(N.GetAncestor(hwnd,2)!=hwnd||N.GetWindow(hwnd,4)!=0||!N.IsWindowEnabled(hwnd)||N.GetLastActivePopup(hwnd)!=hwnd
      ||!OverlayWindowTools.IsUsableWindow(hwnd)||!OverlayWindowTools.IsPresentGameSurface(hwnd))return Array.Empty<QamWindowIdentity>();
    if(WindowsQamFocusAdapter.Capture(hwnd,id.GamePid) is not {} target||target.OwnerPid!=id.GamePid||target.OwnerBirth!=id.GameBirth||target.GameBirth!=id.GameBirth)return Array.Empty<QamWindowIdentity>();
    result.Add(target);if(result.Count>1)return Array.Empty<QamWindowIdentity>();
   }
   return Current?result:Array.Empty<QamWindowIdentity>();
  }
  public void Dispose()=>owned.Dispose();
 }
 static class N {
  [StructLayout(LayoutKind.Sequential)]internal struct Rect{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]internal struct GuiThreadInfo{public uint Size,Flags;public nint Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Rect CaretRect;}
  [DllImport("user32.dll")]internal static extern bool IsWindowEnabled(nint hwnd);
  [DllImport("user32.dll")]internal static extern bool IsWindowVisible(nint hwnd);
  [DllImport("user32.dll")]internal static extern nint GetWindow(nint hwnd,uint relation);
  [DllImport("user32.dll")]internal static extern nint GetAncestor(nint hwnd,uint flags);
  [DllImport("user32.dll")]internal static extern nint GetLastActivePopup(nint hwnd);
  [DllImport("user32.dll")]internal static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
 }
}
