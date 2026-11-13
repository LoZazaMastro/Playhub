// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace Playhub.FirstContent;
internal sealed record CurtainNativeConfiguration(string NativePath,string NativeSha256,string CallbackPath,string CallbackSha256,string OwnerSha256,string RuntimeRoot);
internal sealed record CurtainVisualAuthority(uint AppId,ulong RequestEpoch,string RequestId,string SourceReceipt,uint GamePid,ulong GameBirth,
 uint FramePid,ulong FrameBirth,uint FrameThread,ulong GameWindow,uint SteamPid,ulong SteamBirth,uint SteamThread,ulong SteamWindow,
 int[] GameRect,int[] SteamRect,string SteamClass,string GameClass,string SteamImage,string GameImage,string ExpectedFrameAumid);
// Only the retained service source builds this private wire. The HTTP request
// never contains game/frame PID, image, rect, generation, owner or wire bytes.
internal interface ICurtainNativeDisplay:IDisposable
{
 ulong ActiveGeneration{get;}
 CurtainNativeDisplay.WireSnapshot? Snapshot();
 bool Publish(CurtainVisualAuthority source,long expiresAt);
 void Close();
}
internal sealed class CurtainNativeDisplay:ICurtainNativeDisplay
{
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct WirePulse
 {
  internal uint Size,Version,Purpose,AppId,OwnerPid,GamePid,FramePid,FrameThread,SteamPid,SteamThread,Session,Reserved;
  internal ulong Generation,RequestEpoch,OwnerBirth,GameBirth,FrameBirth,GameWindow,SteamBirth,SteamWindow,Sequence,ObservedAt,ExpiresAt;
  [MarshalAs(UnmanagedType.ByValArray,SizeConst=4)]internal int[] GameRect;
  [MarshalAs(UnmanagedType.ByValArray,SizeConst=4)]internal int[] SteamRect;
  [MarshalAs(UnmanagedType.ByValArray,SizeConst=33)]internal byte[] RequestId;
  [MarshalAs(UnmanagedType.ByValArray,SizeConst=33)]internal byte[] SourceReceipt;
  [MarshalAs(UnmanagedType.ByValArray,SizeConst=65)]internal byte[] OwnerBuild;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]internal string SteamClass;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]internal string GameClass;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=4096)]internal string SteamImage;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=4096)]internal string GameImage;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=513)]internal string ExpectedFrameAumid;
 }
 [StructLayout(LayoutKind.Sequential)]internal struct WireSnapshot
 {
  internal uint Size,Version,Phase,CleanupComplete,Stage,Error;internal int Result;internal uint Flags;
  internal ulong Generation,Destination,ObservedAt;
 }
 [UnmanagedFunctionPointer(CallingConvention.Winapi,CharSet=CharSet.Unicode)]delegate int Configure(string callback,string root);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate int Queue(uint action,in WirePulse pulse,uint bytes);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate int Read(out WireSnapshot snapshot,uint bytes);
 readonly Queue queue;readonly Read read;readonly FileStream nativePin,callbackPin,ownerPin;
 readonly uint pid,session;readonly ulong birth;readonly string ownerHash;
 static readonly List<CurtainNativeDisplay> PinnedWorkers=[];
 WirePulse? active;CurtainVisualAuthority? authority;ulong generation,sequence;bool disposed;
 public ulong ActiveGeneration=>active?.Generation??0;
 static bool Hex(string value,int size)=>value.Length==size&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
 static FileStream Pin(string path,string expected,long limit=20971520)
 {if(!Path.IsPathFullyQualified(path)||!Hex(expected,64))throw new InvalidDataException("Exact display pin required");
  var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);try{if(stream.Length<4096||stream.Length>limit||!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Display component pin mismatch");return stream;}catch{stream.Dispose();throw;}}
 internal CurtainNativeDisplay(CurtainNativeConfiguration config)
 {
  if(Marshal.SizeOf<WirePulse>()!=18736||Marshal.SizeOf<WireSnapshot>()!=56)throw new InvalidDataException("Curtain ABI mismatch");
  using var self=Process.GetCurrentProcess();pid=(uint)self.Id;birth=(ulong)self.StartTime.ToFileTimeUtc();session=(uint)self.SessionId;ownerHash=config.OwnerSha256;
  FileStream? native=null,callback=null,owner=null;nint module=0;bool configured=false;
  try{
   native=Pin(config.NativePath,config.NativeSha256);callback=Pin(config.CallbackPath,config.CallbackSha256);owner=Pin(Environment.ProcessPath??throw new IOException("Owner path unavailable"),ownerHash,536870912);
   module=NativeLibrary.Load(Path.GetFullPath(config.NativePath));var found=new StringBuilder(32768);
   if(GetModuleFileName(module,found,found.Capacity)==0||!Path.GetFullPath(config.NativePath).Equals(Path.GetFullPath(found.ToString()),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Loaded display path differs");
   queue=Marshal.GetDelegateForFunctionPointer<Queue>(NativeLibrary.GetExport(module,"QueueCurtainDisplayV1"));
   read=Marshal.GetDelegateForFunctionPointer<Read>(NativeLibrary.GetExport(module,"ReadCurtainDisplayV1"));
   var configure=Marshal.GetDelegateForFunctionPointer<Configure>(NativeLibrary.GetExport(module,"ConfigureCurtainDisplayV1"));
   Directory.CreateDirectory(config.RuntimeRoot);if(configure(config.CallbackPath,config.RuntimeRoot)!=1)throw new IOException("Curtain worker configuration refused");
   configured=true;nativePin=native;callbackPin=callback;ownerPin=owner;lock(PinnedWorkers)PinnedWorkers.Add(this);
  }catch{if(module!=0&&!configured)NativeLibrary.Free(module);owner?.Dispose();callback?.Dispose();native?.Dispose();throw;}
 }
 internal static CurtainNativeDisplay? FromAssemblyPins()
 {
  var values=Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x=>x.Key,x=>x.Value);
  if(!values.TryGetValue("PlayhubCurtainDisplaySha256",out var native)||!values.TryGetValue("PlayhubCurtainCallbackSha256",out var callback)||native is null||callback is null)return null;
  string own;using(var file=File.OpenRead(Environment.ProcessPath??throw new IOException("Owner path unavailable")))own=Convert.ToHexString(SHA256.HashData(file));
  string root=AppContext.BaseDirectory;return new(new(Path.Combine(root,"PlayhubCurtainDisplayNative.dll"),native,Path.Combine(root,"PlayhubExactBand.dll"),callback,own,
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Playhub","CurtainDisplay")));
 }
 static byte[] Ascii(string value,int size){if(!Hex(value,size-1))throw new InvalidDataException("Curtain identifier differs");byte[] bytes=new byte[size];Encoding.ASCII.GetBytes(value,bytes);return bytes;}
 internal static WirePulse Encode(CurtainVisualAuthority a,ulong generation,ulong sequence,uint pid,ulong birth,uint session,string hash,long now,long until)
 {
  if(a.AppId==0||a.RequestEpoch==0||a.GamePid==0||a.GameBirth==0||a.FramePid==0||a.FrameBirth==0||a.FrameThread==0||a.GameWindow==0||a.SteamPid==0||a.SteamBirth==0||a.SteamThread==0||a.SteamWindow==0
    ||a.GameRect.Length!=4||a.SteamRect.Length!=4||a.GameClass.Length is <1 or >=128||a.SteamClass.Length is <1 or >=256||a.ExpectedFrameAumid.Length>512
    ||!Path.IsPathFullyQualified(a.GameImage)||!Path.IsPathFullyQualified(a.SteamImage)||a.GameImage.Length>=4096||a.SteamImage.Length>=4096
    ||now<=0||until<=now||until-now>1500||generation==0||sequence==0)throw new InvalidDataException("Curtain source identity unavailable");
  return new(){Size=18736,Version=1,Purpose=2,AppId=a.AppId,OwnerPid=pid,OwnerBirth=birth,Session=session,Generation=generation,RequestEpoch=a.RequestEpoch,
   GamePid=a.GamePid,GameBirth=a.GameBirth,FramePid=a.FramePid,FrameBirth=a.FrameBirth,FrameThread=a.FrameThread,GameWindow=a.GameWindow,
   SteamPid=a.SteamPid,SteamBirth=a.SteamBirth,SteamThread=a.SteamThread,SteamWindow=a.SteamWindow,Sequence=sequence,ObservedAt=(ulong)now,ExpiresAt=(ulong)until,
   GameRect=(int[])a.GameRect.Clone(),SteamRect=(int[])a.SteamRect.Clone(),RequestId=Ascii(a.RequestId,33),SourceReceipt=Ascii(a.SourceReceipt,33),OwnerBuild=Ascii(hash,65),
   SteamClass=a.SteamClass,GameClass=a.GameClass,SteamImage=a.SteamImage,GameImage=a.GameImage,ExpectedFrameAumid=a.ExpectedFrameAumid};
 }
 public WireSnapshot? Snapshot()=>read(out var value,56)==1&&value.Size==56&&value.Version==1?value:null;
 public bool Publish(CurtainVisualAuthority source,long expiresAt)
 {
  if(disposed)return false;long now=Environment.TickCount64;
  if(authority is not null&&!Same(authority,source)){Close();return false;}
  uint action=2;if(active is null){if(Snapshot() is not {Phase:0,CleanupComplete:1})return false;action=1;++generation;}
  var pulse=Encode(source,generation,++sequence,pid,birth,session,ownerHash,now,expiresAt);
  if(queue(action,in pulse,18736)!=1){Close();return false;}active=pulse;authority=source;return true;
 }
 internal static bool Same(CurtainVisualAuthority a,CurtainVisualAuthority b)=>a with{GameRect=b.GameRect,SteamRect=b.SteamRect}==b&&a.GameRect.SequenceEqual(b.GameRect)&&a.SteamRect.SequenceEqual(b.SteamRect);
 public void Close(){if(active is {} last){queue(3,in last,18736);active=null;authority=null;}}
 public void Dispose(){if(disposed)return;Close();disposed=true;GC.KeepAlive(nativePin);GC.KeepAlive(callbackPin);GC.KeepAlive(ownerPin);}
 [DllImport("kernel32.dll",EntryPoint="GetModuleFileNameW",CharSet=CharSet.Unicode)]static extern uint GetModuleFileName(nint module,StringBuilder output,int count);
}
