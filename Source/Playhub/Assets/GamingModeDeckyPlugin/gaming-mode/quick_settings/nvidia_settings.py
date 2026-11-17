"""Global, single-setting NVIDIA DRS transactions (no per-application profiles).

Public IDs/layouts: NVIDIA/nvapi 51e5aae4bc40c3ccd3fb7d4dfd94256e7bfc496e.
MIT SDK notice is already bundled as licenses/NVIDIA-NVAPI.txt. No driver DLL
is bundled. Persistence readback does not establish a running game's behavior.
"""
import base64
import ctypes
import json
import os
import re
import threading

FEATURES = ("frame_limit", "vsync", "power")
VSYNC_VALUES = (0x60925292, 0x08416747, 0x47814940)
_invoke_lock = threading.Lock()

NATIVE = r'''
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
public sealed class NvidiaApiError : Exception {
 public readonly int Code;
 public NvidiaApiError(int code) : base("NVIDIA API status") { Code=code; }
}
public sealed class NvidiaSetting {
 public string Id; public uint Raw,DefaultRaw; public int Location;
 public bool Supported,DefaultValid,CurrentPredefined;
 public bool OwnOverride { get { return Supported && Location==0 && !CurrentPredefined; } }
 public bool Same(NvidiaSetting b) { return b!=null && Id==b.Id && Supported==b.Supported && Raw==b.Raw && DefaultRaw==b.DefaultRaw && Location==b.Location && DefaultValid==b.DefaultValid && CurrentPredefined==b.CurrentPredefined; }
}
public sealed class NvidiaSnapshot {
 public uint DriverVersion,GpuSupport,ProfilePredefined,ProfileSettings;
 public string Branch,ProfileName; public NvidiaSetting[] Settings;
 public NvidiaSetting Find(string id) { return Settings.FirstOrDefault(x=>x.Id==id); }
 public bool SameIdentity(NvidiaSnapshot b) { return b!=null && DriverVersion==b.DriverVersion && Branch==b.Branch && ProfileName==b.ProfileName && GpuSupport==b.GpuSupport && ProfilePredefined==b.ProfilePredefined; }
 public string Identity() { return Hash(false); }
 public string Token() { return Hash(true); }
 string Hash(bool settings) {
  using(var bytes=new MemoryStream()) using(var text=new BinaryWriter(bytes,Encoding.UTF8)) {
   text.Write(DriverVersion);text.Write(Branch??"");text.Write(ProfileName??"");text.Write(GpuSupport);text.Write(ProfilePredefined);
   if(settings) { text.Write(ProfileSettings); foreach(var s in Settings.OrderBy(x=>x.Id,StringComparer.Ordinal)) {
    text.Write(s.Id);text.Write(s.Supported);text.Write(s.Raw);text.Write(s.DefaultRaw);text.Write(s.Location);text.Write(s.DefaultValid);text.Write(s.CurrentPredefined);
   }}
   text.Flush();using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes.ToArray())).Replace("-","").ToLowerInvariant();
  }
 }
}
public sealed class NvidiaWriteReceipt { internal bool SaveAttempted; }
public interface INvidiaGlobalDrs {
 NvidiaSnapshot Read(string onlyFeature);
 bool WriteOne(NvidiaSnapshot expected,string feature,uint? wanted,NvidiaWriteReceipt receipt);
}
public sealed class NvidiaControl {
 public string id;public object value;public bool @default,supported,editable,custom;
 public int location;public bool isCurrentPredefined,isPredefinedValid,overridden;public object defaultValue;
 public uint[] values;public int? min,max;
}
public sealed class NvidiaProfile { public string name,identity; }
public sealed class NvidiaDriver { public uint version;public string branch; }
public sealed class NvidiaResult {
 public bool available,ok,global=true,restartGameRecommended=true;
 public string status,token,rollbackStatus="none",effectiveInRunningGame="unknown";
 public NvidiaProfile profile;public NvidiaDriver driver;public NvidiaControl[] settings;
}
public static class NvidiaGlobalTransaction {
 public static readonly string[] Features={"frame_limit","vsync","power"};
 public static bool Allowed(string feature,uint value) {
  return feature=="frame_limit" ? value==0 || (value>=20 && value<=1000) : feature=="vsync" ? value==0x60925292 || value==0x08416747 || value==0x47814940 : feature=="power" && value==1;
 }
 static bool KnownCurrent(string feature,uint value) { return feature=="power" ? value<=5 : Allowed(feature,value); }
 public static string Failure(Exception error) {
  var api=error as NvidiaApiError;if(api==null)return "unavailable";
  return api.Code==-137 ? "permission" : api.Code==-157 ? "driver-reload-required" : api.Code==-124 ? "reboot-required" : api.Code==-104 || api.Code==-160 || api.Code==-3 || api.Code==-6 ? "unsupported" : "failed";
 }
 public static NvidiaResult Result(NvidiaSnapshot state,string status,bool okay=false,string rollback="none") {
  if(state==null)return new NvidiaResult {available=false,ok=false,status=status,rollbackStatus=rollback,settings=new NvidiaControl[0]};
  return new NvidiaResult { available=true,ok=okay,status=status,rollbackStatus=rollback,token=state.Token(),profile=new NvidiaProfile {name=state.ProfileName,identity=state.Identity()},driver=new NvidiaDriver{version=state.DriverVersion,branch=state.Branch},
   settings=state.Settings.Select(s=>new NvidiaControl { id=s.Id,value=s.Supported?(object)s.Raw:null,@default=s.Supported&&!s.OwnOverride,supported=s.Supported,location=s.Location,isCurrentPredefined=s.CurrentPredefined,isPredefinedValid=s.DefaultValid,overridden=s.OwnOverride,defaultValue=s.Supported&&s.DefaultValid?(object)s.DefaultRaw:null,
    editable=s.Supported&&(!s.OwnOverride||KnownCurrent(s.Id,s.Raw)),custom=s.Supported&&s.OwnOverride&&!KnownCurrent(s.Id,s.Raw),values=s.Id=="vsync"?new uint[]{0x60925292,0x08416747,0x47814940}:s.Id=="power"?new uint[]{1}:null,
    min=s.Id=="frame_limit"?(int?)20:null,max=s.Id=="frame_limit"?(int?)1000:null }).ToArray() };
 }
 static bool Desired(NvidiaSetting current,uint? value) {
  return current!=null && current.Supported && (value.HasValue ? current.OwnOverride && current.Raw==value.Value : !current.OwnOverride);
 }
 static bool ProvenWrite(NvidiaSetting before,NvidiaSetting current,uint? value) {
  // A DWORD override has a fully predictable property fingerprint. Deleting
  // an override can reveal an externally changed inheritance chain; without
  // a confirmed post-save receipt it must never trigger a guessed rollback.
  return value.HasValue && Desired(current,value) && before.Id==current.Id && before.DefaultRaw==current.DefaultRaw && before.DefaultValid==current.DefaultValid;
 }
 public static NvidiaResult Run(INvidiaGlobalDrs drs,string expectedToken,string feature,uint? value,bool mutation) {
  NvidiaSnapshot before;
  try { before=drs.Read(null); } catch(Exception error) { return Result(null,Failure(error)); }
  if(!mutation)return Result(before,"ready");
  if(!Features.Contains(feature) || (value.HasValue && !Allowed(feature,value.Value)))return Result(before,"invalid");
  if(expectedToken==null || !String.Equals(before.Token(),expectedToken,StringComparison.Ordinal))return Result(before,"changed");
  var original=before.Find(feature);if(original==null || !original.Supported)return Result(before,"unsupported");
  if(value.HasValue && original.OwnOverride && !KnownCurrent(feature,original.Raw))return Result(before,"unsupported");
  if(Desired(original,value))return Result(before,"unchanged",true);
  var receipt=new NvidiaWriteReceipt();
  try {
   if(!drs.WriteOne(before,feature,value,receipt))return Result(drs.Read(null),"changed");
   var after=drs.Read(null);
   if(before.SameIdentity(after) && Desired(after.Find(feature),value))return Result(after,"persisted",true);
   return Result(after,"changed");
  } catch(Exception error) {
   // A failed save can already have committed. Inspect only this property and
   // identity before a conditional restore; never restore a whole profile.
   string failure=Failure(error);
   // A rejected comparison or pre-save error cannot confer ownership of a
   // matching value that another application may have written meanwhile.
   if(!receipt.SaveAttempted){try{return Result(drs.Read(null),failure);}catch{return Result(null,"lost-state");}}
   try {
    var current=drs.Read(feature);var target=current.Find(feature);
    if(!before.SameIdentity(current))return Result(drs.Read(null),"changed",false,"skipped-changed");
    if(original.Same(target))return Result(drs.Read(null),failure);
    if(!ProvenWrite(original,target,value))return Result(drs.Read(null),"changed",false,value.HasValue?"skipped-changed":"skipped-unproven");
    uint? restore=original.OwnOverride?(uint?)original.Raw:null;
    if(!drs.WriteOne(current,feature,restore,new NvidiaWriteReceipt()))return Result(drs.Read(null),"changed",false,"skipped-changed");
    var restored=drs.Read(feature);
    if(before.SameIdentity(restored) && original.Same(restored.Find(feature)))return Result(drs.Read(null),"restored",false,"restored");
    return Result(drs.Read(null),"changed",false,"failed");
   } catch { return Result(null,"lost-state",false,"lost-state"); }
  }
 }
}
public sealed class NvidiaGlobalDrs : INvidiaGlobalDrs,IDisposable {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Query(uint id);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Zero();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Driver(out uint version,StringBuilder branch);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Gpus([Out,MarshalAs(UnmanagedType.LPArray,SizeConst=64)] IntPtr[] handles,out uint count);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Create(out IntPtr session);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int One(IntPtr session);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Global(IntPtr session,out IntPtr profile);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Info(IntPtr session,IntPtr profile,IntPtr info);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Get(IntPtr session,IntPtr profile,uint id,IntPtr setting);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Set(IntPtr session,IntPtr profile,IntPtr setting);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Delete(IntPtr session,IntPtr profile,uint id);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi,ExactSpelling=true)] static extern IntPtr GetProcAddress(IntPtr module,string name);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="GetModuleFileNameW")] static extern uint GetModuleFileName(IntPtr module,StringBuilder path,int count);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
 readonly IntPtr module;readonly Query query;bool initialized;
 const int SettingSize=12320,ProfileSize=4116;
 static readonly Dictionary<string,uint> Ids=new Dictionary<string,uint>{{"frame_limit",0x10835002},{"vsync",0x00a879cf},{"power",0x1057eb71}};
 static void Check(int status){if(status!=0)throw new NvidiaApiError(status);}
 internal static void ValidateGpuCount(uint count){if(count==0 || count>64)throw new NvidiaApiError(-6);}
 T Function<T>(uint id) where T:class {
  var ptr=query(id);if(ptr==IntPtr.Zero)throw new NvidiaApiError(-3);
  return Marshal.GetDelegateForFunctionPointer(ptr,typeof(T)) as T;
 }
 public NvidiaGlobalDrs(string path) {
  if(IntPtr.Size!=8 || !Path.GetFullPath(path).Equals(Path.Combine(Environment.SystemDirectory,"nvapi64.dll"),StringComparison.OrdinalIgnoreCase))throw new IOException("System driver required");
  module=LoadLibraryEx(path,IntPtr.Zero,0x800);if(module==IntPtr.Zero)throw new IOException("Driver unavailable");
  try {
   var loaded=new StringBuilder(32768);if(GetModuleFileName(module,loaded,loaded.Capacity)==0 || !Path.GetFullPath(loaded.ToString()).Equals(Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))throw new IOException("Loaded driver differs");
   var ptr=GetProcAddress(module,"nvapi_QueryInterface");if(ptr==IntPtr.Zero)throw new NvidiaApiError(-3);
   query=(Query)Marshal.GetDelegateForFunctionPointer(ptr,typeof(Query));Check(Function<Zero>(0x0150e828)());initialized=true;
   uint count;var handles=new IntPtr[64];Check(Function<Gpus>(0xe5ac921f)(handles,out count));ValidateGpuCount(count);if(handles.Take((int)count).Any(x=>x==IntPtr.Zero))throw new NvidiaApiError(-6);
  } catch {try{if(initialized)Function<Zero>(0xd22bdd7e)();}finally{FreeLibrary(module);}throw;}
 }
 static IntPtr Buffer(int size){var memory=Marshal.AllocHGlobal(size);Marshal.Copy(new byte[size],0,memory,size);Marshal.WriteInt32(memory,size|(1<<16));return memory;}
 IntPtr Session(out IntPtr profile) {
  IntPtr session;Check(Function<Create>(0x0694d52e)(out session));
  try {Check(Function<One>(0x375dbd6b)(session));Check(Function<Global>(0x617bff9f)(session,out profile));return session;}
  catch {Function<One>(0xdad9cff8)(session);throw;}
 }
 NvidiaSnapshot Identity(IntPtr session,IntPtr profile) {
  uint version;var branch=new StringBuilder(64);Check(Function<Driver>(0x2926aaad)(out version,branch));var info=Buffer(ProfileSize);
  try {Check(Function<Info>(0x61cd6fd6)(session,profile,info));string name=Marshal.PtrToStringUni(IntPtr.Add(info,4),2048).TrimEnd('\0');
   if(name.Length==0 || name.IndexOf('\0')>=0)throw new IOException("Profile identity unavailable");
   return new NvidiaSnapshot {DriverVersion=version,Branch=branch.ToString(),ProfileName=name,GpuSupport=unchecked((uint)Marshal.ReadInt32(info,4100)),ProfilePredefined=unchecked((uint)Marshal.ReadInt32(info,4104)),ProfileSettings=unchecked((uint)Marshal.ReadInt32(info,4112))};
  }finally{Marshal.FreeHGlobal(info);}
 }
 NvidiaSetting ReadOne(IntPtr session,IntPtr profile,string feature) {
  var setting=Buffer(SettingSize);try {uint id=Ids[feature];Marshal.WriteInt32(setting,4100,unchecked((int)id));int status=Function<Get>(0x73bf8338)(session,profile,id,setting);
   if(status==-160 || status==-104)return new NvidiaSetting {Id=feature,Supported=false};Check(status);
   int type=Marshal.ReadInt32(setting,4104),location=Marshal.ReadInt32(setting,4108);
   if(Marshal.ReadInt32(setting)!=(SettingSize|(1<<16)) || unchecked((uint)Marshal.ReadInt32(setting,4100))!=id || type!=0 || location<0 || location>3)return new NvidiaSetting {Id=feature,Supported=false};
   return new NvidiaSetting {Id=feature,Supported=true,Raw=unchecked((uint)Marshal.ReadInt32(setting,8220)),DefaultRaw=unchecked((uint)Marshal.ReadInt32(setting,4120)),DefaultValid=Marshal.ReadInt32(setting,4116)!=0,CurrentPredefined=Marshal.ReadInt32(setting,4112)!=0,Location=location};
  }finally{Marshal.FreeHGlobal(setting);}
 }
 NvidiaSnapshot ReadAt(IntPtr session,IntPtr profile,string onlyFeature) {
  var result=Identity(session,profile);result.Settings=(onlyFeature==null?NvidiaGlobalTransaction.Features:new[]{onlyFeature}).Select(x=>ReadOne(session,profile,x)).ToArray();return result;
 }
 public NvidiaSnapshot Read(string onlyFeature) {IntPtr profile;var session=Session(out profile);try{return ReadAt(session,profile,onlyFeature);}finally{Function<One>(0xdad9cff8)(session);}}
 public bool WriteOne(NvidiaSnapshot expected,string feature,uint? wanted,NvidiaWriteReceipt receipt) {
  IntPtr profile;var session=Session(out profile);try {
   // Fresh load, identity and ONE-property CAS directly before ONE-property
   // mutation/save. DRS has no cross-process atomic merge guarantee.
   var fresh=ReadAt(session,profile,feature);
   if(!expected.SameIdentity(fresh) || !expected.Find(feature).Same(fresh.Find(feature)))return false;
   if(wanted.HasValue) {var setting=Buffer(SettingSize);try {Marshal.WriteInt32(setting,4100,unchecked((int)Ids[feature]));Marshal.WriteInt32(setting,8220,unchecked((int)wanted.Value));Check(Function<Set>(0x577dd202)(session,profile,setting));}finally{Marshal.FreeHGlobal(setting);}}
   else {int result=Function<Delete>(0xe4a26362)(session,profile,Ids[feature]);if(result!=0 && result!=-160)Check(result);}
   var save=Function<One>(0xfcbc7e14);receipt.SaveAttempted=true;Check(save(session));return true;
  }finally{Function<One>(0xdad9cff8)(session);}
 }
 public void Dispose(){try{if(initialized)Function<Zero>(0xd22bdd7e)();}finally{FreeLibrary(module);}}
}
'''


