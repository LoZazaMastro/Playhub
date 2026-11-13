"""Claw 8 EX AI+ power and charge controls through its ACPI-WMI interface.

Hardware reference: ClawConfigurator fdb2e3acfbafe94a584425f1185a616d0e329c42
(MIT). Its notice is preserved in rgb/LICENSE-ClawConfigurator.txt.
"""
import base64
import json
import os
import re
import tempfile
import threading
from pathlib import Path


_TOKEN = re.compile(r"^[a-f0-9]{64}$")


def _identifier(value):
    return re.sub(r"[^a-z0-9]", "", value.lower()) if isinstance(value, str) else ""


def supports_identity(identity):
    return (isinstance(identity, dict)
            and _identifier(identity.get("manufacturer")) in ("msi", "microstarinternationalcoltd")
            and _identifier(identity.get("model")) in ("ms1t91", "claw8exaicg3em"))


_SCRIPT = r'''
param([Parameter(Mandatory=$true)][string]$RequestBase64)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$request = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RequestBase64)) | ConvertFrom-Json
$clock = [Diagnostics.Stopwatch]::StartNew()
$script:instance = $null
$script:packageClass = $null
$script:token = $null
$script:tdpAvailable = $false
$before = $null
$attempted = $false
$mutex = $null
$locked = $false
function Schema($method, [int]$id) {
    if ($null -eq $method -or $method.Qualifiers['WmiMethodId'].Value -ne $id -or
        $method.Qualifiers['Implemented'].Value -ne $true -or $method.Parameters.Count -ne 1) {return $false}
    $data = $method.Parameters['Data']
    return $null -ne $data -and $data.CimType.ToString() -eq 'Instance' -and
        $data.Qualifiers['EmbeddedInstance'].Value -eq 'Package_32' -and
        $data.Qualifiers['In'].Value -eq $true -and $data.Qualifiers['Out'].Value -eq $true -and
        @($method.Qualifiers['Privileges'].Value | Where-Object {$_}).Count -eq 0
}
function Package([string]$method, [byte[]]$bytes) {
    if (@('Get_AP','Set_AP','Get_SlaveBattery','Set_SlaveBattery') -notcontains $method -or $bytes.Length -ne 32) {throw 'Invalid operation'}
    $embedded = New-CimInstance -CimClass $script:packageClass -Property @{Bytes=$bytes} -ClientOnly
    $result = Invoke-CimMethod -InputObject $script:instance -MethodName $method -Arguments @{Data=$embedded} -OperationTimeoutSec 1
    if ($result.Data.Bytes -isnot [byte[]] -or $result.Data.Bytes.Length -ne 32) {throw 'Invalid firmware package'}
    return ,([byte[]]$result.Data.Bytes.Clone())
}
function ReadPackage {
    $package = Package 'Get_AP' ([byte[]]::new(32))
    if ($package[0] -ne 1 -or $package[1] -ne 0 -or $package[2] -ne 0 -or
        ($package[3] -band 0xF0) -ne 0xC0 -or @(1,2,6) -notcontains ($package[3] -band 0x0F) -or
        $package[4] -ne 0x80 -or ($package[5] -band 0x80) -eq 0) {throw 'Different firmware response'}
    $percent = $package[5] -band 0x7F
    if ($percent -lt 20 -or $percent -gt 100) {throw 'Invalid charge limit'}
    return ,$package
}
function WriteCharge([byte[]]$current, [int]$percent) {
    if ($percent -lt 20 -or $percent -gt 100 -or $current.Length -ne 32) {throw 'Invalid charge limit'}
    $payload = [byte[]]$current.Clone()
    $payload[0] = 0
    $payload[5] = [byte]($percent -bor 0x80)
    $null = Package 'Set_AP' $payload
}
function Confirm([int]$percent) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $package = ReadPackage
        if (($package[5] -band 0x7F) -eq $percent) {return $true}
        Start-Sleep -Milliseconds 60
    } while ($wait.ElapsedMilliseconds -lt 600)
    return $false
}
function ReadLimits {
    $query = [byte[]]::new(32); $query[0] = 1
    $package = Package 'Get_SlaveBattery' $query
    if ($package[0] -ne 1 -or $package[1] -lt 8 -or $package[1] -gt 35 -or
        $package[2] -lt ($package[1] + 2) -or $package[2] -gt 45) {throw 'Different power-limit response'}
    return ,$package
}
function WriteLimits([int]$pl1, [int]$pl2) {
    if ($pl1 -lt 8 -or $pl1 -gt 35 -or $pl2 -lt ($pl1 + 2) -or $pl2 -gt 45) {throw 'Invalid power limits'}
    $payload = [byte[]]::new(32); $payload[0] = 1; $payload[1] = $pl1; $payload[2] = $pl2
    $null = Package 'Set_SlaveBattery' $payload
}
function WriteMode([byte[]]$current, [int]$mode) {
    if (@(1,2,6) -notcontains $mode -or $current.Length -ne 32) {throw 'Invalid mode'}
    $payload = [byte[]]$current.Clone(); $payload[0] = 0
    $payload[3] = [byte](($current[3] -band 0xF0) -bor $mode)
    $null = Package 'Set_AP' $payload
}
function SameLimits([byte[]]$current, [int]$pl1, [int]$pl2) {
    return $current[1] -eq $pl1 -and $current[2] -eq $pl2
}
function Performance {
    $attempted = $false; $previousMode = 0; $previousLimits = $null
    try {
        $package = ReadPackage; $previousMode = $package[3] -band 0x0F
        if ($request.token -cne $script:token -or
            ($request.expectedMode -isnot [int] -and $request.expectedMode -isnot [long]) -or
            $request.expectedMode -ne $previousMode) {Result $false 'profile_changed'; return}
        if ($request.action -eq 'mode') {
            if (($request.mode -isnot [int] -and $request.mode -isnot [long]) -or
                @(1,2,6) -notcontains $request.mode) {Result $false 'invalid_request'; return}
            if ($request.mode -eq $previousMode) {Result $true ''; return}
            $latest = ReadPackage
            if (($latest[3] -band 0x0F) -ne $previousMode) {Result $false 'profile_changed'; return}
            if ($clock.ElapsedMilliseconds -gt 4000) {Result $false 'preflight_timeout'; return}
            $attempted = $true; WriteMode $latest $request.mode
            $after = ReadPackage
            if (($after[3] -band 0x0F) -eq $request.mode) {Result $true ''; return}
        } elseif ($request.action -eq 'power') {
            if (!$script:tdpAvailable -or $previousMode -ne 6) {Result $false 'manual_mode_required'; return}
            foreach ($key in @('pl1','pl2','expectedPl1','expectedPl2')) {
                if ($request.$key -isnot [int] -and $request.$key -isnot [long]) {Result $false 'invalid_request'; return}
            }
            if ($request.pl1 -lt 8 -or $request.pl1 -gt 35 -or
                $request.pl2 -lt ($request.pl1 + 2) -or $request.pl2 -gt 45) {Result $false 'invalid_request'; return}
            $previousLimits = ReadLimits
            if (!(SameLimits $previousLimits $request.expectedPl1 $request.expectedPl2)) {Result $false 'profile_changed'; return}
            if (SameLimits $previousLimits $request.pl1 $request.pl2) {Result $true ''; return}
            $latest = ReadLimits
            if (!(SameLimits $latest $request.expectedPl1 $request.expectedPl2)) {Result $false 'profile_changed'; return}
            $latestMode = ReadPackage
            if (($latestMode[3] -band 0x0F) -ne 6) {Result $false 'profile_changed'; return}
            if ($clock.ElapsedMilliseconds -gt 4000) {Result $false 'preflight_timeout'; return}
            $attempted = $true; WriteLimits $request.pl1 $request.pl2
            $after = ReadLimits; $afterMode = ReadPackage
            if ((SameLimits $after $request.pl1 $request.pl2) -and ($afterMode[3] -band 0x0F) -eq 6) {Result $true ''; return}
        } else {Result $false 'invalid_request'; return}
        $failure = 'not_confirmed'
    } catch {$failure = 'unavailable'; if ($attempted) {$failure = 'write_failed'}}
    if ($attempted) {
        try {
            if ($request.action -eq 'mode') {
                $latest = ReadPackage; $mode = $latest[3] -band 0x0F
                if ($mode -ne $previousMode) {
                    if ($mode -ne $request.mode) {$failure = 'profile_changed'}
                    else {
                        WriteMode $latest $previousMode; $restored = ReadPackage
                        if (($restored[3] -band 0x0F) -ne $previousMode) {$failure = 'restore_failed'}
                    }
                }
            } else {
                $liveMode = ReadPackage
                if (($liveMode[3] -band 0x0F) -ne 6) {$failure = 'profile_changed'}
                else {
                    $latest = ReadLimits
                    if (!(SameLimits $latest $previousLimits[1] $previousLimits[2])) {
                        if (!(SameLimits $latest $request.pl1 $request.pl2)) {$failure = 'profile_changed'}
                        else {
                            $liveMode = ReadPackage
                            if (($liveMode[3] -band 0x0F) -ne 6) {$failure = 'profile_changed'}
                            else {
                                WriteLimits $previousLimits[1] $previousLimits[2]; $restored = ReadLimits
                                if (!(SameLimits $restored $previousLimits[1] $previousLimits[2])) {$failure = 'restore_failed'}
                            }
                        }
                    }
                }
            }
        } catch {$failure = 'restore_failed'}
    }
    Result $false $failure
}
function Result([bool]$ok, [string]$code) {
    $report = @{available=$false;ok=$ok;code=$code}
    if ($null -ne $script:token -and $null -ne $script:instance) {
        try {
            $package = ReadPackage
            $report = @{available=$true;ok=$ok;code=$code;provider='msi-claw-8-ex';token=$script:token;
                chargeLimit=($package[5] -band 0x7F);mode=($package[3] -band 0x0F);tdpAvailable=$false}
            if ($script:tdpAvailable) {
                try {
                    $limits = ReadLimits
                    $report['tdpAvailable'] = $true; $report['pl1'] = [int]$limits[1]; $report['pl2'] = [int]$limits[2]
                } catch {}
            }
            if ($ok -and (($request.action -eq 'charge' -and $report.chargeLimit -ne $request.percent) -or
                ($request.action -eq 'mode' -and $report.mode -ne $request.mode) -or
                ($request.action -eq 'power' -and (!$report.tdpAvailable -or $report.mode -ne 6 -or
                    $report.pl1 -ne $request.pl1 -or $report.pl2 -ne $request.pl2)))) {
                $report['ok'] = $false; $report['code'] = 'profile_changed'
            }
        } catch {}
    }
    $report | ConvertTo-Json -Compress -Depth 4
}
function Operate {
try {
    $bios = Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS'
    $manufacturer = (([string]$bios.SystemManufacturer).ToLowerInvariant() -replace '[^a-z0-9]', '')
    $model = (([string]$bios.SystemProductName).ToLowerInvariant() -replace '[^a-z0-9]', '')
    if (@('msi','microstarinternationalcoltd') -notcontains $manufacturer -or
        @('ms1t91','claw8exaicg3em') -notcontains $model) {throw 'Different device'}
    $script:mutex = [Threading.Mutex]::new($false, 'Local\Playhub.MsiClawFirmware')
    try {$script:locked = $mutex.WaitOne(100)} catch [Threading.AbandonedMutexException] {$script:locked = $true}
    if (!$locked) {Result $false 'busy'; return}
    $class = Get-CimClass -Namespace root/wmi -ClassName MSI_ACPI -OperationTimeoutSec 1
    if ($class.CimClassQualifiers['Provider'].Value -ine 'WmiProv' -or
        $class.CimClassQualifiers['Dynamic'].Value -ne $true -or
        [guid]$class.CimClassQualifiers['guid'].Value -ne [guid]'abbc0f6e-8ea1-11d1-00a0-c90629100000' -or
        !(Schema $class.CimClassMethods['Get_AP'] 25) -or !(Schema $class.CimClassMethods['Set_AP'] 26)) {throw 'Different firmware schema'}
    $script:tdpAvailable = (Schema $class.CimClassMethods['Get_SlaveBattery'] 11) -and
        (Schema $class.CimClassMethods['Set_SlaveBattery'] 12)
    $script:packageClass = Get-CimClass -Namespace root/wmi -ClassName Package_32 -OperationTimeoutSec 1
    if ([guid]$packageClass.CimClassQualifiers['guid'].Value -ne [guid]'abbc0f63-8ea1-11d1-00a0-c90629100000' -or
        $packageClass.CimClassProperties['Bytes'].CimType.ToString() -ne 'UInt8Array') {throw 'Different package schema'}
    $provider = @(Get-CimInstance -Namespace root/wmi -ClassName __Win32Provider -Filter "Name='WmiProv'" -OperationTimeoutSec 1)
    if ($provider.Count -ne 1 -or [guid]$provider[0].CLSID -ne [guid]'d2d588b5-d081-11d0-99e0-00c04fc2f8ec' -or
        $provider[0].HostingModel -ine 'LocalSystemHost') {throw 'Different WMI provider'}
    $registration = Get-ItemProperty -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{D2D588B5-D081-11d0-99E0-00C04FC2F8EC}\InprocServer32'
    $path = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($registration.'(default)'))
    if ($path -ine [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll'))) {throw 'Different provider path'}
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {throw 'Untrusted provider'}
    $instances = @(Get-CimInstance -Namespace root/wmi -ClassName MSI_ACPI -OperationTimeoutSec 1 | Where-Object {$_.Active -eq $true})
    if ($instances.Count -ne 1 -or !([string]$instances[0].InstanceName).Trim()) {throw 'Ambiguous firmware instance'}
    $script:instance = $instances[0]
    $identity = @($manufacturer,$model,[string]$bios.BIOSVersion,[string]$instance.InstanceName,
        [string]$class.CimClassQualifiers['guid'].Value,(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) | ConvertTo-Json -Compress
    $hash = [Security.Cryptography.SHA256]::Create()
    try {$script:token = -join ($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity)) | ForEach-Object {$_.ToString('x2')})} finally {$hash.Dispose()}
    $before = ReadPackage
    $previous = $before[5] -band 0x7F
    if ($request.action -eq 'get') {Result $true ''; return}
    if (@('mode','power') -contains $request.action) {Performance; return}
    if ($request.action -ne 'charge' -or $request.token -cne $token -or
        ($request.percent -isnot [int] -and $request.percent -isnot [long]) -or $request.percent -lt 20 -or $request.percent -gt 100 -or
        ($request.expected -isnot [int] -and $request.expected -isnot [long]) -or $request.expected -ne $previous) {Result $false 'profile_changed'; return}
    $latest = ReadPackage
    if (($latest[5] -band 0x7F) -ne $previous) {Result $false 'profile_changed'; return}
    if ($request.percent -eq $previous) {Result $true ''; return}
    if ($clock.ElapsedMilliseconds -gt 4000) {Result $false 'preflight_timeout'; return}
    $attempted = $true
    WriteCharge $latest $request.percent
    if (Confirm $request.percent) {Result $true ''; return}
    $failure = 'not_confirmed'
} catch {
    $failure = 'unavailable'
    if ($attempted) {$failure = 'write_failed'}
}
if ($attempted) {
    try {
        $latest = ReadPackage
        $current = $latest[5] -band 0x7F
        if ($current -ne $previous) {
            if ($current -ne $request.percent) {$failure = 'profile_changed'}
            else {WriteCharge $latest $previous; if (!(Confirm $previous)) {$failure = 'restore_failed'}}
        }
    } catch {$failure = 'restore_failed'}
}
Result $false $failure
}
try {Operate} finally {
    if ($locked) {$mutex.ReleaseMutex()}
    if ($null -ne $mutex) {$mutex.Dispose()}
}
'''


