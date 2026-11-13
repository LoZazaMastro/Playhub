// SPDX-License-Identifier: GPL-3.0-only
// TEMP prototype. Default OFF. No controller device or driver is hidden.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using WSGM.PackagedLaunch;
internal static class ExperimentalInputFence
{
 // Generated only after the deterministic native build; a path/RPC cannot
 // select another library, export or arbitrary remote data.
 internal static string DllPath=>Path.Combine(AppContext.BaseDirectory,"PlayhubInputFence.dll");
 internal const string DllSha="C2EDEFEC2BAE213A1C9CAEC6837E6E9FEF66ECC9248AC8680A6A3882B312DBF4";
 internal static string PermitPath=>Path.Combine(Path.GetTempPath(),"Playhub-Input-Fence-Root-OptIn.txt");
 internal static bool Permit(string text,ulong gameId,long gameBirth,DateTimeOffset now)
 {
  string[] p=text.Replace("\r","").TrimEnd('\n').Split('\n');
  return p.Length==3&&p[0]==DllSha&&ulong.TryParse(p[1],NumberStyles.None,CultureInfo.InvariantCulture,out ulong id)&&id==gameId
   &&DateTimeOffset.TryParseExact(p[2],"O",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var at)
   &&at.Offset==TimeSpan.Zero&&now>=at&&now-at<=TimeSpan.FromSeconds(120)
   &&gameBirth>=at.AddSeconds(-2).UtcDateTime.ToFileTimeUtc();
 }
 internal static IDisposable? TryStart(TrustedSteamFiles steam,ulong gameId,IRetainedSteamGameTarget target,
  Process parent,CancellationToken cancellation,Func<bool> steamEndpointCurrent)
 {
  if(!OptedIn(gameId,target.Creation))return null;
  // This entry is called ONLY from the companion's already authenticated
  // retained target path, not an HTTP/JSON proof or a stand-alone PID tool.
  var stop=CancellationTokenSource.CreateLinkedTokenSource(cancellation);
  var task=Task.Factory.StartNew(()=>Run(steam,gameId,target,parent,stop.Token,steamEndpointCurrent),stop.Token,TaskCreationOptions.LongRunning,TaskScheduler.Default);
  return new Worker(stop,task);
 }
 private static bool OptedIn(ulong gameId,long birth)
 {
  try
  {
   if(!File.Exists(PermitPath)||(File.GetAttributes(PermitPath)&FileAttributes.ReparsePoint)!=0)return false;
   using var permit=new FileStream(PermitPath,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(permit.Length>256)return false;using var reader=new StreamReader(permit,Encoding.ASCII);
   return Permit(reader.ReadToEnd(),gameId,birth,DateTimeOffset.UtcNow);
  }
  catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
 }
 private static void Run(TrustedSteamFiles steam,ulong gameId,IRetainedSteamGameTarget target,Process parent,CancellationToken cancel,Func<bool> steamEndpointCurrent)
 {
  try
  {
   bool Current()=>!cancel.IsCancellationRequested&&!parent.HasExited&&target.IsCurrent()&&steam.UnchangedRoot()&&steamEndpointCurrent();
   if((uint)gameId!=0x02000000||gameId>>32<0x80000000||!Current())return;
   using var self=Process.GetCurrentProcess();uint own=(uint)self.Id;long ownBirth=self.StartTime.ToFileTimeUtc();
   nint window=0;long until=Environment.TickCount64+120000;
   while(Current()&&Environment.TickCount64<until)
   {nint h=GetForegroundWindow();if(GetWindowThreadProcessId(h,out uint pid)!=0&&pid==target.Pid&&GetAncestor(h,2)==h){window=h;break;}if(cancel.WaitHandle.WaitOne(50))return;}
   if(window==0||!Current()||!OptedIn(gameId,target.Creation))return;
   string image=Image(target.ProcessHandle.DangerousGetHandle());
   if(image.Length is <1 or >=1023||DeniedPath(image,steam))return;
   using var pinnedRenderer=new FileStream(steam.ComponentPaths.Last(),FileMode.Open,FileAccess.Read,FileShare.Read);
   RendererImage renderer=Renderer(pinnedRenderer);
   string nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
   using var sink=new MappingSink();
   var identity=new FenceProducer.Identity(target.Pid,target.Creation,own,ownBirth,(uint)self.SessionId,(ulong)window,nonce,gameId);
   using var producer=new FenceProducer(identity,sink,Current,()=>Environment.TickCount64);
   producer.Update();
   using var pinned=new FileStream(DllPath,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(pinned.Length is <4096 or >1048576||Convert.ToHexString(SHA256.HashData(pinned))!=DllSha)return;
   pinned.Position=0;byte[] bytes=new byte[(int)pinned.Length];pinned.ReadExactly(bytes);
   uint rva=Export(bytes,"FenceAttach"),diagnosticRva=Export(bytes,"FenceReadback");if(rva==0||diagnosticRva==0)return;
   bool Allowed(int pid,nint handle)=>pid==(int)target.Pid&&Current()&&OptedIn(gameId,target.Creation)&&GetProcessId(handle)==target.Pid
     &&GetProcessTimes(handle,out long birth,out _,out _,out _)&&birth==target.Creation
     &&string.Equals(Image(handle),image,StringComparison.OrdinalIgnoreCase)
     &&ProcessIdToSessionId(target.Pid,out uint session)&&session==(uint)self.SessionId
     &&IsWow64Process2(handle,out ushort processArch,out ushort machine)&&processArch==0&&machine==0x8664
     &&GetProcessInformation(handle,7,out uint protection,4)&&protection==0xfffffffe
     &&NoKnownAntiCheatModules(handle)&&Current();
   nint process=NativeMethods.OpenProcess(NativeMethods.InjectorAccess|0x40,false,target.Pid);
   if(process==0)return;
   try
   {
    if(!Allowed((int)target.Pid,process))return;
    if(!DuplicateHandle(GetCurrentProcess(),GetCurrentProcess(),process,out nint remoteProducer,0x101000,false,0))return;
    if(!DuplicateHandle(GetCurrentProcess(),sink.Handle.DangerousGetHandle(),process,out nint remoteMap,4,false,0))
    {CloseRemote(process,remoteProducer);return;}
    var injector=new GameInjector(new PrivilegeJournal(),Allowed);
    bool unknown=false;
    try
    {
     if(!injector.PrototypeLoad((int)target.Pid,DllPath)){unknown=injector.Latched((int)target.Pid);return;}
     byte[] config=Configuration(identity,image,remoteProducer,remoteMap,renderer);
     uint? result=injector.PrototypeCallWithBytes((int)target.Pid,DllPath,rva,config);
     unknown=injector.Latched((int)target.Pid);
     if(result!=0){
      // A completed refusal permits only the same bounded passive diagnostic
      // read. Unknown remote work does not trigger a retry or another call.
      Diagnostic? refused=null;
      if(result.HasValue&&!unknown){nint rejectedAddress=DiagnosticAddress(process,diagnosticRva);if(rejectedAddress!=0)refused=ReadDiagnostic(process,rejectedAddress,identity,Current);}
      PackagedLaunchLog.Info($"Input fence prototype attach refused/unknown; result={result?.ToString("X8")??"unknown"}; stage={refused?.StageName??"not-observed"}; stageCode={refused?.AttachStage.ToString()??"unknown"}; nativeResult={refused?.AttachError.ToString("X8")??"unknown"}; rendererMode={refused?.RendererMode.ToString()??"unknown"}; diagnosticOnly=True; coverage=False.");return;
     }
     PackagedLaunchLog.Info("Input fence prototype attached; APIs=XInput1_4.GetState,ordinal100-required; trusted-Valve-caller=delegated-if-present,exact-absence-supported,GetKeystroke; WGI=False; GameInput=False; physical-SC-claimed=False.");
     uint previousPhase=uint.MaxValue,previousKind=uint.MaxValue,reportedObservations=uint.MaxValue;ulong previousGeneration=0;
     nint diagnostic=DiagnosticAddress(process,diagnosticRva);long nextDiagnostic=0;bool diagnosticUnknown=false;
     while(Current())
     {
      uint phase=producer.Update();bool changed=phase!=previousPhase||producer.PublicationKind!=previousKind||producer.PublicationGeneration!=previousGeneration;
      if(diagnostic!=0&&(changed||Environment.TickCount64>=nextDiagnostic))
      {
       nextDiagnostic=Environment.TickCount64+1000;
       var observed=ReadDiagnostic(process,diagnostic,identity,Current);
       if(observed is not null)
       {
        uint observations=observed.ObservationMask;
        if(changed||observations!=reportedObservations)
         PackagedLaunchLog.Info($"Input fence private readback: flags={observed.Flags}; stage={observed.StageName}; nativeResult={observed.AttachError:X8}; rendererMode={observed.RendererMode}; readyDiagnostic={observed.Ready}; nonValveState={observed.StateReads}; nonValveEx={observed.ExReads}; nonValveKey={observed.KeyReads}; valveDelegated={observed.ValveReads}; gameImageReads={observed.GameImageReads}; guideSeen={observed.GuideSeen}; filtered={observed.Captures}; diagnosticOnly=True; consumption-confirmed=False.");
        reportedObservations=observations;diagnosticUnknown=false;
       }
       else if(!diagnosticUnknown){PackagedLaunchLog.Info("Input fence private readback unavailable; diagnosticOnly=True; no new authority.");diagnosticUnknown=true;}
      }
      if(phase!=previousPhase||producer.PublicationKind!=previousKind||producer.PublicationGeneration!=previousGeneration)
      {
       PackagedLaunchLog.Info($"Input fence prototype publication: phase={phase}; kind={producer.PublicationKind}; generation={producer.PublicationGeneration}; gamePid={target.Pid}; APIs=XInput1_4; consumption-confirmed=False.");
       previousPhase=phase;previousKind=producer.PublicationKind;previousGeneration=producer.PublicationGeneration;
      }
      if(cancel.WaitHandle.WaitOne(25))break;
     }
    }
    finally
    {
     // Closing the anonymous section does not unmap a consumer's existing
     // view. Explicit terminal pass + expiry/producer handle death fail-open.
     // If a remote thread is unknown, its argument/handles are left intact.
     if(!unknown){CloseRemote(process,remoteMap);CloseRemote(process,remoteProducer);}
    }
   }
   finally{NativeMethods.CloseHandle(process);}
  }
  catch(Exception e){PackagedLaunchLog.Info("Input fence prototype unavailable: "+e.GetType().Name+"; no input capture claim.");}
 }
 internal sealed record RendererImage(string Path,byte[] Sha256,uint ImageSize,uint Timestamp);
 internal static RendererImage Renderer(FileStream file)
 {
  if(!System.IO.Path.GetFileName(file.Name).Equals("GameOverlayRenderer64.dll",StringComparison.OrdinalIgnoreCase)||!System.IO.Path.IsPathFullyQualified(file.Name)||file.Name.Length>=767)
   throw new InvalidDataException("Exact trusted renderer path required");
  file.Position=0;using var pe=new PEReader(file,PEStreamOptions.LeaveOpen);var h=pe.PEHeaders;
  if(h.CoffHeader.Machine!=System.Reflection.PortableExecutable.Machine.Amd64||h.PEHeader is null||h.PEHeader.SizeOfImage<4096)throw new InvalidDataException("Renderer PE identity unavailable");
  uint size=checked((uint)h.PEHeader.SizeOfImage),timestamp=unchecked((uint)h.CoffHeader.TimeDateStamp);
  file.Position=0;byte[] sha=SHA256.HashData(file);file.Position=0;return new(file.Name,sha,size,timestamp);
 }
 internal static byte[] Configuration(FenceProducer.Identity i,string image,nint owner,nint mapping,RendererImage renderer)
 {
  if(image.Length>=1023)throw new ArgumentOutOfRangeException(nameof(image));
  if(renderer.Path.Length>=767||!System.IO.Path.IsPathFullyQualified(renderer.Path)||!System.IO.Path.GetFileName(renderer.Path).Equals("GameOverlayRenderer64.dll",StringComparison.OrdinalIgnoreCase)
   ||renderer.Sha256.Length!=32||renderer.ImageSize<4096)throw new ArgumentException("Exact trusted renderer pin required");
  byte[] b=new byte[3728];void U32(int o,uint v)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o,4),v);
  void U64(int o,ulong v)=>BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(o,8),v);
  U32(0,3728);U32(4,2);Convert.FromHexString(i.AuthoritySession).CopyTo(b,8);U64(40,(ulong)i.GameBirth);U64(48,(ulong)i.ProducerBirth);U64(56,i.GameId);
  U32(64,i.GamePid);U32(68,i.ProducerPid);U32(72,i.WindowsSession);U64(80,i.GameWindow);U64(88,(ulong)owner);U64(96,(ulong)mapping);
  Encoding.Unicode.GetBytes(image).CopyTo(b,104);Encoding.Unicode.GetBytes(renderer.Path).CopyTo(b,2152);renderer.Sha256.CopyTo(b,3688);U32(3720,renderer.ImageSize);U32(3724,renderer.Timestamp);return b;
 }
 internal static uint Export(byte[] image,string name)
 {
  if(name is not ("FenceAttach" or "FenceReadback"))return 0;
  using var pe=new PEReader(new MemoryStream(image));var h=pe.PEHeaders;
  if(h.CoffHeader.Machine!=System.Reflection.PortableExecutable.Machine.Amd64||h.PEHeader is null)return 0;
  int Offset(int rva){foreach(var s in h.SectionHeaders)if(rva>=s.VirtualAddress&&rva-s.VirtualAddress<s.SizeOfRawData)
    return checked(s.PointerToRawData+rva-s.VirtualAddress);throw new InvalidDataException();}
  uint U32(int o)=>BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(o,4));ushort U16(int o)=>BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(o,2));
  var directory=h.PEHeader.ExportTableDirectory;if(directory.Size<40)return 0;int e=Offset(directory.RelativeVirtualAddress);
  uint count=U32(e+24);if(count>64)return 0;
  int names=Offset((int)U32(e+32)),ord=Offset((int)U32(e+36)),functions=Offset((int)U32(e+28));
  for(int n=0;n<count;n++){int o=Offset((int)U32(names+n*4)),end=Array.IndexOf(image,(byte)0,o,Math.Min(128,image.Length-o));
   if(end<0||Encoding.ASCII.GetString(image,o,end-o)!=name)continue;uint rva=U32(functions+U16(ord+n*2)*4);
   if(rva>=directory.RelativeVirtualAddress&&rva<(uint)(directory.RelativeVirtualAddress+directory.Size))return 0;
   return rva<(uint)h.PEHeader.SizeOfImage?rva:0;}
  return 0;
 }
 internal sealed record Diagnostic(uint Flags,uint StateReads,uint ExReads,uint KeyReads,uint ValveReads,uint GuideSeen,uint Captures,uint GameImageReads,uint AttachStage,uint AttachError,uint RendererMode)
 {
  internal bool Ready=>AttachStage==17&&AttachError==0&&(Flags&17)==1;
  internal string StageName=>AttachStage switch {1=>"identity",2=>"producer-handle",3=>"mapping-handle",4=>"mapping-view",5=>"xinput-module",6=>"required-exports",7=>"renderer-observation",8=>"renderer-file-open",9=>"renderer-file-pe",10=>"renderer-file-hash",11=>"renderer-module-pin",12=>"renderer-loaded-identity",13=>"game-image-range",14=>"minhook-initialize",15=>"minhook-create",16=>"minhook-enable",17=>"attached",18=>"renderer-changed",_=>"unknown"};
  internal uint ObservationMask=>(StateReads>0?1u:0)|(ExReads>0?2u:0)|(KeyReads>0?4u:0)|(ValveReads>0?8u:0)|(GuideSeen>0?16u:0)|(Captures>0?32u:0)|(GameImageReads>0?64u:0)|((Flags&16)<<8);
 }
 internal static Diagnostic? ParseDiagnostic(byte[] b,uint finalSequence,FenceProducer.Identity expected,long now)
 {
  if(b.Length!=128)return null;uint U32(int o)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o,4));ulong U64(int o)=>BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o,8));
  uint sequence=U32(12);ulong at=U64(104);uint flags=U32(72),stage=U32(112),error=U32(116),mode=U32(120);
  bool attached=(flags&1)!=0,revoked=(flags&16)!=0;
  uint modeFlags=mode==1?4u:mode==2?8u:0u;
  bool statusValid=mode<=2&&stage is >=1 and <=18&&U32(124)==0&&
   (attached ? flags==(3|modeFlags|(revoked?16u:0u))&&mode!=0&&(revoked?stage==18&&error!=0:stage==17&&error==0)
    : flags==(revoked?16u:0u)&&error!=0&&stage!=17&&(revoked?stage==18:true));
  if(U32(0)!=0x52464850||U32(4)!=2||U32(8)!=128||sequence==0||(sequence&1)!=0||sequence!=finalSequence
   ||!b.AsSpan(16,32).SequenceEqual(Convert.FromHexString(expected.AuthoritySession))||U64(48)!=(ulong)expected.GameBirth||U64(56)!=(ulong)expected.ProducerBirth
   ||U32(64)!=expected.GamePid||U32(68)!=expected.ProducerPid||!statusValid||now<0||at>(ulong)now||(ulong)now-at>1500)return null;
  return new(flags,U32(76),U32(80),U32(84),U32(88),U32(92),U32(96),U32(100),stage,error,mode);
 }
 static Diagnostic? ReadDiagnostic(nint process,nint address,FenceProducer.Identity expected,Func<bool> current)
  =>ReadDiagnostic(address,expected,current,(at,buffer)=>DiagnosticReadProcessMemory(process,at,buffer,(nuint)buffer.Length,out nuint read)&&read==(nuint)buffer.Length,()=>Environment.TickCount64);
 // Whole bounded reader seam. Diagnostic values are never passed to a producer.
 internal static Diagnostic? ReadDiagnostic(nint address,FenceProducer.Identity expected,Func<bool> current,Func<nint,byte[],bool> read,Func<long> clock)
 {
  for(int attempt=0;attempt<3&&current();attempt++)
  {
   byte[] bytes=new byte[128],sequence=new byte[4];
   if(!read(address,bytes)||!read(address+12,sequence)||!current())return null;
   var result=ParseDiagnostic(bytes,BinaryPrimitives.ReadUInt32LittleEndian(sequence),expected,clock());
   if(result is not null&&current())return ParseDiagnostic(bytes,BinaryPrimitives.ReadUInt32LittleEndian(sequence),expected,clock());
  }
  return null;
 }
 static nint DiagnosticAddress(nint process,uint rva)
 {
  nint[] modules=new nint[1024];
  if(!NativeMethods.K32EnumProcessModulesEx(process,modules,(uint)(modules.Length*nint.Size),out uint count,3)||count>modules.Length*nint.Size)return 0;
  nint result=0;
  for(int n=0;n<count/nint.Size;n++)
  {
   var path=new StringBuilder(32768);if(NativeMethods.K32GetModuleFileNameExW(process,modules[n],path,(uint)path.Capacity)==0)return 0;
   if(!path.ToString().Equals(DllPath,StringComparison.OrdinalIgnoreCase))continue;
   if(result!=0||!DiagnosticGetModuleInformation(process,modules[n],out var info,(uint)Marshal.SizeOf<DiagnosticModule>())||info.Base!=modules[n]||rva>info.Size||info.Size-rva<128)return 0;
   result=checked(modules[n]+(nint)rva);
  }
  return result;
 }
 [StructLayout(LayoutKind.Sequential)] struct DiagnosticModule {internal nint Base;internal uint Size;internal nint Entry;}
 [DllImport("kernel32.dll",EntryPoint="ReadProcessMemory")] static extern bool DiagnosticReadProcessMemory(nint process,nint address,[Out] byte[] buffer,nuint size,out nuint read);
 [DllImport("kernel32.dll",EntryPoint="K32GetModuleInformation")] static extern bool DiagnosticGetModuleInformation(nint process,nint module,out DiagnosticModule info,uint bytes);
 static string Image(nint process){var b=new StringBuilder(32768);uint n=(uint)b.Capacity;return NativeMethods.QueryFullProcessImageNameW(process,0,b,ref n)?b.ToString():"";}
 static bool DeniedPath(string path,TrustedSteamFiles steam)
 {
  string name=Path.GetFileName(path);string[] denied=["steam.exe","steamwebhelper.exe","GameOverlayUI.exe","steamservice.exe","dwm.exe","explorer.exe","csrss.exe","winlogon.exe","services.exe","lsass.exe","System"];
  return denied.Contains(name,StringComparer.OrdinalIgnoreCase)||path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
   ||steam.ComponentPaths.Contains(path,StringComparer.OrdinalIgnoreCase);
 }
 static bool NoKnownAntiCheatModules(nint process)
 {
  // A negative list is only an additional veto, NOT proof of universal
  // anti-cheat absence. Root must pre-review the opt-in target. Unknown reads
  // and protected processes refuse this experiment rather than bypassing it.
  nint[] modules=new nint[1024];if(!NativeMethods.K32EnumProcessModulesEx(process,modules,(uint)(modules.Length*nint.Size),out uint bytes,3)||bytes>modules.Length*nint.Size)return false;
  for(int n=0;n<bytes/nint.Size;n++){var b=new StringBuilder(32768);if(NativeMethods.K32GetModuleFileNameExW(process,modules[n],b,(uint)b.Capacity)==0)return false;
   string s=b.ToString();if(new[]{"easyanticheat","battleye","beclient","beservice","faceit","vanguard","vgk.sys","equ8"}.Any(v=>s.Contains(v,StringComparison.OrdinalIgnoreCase)))return false;}
  return true;
 }
 static void CloseRemote(nint target,nint value){if(DuplicateHandle(target,value,GetCurrentProcess(),out nint local,0,false,3)&&local!=0)NativeMethods.CloseHandle(local);}
 sealed class Worker(CancellationTokenSource stop,Task task):IDisposable {public void Dispose(){stop.Cancel();try{task.Wait(2000);}catch(AggregateException){}if(task.IsCompleted)stop.Dispose();}}
 [DllImport("kernel32.dll")] static extern nint GetCurrentProcess();
 [DllImport("kernel32.dll")] static extern uint GetProcessId(nint p);
 [DllImport("kernel32.dll")] static extern bool GetProcessTimes(nint p,out long a,out long b,out long c,out long d);
 [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint p,out uint session);
 [DllImport("kernel32.dll")] static extern bool IsWow64Process2(nint p,out ushort process,out ushort machine);
 [DllImport("kernel32.dll")] static extern bool GetProcessInformation(nint p,int kind,out uint value,uint bytes);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool DuplicateHandle(nint source,nint handle,nint target,out nint copy,uint access,bool inherit,uint options);
 [DllImport("user32.dll")] static extern nint GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h,out uint pid);
 [DllImport("user32.dll")] static extern nint GetAncestor(nint h,uint flags);
}