def _driver_present():
    """Cheap absence check only; authenticity is checked before any DLL load."""
    if os.name != "nt":
        return False
    try:
        kernel = ctypes.WinDLL("kernel32.dll", use_last_error=True)
        get_directory = kernel.GetSystemDirectoryW
        get_directory.argtypes = (ctypes.c_wchar_p, ctypes.c_uint)
        get_directory.restype = ctypes.c_uint
        buffer = ctypes.create_unicode_buffer(32768)
        count = get_directory(buffer, len(buffer))
        if not 0 < count < len(buffer):
            return False
        path = os.path.join(buffer.value, "nvapi64.dll")
        return os.path.isfile(path) and not os.path.islink(path)
    except (OSError, ValueError):
        return False


def invoke(run_cmd, powershell, token=None, feature=None, value=None, driver_probe=None):
    mutation = feature is not None
    if mutation and (feature not in FEATURES or not isinstance(token, str) or not re.fullmatch(r"[0-9a-f]{64}", token)):
        return {"available": False, "ok": False, "global": True, "status": "invalid", "settings": []}
    if mutation and (value != "default" and (type(value) is not int or not 0 <= value <= 0xffffffff)):
        return {"available": False, "ok": False, "global": True, "status": "invalid", "settings": []}
    if mutation and value != "default" and not (value in VSYNC_VALUES if feature == "vsync" else value == 1 if feature == "power" else value == 0 or 20 <= value <= 1000):
        return {"available": False, "ok": False, "global": True, "status": "invalid", "settings": []}
    if not (driver_probe or _driver_present)():
        return {"available": False, "ok": False, "global": True, "status": "unsupported", "settings": []}
    if not _invoke_lock.acquire(blocking=False):
        return {"available": False, "ok": False, "global": True, "status": "busy", "settings": []}
    try:
        return _invoke_driver(run_cmd, powershell, token, feature, value)
    finally:
        _invoke_lock.release()