class MsiClawFirmware:
    def __init__(self, read_identity, run_cmd, powershell_path, windows=None):
        self._read_identity = read_identity
        self._run_cmd = run_cmd
        self._powershell_path = powershell_path
        self._windows = os.name == "nt" if windows is None else windows
        self._lock = threading.Lock()

    def _run(self, request):
        if not self._windows or not supports_identity(self._read_identity()):
            return {"available": False, "ok": False, "code": "unavailable"}
        payload = base64.b64encode(json.dumps(request, separators=(",", ":")).encode()).decode("ascii")
        try:
            with tempfile.TemporaryDirectory(prefix="Playhub-MSI-Firmware-") as directory:
                script = Path(directory) / "firmware.ps1"
                with script.open("x", encoding="utf-8-sig") as stream:
                    stream.write(_SCRIPT)
                code, output, _ = self._run_cmd([self._powershell_path(), "-NoProfile", "-NonInteractive",
                    "-ExecutionPolicy", "Bypass", "-File", str(script), "-RequestBase64", payload],
                    timeout=6 if request["action"] == "get" else 24)
            report = json.loads(output.lstrip("\ufeff")) if code == 0 and len(output) <= 4096 else None
            if (isinstance(report, dict) and report.get("available") is True and report.get("provider") == "msi-claw-8-ex"
                    and isinstance(report.get("token"), str) and _TOKEN.fullmatch(report["token"])
                    and type(report.get("chargeLimit")) is int and 20 <= report["chargeLimit"] <= 100
                    and type(report.get("ok")) is bool and isinstance(report.get("code"), str)):
                # Optional power capability needs both the native schema and valid measured limits.
                report["tdpAvailable"] = (report.get("tdpAvailable") is True and
                    type(report.get("mode")) is int and report["mode"] in (1, 2, 6) and
                    type(report.get("pl1")) is int and 8 <= report["pl1"] <= 35 and
                    type(report.get("pl2")) is int and report["pl1"] + 2 <= report["pl2"] <= 45)
                if type(report.get("mode")) is not int or report["mode"] not in (1, 2, 6):
                    return {"available": False, "ok": False, "code": "unavailable"}
                return report
        except (OSError, ValueError, TypeError):
            pass
        return {"available": False, "ok": False, "code": "unavailable"}

    def get(self):
        with self._lock:
            return self._run({"action": "get"})

    def set_charge_limit(self, request):
        if (not isinstance(request, dict) or not isinstance(request.get("token"), str)
                or not _TOKEN.fullmatch(request["token"]) or type(request.get("percent")) is not int
                or not 20 <= request["percent"] <= 100 or type(request.get("expected")) is not int
                or not 20 <= request["expected"] <= 100):
            return {"ok": False, "code": "invalid_request"}
        with self._lock:
            return self._run({"action": "charge", "token": request["token"], "percent": request["percent"], "expected": request["expected"]})

    def set_power_mode(self, request):
        if (not isinstance(request, dict) or not isinstance(request.get("token"), str)
                or not _TOKEN.fullmatch(request["token"]) or type(request.get("mode")) is not int
                or request["mode"] not in (1, 2, 6) or type(request.get("expectedMode")) is not int
                or request["expectedMode"] not in (1, 2, 6)):
            return {"ok": False, "code": "invalid_request"}
        with self._lock:
            return self._run({"action": "mode", "token": request["token"], "mode": request["mode"], "expectedMode": request["expectedMode"]})

    def set_power_limits(self, request):
        if (not isinstance(request, dict) or not isinstance(request.get("token"), str)
                or not _TOKEN.fullmatch(request["token"]) or type(request.get("expectedMode")) is not int
                or request["expectedMode"] != 6 or any(type(request.get(key)) is not int
                    for key in ("pl1", "pl2", "expectedPl1", "expectedPl2"))
                or not 8 <= request["pl1"] <= 35 or not request["pl1"] + 2 <= request["pl2"] <= 45
                or not 8 <= request["expectedPl1"] <= 35 or not request["expectedPl1"] + 2 <= request["expectedPl2"] <= 45):
            return {"ok": False, "code": "invalid_request"}
        with self._lock:
            return self._run({"action": "power", **{key: request[key]
                for key in ("token", "expectedMode", "pl1", "pl2", "expectedPl1", "expectedPl2")}})
