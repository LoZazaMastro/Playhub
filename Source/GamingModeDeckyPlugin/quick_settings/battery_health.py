"""Estimate battery wear from matched Windows battery class capacity reports."""
import json
import threading
import time


_CAPACITIES = r"""
$ErrorActionPreference = 'Stop'
$design = @(Get-CimInstance -Namespace root/wmi -ClassName BatteryStaticData |
    Select-Object InstanceName, Active, Tag, DesignedCapacity)
$full = @(Get-CimInstance -Namespace root/wmi -ClassName BatteryFullChargedCapacity |
    Select-Object InstanceName, Active, Tag, FullChargedCapacity)
@{design = $design; full = $full} | ConvertTo-Json -Depth 3 -Compress
"""
_CACHE_SECONDS = 300


def _active_pack(rows):
    if not isinstance(rows, list):
        return None
    active = [row for row in rows if isinstance(row, dict) and row.get("Active") is True]
    return active[0] if len(active) == 1 else None


def battery_health_percent(report):
    if not isinstance(report, dict):
        return None
    design = _active_pack(report.get("design"))
    full = _active_pack(report.get("full"))
    if not design or not full:
        return None
    instance = design.get("InstanceName")
    other = full.get("InstanceName")
    if not isinstance(instance, str) or not instance.strip() or not isinstance(other, str) or instance.casefold() != other.casefold():
        return None
    tag, other_tag = design.get("Tag"), full.get("Tag")
    if (tag is not None or other_tag is not None) and (type(tag) is not int or type(other_tag) is not int
            or not 0 < tag <= 0xffffffff or tag != other_tag):
        return None
    rated = design.get("DesignedCapacity")
    measured = full.get("FullChargedCapacity")
    if any(type(value) is not int or not 0 < value < 0xffffffff for value in (rated, measured)):
        return None
    # Firmware may report a little above rated capacity on a healthy battery.
    # Hide implausible mismatches and cap the displayed estimate at 100%.
    if measured * 4 > rated * 5:
        return None
    return min(100, round(100 * measured / rated))


class BatteryHealth:
    def __init__(self, read_power, run_cmd, powershell_path):
        self._read_power = read_power
        self._run_cmd = run_cmd
        self._powershell_path = powershell_path
        self._lock = threading.Lock()
        self._sampled_at = None
        self._percent = None

    def get(self):
        with self._lock:
            if self._read_power().get("battery") is not True:
                self._sampled_at = self._percent = None
                return {"percent": None}
            if self._sampled_at is None or time.monotonic() - self._sampled_at >= _CACHE_SECONDS:
                self._percent = self._read()
                self._sampled_at = time.monotonic()
                if self._read_power().get("battery") is not True:
                    self._sampled_at = self._percent = None
            return {"percent": self._percent}

    def _read(self):
        try:
            code, output, _ = self._run_cmd(
                [self._powershell_path(), "-NoProfile", "-NonInteractive", "-Command", _CAPACITIES], timeout=4)
            report = json.loads(output.lstrip("\ufeff")) if code == 0 else None
        except (OSError, ValueError):
            return None
        return battery_health_percent(report)
