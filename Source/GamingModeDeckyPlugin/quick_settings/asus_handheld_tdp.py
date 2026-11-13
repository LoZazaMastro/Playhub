"""Experimental Xbox Ally X RC73XA firmware-limit transactions.

Independent implementation of public ASUS DSTS/DEVS operation identifiers.
No Panel/G-Helper implementation, PawnIO, RyzenAdj or driver is distributed.
A firmware register readback is not measured package power or hardware validation.
"""
import base64
import json
import os
from pathlib import Path
import re
import tempfile
import subprocess
import threading

TOKEN = re.compile(r"^[a-f0-9]{64}$")
KEYS = ("spl", "sppt", "fppt")

def supports_identity(identity):
    norm = lambda v: re.sub(r"[^a-z0-9]", "", v.lower()) if isinstance(v, str) else ""
    return (isinstance(identity, dict) and norm(identity.get("manufacturer")) in ("asus", "asustekcomputerinc")
            and norm(identity.get("model")) in ("rc73xa", "rogxboxallyxrc73xarc73xa"))

def valid_limits(value):
    return (isinstance(value, dict) and set(value) == set(KEYS)
            and all(type(value[k]) is int and 1 <= value[k] <= 255 for k in KEYS)
            and value["spl"] <= value["sppt"] <= value["fppt"])

