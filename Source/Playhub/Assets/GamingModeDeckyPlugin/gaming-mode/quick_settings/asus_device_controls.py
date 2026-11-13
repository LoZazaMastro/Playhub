"""Read-only ASUS Ally fan telemetry through the installed firmware provider.

Independent protocol implementation. Endpoint IDs and units are documented by
seerge/g-helper at 0bda6b55317f67c0ad68f3b5366a0e12a9096c53:
AsusACPI.cs GetFan and HardwareControl.cs ReadSensorsOverlay. No G-Helper or
Handheld Companion implementation is included. Battery charge-limit readback
is not provided by this protocol, so it deliberately exposes no charge control.

Temperature endpoint/units reference: the same G-Helper revision, AsusACPI.cs
Temp_CPU/Temp_GPU and DeviceGet, HardwareControl.cs GetCPUTemp/GetGPUTemp.
All four already-admitted Ally models may probe these separate DSTS sensors;
only actual valid samples expose temperature capability. No CPU-to-GPU fallback.
The strict presence/plausibility decoding from the earlier RC73XA Panel reference
(6c08dc6bcd77c90f54d9a217294f621a7aa93db6) is retained. RC74XA stays unavailable.
No upstream implementation, driver or service is included.
"""
import json
import os
import re
import tempfile
import threading
import time
from pathlib import Path


_TOKEN = re.compile(r"^[a-f0-9]{64}$")
_MODELS = frozenset(("rc71l", "rogallyrc71lrc71l", "rc72la", "rogallyxrc72larc72la",
                    "rc73ya", "rogxboxallyrc73yarc73ya", "rc73xa", "rogxboxallyxrc73xarc73xa"))


def supports_identity(identity):
    normalize = lambda value: re.sub(r"[^a-z0-9]", "", value.lower()) if isinstance(value, str) else ""
    return (isinstance(identity, dict) and normalize(identity.get("manufacturer")) in ("asus", "asustekcomputerinc")
            and normalize(identity.get("model")) in _MODELS)


def supports_temperature_identity(identity):
    return supports_identity(identity)


def _copy_report(report):
    copied = {**report, "fans": dict(report["fans"]), "capabilities": dict(report["capabilities"])}
    if "temperatures" in report:
        copied["temperatures"] = dict(report["temperatures"])
    return copied


