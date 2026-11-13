"""Explicit ASUS Ally BIOS profile commands, with no inferred current state.

Protocol reference: seerge/g-helper, AsusACPI.cs at
0bda6b55317f67c0ad68f3b5366a0e12a9096c53. This is an independent implementation;
no G-Helper implementation or binary is distributed. The firmware interface
acknowledges commands but does not expose reliable current-profile readback.
"""
import base64
import json
import os
import re
import tempfile
import threading
from pathlib import Path


_TOKEN = re.compile(r"^[a-f0-9]{64}$")
_MODELS = frozenset(("rc71l", "rogallyrc71lrc71l", "rc72la", "rogallyxrc72larc72la",
                    "rc73ya", "rogxboxallyrc73yarc73ya", "rc73xa", "rogxboxallyxrc73xarc73xa"))


def _identifier(value):
    return re.sub(r"[^a-z0-9]", "", value.lower()) if isinstance(value, str) else ""


def supports_identity(identity):
    return (isinstance(identity, dict) and _identifier(identity.get("manufacturer")) in ("asus", "asustekcomputerinc")
            and _identifier(identity.get("model")) in _MODELS)


_SCRIPT = r'''
param([Parameter(Mandatory=$true)][string]$RequestBase64)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$request = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RequestBase64)) | ConvertFrom-Json
$clock = [Diagnostics.Stopwatch]::StartNew()
$script:token = $null
$script:available = $false
$script:instance = $null
$mutex = $null; $locked = $false
function InputParameter($method, [string]$name) {
    $parameter = $method.Parameters[$name]
    return $null -ne $parameter -and $parameter.CimType.ToString() -eq 'UInt32' -and
        $parameter.Qualifiers['In'].Value -eq $true -and $parameter.Qualifiers['Out'].Value -ne $true
}
function OutputParameter($method, [string]$name) {
    $parameter = $method.Parameters[$name]
    return $null -ne $parameter -and $parameter.CimType.ToString() -eq 'UInt32' -and
        $parameter.Qualifiers['Out'].Value -eq $true -and $parameter.Qualifiers['In'].Value -ne $true
}
function Schema($method, [bool]$write) {
    if ($null -eq $method -or $method.Qualifiers['Implemented'].Value -ne $true -or
        @($method.Qualifiers['Privileges'].Value | Where-Object {$_}).Count -ne 0 -or
        !(InputParameter $method 'Device_ID')) {return $false}
    if ($write) {
        return $method.Parameters.Count -eq 3 -and (InputParameter $method 'Control_status') -and (OutputParameter $method 'result')
    }
    return $method.Parameters.Count -eq 2 -and (OutputParameter $method 'device_status')
}
function Present {
    $response = Invoke-CimMethod -InputObject $script:instance -MethodName DSTS -Arguments @{Device_ID=[uint32]0x00120075} -OperationTimeoutSec 1
    $value = $response.device_status
    if (($value -isnot [uint32] -and $value -isnot [int] -and $value -isnot [long]) -or
        $value -lt 0 -or $value -gt [uint32]::MaxValue) {return $false}
    # This proves endpoint presence only. Low bits are not a current-mode value.
    return ($value -band 0x10000) -ne 0 -and ($value -band 0x80000) -eq 0
}
function Result([bool]$ok, [string]$code) {
    $report = @{available=$script:available;ok=$ok;code=$code}
    if ($script:available) {
        $report['provider']='asus-ally'; $report['token']=$script:token
        $report['commands']=@(2,0,1); $report['acknowledged']=$ok -and $request.action -eq 'apply'
    }
    $report | ConvertTo-Json -Compress -Depth 4
}
function Operate {
    try {
        $bios = Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS'
        $manufacturer = (([string]$bios.SystemManufacturer).ToLowerInvariant() -replace '[^a-z0-9]', '')
        $model = (([string]$bios.SystemProductName).ToLowerInvariant() -replace '[^a-z0-9]', '')
        if (@('asus','asustekcomputerinc') -notcontains $manufacturer -or
            @('rc71l','rogallyrc71lrc71l','rc72la','rogallyxrc72larc72la','rc73ya','rogxboxallyrc73yarc73ya','rc73xa','rogxboxallyxrc73xarc73xa') -notcontains $model) {throw 'Different device'}
        $script:mutex = [Threading.Mutex]::new($false, 'Local\Playhub.AsusAllyFirmware')
        try {$script:locked = $mutex.WaitOne(100)} catch [Threading.AbandonedMutexException] {$script:locked = $true}
        if (!$locked) {Result $false 'busy'; return}
        $class = Get-CimClass -Namespace root/wmi -ClassName AsusAtkWmi_WMNB -OperationTimeoutSec 1
        if ($class.CimClassQualifiers['Provider'].Value -ine 'WmiProv' -or $class.CimClassQualifiers['Dynamic'].Value -ne $true -or
            [guid]$class.CimClassQualifiers['guid'].Value -ne [guid]'97845ed0-4e6d-11de-8a39-0800200c9a66' -or
            !(Schema $class.CimClassMethods['DSTS'] $false) -or !(Schema $class.CimClassMethods['DEVS'] $true)) {throw 'Different firmware schema'}
        $provider = @(Get-CimInstance -Namespace root/wmi -ClassName __Win32Provider -Filter "Name='WmiProv'" -OperationTimeoutSec 1)
        if ($provider.Count -ne 1 -or [guid]$provider[0].CLSID -ne [guid]'d2d588b5-d081-11d0-99e0-00c04fc2f8ec' -or
            $provider[0].HostingModel -ine 'LocalSystemHost') {throw 'Different WMI provider'}
        $registration = Get-ItemProperty -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{D2D588B5-D081-11d0-99E0-00C04FC2F8EC}\InprocServer32'
        $path = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($registration.'(default)'))
        if ($path -ine [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll'))) {throw 'Different provider path'}
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {throw 'Untrusted provider'}
        $instances = @(Get-CimInstance -Namespace root/wmi -ClassName AsusAtkWmi_WMNB -OperationTimeoutSec 1 | Where-Object {$_.Active -eq $true})
        if ($instances.Count -ne 1 -or !([string]$instances[0].InstanceName).Trim()) {throw 'Ambiguous firmware instance'}
        $script:instance = $instances[0]
        if (!(Present)) {throw 'Endpoint unavailable'}
        $identity = @($manufacturer,$model,[string]$bios.BIOSVersion,[string]$instance.InstanceName,
            [string]$class.CimClassQualifiers['guid'].Value,(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) | ConvertTo-Json -Compress
        $hash = [Security.Cryptography.SHA256]::Create()
        try {$script:token = -join ($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity)) | ForEach-Object {$_.ToString('x2')})} finally {$hash.Dispose()}
        $script:available = $true
        if ($request.action -eq 'get') {Result $true ''; return}
        if ($request.action -ne 'apply' -or $request.token -cne $script:token -or
            ($request.mode -isnot [int] -and $request.mode -isnot [long]) -or @(0,1,2) -notcontains $request.mode) {Result $false 'invalid_request'; return}
        if (!(Present)) {$script:available=$false; Result $false 'unavailable'; return}
        if ($clock.ElapsedMilliseconds -gt 4000) {Result $false 'preflight_timeout'; return}
        $response = Invoke-CimMethod -InputObject $script:instance -MethodName DEVS -Arguments @{
            Device_ID=[uint32]0x00120075;Control_status=[uint32]$request.mode} -OperationTimeoutSec 1
        $ack = $response.result
        if (($ack -is [uint32] -or $ack -is [int] -or $ack -is [long]) -and $ack -eq 1) {Result $true ''}
        else {Result $false 'command_refused'}
    } catch {Result $false 'unavailable'}
}
try {Operate} finally {
    if ($locked) {$mutex.ReleaseMutex()}
    if ($null -ne $mutex) {$mutex.Dispose()}
}
'''