_SCRIPT = r'''
param([Parameter(Mandatory=$true)][string]$RequestBase64)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RequestBase64))|ConvertFrom-Json
$clock=[Diagnostics.Stopwatch]::StartNew()
$script:instance=$null; $script:token=$null; $script:identity=$null; $script:oemActive=$null
$script:mutex=$null; $script:locked=$false
$script:registers=[ordered]@{spl=[uint32]0x001200A3;sppt=[uint32]0x001200A0;fppt=[uint32]0x001200C1}
function Normalize($v) {return (([string]$v).ToLowerInvariant() -replace '[^a-z0-9]','')}
function DeviceIdentity {
    $bios=Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS'
    $maker=Normalize $bios.SystemManufacturer; $model=Normalize $bios.SystemProductName
    if (@('asus','asustekcomputerinc') -notcontains $maker -or @('rc73xa','rogxboxallyxrc73xarc73xa') -notcontains $model) {throw 'device_unsupported'}
    $version=[string]$bios.BIOSVersion
    if ([string]::IsNullOrWhiteSpace($version)) {throw 'firmware_identity_unknown'}
    return @($maker,$model,$version)|ConvertTo-Json -Compress
}
function ParameterIs($method,[string]$key,[bool]$inputValue) {
    $p=$method.Parameters[$key]
    if ($null -eq $p -or $p.CimType.ToString() -ne 'UInt32') {return $false}
    if ($inputValue) {return $p.Qualifiers['In'].Value -eq $true -and $p.Qualifiers['Out'].Value -ne $true}
    return $p.Qualifiers['Out'].Value -eq $true -and $p.Qualifiers['In'].Value -ne $true
}
function MethodIs($method,[bool]$write) {
    if ($null -eq $method -or $method.Qualifiers['Implemented'].Value -ne $true -or
        @($method.Qualifiers['Privileges'].Value|Where-Object {$_}).Count -ne 0 -or !(ParameterIs $method 'Device_ID' $true)) {return $false}
    if ($write) {return $method.Parameters.Count -eq 3 -and (ParameterIs $method 'Control_status' $true) -and (ParameterIs $method 'result' $false)}
    return $method.Parameters.Count -eq 2 -and (ParameterIs $method 'device_status' $false)
}
function OpenProvider {
    $script:identity=DeviceIdentity
    $script:mutex=[Threading.Mutex]::new($false,'Local\Playhub.AsusAllyFirmware')
    try {$script:locked=$script:mutex.WaitOne(100)} catch [Threading.AbandonedMutexException] {$script:locked=$true}
    if (!$script:locked) {throw 'busy'}
    $class=Get-CimClass -Namespace root/wmi -ClassName AsusAtkWmi_WMNB -OperationTimeoutSec 1
    if ($class.CimClassQualifiers['Provider'].Value -ine 'WmiProv' -or $class.CimClassQualifiers['Dynamic'].Value -ne $true -or
        [guid]$class.CimClassQualifiers['guid'].Value -ne [guid]'97845ed0-4e6d-11de-8a39-0800200c9a66' -or
        !(MethodIs $class.CimClassMethods['DSTS'] $false) -or !(MethodIs $class.CimClassMethods['DEVS'] $true)) {throw 'firmware_schema_unknown'}
    $providers=@(Get-CimInstance -Namespace root/wmi -ClassName __Win32Provider -Filter "Name='WmiProv'" -OperationTimeoutSec 1)
    if ($providers.Count -ne 1 -or [guid]$providers[0].CLSID -ne [guid]'d2d588b5-d081-11d0-99e0-00c04fc2f8ec' -or $providers[0].HostingModel -ine 'LocalSystemHost') {throw 'provider_unknown'}
    $reg=Get-ItemProperty -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{D2D588B5-D081-11D0-99E0-00C04FC2F8EC}\InprocServer32'
    $path=[IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($reg.'(default)'))
    if ($path -ine [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll'))) {throw 'provider_path_unknown'}
    $sig=Get-AuthenticodeSignature -LiteralPath $path
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {throw 'provider_signature_unknown'}
    $instances=@(Get-CimInstance -Namespace root/wmi -ClassName AsusAtkWmi_WMNB -OperationTimeoutSec 1|Where-Object {$_.Active -eq $true})
    if ($instances.Count -ne 1 -or !([string]$instances[0].InstanceName).Trim()) {throw 'provider_ambiguous'}
    $script:instance=$instances[0]
    $stamp=@($script:identity,[string]$instance.InstanceName,[string]$class.CimClassQualifiers['guid'].Value,(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)|ConvertTo-Json -Compress
    $hash=[Security.Cryptography.SHA256]::Create()
    try {$script:token=-join($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($stamp))|ForEach-Object {$_.ToString('x2')})} finally {$hash.Dispose()}
}
function ReadRegister([uint32]$id) {
    if (@($script:registers.Values) -notcontains $id) {throw 'register_unknown'}
    $r=Invoke-CimMethod -InputObject $script:instance -MethodName DSTS -Arguments @{Device_ID=$id} -OperationTimeoutSec 1
    $v=$r.device_status
    if (($v -isnot [uint32] -and $v -isnot [int] -and $v -isnot [long]) -or $v -lt 0 -or
        $v -gt [uint32]::MaxValue -or ($v -band 0xFFFF0000L) -ne 0x10000) {throw 'readback_unavailable'}
    $watts=[int]($v -band 0xFFFF)
    if ($watts -lt 1 -or $watts -gt 255) {throw 'readback_invalid'}
    return $watts
}
function ReadLimits {
    $v=@{spl=(ReadRegister $script:registers.spl);sppt=(ReadRegister $script:registers.sppt);fppt=(ReadRegister $script:registers.fppt)}
    if ($v.spl -gt $v.sppt -or $v.sppt -gt $v.fppt) {throw 'readback_invalid'}
    return $v
}
function WriteRegister([uint32]$id,[int]$watts) {
    if (@($script:registers.Values) -notcontains $id -or $watts -lt 1 -or $watts -gt 255) {throw 'write_invalid'}
    $r=Invoke-CimMethod -InputObject $script:instance -MethodName DEVS -Arguments @{Device_ID=$id;Control_status=[uint32]$watts} -OperationTimeoutSec 1
    $v=$r.result
    if (($v -isnot [uint32] -and $v -isnot [int] -and $v -isnot [long]) -or $v -ne 1) {throw 'write_rejected'}
}
function Gate {
    # OEM control blocks writes only. Read-only DSTS telemetry never stops services.
    $script:oemActive=$null
    $services=@(Get-CimInstance Win32_Service -Filter "Name='ArmouryCrateSEService' OR Name='AsusAppService' OR Name='ArmouryCrateControlInterface'" -OperationTimeoutSec 1)
    $active=$false
    foreach($service in $services) {
        if ($service.State -isnot [string]) {throw 'oem_state_unknown'}
        $state=($service.State -replace '\s','').ToLowerInvariant()
        if ($state -eq 'stopped') {continue}
        if (@('running','startpending','stoppending','continuepending','pausepending','paused') -notcontains $state) {throw 'oem_state_unknown'}
        $active=$true
    }
    # Use the same bounded CIM owner observation as the Lenovo/OneX controls.
    # These utilities can own power policy while Armoury services are stopped.
    # Presence only vetoes our writes; the utility is never stopped or altered.
    $controllers=@(Get-CimInstance -ClassName Win32_Process -Filter "Name='GHelper.exe' OR Name='HandheldCompanion.exe' OR Name='HandheldControlPanel.exe' OR Name='Handheld Control Panel.exe'" -OperationTimeoutSec 1)
    if ($controllers.Count -ne 0) {$active=$true}
    $script:oemActive=$active
    if ($request.action -ne 'get' -and $script:oemActive) {throw 'oem_controller_active'}
    # Win32_Battery does not reliably encode AC online: use SYSTEM_POWER_STATUS.
    if ($null -eq ('PlayhubHandheldPowerState' -as [type])) {
        Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public static class PlayhubHandheldPowerState{[StructLayout(LayoutKind.Sequential)]public struct S{public byte ACLineStatus,BatteryFlag,BatteryLifePercent,SystemStatusFlag;public uint BatteryLifeTime,BatteryFullLifeTime;}[DllImport("kernel32.dll")]public static extern bool GetSystemPowerStatus(out S status);}'
    }
    $status=[PlayhubHandheldPowerState+S]::new()
    if (![PlayhubHandheldPowerState]::GetSystemPowerStatus([ref]$status) -or @([byte]0,[byte]1) -notcontains $status.ACLineStatus) {throw 'power_source_unknown'}
    if ((DeviceIdentity) -cne $script:identity) {throw 'device_changed'}
    return [bool]($status.ACLineStatus -eq 1)
}
function Same($a,$b) {
    if ($null -eq $a -or $null -eq $b) {return $false}
    foreach($key in @('spl','sppt','fppt')) {if ($a.$key -ne $b.$key) {return $false}}
    return $true
}
function Integer($v) {return ($v -is [int] -or $v -is [long])}
function LimitsValid($v) {
    if ($null -eq $v -or @($v.PSObject.Properties.Name).Count -ne 3) {return $false}
    foreach($key in @('spl','sppt','fppt')) {if (!(Integer $v.$key) -or $v.$key -lt 1 -or $v.$key -gt 255) {return $false}}
    return $true
}
function Operate {
    $result=@{ok=$false;available=$false;code='unavailable';changed=$false;recoveryRequired=$false;oemControllerActive=$null;writeAvailable=$false}
    $before=$null; $target=$null; $attempted=@(); $external=$null
    try {
        OpenProvider
        $external=Gate; $before=ReadLimits
        if ((Gate) -cne $external) {throw 'power_source_changed'}
        $result=@{ok=$true;available=$true;code='';provider='asus-rc73xa-tdp';token=$script:token;
            limitsWatts=$before;minimumWatts=7;maximumWatts=$(if($external){35}else{25});externalPower=$external;
            changed=$false;recoveryRequired=$false;readbackKind='firmware-register';hardwareValidated=$false;
            oemControllerActive=$script:oemActive;writeAvailable=(!$script:oemActive)}
        if ($request.action -eq 'get') {return $result}
        if (@('apply','restore') -notcontains $request.action -or $request.confirmed -isnot [bool] -or $request.confirmed -ne $true -or
            $request.token -cne $script:token -or !(LimitsValid $request.expected) -or !(Same $before $request.expected) -or
            $request.externalPower -cne $external) {throw 'state_changed'}
        if ($request.action -eq 'apply') {
            $max=if($external){35}else{25}
            if (!(Integer $request.watts) -or $request.watts -lt 7 -or $request.watts -gt $max) {throw 'target_out_of_range'}
            $target=@{spl=[int]$request.watts;sppt=[int]$request.watts;fppt=[int]$request.watts}
        } else {
            if (!(LimitsValid $request.baseline)) {throw 'baseline_invalid'}
            $target=@{spl=[int]$request.baseline.spl;sppt=[int]$request.baseline.sppt;fppt=[int]$request.baseline.fppt}
            # Baseline is captured by the Python owner, never accepted from the UI.
            # Preserve independent OEM boost limits, which can exceed sustained TDP policy.
        }
        if (Same $before $target) {return $result}
        # Preserve SPL <= SPPT <= FPPT across mixed restore operations.
        $order=@()
        foreach($key in @('spl','sppt','fppt')) {if ($target[$key] -lt $before[$key]) {$order+=,$key}}
        foreach($key in @('fppt','sppt','spl')) {if ($target[$key] -gt $before[$key]) {$order+=,$key}}
        foreach($key in $order) {
            $latest=ReadLimits
            $expected=@{spl=$before.spl;sppt=$before.sppt;fppt=$before.fppt}
            foreach($done in $attempted) {$expected[$done]=$target[$done]}
            if (!(Same $latest $expected)) {throw 'concurrent_change'}
            if ((Gate) -cne $external) {throw 'power_source_changed'}
            if ($clock.ElapsedMilliseconds -gt 6000) {throw 'preflight_timeout'}
            # Record BEFORE invocation: a throwing native method may already apply.
            $attempted+=,$key
            WriteRegister $script:registers[$key] $target[$key]
            if ((ReadRegister $script:registers[$key]) -ne $target[$key]) {throw 'write_not_confirmed'}
        }
        $after=ReadLimits
        if (!(Same $after $target) -or (Gate) -cne $external) {throw 'final_state_changed'}
        $result.limitsWatts=$after; $result.changed=$true
        return $result
    } catch {
        $result.ok=$false; $result.code='unavailable'
        $result.oemControllerActive=$script:oemActive; $result.writeAvailable=$false
        if ($_.Exception.Message -match '^[a-z_]+$') {$result.code=$_.Exception.Message}
        if (@('device_changed','power_source_changed','oem_controller_active') -contains $result.code) {$result.available=$false}
        if ($attempted.Count -gt 0) {
            $result.recoveryRequired=$true
            try {
                # Rollback only values still attributable to this transaction.
                # Device/power/OEM guard remains mandatory; foreign values are preserved.
                if ((Gate) -cne $external) {throw 'power_source_changed'}
                $reverse=@($attempted);[array]::Reverse($reverse)
                foreach($key in $reverse) {
                    $latest=ReadLimits
                    if ($latest[$key] -eq $before[$key]) {continue}
                    if ($latest[$key] -ne $target[$key]) {throw 'external_change'}
                    if ((Gate) -cne $external) {throw 'power_source_changed'}
                    WriteRegister $script:registers[$key] $before[$key]
                    if ((ReadRegister $script:registers[$key]) -ne $before[$key]) {throw 'restore_not_confirmed'}
                }
                $result.limitsWatts=ReadLimits
                $result.recoveryRequired=!(Same $result.limitsWatts $before)
                $result.rollbackConfirmed=!$result.recoveryRequired
            } catch {$result.code='recovery_required'; $result.available=$false; $null=$result.Remove('limitsWatts')}
        }
        return $result
    } finally {
        if ($script:locked) {$script:mutex.ReleaseMutex()}
        if ($null -ne $script:mutex) {$script:mutex.Dispose()}
    }
}
'''

