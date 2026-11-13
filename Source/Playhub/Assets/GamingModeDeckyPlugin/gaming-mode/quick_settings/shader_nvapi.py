"""Bounded access to NVIDIA's public DRS shader-cache settings.

Interface constants/layout: NVIDIA/nvapi 51e5aae4bc40c3ccd3fb7d4dfd94256e7bfc496e
(MIT; full notice in licenses/NVIDIA-NVAPI.txt). The installed signed driver is
used; no NVIDIA binary or private NVIDIA App interface is redistributed.
"""
import base64
import json

LIMITS = (1024, 4096, 10240, 16384, 102400, 0xffffffff)

NATIVE = r'''
using System;
using System.Runtime.InteropServices;
public sealed class ShaderState {
 public bool Enabled; public uint Limit; public uint DefaultLimit; public bool EnableDefault; public bool LimitDefault;
 public bool Same(ShaderState b) { return b!=null && Enabled==b.Enabled && Limit==b.Limit && DefaultLimit==b.DefaultLimit && EnableDefault==b.EnableDefault && LimitDefault==b.LimitDefault; }
}
public interface IShaderDrs { ShaderState Read(); bool Write(ShaderState expected, ShaderState wanted); }
public sealed class ShaderResult { public bool available; public string status; public ShaderState state; }
public static class ShaderTransaction {
 public static ShaderResult Run(IShaderDrs driver, ShaderState expected, bool enabled, uint limit, bool useDefault) {
  var before=driver.Read();
  if(expected==null) return new ShaderResult { available=true,status="ready",state=before };
  if(!before.Same(expected)) return new ShaderResult { available=true,status="changed",state=before };
  var desired=new ShaderState { Enabled=enabled,Limit=useDefault?before.DefaultLimit:limit,DefaultLimit=before.DefaultLimit,
   EnableDefault=before.EnableDefault && enabled==before.Enabled,LimitDefault=useDefault };
  if(desired.Same(before)) return new ShaderResult { available=true,status="applied",state=before };
  try {
   if(!driver.Write(before,desired)) return new ShaderResult { available=true,status="changed",state=driver.Read() };
   var after=driver.Read();
   if(after.Same(desired)) return new ShaderResult { available=true,status="applied",state=after };
   return new ShaderResult { available=true,status="changed",state=after };
  } catch {
   // A failed save may already have committed. Restore only our complete
   // value, and let Write repeat its fresh comparison before saving again.
   try { var current=driver.Read(); if(current.Same(before)) return new ShaderResult { available=true,status="failed",state=current };
    if(current.Same(desired) && driver.Write(desired,before)) {
     var restored=driver.Read(); return new ShaderResult { available=true,status=restored.Same(before)?"restored":"changed",state=restored };
    }
    return new ShaderResult { available=true,status="changed",state=current };
   } catch { return new ShaderResult { available=false,status="unavailable" }; }
  }
 }
}
public sealed class NvidiaShaderDrs : IShaderDrs, IDisposable {
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr Query(uint id);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Zero();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Create(out IntPtr session);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int One(IntPtr session);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Base(IntPtr session,out IntPtr profile);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Get(IntPtr session,IntPtr profile,uint id,IntPtr setting);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Set(IntPtr session,IntPtr profile,IntPtr setting);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Delete(IntPtr session,IntPtr profile,uint id);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
 [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr module,string name);
 [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
 readonly IntPtr module; readonly Query query; bool initialized;
 const uint EnableId=0x00198fff,LimitId=0x00ac8497;
 // NVDRS_SETTING_V1: pack(4), WCHAR[2048], two 4100-byte unions.
 const int Size=12320, IdOffset=4100, TypeOffset=4104, LocationOffset=4108, DefaultOffset=4112, CurrentOffset=8220;
 T Function<T>(uint id) where T:class {
  var address=query(id); if(address==IntPtr.Zero)throw new InvalidOperationException("Unsupported driver API");
  return Marshal.GetDelegateForFunctionPointer(address,typeof(T)) as T;
 }
 static void Check(int result) { if(result!=0)throw new InvalidOperationException("Driver API unavailable"); }
 public NvidiaShaderDrs(string path) {
  module=LoadLibraryEx(path,IntPtr.Zero,0x800); if(module==IntPtr.Zero)throw new InvalidOperationException("Driver unavailable");
  try { query=(Query)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module,"nvapi_QueryInterface"),typeof(Query));
   Check(Function<Zero>(0x0150e828)()); initialized=true;
  } catch { FreeLibrary(module);throw; }
 }
 IntPtr Session(out IntPtr profile) {
  IntPtr session;Check(Function<Create>(0x0694d52e)(out session));
  try {Check(Function<One>(0x375dbd6b)(session));Check(Function<Base>(0xda8466a0)(session,out profile));return session;}
  catch {Function<One>(0xdad9cff8)(session);throw;}
 }
 IntPtr Setting(uint id) {
  var memory=Marshal.AllocHGlobal(Size);Marshal.Copy(new byte[Size],0,memory,Size);
  Marshal.WriteInt32(memory,Size|(1<<16));Marshal.WriteInt32(memory,IdOffset,unchecked((int)id));return memory;
 }
 uint ReadOne(IntPtr session,IntPtr profile,uint id,out bool predefined,out uint defaultValue) {
  var value=Setting(id);try {Check(Function<Get>(0x73bf8338)(session,profile,id,value));
   if(Marshal.ReadInt32(value,TypeOffset)!=0 || Marshal.ReadInt32(value,LocationOffset)<0 || Marshal.ReadInt32(value,LocationOffset)>3)
    throw new InvalidOperationException("Unsupported setting type");
   if(Marshal.ReadInt32(value,4116)==0)throw new InvalidOperationException("Driver default unavailable");
   defaultValue=unchecked((uint)Marshal.ReadInt32(value,4120));predefined=Marshal.ReadInt32(value,DefaultOffset)!=0;return unchecked((uint)Marshal.ReadInt32(value,CurrentOffset));
  } finally {Marshal.FreeHGlobal(value);}
 }
 ShaderState ReadAt(IntPtr session,IntPtr profile) {
  bool e,l;uint de,dl;uint enabled=ReadOne(session,profile,EnableId,out e,out de),limit=ReadOne(session,profile,LimitId,out l,out dl);
  if(enabled>1)throw new InvalidOperationException("Unsupported cache mode");
  return new ShaderState {Enabled=enabled==1,Limit=limit,DefaultLimit=dl,EnableDefault=e,LimitDefault=l};
 }
 public ShaderState Read() {IntPtr profile;var session=Session(out profile);try{return ReadAt(session,profile);}finally{Function<One>(0xdad9cff8)(session);}}
 void SetOne(IntPtr session,IntPtr profile,uint id,uint wanted,bool predefined) {
  if(predefined) {int result=Function<Delete>(0xe4a26362)(session,profile,id);if(result!=0 && result!=-160)Check(result);return;}
  var value=Setting(id);try {Marshal.WriteInt32(value,CurrentOffset,unchecked((int)wanted));Check(Function<Set>(0x577dd202)(session,profile,value));}finally{Marshal.FreeHGlobal(value);}
 }
 public bool Write(ShaderState expected,ShaderState wanted) {
  IntPtr profile;var session=Session(out profile);try {
   if(!ReadAt(session,profile).Same(expected))return false;
   if(expected.Enabled!=wanted.Enabled || expected.EnableDefault!=wanted.EnableDefault)SetOne(session,profile,EnableId,wanted.Enabled?1u:0u,wanted.EnableDefault);
   if(expected.Limit!=wanted.Limit || expected.LimitDefault!=wanted.LimitDefault)SetOne(session,profile,LimitId,wanted.Limit,wanted.LimitDefault);
   Check(Function<One>(0xfcbc7e14)(session));return true;
  }finally{Function<One>(0xdad9cff8)(session);}
 }
 public void Dispose() {try{if(initialized)Function<Zero>(0xd22bdd7e)();}finally{FreeLibrary(module);}}
}
'''