def _invoke_driver(run_cmd, powershell, token, feature, value):
    payload = base64.b64encode(json.dumps({"token": token, "feature": feature, "value": value}, separators=(",", ":")).encode()).decode("ascii")
    script = r'''$ErrorActionPreference='Stop'
$driver=$null;$mutex=$null;$held=$false;$pin=$null;$detected=$false
try {
 $path=Join-Path ([Environment]::SystemDirectory) 'nvapi64.dll'
 if(-not [Environment]::Is64BitProcess -or -not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Unavailable'}
 if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unavailable'}
 $pin=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 $signature=Get-AuthenticodeSignature -LiteralPath $path
 if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=NVIDIA Corporation(?:,|$)'){throw 'Unavailable'}
 Add-Type -TypeDefinition @'
''' + NATIVE + "\n'@\n" + r'''
 $mutex=New-Object Threading.Mutex($false,'Local\Playhub.NvidiaShaderCache')
 try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true}
 if(-not $held){@{available=$false;ok=$false;global=$true;status='busy';settings=@()}|ConvertTo-Json -Compress;exit}
 $request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('PAYLOAD'))|ConvertFrom-Json
 $driver=New-Object -TypeName NvidiaGlobalDrs -ArgumentList $path
 $detected=$true
 $wanted=$null;if($request.feature -and $request.value -ne 'default'){$wanted=[uint32]$request.value}
 $result=[NvidiaGlobalTransaction]::Run($driver,$request.token,$request.feature,$wanted,[bool]$request.feature)
 $result|Add-Member -NotePropertyName detected -NotePropertyValue $detected
 $result|ConvertTo-Json -Depth 6 -Compress
}catch{$failure=$_.Exception;while($failure.InnerException){$failure=$failure.InnerException};$status='unavailable';if($failure.GetType().Name -eq 'NvidiaApiError'){$status=[NvidiaGlobalTransaction]::Failure($failure)};@{available=$false;ok=$false;global=$true;detected=$detected;status=$status;settings=@()}|ConvertTo-Json -Compress}
finally{if($driver){$driver.Dispose()};if($pin){$pin.Dispose()};if($held){$mutex.ReleaseMutex()};if($mutex){$mutex.Dispose()}}
'''
    encoded = base64.b64encode(script.replace("PAYLOAD", payload).encode("utf-16-le")).decode("ascii")
    code, output, _ = run_cmd([powershell(), "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], timeout=12)
    try:
        result = json.loads(output.lstrip("\ufeff")) if code == 0 else None
    except (ValueError, TypeError):
        result = None
    return result if isinstance(result, dict) else {"available": False, "ok": False, "global": True, "status": "unavailable", "settings": []}