class AsusHandheldTdp:
    """No constructor hardware access. Requests cannot choose a method/register/path.

    Successful writes are explicit user settings, not periodic game profiles.
    The in-memory baseline permits an explicit restore while this instance lives.
    A process crash does not promise automatic restoration.
    """
    def __init__(self, read_identity, run_cmd, powershell_path, windows=None):
        self._identity, self._run_cmd, self._powershell = read_identity, run_cmd, powershell_path
        self._windows = os.name == "nt" if windows is None else windows
        self._lock = threading.Lock()
        self._last = None
        self._owned = None
        self._uncertain = False

    def _run(self, request):
        if not self._windows or not supports_identity(self._identity()):
            return {"ok": False, "available": False, "code": "device_unsupported"}
        try:
            payload = base64.b64encode(json.dumps(request, separators=(",", ":")).encode()).decode("ascii")
            with tempfile.TemporaryDirectory(prefix="Playhub-Handheld-Tdp-") as directory:
                file = Path(directory) / "tdp.ps1"
                file.write_text(_SCRIPT + "\nOperate | ConvertTo-Json -Compress -Depth 5\n", encoding="utf-8-sig")
                code, output, _ = self._run_cmd([self._powershell(), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(file), "-RequestBase64", payload], timeout=24)
            value = json.loads(output.lstrip("\ufeff")) if code == 0 and len(output) <= 4096 else None
            if not supports_identity(self._identity()):
                return {"ok": False, "available": False, "code": "device_changed", "recoveryRequired": request["action"] != "get"}
            if self._valid(value):
                return value
        except (OSError, ValueError, TypeError, subprocess.SubprocessError):
            pass
        return {"ok": False, "available": False, "code": "operation_unknown", "recoveryRequired": request["action"] != "get"}

    @staticmethod
    def _valid(value):
        return (isinstance(value, dict) and type(value.get("ok")) is bool and type(value.get("available")) is bool
                and type(value.get("recoveryRequired")) is bool and isinstance(value.get("code"), str)
                and type(value.get("changed")) is bool and (not value["ok"] or value["available"])
                and (not value["available"] or (value.get("provider") == "asus-rc73xa-tdp"
                    and isinstance(value.get("token"), str) and TOKEN.fullmatch(value["token"])
                    and valid_limits(value.get("limitsWatts")) and type(value.get("minimumWatts")) is int and value["minimumWatts"] == 7
                    and type(value.get("externalPower")) is bool and type(value.get("maximumWatts")) is int
                    and value["maximumWatts"] == (35 if value["externalPower"] else 25)
                    and value.get("readbackKind") == "firmware-register" and value.get("hardwareValidated") is False
                    and type(value.get("oemControllerActive")) is bool and type(value.get("writeAvailable")) is bool
                    and value["writeAvailable"] is (not value["oemControllerActive"]))))

    def get(self):
        with self._lock:
            self._last = None
            value = self._run({"action": "get"})
            relinquished = False
            if value.get("ok") is True and value.get("available") is True:
                if (not self._uncertain and self._owned is not None and
                        (value["token"] != self._owned["token"] or value["limitsWatts"] != self._owned["target"]
                         or value["externalPower"] != self._owned["externalPower"])):
                    # Fresh independent observation supersedes a known receipt.
                    # Relinquish without writing; never clear a write-uncertain state.
                    self._owned = None
                    relinquished = True
                self._last = json.loads(json.dumps(value))
            write_available = (value.get("ok") is True and value.get("available") is True
                               and value.get("writeAvailable") is True and not self._uncertain)
            blocked = ("manual_recovery_required" if self._uncertain else
                       "oem_controller_active" if value.get("oemControllerActive") is True else
                       value.get("code", "unavailable") if not write_available else "")
            return {**value, "experimental": True, "restoreAvailable": self._owned is not None,
                    "manualRecoveryRequired": self._uncertain, "writeAvailable": write_available,
                    "writeBlockedReason": blocked, "ownedRelinquished": relinquished}

    def apply(self, request):
        if (not isinstance(request, dict) or request.get("confirmed") is not True
                or type(request.get("watts")) is not int or not isinstance(request.get("token"), str)):
            return {"ok": False, "code": "invalid_request"}
        with self._lock:
            if self._uncertain:
                return {"ok": False, "code": "manual_recovery_required"}
            if not self._last or request["token"] != self._last["token"]:
                return {"ok": False, "code": "fresh_read_required"}
            previous = self._last
            if previous.get("writeAvailable") is not True:
                return {"ok": False, "code": "oem_controller_active", "writeAvailable": False,
                        "oemControllerActive": previous.get("oemControllerActive")}
            owned = self._owned
            if owned is not None and (previous["token"] != owned["token"] or previous["limitsWatts"] != owned["target"]
                                      or previous["externalPower"] != owned["externalPower"]):
                self._owned = None
                self._last = None
                return {"ok": False, "code": "fresh_read_required", "ownedRelinquished": True}
            if not previous["minimumWatts"] <= request["watts"] <= previous["maximumWatts"]:
                return {"ok": False, "code": "target_out_of_range"}
            command = {"action": "apply", "confirmed": True, "token": previous["token"],
                       "expected": previous["limitsWatts"], "externalPower": previous["externalPower"], "watts": request["watts"]}
            target = dict.fromkeys(KEYS, request["watts"])
            value = self._run(command)
            self._last = None
            self._uncertain = value.get("recoveryRequired") is True
            if value.get("ok") is True and value.get("available") is True:
                if (value["token"] != previous["token"] or value["limitsWatts"] != target or value["externalPower"] != previous["externalPower"]
                        or (previous["limitsWatts"] != target and value.get("changed") is not True)):
                    self._uncertain = True
                    return {"ok": False, "code": "confirmation_mismatch", "recoveryRequired": True}
                if value.get("changed") is True:
                    self._owned = {"token": value["token"], "target": target,
                                   "baseline": owned["baseline"] if owned is not None else previous["limitsWatts"],
                                   "externalPower": previous["externalPower"]}
            return value

    def restore(self, request):
        if not isinstance(request, dict) or request.get("confirmed") is not True:
            return {"ok": False, "code": "invalid_request"}
        with self._lock:
            if self._uncertain:
                return {"ok": False, "code": "manual_recovery_required"}
            if self._owned is None or request.get("token") != self._owned["token"]:
                return {"ok": False, "code": "owned_restore_required"}
            owned = self._owned
            if self._last is not None and self._last.get("oemControllerActive") is True:
                return {"ok": False, "code": "oem_controller_active", "writeAvailable": False,
                        "oemControllerActive": True}
            value = self._run({"action": "restore", "confirmed": True, "token": owned["token"],
                               "expected": owned["target"], "externalPower": owned["externalPower"], "baseline": owned["baseline"]})
            self._last = None
            self._uncertain = value.get("recoveryRequired") is True
            if value.get("ok") is True:
                if value.get("token") != owned["token"] or value.get("limitsWatts") != owned["baseline"]:
                    self._uncertain = True
                    return {"ok": False, "code": "confirmation_mismatch", "recoveryRequired": True}
                self._owned = None
            return value
