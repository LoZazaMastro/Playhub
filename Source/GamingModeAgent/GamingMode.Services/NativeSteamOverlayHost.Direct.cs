using System.IO;
// Playhub changes. GPL-3.0-only. Separate original-game host discovery.
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Playhub.DeckyHost;
namespace GamingMode.Services;
internal static partial class NativeSteamOverlayHost
{
 private sealed class DirectHost(SafeProcessHandle handle,OwnedProcessFacts facts):IOwnedWin32Process
 {
  internal SafeProcessHandle Handle=>handle;
  public OwnedProcessFacts Identity=>facts;
  public bool Current=>!handle.IsClosed&&!handle.IsInvalid&&DirectWait(handle,0)==258&&DirectPid(handle)==facts.Pid
   &&DirectTimes(handle,out long birth,out long exit,out _,out _)&&birth==facts.Birth&&exit==0&&!handle.IsClosed;
  public void Dispose()=>handle.Dispose();
 }
 // Direct-only aliases are facts of THIS retained launch, not a 480 map.
 internal static bool DirectMatches(string[] args,string image,uint gamePid,uint steamPid,ulong fullGameId,ulong observedGameId)
 {
  if(args.Length<7||gamePid==0||steamPid==0||(uint)fullGameId!=0x02000000||fullGameId>>32<0x80000000||observedGameId==0
   ||!args[0].Equals(image,StringComparison.OrdinalIgnoreCase))return false;
  bool Read(string flag,out ulong value){value=0;var positions=Enumerable.Range(1,args.Length-1).Where(i=>args[i].Equals(flag,StringComparison.OrdinalIgnoreCase)).ToArray();
   return positions is [var index]&&index+1<args.Length&&ulong.TryParse(args[index+1],NumberStyles.None,CultureInfo.InvariantCulture,out value);}
  return Read("-pid",out ulong pid)&&pid==gamePid&&Read("-steampid",out ulong steam)&&steam==steamPid&&Read("-gameid",out ulong id)
   &&(id==fullGameId||id==observedGameId);
 }
 internal static NativeGameHostSearch FindDirect(string root,OwnedProcessFacts game,OwnedProcessFacts steam,ulong fullGameId,ulong observedGameId,Func<bool> ownerCurrent)
 {
  if(!ownerCurrent()||!ValveImageTrust.Verify(Path.Combine(root,"steam.exe"))||!ValveImageTrust.Verify(Path.Combine(root,"gameoverlayui64.exe")))return new("unknown");
  var all=Process.GetProcessesByName("gameoverlayui64");IOwnedWin32Process? match=null;bool unknown=false;
  try{
   if(all.Length>64)return new("unknown");
   foreach(var process in all){
    SafeProcessHandle? handle=null;
    try{
     var host=DeckyProcessTree.Read(process.Id);if(host is null){unknown=true;continue;}
     if(host.Session!=game.Session||host.Sid!=game.Sid||!host.Path.Equals(Path.Combine(root,"gameoverlayui64.exe"),StringComparison.OrdinalIgnoreCase))continue;
     if(host.ParentPid!=steam.Pid)continue; // another retained Steam generation is not this host
     if(host.Birth<steam.Birth){unknown=true;continue;}
     handle=OpenProcess(0x101000,false,process.Id);
     var held=new DirectHost(handle,new((uint)host.Pid,host.Birth,host.Path,host.Sid,host.Session));handle=null;
     bool transfer=false;
     try{
      if(!held.Current||ReadCommandLine(held.Handle) is not { } line){unknown=true;continue;}
      var args=Arguments(line);var pidFlags=Enumerable.Range(1,Math.Max(0,args.Length-1)).Where(i=>args[i].Equals("-pid",StringComparison.OrdinalIgnoreCase)).ToArray();
      if(pidFlags is not [var at]||at+1>=args.Length||!uint.TryParse(args[at+1],NumberStyles.None,CultureInfo.InvariantCulture,out uint target)||target==0){unknown=true;continue;}
      if(target!=game.Pid)continue;
      if(host.Birth<game.Birth||!DirectMatches(args,host.Path,game.Pid,steam.Pid,fullGameId,observedGameId)||!held.Current||!ownerCurrent()){unknown=true;continue;}
      if(match is not null){unknown=true;continue;}
      match=held;transfer=true;
     }finally{if(!transfer)held.Dispose();}
    }catch(Exception e)when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException or OverflowException){unknown=true;}
    finally{handle?.Dispose();}
   }
   if(unknown||!ownerCurrent()){match?.Dispose();match=null;return new("unknown");}
   if(match is null)return new("absent");var result=new NativeGameHostSearch("present",match);match=null;return result;
  }finally{match?.Dispose();foreach(var item in all)item.Dispose();}
 }
 [DllImport("kernel32.dll",EntryPoint="WaitForSingleObject")]private static extern uint DirectWait(SafeProcessHandle h,uint milliseconds);
 [DllImport("kernel32.dll",EntryPoint="GetProcessId")]private static extern uint DirectPid(SafeProcessHandle h);
 [DllImport("kernel32.dll",EntryPoint="GetProcessTimes")]private static extern bool DirectTimes(SafeProcessHandle h,out long birth,out long exit,out long kernel,out long user);
}
