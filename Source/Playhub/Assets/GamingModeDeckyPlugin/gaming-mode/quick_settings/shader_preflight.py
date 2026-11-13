"""Read local graphics prerequisites and a bounded, source-verified game list."""

import ctypes
import json
import os
from pathlib import Path
import re
import sys
import threading
import time


_MICROSOFT_ASD = "https://devblogs.microsoft.com/directx/advanced-shader-delivery-is-available-now-across-hardware/"
_INTEL_PRECOMPILED = "https://www.intel.com/content/www/us/en/support/articles/000102440/graphics.html"
_SCSKILLER = "https://github.com/BlueHeisenberg/SCSKiller/blob/main/README.md"

# AMD uses parallel driver-store branches. These exact versions are published in
# the AMD Adrenalin 26.6.1 and 26.10.1 Windows release notes respectively.
_AMD_VERIFIED_DRIVERS = {(32, 0, 31019, 2002), (32, 0, 21043, 12001), (32, 0, 32015, 7029)}
_AMD_RX_MODELS = re.compile(r"\bRadeon\b.*\bRX\s+(?:(?:53|55|56|57|63|64|65|66|67|68|69|76|77|78|79)00|7650|9060|9070)(?:[MS])?\b", re.I)
_INTEL_ARC_MODELS = re.compile(r"\bArc(?:\(TM\)|™)?\s+(?:B570|B580|130V|140V)\b", re.I)
_NVIDIA_RTX_MODELS = re.compile(r"\bNVIDIA\b.*\bRTX\b", re.I)
_PCI_VENDORS = {"1002": "amd", "8086": "intel", "10de": "nvidia", "17cb": "qualcomm", "5143": "qualcomm"}

# Entries require both Intel's title listing and its exact Steam store AppID.
# Call of Duty's shared launcher does not identify which title is installed.
_INTEL_STEAM_TITLES = {
    990080: "Hogwarts Legacy",
    1091500: "Cyberpunk 2077",
    1285190: "Borderlands 4",
    1449110: "The Outer Worlds 2",
    1496790: "Gotham Knights",
    1643320: "S.T.A.L.K.E.R. 2: Heart of Chornobyl",
    1716740: "Starfield",
    2322010: "God of War Ragnarök",
    2358720: "Black Myth: Wukong",
    2623190: "The Elder Scrolls IV: Oblivion Remastered",
    3472040: "NBA 2K26",
}

_INVENTORY_SCRIPT = r"""
$gpu = @()
try {
    $gpu = @(Get-CimInstance Win32_VideoController -Property Name,PNPDeviceID,DriverVersion -ErrorAction Stop |
        Select-Object -First 16 Name,PNPDeviceID,DriverVersion)
} catch {}
$steam = @(Get-Process -Name steam -ErrorAction SilentlyContinue | Select-Object -First 4 -ExpandProperty Path)
@{ adapters = $gpu; steam = $steam } | ConvertTo-Json -Depth 3 -Compress
"""


def _driver_tuple(version):
    if not re.fullmatch(r"\d{1,5}(?:\.\d{1,5}){3}", version):
        return None
    return tuple(int(part) for part in version.split("."))


def _driver_status(known_model, driver, minimum):
    if not known_model or driver is None:
        return "unverified"
    return "eligible" if driver >= minimum else "unsupported"


def _adapter(raw, build):
    if not isinstance(raw, dict) or not isinstance(raw.get("Name"), str):
        return None
    name = raw["Name"].strip()[:200]
    if not name:
        return None
    pci = re.match(r"PCI\\VEN_([a-f0-9]{4})&", str(raw.get("PNPDeviceID", "")), re.I)
    vendor = _PCI_VENDORS.get(pci[1].lower(), "unknown") if pci else "unknown"
    version = str(raw.get("DriverVersion") or "").strip()[:30]
    driver = _driver_tuple(version)
    microsoft = "unverified"
    intel = "unverified"
    if vendor == "amd" and _AMD_RX_MODELS.search(name) and driver in _AMD_VERIFIED_DRIVERS:
        microsoft = "eligible"
    elif vendor == "intel":
        known_model = _INTEL_ARC_MODELS.search(name)
        microsoft = _driver_status(known_model, driver, (32, 0, 101, 9150))
        intel = _driver_status(known_model, driver, (32, 0, 101, 8626))
    elif vendor == "nvidia" and driver and driver[:2] == (32, 0) and len(version.split(".")[-1]) == 4:
        # NVIDIA's Windows driver 32.0.16.2101 corresponds to 621.01.
        microsoft = _driver_status(_NVIDIA_RTX_MODELS.search(name), driver, (32, 0, 16, 2101))
    if build is None:
        microsoft = intel = "unverified"
    elif build < 26100:
        microsoft = "unsupported"
        if build < 10240:
            intel = "unsupported"
    return {"name": name, "vendor": vendor, "driver_version": version if driver else None,
            "microsoft_asd": microsoft, "intel_precompiled": intel}


