using System.IO;
// Playhub changes. GPL-3.0-only. Read-only retained launch/pipe proof.
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Playhub.DeckyHost;
namespace GamingMode.Services;
internal sealed class NativeGameOverlayOwner:INativeGameOverlayOwner
{
 private readonly Win32OwnedAssociationCatalog.Lease association;
 private readonly IOwnedSteamGeneration steam;
 private readonly IOwnedWin32Process wrapper,game,companion;
 private readonly OwnedWin32Route initial;
 private bool disposed;
 private NativeGameOverlayOwner(Win32OwnedAssociationCatalog.Lease a,IOwnedSteamGeneration s,IOwnedWin32Process w,IOwnedWin32Process g,IOwnedWin32Process c,OwnedWin32Route route)
  =>(association,steam,wrapper,game,companion,initial)=(a,s,w,g,c,route);
 public OwnedWin32Route Route=>association.Route??throw new InvalidDataException("Original launch proof expired.");
 public OwnedProcessFacts Companion=>companion.Identity;
 public long Tick=>Environment.TickCount64;
 public bool Current=>!disposed&&association.Current&&steam.Current&&wrapper.Current&&game.Current&&companion.Current
  &&association.Route is { } r&&OwnedWin32RoutePolicy.Same(initial,r)&&!disposed;
 public nint GameForeground(){
  if(!Current)return 0;nint window=OverlayWindowTools.GetForegroundWindowHandle();
  return window!=0&&OverlayWindowTools.IsPresentGameSurface(window)&&WindowPid(window,out uint pid)!=0&&pid==initial.GamePid
   &&Root(window,2)==window&&Current&&OverlayWindowTools.GetForegroundWindowHandle()==window?window:0;
 }
 internal static bool OwnedGameWindow(nint window,uint gamePid)=>WindowPid(window,out uint pid)!=0&&pid==gamePid&&Root(window,2)==window;
 public NativeGameHostSearch FindHost(){
  if(ulong.TryParse(Route.ObservedSteamGameId,NumberStyles.None,CultureInfo.InvariantCulture,out ulong observed)&&observed>0)
   return NativeSteamOverlayHost.FindDirect(steam.Root,game.Identity,steam.Identity,association.Identity.GameId,observed,()=>Current);
  // A zero observation is not a host identity: the unchanged matcher refuses
  // every present game host. A complete signed inventory may only prove ABSENT.
  return association.Identity.Epic is not null
   ?NativeSteamOverlayHost.FindDirect(steam.Root,game.Identity,steam.Identity,association.Identity.GameId,0,()=>Current):new("unknown");
 }
 private sealed record Status(ulong GameId,uint GamePid,long GameBirth);
 internal static async Task<NativeGameOverlayOwner?> Open(uint appId,CancellationToken cancellation)
 {
  Win32OwnedAssociationCatalog.Lease? a=null;IOwnedSteamGeneration? s=null;IOwnedWin32Process? w=null,g=null,c=null;
  try{
   a=await Win32SteamSessionAssociation.FindLeaseAsync(appId,cancellation).ConfigureAwait(false);
   if(a?.Current!=true||a.Route is not { } route)return null;
   var system=new NativeOwnedAssociationSystem();var runtime=new WindowsOwnedAssociationRuntime(system);
   s=runtime.OpenSteam();w=runtime.Open(route.WrapperPid);g=runtime.Open(route.GamePid);
   if(s?.Current!=true||w?.Current!=true||g?.Current!=true||UwpSteamSessionAssociation.ReadInstalledImages() is not { } images
    ||s.Identity.Pid!=route.SteamPid||Dec(s.Identity.Birth)!=route.SteamBirth||w.Identity.Birth!=a.Identity.WrapperBirth
    ||g.Identity.Birth!=a.Identity.GameBirth||!w.Identity.Image.Equals(images.Wrapper,StringComparison.OrdinalIgnoreCase)
    ||!g.Identity.Image.Equals(a.Identity.Executable,StringComparison.OrdinalIgnoreCase)
    ||w.Identity.Sid!=s.Identity.Sid||g.Identity.Sid!=s.Identity.Sid||w.Identity.Session!=s.Identity.Session||g.Identity.Session!=s.Identity.Session)return null;
   string name=FormattableString.Invariant($"Playhub.XboxCompanion.{route.WrapperPid}.{route.WrapperBirth}");
   using var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancellation);bounded.CancelAfter(600);
   using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.In,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly)){
    await pipe.ConnectAsync(bounded.Token).ConfigureAwait(false);
    if(!ServerPid(pipe.SafePipeHandle.DangerousGetHandle(),out uint server))return null;c=runtime.Open(server);
    if(c?.Current!=true||c.Identity.Pid==g.Identity.Pid||c.Identity.Pid==w.Identity.Pid||c.Identity.Pid==s.Identity.Pid
     ||c.Identity.Birth<w.Identity.Birth||c.Identity.Sid!=s.Identity.Sid||c.Identity.Session!=s.Identity.Session
     ||!c.Identity.Image.Equals(images.Companion,StringComparison.OrdinalIgnoreCase)
     ||DeckyProcessTree.Read(checked((int)server))?.ParentPid!=w.Identity.Pid)return null;
    byte[] data=new byte[513];int used=0;while(used<data.Length){int count=await pipe.ReadAsync(data.AsMemory(used),bounded.Token).ConfigureAwait(false);if(count==0)break;used+=count;}
    if(used==0||used>=data.Length)return null;var state=JsonSerializer.Deserialize<Status>(data.AsSpan(0,used));
    if(state is null||state.GameId!=a.Identity.GameId||state.GamePid!=route.GamePid||state.GameBirth!=a.Identity.GameBirth)return null;
   }
   var result=new NativeGameOverlayOwner(a,s,w,g,c,route);if(!result.Current||result.GameForeground()==0)return null;
   a=null;s=null;w=g=c=null;return result;
  }finally{c?.Dispose();g?.Dispose();w?.Dispose();s?.Dispose();a?.Dispose();}
 }
 private static string Dec(long value)=>value.ToString(CultureInfo.InvariantCulture);
 public void Dispose(){if(disposed)return;disposed=true;companion.Dispose();game.Dispose();wrapper.Dispose();steam.Dispose();association.Dispose();}
 [DllImport("kernel32.dll",EntryPoint="GetNamedPipeServerProcessId")]internal static extern bool ServerPid(nint pipe,out uint pid);
 [DllImport("user32.dll",EntryPoint="GetWindowThreadProcessId")]private static extern uint WindowPid(nint window,out uint pid);
 [DllImport("user32.dll",EntryPoint="GetAncestor")]private static extern nint Root(nint window,uint flags);
}
