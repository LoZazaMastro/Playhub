// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace GamingMode.Services;

internal sealed record HudDisplayConfiguration(string NativePath,string NativeSha256,string CallbackPath,
    string CallbackSha256,string AgentSha256,string RuntimeRoot);
// Only the real WPF producer uses this class. No frontend/HTTP method accepts
// Configuration or WirePulse. The default (no assembly pins) is disabled.
internal sealed class LivePerformanceHudNativeDisplay : IDisposable {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct WirePulse {
        internal uint Size,Version,Purpose,AppId,AgentPid,GamePid,FramePid,FrameThread,SourceThread,Session,Enabled,Suspended,Ready,Reserved;
        internal ulong Generation,CollectorEpoch,AgentBirth,GameBirth,FrameBirth,GameWindow,WpfWindow,WpfInstance,Sequence,SampleTick,ObservedAt,LeaseExpiresAt;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=4)]internal int[] GameRect;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=4)]internal int[] WpfRect;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=33)]internal byte[] CollectorLease;
        [MarshalAs(UnmanagedType.ByValArray,SizeConst=65)]internal byte[] OwnerBuild;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]internal string WpfClass;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=513)]internal string ExpectedFrameAumid;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct WireSnapshot {
        internal uint Size,Version,Phase,CleanupComplete,Stage,Error;internal int Hresult;internal uint Flags;
        internal ulong Generation,Source,Destination,ShownSequence,ObservedAt,DwmUpdates;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi,CharSet=CharSet.Unicode)]private delegate int Configure(string callback,string root);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate int Queue(uint action,in WirePulse pulse,uint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]private delegate int Read(out WireSnapshot value,uint bytes);
    private static readonly System.Collections.Generic.List<LivePerformanceHudNativeDisplay> PinnedWorkers = new();
    private readonly Queue _queue;private readonly Read _read;private readonly FileStream _nativePin,_callbackPin,_agentPin;
    private readonly string _agentSha;private readonly uint _pid,_session;private readonly ulong _birth;
    private readonly HudDisplayReadyCache _readyCache = new();
    private WirePulse? _active;private ulong _generation;private bool _disposed,_retired;private string? _failure;
    private HudNativeDisplayReadback _copy=new(0,0,true,0,0,0,0,"0","0",0,0,false,false,false,false,false,null);
    internal HudNativeDisplayReadback CopyDiagnostics()=>System.Threading.Volatile.Read(ref _copy);
    private static FileStream Pin(string path,string expected){if(expected.Length!=64||expected.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Missing exact build pin.");var file=new FileStream(Path.GetFullPath(path),FileMode.Open,FileAccess.Read,FileShare.Read);try{if(!Convert.ToHexString(SHA256.HashData(file)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Display component digest differs.");return file;}catch{file.Dispose();throw;}}
    internal LivePerformanceHudNativeDisplay(HudDisplayConfiguration config){
        if(Marshal.SizeOf<WirePulse>()!=1824||Marshal.SizeOf<WireSnapshot>()!=80)throw new InvalidDataException("Display ABI differs.");
        using var own=Process.GetCurrentProcess();_pid=(uint)own.Id;_birth=(ulong)own.StartTime.ToFileTimeUtc();_session=(uint)own.SessionId;_agentSha=config.AgentSha256;
        FileStream? native=null,callback=null,agent=null;nint module=0;bool configured=false;
        try{native=Pin(config.NativePath,config.NativeSha256);callback=Pin(config.CallbackPath,config.CallbackSha256);agent=Pin(Environment.ProcessPath??throw new InvalidDataException("Agent path unavailable."),config.AgentSha256);module=NativeLibrary.Load(Path.GetFullPath(config.NativePath));var path=new StringBuilder(32768);if(Native.GetModuleFileName(module,path,path.Capacity)==0||!Path.GetFullPath(config.NativePath).Equals(Path.GetFullPath(path.ToString()),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Loaded display module path differs.");
            _queue=Marshal.GetDelegateForFunctionPointer<Queue>(NativeLibrary.GetExport(module,"QueueHudDisplayV1"));_read=Marshal.GetDelegateForFunctionPointer<Read>(NativeLibrary.GetExport(module,"ReadHudDisplayV1"));var configure=Marshal.GetDelegateForFunctionPointer<Configure>(NativeLibrary.GetExport(module,"ConfigureHudDisplayV1"));Directory.CreateDirectory(config.RuntimeRoot);if(configure(Path.GetFullPath(config.CallbackPath),Path.GetFullPath(config.RuntimeRoot))!=1)throw new InvalidDataException("Display configuration refused.");configured=true;_nativePin=native;_callbackPin=callback;_agentPin=agent;lock(PinnedWorkers)PinnedWorkers.Add(this);
        }catch{if(module!=0&&!configured)NativeLibrary.Free(module);agent?.Dispose();callback?.Dispose();native?.Dispose();throw;}
        // Configure owns a worker module reference. No FreeLibrary while its
        // callback may still retire; final resources live until Agent exits.
    }
    internal static LivePerformanceHudNativeDisplay? FromAssemblyPins(){
        var pins=Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(x=>x.Key,x=>x.Value);
        if(!pins.TryGetValue("PlayhubHudDisplaySha256",out string? native)||!pins.TryGetValue("PlayhubHudDisplayCallbackSha256",out string? callback)||native is null||callback is null)return null;
        string agent;using(var image=File.OpenRead(Environment.ProcessPath??throw new InvalidDataException("Agent path unavailable.")))agent=Convert.ToHexString(SHA256.HashData(image));
        string root=AppContext.BaseDirectory;return new(new(Path.Combine(root,"PlayhubHudDisplayNative.dll"),native,Path.Combine(root,"PlayhubExactBand.dll"),callback,agent,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Playhub","HudDisplay")));
    }
    private static byte[] Ascii(string value,int bytes){var result=new byte[bytes];if(Encoding.ASCII.GetByteCount(value)!=bytes-1)throw new InvalidDataException("Invalid display identity.");Encoding.ASCII.GetBytes(value,result);return result;}
    internal void Publish(LivePerformanceHudSubscriptionProof proof,LiveFrameTelemetry sample,nint wpf,long instance,string? cls,uint sourceThread,LivePerformanceHudForegroundProof? foreground){
        if(_disposed)return;
        try{
            if(!proof.Enabled||proof.Suspended||!proof.LeaseCurrent||!proof.SourceResolved||proof.Lease is null||!Guid.TryParseExact(proof.Lease,"N",out _)||proof.Epoch<=0||instance<=0||wpf==0||cls is null){Close();return;}
            nint game=Native.GetForegroundWindow();uint frameThread=Native.GetWindowThreadProcessId(game,out uint framePid);
            if(frameThread==0||!Native.GetWindowRect(game,out var gameRect)||!Native.GetWindowRect(wpf,out var wpfRect)){Close();return;}
            string context=FormattableString.Invariant($"{game}:{framePid}:{frameThread}:{gameRect.Left},{gameRect.Top},{gameRect.Right},{gameRect.Bottom}|{wpf}:{instance}:{sourceThread}:{cls}:{wpfRect.Left},{wpfRect.Top},{wpfRect.Right},{wpfRect.Bottom}");
            bool actualReady=sample.Status==FrameMeasurementStatus.Ready;
            var selected = _readyCache.Select(proof, sample, Environment.TickCount64, context);if(selected is null){Close();return;}sample=selected;
            ulong frameBirth=framePid==(uint)sample.ProcessId?(ulong)sample.ProcessBirth:foreground is not null&&foreground.WindowHandle==game&&foreground.ProcessId==framePid?(ulong)foreground.ProcessBirth:0;
            if(frameBirth==0){Close();return;}
            var value=new WirePulse{Size=1824,Version=1,Purpose=1,AppId=proof.AppId,AgentPid=_pid,GamePid=(uint)sample.ProcessId,FramePid=framePid,FrameThread=frameThread,SourceThread=sourceThread,Session=_session,Enabled=1,Ready=1,CollectorEpoch=(ulong)proof.Epoch,AgentBirth=_birth,GameBirth=(ulong)sample.ProcessBirth,FrameBirth=frameBirth,GameWindow=(ulong)game,WpfWindow=(ulong)wpf,WpfInstance=(ulong)instance,Sequence=(ulong)sample.Sequence,SampleTick=(ulong)sample.UpdatedAtTick,ObservedAt=(ulong)Environment.TickCount64,LeaseExpiresAt=(ulong)proof.DeadlineTick,GameRect=gameRect.Array,WpfRect=wpfRect.Array,CollectorLease=Ascii(proof.Lease,33),OwnerBuild=Ascii(_agentSha,65),WpfClass=cls,ExpectedFrameAumid=""};
            if(_active is { } old&&!SameIdentity(old,value)){Close();_retired=false;if(!actualReady)return;_readyCache.Select(proof,sample,Environment.TickCount64,context);}
            if(_active is null){if(_retired)return;if(_read(out var prior,80)!=1||prior.Phase!=0||prior.CleanupComplete!=1)return;value.Generation=++_generation;if(_queue(1,in value,1824)!=1){_retired=true;_failure="NativeBeginRefused";return;}_active=value;}
            else{value.Generation=_active.Value.Generation;if(_queue(2,in value,1824)!=1){Close();_retired=true;_failure="NativePulseRefused";return;}_active=value;}
            Observe();
        }catch(Exception error){_failure=error.GetType().Name;Close();_retired=true;}
    }
    private static bool SameIdentity(WirePulse a,WirePulse b)=>a.CollectorEpoch==b.CollectorEpoch&&a.WpfWindow==b.WpfWindow&&a.WpfInstance==b.WpfInstance&&a.GamePid==b.GamePid&&a.GameBirth==b.GameBirth&&a.FramePid==b.FramePid&&a.FrameBirth==b.FrameBirth&&a.GameWindow==b.GameWindow&&a.AppId==b.AppId&&a.SourceThread==b.SourceThread&&a.FrameThread==b.FrameThread&&a.WpfClass==b.WpfClass&&a.CollectorLease.SequenceEqual(b.CollectorLease)&&a.GameRect.SequenceEqual(b.GameRect)&&a.WpfRect.SequenceEqual(b.WpfRect);
    private void Observe(){if(_read(out var s,80)!=1)return;if(_active is { } active&&s.Generation==active.Generation&&s.Phase==0&&s.CleanupComplete==1)_retired=true;System.Threading.Volatile.Write(ref _copy,new((long)s.ObservedAt,s.Phase,s.CleanupComplete==1,s.Stage,s.Error,s.Hresult,s.Generation,s.Source.ToString("X",CultureInfo.InvariantCulture),s.Destination.ToString("X",CultureInfo.InvariantCulture),s.ShownSequence,s.DwmUpdates,(s.Flags&1)!=0,(s.Flags&2)!=0,(s.Flags&4)!=0,(s.Flags&8)!=0,(s.Flags&16)!=0,_failure));}
    internal void Close(){_readyCache.Clear();if(_active is { } active){_queue(3,in active,1824);_active=null;} _retired=false;Observe();}
    public void Dispose(){if(_disposed)return;Close();_disposed=true;/* Native worker pins module and files until process exit; no false unload claim. */GC.KeepAlive(_nativePin);GC.KeepAlive(_callbackPin);GC.KeepAlive(_agentPin);}
    private static class Native {
        [StructLayout(LayoutKind.Sequential)]internal struct Rect {internal int Left,Top,Right,Bottom;internal int[] Array=>new[]{Left,Top,Right,Bottom};}
        [DllImport("user32.dll")]internal static extern nint GetForegroundWindow();
        [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(nint w,out uint pid);
        [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool GetWindowRect(nint w,out Rect r);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="GetModuleFileNameW")]internal static extern uint GetModuleFileName(nint module,StringBuilder text,int size);
    }
}

// Busy carries no new frame. Preserve the real Ready timestamp/sequence only in
// the same subscription epoch; no synthetic frame or observed tick is invented.
internal sealed class HudDisplayReadyCache {
 private LiveFrameTelemetry? _ready;private string? _lease,_context;private long _epoch;
 internal void Clear(){_ready=null;_lease=_context=null;_epoch=0;}
 internal LiveFrameTelemetry? Select(LivePerformanceHudSubscriptionProof proof,LiveFrameTelemetry sample,long now,string context="same"){
  if(!proof.Enabled||proof.Suspended||!proof.LeaseCurrent||!proof.SourceResolved||proof.Lease is null||proof.Epoch<=0||proof.DeadlineTick<=now){Clear();return null;}
  if(sample.Status==FrameMeasurementStatus.Ready&&sample.ProcessId>0&&sample.ProcessBirth>0&&sample.Sequence>0&&sample.UpdatedAtTick>0&&sample.UpdatedAtTick<=now&&now-sample.UpdatedAtTick<500){_ready=sample;_lease=proof.Lease;_epoch=proof.Epoch;_context=context;return sample;}
  if(sample.Status==FrameMeasurementStatus.Busy&&_ready is { } old&&proof.Lease==_lease&&proof.Epoch==_epoch&&context==_context&&old.UpdatedAtTick<=now&&now-old.UpdatedAtTick<500
    &&(sample.ProcessId==0||(sample.ProcessId==old.ProcessId&&sample.ProcessBirth==old.ProcessBirth)))return old;
  Clear();return null;
 }
}
