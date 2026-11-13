"""Original Legion Go firmware presets through its Windows ACPI-WMI interface.

Protocol references: GoTweaks 99ed5f5f8c22bb78bfffee9b37f9a04094f30482
LenovoWMIService (MIT), and hhd-dev/hwinfo 601fe44fc660019c01775b1777714c0759b87fe5.
"""
import base64
import json
import os
import re
import threading
import tempfile
from pathlib import Path


_MODELS = frozenset(("83e1", "legiongo8apu1"))
_MODES = frozenset((1, 2, 3))
_TOKEN = re.compile(r"^[a-f0-9]{64}$")


def _identifier(value):
    return re.sub(r"[^a-z0-9]", "", value.lower()) if isinstance(value, str) else ""


def supports_identity(identity):
    return (isinstance(identity, dict) and _identifier(identity.get("manufacturer")) == "lenovo"
            and _identifier(identity.get("model")) in _MODELS)


# The protocol deliberately exposes one setting. Class, method, parameter and
# provider paths are constants; client input cannot select a WMI operation.
_SCRIPT = r'''
param([Parameter(Mandatory=$true)][string]$RequestBase64)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$request = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RequestBase64)) | ConvertFrom-Json
$operation = [Diagnostics.Stopwatch]::StartNew()
$className = 'LENOVO_GAMEZONE_DATA'
$instance = $null
$before = $null
$attempted = $false
$mutex = $null
$locked = $false
function Result([bool]$ok, [string]$code) {
    $report = @{available=$false;ok=$ok;code=$code}
    if ($null -ne $script:instance -and $null -ne $script:before) {
        try {$mode = ReadMode} catch {$mode = $null}
        if ($null -ne $mode -and (@(1,2,3,255) -contains $mode)) {
            $report = @{available=$true;ok=$ok;code=$code;provider='lenovo-legion-go';mode=$mode;
                modes=@($script:allowed);token=$script:token;editable=($script:allowed -contains $mode)}
            if ($ok -and $request.action -eq 'set' -and $mode -ne $request.mode) {
                $report['ok'] = $false; $report['code'] = 'profile_changed'
            }
        }
    }
    $report | ConvertTo-Json -Compress -Depth 4
}
function Schema($method, [int]$id, [string]$direction) {
    if ($null -eq $method -or $method.Qualifiers['WmiMethodId'].Value -ne $id -or
        $method.Qualifiers['Implemented'].Value -ne $true -or $method.Parameters.Count -ne 1) {return $false}
    $parameter = $method.Parameters['Data']
    if ($null -eq $parameter -or $parameter.CimType.ToString() -ne 'UInt32' -or
        $parameter.Qualifiers[$direction].Value -ne $true) {return $false}
    $opposite = 'In'; if ($direction -eq 'In') {$opposite = 'Out'}
    if ($parameter.Qualifiers[$opposite].Value -eq $true) {return $false}
    return @($method.Qualifiers['Privileges'].Value | Where-Object {$_}).Count -eq 0
}
function ReadMode {
    $result = Invoke-CimMethod -InputObject $script:instance -MethodName GetSmartFanMode -OperationTimeoutSec 1
    if ($null -eq $result.Data -or $result.Data -isnot [uint32]) {throw 'Invalid mode response'}
    return [int]$result.Data
}
function WriteMode([int]$mode) {
    if ($script:allowed -notcontains $mode) {throw 'Invalid firmware preset'}
    $null = Invoke-CimMethod -InputObject $script:instance -MethodName SetSmartFanMode -Arguments @{Data=[uint32]$mode} -OperationTimeoutSec 1
}
function Confirm([int]$mode) {
    $wait = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ((ReadMode) -eq $mode) {return $true}
        Start-Sleep -Milliseconds 60
    } while ($wait.ElapsedMilliseconds -lt 600)
    return $false
}
try {
    $mutex = [Threading.Mutex]::new($false, 'Local\Playhub.LenovoGoFirmware')
    try {$locked = $mutex.WaitOne(100)} catch [Threading.AbandonedMutexException] {$locked = $true}
    if (!$locked) {Result $false 'busy'; exit}
try {
    $bios = Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS'
    $manufacturer = ([string]$bios.SystemManufacturer).Trim()
    $model = ([string]$bios.SystemProductName).Trim()
    $modelKey = $model.ToLowerInvariant() -replace '[^a-z0-9]', ''
    if ($manufacturer -ine 'LENOVO' -or @('83e1','legiongo8apu1') -notcontains $modelKey) {throw 'Different device'}
    $class = Get-CimClass -Namespace root/wmi -ClassName $className -OperationTimeoutSec 1
    if ($class.CimClassQualifiers['Provider'].Value -ine 'WmiProv' -or
        $class.CimClassQualifiers['Dynamic'].Value -ne $true -or
        [guid]$class.CimClassQualifiers['guid'].Value -ne [guid]'887b54e3-dddc-4b2c-8b88-68a26a8835d0' -or
        !(Schema $class.CimClassMethods['IsSupportSmartFan'] 43 'Out') -or
        !(Schema $class.CimClassMethods['GetSmartFanMode'] 45 'Out') -or
        !(Schema $class.CimClassMethods['SetSmartFanMode'] 44 'In')) {throw 'Different firmware schema'}
    $provider = @(Get-CimInstance -Namespace root/wmi -ClassName __Win32Provider -Filter "Name='WmiProv'" -OperationTimeoutSec 1)
    if ($provider.Count -ne 1 -or [guid]$provider[0].CLSID -ne [guid]'d2d588b5-d081-11d0-99e0-00c04fc2f8ec' -or
        $provider[0].HostingModel -ine 'LocalSystemHost') {throw 'Different WMI provider'}
    $registration = Get-ItemProperty -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{D2D588B5-D081-11d0-99E0-00C04FC2F8EC}\InprocServer32'
    $providerPath = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($registration.'(default)'))
    $expectedPath = [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll'))
    if ($providerPath -ine $expectedPath) {throw 'Different provider path'}
    $signature = Get-AuthenticodeSignature -LiteralPath $providerPath
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {throw 'Untrusted provider'}
    $instances = @(Get-CimInstance -Namespace root/wmi -ClassName $className -OperationTimeoutSec 1 | Where-Object {$_.Active -eq $true})
    if ($instances.Count -ne 1 -or !([string]$instances[0].InstanceName).Trim()) {throw 'Ambiguous firmware instance'}
    $instance = $instances[0]
    $support = Invoke-CimMethod -InputObject $instance -MethodName IsSupportSmartFan -OperationTimeoutSec 1
    # IsSupportSmartFan reports an interface version. This adapter is validated
    # against the original Go's version 6 contract, not other Lenovo firmware.
    if ($support.Data -isnot [uint32] -or $support.Data -ne 6) {throw 'Different firmware version'}
    $allowed = @(1,2,3)
    $valueMap = @($class.CimClassMethods['SetSmartFanMode'].Parameters['Data'].Qualifiers['ValueMap'].Value | Where-Object {$_ -ne $null})
    if ($valueMap.Count -gt 0) {$allowed = @($allowed | Where-Object {$valueMap -contains ([string]$_)})}
    if ($allowed.Count -lt 2) {throw 'Firmware presets unavailable'}
    $tokenData = @($manufacturer,$model,[string]$bios.BIOSVersion,[string]$instance.InstanceName,
        [string]$class.CimClassQualifiers['guid'].Value,(Get-FileHash -LiteralPath $providerPath -Algorithm SHA256).Hash) | ConvertTo-Json -Compress
    $hash = [Security.Cryptography.SHA256]::Create()
    try {$token = -join ($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($tokenData)) | ForEach-Object {$_.ToString('x2')})} finally {$hash.Dispose()}
    $before = ReadMode
    if (@(1,2,3,255) -notcontains $before) {throw 'Unknown firmware preset'}
    if ($request.action -eq 'get') {Result $true ''; exit}
    if ($request.action -ne 'set' -or $request.token -cne $token -or ($request.mode -isnot [int] -and $request.mode -isnot [long]) -or
        $allowed -notcontains $request.mode -or ($request.expected -isnot [int] -and $request.expected -isnot [long]) -or $request.expected -ne $before -or
        $allowed -notcontains $before -or (ReadMode) -ne $before) {Result $false 'profile_changed'; exit}
    if ($before -eq $request.mode) {Result $true ''; exit}
    # Leave the outer helper budget free for Set, confirmation and rollback.
    if ($operation.ElapsedMilliseconds -gt 4000) {Result $false 'preflight_timeout'; exit}
    $attempted = $true
    WriteMode $request.mode
    if (Confirm $request.mode) {Result $true ''; exit}
    $failure = 'not_confirmed'
} catch {
    $failure = 'unavailable'
    if ($attempted) {$failure = 'write_failed'}
}
if ($attempted) {
    try {
        $current = ReadMode
        if ($current -ne $before) {
            if ($current -ne $request.mode) {$failure = 'profile_changed'}
            else {WriteMode $before; if (!(Confirm $before)) {$failure = 'restore_failed'}}
        }
    } catch {$failure = 'restore_failed'}
}
Result $false $failure
} finally {
    if ($locked) {$mutex.ReleaseMutex()}
    if ($null -ne $mutex) {$mutex.Dispose()}
}
'''


