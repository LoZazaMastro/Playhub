"""OneX type-2 fan transactions, written from independent protocol facts.

Source facts: hanxiao1982/steamos-onexplayer at 69f31be384f11029fe7762ef4c19837637889fbf,
Documentation/wmi/devices/oxp-wmi.rst and docs/ec/oxp-wmi.md / onexconsole-dmi-ec-profiles.md.
No HC or prior held OneX module is used. Transport scaffolding follows Playhub's
own asus_handheld_tdp.py. No hardware access on import or construction.
"""
import base64
import copy
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import threading

TYPE2_BOARDS = frozenset(("ONEXPLAYER Apex i", "ONEXPLAYER Apex Air", "ONEXPLAYER SUPER V",
                         "ONEXPLAYER X2", "ONEXPLAYER X2 EVA", "ONEXPLAYER X2Mini", "ONEXPLAYER 3"))
_TOKEN = re.compile(r"[a-f0-9]{64}\Z")
_HEX_RESPONSE = re.compile(r"0x[0-9a-fA-F]{2}(?:,0x[0-9a-fA-F]{2}){7}\Z")


def supports_board(board):
    return (isinstance(board, dict) and isinstance(board.get("manufacturer"), str)
            and "ONE-NETBOOK" in board["manufacturer"] and board.get("product") in TYPE2_BOARDS)


def parse_firmware_response(value):
    """Only the independently documented eight-token hex representation.

    No decimal/short response fallback or inferred conversion from an arbitrary
    PowerShell object. Six reserved zero bytes are required by this profile.
    """
    if not isinstance(value, str) or not _HEX_RESPONSE.fullmatch(value):
        raise ValueError("response_format_unknown")
    octets = bytes(int(part[2:], 16) for part in value.split(","))
    if octets[0] != 0 or any(octets[2:]):
        raise ValueError("firmware_response_rejected")
    return octets[1]


def _fan_state(value):
    return (isinstance(value, dict) and set(value) == {"mode", "pwm"}
            and type(value["mode"]) is int and value["mode"] in (0, 1)
            and type(value["pwm"]) is int and 0 <= value["pwm"] <= 184)


def _same_control(current, expected):
    return (_fan_state(current) and _fan_state(expected) and current["mode"] == expected["mode"]
            and (current["mode"] == 0 or current["pwm"] == expected["pwm"]))


