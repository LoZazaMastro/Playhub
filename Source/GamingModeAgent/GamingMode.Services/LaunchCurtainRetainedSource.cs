// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Playhub.FirstContent;
namespace GamingMode.Services;
// App identity is resolved server-side by the existing retained association.
// Client fields identify only its existing LC publisher and Steam cover receipt.
internal sealed class LaunchCurtainRetainedSource:ILaunchCurtainSource
{
 readonly ILivePerformanceSource gameSource;readonly CurtainCoverRequest request;
 readonly Retained publisher,steam,game,frame;readonly FileStream steamImagePin,publisherImagePin;
 readonly CurtainVisualAuthority visual;readonly ContentTarget capture;
 bool disposed;
 LaunchCurtainRetainedSource(ILivePerformanceSource source,CurtainCoverRequest r,Retained p,Retained s,Retained g,Retained f,
   FileStream image,FileStream publisherFile,CurtainVisualAuthority authority,ContentTarget target)
  =>(gameSource,request,publisher,steam,game,frame,steamImagePin,publisherImagePin,visual,capture)=(source,r,p,s,g,f,image,publisherFile,authority,target);
 public CurtainVisualAuthority Visual=>visual;
 public ContentTarget CaptureTarget=>capture;
 internal static async Task<LaunchCurtainRetainedSource?> ResolveAsync(CurtainCoverRequest request,LivePerformanceSourceResolver resolver,CancellationToken cancellation)
 {
  ILivePerformanceSource? source=null;Retained? publisher=null,steam=null,game=null,frame=null;FileStream? image=null,publisherFile=null;
  try{
   long now=DateTime.UtcNow.ToFileTimeUtc();if(request.Action!="begin"||!CurtainCoverProtocol.Hex(request.SourceReceipt)||request.SurfaceRevision==0||request.RequestEpoch>(ulong)now||(ulong)now-request.RequestEpoch>36000000000)return null;
   source=await resolver.ResolveAsync(request.AppId,cancellation).ConfigureAwait(false);
   if(source is null||!source.Current||!source.TryGetBounds(out _)||source.ForegroundProof is not {} proof)return null;
   publisher=new(request.PublisherPid,request.PublisherBirth);steam=new(request.SteamPid,request.SteamBirth);
   if(string.IsNullOrWhiteSpace(request.PublisherImage)||!publisher.Image().Equals(request.PublisherImage,StringComparison.OrdinalIgnoreCase))return null;
   publisherFile=new(request.PublisherImage,FileMode.Open,FileAccess.Read,FileShare.Read);
   game=new(checked((uint)source.Target.ProcessId),source.Target.CreationFileTimeUtc);frame=new(checked((uint)proof.ProcessId),proof.ProcessBirth);
   using var self=Process.GetCurrentProcess();uint session=(uint)self.SessionId;
   if(!publisher.Current(session)||!steam.Current(session)||!game.Current(session)||!frame.Current(session)
     ||request.PublisherPid==game.Pid||request.PublisherPid==frame.Pid||request.SteamPid==game.Pid||request.SteamPid==frame.Pid
     ||game.Birth<checked((long)request.RequestEpoch)-20000000||proof.WindowHandle==0||N.GetForegroundWindow()!=proof.WindowHandle)return null;
   string? root=SteamInstallationPath.FindRoot();string steamImage=steam.Image();
   if(root is null||!steamImage.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
     ||!Path.GetFileName(steamImage).Equals("steamwebhelper.exe",StringComparison.OrdinalIgnoreCase)
     ||(File.GetAttributes(steamImage)&FileAttributes.ReparsePoint)!=0)return null;
   image=new(steamImage,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(!ValveImageTrust.Verify(steamImage))return null;
   nint previous=N.SetThreadDpiAwarenessContext(new(-4));if(previous==0)return null;
   try{
    nint steamWindow=checked((nint)request.SteamWindow);nint gameWindow=proof.WindowHandle;
    string steamClass=Class(steamWindow),gameClass=Class(gameWindow);uint thread=N.GetWindowThreadProcessId(gameWindow,out uint framePid);
    if(N.GetWindowThreadProcessId(steamWindow,out uint steamPid)!=request.SteamThread||steamPid!=request.SteamPid||steamClass!=request.SteamClass
      ||N.GetAncestor(steamWindow,2)!=steamWindow||N.GetWindow(steamWindow,4)!=0||!N.IsWindowVisible(steamWindow)||!N.IsWindowEnabled(steamWindow)||N.IsIconic(steamWindow)
      ||thread==0||framePid!=frame.Pid
      ||N.GetAncestor(gameWindow,2)!=gameWindow||!N.GetWindowRect(gameWindow,out var gameRect)||!N.GetWindowRect(steamWindow,out var steamRect))return null;
    string aumid="";
    if(frame.Pid!=game.Pid){var uwp=UwpSteamSessionAssociation.Find(game.Pid,request.AppId);
     if(uwp is null||uwp.GameBirth!=game.Birth||!UwpSteamSessionAssociation.IsOnlyRunningInstance(uwp))return null;
     aumid=UwpFrameWindow.ReadAppId(gameWindow)??"";if(!aumid.Equals(uwp.Aumid,StringComparison.OrdinalIgnoreCase))return null;}
    string gameImage=game.Image(),frameImage=frame.Image();if(gameImage.Length==0||frameImage.Length==0||gameClass.Length is <1 or >=128)return null;
    var authority=new CurtainVisualAuthority(request.AppId,request.RequestEpoch,request.RequestId,request.SourceReceipt,game.Pid,(ulong)game.Birth,frame.Pid,(ulong)frame.Birth,
      thread,(ulong)gameWindow,steam.Pid,(ulong)steam.Birth,request.SteamThread,request.SteamWindow,gameRect.Array,steamRect.Array,steamClass,gameClass,steamImage,gameImage,aumid);
    var target=new ContentTarget((ulong)gameWindow,frame.Pid,frame.Birth,thread,frameImage,gameClass);
    var result=new LaunchCurtainRetainedSource(source,request,publisher,steam,game,frame,image,publisherFile,authority,target);
    if(!result.Current)return null;cancellation.ThrowIfCancellationRequested();
    source=null;publisher=steam=game=frame=null;image=publisherFile=null;return result;
   }finally{N.SetThreadDpiAwarenessContext(previous);}
  }catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or OverflowException or InvalidOperationException or System.ComponentModel.Win32Exception){return null;}
  finally{source?.Dispose();publisher?.Dispose();steam?.Dispose();game?.Dispose();frame?.Dispose();image?.Dispose();publisherFile?.Dispose();}
 }
 public bool Current{
  get{
   if(disposed||!gameSource.Current)return false;using var self=Process.GetCurrentProcess();uint session=(uint)self.SessionId;
   if(!publisher.Current(session)||!steam.Current(session)||!game.Current(session)||!frame.Current(session))return false;
   nint old=N.SetThreadDpiAwarenessContext(new(-4));if(old==0)return false;
   try{
    nint gw=(nint)visual.GameWindow,sw=(nint)visual.SteamWindow;
    return gameSource.ForegroundProof is {} proof&&proof.WindowHandle==gw&&proof.ProcessId==frame.Pid&&proof.ProcessBirth==frame.Birth
      &&N.GetWindowThreadProcessId(gw,out uint fp)==visual.FrameThread&&fp==frame.Pid&&Class(gw)==visual.GameClass&&N.GetAncestor(gw,2)==gw
      &&N.GetWindowRect(gw,out var gr)&&gr.Array.SequenceEqual(visual.GameRect)
      &&N.GetWindowThreadProcessId(sw,out uint sp)==visual.SteamThread&&sp==steam.Pid&&Class(sw)==visual.SteamClass&&N.GetAncestor(sw,2)==sw&&N.GetWindow(sw,4)==0
      &&N.GetWindowRect(sw,out var sr)&&sr.Array.SequenceEqual(visual.SteamRect)&&N.IsWindowVisible(sw)&&N.IsWindowEnabled(sw)&&!N.IsIconic(sw)
      &&gameSource.Current&&publisher.Current(session)&&steam.Current(session)&&frame.Current(session)&&game.Current(session)
      &&N.GetForegroundWindow()==gw;
   }finally{N.SetThreadDpiAwarenessContext(old);}
  }
 }
 public Task<bool> RefreshAssociationAsync(CancellationToken stop)=>gameSource is ILivePerformanceAssociationRefresh refresh?refresh.RefreshAssociationAsync(stop):Task.FromResult(Current);
 public void Dispose(){if(disposed)return;disposed=true;gameSource.Dispose();publisher.Dispose();steam.Dispose();game.Dispose();frame.Dispose();steamImagePin.Dispose();publisherImagePin.Dispose();}
 static string Class(nint window){var text=new StringBuilder(256);return N.GetClassName(window,text,text.Capacity)>0?text.ToString():"";}
 sealed class Retained:IDisposable{
  readonly SafeProcessHandle process;internal uint Pid{get;}internal long Birth{get;}
  internal Retained(uint pid,long birth){Pid=pid;Birth=birth;process=N.OpenProcess(0x101000,false,pid);if(process.IsInvalid)throw new System.ComponentModel.Win32Exception();}
  internal bool Current(uint session)=>!process.IsClosed&&!process.IsInvalid&&N.WaitForSingleObject(process,0)==0x102&&N.GetProcessId(process)==Pid
    &&N.GetProcessTimes(process,out long creation,out _,out _,out _)&&creation==Birth&&N.ProcessIdToSessionId(Pid,out uint actual)&&actual==session;
  internal string Image(){var text=new StringBuilder(32768);uint size=(uint)text.Capacity;return N.QueryFullProcessImageName(process,0,text,ref size)?text.ToString():"";}
  public void Dispose()=>process.Dispose();
 }
 static class N{
  [StructLayout(LayoutKind.Sequential)]internal struct Rect{internal int Left,Top,Right,Bottom;internal int[] Array=>[Left,Top,Right,Bottom];}
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint pid);
  [DllImport("kernel32.dll")]internal static extern uint GetProcessId(SafeProcessHandle process);
  [DllImport("kernel32.dll")]internal static extern uint WaitForSingleObject(SafeProcessHandle process,uint milliseconds);
  [DllImport("kernel32.dll")]internal static extern bool GetProcessTimes(SafeProcessHandle process,out long creation,out long exit,out long kernel,out long user);
  [DllImport("kernel32.dll")]internal static extern bool ProcessIdToSessionId(uint pid,out uint session);
  [DllImport("kernel32.dll",EntryPoint="QueryFullProcessImageNameW",CharSet=CharSet.Unicode)]internal static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref uint length);
  [DllImport("user32.dll")]internal static extern nint SetThreadDpiAwarenessContext(nint context);
  [DllImport("user32.dll")]internal static extern nint GetForegroundWindow();
  [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(nint window,out uint pid);
  [DllImport("user32.dll")]internal static extern nint GetAncestor(nint window,uint flags);
  [DllImport("user32.dll")]internal static extern nint GetWindow(nint window,uint flags);
  [DllImport("user32.dll",EntryPoint="GetClassNameW",CharSet=CharSet.Unicode)]internal static extern int GetClassName(nint window,StringBuilder name,int count);
  [DllImport("user32.dll")]internal static extern bool GetWindowRect(nint window,out Rect rect);
  [DllImport("user32.dll")]internal static extern bool IsWindowVisible(nint window);
  [DllImport("user32.dll")]internal static extern bool IsWindowEnabled(nint window);
  [DllImport("user32.dll")]internal static extern bool IsIconic(nint window);
 }
}