def _local_path(value):
    if not isinstance(value, str) or not value or len(value) > 2048 or "\x00" in value:
        return None
    path = Path(value)
    if not path.is_absolute():
        return None
    if os.name == "nt":
        drive_type = ctypes.windll.kernel32.GetDriveTypeW
        drive_type.argtypes = [ctypes.c_wchar_p]
        drive_type.restype = ctypes.c_uint
        if drive_type(path.anchor) not in (2, 3, 6):
            return None
        try:
            path = path.resolve()
        except (OSError, RuntimeError):
            return None
        if drive_type(path.anchor) not in (2, 3, 6):
            return None
    return path


def _read_text(path, max_bytes):
    local = _local_path(str(path))
    if local is None:
        return ""
    try:
        with local.open("rb") as stream:
            data = stream.read(max_bytes + 1)
        return data.decode("utf-8-sig") if len(data) <= max_bytes else ""
    except (OSError, UnicodeError):
        return ""


def _steam_libraries(executables, registry_root):
    roots = [Path(path).parent for path in executables
             if isinstance(path, str) and Path(path).name.lower() == "steam.exe"]
    roots.append(registry_root)
    for candidate in roots:
        root = _local_path(str(candidate))
        try:
            if root is None or not (root / "steam.exe").is_file():
                continue
        except OSError:
            continue
        config = _read_text(root / "steamapps" / "libraryfolders.vdf", 2 * 1024 * 1024)
        candidates = re.findall(r'^\s*"path"\s*"([^"\r\n]+)"\s*$', config, re.M)
        libraries = [_local_path(value.replace("\\\\", "\\")) for value in candidates[:32]]
        return tuple(dict.fromkeys([root, *(path for path in libraries if path is not None)]))
    return ()


def _installed(libraries, app_id):
    for library in libraries:
        steamapps = library / "steamapps"
        manifest = _read_text(steamapps / f"appmanifest_{app_id}.acf", 64 * 1024)
        if not re.match(r'^\s*"AppState"\s*\{', manifest, re.I):
            continue
        fields = re.findall(r'^\s*"(appid|StateFlags|installdir)"\s*"([^"\r\n]+)"\s*$', manifest, re.M | re.I)
        values = {key.casefold(): value for key, value in fields}
        if len(fields) != 3 or len(values) != 3 or values["appid"] != str(app_id):
            continue
        flags, folder = values["stateflags"], values["installdir"]
        if not re.fullmatch(r"[0-9]{1,10}", flags) or not int(flags) & 4:
            continue
        if folder in (".", "..") or any(char in folder for char in '/\\:\x00'):
            continue
        install_dir = _local_path(str(steamapps / "common" / folder))
        try:
            if install_dir is not None and install_dir.is_dir():
                return True
        except OSError:
            pass
    return False


def _windows_build():
    return sys.getwindowsversion().build if os.name == "nt" else None


class ShaderPreflight:
    def __init__(self, run_cmd, powershell_path, steam_path):
        self._run_cmd = run_cmd
        self._powershell_path = powershell_path
        self._steam_path = steam_path
        self._lock = threading.Lock()
        self._snapshot = {}
        self._snapshot_at = float("-inf")

    def _inventory(self):
        with self._lock:
            if time.monotonic() - self._snapshot_at < 30:
                return self._snapshot
            code, output, _ = self._run_cmd([self._powershell_path(), "-NoProfile", "-NonInteractive",
                                             "-Command", _INVENTORY_SCRIPT], timeout=4)
            try:
                raw = json.loads(output.lstrip("\ufeff")) if code == 0 else None
            except (ValueError, TypeError):
                raw = None
            self._snapshot = raw if isinstance(raw, dict) else {}
            self._snapshot_at = time.monotonic()
            return self._snapshot

    def get(self, app_id=0):
        app_id = app_id if type(app_id) is int and 0 < app_id <= 0xffffffff else 0
        build = _windows_build()
        inventory = self._inventory() if build is not None else {}
        rows = inventory.get("adapters")
        adapters = [_adapter(row, build) for row in rows[:16]] if isinstance(rows, list) else []
        game = {"state": "unverified" if app_id else "no_game", "app_id": app_id, "provider": None,
                "title": None, "graphics_api": None, "cache": "unverified", "source": None}
        if app_id in _INTEL_STEAM_TITLES:
            executables = inventory.get("steam")
            libraries = _steam_libraries(executables[:4] if isinstance(executables, list) else (), self._steam_path())
            game.update(provider="intel_precompiled", title=_INTEL_STEAM_TITLES[app_id], source=_INTEL_PRECOMPILED)
            if libraries:
                game["state"] = "provider_listed" if _installed(libraries, app_id) else "not_installed"
        return {"windows_build": build, "adapters": [row for row in adapters if row is not None], "game": game,
                "sources": {"microsoft_asd": _MICROSOFT_ASD, "intel_precompiled": _INTEL_PRECOMPILED},
                "benchmark": {"state": "manual_reference", "source": _SCSKILLER}}

    def cache_paths(self, app_id):
        if type(app_id) is not int or not 0 < app_id <= 0xffffffff:
            return ()
        executables = self._inventory().get("steam")
        libraries = _steam_libraries(executables[:4] if isinstance(executables, list) else (), self._steam_path())
        return tuple(library / "steamapps" / "shadercache" / str(app_id)
                     for library in libraries if _installed((library,), app_id))