_SCRIPT = r'''
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$mutex = $null; $locked = $false
function CimParameter($method, [string]$name, [bool]$inputParameter) {
    $value = $method.Parameters[$name]
    if ($null -eq $value -or $value.CimType.ToString() -ne 'UInt32') {return $false}
    if ($inputParameter) {return $value.Qualifiers['In'].Value -eq $true -and $value.Qualifiers['Out'].Value -ne $true}
    return $value.Qualifiers['Out'].Value -eq $true -and $value.Qualifiers['In'].Value -ne $true
}
function ReadFan([uint32]$endpoint) {
    try {
        $response = Invoke-CimMethod -InputObject $script:instance -MethodName DSTS -Arguments @{Device_ID=$endpoint} -OperationTimeoutSec 1
        $raw = $response.device_status
        if (($raw -isnot [uint32] -and $raw -isnot [int] -and $raw -isnot [long]) -or
            $raw -lt 0 -or $raw -gt [uint32]::MaxValue -or ($raw -band 0x10000) -eq 0 -or ($raw -band 0x80000) -ne 0) {return $null}
        $hundreds = $raw -band 0xFFFF
        if ($hundreds -gt 120) {return $null}
        return [int]($hundreds * 100)
    } catch {return $null}
}
function ReadTemperature([uint32]$endpoint) {
    try {
        $response = Invoke-CimMethod -InputObject $script:instance -MethodName DSTS -Arguments @{Device_ID=$endpoint} -OperationTimeoutSec 1
        $raw = $response.device_status
        if (($raw -isnot [uint32] -and $raw -isnot [int] -and $raw -isnot [long]) -or
            $raw -lt 0 -or $raw -gt [uint32]::MaxValue -or ($raw -band 0xFFFF0000L) -ne 0x00010000) {return $null}
        $celsius = $raw -band 0xFFFF
        if ($celsius -lt 1 -or $celsius -gt 125) {return $null}
        return [int]$celsius
    } catch {return $null}
}
function Operate {
    $report = @{available=$false;ok=$false;code='unavailable'}
    try {
        $bios = Get-ItemProperty -LiteralPath 'Registry::HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS'
        $manufacturer = (([string]$bios.SystemManufacturer).ToLowerInvariant() -replace '[^a-z0-9]', '')
        $model = (([string]$bios.SystemProductName).ToLowerInvariant() -replace '[^a-z0-9]', '')
        if (@('asus','asustekcomputerinc') -notcontains $manufacturer -or
            @('rc71l','rogallyrc71lrc71l','rc72la','rogallyxrc72larc72la','rc73ya','rogxboxallyrc73yarc73ya','rc73xa','rogxboxallyxrc73xarc73xa') -notcontains $model) {throw 'Different device'}
        $script:mutex = [Threading.Mutex]::new($false, 'Local\Playhub.AsusAllyFirmware')
        try {$script:locked=$mutex.WaitOne(100)} catch [Threading.AbandonedMutexException] {$script:locked=$true}
        if (!$locked) {$report.code='busy';return $report}
        $class = Get-CimClass -Namespace root/wmi -ClassName AsusAtkWmi_WMNB -OperationTimeoutSec 1
        $method = $class.CimClassMethods['DSTS']
        if ($class.CimClassQualifiers['Provider'].Value -ine 'WmiProv' -or $class.CimClassQualifiers['Dynamic'].Value -ne $true -or
            [guid]$class.CimClassQualifiers['guid'].Value -ne [guid]'97845ed0-4e6d-11de-8a39-0800200c9a66' -or
            $null -eq $method -or $method.Qualifiers['Implemented'].Value -ne $true -or
            @($method.Qualifiers['Privileges'].Value | Where-Object {$_}).Count -ne 0 -or $method.Parameters.Count -ne 2 -or
            !(CimParameter $method 'Device_ID' $true) -or !(CimParameter $method 'device_status' $false)) {throw 'Different firmware schema'}
        $provider = @(Get-CimInstance -Namespace root/wmi -ClassName __Win32Provider -Filter "Name='WmiProv'" -OperationTimeoutSec 1)
        if ($provider.Count -ne 1 -or [guid]$provider[0].CLSID -ne [guid]'d2d588b5-d081-11d0-99e0-00c04fc2f8ec' -or
            $provider[0].HostingModel -ine 'LocalSystemHost') {throw 'Different provider'}
        $registration = Get-ItemProperty -LiteralPath 'Registry::HKEY_CLASSES_ROOT\CLSID\{D2D588B5-D081-11D0-99E0-00C04FC2F8EC}\InprocServer32'
        $path = [IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($registration.'(default)'))
        if ($path -ine [IO.Path]::GetFullPath((Join-Path $env:SystemRoot 'System32\wbem\wmiprov.dll'))) {throw 'Different provider path'}
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {throw 'Untrusted provider'}
        $instances = @(Get-CimInstance -Namespace root/wmi -ClassName AsusAtkWmi_WMNB -OperationTimeoutSec 1 | Where-Object {$_.Active -eq $true})
        if ($instances.Count -ne 1 -or !([string]$instances[0].InstanceName).Trim()) {throw 'Ambiguous firmware instance'}
        $script:instance=$instances[0]
        $cpu=ReadFan ([uint32]0x00110013)
        $gpu=ReadFan ([uint32]0x00110014)
        # Exact model/provider admission above applies to both independent sensors.
        $temperatureModel = $true
        $cpuC=$null; $gpuC=$null
        if ($temperatureModel) {
            $cpuC=ReadTemperature ([uint32]0x00120094)
            $gpuC=ReadTemperature ([uint32]0x00120097)
        }
        $fanAvailable=$null -ne $cpu -or $null -ne $gpu
        $temperatureAvailable=$null -ne $cpuC -or $null -ne $gpuC
        if (!$fanAvailable -and !$temperatureAvailable) {return $report}
        $identity = @($manufacturer,$model,[string]$bios.BIOSVersion,[string]$instance.InstanceName,
            [string]$class.CimClassQualifiers['guid'].Value,(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) | ConvertTo-Json -Compress
        $hash = [Security.Cryptography.SHA256]::Create()
        try {$token = -join ($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity)) | ForEach-Object {$_.ToString('x2')})} finally {$hash.Dispose()}
        $result=@{available=$true;ok=$true;code='';provider='asus-ally';token=$token;
            capabilities=@{fan_rpm=$fanAvailable;charge_limit=$false};fans=@{cpu_rpm=$cpu;gpu_rpm=$gpu}}
        if ($temperatureModel) {
            $result.capabilities.temperature_c=$temperatureAvailable
            $result.temperatures=@{cpu_c=$cpuC;gpu_c=$gpuC}
        }
        return $result
    } catch {return $report}
}
try {Operate | ConvertTo-Json -Compress -Depth 4} finally {
    if ($locked) {$mutex.ReleaseMutex()}
    if ($null -ne $mutex) {$mutex.Dispose()}
}
'''