def invoke(run_cmd, powershell, expected=None, enabled=True, limit="default"):
    if limit != "default" and (type(limit) is not int or not 0 <= limit <= 0xffffffff):
        return {"available": False, "status": "invalid"}
    if type(enabled) is not bool:
        return {"available": False, "status": "invalid"}
    state = "$null"
    if expected is not None:
        if (not isinstance(expected, dict) or type(expected.get("Enabled")) is not bool
                or type(expected.get("Limit")) is not int or not 0 <= expected["Limit"] <= 0xffffffff
                or type(expected.get("DefaultLimit")) is not int or not 0 <= expected["DefaultLimit"] <= 0xffffffff
                or any(type(expected.get(k)) is not bool for k in ("EnableDefault", "LimitDefault"))):
            return {"available": False, "status": "invalid"}
        state = "(New-Object ShaderState -Property @{Enabled=$%s;Limit=[uint32]%d;DefaultLimit=[uint32]%d;EnableDefault=$%s;LimitDefault=$%s})" % (
            str(expected["Enabled"]).lower(), expected["Limit"], expected["DefaultLimit"], str(expected["EnableDefault"]).lower(), str(expected["LimitDefault"]).lower())
    if limit != "default" and limit not in LIMITS and (expected is None or limit != expected["Limit"]):
        return {"available": False, "status": "invalid"}
    script = r'''$ErrorActionPreference='Stop'
$driver=$null;$mutex=$null;$held=$false
try {
 $path=Join-Path ([Environment]::SystemDirectory) 'nvapi64.dll'
 if(-not [Environment]::Is64BitProcess -or -not (Test-Path -LiteralPath $path -PathType Leaf)){throw 'Unavailable'}
 if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Unavailable'}
 $signature=Get-AuthenticodeSignature -LiteralPath $path
 if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=NVIDIA Corporation(?:,|$)'){throw 'Unavailable'}
 Add-Type -TypeDefinition @'
''' + NATIVE + "\n'@\n" + r'''
 $mutex=New-Object Threading.Mutex($false,'Local\Playhub.NvidiaShaderCache')
 try{$held=$mutex.WaitOne(0)}catch [Threading.AbandonedMutexException]{$held=$true}
 if(-not $held){@{available=$false;status='busy'}|ConvertTo-Json -Compress;exit}
 $driver=New-Object -TypeName NvidiaShaderDrs -ArgumentList $path
 [ShaderTransaction]::Run($driver,EXPECTED,ENABLED,[uint32]LIMIT,DEFAULT)|ConvertTo-Json -Depth 4 -Compress
}catch{@{available=$false;status='unavailable'}|ConvertTo-Json -Compress}
finally{if($driver){$driver.Dispose()};if($held){$mutex.ReleaseMutex()};if($mutex){$mutex.Dispose()}}
'''
    script = script.replace("EXPECTED", state).replace("ENABLED", "$" + str(enabled).lower()).replace(
        "LIMIT", str(0 if limit == "default" else limit)).replace("DEFAULT", "$" + str(limit == "default").lower())
    encoded = base64.b64encode(script.encode("utf-16-le")).decode("ascii")
    code, output, _ = run_cmd([powershell(), "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], timeout=12)
    try:
        result = json.loads(output.lstrip("\ufeff")) if code == 0 else None
    except (ValueError, TypeError):
        result = None
    return result if isinstance(result, dict) else {"available": False, "status": "unavailable"}