_SCRIPT = r'''
param([Parameter(Mandatory=$true)][string]$RequestBase64)
$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'
$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RequestBase64))|ConvertFrom-Json
$clock=[Diagnostics.Stopwatch]::StartNew()
$script:instance=$null; $script:identity=$null; $script:token=$null
$script:mutex=$null; $script:locked=$false
$script:products=@('ONEXPLAYER Apex i','ONEXPLAYER Apex Air','ONEXPLAYER SUPER V','ONEXPLAYER X2','ONEXPLAYER X2 EVA','ONEXPLAYER X2Mini','ONEXPLAYER 3')
function BoardIdentity {
    $boards=@(Get-CimInstance -ClassName Win32_BaseBoard -OperationTimeoutSec 1)
    $bios=@(Get-CimInstance -ClassName Win32_BIOS -OperationTimeoutSec 1)
    if($boards.Count -ne 1 -or $bios.Count -ne 1) {throw 'board_ambiguous'}
    $b=$boards[0]; $version=[string]$bios[0].SMBIOSBIOSVersion
    if(!([string]$b.Manufacturer).Contains('ONE-NETBOOK') -or $script:products -cnotcontains [string]$b.Product -or [string]::IsNullOrWhiteSpace($version)) {throw 'device_unsupported'}
    return @([string]$b.Manufacturer,[string]$b.Product,[string]$b.Version,$version)|ConvertTo-Json -Compress
}
function ParamIs($m,[string]$name,[string]$type,[bool]$asInput) {
    $p=$m.Parameters[$name]
    if($null -eq $p -or $p.CimType.ToString() -cne $type) {return $false}
    if($asInput) {return ($p.Qualifiers['In'].Value -eq $true -and $p.Qualifiers['Out'].Value -ne $true)}
    return ($p.Qualifiers['Out'].Value -eq $true -and $p.Qualifiers['In'].Value -ne $true)
}
function MethodIs($m,[string]$inputName,[int]$id) {
    return ($null -ne $m -and $m.Parameters.Count -eq 2 -and $m.Qualifiers['Implemented'].Value -eq $true -and
        $m.Qualifiers['WmiMethodId'].Value -eq $id -and @($m.Qualifiers['Privileges'].Value|Where-Object {$_}).Count -eq 0 -and
        (ParamIs $m $inputName 'UInt32' $true) -and (ParamIs $m 'uStringReturn' 'String' $false))
}
function ClassIs($class) {
    try {
        return ($null -ne $class -and $class.CimClassName -ceq 'SuRwECRegInterface' -and
            $class.CimClassQualifiers['Dynamic'].Value -eq $true -and $class.CimClassQualifiers['Provider'].Value -ieq 'WmiProv' -and
            [guid]$class.CimClassQualifiers['guid'].Value -eq [guid]'43B5A593-AD62-4257-8546-91B0797BEC1B' -and
            $class.CimClassProperties['Active'].CimType.ToString() -ceq 'Boolean' -and
            $class.CimClassProperties['InstanceName'].CimType.ToString() -ceq 'String' -and
            $class.CimClassProperties['InstanceName'].Qualifiers['Key'].Value -eq $true -and
            @($class.CimClassProperties|Where-Object {$_.Qualifiers['Key'].Value -eq $true}).Count -eq 1 -and
            (MethodIs $class.CimClassMethods['ReadECReg'] 'GroupOffset' 1) -and
            (MethodIs $class.CimClassMethods['WriteECReg'] 'GroupOffsetValue' 2))
    } catch {return $false}
}
function InstanceIs($instances,$expected=$null) {
    if(@($instances).Count -ne 1 -or $instances[0].Active -isnot [bool] -or !$instances[0].Active -or
        [string]::IsNullOrWhiteSpace([string]$instances[0].InstanceName) -or
        $instances[0].CimSystemProperties.ClassName -cne 'SuRwECRegInterface' -or
        @('root/wmi','root\wmi') -inotcontains $instances[0].CimSystemProperties.Namespace) {return $false}
    return ($null -eq $expected -or ([string]$instances[0].InstanceName -ceq [string]$expected.InstanceName -and
        [string]$instances[0].CimSystemProperties.ServerName -ceq [string]$expected.CimSystemProperties.ServerName))
}
function OpenProvider {
    $script:identity=BoardIdentity
    $script:mutex=[Threading.Mutex]::new($false,'Global\Playhub.OneXTypeTwoFan')
    try {$script:locked=$script:mutex.WaitOne(100)} catch [Threading.AbandonedMutexException] {$script:locked=$true}
    if(!$script:locked) {throw 'busy'}
    $class=Get-CimClass -Namespace root/wmi -ClassName SuRwECRegInterface -OperationTimeoutSec 1
    if(!(ClassIs $class)) {throw 'schema_unknown'}
    $providers=@(Get-CimInstance -Namespace root/wmi -ClassName __Win32Provider -Filter "Name='WmiProv'" -OperationTimeoutSec 1)
    if($providers.Count -ne 1 -or [guid]$providers[0].CLSID -ne [guid]'d2d588b5-d081-11d0-99e0-00c04fc2f8ec' -or $providers[0].HostingModel -ine 'LocalSystemHost') {throw 'provider_unknown'}
    $r=Get-ItemProperty -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{D2D588B5-D081-11D0-99E0-00C04FC2F8EC}\InprocServer32'
    $path=[IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($r.'(default)'))
    if($path -ine [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll'))) {throw 'provider_path_unknown'}
    $sig=Get-AuthenticodeSignature -LiteralPath $path
    if($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {throw 'provider_signature_unknown'}
    $instances=@(Get-CimInstance -Namespace root/wmi -ClassName SuRwECRegInterface -OperationTimeoutSec 1)
    if(!(InstanceIs $instances)) {throw 'instance_ambiguous'}
    $script:instance=$instances[0]
    # InstanceName is the attested sole key. MI's Path may be null even for valid instances.
    $stamp=@($script:identity,[string]$instance.InstanceName,[string]$instance.CimSystemProperties.ClassName,[string]$instance.CimSystemProperties.Namespace,[string]$instance.CimSystemProperties.ServerName,(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)|ConvertTo-Json -Compress
    $h=[Security.Cryptography.SHA256]::Create()
    try {$script:token=-join($h.ComputeHash([Text.Encoding]::UTF8.GetBytes($stamp))|ForEach-Object {$_.ToString('x2')})} finally {$h.Dispose()}
}
function Guard {
    if($clock.ElapsedMilliseconds -gt 6000) {throw 'transaction_expired'}
    # Positive own-device binding is not permission to stop an OEM utility.
    $processes=@(Get-CimInstance -ClassName Win32_Process -Filter "Name='OneXConsole.exe' OR Name='CompatLayerCT.exe' OR Name='HandheldCompanion.exe' OR Name='Handheld Control Panel.exe'" -OperationTimeoutSec 1)
    if($processes.Count -ne 0) {throw 'competing_controller'}
    $instances=@(Get-CimInstance -Namespace root/wmi -ClassName SuRwECRegInterface -OperationTimeoutSec 1)
    if(!(InstanceIs $instances $script:instance) -or
        (BoardIdentity) -cne $script:identity -or $clock.ElapsedMilliseconds -gt 6000) {throw 'device_changed'}
}
function Decode($value) {
    if($value -isnot [string] -or $value -cnotmatch '^0x[0-9a-fA-F]{2}(,0x[0-9a-fA-F]{2}){7}$') {throw 'response_format_unknown'}
    $bytes=@($value.Split(',')|ForEach-Object {[Convert]::ToInt32($_.Substring(2),16)})
    if($bytes[0] -ne 0 -or @($bytes[2..7]|Where-Object {$_ -ne 0}).Count -ne 0) {throw 'firmware_response_rejected'}
    return [int]$bytes[1]
}
function ReadByte([int]$offset) {
    if(@(0x4A,0x4B,0x58,0x59,0x70) -notcontains $offset) {throw 'register_forbidden'}
    $r=Invoke-CimMethod -InputObject $script:instance -MethodName ReadECReg -Arguments @{GroupOffset=[uint32](4+256*$offset)} -OperationTimeoutSec 1
    return Decode $r.uStringReturn
}
function FanState {
    $m=ReadByte 0x4A; $p=ReadByte 0x4B
    if(@(0,1) -notcontains $m -or $p -gt 184) {throw 'fan_state_unknown'}
    return @{mode=$m;pwm=$p}
}
function SameControl($a,$b) {
    return ($null -ne $a -and $null -ne $b -and $a.mode -eq $b.mode -and ($a.mode -eq 0 -or $a.pwm -eq $b.pwm))
}
function ValidState($s) {
    return ($null -ne $s -and @($s.PSObject.Properties.Name).Count -eq 2 -and
        $s.mode -is [int] -and @(0,1) -contains $s.mode -and $s.pwm -is [int] -and $s.pwm -ge 0 -and $s.pwm -le 184)
}
function WriteByte([int]$offset,[int]$value) {
    if(!(($offset -eq 0x4A -and @(0,1) -contains $value) -or ($offset -eq 0x4B -and $value -ge 0 -and $value -le 184))) {throw 'write_forbidden'}
    $r=Invoke-CimMethod -InputObject $script:instance -MethodName WriteECReg -Arguments @{GroupOffsetValue=[uint32](4+256*$offset+65536*$value)} -OperationTimeoutSec 1
    if((Decode $r.uStringReturn) -ne $value) {throw 'write_reply_mismatch'}
}
function Telemetry {
    $a=ReadByte 0x58; $lo=ReadByte 0x59; $b=ReadByte 0x58
    if($a -ne $b) {throw 'tachometer_changed'}
    return @{fanRpm=($a*256+$lo);cpuTemperatureC=(ReadByte 0x70)}
}
function Operate {
    $attempted=$false
    try {
        OpenProvider; Guard; $before=FanState; Guard
        if($request.action -eq 'snapshot') {
            $t=Telemetry; $last=FanState; Guard
            if(!(SameControl $before $last)) {throw 'concurrent_change'}
            return @{ok=$true;available=$true;code='';token=$script:token;state=$last;telemetry=$t;changed=$false;recoveryRequired=$false}
        }
        if(@('manual','auto','restore') -notcontains $request.action -or $request.confirmed -isnot [bool] -or !$request.confirmed -or
            $request.token -cne $script:token -or !(ValidState $request.expected) -or !(SameControl $before $request.expected)) {throw 'state_changed'}
        if($request.action -eq 'manual') {
            if($request.percent -isnot [int] -or $request.percent -lt 0 -or $request.percent -gt 100) {throw 'percent_invalid'}
            $target=@{mode=1;pwm=[int][Math]::Floor(($request.percent*184+50)/100)}
        } elseif($request.action -eq 'auto') {$target=@{mode=0;pwm=$before.pwm}}
        else {if(!(ValidState $request.baseline)) {throw 'baseline_invalid'}; $target=@{mode=$request.baseline.mode;pwm=$request.baseline.pwm}}
        # No skip when duty already equals target: manual mode must latch PWM afterwards.
        $current=FanState; Guard
        if(!(SameControl $current $before)) {throw 'concurrent_change'}
        $attempted=$true; WriteByte 0x4A $target.mode
        if((ReadByte 0x4A) -ne $target.mode) {throw 'mode_not_confirmed'}
        if($target.mode -eq 1) {
            $mid=FanState; Guard
            if($mid.mode -ne 1 -or ($before.mode -eq 1 -and $mid.pwm -ne $before.pwm)) {throw 'concurrent_change'}
            WriteByte 0x4B $target.pwm
        }
        $after=FanState; Guard
        if(!(SameControl $after $target)) {throw 'write_not_confirmed'}
        return @{ok=$true;available=$true;code='';token=$script:token;state=$after;changed=$true;recoveryRequired=$false}
    } catch {
        $code='operation_unknown'; if($_.Exception.Message -match '^[a-z_]+$') {$code=$_.Exception.Message}
        # A throwing method may already have changed EC state. Never blind-retry/rollback.
        return @{ok=$false;available=$false;code=$code;changed=$false;recoveryRequired=$attempted}
    } finally {
        if($script:locked) {$script:mutex.ReleaseMutex()}
        if($null -ne $script:mutex) {$script:mutex.Dispose()}
    }
}
'''