class AsusDeviceControls:
    def __init__(self, read_identity, run_cmd, powershell_path, windows=None, clock=None, wall_clock=None):
        self._read_identity, self._run_cmd, self._powershell_path = read_identity, run_cmd, powershell_path
        self._windows = os.name == "nt" if windows is None else windows
        self._clock, self._wall_clock = clock or time.monotonic, wall_clock or time.time
        self._lock = threading.Lock()
        self._cached = None
        self._cached_at = 0
        self._cached_identity = None

    def get(self):
        unavailable = {"available": False, "ok": False, "code": "unavailable"}
        with self._lock:
            try:
                identity = self._read_identity()
            except (OSError, ValueError, TypeError):
                self._cached = None
                return unavailable
            if not self._windows or not supports_identity(identity):
                self._cached = None
                return unavailable
            now = self._clock()
            if self._cached is not None and identity == self._cached_identity and 0 <= now - self._cached_at < 2:
                return _copy_report(self._cached)
            self._cached = None
            try:
                with tempfile.TemporaryDirectory(prefix="Playhub-ASUS-Sensors-") as directory:
                    script = Path(directory) / "fans.ps1"
                    with script.open("x", encoding="utf-8-sig") as stream:
                        stream.write(_SCRIPT)
                    code, output, _ = self._run_cmd([self._powershell_path(), "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                        "-File", str(script)], timeout=8)
                report = json.loads(output.lstrip("\ufeff")) if code == 0 and len(output) <= 4096 else None
                if not (isinstance(report, dict) and report.get("available") is True and report.get("ok") is True
                        and report.get("provider") == "asus-ally" and isinstance(report.get("token"), str)
                        and _TOKEN.fullmatch(report["token"]) and isinstance(report.get("capabilities"), dict)
                        and type(report["capabilities"].get("fan_rpm")) is bool and report["capabilities"].get("charge_limit") is False
                        and isinstance(report.get("fans"), dict) and set(report["fans"]) == {"cpu_rpm", "gpu_rpm"}):
                    return unavailable
                fans = report["fans"]
                if any(value is not None and
                        (type(value) is not int or not 0 <= value <= 12000 or value % 100 != 0) for value in fans.values()):
                    return unavailable
                fan_available = any(value is not None for value in fans.values())
                if report["capabilities"]["fan_rpm"] is not fan_available:
                    return unavailable
                temperatures = None
                temperature_available = False
                if "temperatures" in report or "temperature_c" in report["capabilities"]:
                    temperatures = report.get("temperatures")
                    if (not supports_temperature_identity(identity) or not isinstance(temperatures, dict)
                            or set(temperatures) != {"cpu_c", "gpu_c"} or any(value is not None and
                            (type(value) is not int or not 1 <= value <= 125) for value in temperatures.values())):
                        return unavailable
                    temperature_available = any(value is not None for value in temperatures.values())
                    if report["capabilities"].get("temperature_c") is not temperature_available:
                        return unavailable
                if not fan_available and not temperature_available:
                    return unavailable
                if self._read_identity() != identity:
                    return unavailable
                result = {"available": True, "ok": True, "code": "", "provider": "asus-ally", "token": report["token"],
                    "capabilities": {"fan_rpm": fan_available, "charge_limit": False}, "fans": dict(fans),
                    "sampled_at_ms": int(self._wall_clock() * 1000)}
                if temperatures is not None:
                    result["temperatures"] = dict(temperatures)
                    result["capabilities"]["temperature_c"] = temperature_available
                self._cached_at, self._cached_identity = self._clock(), dict(identity)
                self._cached = result
                return _copy_report(result)
            except (OSError, ValueError, TypeError):
                return unavailable