class AsusPerformanceCommands:
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
            with tempfile.TemporaryDirectory(prefix="Playhub-ASUS-Commands-") as directory:
                script = Path(directory) / "performance.ps1"
                with script.open("x", encoding="utf-8-sig") as stream:
                    stream.write(_SCRIPT)
                code, output, _ = self._run_cmd([self._powershell_path(), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                    "-File", str(script), "-RequestBase64", payload], timeout=8)
            report = json.loads(output.lstrip("\ufeff")) if code == 0 and len(output) <= 4096 else None
            if (isinstance(report, dict) and report.get("available") is True and report.get("provider") == "asus-ally"
                    and isinstance(report.get("token"), str) and _TOKEN.fullmatch(report["token"])
                    and report.get("commands") == [2, 0, 1] and all(type(value) is int for value in report["commands"])
                    and type(report.get("ok")) is bool and type(report.get("acknowledged")) is bool
                    and isinstance(report.get("code"), str)):
                # Expose only command capability and acknowledgement, never an inferred mode.
                return {key: report[key] for key in ("available", "ok", "code", "provider", "token", "commands", "acknowledged")}
        except (OSError, ValueError, TypeError):
            pass
        return {"available": False, "ok": False, "code": "unavailable"}

    def get(self):
        with self._lock:
            return self._run({"action": "get"})

    def apply(self, request):
        if (not isinstance(request, dict) or not isinstance(request.get("token"), str)
                or not _TOKEN.fullmatch(request["token"]) or type(request.get("mode")) is not int or request["mode"] not in (0, 1, 2)):
            return {"ok": False, "code": "invalid_request"}
        with self._lock:
            return self._run({"action": "apply", "token": request["token"], "mode": request["mode"]})