class LenovoPowerMode:
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
            with tempfile.TemporaryDirectory(prefix="Playhub-Lenovo-Power-") as directory:
                script = Path(directory) / "firmware-mode.ps1"
                with script.open("x", encoding="utf-8-sig") as stream:
                    stream.write(_SCRIPT)
                code, output, _ = self._run_cmd([self._powershell_path(), "-NoProfile", "-NonInteractive",
                    "-ExecutionPolicy", "Bypass", "-File", str(script), "-RequestBase64", payload],
                    timeout=16 if request["action"] == "set" else 6)
            report = json.loads(output.lstrip("\ufeff")) if code == 0 and len(output) <= 4096 else None
            if isinstance(report, dict) and report.get("available") is False:
                return {"available": False, "ok": False, "code": "unavailable"}
            if (not isinstance(report, dict) or report.get("available") is not True
                    or report.get("provider") != "lenovo-legion-go" or type(report.get("mode")) is not int
                    or report["mode"] not in (*_MODES, 255) or not isinstance(report.get("token"), str)
                    or _TOKEN.fullmatch(report["token"]) is None or type(report.get("ok")) is not bool
                    or not isinstance(report.get("modes"), list) or len(report["modes"]) < 2
                    or any(type(mode) is not int or mode not in _MODES for mode in report["modes"])
                    or len(set(report["modes"])) != len(report["modes"])
                    or type(report.get("editable")) is not bool
                    or report["editable"] != (report["mode"] in report["modes"])):
                raise ValueError("Invalid firmware response")
            return report
        except (OSError, ValueError, TypeError):
            return {"available": False, "ok": False, "code": "unavailable"}

    def get(self):
        with self._lock:
            return self._run({"action": "get"})

    def set(self, request):
        if (not isinstance(request, dict) or not isinstance(request.get("token"), str)
                or _TOKEN.fullmatch(request["token"]) is None or type(request.get("mode")) is not int
                or request["mode"] not in _MODES or type(request.get("expected")) is not int
                or request["expected"] not in _MODES):
            return {"ok": False, "code": "invalid_request"}
        with self._lock:
            return self._run({"action": "set", "token": request["token"], "mode": request["mode"], "expected": request["expected"]})