class OneXTypeTwoFan:
    """Feature-only API. Baseline/restore state is server-owned, never UI supplied.

    One instance is intended per backend. A successful local readback is not a
    measured cooling result. OEM tools are not stopped; unknown results revoke
    writes until the host instance is recreated after explicit manual recovery.
    """
    _owners_lock = threading.Lock()
    _owners = {}

    def __init__(self, read_board, run_cmd, powershell_path, windows=None):
        self._board, self._run_cmd, self._powershell = read_board, run_cmd, powershell_path
        self._windows = os.name == "nt" if windows is None else windows
        self._lock = threading.Lock()
        self._last = self._owned = None
        self._uncertain = False
        self._owner_id = object()

    def _send(self, request):
        if not self._windows or not supports_board(self._board()):
            return {"ok": False, "available": False, "code": "device_unsupported", "recoveryRequired": False}
        try:
            payload = base64.b64encode(json.dumps(request, separators=(",", ":")).encode()).decode("ascii")
            with tempfile.TemporaryDirectory(prefix="Playhub-OneX-Fan-") as directory:
                script = Path(directory) / "fan.ps1"
                script.write_text(_SCRIPT + "\nOperate | ConvertTo-Json -Compress -Depth 5\n", encoding="utf-8-sig")
                status, output, _ = self._run_cmd([self._powershell(), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(script), "-RequestBase64", payload], timeout=20)
            reply = json.loads(output.lstrip("\ufeff")) if status == 0 and len(output) <= 4096 else None
            if not supports_board(self._board()):
                raise ValueError("device_changed")
            if self._valid(reply):
                return reply
        except (OSError, ValueError, TypeError, subprocess.SubprocessError):
            pass
        return {"ok": False, "available": False, "code": "operation_unknown", "recoveryRequired": request["action"] != "snapshot"}

    @staticmethod
    def _valid(reply):
        return (isinstance(reply, dict) and type(reply.get("ok")) is bool and type(reply.get("available")) is bool
                and isinstance(reply.get("code"), str) and type(reply.get("changed")) is bool
                and type(reply.get("recoveryRequired")) is bool and (not reply["ok"] or
                    (reply["available"] and not reply["recoveryRequired"] and isinstance(reply.get("token"), str)
                     and _TOKEN.fullmatch(reply["token"]) and _fan_state(reply.get("state")))))

    def snapshot(self):
        with self._lock:
            self._last = None
            value = self._send({"action": "snapshot"})
            if value.get("ok") is True:
                telemetry = value.get("telemetry")
                if (not isinstance(telemetry, dict) or type(telemetry.get("fanRpm")) is not int
                        or not 0 <= telemetry["fanRpm"] <= 65535 or type(telemetry.get("cpuTemperatureC")) is not int
                        or not 0 <= telemetry["cpuTemperatureC"] <= 255):
                    return {"ok": False, "available": False, "code": "telemetry_unknown"}
                # A fresh verified OEM/manual change supersedes our old setting.
                # Relinquish it without writing a stale baseline over the new choice.
                # An uncertain attempted write still requires explicit recovery.
                if self._owned is not None and not self._uncertain and (self._owned["token"] != value["token"]
                        or not _same_control(value["state"], self._owned["target"])):
                    with self._owners_lock:
                        if self._owners.get(self._owned["token"]) is self._owner_id:
                            del self._owners[self._owned["token"]]
                    self._owned = None
                self._last = copy.deepcopy(value)
            return {**value, "provider": "onex-type2-fan", "experimental": True, "hardwareValidated": False,
                    "readbackKind": "firmware-register", "restoreAvailable": self._owned is not None,
                    "manualRecoveryRequired": self._uncertain}

    def set_manual(self, percent, token, confirmed=False):
        if type(percent) is not int or not 0 <= percent <= 100:
            return {"ok": False, "code": "percent_invalid"}
        return self._change("manual", token, confirmed, percent)

    def set_auto(self, token, confirmed=False):
        return self._change("auto", token, confirmed)

    def restore(self, token, confirmed=False):
        return self._change("restore", token, confirmed)

    def _change(self, action, token, confirmed, percent=None):
        if confirmed is not True or not isinstance(token, str):
            return {"ok": False, "code": "confirmation_required"}
        with self._lock:
            if self._uncertain:
                return {"ok": False, "code": "manual_recovery_required"}
            last, owned = self._last, self._owned
            if not last or token != last["token"]:
                return {"ok": False, "code": "fresh_snapshot_required"}
            if action == "restore" and owned is None:
                return {"ok": False, "code": "no_owned_change"}
            if owned is not None and (owned["token"] != token or not _same_control(last["state"], owned["target"])):
                return {"ok": False, "code": "external_change_restore_skipped"}
            newly_claimed = False
            with self._owners_lock:
                current_owner = self._owners.get(token)
                if current_owner is not None and current_owner is not self._owner_id:
                    return {"ok": False, "code": "owned_by_another_session"}
                if current_owner is None:
                    self._owners[token] = self._owner_id
                    newly_claimed = True
            command = {"action": action, "confirmed": True, "token": token, "expected": copy.deepcopy(last["state"])}
            if action == "manual":
                command["percent"] = percent
                target = {"mode": 1, "pwm": (percent * 184 + 50) // 100}
            elif action == "auto":
                target = {"mode": 0, "pwm": last["state"]["pwm"]}
            else:
                command["baseline"] = copy.deepcopy(owned["baseline"])
                target = command["baseline"]
            value = self._send(command)
            self._last = None
            self._uncertain = value.get("recoveryRequired") is True
            if value.get("ok") is True:
                if value["token"] != token or not _same_control(value["state"], target) or value["changed"] is not True:
                    self._uncertain = True
                    return {"ok": False, "code": "readback_mismatch", "recoveryRequired": True}
                if action == "manual":
                    self._owned = {"token": token, "baseline": copy.deepcopy(owned["baseline"] if owned else last["state"]),
                                   "target": copy.deepcopy(target)}
                else:
                    self._owned = None
                    with self._owners_lock:
                        if self._owners.get(token) is self._owner_id:
                            del self._owners[token]
            elif newly_claimed and not self._uncertain:
                with self._owners_lock:
                    if self._owners.get(token) is self._owner_id:
                        del self._owners[token]
            return value
