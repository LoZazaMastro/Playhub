import asyncio
import copy
import ctypes
import datetime
import json
import os
import platform
import shutil
import subprocess
import tempfile
import threading
import time
import traceback
import urllib.request
from ctypes import wintypes
from concurrent.futures import ThreadPoolExecutor, as_completed
from contextlib import contextmanager


_DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4


def _configure_process_dpi_awareness():
    state = {
        "requested": "PER_MONITOR_AWARE_V2",
        "applied": False,
        "fallback": "",
        "error": 0,
    }
    if os.name != "nt":
        return state
    try:
        user32 = ctypes.WinDLL("user32", use_last_error=True)
        setter = user32.SetProcessDpiAwarenessContext
        setter.argtypes = [ctypes.c_void_p]
        setter.restype = wintypes.BOOL
        ctypes.set_last_error(0)
        state["applied"] = bool(
            setter(ctypes.c_void_p(_DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
        )
        state["error"] = int(ctypes.get_last_error() or 0)
        if state["applied"]:
            return state
    except Exception as error:
        state["exception"] = f"{type(error).__name__}: {error}"

    # Windows 8.1 fallback. Per-monitor v1 still prevents logical desktop
    # dimensions from being passed to the Lossless Scaling engine.
    try:
        shcore = ctypes.WinDLL("shcore", use_last_error=True)
        fallback = shcore.SetProcessDpiAwareness
        fallback.argtypes = [ctypes.c_int]
        fallback.restype = ctypes.c_long
        result = int(fallback(2))
        state["fallback"] = "PER_MONITOR_AWARE"
        state["fallback_result"] = result
        state["applied"] = result in (0, -2147024891)
    except Exception as error:
        state["fallback_exception"] = f"{type(error).__name__}: {error}"
    return state


# Each Decky plugin has its own backend worker. Set DPI awareness before Decky
# initializes the worker so Lossless.dll always receives physical monitor pixels.
_PROCESS_DPI_STATE = _configure_process_dpi_awareness()

import decky


PORT = 47993
PLUGIN_VERSION = "2.3.0"
HEALTH_URL = f"http://127.0.0.1:{PORT}/health"
AUDIO_CACHE_SECONDS = 600
_AUDIO_CACHE_LOCK = threading.Lock()
_AUDIO_OPERATION_LOCK = threading.Lock()
_AUDIO_CACHE_VALUE = None
_AUDIO_CACHE_EXPIRES_AT = 0.0
_LOSSLESS_PATH_CACHE = {"value": "", "checked_at": 0.0}
_LS_CONTROL_LOCK = threading.RLock()
_LS_CORE_EVENT = threading.Event()
_LS_CORE = {
    "dll": None,
    "dll_path": "",
    "dll_directory": None,
    "callback": None,
    "initialized": False,
    "last_status": {},
    "target_monitor": {},
}
_ADLX_LOCK = threading.RLock()
_AMD_REQUEST_LOCK = threading.Lock()
_AMD_REQUEST_GENERATIONS = {}
_AMD_PROFILE_LOCK = threading.RLock()
_AMD_LAST_TRANSACTIONS = []

# --- Enumerazione finestre senza perdita di memoria -------------------------
# ctypes alloca un trampolino nativo per OGNI istanza di callback creata, e quei
# trampolini non vengono mai liberati finche' il processo vive. Creando la
# callback dentro funzioni richiamate dai cicli di polling (ogni 0,12 s) la
# memoria del plugin cresceva senza limite fino a decine di gigabyte.
# Qui la callback viene creata UNA SOLA VOLTA e riusata: le funzioni chiamanti
# ricevono la lista degli handle e filtrano in Python.
if os.name == "nt":
    _ENUM_WINDOWS_PROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    _ENUM_WINDOWS_BUFFER = []
    _ENUM_WINDOWS_LOCK = threading.Lock()

    def _collect_window_handle(hwnd, _lparam):
        _ENUM_WINDOWS_BUFFER.append(int(hwnd))
        return True

    _ENUM_WINDOWS_CALLBACK = _ENUM_WINDOWS_PROC(_collect_window_handle)
else:
    _ENUM_WINDOWS_CALLBACK = None
    _ENUM_WINDOWS_LOCK = threading.Lock()
    _ENUM_WINDOWS_BUFFER = []


def _enum_top_level_windows():
    """Handle di tutte le finestre di primo livello, senza creare callback."""
    if os.name != "nt":
        return []
    with _ENUM_WINDOWS_LOCK:
        del _ENUM_WINDOWS_BUFFER[:]
        try:
            ctypes.windll.user32.EnumWindows(_ENUM_WINDOWS_CALLBACK, 0)
        except Exception:
            return []
        return list(_ENUM_WINDOWS_BUFFER)

_LS_RUNTIME = {
    "active": False,
    "active_app_id": 0,
    "active_title": "",
    "managed_pid": 0,
    "last_action": "",
    "last_error": "",
    "last_transition_at": "",
}
_STEAM_OVERLAY_STATE = {
    "active": False,
    "overlay_pid": 0,
    "app_id": 0,
    "user_initiated": False,
    "updated_at": "",
    "output_windows": [],
}
_LS_SESSION_CONTROL = {
    "manual_disabled_app_id": 0,
    "manual_disabled_title": "",
    "overlay_suspended": False,
    "overlay_suspend_in_progress": False,
    "resume_requested": False,
    "resume_app_id": 0,
    "resume_title": "",
    "resume_settings": {},
    "resume_filters": [],
    "last_suspend_result": {},
    "last_resume_result": {},
    "last_changed_at": "",
}
_LOSSLESS_AUTOSTART_FOREGROUND_TIMEOUT_SECONDS = 45.0
_LOSSLESS_AUTOSTART_STABLE_SECONDS = 0.65
_LS_AUTOSTART_GATE = {
    "token": "",
    "app_id": 0,
    "title": "",
    "worker_running": False,
    "started_at": "",
    "last_result": {},
}
_AMD_PROFILE_RUNTIME = {
    "active": False,
    "recovery_required": False,
    "active_app_id": 0,
    "active_title": "",
    "baseline": {},
    "profile": {},
    "attempted_app_id": 0,
    "attempted_profile": {},
    "last_attempt_ok": None,
    "last_action": "",
    "last_error": "",
    "last_result": {},
    "last_transition_at": "",
}


class _MONITORINFOEXW(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("rcMonitor", wintypes.RECT),
        ("rcWork", wintypes.RECT),
        ("dwFlags", wintypes.DWORD),
        ("szDevice", wintypes.WCHAR * 32),
    ]


@contextmanager
def _lossless_dpi_scope():
    previous = None
    setter = None
    if os.name == "nt":
        try:
            user32 = ctypes.WinDLL("user32", use_last_error=True)
            setter = user32.SetThreadDpiAwarenessContext
            setter.argtypes = [ctypes.c_void_p]
            setter.restype = ctypes.c_void_p
            previous = setter(
                ctypes.c_void_p(_DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)
            )
        except Exception:
            previous = None
            setter = None
    try:
        yield
    finally:
        if setter is not None and previous:
            try:
                setter(ctypes.c_void_p(previous))
            except Exception:
                pass


def _dpi_awareness_snapshot():
    snapshot = copy.deepcopy(_PROCESS_DPI_STATE)
    if os.name != "nt":
        return snapshot
    try:
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        shcore = ctypes.WinDLL("shcore", use_last_error=True)
        user32 = ctypes.WinDLL("user32", use_last_error=True)
        shcore.GetProcessDpiAwareness.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(ctypes.c_int),
        ]
        shcore.GetProcessDpiAwareness.restype = ctypes.c_long
        awareness = ctypes.c_int(-1)
        result = int(
            shcore.GetProcessDpiAwareness(
                kernel32.GetCurrentProcess(), ctypes.byref(awareness)
            )
        )
        get_thread_context = user32.GetThreadDpiAwarenessContext
        get_thread_context.argtypes = []
        get_thread_context.restype = ctypes.c_void_p
        get_awareness = user32.GetAwarenessFromDpiAwarenessContext
        get_awareness.argtypes = [ctypes.c_void_p]
        get_awareness.restype = ctypes.c_int
        thread_context = get_thread_context()
        snapshot.update({
            "process_awareness": int(awareness.value),
            "process_query_result": result,
            "thread_awareness": int(get_awareness(thread_context)),
            "system_metrics": {
                "width": int(user32.GetSystemMetrics(0)),
                "height": int(user32.GetSystemMetrics(1)),
            },
        })
    except Exception as error:
        snapshot["query_exception"] = f"{type(error).__name__}: {error}"
    return snapshot


def _lossless_monitor_snapshot(hwnd=0):
    if os.name != "nt":
        return {}
    try:
        with _lossless_dpi_scope():
            user32 = ctypes.WinDLL("user32", use_last_error=True)
            user32.MonitorFromWindow.argtypes = [wintypes.HWND, wintypes.DWORD]
            user32.MonitorFromWindow.restype = wintypes.HMONITOR
            user32.GetMonitorInfoW.argtypes = [
                wintypes.HMONITOR,
                ctypes.POINTER(_MONITORINFOEXW),
            ]
            user32.GetMonitorInfoW.restype = wintypes.BOOL
            monitor = user32.MonitorFromWindow(wintypes.HWND(int(hwnd or 0)), 2)
            info = _MONITORINFOEXW()
            info.cbSize = ctypes.sizeof(info)
            if not monitor or not user32.GetMonitorInfoW(monitor, ctypes.byref(info)):
                return {"ok": False, "error": int(ctypes.get_last_error() or 0)}
            window_rect = wintypes.RECT()
            has_window_rect = bool(
                hwnd
                and user32.GetWindowRect(
                    wintypes.HWND(int(hwnd)), ctypes.byref(window_rect)
                )
            )
            dpi = 96
            try:
                get_dpi = user32.GetDpiForWindow
                get_dpi.argtypes = [wintypes.HWND]
                get_dpi.restype = wintypes.UINT
                dpi = int(get_dpi(wintypes.HWND(int(hwnd)))) if hwnd else 96
            except Exception:
                pass
            monitor_width = int(info.rcMonitor.right - info.rcMonitor.left)
            monitor_height = int(info.rcMonitor.bottom - info.rcMonitor.top)
            return {
                "ok": True,
                "device": str(info.szDevice),
                "dpi": dpi or 96,
                "scale_percent": round((dpi or 96) * 100 / 96),
                "monitor": {
                    "left": int(info.rcMonitor.left),
                    "top": int(info.rcMonitor.top),
                    "right": int(info.rcMonitor.right),
                    "bottom": int(info.rcMonitor.bottom),
                    "width": monitor_width,
                    "height": monitor_height,
                },
                "work_area": {
                    "left": int(info.rcWork.left),
                    "top": int(info.rcWork.top),
                    "right": int(info.rcWork.right),
                    "bottom": int(info.rcWork.bottom),
                },
                "window": (
                    {
                        "left": int(window_rect.left),
                        "top": int(window_rect.top),
                        "right": int(window_rect.right),
                        "bottom": int(window_rect.bottom),
                        "width": int(window_rect.right - window_rect.left),
                        "height": int(window_rect.bottom - window_rect.top),
                    }
                    if has_window_rect
                    else {}
                ),
                "virtual_screen": {
                    "left": int(user32.GetSystemMetrics(76)),
                    "top": int(user32.GetSystemMetrics(77)),
                    "width": int(user32.GetSystemMetrics(78)),
                    "height": int(user32.GetSystemMetrics(79)),
                },
            }
    except Exception as error:
        return {"ok": False, "exception": f"{type(error).__name__}: {error}"}


def _lossless_output_matches_monitor(status, monitor_snapshot):
    if int(status.get("status", -1) or -1) != 2:
        return False
    monitor = (monitor_snapshot or {}).get("monitor", {})
    expected_width = int(monitor.get("width", 0) or 0)
    expected_height = int(monitor.get("height", 0) or 0)
    actual_width = int(status.get("output_width", 0) or 0)
    actual_height = int(status.get("output_height", 0) or 0)
    if min(expected_width, expected_height, actual_width, actual_height) <= 0:
        return False
    tolerance = max(4, round(max(expected_width, expected_height) * 0.002))
    return (
        abs(actual_width - expected_width) <= tolerance
        and abs(actual_height - expected_height) <= tolerance
    )

LS_PROFILE_DEFAULTS = {
    "activation_delay_ms": 900,
    "scaling_mode": "Auto",
    "scaling_fit_mode": "AspectRatio",
    "scale_factor": 1.5,
    "resize_before_scaling": False,
    "windowed_mode": False,
    "scaling_type": "Off",
    "fsr_type": "ORIGINAL",
    "ls1_type": "BALANCED",
    "anime4k_type": "S",
    "sharpness": 5,
    "ls1_sharpness": 1,
    "vrs": False,
    "frame_generation": "LSFG3",
    "lsfg2_mode": "X2",
    "lsfg3_mode": "FIXED",
    "lsfg3_multiplier": 2,
    "lsfg3_target": 120,
    "lsfg_flow_scale": 100,
    "lsfg_size": "BALANCED",
    "clip_cursor": False,
    "adjust_cursor_speed": False,
    "hide_cursor": False,
    "scale_cursor": False,
    "sync_mode": "DEFAULT",
    "max_frame_latency": 3,
    "gsync_support": False,
    "hdr_support": True,
    "draw_fps": True,
    "capture_api": "DXGI",
    "queue_target": 1,
    "preferred_gpu_id": 0,
    "output_display_id": 0,
    "multi_display_mode": False,
    "crop_input": False,
    "crop_input_left": 0,
    "crop_input_top": 0,
    "crop_input_right": 0,
    "crop_input_bottom": 0,
}

LS_SETTING_ELEMENTS = {
    "scaling_mode": "ScalingMode",
    "scaling_fit_mode": "ScalingFitMode",
    "scale_factor": "ScaleFactor",
    "resize_before_scaling": "ResizeBeforeScaling",
    "windowed_mode": "WindowedMode",
    "scaling_type": "ScalingType",
    "fsr_type": "FSRType",
    "ls1_type": "LS1Type",
    "anime4k_type": "Anime4kType",
    "sharpness": "Sharpness",
    "ls1_sharpness": "LS1Sharpness",
    "vrs": "VRS",
    "frame_generation": "FrameGeneration",
    "lsfg2_mode": "LSFG2Mode",
    "lsfg3_mode": "LSFG3Mode1",
    "lsfg3_multiplier": "LSFG3Multiplier",
    "lsfg3_target": "LSFG3Target",
    "lsfg_flow_scale": "LSFGFlowScale",
    "lsfg_size": "LSFGSize",
    "clip_cursor": "ClipCursor",
    "adjust_cursor_speed": "AdjustCursorSpeed",
    "hide_cursor": "HideCursor",
    "scale_cursor": "ScaleCursor",
    "sync_mode": "SyncMode",
    "max_frame_latency": "MaxFrameLatency",
    "gsync_support": "GsyncSupport",
    "hdr_support": "HdrSupport",
    "draw_fps": "DrawFps",
    "capture_api": "CaptureApi",
    "queue_target": "QueueTarget",
    "preferred_gpu_id": "PreferredGpuId",
    "output_display_id": "OutputDisplayId",
    "multi_display_mode": "MultiDisplayMode",
    "crop_input": "CropInput",
    "crop_input_left": "CropInputLeft",
    "crop_input_top": "CropInputTop",
    "crop_input_right": "CropInputRight",
    "crop_input_bottom": "CropInputBottom",
}

AMD_PROFILE_DEFAULTS = {
    "rsr": False,
    "rsr_sharpness": 75,
    "afmf": False,
    "antilag": False,
    "chill": False,
    "chill_min": 60,
    "chill_max": 120,
    "sharpening": False,
    "sharpening_value": 80,
    "boost": False,
    "boost_resolution": 83,
    "enhanced_sync": False,
}



class Plugin:
    def __init__(self):
        self._agent_process = None
        self._agent_user_stopped = False
        self._agent_lock = threading.RLock()
        self._settings_lock = threading.RLock()
        settings_root = str(getattr(decky, "DECKY_PLUGIN_SETTINGS_DIR", "") or "").strip()
        if not settings_root:
            settings_root = os.path.join(
                os.environ.get("USERPROFILE", os.path.expanduser("~")),
                "homebrew", "settings", "quick-settings",
            )
        self._settings_dir = settings_root
        self._settings_path = os.path.join(settings_root, "quick-settings-2.3.json")
        self._event_log_path = os.path.join(settings_root, "quick-settings-events.log")
        self._settings = self._load_plugin_settings()
        self._last_runtime = {
            "app_id": 0,
            "title": "",
            "ui_mode": -1,
            "source": "",
            "reported_at": "",
        }

    async def _main(self):
        self._record_event(
            "plugin_start",
            version=PLUGIN_VERSION,
            pid=os.getpid(),
            dpi_awareness=_dpi_awareness_snapshot(),
        )
        loop = asyncio.get_event_loop()
        # Start endpoint discovery immediately, in parallel with agent startup.
        # By the time the QAM can be opened, the expensive audio result is cached.
        loop.run_in_executor(None, _get_audio_devices_sync)

        # The agent must NOT be awaited here. Decky loads plugins while Steam is
        # still starting, so blocking _main until the agent answers delays the
        # whole plugin chain and makes Steam visibly slower to come up. The agent
        # is started in the background instead; every endpoint already calls
        # _ensure_agent_sync before using it, so nothing depends on this await.
        async def _start_agent_in_background():
            try:
                running = await loop.run_in_executor(None, self._ensure_agent_sync)
                if not running:
                    decky.logger.warning(
                        "Quick Settings agent was not ready during plugin startup"
                    )
            except Exception as error:
                decky.logger.warning("Quick Settings agent startup failed: %s", error)

        asyncio.create_task(_start_agent_in_background())

    async def _unload(self):
        _cancel_lossless_autostart("plugin_unload")
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(
            None, _stop_lossless_managed_sync, "plugin_unload", True
        )
        await loop.run_in_executor(None, self._restore_amd_profile_sync, "plugin_unload")
        await loop.run_in_executor(None, self._stop_agent_fully)
        self._record_event("plugin_unload")

    async def _uninstall(self):
        _cancel_lossless_autostart("plugin_uninstall")
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(
            None, _stop_lossless_managed_sync, "plugin_uninstall", True
        )
        await loop.run_in_executor(None, self._restore_amd_profile_sync, "plugin_uninstall")
        await loop.run_in_executor(None, self._stop_agent_fully)

    async def _migration(self):
        pass

    async def ensure_agent(self):
        # Auto path (loaders / Big Picture): respect a manual stop.
        if self._agent_user_stopped:
            return False
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self._ensure_agent_sync)

    def _ensure_agent_sync(self):
        with self._agent_lock:
            return self._ensure_agent_locked()

    def _ensure_agent_locked(self):
        if self._is_agent_ready():
            return True

        agent_path = self._agent_path()
        if not os.path.exists(agent_path):
            decky.logger.error(f"Quick Settings agent not found: {agent_path}")
            return False

        creation_flags = 0
        if os.name == "nt":
            creation_flags = getattr(subprocess, "CREATE_NO_WINDOW", 0x08000000)

        for attempt in range(3):
            try:
                self._agent_process = subprocess.Popen(
                    [agent_path],
                    cwd=os.path.dirname(agent_path),
                    stdin=subprocess.DEVNULL,
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    creationflags=creation_flags,
                    close_fds=True,
                )
            except Exception as error:
                decky.logger.error(f"Cannot start Quick Settings agent: {error}")
                return False

            exited = False
            for _ in range(40):
                if self._is_agent_ready():
                    decky.logger.info("Quick Settings agent started")
                    return True
                if self._agent_process and self._agent_process.poll() is not None:
                    exited = True
                    break
                time.sleep(0.25)

            # Exited (e.g. the HTTP port is still held right after a kill) or
            # never answered: drop it, let the port free, and retry.
            self._agent_process = None
            if not exited:
                break
            time.sleep(1.2)

        decky.logger.error("Quick Settings agent did not start")
        return False

    def _stop_agent(self):
        if not self._agent_process:
            return

        if self._agent_process.poll() is None:
            try:
                self._agent_process.terminate()
                self._agent_process.wait(timeout=3)
            except Exception:
                try:
                    self._agent_process.kill()
                except Exception:
                    pass

        self._agent_process = None

    def _is_agent_ready(self, timeout=0.25):
        try:
            with urllib.request.urlopen(HEALTH_URL, timeout=timeout) as response:
                return response.status == 200
        except Exception:
            return False

    def _agent_path(self):
        return os.path.join(os.path.dirname(__file__), "bin", "QuickSettingsAgent.exe")

    async def get_hdr_status(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _get_hdr_status_sync)

    async def set_hdr_enabled(self, request):
        enabled = False
        if isinstance(request, dict):
            enabled = bool(request.get("enabled"))
        else:
            enabled = bool(request)

        loop = asyncio.get_event_loop()
        started = time.perf_counter()
        result = await loop.run_in_executor(None, _set_hdr_enabled_sync, enabled)
        self._record_event(
            "hdr_set",
            requested=enabled,
            ok=bool(result.get("ok")) if isinstance(result, dict) else False,
            verified=bool(result.get("verified")) if isinstance(result, dict) else False,
            write=result.get("write", {}) if isinstance(result, dict) else {},
            rollback=result.get("rollback", {}) if isinstance(result, dict) else {},
            hdr=result.get("hdr", {}) if isinstance(result, dict) else {},
            message=result.get("message", "") if isinstance(result, dict) else "Invalid HDR response",
            duration_ms=round((time.perf_counter() - started) * 1000),
        )
        return result

    async def get_audio_devices(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _get_audio_devices_sync)

    async def get_initial_state(self):
        """Return the first QAM snapshot without serial Decky round-trips."""
        loop = asyncio.get_event_loop()
        started = time.perf_counter()
        timings = {}

        async def timed(name, func):
            item_started = time.perf_counter()
            try:
                return await loop.run_in_executor(None, func)
            except Exception as error:
                decky.logger.exception(f"Quick Settings initial {name} failed")
                return {"ok": False, "message": str(error)}
            finally:
                timings[name] = round((time.perf_counter() - item_started) * 1000)

        capabilities = await timed("capabilities", _get_capabilities_sync)
        jobs = {
            "audio": timed("audio", _get_audio_devices_sync),
            "hdr": timed("hdr", _get_hdr_status_sync),
        }
        if capabilities.get("display"):
            jobs["display"] = timed("display", _get_display_status_sync)
        if capabilities.get("lossless"):
            jobs["lossless"] = timed("lossless", _get_lossless_status_sync)
        if capabilities.get("amd_radeon"):
            jobs["amd"] = timed("amd", _get_amd_status_sync)

        names = list(jobs)
        values = await asyncio.gather(*(jobs[name] for name in names))
        result = {"capabilities": capabilities}
        result.update(dict(zip(names, values)))
        if isinstance(result.get("lossless"), dict):
            current_game = copy.deepcopy(self._last_runtime)
            app_id = int(current_game.get("app_id", 0) or 0)
            result["lossless"]["current_game"] = current_game
            result["lossless"]["can_scale"] = bool(
                app_id and app_id != int(LOSSLESS_APPID)
            )
        result["timings_ms"] = timings
        result["total_ms"] = round((time.perf_counter() - started) * 1000)
        decky.logger.info(
            f"Quick Settings initial snapshot ready in {result['total_ms']} ms: {timings}"
        )
        return result

    async def set_audio_output(self, request):
        device_id = ""
        if isinstance(request, dict):
            device_id = str(request.get("id", request.get("device_id", "")) or "")
        else:
            device_id = str(request or "")
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _set_audio_device_sync, "output", device_id)

    async def set_audio_input(self, request):
        device_id = ""
        if isinstance(request, dict):
            device_id = str(request.get("id", request.get("device_id", "")) or "")
        else:
            device_id = str(request or "")
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _set_audio_device_sync, "input", device_id)

    async def set_microphone_volume(self, request):
        expected_endpoint = request.get("expected_endpoint") if isinstance(request, dict) else None
        if expected_endpoint is not None and (not isinstance(expected_endpoint, str) or not expected_endpoint.strip()):
            return {"ok": False, "message": "Invalid expected microphone endpoint."}
        if isinstance(request, dict):
            level = request.get("level", request.get("volume"))
        else:
            level = request
        try:
            if isinstance(level, bool):
                raise ValueError("Boolean volume is invalid")
            level = int(round(float(level)))
        except (TypeError, ValueError, OverflowError):
            return {"ok": False, "message": "Invalid microphone volume."}
        level = max(0, min(100, level))
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _set_microphone_volume_sync, level, expected_endpoint)

    # ----------------------------------------------------------------- #
    #  Capabilities (let the UI hide controls the machine cannot use)   #
    # ----------------------------------------------------------------- #

    async def get_capabilities(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _get_capabilities_sync)

    # ----------------------------------------------------------------- #
    #  Performance / CPU power-plan controls (any Windows PC)           #
    # ----------------------------------------------------------------- #

    async def get_performance_status(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _get_performance_status_sync)

    async def set_power_mode(self, request):
        return {"ok": False, "reason": "feature_removed"}

    async def get_lossless_status(self):
        loop = asyncio.get_event_loop()
        result = await loop.run_in_executor(None, _get_lossless_status_sync)
        current_game = copy.deepcopy(self._last_runtime)
        app_id = int(current_game.get("app_id", 0) or 0)
        result["current_game"] = current_game
        result["can_scale"] = bool(app_id and app_id != int(LOSSLESS_APPID))
        return result

    async def launch_lossless(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _launch_lossless_sync)

    async def set_lossless_scaling(self, request):
        enabled = bool(_arg(request, "enabled", False))
        app_id = int(self._last_runtime.get("app_id", 0) or 0)
        title = str(self._last_runtime.get("title", "") or "")
        loop = asyncio.get_event_loop()
        started = time.perf_counter()
        result = await loop.run_in_executor(
            None, _set_lossless_scaling_sync, enabled, app_id, title
        )
        runtime = result.get("runtime", {}) if isinstance(result, dict) else {}
        self._record_event(
            "lossless_manual_toggle",
            enabled=enabled,
            app_id=app_id,
            title=title,
            ok=bool(isinstance(result, dict) and result.get("ok")),
            active=bool(isinstance(result, dict) and result.get("active")),
            verified=bool(isinstance(result, dict) and result.get("verified")),
            target=runtime.get("activation_focus", {}).get("target", {}),
            core_status=(
                result.get("core_status", {})
                if isinstance(result, dict)
                else {}
            ),
            message=result.get("message", "") if isinstance(result, dict) else "",
            duration_ms=round((time.perf_counter() - started) * 1000),
        )
        return result

    async def set_steam_overlay_active(self, request):
        active = bool(_arg(request, "active", False))
        overlay_pid = int(_arg(request, "overlay_pid", 0) or 0)
        app_id = int(_arg(request, "app_id", 0) or 0)
        user_initiated = bool(_arg(request, "user_initiated", False))
        loop = asyncio.get_event_loop()
        result = await loop.run_in_executor(
            None,
            _set_steam_overlay_active_sync,
            active,
            overlay_pid,
            app_id,
            user_initiated,
        )
        self._record_event("steam_overlay_state", **result)
        return result

    async def set_lossless_setting(self, request):
        key = str(_arg(request, "key", ""))
        value = _arg(request, "value", "")
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _set_lossless_setting_sync, key, value)

    async def get_lossless_profile(self, request):
        app_id = str(_arg(request, "app_id", "0") or "0")
        title = str(_arg(request, "title", "") or "")
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self._get_lossless_profile_sync, app_id, title)

    async def save_lossless_profile(self, request):
        app_id = str(_arg(request, "app_id", "0") or "0")
        title = str(_arg(request, "title", "") or "")
        auto_enabled = bool(_arg(request, "auto_enabled", False))
        settings = _arg(request, "settings", {})
        amd_auto_enabled = bool(_arg(request, "amd_auto_enabled", False))
        amd_settings = _arg(request, "amd_settings", {})
        sdl3_native_controller_enabled = bool(
            _arg(request, "sdl3_native_controller_enabled", False)
        )
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(
            None,
            self._save_lossless_profile_sync,
            app_id,
            title,
            auto_enabled,
            settings,
            amd_auto_enabled,
            amd_settings,
            sdl3_native_controller_enabled,
        )

    async def game_runtime_changed(self, request):
        app_id = int(_arg(request, "app_id", 0) or 0)
        title = str(_arg(request, "title", "") or "")
        ui_mode = int(_arg(request, "ui_mode", -1) or -1)
        source = str(_arg(request, "source", "") or "")
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(
            None, self._game_runtime_changed_sync, app_id, title, ui_mode, source
        )

    async def generate_diagnostics(self):
        from quick_settings.support_report import generate
        return await asyncio.to_thread(generate)

    async def get_amd_status(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _get_amd_status_sync)

    async def set_amd(self, request):
        feature = str(_arg(request, "feature", ""))
        value = _arg(request, "value", "")
        with _AMD_REQUEST_LOCK:
            generation = int(_AMD_REQUEST_GENERATIONS.get(feature, 0) or 0) + 1
            _AMD_REQUEST_GENERATIONS[feature] = generation
        loop = asyncio.get_event_loop()
        started = time.perf_counter()
        result = await loop.run_in_executor(
            None, self._set_amd_global_sync, feature, value, generation
        )
        if isinstance(result, dict) and result.get("superseded"):
            return result
        self._record_event(
            "amd_global_setting",
            feature=feature,
            requested_value=value,
            generation=generation,
            ok=bool(isinstance(result, dict) and result.get("ok")),
            applied_value=_amd_feature_value(result, feature),
            driver_result=(
                result.get("helper", {}).get("adlx_result", "")
                if isinstance(result, dict) else ""
            ),
            message=result.get("message", "") if isinstance(result, dict) else "Invalid helper response",
            duration_ms=round((time.perf_counter() - started) * 1000),
        )
        return result

    # ----------------------------------------------------------------- #
    #  Display resolution / refresh-rate controls (any Windows PC)      #
    # ----------------------------------------------------------------- #

    async def get_display_status(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _get_display_status_sync)

    async def set_display_mode(self, request):
        width = int(_arg(request, "width", 0) or 0)
        height = int(_arg(request, "height", 0) or 0)
        hz = int(_arg(request, "hz", _arg(request, "refresh", 0)) or 0)
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _set_display_mode_sync, width, height, hz)

    async def set_refresh_rate(self, request):
        hz = int(_arg(request, "hz", _arg(request, "refresh", 0)) or 0)
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _set_display_mode_sync, 0, 0, hz)

    # ----------------------------------------------------------------- #
    #  CPU power capability and explicit PPT transactions               #
    # ----------------------------------------------------------------- #

    async def get_tdp_status(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _get_tdp_status_sync)

    async def set_tdp(self, request):
        watts = _arg(request, "watts", None)
        stapm = _arg(request, "stapm", watts)
        fast = _arg(request, "fast", watts)
        slow = _arg(request, "slow", watts)
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _set_tdp_sync, stapm, fast, slow)

    async def set_cpu_ppt(self, request):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _set_cpu_ppt_sync,
                                          _arg(request, "watts", None),
                                          _arg(request, "confirmed", False))

    async def restore_cpu_ppt(self, request):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, _restore_cpu_ppt_sync,
                                          _arg(request, "confirmed", False))

    # ----------------------------------------------------------------- #
    #  Agent lifecycle (manual buttons + Big Picture mode control)      #
    # ----------------------------------------------------------------- #

    async def get_agent_status(self):
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, lambda: {"running": self._is_agent_ready()})

    async def start_agent(self):
        # Explicit start: clear the manual-stop latch and (re)spawn.
        self._agent_user_stopped = False
        loop = asyncio.get_event_loop()
        running = await loop.run_in_executor(None, self._ensure_agent_sync)
        return {"ok": bool(running), "running": bool(running)}

    async def stop_agent(self):
        # Explicit stop: latch so auto-start won't immediately undo it.
        self._agent_user_stopped = True
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self._stop_agent_fully)

    async def stop_agent_auto(self):
        # Leaving Big Picture: stop without latching the manual flag.
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self._stop_agent_fully)

    def _stop_agent_fully(self):
        with self._agent_lock:
            self._stop_agent()
            self._kill_agent_processes()
            return {"ok": True, "running": self._is_agent_ready()}

    def _kill_agent_processes(self):
        if os.name != "nt":
            return
        try:
            subprocess.run(
                ["taskkill", "/F", "/IM", "QuickSettingsAgent.exe"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                timeout=5,
            )
        except Exception:
            pass

    def _default_plugin_settings(self):
        return {"schema": 1, "lossless_profiles": {}}

    def _load_plugin_settings(self):
        defaults = self._default_plugin_settings()
        try:
            if not os.path.exists(self._settings_path):
                return defaults
            with open(self._settings_path, "r", encoding="utf-8") as handle:
                loaded = json.load(handle)
            if not isinstance(loaded, dict):
                return defaults
            profiles = loaded.get("lossless_profiles")
            if not isinstance(profiles, dict):
                profiles = {}
            return {"schema": 1, "lossless_profiles": profiles}
        except Exception:
            decky.logger.exception("Quick Settings could not load its settings")
            return defaults

    def _save_plugin_settings(self):
        with self._settings_lock:
            os.makedirs(self._settings_dir, exist_ok=True)
            temporary = self._settings_path + ".tmp"
            backup = self._settings_path + ".bak"
            if os.path.exists(self._settings_path):
                try:
                    shutil.copyfile(self._settings_path, backup)
                except Exception:
                    pass
            with open(temporary, "w", encoding="utf-8") as handle:
                json.dump(self._settings, handle, ensure_ascii=False, indent=2)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, self._settings_path)

    def _record_event(self, event, **fields):
        payload = {
            "timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "event": str(event),
            **fields,
        }
        try:
            os.makedirs(self._settings_dir, exist_ok=True)
            if os.path.exists(self._event_log_path) and os.path.getsize(self._event_log_path) > 2 * 1024 * 1024:
                rotated = self._event_log_path + ".1"
                try:
                    os.replace(self._event_log_path, rotated)
                except Exception:
                    pass
            with open(self._event_log_path, "a", encoding="utf-8") as handle:
                handle.write(json.dumps(payload, ensure_ascii=False, default=str) + "\n")
        except Exception:
            decky.logger.exception("Quick Settings event log write failed")

    def _get_lossless_profile_sync(self, app_id, title, include_amd_status=True):
        key = str(app_id or "0")
        with self._settings_lock:
            stored = copy.deepcopy(self._settings.get("lossless_profiles", {}).get(key, {}))
        settings = stored.get("settings")
        if not isinstance(settings, dict):
            settings = _read_lossless_settings().get("settings", {})
        settings = _normalize_lossless_profile(settings)
        amd_status = _get_amd_status_sync() if include_amd_status else {}
        amd_settings = stored.get("amd_settings")
        if not isinstance(amd_settings, dict):
            amd_settings = _amd_profile_snapshot(amd_status)
        amd_settings = _normalize_amd_profile(amd_settings)
        return {
            "ok": True,
            "app_id": int(app_id or 0),
            "title": stored.get("title") or title,
            "auto_enabled": bool(stored.get("auto_enabled", False)),
            "settings": settings,
            "amd_auto_enabled": bool(stored.get("amd_auto_enabled", False)),
            "amd_settings": amd_settings,
            "sdl3_native_controller_enabled": bool(
                stored.get("sdl3_native_controller_enabled", False)
            ),
            "amd_status": amd_status,
        }

    def _save_lossless_profile_sync(
        self, app_id, title, auto_enabled, settings, amd_auto_enabled=False, amd_settings=None,
        sdl3_native_controller_enabled=False
    ):
        key = str(app_id or "0")
        normalized = _normalize_lossless_profile(settings)
        normalized_amd = _normalize_amd_profile(amd_settings)
        if normalized.get("frame_generation") != "Off":
            normalized_amd["afmf"] = False
        with self._settings_lock:
            profiles = self._settings.setdefault("lossless_profiles", {})
            previous = profiles.get(key, {}) if isinstance(profiles.get(key), dict) else {}
            profiles[key] = {
                "title": title,
                "auto_enabled": bool(auto_enabled),
                "settings": normalized,
                "amd_auto_enabled": bool(amd_auto_enabled),
                "amd_settings": normalized_amd,
                "sdl3_native_controller_enabled": bool(sdl3_native_controller_enabled),
                "lossless_filters": (
                    list(previous.get("lossless_filters", []))
                    if isinstance(previous.get("lossless_filters"), list)
                    else []
                ),
                "updated_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            }
            self._save_plugin_settings()
        self._record_event(
            "lossless_profile_saved",
            app_id=key,
            title=title,
            auto_enabled=bool(auto_enabled),
            amd_auto_enabled=bool(amd_auto_enabled),
        )
        runtime_results = {}
        current_game_matches = (
            int(self._last_runtime.get("app_id", 0) or 0) == int(app_id or 0)
        )
        if current_game_matches:
            if auto_enabled:
                _clear_lossless_manual_override(int(app_id or 0))
                filters = _lossless_profile_filters(
                    title, previous.get("lossless_filters")
                )
                if _STEAM_OVERLAY_STATE.get("active"):
                    _queue_lossless_overlay_resume(
                        normalized, int(app_id or 0), title, filters
                    )
                    runtime_results["lossless"] = {
                        "ok": True,
                        "active": True,
                        "suspended": True,
                        "pending": True,
                        "message": "",
                    }
                else:
                    runtime_results["lossless"] = _schedule_lossless_autostart(
                        normalized,
                        int(app_id or 0),
                        title,
                        filters,
                        self._record_event,
                    )
            else:
                _cancel_lossless_autostart("profile_disabled")
                runtime_results["lossless"] = _stop_lossless_managed_sync("profile_disabled")
        if int(self._last_runtime.get("app_id", 0) or 0) == int(app_id or 0):
            if amd_auto_enabled:
                runtime_results["amd"] = self._activate_amd_profile_sync(
                    normalized_amd, int(app_id or 0), title, force=True
                )
            elif (
                _AMD_PROFILE_RUNTIME.get("active")
                and int(_AMD_PROFILE_RUNTIME.get("active_app_id", 0) or 0) == int(app_id or 0)
            ):
                runtime_results["amd"] = self._restore_amd_profile_sync("profile_disabled")
        ok = all(bool(value.get("ok")) for value in runtime_results.values()) if runtime_results else True
        return {"ok": ok, "profile": profiles[key], "runtime": runtime_results}

    def _set_amd_global_sync(self, feature, value, generation=0):
        with _AMD_PROFILE_LOCK:
            with _AMD_REQUEST_LOCK:
                if (
                    generation
                    and int(_AMD_REQUEST_GENERATIONS.get(str(feature), 0) or 0)
                    != int(generation)
                ):
                    return {
                        "ok": True,
                        "superseded": True,
                        "feature": str(feature),
                        "requested_value": value,
                    }
            if _AMD_PROFILE_RUNTIME.get("active"):
                baseline = copy.deepcopy(_AMD_PROFILE_RUNTIME.get("baseline", {}))
                baseline[str(feature)] = _normalize_amd_value(str(feature), value)
                _AMD_PROFILE_RUNTIME["baseline"] = _normalize_amd_profile(baseline)
                profile_value = _AMD_PROFILE_RUNTIME.get("profile", {}).get(str(feature))
                if profile_value is not None:
                    result = _set_amd_sync(feature, profile_value, generation)
                    result["global_value_saved"] = baseline[str(feature)]
                    result["profile_override_active"] = True
                    return result
            return _set_amd_sync(feature, value, generation)

    def _restore_amd_profile_sync(self, reason):
        with _AMD_PROFILE_LOCK:
            if not (_AMD_PROFILE_RUNTIME.get("active") or _AMD_PROFILE_RUNTIME.get("recovery_required")):
                return {"ok": True, "active": False, "reason": reason}
            started = time.perf_counter()
            app_id = int(_AMD_PROFILE_RUNTIME.get("active_app_id", 0) or 0)
            title = str(_AMD_PROFILE_RUNTIME.get("active_title", "") or "")
            was_active = bool(_AMD_PROFILE_RUNTIME.get("active"))
            baseline = copy.deepcopy(_AMD_PROFILE_RUNTIME.get("baseline", {}))
            result = _apply_amd_profile_sync(baseline)
            ok = bool(result.get("ok"))
            _AMD_PROFILE_RUNTIME.update({
                "active": was_active and not ok,
                "recovery_required": not ok,
                "active_app_id": app_id if not ok else 0,
                "active_title": title if not ok else "",
                "baseline": baseline if not ok else {},
                "profile": copy.deepcopy(_AMD_PROFILE_RUNTIME.get("profile", {})) if not ok else {},
                "attempted_app_id": app_id if not ok else 0,
                "attempted_profile": (
                    copy.deepcopy(_AMD_PROFILE_RUNTIME.get("profile", {}))
                    if not ok
                    else {}
                ),
                "last_attempt_ok": None if ok else False,
                "last_action": "restore",
                "last_error": "" if ok else str(result.get("message", "")),
                "last_result": copy.deepcopy(result),
                "last_transition_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            })
            self._record_event(
                "amd_game_profile_restore",
                app_id=app_id,
                title=title,
                reason=reason,
                baseline=baseline,
                result=result,
                duration_ms=round((time.perf_counter() - started) * 1000),
            )
            return {"ok": ok, "active": was_active and not ok, "recovery_required": not ok,
                    "reason": reason, "result": result}

    def _activate_amd_profile_sync(self, settings, app_id, title, force=False):
        with _AMD_PROFILE_LOCK:
            if _AMD_PROFILE_RUNTIME.get("recovery_required"):
                return {"ok": False, "active": False, "recovery_required": True,
                        "message": "Previous AMD profile requires restoration before another apply."}
            normalized = _normalize_amd_profile(settings)
            if (
                not force
                and int(_AMD_PROFILE_RUNTIME.get("attempted_app_id", 0) or 0) == int(app_id)
                and _AMD_PROFILE_RUNTIME.get("attempted_profile") == normalized
                and _AMD_PROFILE_RUNTIME.get("last_attempt_ok") is False
            ):
                return {
                    "ok": False,
                    "active": False,
                    "unchanged": True,
                    "retry_suppressed": True,
                    "message": str(
                        _AMD_PROFILE_RUNTIME.get("last_error", "")
                        or "AMD driver did not confirm the requested profile."
                    ),
                }
            if (
                _AMD_PROFILE_RUNTIME.get("active")
                and int(_AMD_PROFILE_RUNTIME.get("active_app_id", 0) or 0) == int(app_id)
                and _AMD_PROFILE_RUNTIME.get("profile") == normalized
                and not force
            ):
                return {"ok": True, "active": True, "unchanged": True}
            if _AMD_PROFILE_RUNTIME.get("active"):
                restored = self._restore_amd_profile_sync("game_changed")
                if not restored.get("ok"):
                    return restored
            started = time.perf_counter()
            status = _get_amd_status_sync()
            baseline = _amd_profile_snapshot(status)
            if not status.get("ok") or not baseline:
                result = {"ok": False, "message": status.get("message", "AMD state unavailable.")}
            else:
                result = _apply_amd_profile_sync(normalized, status)
            ok = bool(result.get("ok"))
            if not ok and baseline:
                result["rollback"] = _apply_amd_profile_sync(baseline)
            recovery_required = bool(not ok and baseline and not result.get("rollback", {}).get("ok"))
            _AMD_PROFILE_RUNTIME.update({
                "active": ok,
                "recovery_required": recovery_required,
                "active_app_id": int(app_id) if ok else 0,
                "active_title": title if ok else "",
                "baseline": baseline if ok or recovery_required else {},
                "profile": normalized if ok else {},
                "attempted_app_id": int(app_id),
                "attempted_profile": normalized,
                "last_attempt_ok": ok,
                "last_action": "apply",
                "last_error": "" if ok else str(result.get("message", "")),
                "last_result": copy.deepcopy(result),
                "last_transition_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            })
            self._record_event(
                "amd_game_profile_apply",
                app_id=app_id,
                title=title,
                baseline=baseline,
                requested=normalized,
                result=result,
                duration_ms=round((time.perf_counter() - started) * 1000),
            )
            return {"ok": ok, "active": ok, "recovery_required": recovery_required, "result": result}

    def _game_runtime_changed_sync(self, app_id, title, ui_mode, source):
        now = datetime.datetime.now(datetime.timezone.utc).isoformat()
        previous_app_id = int(self._last_runtime.get("app_id", 0) or 0)
        changed = (
            previous_app_id != int(app_id or 0)
            or str(self._last_runtime.get("title", "")) != title
        )
        game_changed = previous_app_id != int(app_id or 0)
        self._last_runtime = {
            "app_id": int(app_id or 0),
            "title": title,
            "ui_mode": ui_mode,
            "source": source,
            "reported_at": now,
        }
        if changed:
            self._record_event("running_game_changed", **self._last_runtime)
        if game_changed:
            _cancel_lossless_autostart("game_changed")
            _reset_lossless_session_control(clear_manual=True, clear_overlay=True)
            with _AMD_PROFILE_LOCK:
                _AMD_PROFILE_RUNTIME.update({
                    "attempted_app_id": 0,
                    "attempted_profile": {},
                    "last_attempt_ok": None,
                })

        results = {}
        if not app_id or int(app_id) == int(LOSSLESS_APPID):
            if (
                _LS_RUNTIME.get("active")
                or int(_LS_RUNTIME.get("active_app_id", 0) or 0)
                or _LS_AUTOSTART_GATE.get("worker_running")
            ):
                results["lossless"] = _stop_lossless_managed_sync("game_stopped")
            if _AMD_PROFILE_RUNTIME.get("active") or _AMD_PROFILE_RUNTIME.get("recovery_required"):
                results["amd"] = self._restore_amd_profile_sync("game_stopped")
            return {"ok": all(value.get("ok") for value in results.values()) if results else True,
                    "active": False, "components": results}

        profile = self._get_lossless_profile_sync(str(app_id), title, False)
        if not profile.get("auto_enabled"):
            if _LS_RUNTIME.get("active") and int(_LS_RUNTIME.get("active_app_id", 0)) != int(app_id):
                results["lossless"] = _stop_lossless_managed_sync("unmanaged_game_started")
        else:
            filters = _lossless_profile_filters(
                title,
                self._settings.get("lossless_profiles", {})
                .get(str(app_id), {})
                .get("lossless_filters"),
            )
            if int(_LS_SESSION_CONTROL.get("manual_disabled_app_id", 0) or 0) == int(app_id):
                results["lossless"] = {
                    "ok": True,
                    "active": False,
                    "manual_disabled": True,
                    "message": "",
                }
            elif _STEAM_OVERLAY_STATE.get("active"):
                pending_same_app = (
                    bool(_LS_SESSION_CONTROL.get("resume_requested"))
                    and int(_LS_SESSION_CONTROL.get("resume_app_id", 0) or 0)
                    == int(app_id)
                )
                if not pending_same_app:
                    _queue_lossless_overlay_resume(
                        profile.get("settings", {}), int(app_id), title, filters
                    )
                results["lossless"] = {
                    "ok": True,
                    "active": True,
                    "suspended": True,
                    "pending": True,
                    "message": "",
                    "session_control": copy.deepcopy(_LS_SESSION_CONTROL),
                }
            else:
                results["lossless"] = _schedule_lossless_autostart(
                    profile.get("settings", {}),
                    int(app_id),
                    title,
                    filters,
                    self._record_event,
                )
            if changed:
                self._record_event(
                    "lossless_auto_reconcile",
                    app_id=app_id,
                    title=title,
                    result=_compact_lossless_result(results["lossless"]),
                )

        if profile.get("amd_auto_enabled"):
            results["amd"] = self._activate_amd_profile_sync(
                profile.get("amd_settings", {}), int(app_id), title, force=False
            )
        elif _AMD_PROFILE_RUNTIME.get("active"):
            results["amd"] = self._restore_amd_profile_sync("game_without_amd_profile")
        return {
            "ok": all(bool(value.get("ok")) for value in results.values()) if results else True,
            "active": bool(_LS_RUNTIME.get("active") or _AMD_PROFILE_RUNTIME.get("active")),
            "auto_enabled": bool(profile.get("auto_enabled")),
            "amd_auto_enabled": bool(profile.get("amd_auto_enabled")),
            "components": results,
        }

# HDR and Windows audio device helpers.

def _hdr_status_payload(enabled=False, available=False, supported=False, targets=None, message="", real_state=False):
    return {
        "available": bool(available),
        "supported": bool(supported),
        "enabled": bool(enabled),
        "shortcut_only": not bool(real_state),
        "real_state": bool(real_state),
        "targets": targets or [],
        "message": message,
    }


# Minimal DisplayConfig / Advanced Color definitions.
# The HDR toggle must mirror Windows' actual HDR state instead of a plugin-side saved value.
UINT32 = ctypes.c_uint32
UINT64 = ctypes.c_uint64
INT32 = ctypes.c_int32


class LUID(ctypes.Structure):
    _fields_ = [("LowPart", UINT32), ("HighPart", INT32)]


class DISPLAYCONFIG_RATIONAL(ctypes.Structure):
    _fields_ = [("Numerator", UINT32), ("Denominator", UINT32)]


class DISPLAYCONFIG_2DREGION(ctypes.Structure):
    _fields_ = [("cx", UINT32), ("cy", UINT32)]


class POINTL(ctypes.Structure):
    _fields_ = [("x", INT32), ("y", INT32)]


class DISPLAYCONFIG_VIDEO_SIGNAL_INFO(ctypes.Structure):
    _fields_ = [
        ("pixelRate", UINT64),
        ("hSyncFreq", DISPLAYCONFIG_RATIONAL),
        ("vSyncFreq", DISPLAYCONFIG_RATIONAL),
        ("activeSize", DISPLAYCONFIG_2DREGION),
        ("totalSize", DISPLAYCONFIG_2DREGION),
        ("videoStandard", UINT32),
        ("scanLineOrdering", UINT32),
    ]


class DISPLAYCONFIG_TARGET_MODE(ctypes.Structure):
    _fields_ = [("targetVideoSignalInfo", DISPLAYCONFIG_VIDEO_SIGNAL_INFO)]


class DISPLAYCONFIG_SOURCE_MODE(ctypes.Structure):
    _fields_ = [("width", UINT32), ("height", UINT32), ("pixelFormat", UINT32), ("position", POINTL)]


class DISPLAYCONFIG_DESKTOP_IMAGE_INFO(ctypes.Structure):
    _fields_ = [
        ("PathSourceSize", DISPLAYCONFIG_2DREGION),
        ("DesktopImageRegion", wintypes.RECT),
        ("DesktopImageClip", wintypes.RECT),
    ]


class DISPLAYCONFIG_MODE_INFO_UNION(ctypes.Union):
    _fields_ = [
        ("targetMode", DISPLAYCONFIG_TARGET_MODE),
        ("sourceMode", DISPLAYCONFIG_SOURCE_MODE),
        ("desktopImageInfo", DISPLAYCONFIG_DESKTOP_IMAGE_INFO),
    ]


class DISPLAYCONFIG_MODE_INFO(ctypes.Structure):
    _fields_ = [("infoType", UINT32), ("id", UINT32), ("adapterId", LUID), ("modeInfo", DISPLAYCONFIG_MODE_INFO_UNION)]


class DISPLAYCONFIG_PATH_SOURCE_INFO(ctypes.Structure):
    _fields_ = [("adapterId", LUID), ("id", UINT32), ("modeInfoIdx", UINT32), ("statusFlags", UINT32)]


class DISPLAYCONFIG_PATH_TARGET_INFO(ctypes.Structure):
    _fields_ = [
        ("adapterId", LUID),
        ("id", UINT32),
        ("modeInfoIdx", UINT32),
        ("outputTechnology", UINT32),
        ("rotation", UINT32),
        ("scaling", UINT32),
        ("refreshRate", DISPLAYCONFIG_RATIONAL),
        ("scanLineOrdering", UINT32),
        ("targetAvailable", wintypes.BOOL),
        ("statusFlags", UINT32),
    ]


class DISPLAYCONFIG_PATH_INFO(ctypes.Structure):
    _fields_ = [("sourceInfo", DISPLAYCONFIG_PATH_SOURCE_INFO), ("targetInfo", DISPLAYCONFIG_PATH_TARGET_INFO), ("flags", UINT32)]


class DISPLAYCONFIG_DEVICE_INFO_HEADER(ctypes.Structure):
    _fields_ = [("type", UINT32), ("size", UINT32), ("adapterId", LUID), ("id", UINT32)]


class DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO(ctypes.Structure):
    _fields_ = [("header", DISPLAYCONFIG_DEVICE_INFO_HEADER), ("value", UINT32), ("colorEncoding", UINT32), ("bitsPerColorChannel", UINT32)]


class DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE(ctypes.Structure):
    _fields_ = [("header", DISPLAYCONFIG_DEVICE_INFO_HEADER), ("value", UINT32)]


QDC_ONLY_ACTIVE_PATHS = 0x00000002
DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9
DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10
ERROR_SUCCESS = 0


def _copy_luid(value):
    copied = LUID()
    copied.LowPart = value.LowPart
    copied.HighPart = value.HighPart
    return copied


def _active_display_paths():
    if os.name != "nt":
        return []

    user32 = ctypes.windll.user32
    get_sizes = user32.GetDisplayConfigBufferSizes
    query = user32.QueryDisplayConfig

    get_sizes.argtypes = [UINT32, ctypes.POINTER(UINT32), ctypes.POINTER(UINT32)]
    get_sizes.restype = wintypes.LONG
    query.restype = wintypes.LONG

    path_count = UINT32(0)
    mode_count = UINT32(0)
    status = get_sizes(QDC_ONLY_ACTIVE_PATHS, ctypes.byref(path_count), ctypes.byref(mode_count))
    if status != ERROR_SUCCESS or path_count.value <= 0:
        return []

    paths = (DISPLAYCONFIG_PATH_INFO * path_count.value)()
    modes = (DISPLAYCONFIG_MODE_INFO * max(1, mode_count.value))()
    # With QDC_ONLY_ACTIVE_PATHS the topology pointer MUST be NULL; passing a
    # non-NULL pointer makes QueryDisplayConfig fail and report zero displays
    # (this is exactly what broke HDR: the diagnostic showed "paths=0/0").
    status = query(
        QDC_ONLY_ACTIVE_PATHS,
        ctypes.byref(path_count),
        paths,
        ctypes.byref(mode_count),
        modes,
        None,
    )
    if status != ERROR_SUCCESS:
        return []

    return list(paths)[: path_count.value]


# Windows SDK 10.0.26100 wingdi.h: GET_ADVANCED_COLOR_INFO_2 is type 15.
DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15


class DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2(ctypes.Structure):
    _fields_ = [
        ("header", DISPLAYCONFIG_DEVICE_INFO_HEADER),
        ("value", UINT32),
        ("colorEncoding", UINT32),
        ("bitsPerColorChannel", UINT32),
        ("activeColorMode", UINT32),
    ]


def _display_target_key(target):
    return "%d:%d:%d" % (
        int(target.adapterId.HighPart),
        int(target.adapterId.LowPart),
        int(target.id),
    )


def _query_advanced_color(get_device_info, target):
    """Read HDR/advanced-colour info for one target, trying the modern API
    (GET_ADVANCED_COLOR_INFO_2, Win11 24H2+) then the classic one."""
    # Modern API first: it is the reliable one on current Windows builds.
    info2 = DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2()
    info2.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2
    info2.header.size = ctypes.sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2)
    info2.header.adapterId = _copy_luid(target.adapterId)
    info2.header.id = target.id
    if get_device_info(ctypes.byref(info2)) == ERROR_SUCCESS:
        value = int(info2.value)
        # bit0 advancedColorSupported, bit1 advancedColorActive,
        # bit4 highDynamicRangeSupported, bit5 highDynamicRangeUserEnabled.
        return {
            "id": int(target.id),
            "adapter_id": {
                "high": int(target.adapterId.HighPart),
                "low": int(target.adapterId.LowPart),
            },
            "target_key": _display_target_key(target),
            "supported": bool(value & 0x10),
            "enabled": int(info2.activeColorMode) == 2,
            "user_enabled": bool(value & 0x20),
            "wide_color_enforced": False,
            "wide_color_enabled": bool(value & 0x80),
            "force_disabled": bool(value & 0x8),
            "bits_per_color_channel": int(info2.bitsPerColorChannel),
            "color_encoding": int(info2.colorEncoding),
            "color_api": "advanced_color_info_2",
        }

    # Classic API (Win10 / Win11 < 24H2).
    info = DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO()
    info.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO
    info.header.size = ctypes.sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO)
    info.header.adapterId = _copy_luid(target.adapterId)
    info.header.id = target.id
    if get_device_info(ctypes.byref(info)) == ERROR_SUCCESS:
        value = int(info.value)
        return {
            "id": int(target.id),
            "adapter_id": {
                "high": int(target.adapterId.HighPart),
                "low": int(target.adapterId.LowPart),
            },
            "target_key": _display_target_key(target),
            "supported": bool(value & 0x1),
            "enabled": bool(value & 0x2),
            "wide_color_enforced": bool(value & 0x4),
            "force_disabled": bool(value & 0x8),
            "bits_per_color_channel": int(info.bitsPerColorChannel),
            "color_encoding": int(info.colorEncoding),
            "color_api": "advanced_color_info",
        }
    return None


def _get_advanced_color_targets():
    if os.name != "nt":
        return []

    user32 = ctypes.windll.user32
    get_device_info = user32.DisplayConfigGetDeviceInfo
    get_device_info.restype = wintypes.LONG

    targets = []
    for path in _active_display_paths():
        result = _query_advanced_color(get_device_info, path.targetInfo)
        if result:
            targets.append(result)

    return targets


def _read_real_hdr_status():
    if os.name != "nt":
        return _hdr_status_payload(False, False, False, [], "HDR is only available on Windows.", False)

    try:
        targets = _get_advanced_color_targets()
    except Exception as error:
        return _hdr_status_payload(False, False, False, [], f"Could not read HDR state: {error}", False)

    if not targets:
        return _hdr_status_payload(False, False, False, [], "Could not read HDR state from Windows.", False)

    supported = any(target.get("supported") for target in targets)
    enabled = any(target.get("enabled") for target in targets if target.get("supported"))
    if not supported:
        enabled = any(target.get("enabled") for target in targets)

    message = "" if supported else "No active HDR-capable display found."
    return _hdr_status_payload(enabled, supported, supported, targets, message, True)


def _get_hdr_status_sync():
    hdr = _read_real_hdr_status()
    return {"ok": os.name == "nt" and bool(hdr.get("real_state")), "hdr": hdr, **hdr}


def _target_supports_hdr(target):
    """Query a single display target and report whether it advertises HDR."""
    user32 = ctypes.windll.user32
    get_device_info = user32.DisplayConfigGetDeviceInfo
    get_device_info.restype = wintypes.LONG
    result = _query_advanced_color(get_device_info, target)
    return bool(result and result.get("supported"))


DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE = 16


def _write_color_state(target, enabled, info_type):
    """Write one HDR / advanced-colour state to a target; returns Win32 status."""
    user32 = ctypes.windll.user32
    set_device_info = user32.DisplayConfigSetDeviceInfo
    set_device_info.restype = wintypes.LONG
    state = DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE()
    state.header.type = info_type
    state.header.size = ctypes.sizeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE)
    state.header.adapterId = _copy_luid(target.adapterId)
    state.header.id = target.id
    state.value = 1 if enabled else 0
    return int(set_device_info(ctypes.byref(state)))


def _write_hdr_target(target, enabled):
    key = _display_target_key(target)
    # A supported modern read identifies the matching writer. Do not retry a
    # rejected HDR request through the legacy WCG/advanced-color setter.
    get_info = ctypes.windll.user32.DisplayConfigGetDeviceInfo
    get_info.restype = wintypes.LONG
    current = _query_advanced_color(get_info, target)
    if not current or not current.get("supported"):
        return {"target_key": key, "requested": bool(enabled), "accepted": False,
                "api": "none", "message": "HDR capability could not be verified."}
    if current.get("color_api") == "advanced_color_info":
        code = _write_color_state(target, enabled, DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE)
        return {"target_key": key, "requested": bool(enabled), "accepted": code == ERROR_SUCCESS,
                "api": "SET_ADVANCED_COLOR_STATE", "advanced_color_status": code}
    hdr_rc = _write_color_state(
        target, enabled, DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE
    )
    if hdr_rc == ERROR_SUCCESS:
        return {
            "target_key": key,
            "requested": bool(enabled),
            "accepted": True,
            "api": "SET_HDR_STATE",
            "hdr_status": hdr_rc,
        }
    return {
        "target_key": key,
        "requested": bool(enabled),
        "accepted": False,
        "api": "SET_HDR_STATE",
        "hdr_status": hdr_rc,
    }


def _set_advanced_color_states(default_enabled=None, states=None):
    if os.name != "nt":
        return {
            "accepted": False,
            "all_accepted": False,
            "message": "HDR is only available on Windows.",
            "attempts": [],
        }

    paths = _active_display_paths()
    supported_paths = [p for p in paths if _target_supports_hdr(p.targetInfo)]
    desired_states = states if isinstance(states, dict) else {}
    attempts = []
    for path in supported_paths:
        target = path.targetInfo
        key = _display_target_key(target)
        if key in desired_states:
            desired = bool(desired_states[key])
        elif default_enabled is not None:
            desired = bool(default_enabled)
        else:
            continue
        attempts.append(_write_hdr_target(target, desired))
    accepted = any(attempt.get("accepted") for attempt in attempts)
    all_accepted = bool(attempts) and all(
        attempt.get("accepted") for attempt in attempts
    )
    if not supported_paths:
        message = "No active HDR-capable display found."
    elif not attempts:
        message = "No matching active HDR target found."
    elif not all_accepted:
        message = "Windows rejected the HDR request on one or more displays."
    else:
        message = ""
    return {
        "accepted": accepted,
        "all_accepted": all_accepted,
        "paths_total": len(paths),
        "supported_targets": len(supported_paths),
        "attempts": attempts,
        "message": message,
    }


def _hdr_target_states(status):
    if not isinstance(status, dict):
        return {}
    return {
        str(target.get("target_key", "")): bool(target.get("enabled"))
        for target in status.get("targets", [])
        if isinstance(target, dict)
        and target.get("supported")
        and target.get("target_key")
    }


# An LG OLED (and most HDMI 2.1 sinks) drops the link for several seconds
# while HDR is switched on or off. 3.5 s expired mid-handshake, the write was
# declared failed and HDR was toggled straight back - a second renegotiation
# on top of the first is what leaves the panel on "no signal".
HDR_VERIFY_TIMEOUT = 15.0


def _wait_for_hdr_states(expected, timeout=HDR_VERIFY_TIMEOUT):
    expected = {
        str(key): bool(value)
        for key, value in (expected or {}).items()
        if str(key)
    }
    deadline = time.monotonic() + max(0.1, float(timeout))
    last = _read_real_hdr_status()
    while time.monotonic() < deadline:
        states = _hdr_target_states(last)
        if expected and all(states.get(key) == value for key, value in expected.items()):
            return True, last
        time.sleep(0.2)
        last = _read_real_hdr_status()
    states = _hdr_target_states(last)
    return bool(expected) and all(
        states.get(key) == value for key, value in expected.items()
    ), last


def _set_hdr_enabled_sync(enabled):
    enabled = bool(enabled)
    if os.name != "nt":
        hdr = _read_real_hdr_status()
        return {"ok": False, "message": hdr.get("message", "HDR is only available on Windows."), "hdr": hdr, "status": {"hdr": hdr}}

    previous = _read_real_hdr_status()
    previous_enabled = bool(previous.get("enabled"))
    if previous.get("real_state") and previous_enabled == enabled:
        return {
            "ok": True,
            "verified": True,
            "unchanged": True,
            "message": "",
            "hdr": previous,
            "status": {"hdr": previous},
        }
    previous_states = _hdr_target_states(previous)
    if not previous.get("real_state") or not previous_states:
        message = previous.get("message") or "Could not read the active HDR display state."
        return {
            "ok": False,
            "verified": False,
            "message": message,
            "hdr": previous,
            "status": {"hdr": previous},
        }

    requested_states = {key: enabled for key in previous_states}
    try:
        write = _set_advanced_color_states(states=requested_states)
    except Exception as exc:
        write = {
            "accepted": False,
            "all_accepted": False,
            "message": f"{type(exc).__name__}: {exc}",
            "attempts": [],
        }
    verified, hdr = _wait_for_hdr_states(requested_states)
    if verified:
        return {
            "ok": True,
            "verified": True,
            "message": "",
            "write": write,
            "hdr": hdr,
            "status": {"hdr": hdr},
        }

    try:
        rollback_write = _set_advanced_color_states(states=previous_states)
        rollback_verified, rollback_hdr = _wait_for_hdr_states(previous_states)
    except Exception as exc:
        rollback_write = {
            "accepted": False,
            "all_accepted": False,
            "message": f"{type(exc).__name__}: {exc}",
            "attempts": [],
        }
        rollback_verified = False
        rollback_hdr = _read_real_hdr_status()
    message = (
        write.get("message")
        or hdr.get("message")
        or "Windows did not confirm the requested HDR state."
    )
    if rollback_verified:
        message += " The previous HDR state was restored."
    else:
        message += " The previous HDR state could not be confirmed."
    return {
        "ok": False,
        "verified": False,
        "message": message,
        "write": write,
        "rollback": {
            "ok": rollback_verified,
            "write": rollback_write,
            "hdr": rollback_hdr,
        },
        "hdr": rollback_hdr if rollback_verified else hdr,
        "status": {"hdr": rollback_hdr if rollback_verified else hdr},
    }


def _powershell_path():
    if os.name == "nt":
        candidate = os.path.join(os.environ.get("WINDIR", "C:\\Windows"), "System32", "WindowsPowerShell", "v1.0", "powershell.exe")
        if os.path.exists(candidate):
            return candidate
    return "powershell"


_AUDIO_HELPER_SCRIPT = r'''
$ErrorActionPreference = 'Stop'
Add-Type -Language CSharp -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

namespace QuickSettingsAudio
{
    public enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    public enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr pClient);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
        [PreserveSig] int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        [PreserveSig] int GetState(out uint pdwState);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetChannelCount(out uint pnChannelCount);
        [PreserveSig] int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float pfLevelDB);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        [PreserveSig] int GetMute(out bool pbMute);
        [PreserveSig] int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
        [PreserveSig] int VolumeStepUp(ref Guid pguidEventContext);
        [PreserveSig] int VolumeStepDown(ref Guid pguidEventContext);
        [PreserveSig] int QueryHardwareSupport(out uint pdwHardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    public class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, out IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, out IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pEndpointFormat, IntPtr pMixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int bDefault, out long pmftDefaultPeriod, out long pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ref long pmftPeriod);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr key, out IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, int visible);
    }

    public sealed class MicrophoneChangeResult
    {
        public bool Ok;
        public string EndpointId;
        public float? Volume;
        public string Message = "";
    }

    public static class MicrophoneEndpointTransaction
    {
        public static MicrophoneChangeResult Apply(string expected, string actual, float level,
                                                    Func<float> writeAndReadCapturedEndpoint)
        {
            var result = new MicrophoneChangeResult { EndpointId = actual };
            if (String.IsNullOrWhiteSpace(actual) || (!String.IsNullOrEmpty(expected) &&
                !String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)))
            {
                result.Message = "Microphone endpoint changed before volume write.";
                return result;
            }
            try
            {
                float volume = writeAndReadCapturedEndpoint();
                if (!Single.IsNaN(volume) && !Single.IsInfinity(volume) && volume >= 0 && volume <= 100)
                    result.Volume = volume;
                result.Ok = result.Volume.HasValue && Math.Abs(volume - level) <= 1;
                result.Message = result.Ok ? "Microphone volume updated." : "Microphone volume was not confirmed by readback.";
            }
            catch (Exception error) { result.Message = error.Message; }
            return result;
        }
    }

    public sealed class DefaultChangeResult
    {
        public bool Ok;
        public bool RollbackAttempted;
        public bool RollbackVerified;
        public bool RecoveryRequired;
        public string Message = "";
        public string[] Previous = new string[3];
    }

    public static class DefaultRoleTransaction
    {
        private static bool Same(string left, string right)
        {
            return String.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        // Delegates keep the transaction testable without constructing any COM object.
        public static DefaultChangeResult Apply(string id, int expectedFlow, int actualFlow, Func<ERole, string> read,
                                                Func<ERole, string, int> write)
        {
            var result = new DefaultChangeResult();
            var attempted = new bool[3];
            try
            {
                if (String.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Missing endpoint.");
                if ((expectedFlow != 0 && expectedFlow != 1) || actualFlow != expectedFlow)
                    throw new InvalidOperationException("Audio endpoint does not match the requested input/output flow.");
                for (int i = 0; i < 3; i++)
                {
                    result.Previous[i] = read((ERole)i);
                    if (String.IsNullOrWhiteSpace(result.Previous[i]))
                        throw new InvalidOperationException("Could not capture every audio role.");
                }
                // Il preflight va chiuso PRIMA di scrivere. Windows sposta piu' ruoli
                // con una sola SetDefaultEndpoint: controllarlo dentro il ciclo di
                // scrittura faceva scattare l'invariante sull'effetto delle nostre
                // stesse scritture, ed e' quello che l'utente vedeva come
                // "Audio role changed during preflight".
                for (int i = 0; i < 3; i++)
                    if (!Same(read((ERole)i), result.Previous[i]))
                        throw new InvalidOperationException("Audio role changed during preflight.");
                for (int i = 0; i < 3; i++)
                {
                    // Rilettura: un ruolo gia' arrivato a destinazione per effetto di una
                    // scrittura precedente e' un successo, non una violazione.
                    if (Same(read((ERole)i), id)) continue;
                    attempted[i] = true;
                    Marshal.ThrowExceptionForHR(write((ERole)i, id));
                    if (!Same(read((ERole)i), id))
                        throw new InvalidOperationException("Audio role write was not confirmed.");
                }
                for (int i = 0; i < 3; i++)
                    if (!Same(read((ERole)i), id))
                        throw new InvalidOperationException("Audio roles changed before confirmation.");
                result.Ok = true;
                return result;
            }
            catch (Exception error)
            {
                result.Message = error.Message;
            }

            // Include a setter that failed after mutating. Never overwrite an
            // unrelated external selection, and continue restoring other roles.
            for (int i = 2; i >= 0; i--)
            {
                if (!attempted[i]) continue;
                result.RollbackAttempted = true;
                try
                {
                    string current = read((ERole)i);
                    if (Same(current, result.Previous[i])) continue;
                    if (!Same(current, id)) throw new InvalidOperationException("External audio change; restore skipped.");
                    Marshal.ThrowExceptionForHR(write((ERole)i, result.Previous[i]));
                    if (!Same(read((ERole)i), result.Previous[i]))
                        throw new InvalidOperationException("Audio rollback was not confirmed.");
                }
                catch (Exception error)
                {
                    result.RecoveryRequired = true;
                    result.Message += " " + error.Message;
                }
            }
            if (result.RollbackAttempted)
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        if (!Same(read((ERole)i), result.Previous[i])) result.RecoveryRequired = true;
                    }
                    catch { result.RecoveryRequired = true; }
                }
                result.RollbackVerified = !result.RecoveryRequired;
            }
            return result;
        }
    }

    [ComImport, Guid("1BE09788-6894-4089-8586-9A2A6C265AC5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMEndpoint
    {
        [PreserveSig] int GetDataFlow(out EDataFlow flow);
    }

    public static class Audio
    {
        private static IMMDeviceEnumerator Enumerator() { return (IMMDeviceEnumerator)(new MMDeviceEnumeratorComObject()); }

        public static string DefaultId(int flow)
        {
            return DefaultId(flow, ERole.eMultimedia);
        }

        private static string DefaultId(int flow, ERole role)
        {
            try
            {
                IMMDevice device;
                int hr = Enumerator().GetDefaultAudioEndpoint((EDataFlow)flow, role, out device);
                if (hr != 0 || device == null) return String.Empty;
                string id;
                hr = device.GetId(out id);
                return hr == 0 ? id : String.Empty;
            }
            catch { return String.Empty; }
        }

        public static DefaultChangeResult SetDefault(string id, int expectedFlow)
        {
            IMMDevice device;
            Marshal.ThrowExceptionForHR(Enumerator().GetDevice(id, out device));
            EDataFlow flow;
            Marshal.ThrowExceptionForHR(((IMMEndpoint)device).GetDataFlow(out flow));
            if (flow != EDataFlow.eRender && flow != EDataFlow.eCapture)
                throw new InvalidOperationException("Invalid endpoint flow.");
            var policy = (IPolicyConfig)(new PolicyConfigClient());
            return DefaultRoleTransaction.Apply(id, expectedFlow, (int)flow,
                role => DefaultId((int)flow, role),
                (role, endpoint) => policy.SetDefaultEndpoint(endpoint, role));
        }

        public static float EndpointVolume(int flow)
        {
            try
            {
                IMMDevice device;
                int hr = Enumerator().GetDefaultAudioEndpoint((EDataFlow)flow, ERole.eMultimedia, out device);
                if (hr != 0 || device == null) return -1;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                IntPtr ptr;
                hr = device.Activate(ref iid, 23, IntPtr.Zero, out ptr);
                if (hr != 0 || ptr == IntPtr.Zero) return -1;
                try
                {
                    var endpoint = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(ptr);
                    float scalar;
                    hr = endpoint.GetMasterVolumeLevelScalar(out scalar);
                    if (hr != 0) return -1;
                    return scalar * 100.0f;
                }
                finally { Marshal.Release(ptr); }
            }
            catch { return -1; }
        }

        public static MicrophoneChangeResult SetEndpointVolume(int flow, float level, string expected)
        {
            if (level < 0) level = 0;
            if (level > 100) level = 100;
            IMMDevice device;
            int hr = Enumerator().GetDefaultAudioEndpoint((EDataFlow)flow, ERole.eMultimedia, out device);
            if (hr != 0 || device == null)
                return new MicrophoneChangeResult { Message = "Microphone endpoint unavailable." };
            string actual;
            Marshal.ThrowExceptionForHR(device.GetId(out actual));
            return MicrophoneEndpointTransaction.Apply(expected, actual, level, () =>
            {
                // Keep the captured IMMDevice: Windows may change its default
                // after validation, but that must not redirect this write.
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                IntPtr ptr;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out ptr));
                if (ptr == IntPtr.Zero) throw new InvalidOperationException("Microphone volume interface unavailable.");
                try
                {
                    var endpoint = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(ptr);
                    Guid eventContext = Guid.Empty;
                    Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(level / 100.0f, ref eventContext));
                    float scalar;
                    Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out scalar));
                    return scalar * 100.0f;
                }
                finally { Marshal.Release(ptr); }
            });
        }
    }
}
"@

function Get-QSAudioDevices {
    param(
        [Parameter(Mandatory=$true)][ValidateSet('Render','Capture')][string]$Kind,
        [Parameter(Mandatory=$false)][string]$DefaultId = ''
    )

    $basePath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\$Kind"
    $prefix = if ($Kind -eq 'Render') { '{0.0.0.00000000}.' } else { '{0.0.1.00000000}.' }
    $devices = @()

    if (-not (Test-Path $basePath)) { return $devices }

    foreach ($endpoint in Get-ChildItem -Path $basePath -ErrorAction SilentlyContinue) {
        $state = 0
        try { $state = [int](Get-ItemProperty -Path $endpoint.PSPath -Name 'DeviceState' -ErrorAction Stop).DeviceState } catch { $state = 0 }
        if ($state -ne 1) { continue }

        $name = ''
        $propertiesPath = Join-Path $endpoint.PSPath 'Properties'
        if (Test-Path $propertiesPath) {
            try {
                $props = Get-ItemProperty -Path $propertiesPath -ErrorAction Stop
                foreach ($propName in @('{a45c254e-df1c-4efd-8020-67d146a850e0},14', '{a45c254e-df1c-4efd-8020-67d146a850e0},2')) {
                    $value = $props.$propName
                    if ($null -ne $value -and -not [String]::IsNullOrWhiteSpace([string]$value)) {
                        $name = [string]$value
                        break
                    }
                }
            } catch { }
        }

        if ([String]::IsNullOrWhiteSpace($name)) { $name = $endpoint.PSChildName }
        $id = "$prefix$($endpoint.PSChildName)"
        $devices += [PSCustomObject]@{
            id = $id
            name = $name
            is_default = [String]::Equals($id, $DefaultId, [StringComparison]::OrdinalIgnoreCase)
        }
    }

    return $devices
}

$transaction = $null
if ($env:QS_AUDIO_ACTION -eq 'set' -and -not [String]::IsNullOrWhiteSpace($env:QS_AUDIO_DEVICE_ID)) {
    $expectedFlow = if ($env:QS_AUDIO_KIND -eq 'output') { 0 } elseif ($env:QS_AUDIO_KIND -eq 'input') { 1 } else { -1 }
    $transaction = [QuickSettingsAudio.Audio]::SetDefault($env:QS_AUDIO_DEVICE_ID, $expectedFlow)
    Start-Sleep -Milliseconds 150
}

if ($env:QS_AUDIO_ACTION -eq 'set_input_volume' -and -not [String]::IsNullOrWhiteSpace($env:QS_AUDIO_LEVEL)) {
    $expected = if ([String]::IsNullOrEmpty($env:QS_AUDIO_EXPECTED_ENDPOINT)) { $null } else { $env:QS_AUDIO_EXPECTED_ENDPOINT }
    $change = [QuickSettingsAudio.Audio]::SetEndpointVolume(1, [float]$env:QS_AUDIO_LEVEL, $expected)
    $result = [PSCustomObject]@{
        ok = $change.Ok
        message = $change.Message
        endpoint_id = $change.EndpointId
        input_volume = $(if ($null -ne $change.Volume) { [int][Math]::Round($change.Volume) } else { $null })
    }
    $result | ConvertTo-Json -Depth 8 -Compress
    return
}

$outputDefault = [QuickSettingsAudio.Audio]::DefaultId(0)
$inputDefault = [QuickSettingsAudio.Audio]::DefaultId(1)
$inputVolume = [QuickSettingsAudio.Audio]::EndpointVolume(1)
if ($inputVolume -lt 0) { $inputVolume = 0 }
$result = [PSCustomObject]@{
    ok = ($null -eq $transaction -or $transaction.Ok)
    message = $(if ($null -ne $transaction -and -not $transaction.Ok) { $transaction.Message } else { 'Audio devices loaded.' })
    audio_transaction = $transaction
    default_output_id = $outputDefault
    default_input_id = $inputDefault
    input_volume = [int][Math]::Round($inputVolume)
    outputs = @(Get-QSAudioDevices -Kind 'Render' -DefaultId $outputDefault)
    inputs = @(Get-QSAudioDevices -Kind 'Capture' -DefaultId $inputDefault)
}
$result | ConvertTo-Json -Depth 8 -Compress
'''



def _empty_audio_result(message="Audio devices unavailable", code="audio_unavailable"):
    """A failed call. `code` is always set here, so callers (and the UI) can
    tell a broken call apart from a machine that genuinely has no endpoint."""
    return {
        "ok": False,
        "code": code,
        "message": message,
        "outputs": [],
        "inputs": [],
        "default_output_id": "",
        "default_input_id": "",
        "input_volume": 0,
    }


def _normalize_audio_result(data):
    if not isinstance(data, dict):
        return _empty_audio_result("Could not read audio devices.", "audio_helper_output")
    data.setdefault("ok", True)
    data.setdefault("code", "" if data.get("ok") else "audio_change_rejected")
    data.setdefault("message", "Audio devices loaded.")
    data.setdefault("outputs", [])
    data.setdefault("inputs", [])
    data.setdefault("default_output_id", "")
    data.setdefault("default_input_id", "")
    try:
        data["input_volume"] = max(0, min(100, int(round(float(data.get("input_volume", 0))))))
    except Exception:
        data["input_volume"] = 0
    return data


def _run_audio_powershell(action="get", device_id="", level=None, kind=None, expected_endpoint=None):
    if os.name != "nt":
        return _empty_audio_result("Audio device selection is only available on Windows.",
                                   "unsupported_platform")

    env = os.environ.copy()
    env["QS_AUDIO_ACTION"] = action
    env["QS_AUDIO_DEVICE_ID"] = device_id or ""
    env["QS_AUDIO_LEVEL"] = "" if level is None else str(level)
    env["QS_AUDIO_KIND"] = kind or ""
    env["QS_AUDIO_EXPECTED_ENDPOINT"] = expected_endpoint or ""
    try:
        completed = subprocess.run(
            [_powershell_path(), "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", _AUDIO_HELPER_SCRIPT],
            cwd=os.path.dirname(__file__),
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=12,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
    except Exception as error:
        return _empty_audio_result(str(error), "audio_helper_failed")

    if completed.returncode != 0:
        message = (completed.stderr or completed.stdout or "PowerShell audio helper failed.").strip()
        decky.logger.warning(f"Quick Settings audio helper failed: {message}")
        return _empty_audio_result(message, "audio_helper_failed")

    try:
        data = json.loads((completed.stdout or "{}").strip())
        return _normalize_audio_result(data)
    except Exception as error:
        decky.logger.warning(f"Quick Settings audio JSON parse failed: {error}")

    return _empty_audio_result("Could not read audio devices.", "audio_helper_output")


def _audio_cache_get():
    global _AUDIO_CACHE_VALUE, _AUDIO_CACHE_EXPIRES_AT
    with _AUDIO_CACHE_LOCK:
        if _AUDIO_CACHE_VALUE is not None and time.time() < _AUDIO_CACHE_EXPIRES_AT:
            return copy.deepcopy(_AUDIO_CACHE_VALUE)
    return None


def _audio_cache_set(value):
    global _AUDIO_CACHE_VALUE, _AUDIO_CACHE_EXPIRES_AT
    if not isinstance(value, dict) or not value.get("ok"):
        return
    with _AUDIO_CACHE_LOCK:
        _AUDIO_CACHE_VALUE = copy.deepcopy(value)
        _AUDIO_CACHE_EXPIRES_AT = time.time() + AUDIO_CACHE_SECONDS


def _audio_cache_update(updates):
    global _AUDIO_CACHE_VALUE, _AUDIO_CACHE_EXPIRES_AT
    if not isinstance(updates, dict):
        return
    with _AUDIO_CACHE_LOCK:
        if _AUDIO_CACHE_VALUE is None or time.time() >= _AUDIO_CACHE_EXPIRES_AT:
            return
        _AUDIO_CACHE_VALUE.update(copy.deepcopy(updates))


def _audio_cache_clear():
    global _AUDIO_CACHE_VALUE, _AUDIO_CACHE_EXPIRES_AT
    with _AUDIO_CACHE_LOCK:
        _AUDIO_CACHE_VALUE = None
        _AUDIO_CACHE_EXPIRES_AT = 0.0


def _get_audio_devices_sync():
    # Serialize helper completion and cache publication with endpoint changes.
    with _AUDIO_OPERATION_LOCK:
        cached = _audio_cache_get()
        if cached is not None:
            cached["cached"] = True
            return cached
        result = _run_audio_powershell("get", "")
        _audio_cache_set(result)
        return result


def _same_endpoint_id(left, right):
    """MMDevice endpoint ids are case-insensitive: the id shown in the picker
    comes from the registry, the readback from IMMDevice::GetId, and the two
    do not always agree on GUID casing."""
    return str(left or "").casefold() == str(right or "").casefold()


def _set_audio_device_sync(kind, device_id):
    if kind not in ("output", "input"):
        return _empty_audio_result("Invalid audio endpoint kind.", "invalid_endpoint_kind")
    if not device_id:
        return _empty_audio_result("No audio device selected.", "no_device_selected")
    with _AUDIO_OPERATION_LOCK:
        result = _run_audio_powershell("set", device_id, kind=kind)
        field = "default_output_id" if kind == "output" else "default_input_id"
        if result.get("ok") and not _same_endpoint_id(result.get(field), device_id):
            result = {**result, "ok": False, "code": "audio_change_rejected",
                      "message": "Audio device change was not confirmed by readback."}
        elif not result.get("ok"):
            result = {**result, "code": result.get("code") or "audio_change_rejected"}
        if result.get("ok"):
            result["code"] = ""
            result["message"] = "Audio output updated." if kind == "output" else "Microphone input updated."
            _audio_cache_set(result)
        else:
            _audio_cache_clear()
        return result


def _set_microphone_volume_sync(level, expected_endpoint=None):
    level = max(0, min(100, int(level)))
    with _AUDIO_OPERATION_LOCK:
        result = (_run_audio_powershell("set_input_volume", "", level, expected_endpoint=expected_endpoint)
                  if expected_endpoint is not None else _run_audio_powershell("set_input_volume", "", level))
        try:
            observed = float(result.get("input_volume"))
            verified = 0 <= observed <= 100 and abs(observed - level) <= 1
            if expected_endpoint is not None:
                verified = verified and str(result.get("endpoint_id", "")).casefold() == expected_endpoint.casefold()
        except (TypeError, ValueError, OverflowError):
            verified = False
        if result.get("ok") and verified:
            input_volume = int(round(observed))
            _audio_cache_update({"input_volume": input_volume})
            result = {**result, "input_volume": input_volume}
        else:
            _audio_cache_clear()
            result = {**result, "ok": False,
                      "message": "Microphone volume was not confirmed by readback."}
        return result




# ===================================================================== #
#  Capabilities, performance, display and TDP helpers                   #
#                                                                       #
#  Everything below is cross-platform-safe at import time: Windows-only  #
#  APIs (ctypes.windll, winreg, powercfg) are only touched inside the    #
#  functions, guarded by an os.name == "nt" check, so the module still   #
#  imports on non-Windows hosts.                                         #
# ===================================================================== #

_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0)

# Processor power-management power-plan setting GUIDs (stable across Windows).
SUB_PROCESSOR = "54533251-82be-4824-96c1-47b60b740d00"
PERF_BOOST_MODE = "be337238-0d82-4146-a960-4f3749d470c7"
PROC_THROTTLE_MIN = "893dee8e-2bef-41e0-89c6-b55d0929964c"
PROC_THROTTLE_MAX = "bc5038f7-23e0-4960-96da-33abaf5935ec"
PERF_EPP = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863"

BOOST_MODES = {
    "disabled": 0,
    "enabled": 1,
    "aggressive": 2,
    "efficient_enabled": 3,
    "efficient_aggressive": 4,
}

# Windows power-mode ("overlay") scheme GUIDs. Balanced == GUID_NULL (default).
POWER_OVERLAYS = {
    "efficiency": "961cc777-2547-4f9d-8174-7d86181b8a7a",
    "balanced": "00000000-0000-0000-0000-000000000000",
    "better": "3af9b8d9-7c97-431d-ad78-34a8bfea439f",
    "best": "ded574b5-45a0-4f42-8737-46345c09c238",
}


def _arg(request, key, default=None):
    if isinstance(request, dict):
        return request.get(key, default)
    return request if default is None else default


def _coerce_percent(value):
    try:
        return max(0, min(100, int(round(float(value)))))
    except Exception:
        return 0


# --------------------------- CPU identity ---------------------------- #

def _cpu_brand_string():
    if os.name != "nt":
        return ""
    try:
        import winreg
        with winreg.OpenKey(
            winreg.HKEY_LOCAL_MACHINE,
            r"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
        ) as key:
            return str(winreg.QueryValueEx(key, "ProcessorNameString")[0]).strip()
    except Exception:
        return os.environ.get("PROCESSOR_IDENTIFIER", "")


def _is_amd_cpu():
    name = _cpu_brand_string().lower()
    return "amd" in name or "ryzen" in name


def _is_admin():
    if os.name != "nt":
        return False
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def _get_capabilities_sync():
    is_windows = os.name == "nt"
    return {
        "ok": is_windows,
        "platform": "windows" if is_windows else os.name,
        "cpu": _cpu_brand_string(),
        "is_amd": _is_amd_cpu(),
        "performance": is_windows,
        "power_mode": is_windows,
        "display": is_windows,
        "tdp": False,
        "tdp_message": "",
        "lossless": bool(_find_lossless()) if is_windows else False,
        "amd_radeon": is_windows and os.path.exists(_adlx_exe()),
        "amd_build_needed": is_windows and (not os.path.exists(_adlx_exe())) and _adlx_source_present(),
        "amd_path": _adlx_dir() if is_windows else "",
    }


# ----------------------- powercfg (CPU) helpers ---------------------- #

def _powercfg_path():
    if os.name == "nt":
        candidate = os.path.join(
            os.environ.get("WINDIR", "C:\\Windows"), "System32", "powercfg.exe"
        )
        if os.path.exists(candidate):
            return candidate
    return "powercfg"


def _run_powercfg(args, timeout=10):
    try:
        completed = subprocess.run(
            [_powercfg_path()] + args,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=timeout,
            creationflags=_NO_WINDOW,
        )
        return completed.returncode, completed.stdout or "", completed.stderr or ""
    except Exception as error:
        return -1, "", str(error)


def _parse_hex_index(line):
    try:
        return int(line.split(":")[-1].strip(), 16)
    except Exception:
        return None


def _powercfg_query(sub, setting):
    rc, out, _err = _run_powercfg(["/query", "SCHEME_CURRENT", sub, setting])
    if rc != 0:
        return None
    ac = dc = None
    for line in out.splitlines():
        low = line.strip().lower()
        if low.startswith("current ac power setting index"):
            ac = _parse_hex_index(line)
        elif low.startswith("current dc power setting index"):
            dc = _parse_hex_index(line)
    return {"ac": ac, "dc": dc}


def _powercfg_set(sub, setting, value):
    value = int(value)
    rc1, _o1, e1 = _run_powercfg(["/setacvalueindex", "SCHEME_CURRENT", sub, setting, str(value)])
    _rc2, _o2, _e2 = _run_powercfg(["/setdcvalueindex", "SCHEME_CURRENT", sub, setting, str(value)])
    rc3, _o3, e3 = _run_powercfg(["/setactive", "SCHEME_CURRENT"])
    ok = rc1 == 0 and rc3 == 0
    message = "" if ok else (e1 or e3 or "powercfg failed (administrator rights may be required).").strip()
    return ok, message


def _acval(query, default):
    if query and query.get("ac") is not None:
        return query["ac"]
    if query and query.get("dc") is not None:
        return query["dc"]
    return default


# ----------------- elevated apply for CPU power-plan settings --------- #
# powercfg writes need administrator rights. When Decky is not elevated we
# route them through a pre-registered scheduled task that runs the bundled
# helper/apply_perf.ps1 with highest privileges (set up once, via one UAC
# prompt). Power mode and read-back do not need this.

PERF_TASK_NAME = "QuickSettings_ApplyPerf"
PERF_SETTING_GUID = {
    "boost": PERF_BOOST_MODE,
    "epp": PERF_EPP,
    "min": PROC_THROTTLE_MIN,
    "max": PROC_THROTTLE_MAX,
}


def _run_cmd(args, timeout=20):
    try:
        completed = subprocess.run(
            args, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
            timeout=timeout, creationflags=_NO_WINDOW,
        )
        return completed.returncode, completed.stdout or "", completed.stderr or ""
    except Exception as error:
        return -1, "", str(error)


def _perf_helper_dir():
    return os.path.join(os.path.dirname(__file__), "helper")


def _perf_cfg_path():
    return os.path.join(tempfile.gettempdir(), "quick_settings_perf.json")


def _perf_task_exists():
    rc, _o, _e = _run_cmd(["schtasks", "/query", "/tn", PERF_TASK_NAME])
    return rc == 0


def _apply_via_task(settings):
    try:
        with open(_perf_cfg_path(), "w", encoding="utf-8") as handle:
            json.dump(settings, handle)
    except Exception as error:
        return False, str(error)
    rc, out, err = _run_cmd(["schtasks", "/run", "/tn", PERF_TASK_NAME])
    if rc == 0:
        return True, ""
    return False, (err or out or "Could not trigger the elevated helper task.").strip()


def _apply_perf(settings):
    if os.name != "nt":
        return False, "Performance controls are only available on Windows."
    if _is_admin():
        ok = True
        message = ""
        for key, value in settings.items():
            applied, msg = _powercfg_set(SUB_PROCESSOR, PERF_SETTING_GUID[key], value)
            ok = ok and applied
            if msg and not message:
                message = msg
        return ok, message
    if _perf_task_exists():
        return _apply_via_task(settings)
    return False, "needs_setup"


def _setup_perf_elevation_sync():
    if os.name != "nt":
        return {"ok": False, "message": "Performance controls are only available on Windows."}
    setup = os.path.join(_perf_helper_dir(), "setup_perf.ps1")
    if not os.path.exists(setup):
        return {"ok": False, "message": "Helper script not found in the plugin."}
    command = (
        "Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -Wait "
        "-ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File','"
        + setup + "'"
    )
    _run_cmd([_powershell_path(), "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command], timeout=120)
    if _perf_task_exists():
        return {"ok": True, "message": ""}
    return {"ok": False, "message": "Elevation was cancelled or the helper task could not be created."}


def _get_perf_elevation_status_sync():
    return {
        "ok": True,
        "elevated_ready": _is_admin() or _perf_task_exists(),
        "is_admin": _is_admin(),
        "task": _perf_task_exists(),
    }


def _get_performance_status_sync():
    if os.name != "nt":
        return {"ok": False, "message": "Performance controls are only available on Windows."}
    overlay = _get_power_overlay()
    if overlay is None:
        return {"ok": True, "detected": False, "power_mode": "balanced",
                "message": "Windows did not report the active power mode."}
    return {"ok": True, "detected": True, "message": "", "power_mode": overlay}


def _set_cpu_boost_sync(mode):
    key = str(mode).lower()
    if key in ("true", "on", "1", "yes"):
        key = "enabled"
    elif key in ("false", "off", "0", "no"):
        key = "disabled"
    value = BOOST_MODES.get(key, 1)
    ok, message = _apply_perf({"boost": value})
    return {"ok": ok, "message": message, "boost_mode": value, "boost_enabled": bool(value)}


def _set_cpu_epp_sync(value):
    value = max(0, min(100, int(value)))
    ok, message = _apply_perf({"epp": value})
    return {"ok": ok, "message": message, "epp": value}


def _set_processor_state_sync(which, value):
    value = max(0, min(100, int(value)))
    ok, message = _apply_perf({("min" if which == "min" else "max"): value})
    field = "min_processor_state" if which == "min" else "max_processor_state"
    return {"ok": ok, "message": message, field: value}


# ------------------- Windows power-mode overlay ---------------------- #

class _GUID(ctypes.Structure):
    _fields_ = [
        ("Data1", ctypes.c_uint32),
        ("Data2", ctypes.c_uint16),
        ("Data3", ctypes.c_uint16),
        ("Data4", ctypes.c_ubyte * 8),
    ]


def _guid_from_string(text):
    text = text.strip("{}")
    parts = text.split("-")
    guid = _GUID()
    guid.Data1 = int(parts[0], 16)
    guid.Data2 = int(parts[1], 16)
    guid.Data3 = int(parts[2], 16)
    tail = bytes.fromhex(parts[3] + parts[4])
    for i in range(8):
        guid.Data4[i] = tail[i]
    return guid


def _guid_to_string(guid):
    tail = bytes(guid.Data4)
    return "%08x-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x" % (
        guid.Data1, guid.Data2, guid.Data3,
        tail[0], tail[1], tail[2], tail[3], tail[4], tail[5], tail[6], tail[7],
    )


def _get_power_overlay():
    """The active Windows 11 power mode, or None when it cannot be read."""
    if os.name != "nt":
        return None
    try:
        powrprof = ctypes.windll.powrprof
        current = _GUID()
        powrprof.PowerGetActualOverlayScheme.argtypes = [ctypes.POINTER(_GUID)]
        powrprof.PowerGetActualOverlayScheme.restype = wintypes.DWORD
        if powrprof.PowerGetActualOverlayScheme(ctypes.byref(current)) == 0:
            value = _guid_to_string(current).lower()
            for name, guid in POWER_OVERLAYS.items():
                if guid.lower() == value:
                    return name
            return None
    except Exception as error:
        decky.logger.warning(f"Quick Settings power overlay read failed: {error}")
    return None


def _set_power_overlay_sync(mode):
    if os.name != "nt":
        return {"ok": False, "message": "Power mode is only available on Windows."}
    guid_str = POWER_OVERLAYS.get(str(mode).lower())
    if guid_str is None:
        return {"ok": False, "message": f"Unknown power mode '{mode}'."}
    try:
        powrprof = ctypes.windll.powrprof
        powrprof.PowerSetActiveOverlayScheme.argtypes = [_GUID]
        powrprof.PowerSetActiveOverlayScheme.restype = wintypes.DWORD
        result = powrprof.PowerSetActiveOverlayScheme(_guid_from_string(guid_str))
        if result != 0:
            return {"ok": False, "code": "power_mode_write_failed",
                    "message": f"PowerSetActiveOverlayScheme failed (code {result}).",
                    "power_mode": _get_power_overlay() or "balanced"}
        # PowerSetActiveOverlayScheme returns ERROR_SUCCESS even when Windows
        # ignores the overlay (a non-stock active power plan hides the modern
        # power mode entirely). Only a readback proves the mode really moved.
        target = str(mode).lower()
        observed = None
        deadline = time.monotonic() + 2.0
        while True:
            observed = _get_power_overlay()
            if observed == target or time.monotonic() >= deadline:
                break
            time.sleep(0.15)
        if observed != target:
            return {
                "ok": False,
                "code": "power_mode_not_applied",
                "message": ("Windows accepted the power mode but is still reporting "
                            f"'{observed or 'unknown'}'. The active power plan does not "
                            "support the Windows 11 power mode; select a default plan "
                            "(Balanced) in Control Panel and try again."),
                "power_mode": observed or "balanced",
                "detected": observed is not None,
            }
        return {"ok": True, "detected": True, "message": "", "power_mode": target}
    except Exception as error:
        return {"ok": False, "code": "power_mode_write_failed", "message": str(error)}


# --------------------- Display resolution / refresh ------------------ #

_CCHDEVICENAME = 32
_CCHFORMNAME = 32
_ENUM_CURRENT_SETTINGS = 0xFFFFFFFF
_DM_BITSPERPEL = 0x00040000
_DM_PELSWIDTH = 0x00080000
_DM_PELSHEIGHT = 0x00100000
_DM_DISPLAYFREQUENCY = 0x00400000
_CDS_UPDATEREGISTRY = 0x00000001
_CDS_TEST = 0x00000002
_DISP_CHANGE_SUCCESSFUL = 0


class _DEVMODE(ctypes.Structure):
    _fields_ = [
        ("dmDeviceName", ctypes.c_wchar * _CCHDEVICENAME),
        ("dmSpecVersion", ctypes.c_uint16),
        ("dmDriverVersion", ctypes.c_uint16),
        ("dmSize", ctypes.c_uint16),
        ("dmDriverExtra", ctypes.c_uint16),
        ("dmFields", ctypes.c_uint32),
        ("dmPositionX", ctypes.c_int32),
        ("dmPositionY", ctypes.c_int32),
        ("dmDisplayOrientation", ctypes.c_uint32),
        ("dmDisplayFixedOutput", ctypes.c_uint32),
        ("dmColor", ctypes.c_int16),
        ("dmDuplex", ctypes.c_int16),
        ("dmYResolution", ctypes.c_int16),
        ("dmTTOption", ctypes.c_int16),
        ("dmCollate", ctypes.c_int16),
        ("dmFormName", ctypes.c_wchar * _CCHFORMNAME),
        ("dmLogPixels", ctypes.c_uint16),
        ("dmBitsPerPel", ctypes.c_uint32),
        ("dmPelsWidth", ctypes.c_uint32),
        ("dmPelsHeight", ctypes.c_uint32),
        ("dmDisplayFlags", ctypes.c_uint32),
        ("dmDisplayFrequency", ctypes.c_uint32),
        ("dmICMMethod", ctypes.c_uint32),
        ("dmICMIntent", ctypes.c_uint32),
        ("dmMediaType", ctypes.c_uint32),
        ("dmDitherType", ctypes.c_uint32),
        ("dmReserved1", ctypes.c_uint32),
        ("dmReserved2", ctypes.c_uint32),
        ("dmPanningWidth", ctypes.c_uint32),
        ("dmPanningHeight", ctypes.c_uint32),
    ]


def _primary_display_name():
    class DisplayDevice(ctypes.Structure):
        _fields_ = [("cb", ctypes.c_uint32), ("name", ctypes.c_wchar * 32),
                    ("description", ctypes.c_wchar * 128), ("flags", ctypes.c_uint32),
                    ("device_id", ctypes.c_wchar * 128), ("device_key", ctypes.c_wchar * 128)]
    for index in range(32):
        device = DisplayDevice()
        device.cb = ctypes.sizeof(device)
        if not ctypes.windll.user32.EnumDisplayDevicesW(None, index, ctypes.byref(device), 0):
            break
        if device.flags & 4:
            return device.name
    return None


def _read_current_devmode(device_name=None):
    user32 = ctypes.windll.user32
    dm = _DEVMODE()
    dm.dmSize = ctypes.sizeof(_DEVMODE)
    if not user32.EnumDisplaySettingsW(device_name, _ENUM_CURRENT_SETTINGS, ctypes.byref(dm)):
        return None
    return dm


def _get_registered_display_mode_sync(device_name):
    if os.name != "nt":
        return None
    dm = _DEVMODE()
    dm.dmSize = ctypes.sizeof(_DEVMODE)
    if not ctypes.windll.user32.EnumDisplaySettingsW(device_name, -2, ctypes.byref(dm)):
        return None
    return {"width": int(dm.dmPelsWidth), "height": int(dm.dmPelsHeight), "hz": int(dm.dmDisplayFrequency)}


def _enum_display_modes(device_name=None):
    user32 = ctypes.windll.user32
    modes = []
    seen = set()
    index = 0
    while True:
        dm = _DEVMODE()
        dm.dmSize = ctypes.sizeof(_DEVMODE)
        if not user32.EnumDisplaySettingsW(device_name, index, ctypes.byref(dm)):
            break
        index += 1
        if dm.dmBitsPerPel and dm.dmBitsPerPel < 32:
            continue
        key = (int(dm.dmPelsWidth), int(dm.dmPelsHeight), int(dm.dmDisplayFrequency))
        if key in seen:
            continue
        seen.add(key)
        modes.append({"width": key[0], "height": key[1], "hz": key[2]})
    return modes


def _get_display_status_sync(device_name=None):
    empty = {"ok": False, "message": "", "current": {}, "modes": [], "resolutions": [], "refresh_rates": []}
    if os.name != "nt":
        empty["message"] = "Display controls are only available on Windows."
        return empty
    try:
        cur = _read_current_devmode(device_name)
        current = (
            {"width": int(cur.dmPelsWidth), "height": int(cur.dmPelsHeight), "hz": int(cur.dmDisplayFrequency)}
            if cur else {}
        )
        modes = _enum_display_modes(device_name)
        resolutions = sorted(
            {(m["width"], m["height"]) for m in modes}, key=lambda r: r[0] * r[1], reverse=True
        )
        resolutions = [{"width": w, "height": h} for (w, h) in resolutions]
        refresh = sorted(
            {
                m["hz"] for m in modes
                if not current or (m["width"] == current.get("width") and m["height"] == current.get("height"))
            },
            reverse=True,
        )
        return {
            "ok": True,
            "message": "",
            "current": current,
            "modes": modes,
            "resolutions": resolutions,
            "refresh_rates": refresh,
        }
    except Exception as error:
        empty["message"] = str(error)
        return empty


def _resolve_display_mode_sync(desired, device_name=None):
    """Map a partially specified request onto a mode Windows actually reports.

    Changing only the resolution used to carry the current refresh rate over
    verbatim; when that pair is not in the mode list ChangeDisplaySettingsEx
    fails with DISP_CHANGE_BADMODE and the whole transaction rolls straight
    back, which reads as "the setting does nothing"."""
    if os.name != "nt":
        return None, "Display controls are only available on Windows.", "unsupported_platform"
    try:
        modes = _enum_display_modes(device_name)
    except Exception as error:
        return None, str(error), "display_enumeration_failed"
    if not modes:
        return None, "Windows reported no display modes.", "display_enumeration_failed"
    width, height, hz = int(desired.get("width", 0)), int(desired.get("height", 0)), int(desired.get("hz", 0))
    if any(m["width"] == width and m["height"] == height and m["hz"] == hz for m in modes):
        return {"width": width, "height": height, "hz": hz}, "", ""
    same_size = [m for m in modes if m["width"] == width and m["height"] == height]
    if same_size:
        # Keep the closest refresh rate the resolution supports, preferring a
        # faster one on a tie so a 120 Hz panel does not silently drop to 60.
        best = min(same_size, key=lambda m: (abs(m["hz"] - hz), -m["hz"]))
        return dict(best), "", ""
    return None, f"The display does not support {width} x {height} at {hz} Hz.", "display_mode_unsupported"


def _set_display_mode_sync(width, height, hz, persist=True, device_name=None):
    if os.name != "nt":
        return {"ok": False, "message": "Display controls are only available on Windows."}
    try:
        user32 = ctypes.windll.user32
        cur = _read_current_devmode(device_name)
        if cur is None:
            return {"ok": False, "message": "Could not read the current display mode."}
        dm = _DEVMODE()
        ctypes.memmove(ctypes.byref(dm), ctypes.byref(cur), ctypes.sizeof(_DEVMODE))
        dm.dmSize = ctypes.sizeof(_DEVMODE)
        fields = 0
        if width > 0 and height > 0:
            dm.dmPelsWidth = width
            dm.dmPelsHeight = height
            fields |= _DM_PELSWIDTH | _DM_PELSHEIGHT
        if hz > 0:
            dm.dmDisplayFrequency = hz
            fields |= _DM_DISPLAYFREQUENCY
        if fields == 0:
            return {"ok": False, "message": "No display change requested."}
        dm.dmFields = fields
        test = user32.ChangeDisplaySettingsExW(device_name, ctypes.byref(dm), None, _CDS_TEST, None)
        if test != _DISP_CHANGE_SUCCESSFUL:
            return {"ok": False, "message": f"Display mode not supported (code {test})."}
        applied = user32.ChangeDisplaySettingsExW(device_name, ctypes.byref(dm), None, _CDS_UPDATEREGISTRY if persist else 0, None)
        ok = applied == _DISP_CHANGE_SUCCESSFUL
        result = {"ok": ok, "message": "" if ok else f"Could not apply display mode (code {applied})."}
        if ok:
            deadline = time.monotonic() + 3
            verified = False
            while time.monotonic() < deadline:
                actual = _read_current_devmode(device_name)
                if actual:
                    result["current"] = {"width": int(actual.dmPelsWidth), "height": int(actual.dmPelsHeight), "hz": int(actual.dmDisplayFrequency)}
                    verified = (actual.dmPelsWidth == dm.dmPelsWidth and actual.dmPelsHeight == dm.dmPelsHeight
                                and actual.dmDisplayFrequency == dm.dmDisplayFrequency)
                    if verified:
                        break
                time.sleep(0.15)
            result["ok"] = verified
            if verified and persist:
                result["ok"] = _get_registered_display_mode_sync(device_name) == {
                    "width": int(dm.dmPelsWidth), "height": int(dm.dmPelsHeight), "hz": int(dm.dmDisplayFrequency)}
            result["verified"] = result["ok"]
            if not result["ok"]:
                result["message"] = "Windows did not confirm the requested display mode."
        return result
    except Exception as error:
        return {"ok": False, "message": str(error)}


# ------------------------- CPU power -------------------------------- #

def _get_tdp_status_sync():
    return {"ok": False, "available": False, "supported_read": False,
            "supported_write": False, "hardware_accessed": False,
            "reason": "feature_removed", "message": "CPU power control has been removed."}


def _tdp_probe():
    return _get_tdp_status_sync()


def _set_tdp_sync(*args, **kwargs):
    return _get_tdp_status_sync()


def _set_cpu_ppt_sync(*args, **kwargs):
    return _get_tdp_status_sync()


def _restore_cpu_ppt_sync(*args, **kwargs):
    return _get_tdp_status_sync()


# ===================== Lossless Scaling integration ================== #

LOSSLESS_APPID = "993090"
_LS_SCALING_ACTIVE = {"on": False}
# While True, the launch helper keeps Lossless Scaling's control window hidden.
# It is switched off the moment scaling is armed so the actual scaling OVERLAY
# (also owned by LosslessScaling.exe) is never hidden - hiding it was why the
# game looked like it was "active but not scaled".
_LS_HIDE_ACTIVE = {"on": False}
_LS_ACTIVATION_SETTLE_SECONDS = 1.2

_MODIFIER_VK = {"control": 0x11, "ctrl": 0x11, "alt": 0x12, "menu": 0x12, "shift": 0x10, "win": 0x5B}


def _key_to_vk(name):
    name = (name or "").strip()
    if len(name) == 1:
        ch = name.upper()
        if "A" <= ch <= "Z" or "0" <= ch <= "9":
            return ord(ch)
    if len(name) >= 2 and name[0].upper() == "F" and name[1:].isdigit():
        n = int(name[1:])
        if 1 <= n <= 24:
            return 0x70 + (n - 1)
    return None


def _send_key_combo(vks):
    if os.name != "nt" or not vks:
        return False
    try:
        user32 = ctypes.windll.user32
        ULONG_PTR = ctypes.c_ulonglong if ctypes.sizeof(ctypes.c_void_p) == 8 else ctypes.c_ulong

        class KEYBDINPUT(ctypes.Structure):
            _fields_ = [
                ("wVk", wintypes.WORD),
                ("wScan", wintypes.WORD),
                ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD),
                ("dwExtraInfo", ULONG_PTR),
            ]

        class MOUSEINPUT(ctypes.Structure):
            _fields_ = [
                ("dx", wintypes.LONG),
                ("dy", wintypes.LONG),
                ("mouseData", wintypes.DWORD),
                ("dwFlags", wintypes.DWORD),
                ("time", wintypes.DWORD),
                ("dwExtraInfo", ULONG_PTR),
            ]

        class HARDWAREINPUT(ctypes.Structure):
            _fields_ = [
                ("uMsg", wintypes.DWORD),
                ("wParamL", wintypes.WORD),
                ("wParamH", wintypes.WORD),
            ]

        class INPUTUNION(ctypes.Union):
            _fields_ = [
                ("ki", KEYBDINPUT),
                ("mi", MOUSEINPUT),
                ("hi", HARDWAREINPUT),
            ]

        class INPUT(ctypes.Structure):
            _anonymous_ = ("union",)
            _fields_ = [("type", wintypes.DWORD), ("union", INPUTUNION)]

        events = []
        for vk in vks:
            events.append(INPUT(type=1, ki=KEYBDINPUT(vk, 0, 0, 0, 0)))
        for vk in reversed(vks):
            events.append(INPUT(type=1, ki=KEYBDINPUT(vk, 0, 0x0002, 0, 0)))
        event_array = (INPUT * len(events))(*events)
        user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
        user32.SendInput.restype = wintypes.UINT
        return int(user32.SendInput(len(events), event_array, ctypes.sizeof(INPUT))) == len(events)
    except Exception:
        return False


def _current_session_id():
    if os.name != "nt":
        return -1
    session = wintypes.DWORD()
    try:
        if ctypes.windll.kernel32.ProcessIdToSessionId(os.getpid(), ctypes.byref(session)):
            return int(session.value)
    except Exception:
        pass
    return -1


def _window_snapshot(hwnd):
    if os.name != "nt" or not hwnd:
        return {"hwnd": 0, "pid": 0, "process": "", "path": "", "title": ""}
    user32 = ctypes.windll.user32
    hwnd = int(hwnd or 0)
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    length = int(user32.GetWindowTextLengthW(hwnd) or 0)
    buffer = ctypes.create_unicode_buffer(max(1, length + 1))
    user32.GetWindowTextW(hwnd, buffer, len(buffer))
    process_path = _process_path_for_pid(int(pid.value))
    return {
        "hwnd": hwnd,
        "pid": int(pid.value),
        "process": os.path.basename(process_path).lower(),
        "path": process_path,
        "title": buffer.value,
    }


def _foreground_window_snapshot():
    if os.name != "nt":
        return {"hwnd": 0, "pid": 0, "process": "", "path": "", "title": ""}
    return _window_snapshot(int(ctypes.windll.user32.GetForegroundWindow() or 0))


def _process_path_for_pid(pid):
    if os.name != "nt" or not pid:
        return ""
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    kernel32 = ctypes.windll.kernel32
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.QueryFullProcessImageNameW.argtypes = [
        wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD),
    ]
    kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, int(pid))
    if not handle:
        return ""
    try:
        capacity = wintypes.DWORD(32768)
        buffer = ctypes.create_unicode_buffer(capacity.value)
        if kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(capacity)):
            return buffer.value
    except Exception:
        pass
    finally:
        kernel32.CloseHandle(handle)
    return ""


def _process_name_for_pid(pid):
    return os.path.basename(_process_path_for_pid(pid)).lower()


# Known emulator executables. Emulated titles are almost always launched as
# non-Steam shortcuts, so Steam reports a shortcut name (e.g. "007: Quantum of
# Solace") while the real game window belongs to the emulator process and carries
# the emulator's own title. Title-derived Lossless filters therefore never match,
# which is why scaling could not find the window for any emulated game. These
# names let the detector recognize the real render window regardless of title.
_EMULATOR_PROCESS_NAMES = {
    "rpcs3.exe",
    "pcsx2.exe", "pcsx2-qt.exe", "pcsx2x64.exe", "pcsx2x64-avx2.exe",
    "dolphin.exe", "dolphinqt2.exe",
    "cemu.exe",
    "duckstation-qt-x64-releaseltcg.exe", "duckstation-nogui-x64-releaseltcg.exe",
    "duckstation-qt-x64-release.exe",
    "ppsspp.exe", "ppssppwindows.exe", "ppssppwindows64.exe",
    "xenia.exe", "xenia_canary.exe",
    "ryujinx.exe", "ryujinx.ava.exe", "ryujinx.headless.sdl2.exe",
    "yuzu.exe", "suyu.exe", "sudachi.exe", "eden.exe",
    "citra-qt.exe", "lime3ds.exe", "lime3ds-qt.exe", "azahar.exe",
    "retroarch.exe",
    "mgba.exe", "mgba-qt.exe",
    "flycast.exe", "redream.exe",
    "vita3k.exe",
    "xemu.exe",
    "melonds.exe",
    "mednafen.exe", "mednaffe.exe",
    "snes9x.exe", "snes9x-x64.exe", "bsnes.exe",
    "project64.exe", "mupen64plus.exe", "rmg.exe", "simple64-gui.exe",
    "dosbox.exe", "dosbox-x.exe", "dosboxstaging.exe",
    "scummvm.exe",
    "ares.exe",
    "shadps4.exe",
    "emuhawk.exe", "bizhawk.exe",
    "supermodel.exe", "model2emu.exe", "demul.exe",
    "fpkdc.exe", "play.exe", "xenia-canary.exe",
    "vitadeck.exe",
}


def _is_emulator_process(process_name):
    return str(process_name or "").strip().lower() in _EMULATOR_PROCESS_NAMES


def _is_scaling_target_window(snapshot):
    if os.name != "nt" or not isinstance(snapshot, dict):
        return False
    hwnd = int(snapshot.get("hwnd", 0) or 0)
    process_name = str(snapshot.get("process", "") or "").lower()
    if not hwnd or not process_name:
        return False
    excluded = {
        "steam.exe", "steamwebhelper.exe", "gamingmode.exe", "gameoverlayui.exe",
        "steamerrorreporter.exe", "explorer.exe", "losslessscaling.exe",
        "quicksettingsagent.exe", "python.exe", "pythonw.exe",
    }
    if process_name in excluded or "curtain" in process_name:
        return False
    try:
        user32 = ctypes.windll.user32
        if not user32.IsWindow(hwnd) or not user32.IsWindowVisible(hwnd):
            return False
        rect = wintypes.RECT()
        if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
            return False
        width = max(0, int(rect.right - rect.left))
        height = max(0, int(rect.bottom - rect.top))
        return width >= 640 and height >= 360
    except Exception:
        return False


def _active_emulator_window():
    """Return an eligible emulator game window if one is open, else {}."""
    if os.name != "nt":
        return {}
    user32 = ctypes.windll.user32
    found = []

    def callback(hwnd, _lparam):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        process_path = _process_path_for_pid(int(pid.value))
        process = os.path.basename(process_path).lower()
        if not _is_emulator_process(process):
            return True
        length = int(user32.GetWindowTextLengthW(hwnd) or 0)
        title_buffer = ctypes.create_unicode_buffer(max(1, length + 1))
        user32.GetWindowTextW(hwnd, title_buffer, len(title_buffer))
        snapshot = {
            "hwnd": int(hwnd),
            "pid": int(pid.value),
            "process": process,
            "path": process_path,
            "title": title_buffer.value,
        }
        if _is_scaling_target_window(snapshot):
            found.append(snapshot)
        return True

    try:
        for _hwnd in _enum_top_level_windows():
            callback(_hwnd, 0)
    except Exception:
        return {}
    return found[0] if found else {}


def _process_parent_map():
    """Return {pid: parent_pid} for every process (empty off Windows)."""
    if os.name != "nt":
        return {}
    kernel32 = ctypes.windll.kernel32
    snapshot = kernel32.CreateToolhelp32Snapshot(0x00000002, 0)
    if snapshot in (0, ctypes.c_void_p(-1).value):
        return {}
    entry = _PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(entry)
    parents = {}
    try:
        ok = kernel32.Process32FirstW(snapshot, ctypes.byref(entry))
        while ok:
            parents[int(entry.th32ProcessID)] = int(entry.th32ParentProcessID)
            ok = kernel32.Process32NextW(snapshot, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snapshot)
    return parents


def _descends_from(pid, ancestor_pids, parent_map, max_depth=12):
    """True if pid has any of ancestor_pids in its parent chain."""
    current = int(pid or 0)
    seen = set()
    depth = 0
    while current and current not in seen and depth < max_depth:
        if current in ancestor_pids:
            return True
        seen.add(current)
        parent = int(parent_map.get(current, 0) or 0)
        if parent in (0, 4) or parent == current:
            break
        current = parent
        depth += 1
    return False


def _steam_launched_game_window():
    """Universal game-window finder. Steam launches every title - a native game
    OR a non-Steam shortcut / emulator - as a descendant of steam.exe, so the game
    window is the eligible top-level window whose process descends from steam.exe.
    This needs no per-emulator list and no title guessing, and never matches an
    unrelated app (e.g. Notepad) because it is not a child of Steam. Returns {}."""
    if os.name != "nt":
        return {}
    steam_pids = set(_process_ids_by_name("steam.exe"))
    if not steam_pids:
        return {}
    parent_map = _process_parent_map()
    user32 = ctypes.windll.user32
    candidates = []

    def callback(hwnd, _lparam):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        process_path = _process_path_for_pid(int(pid.value))
        length = int(user32.GetWindowTextLengthW(hwnd) or 0)
        title_buffer = ctypes.create_unicode_buffer(max(1, length + 1))
        user32.GetWindowTextW(hwnd, title_buffer, len(title_buffer))
        snapshot = {
            "hwnd": int(hwnd),
            "pid": int(pid.value),
            "process": os.path.basename(process_path).lower(),
            "path": process_path,
            "title": title_buffer.value,
        }
        if not _is_scaling_target_window(snapshot):
            return True
        if not _descends_from(int(pid.value), steam_pids, parent_map):
            return True
        rect = wintypes.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(rect))
        snapshot["area"] = max(0, rect.right - rect.left) * max(0, rect.bottom - rect.top)
        candidates.append(snapshot)
        return True

    try:
        for _hwnd in _enum_top_level_windows():
            callback(_hwnd, 0)
    except Exception:
        return {}
    if not candidates:
        return {}
    foreground_hwnd = int(_foreground_window_snapshot().get("hwnd", 0) or 0)
    for snapshot in candidates:
        if int(snapshot.get("hwnd", 0) or 0) == foreground_hwnd:
            result = dict(snapshot)
            result.pop("area", None)
            result["matched_filter"] = "<steam-child-foreground>"
            return result
    candidates.sort(key=lambda item: int(item.get("area", 0) or 0), reverse=True)
    result = dict(candidates[0])
    result.pop("area", None)
    result["matched_filter"] = "<steam-child>"
    return result


def _detect_game_window(filters):
    """Universal detector for the currently running game's window.
    Order of preference:
      1. a window whose exe path / title matches the profile filters (specific),
      2. the eligible window whose process descends from steam.exe (universal),
      3. a known-emulator window (net for emulators detached from Steam's tree).
    Returns the window snapshot, or {}."""
    match = _find_matching_scaling_window(list(filters or []), allow_fallback=False)
    if match:
        return match
    steam_child = _steam_launched_game_window()
    if steam_child:
        return steam_child
    return _active_emulator_window()


def _wait_for_lossless_autostart_target(
    filters,
    token="",
    app_id=0,
    timeout_seconds=_LOSSLESS_AUTOSTART_FOREGROUND_TIMEOUT_SECONDS,
    stable_seconds=_LOSSLESS_AUTOSTART_STABLE_SECONDS,
):
    deadline = time.time() + max(0.0, float(timeout_seconds))
    stable_hwnd = 0
    stable_since = 0.0
    observations = []
    last_observation = None

    while time.time() < deadline:
        if token and str(_LS_AUTOSTART_GATE.get("token", "")) != str(token):
            return {
                "ok": False,
                "cancelled": True,
                "reason": "superseded",
                "observations": observations,
            }
        if (
            app_id
            and int(
                _LS_SESSION_CONTROL.get("manual_disabled_app_id", 0) or 0
            ) == int(app_id)
        ):
            return {
                "ok": False,
                "cancelled": True,
                "reason": "manual_override",
                "observations": observations,
            }
        target = _detect_game_window(list(filters or []))
        foreground = _foreground_window_snapshot()
        target_hwnd = int(target.get("hwnd", 0) or 0)
        foreground_hwnd = int(foreground.get("hwnd", 0) or 0)
        foreground_pid = int(foreground.get("pid", 0) or 0)
        target_pid = int(target.get("pid", 0) or 0)

        # Some games and emulators expose more than one top-level surface. When
        # the foreground surface belongs to the detected game process, capture
        # that surface instead of a stale splash or launcher window.
        if (
            target
            and foreground_pid
            and foreground_pid == target_pid
            and _is_scaling_target_window(foreground)
        ):
            target = {
                **foreground,
                "matched_filter": str(
                    target.get("matched_filter", "<foreground-process>")
                ),
            }
            target_hwnd = foreground_hwnd

        observation = {
            "target_hwnd": target_hwnd,
            "target_pid": int(target.get("pid", 0) or 0),
            "target_process": str(target.get("process", "") or ""),
            "foreground_hwnd": foreground_hwnd,
            "foreground_pid": foreground_pid,
            "foreground_process": str(foreground.get("process", "") or ""),
            "steam_overlay_active": bool(_STEAM_OVERLAY_STATE.get("active")),
        }
        if observation != last_observation:
            observations.append(observation)
            observations = observations[-12:]
            last_observation = observation

        ready = (
            bool(target)
            and target_hwnd > 0
            and foreground_hwnd == target_hwnd
            and not _STEAM_OVERLAY_STATE.get("active")
        )
        if ready:
            if stable_hwnd != target_hwnd:
                stable_hwnd = target_hwnd
                stable_since = time.time()
            elif time.time() - stable_since >= max(0.1, float(stable_seconds)):
                return {
                    "ok": True,
                    "target": target,
                    "stable_ms": int((time.time() - stable_since) * 1000),
                    "observations": observations,
                }
        else:
            stable_hwnd = 0
            stable_since = 0.0
        time.sleep(0.12)

    return {
        "ok": False,
        "pending": True,
        "target": _detect_game_window(list(filters or [])),
        "foreground": _foreground_window_snapshot(),
        "observations": observations,
    }


def _cancel_lossless_autostart(reason="cancelled"):
    previous_token = str(_LS_AUTOSTART_GATE.get("token", "") or "")
    _LS_AUTOSTART_GATE.update({
        "token": "",
        "app_id": 0,
        "title": "",
        "worker_running": False,
        "last_result": {
            "ok": True,
            "active": False,
            "cancelled": bool(previous_token),
            "reason": str(reason or "cancelled"),
        },
    })


def _compact_lossless_result(result):
    if not isinstance(result, dict):
        return {}
    runtime = (
        result.get("runtime", {})
        if isinstance(result.get("runtime"), dict)
        else {}
    )
    core_status = result.get("core_status", {})
    if not isinstance(core_status, dict) or not core_status:
        core_status = (
            runtime.get("core_status", {})
            if isinstance(runtime.get("core_status"), dict)
            else {}
        )
    activation_focus = (
        runtime.get("activation_focus", {})
        if isinstance(runtime.get("activation_focus"), dict)
        else {}
    )
    target = (
        activation_focus.get("target", {})
        if isinstance(activation_focus.get("target"), dict)
        else {}
    )
    compact = {
        key: result.get(key)
        for key in (
            "ok",
            "active",
            "running",
            "pending",
            "cancelled",
            "resumed",
            "suspended",
            "verified",
            "capture_confirmed",
            "manual_disabled",
            "message",
        )
        if key in result
    }
    compact.update({
        "completed_at": _session_timestamp(),
        "core_status": {
            key: core_status.get(key)
            for key in (
                "status",
                "hwnd",
                "input_width",
                "input_height",
                "output_width",
                "output_height",
                "error_code",
                "fullscreen_output_verified",
            )
            if key in core_status
        },
        "target": {
            key: target.get(key)
            for key in ("hwnd", "pid", "process", "title", "matched_filter")
            if key in target
        },
    })
    return compact


def _schedule_lossless_autostart(
    settings,
    app_id,
    title,
    filters,
    record_event=None,
):
    app_id = int(app_id or 0)
    if (
        _LS_RUNTIME.get("active")
        and int(_LS_RUNTIME.get("active_app_id", 0) or 0) == app_id
    ):
        return {
            "ok": True,
            "active": True,
            "running": True,
            "pending": False,
            "runtime": copy.deepcopy(_LS_RUNTIME),
        }
    if (
        _LS_AUTOSTART_GATE.get("worker_running")
        and int(_LS_AUTOSTART_GATE.get("app_id", 0) or 0) == app_id
    ):
        return {
            "ok": True,
            "active": False,
            "running": bool(_LS_CORE.get("initialized")),
            "pending": True,
            "message": "Waiting for the foreground game window.",
            "autostart": copy.deepcopy(_LS_AUTOSTART_GATE),
        }

    token = f"{app_id}:{time.time_ns()}"
    normalized = _normalize_lossless_profile(settings or {})
    profile_filters = list(filters or [])
    _LS_AUTOSTART_GATE.update({
        "token": token,
        "app_id": app_id,
        "title": str(title or ""),
        "worker_running": True,
        "started_at": _session_timestamp(),
        "last_result": {},
    })
    _LS_RUNTIME.update({
        "active": False,
        "active_app_id": app_id,
        "active_title": str(title or ""),
        "verified_capture": False,
        "last_action": "native_core_waiting_for_foreground_game",
        "last_error": "",
        "activation_focus": {
            "mode": "lossless_native_core_autostart",
            "filters": profile_filters,
            "token": token,
        },
        "last_transition_at": _session_timestamp(),
    })

    def worker():
        readiness = _wait_for_lossless_autostart_target(
            profile_filters,
            token=token,
            app_id=app_id,
            timeout_seconds=_LOSSLESS_AUTOSTART_FOREGROUND_TIMEOUT_SECONDS,
        )
        if str(_LS_AUTOSTART_GATE.get("token", "")) != token:
            return
        if readiness.get("ok"):
            result = _activate_lossless_managed_sync(
                normalized,
                app_id,
                str(title or ""),
                force_restart=False,
                auto_scale=True,
                filters=profile_filters,
            )
            if result.get("runtime", {}).get("activation_focus"):
                result["runtime"]["activation_focus"]["readiness"] = readiness
                _LS_RUNTIME["activation_focus"]["readiness"] = readiness
        else:
            result = {
                "ok": bool(readiness.get("cancelled")),
                "active": False,
                "running": bool(_LS_CORE.get("initialized")),
                "pending": not bool(readiness.get("cancelled")),
                "cancelled": bool(readiness.get("cancelled")),
                "message": (
                    ""
                    if readiness.get("cancelled")
                    else "Waiting for the foreground game window timed out."
                ),
                "readiness": readiness,
            }
            if not readiness.get("cancelled"):
                _LS_RUNTIME.update({
                    "last_action": "native_core_foreground_wait_timeout",
                    "last_error": "",
                    "last_transition_at": _session_timestamp(),
                })
        if str(_LS_AUTOSTART_GATE.get("token", "")) != token:
            return
        compact_result = _compact_lossless_result(result)
        _LS_AUTOSTART_GATE.update({
            "worker_running": False,
            "last_result": compact_result,
        })
        if callable(record_event):
            try:
                record_event(
                    "lossless_autostart_completed",
                    app_id=app_id,
                    title=str(title or ""),
                    readiness=readiness,
                    result=compact_result,
                )
            except Exception:
                pass

    threading.Thread(
        target=worker,
        daemon=True,
        name="QuickSettingsLosslessAutostartGate",
    ).start()
    return {
        "ok": True,
        "active": False,
        "running": bool(_LS_CORE.get("initialized")),
        "pending": True,
        "message": "Waiting for the foreground game window.",
        "autostart": copy.deepcopy(_LS_AUTOSTART_GATE),
    }


def _wait_for_scaling_target(timeout_seconds=12.0):
    deadline = time.time() + max(0.0, float(timeout_seconds))
    observations = []
    stable_hwnd = 0
    stable_since = 0.0
    while time.time() < deadline:
        snapshot = _foreground_window_snapshot()
        if not observations or (
            observations[-1].get("hwnd") != snapshot.get("hwnd")
            or observations[-1].get("process") != snapshot.get("process")
        ):
            observations.append(snapshot)
            observations = observations[-8:]
        if _is_scaling_target_window(snapshot):
            hwnd = int(snapshot.get("hwnd", 0) or 0)
            if hwnd != stable_hwnd:
                stable_hwnd = hwnd
                stable_since = time.time()
            elif time.time() - stable_since >= 0.48:
                return {
                    "ok": True,
                    "target": snapshot,
                    "stable_ms": int((time.time() - stable_since) * 1000),
                    "observations": observations,
                }
        else:
            stable_hwnd = 0
            stable_since = 0.0
        time.sleep(0.12)
    return {
        "ok": False,
        "target": _foreground_window_snapshot(),
        "observations": observations,
    }


def _restore_foreground_window(hwnd):
    if os.name != "nt" or not hwnd:
        return False
    try:
        user32 = ctypes.windll.user32
        kernel32 = ctypes.windll.kernel32
        hwnd = int(hwnd)
        if not user32.IsWindow(hwnd):
            return False
        user32.GetForegroundWindow.restype = wintypes.HWND
        target_pid = wintypes.DWORD()
        target_thread = int(user32.GetWindowThreadProcessId(hwnd, ctypes.byref(target_pid)) or 0)
        current_thread = int(kernel32.GetCurrentThreadId() or 0)
        attached = False
        if target_thread and current_thread and target_thread != current_thread:
            attached = bool(user32.AttachThreadInput(current_thread, target_thread, True))
        if user32.IsIconic(hwnd):
            user32.ShowWindow(hwnd, 9)
        try:
            user32.ShowWindow(hwnd, 5)
            user32.BringWindowToTop(hwnd)
            user32.SetForegroundWindow(hwnd)
            user32.SetFocus(hwnd)
            return int(user32.GetForegroundWindow() or 0) == hwnd
        finally:
            if attached:
                user32.AttachThreadInput(current_thread, target_thread, False)
    except Exception:
        return False


def _process_is_alive(pid):
    if os.name != "nt" or not pid:
        return False
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    kernel32 = ctypes.windll.kernel32
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel32.GetExitCodeProcess.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, int(pid))
    if not handle:
        return False
    try:
        exit_code = wintypes.DWORD()
        return bool(kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code))) and exit_code.value == 259
    finally:
        kernel32.CloseHandle(handle)


def _start_lossless_target_watchdog(target_pid, target_hwnd, app_id):
    token = f"{int(app_id or 0)}:{int(target_pid or 0)}:{int(target_hwnd or 0)}:{time.time_ns()}"
    _LS_RUNTIME["watchdog_token"] = token

    def worker():
        while True:
            time.sleep(0.2)
            if _LS_RUNTIME.get("watchdog_token") != token or not _LS_RUNTIME.get("active"):
                return
            if _process_is_alive(target_pid):
                continue
            _LS_RUNTIME["target_exit_detected_at"] = datetime.datetime.now(
                datetime.timezone.utc
            ).isoformat()
            _stop_lossless_managed_sync("target_process_exited")
            return

    threading.Thread(
        target=worker,
        name="QuickSettingsLosslessTargetWatchdog",
        daemon=True,
    ).start()


def _find_matching_scaling_window(filters, allow_fallback=False):
    if os.name != "nt":
        return {}
    import fnmatch

    patterns = [
        str(pattern or "").casefold()
        for pattern in filters if str(pattern or "").strip()
    ]
    if not patterns and not allow_fallback:
        return {}

    user32 = ctypes.windll.user32
    matches = []
    eligible = []

    def callback(hwnd, _lparam):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        process_path = _process_path_for_pid(int(pid.value))
        length = int(user32.GetWindowTextLengthW(hwnd) or 0)
        title_buffer = ctypes.create_unicode_buffer(max(1, length + 1))
        user32.GetWindowTextW(hwnd, title_buffer, len(title_buffer))
        snapshot = {
            "hwnd": int(hwnd),
            "pid": int(pid.value),
            "process": os.path.basename(process_path).lower(),
            "path": process_path,
            "title": title_buffer.value,
        }
        if not _is_scaling_target_window(snapshot):
            return True
        rect = wintypes.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(rect))
        snapshot["area"] = max(0, rect.right - rect.left) * max(0, rect.bottom - rect.top)
        candidates = [
            snapshot["path"].casefold(),
            snapshot["process"].casefold(),
            snapshot["title"].casefold(),
        ]
        matched_filter = next(
            (
                pattern
                for pattern in patterns
                if any(fnmatch.fnmatchcase(candidate, pattern) for candidate in candidates)
            ),
            "",
        )
        if matched_filter:
            match = dict(snapshot)
            match["matched_filter"] = matched_filter
            matches.append(match)
        # Always retain every eligible game window so the fallback below can pick
        # the real emulator / foreground render window when no title filter hits.
        eligible.append(snapshot)
        return True

    for _hwnd in _enum_top_level_windows():
        callback(_hwnd, 0)

    if matches:
        matches.sort(key=lambda item: int(item.get("area", 0) or 0), reverse=True)
        result = matches[0]
        result.pop("area", None)
        return result

    if not allow_fallback or not eligible:
        return {}

    # No Steam-title match. This only happens for emulated / non-Steam-shortcut
    # games, whose real window belongs to a known emulator process. Fall back
    # ONLY to a known emulator window - never to an arbitrary foreground window.
    # (An earlier "grab the foreground window" fallback once scaled Notepad
    # because the diagnostics JSON was open there instead of the game.)
    emulator_windows = [
        snapshot for snapshot in eligible
        if _is_emulator_process(snapshot.get("process"))
    ]
    if not emulator_windows:
        return {}

    foreground_hwnd = int(_foreground_window_snapshot().get("hwnd", 0) or 0)
    for snapshot in emulator_windows:
        if int(snapshot.get("hwnd", 0) or 0) == foreground_hwnd:
            result = dict(snapshot)
            result.pop("area", None)
            result["matched_filter"] = "<emulator-foreground>"
            return result

    emulator_windows.sort(key=lambda item: int(item.get("area", 0) or 0), reverse=True)
    result = dict(emulator_windows[0])
    result.pop("area", None)
    result["matched_filter"] = "<emulator>"
    return result

    return {}


def _start_lossless_autoscale_watchdog(app_id, filters):
    import fnmatch
    token = f"native:{int(app_id or 0)}:{time.time_ns()}"
    _LS_RUNTIME["watchdog_token"] = token

    def matches(snapshot):
        candidates = [
            str(snapshot.get("path", "") or ""),
            str(snapshot.get("process", "") or ""),
            str(snapshot.get("title", "") or ""),
        ]
        for pattern in filters if isinstance(filters, list) else []:
            lowered_pattern = str(pattern or "").casefold()
            if any(fnmatch.fnmatchcase(candidate.casefold(), lowered_pattern) for candidate in candidates):
                return pattern
        return ""

    def worker():
        deadline = time.time() + 45.0
        stable_hwnd = 0
        stable_since = 0.0
        observations = []
        while time.time() < deadline:
            time.sleep(0.12)
            if (
                _LS_RUNTIME.get("watchdog_token") != token
                or not _LS_RUNTIME.get("active")
                or int(_LS_RUNTIME.get("active_app_id", 0) or 0) != int(app_id or 0)
            ):
                return
            snapshot = _foreground_window_snapshot()
            if not _is_scaling_target_window(snapshot) or not matches(snapshot):
                enumerated = _detect_game_window(filters)
                if enumerated:
                    snapshot = enumerated
            if not observations or (
                observations[-1].get("hwnd") != snapshot.get("hwnd")
                or observations[-1].get("process") != snapshot.get("process")
            ):
                observations.append(snapshot)
                observations = observations[-10:]
            if not _is_scaling_target_window(snapshot):
                stable_hwnd = 0
                stable_since = 0.0
                continue
            hwnd = int(snapshot.get("hwnd", 0) or 0)
            if hwnd != stable_hwnd:
                stable_hwnd = hwnd
                stable_since = time.time()
                continue
            if time.time() - stable_since < 0.48:
                continue
            # Accept either a real title-filter match or the emulator/foreground
            # fallback tag carried by the enumerated snapshot, so emulated games
            # (whose window never carries the Steam title) still lock on.
            matched_filter = matches(snapshot) or str(snapshot.get("matched_filter", "") or "")
            if not matched_filter:
                stable_hwnd = 0
                stable_since = 0.0
                continue
            matched_filter = str(snapshot.get("matched_filter", "") or matched_filter)
            _LS_RUNTIME["activation_focus"] = {
                "mode": "lossless_native_profile",
                "filters": list(filters or []),
                "matched_filter": matched_filter,
                "target": snapshot,
                "stable_ms": int((time.time() - stable_since) * 1000),
                "observations": observations,
            }
            _LS_RUNTIME["last_action"] = "native_autoscale_target_matched"
            # Monitor the game process. A single failed liveness probe (a transient
            # OpenProcess failure, the emulator briefly recreating its render
            # window, or heavy load) must NOT tear the whole session down - that is
            # what produced the relaunch loop where Lossless restarted every ~40 s
            # and never stayed on the game. Require several consecutive dead
            # readings AND confirm no eligible game/emulator window is still on
            # screen before concluding the game really exited.
            target_pid = int(snapshot.get("pid", 0) or 0)
            dead_readings = 0
            while _LS_RUNTIME.get("watchdog_token") == token and _LS_RUNTIME.get("active"):
                if _process_is_alive(target_pid):
                    dead_readings = 0
                    time.sleep(0.3)
                    continue
                if _detect_game_window(filters):
                    dead_readings = 0
                    time.sleep(0.3)
                    continue
                dead_readings += 1
                if dead_readings >= 5:  # ~1.5 s of sustained absence
                    break
                time.sleep(0.3)
            if _LS_RUNTIME.get("watchdog_token") == token and _LS_RUNTIME.get("active"):
                _stop_lossless_managed_sync("target_process_exited")
            return

        if _LS_RUNTIME.get("watchdog_token") == token and _LS_RUNTIME.get("active"):
            _LS_RUNTIME["last_action"] = "native_autoscale_target_timeout"
            _LS_RUNTIME["last_error"] = "No eligible game window appeared within 45 seconds."
            _LS_RUNTIME["activation_focus"] = {
                "mode": "lossless_native_profile",
                "filters": list(filters or []),
                "target": None,
                "observations": observations,
            }

    threading.Thread(
        target=worker,
        name="QuickSettingsLosslessNativeAutoscale",
        daemon=True,
    ).start()


def _steam_path():
    if os.name != "nt":
        return ""
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as key:
            return str(winreg.QueryValueEx(key, "SteamPath")[0])
    except Exception:
        return ""


def _steam_library_dirs():
    import re
    dirs = []
    base = _steam_path()
    if base:
        dirs.append(base.replace("/", "\\"))
    vdf = os.path.join(base, "steamapps", "libraryfolders.vdf") if base else ""
    if vdf and os.path.exists(vdf):
        try:
            text = open(vdf, encoding="utf-8", errors="ignore").read()
            for match in re.findall(r'"path"\s*"([^"]+)"', text):
                dirs.append(match.replace("\\\\", "\\"))
        except Exception:
            pass
    return dirs


def _find_lossless():
    now = time.time()
    cached = str(_LOSSLESS_PATH_CACHE.get("value", "") or "")
    if now - float(_LOSSLESS_PATH_CACHE.get("checked_at", 0.0) or 0.0) < 300:
        return cached if cached and os.path.exists(cached) else ""
    for directory in _steam_library_dirs():
        candidate = os.path.join(directory, "steamapps", "common", "Lossless Scaling", "LosslessScaling.exe")
        if os.path.exists(candidate):
            _LOSSLESS_PATH_CACHE.update({"value": candidate, "checked_at": now})
            return candidate
    _LOSSLESS_PATH_CACHE.update({"value": "", "checked_at": now})
    return ""


def _lossless_core_status_callback(
    status, hwnd, input_width, input_height, output_width, output_height,
    resized, scale_factor, error_code,
):
    target_monitor = copy.deepcopy(_LS_CORE.get("target_monitor", {}))
    payload = {
        "status": int(status),
        "hwnd": int(hwnd or 0),
        "input_width": int(input_width),
        "input_height": int(input_height),
        "output_width": int(output_width),
        "output_height": int(output_height),
        "resized": bool(resized),
        "scale_factor": float(scale_factor),
        "error_code": int(error_code),
        "target_monitor": target_monitor,
        "reported_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    }
    if int(status) == 2:
        payload["fullscreen_output_verified"] = _lossless_output_matches_monitor(
            payload, target_monitor
        )
    _LS_CORE["last_status"] = payload
    _LS_RUNTIME["core_status"] = copy.deepcopy(payload)
    if int(error_code):
        _LS_RUNTIME["active"] = False
        _LS_RUNTIME["last_error"] = f"Lossless core error {int(error_code)}."
        _LS_SCALING_ACTIVE["on"] = False
    elif int(status) == 0:
        _LS_RUNTIME["active"] = False
        _LS_SCALING_ACTIVE["on"] = False
    elif int(status) == 2 and not payload.get("fullscreen_output_verified", False):
        was_active = bool(_LS_RUNTIME.get("active"))
        _LS_RUNTIME["active"] = False
        _LS_RUNTIME["verified_capture"] = False
        _LS_RUNTIME["last_error"] = (
            "Lossless Scaling output does not match the target monitor's "
            "physical pixel dimensions."
        )
        _LS_SCALING_ACTIVE["on"] = False
        if was_active:
            threading.Thread(
                target=lambda: _stop_lossless_managed_sync(
                    "invalid_output_dimensions"
                ),
                daemon=True,
                name="QuickSettingsLosslessOutputGuard",
            ).start()
    elif int(status) in (1, 2):
        _LS_RUNTIME["active"] = True
        _LS_RUNTIME["verified_capture"] = int(status) == 2
        _LS_RUNTIME["last_error"] = ""
        _LS_SCALING_ACTIVE["on"] = True
        if (
            int(status) == 2
            and _STEAM_OVERLAY_STATE.get("active")
            and not _LS_SESSION_CONTROL.get("overlay_suspend_in_progress")
        ):
            _LS_SESSION_CONTROL["overlay_suspend_in_progress"] = True
            threading.Thread(
                target=_suspend_lossless_for_overlay_sync,
                daemon=True,
                name="QuickSettingsLosslessOverlaySuspend",
            ).start()
    _LS_CORE_EVENT.set()


def _load_lossless_core():
    if os.name != "nt":
        return {"ok": False, "message": "Windows only."}
    exe_path = _find_lossless()
    if not exe_path:
        return {"ok": False, "message": "Lossless Scaling is not installed."}
    dll_path = os.path.join(os.path.dirname(exe_path), "Lossless.dll")
    if not os.path.exists(dll_path):
        return {"ok": False, "message": "Lossless.dll was not found."}
    if _LS_CORE.get("dll") is not None and _LS_CORE.get("dll_path") == dll_path:
        return {"ok": True, "dll": _LS_CORE["dll"], "path": dll_path}
    try:
        dll_directory = os.add_dll_directory(os.path.dirname(dll_path))
        with _lossless_dpi_scope():
            dll = ctypes.CDLL(dll_path)
        callback_type = ctypes.CFUNCTYPE(
            None,
            ctypes.c_int, ctypes.c_void_p,
            ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
            ctypes.c_bool, ctypes.c_float, ctypes.c_int,
        )
        callback = callback_type(_lossless_core_status_callback)
        dll.Init.argtypes = [callback_type]
        dll.Init.restype = ctypes.c_bool
        dll.UnInit.argtypes = []
        dll.UnInit.restype = None
        dll.GetForegroundWindowEx.argtypes = []
        dll.GetForegroundWindowEx.restype = ctypes.c_void_p
        dll.Activate.argtypes = [ctypes.c_void_p]
        dll.Activate.restype = ctypes.c_bool
        dll.ApplySettings.argtypes = [
            ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
            ctypes.c_float, ctypes.c_bool, ctypes.c_int, ctypes.c_bool,
            ctypes.c_int, ctypes.c_int, ctypes.c_int,
            ctypes.c_float, ctypes.c_float, ctypes.c_int,
            ctypes.c_bool, ctypes.c_bool, ctypes.c_bool, ctypes.c_bool,
            ctypes.c_int, ctypes.c_int, ctypes.c_bool, ctypes.c_bool,
            ctypes.c_int, ctypes.c_int, ctypes.c_bool,
            ctypes.c_int, ctypes.c_int,
            ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
            ctypes.c_bool, ctypes.c_int,
        ]
        dll.ApplySettings.restype = None
        _LS_CORE.update({
            "dll": dll,
            "dll_path": dll_path,
            "dll_directory": dll_directory,
            "callback": callback,
            "initialized": False,
            "last_status": {},
            "target_monitor": {},
        })
        return {"ok": True, "dll": dll, "path": dll_path}
    except Exception as error:
        return {
            "ok": False,
            "message": f"{type(error).__name__}: {error}",
            "path": dll_path,
        }


def _initialize_lossless_core():
    loaded = _load_lossless_core()
    if not loaded.get("ok"):
        return loaded
    if _LS_CORE.get("initialized"):
        return {"ok": True, "already_initialized": True}
    try:
        _LS_CORE_EVENT.clear()
        _LS_CORE["last_status"] = {}
        with _lossless_dpi_scope():
            initialized = bool(loaded["dll"].Init(_LS_CORE["callback"]))
        _LS_CORE["initialized"] = initialized
        return {
            "ok": initialized,
            "message": "" if initialized else "Lossless core initialization failed.",
        }
    except Exception as error:
        _LS_CORE["initialized"] = False
        return {"ok": False, "message": f"{type(error).__name__}: {error}"}


def _lossless_setup_phase():
    import re
    _, text = _ls_read_text()
    match = re.search(r"<SetupPhase>(-?\d+)</SetupPhase>", text or "")
    return int(match.group(1)) if match else 0


def _apply_lossless_core_settings(settings):
    normalized = _normalize_lossless_profile(settings)
    dll = _LS_CORE.get("dll")
    if dll is None or not _LS_CORE.get("initialized"):
        return {"ok": False, "message": "Lossless core is not initialized."}
    scaling_type = {
        "Off": 0, "LS1": 1, "FSR": 2, "NIS": 3, "SGSR": 4,
        "BCAS": 5, "BicubicCAS": 5, "Anime4K": 6, "xBR": 7,
        "XBR": 7, "SharpBilinear": 8, "Integer": 9,
        "NearestNeighbor": 10, "Nearest": 10,
    }.get(normalized["scaling_type"], 0)
    scaling_subtype = 0
    if scaling_type == 2:
        scaling_subtype = {"ORIGINAL": 0, "OPTIMIZED": 1}.get(normalized["fsr_type"], 0)
    elif scaling_type == 1:
        scaling_subtype = {"BALANCED": 0, "PERFORMANCE": 1}.get(normalized["ls1_type"], 0)
    elif scaling_type == 6:
        scaling_subtype = {"S": 0, "M": 1, "L": 2, "VL": 3, "UL": 4}.get(
            normalized["anime4k_type"], 0
        )
    frame_generation = {
        "Off": 0, "LSFG3": 1, "LSFG2": 2, "LSFG1": 3,
    }.get(normalized["frame_generation"], 0)
    frame_mode = (
        {"FIXED": 0, "ADAPTIVE": 1}.get(normalized["lsfg3_mode"], 0)
        if frame_generation == 1
        else {"X2": 0, "X3": 1, "X4": 2}.get(normalized["lsfg2_mode"], 0)
    )
    sharpness = (
        normalized["ls1_sharpness"] if scaling_type == 1 else normalized["sharpness"]
    )
    try:
        with _lossless_dpi_scope():
            dll.ApplySettings(
                {"Auto": 0, "Custom": 1}.get(normalized["scaling_mode"], 0),
                {"AspectRatio": 0, "Fullscreen": 1}.get(normalized["scaling_fit_mode"], 0),
                scaling_type,
                scaling_subtype,
                ctypes.c_float(normalized["scale_factor"]),
                normalized["resize_before_scaling"],
                sharpness,
                normalized["vrs"],
                frame_generation,
                {"BALANCED": 0, "PERFORMANCE": 1}.get(normalized["lsfg_size"], 0),
                frame_mode,
                ctypes.c_float(normalized["lsfg3_multiplier"]),
                ctypes.c_float(normalized["lsfg3_target"]),
                normalized["lsfg_flow_scale"],
                normalized["clip_cursor"],
                normalized["adjust_cursor_speed"],
                normalized["hide_cursor"],
                normalized["scale_cursor"],
                {
                    "OFF": 0, "DEFAULT": 1, "VSYNC1": 2, "VSYNC2": 3,
                    "VSYNC3": 4, "VSYNC4": 5,
                }.get(normalized["sync_mode"], 1),
                normalized["max_frame_latency"],
                normalized["gsync_support"],
                normalized["hdr_support"],
                {"DXGI": 0, "WGC": 1, "GDI": 2}.get(normalized["capture_api"], 0),
                normalized["queue_target"],
                normalized["draw_fps"],
                normalized["preferred_gpu_id"],
                normalized["output_display_id"],
                normalized["crop_input_left"],
                normalized["crop_input_top"],
                normalized["crop_input_right"],
                normalized["crop_input_bottom"],
                normalized["multi_display_mode"],
                _lossless_setup_phase(),
            )
        return {"ok": True, "settings": normalized}
    except Exception as error:
        return {"ok": False, "message": f"{type(error).__name__}: {error}"}


def _lossless_core_target(filters=None):
    # Prefer the actual Steam-launched game (or active emulator) even while the
    # QAM, Decky, or another desktop window owns foreground focus.
    detected = _detect_game_window(list(filters or []))
    if detected:
        return detected
    return {}


class _PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wintypes.DWORD), ("cntUsage", wintypes.DWORD),
        ("th32ProcessID", wintypes.DWORD), ("th32DefaultHeapID", ctypes.c_size_t),
        ("th32ModuleID", wintypes.DWORD), ("cntThreads", wintypes.DWORD),
        ("th32ParentProcessID", wintypes.DWORD), ("pcPriClassBase", ctypes.c_long),
        ("dwFlags", wintypes.DWORD), ("szExeFile", wintypes.WCHAR * 260),
    ]


def _process_ids_by_name(image_name):
    if os.name != "nt":
        return []
    kernel32 = ctypes.windll.kernel32
    snapshot = kernel32.CreateToolhelp32Snapshot(0x00000002, 0)
    if snapshot in (0, ctypes.c_void_p(-1).value):
        return []
    entry = _PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(entry)
    found = []
    try:
        ok = kernel32.Process32FirstW(snapshot, ctypes.byref(entry))
        while ok:
            if str(entry.szExeFile or "").lower() == str(image_name or "").lower():
                found.append(int(entry.th32ProcessID))
            ok = kernel32.Process32NextW(snapshot, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snapshot)
    return found


def _diagnostic_process_snapshot():
    names = [
        "QuickSettingsAgent.exe",
        "LosslessScaling.exe",
        "steam.exe",
        "steamwebhelper.exe",
        "PluginLoader.exe",
        "PluginLoader_noconsole.exe",
        "GamingMode.exe",
        "RadeonSoftware.exe",
        "Winhanced.exe",
        "WinhancedWatchdog.exe",
        "WHService.exe",
    ]
    return {
        name: {"pids": _process_ids_by_name(name)}
        for name in names
    }


def _lossless_running():
    return bool(_process_ids_by_name("LosslessScaling.exe"))


def _hide_lossless_windows(pid):
    if os.name != "nt" or not pid:
        return
    user32 = ctypes.windll.user32

    def callback(hwnd, _lparam):
        window_pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(window_pid))
        if int(window_pid.value) == int(pid):
            user32.ShowWindow(hwnd, 0)
        return True

    for _hwnd in _enum_top_level_windows():
        callback(_hwnd, 0)


def _lossless_output_windows():
    if os.name != "nt":
        return []
    user32 = ctypes.windll.user32
    windows = []

    def callback(hwnd, _lparam):
        window_pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(window_pid))
        if int(window_pid.value) != os.getpid():
            return True
        class_buffer = ctypes.create_unicode_buffer(128)
        user32.GetClassNameW(hwnd, class_buffer, len(class_buffer))
        if class_buffer.value != "LosslessScaling":
            return True
        windows.append({
            "hwnd": int(hwnd),
            "visible": bool(user32.IsWindowVisible(hwnd)),
            "class": class_buffer.value,
        })
        return True

    try:
        for _hwnd in _enum_top_level_windows():
            callback(_hwnd, 0)
    except Exception:
        return []
    return windows


def _set_lossless_output_visible(visible):
    windows = _lossless_output_windows()
    if os.name != "nt":
        return windows
    user32 = ctypes.windll.user32
    for window in windows:
        hwnd = wintypes.HWND(int(window["hwnd"]))
        if visible:
            # Show without stealing focus from the game. The native window
            # keeps its own topmost style and resumes presenting immediately.
            user32.ShowWindow(hwnd, 8)
        else:
            user32.ShowWindow(hwnd, 0)
        window["visible_after"] = bool(user32.IsWindowVisible(hwnd))
    return windows


def _enforce_steam_overlay_visibility():
    def worker():
        for _ in range(30):
            if not _STEAM_OVERLAY_STATE.get("active"):
                return
            _STEAM_OVERLAY_STATE["output_windows"] = (
                _set_lossless_output_visible(False)
            )
            time.sleep(0.1)

    threading.Thread(
        target=worker,
        daemon=True,
        name="QuickSettingsSteamOverlayGuard",
    ).start()


def _session_timestamp():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def _reset_lossless_session_control(clear_manual=False, clear_overlay=True):
    if clear_manual:
        _LS_SESSION_CONTROL.update({
            "manual_disabled_app_id": 0,
            "manual_disabled_title": "",
        })
    if clear_overlay:
        _LS_SESSION_CONTROL.update({
            "overlay_suspended": False,
            "overlay_suspend_in_progress": False,
            "resume_requested": False,
            "resume_app_id": 0,
            "resume_title": "",
            "resume_settings": {},
            "resume_filters": [],
        })
    _LS_SESSION_CONTROL["last_changed_at"] = _session_timestamp()


def _clear_lossless_manual_override(app_id=0):
    disabled_app_id = int(
        _LS_SESSION_CONTROL.get("manual_disabled_app_id", 0) or 0
    )
    if not app_id or disabled_app_id == int(app_id or 0):
        _LS_SESSION_CONTROL.update({
            "manual_disabled_app_id": 0,
            "manual_disabled_title": "",
            "last_changed_at": _session_timestamp(),
        })


def _queue_lossless_overlay_resume(settings, app_id, title, filters):
    _LS_SESSION_CONTROL.update({
        "resume_requested": True,
        "resume_app_id": int(app_id or 0),
        "resume_title": str(title or ""),
        "resume_settings": _normalize_lossless_profile(settings or {}),
        "resume_filters": list(filters or []),
        "last_changed_at": _session_timestamp(),
    })


def _suspend_lossless_for_overlay_sync():
    with _LS_CONTROL_LOCK:
        try:
            if _LS_SESSION_CONTROL.get("overlay_suspended"):
                return {
                    "ok": True,
                    "active": False,
                    "suspended": True,
                    "message": "",
                }
            if not _LS_RUNTIME.get("active"):
                return {
                    "ok": True,
                    "active": False,
                    "suspended": False,
                    "message": "",
                }
            settings = copy.deepcopy(
                _LS_RUNTIME.get("active_settings")
                or _read_lossless_settings().get("settings", {})
            )
            app_id = int(_LS_RUNTIME.get("active_app_id", 0) or 0)
            title = str(_LS_RUNTIME.get("active_title", "") or "")
            filters = list(
                _LS_RUNTIME.get("activation_focus", {}).get("filters", []) or []
            )
            _queue_lossless_overlay_resume(settings, app_id, title, filters)
            result = _deactivate_lossless_capture_sync(
                "steam_overlay", preserve_runtime=True
            )
            suspended = bool(result.get("ok"))
            _LS_SESSION_CONTROL.update({
                "overlay_suspended": suspended,
                "last_suspend_result": _compact_lossless_result(result),
                "last_changed_at": _session_timestamp(),
            })
            return {
                **result,
                "suspended": suspended,
                "session_control": copy.deepcopy(_LS_SESSION_CONTROL),
            }
        finally:
            _LS_SESSION_CONTROL["overlay_suspend_in_progress"] = False


def _resume_lossless_after_overlay_sync():
    with _LS_CONTROL_LOCK:
        requested = bool(_LS_SESSION_CONTROL.get("resume_requested"))
        app_id = int(_LS_SESSION_CONTROL.get("resume_app_id", 0) or 0)
        title = str(_LS_SESSION_CONTROL.get("resume_title", "") or "")
        settings = copy.deepcopy(_LS_SESSION_CONTROL.get("resume_settings", {}))
        filters = list(_LS_SESSION_CONTROL.get("resume_filters", []) or [])
        manually_disabled = (
            app_id
            and int(
                _LS_SESSION_CONTROL.get("manual_disabled_app_id", 0) or 0
            ) == app_id
        )
        _LS_SESSION_CONTROL.update({
            "overlay_suspended": False,
            "overlay_suspend_in_progress": False,
            "resume_requested": False,
            "resume_app_id": 0,
            "resume_title": "",
            "resume_settings": {},
            "resume_filters": [],
            "last_changed_at": _session_timestamp(),
        })
        if not requested or manually_disabled:
            result = {
                "ok": True,
                "active": False,
                "resumed": False,
                "manual_disabled": manually_disabled,
                "message": "",
            }
        else:
            result = _activate_lossless_managed_sync(
                settings,
                app_id,
                title,
                force_restart=False,
                auto_scale=True,
                filters=filters,
            )
            result["resumed"] = bool(result.get("ok") and result.get("active"))
        _LS_SESSION_CONTROL["last_resume_result"] = (
            _compact_lossless_result(result)
        )
        return result


def _set_steam_overlay_active_sync(
    active, overlay_pid=0, app_id=0, user_initiated=False
):
    was_active = bool(_STEAM_OVERLAY_STATE.get("active"))
    _STEAM_OVERLAY_STATE.update({
        "active": bool(active),
        "overlay_pid": int(overlay_pid or 0),
        "app_id": int(app_id or 0),
        "user_initiated": bool(user_initiated),
        "updated_at": _session_timestamp(),
    })
    if active:
        suspend_result = (
            _suspend_lossless_for_overlay_sync()
            if not was_active
            else copy.deepcopy(_LS_SESSION_CONTROL.get("last_suspend_result", {}))
        )
        windows = _lossless_output_windows()
        resume_result = {}
    else:
        resume_result = (
            _resume_lossless_after_overlay_sync()
            if was_active or _LS_SESSION_CONTROL.get("resume_requested")
            else {}
        )
        suspend_result = {}
        windows = _lossless_output_windows()
    _STEAM_OVERLAY_STATE["output_windows"] = windows
    return {
        "active": bool(active),
        "overlay_pid": int(overlay_pid or 0),
        "app_id": int(app_id or 0),
        "user_initiated": bool(user_initiated),
        "lossless_active": bool(
            _LS_RUNTIME.get("active")
            or _LS_SESSION_CONTROL.get("resume_requested")
        ),
        "output_windows": windows,
        "suspend_result": suspend_result,
        "resume_result": resume_result,
        "session_control": copy.deepcopy(_LS_SESSION_CONTROL),
    }


def _keep_lossless_hidden(pid, previous_foreground=0):
    def worker():
        user32 = ctypes.windll.user32
        for _ in range(40):
            # Stop as soon as scaling is armed: after that point Lossless creates
            # its scaling overlay (owned by the same pid), and hiding it would make
            # the game look un-scaled. Only the pre-activation control window is
            # hidden.
            if not _LS_HIDE_ACTIVE.get("on"):
                break
            if pid not in _process_ids_by_name("LosslessScaling.exe"):
                break
            _hide_lossless_windows(pid)
            current = int(user32.GetForegroundWindow() or 0)
            current_pid = wintypes.DWORD()
            if current:
                user32.GetWindowThreadProcessId(current, ctypes.byref(current_pid))
            if int(current_pid.value) == int(pid) and previous_foreground and user32.IsWindow(previous_foreground):
                user32.SetForegroundWindow(previous_foreground)
            time.sleep(0.1)
    threading.Thread(target=worker, name="QuickSettingsLosslessHide", daemon=True).start()


def _start_lossless_hidden(path):
    _LS_HIDE_ACTIVE["on"] = True
    previous_foreground = int(ctypes.windll.user32.GetForegroundWindow() or 0) if os.name == "nt" else 0
    startupinfo = None
    creation_flags = _NO_WINDOW
    if os.name == "nt":
        startupinfo = subprocess.STARTUPINFO()
        startupinfo.dwFlags |= subprocess.STARTF_USESHOWWINDOW
        startupinfo.wShowWindow = 0
        creation_flags |= getattr(subprocess, "DETACHED_PROCESS", 0x00000008)
    process = subprocess.Popen(
        [path, "-StartMinimized"], cwd=os.path.dirname(path), stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        startupinfo=startupinfo, creationflags=creation_flags, close_fds=True,
    )
    _keep_lossless_hidden(process.pid, previous_foreground)
    return process


def _lossless_settings_path():
    base = os.environ.get("LOCALAPPDATA", "")
    if not base:
        return ""
    return os.path.join(base, "Lossless Scaling", "Settings.xml")


def _ls_read_text():
    path = _lossless_settings_path()
    if not path or not os.path.exists(path):
        return path, ""
    try:
        return path, open(path, encoding="utf-8-sig", errors="ignore").read()
    except Exception:
        return path, ""


def _ls_default_block_span(text):
    # The settings file has one <Profile> per app plus a "Default" profile we
    # edit. Parsed by hand (the Decky Python has no xml module).
    import re
    for m in re.finditer(r"<Profile\b[^>]*>.*?</Profile>", text, re.DOTALL):
        if re.search(r"<Title>\s*Default\s*</Title>", m.group(0)):
            return m.start(), m.end()
    m = re.search(r"<Profile\b[^>]*>.*?</Profile>", text, re.DOTALL)
    return (m.start(), m.end()) if m else None


def _ls_get(block, name, default=""):
    import re
    m = re.search(r"<%s>(.*?)</%s>" % (name, name), block, re.DOTALL)
    return m.group(1).strip() if m else default


def _coerce_ls_bool(value):
    if isinstance(value, bool):
        return value
    return str(value).strip().lower() in ("1", "true", "yes", "on")


def _coerce_ls_int(value, default, minimum, maximum):
    try:
        number = int(round(float(value)))
    except Exception:
        number = int(default)
    return max(minimum, min(maximum, number))


def _coerce_ls_float(value, default, minimum, maximum):
    try:
        number = float(value)
    except Exception:
        number = float(default)
    return max(minimum, min(maximum, round(number, 2)))


def _normalize_choice(value, allowed, default):
    candidate = str(value or "").strip()
    lookup = {str(item).lower(): item for item in allowed}
    return lookup.get(candidate.lower(), default)


def _normalize_lossless_profile(settings):
    source = settings if isinstance(settings, dict) else {}
    result = dict(LS_PROFILE_DEFAULTS)
    result.update({key: source[key] for key in result if key in source})

    bool_keys = {
        "resize_before_scaling", "windowed_mode", "vrs", "clip_cursor",
        "adjust_cursor_speed", "hide_cursor", "scale_cursor", "gsync_support",
        "hdr_support", "draw_fps", "multi_display_mode", "crop_input",
    }
    for key in bool_keys:
        result[key] = _coerce_ls_bool(result[key])

    choices = {
        "scaling_mode": (["Auto", "Custom"], "Auto"),
        "scaling_fit_mode": (["AspectRatio", "Fullscreen"], "AspectRatio"),
        "scaling_type": (
            ["Off", "LS1", "FSR", "NIS", "SGSR", "BCAS", "Anime4K",
             "xBR", "SharpBilinear", "Integer", "NearestNeighbor"],
            "Off",
        ),
        "fsr_type": (["ORIGINAL", "OPTIMIZED"], "ORIGINAL"),
        "ls1_type": (["BALANCED", "PERFORMANCE"], "BALANCED"),
        "anime4k_type": (["S", "M", "L", "VL", "UL"], "S"),
        "frame_generation": (["Off", "LSFG1", "LSFG2", "LSFG3"], "Off"),
        "lsfg2_mode": (["X2", "X3", "X4"], "X2"),
        "lsfg3_mode": (["FIXED", "ADAPTIVE"], "FIXED"),
        "lsfg_size": (["PERFORMANCE", "BALANCED"], "BALANCED"),
        "sync_mode": (["OFF", "DEFAULT", "VSYNC1", "VSYNC2", "VSYNC3", "VSYNC4"], "DEFAULT"),
        "capture_api": (["DXGI", "WGC", "GDI"], "DXGI"),
    }
    for key, (allowed, default) in choices.items():
        result[key] = _normalize_choice(result[key], allowed, default)

    result["activation_delay_ms"] = _coerce_ls_int(result["activation_delay_ms"], 900, 250, 10000)
    result["scale_factor"] = _coerce_ls_float(result["scale_factor"], 1.5, 1.0, 5.0)
    result["sharpness"] = _coerce_ls_int(result["sharpness"], 5, 0, 10)
    result["ls1_sharpness"] = _coerce_ls_int(result["ls1_sharpness"], 1, 0, 10)
    result["lsfg3_multiplier"] = _coerce_ls_int(result["lsfg3_multiplier"], 2, 2, 4)
    result["lsfg3_target"] = _coerce_ls_int(result["lsfg3_target"], 120, 30, 360)
    result["lsfg_flow_scale"] = _coerce_ls_int(result["lsfg_flow_scale"], 100, 25, 100)
    result["max_frame_latency"] = _coerce_ls_int(result["max_frame_latency"], 3, 0, 4)
    result["queue_target"] = _coerce_ls_int(result["queue_target"], 1, 0, 4)
    result["preferred_gpu_id"] = _coerce_ls_int(result["preferred_gpu_id"], 0, 0, 16)
    result["output_display_id"] = _coerce_ls_int(result["output_display_id"], 0, 0, 16)
    for key in ("crop_input_left", "crop_input_top", "crop_input_right", "crop_input_bottom"):
        result[key] = _coerce_ls_int(result[key], 0, 0, 8192)
    return result


def _lossless_profile_filters(title, learned=None):
    import re
    filters = []
    for value in learned if isinstance(learned, list) else []:
        candidate = str(value or "").strip()
        if candidate and candidate not in filters:
            filters.append(candidate)

    words = re.findall(r"[A-Za-z0-9]+", str(title or ""))
    if words:
        title_filter = "*" + "*".join(words) + "*"
        filters.append(title_filter)
        first = words[0]
        if any(character.isdigit() for character in first):
            filters.append(f"*{first}*")
        initials = "".join(word[0] for word in words if word and word[0].isalpha())
        if len(initials) >= 2:
            filters.append(f"*{initials}*")
            suffix = "".join(word for word in words if word.isdigit() or any(c.isdigit() for c in word))
            if suffix:
                filters.append(f"*{initials}{suffix}*")

    unique = []
    for value in filters:
        if value and value.casefold() not in {item.casefold() for item in unique}:
            unique.append(value)
    return unique[:8]


def _xml_escape(value):
    return (
        str(value or "")
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
        .replace("'", "&apos;")
    )


def _parse_ls_value(raw, default):
    if isinstance(default, bool):
        return _coerce_ls_bool(raw)
    if isinstance(default, int) and not isinstance(default, bool):
        return _coerce_ls_int(raw, default, -2147483648, 2147483647)
    if isinstance(default, float):
        return _coerce_ls_float(raw, default, -1000000, 1000000)
    return str(raw if raw != "" else default)


def _read_lossless_settings():
    import re
    result = {
        "frame_gen": "Off",
        "multiplier": 2,
        "hotkey": "Ctrl+Alt+S",
        "scaling_active": bool(
            (
                _LS_RUNTIME["active"]
                or _LS_SESSION_CONTROL.get("resume_requested")
            )
            and (
                _LS_CORE.get("initialized") or _lossless_running()
            )
        ),
        "settings_found": False,
        "settings": dict(LS_PROFILE_DEFAULTS),
    }
    path, text = _ls_read_text()
    if not text:
        return result
    key_m = re.search(r"<Hotkey>(.*?)</Hotkey>", text, re.DOTALL)
    key = (key_m.group(1).strip() if key_m else "S")
    mod_m = re.search(r"<HotkeyModifierKeys>(.*?)</HotkeyModifierKeys>", text, re.DOTALL)
    mods = (mod_m.group(1).strip() if mod_m else "Alt Control")
    pretty = []
    for token in mods.split():
        low = token.lower()
        if low in ("control", "ctrl"):
            pretty.append("Ctrl")
        elif low in ("alt", "menu"):
            pretty.append("Alt")
        elif low == "shift":
            pretty.append("Shift")
    result["hotkey"] = "+".join(pretty + [key]) if key else "+".join(pretty)
    span = _ls_default_block_span(text)
    if span:
        block = text[span[0]:span[1]]
        values = {}
        for key, element in LS_SETTING_ELEMENTS.items():
            default = LS_PROFILE_DEFAULTS[key]
            values[key] = _parse_ls_value(_ls_get(block, element, str(default)), default)
        values["activation_delay_ms"] = LS_PROFILE_DEFAULTS["activation_delay_ms"]
        result["settings"] = _normalize_lossless_profile(values)
        result["frame_gen"] = result["settings"]["frame_generation"]
        result["multiplier"] = result["settings"]["lsfg3_multiplier"]
        result["settings_found"] = True
    return result


def _format_ls_value(value):
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, float):
        return ("%.2f" % value).rstrip("0").rstrip(".")
    return str(value)


def _write_lossless_profile_settings(settings):
    import re
    normalized = _normalize_lossless_profile(settings)
    path, text = _ls_read_text()
    if not text:
        return {"ok": False, "message": "Lossless Scaling Settings.xml was not found."}
    span = _ls_default_block_span(text)
    if not span:
        return {"ok": False, "message": "Lossless Scaling Default profile was not found."}
    block = text[span[0]:span[1]]
    for key, element in LS_SETTING_ELEMENTS.items():
        value = _format_ls_value(normalized[key])
        pattern = r"<%s>.*?</%s>" % (element, element)
        replacement = "<%s>%s</%s>" % (element, value, element)
        if re.search(pattern, block, re.DOTALL):
            block = re.sub(pattern, replacement, block, count=1, flags=re.DOTALL)
        else:
            block = block.replace("</Profile>", replacement + "</Profile>", 1)
    # Quick Settings owns activation, so LS's own title watcher stays disabled.
    auto_pattern = r"<AutoScale>.*?</AutoScale>"
    if re.search(auto_pattern, block, re.DOTALL):
        block = re.sub(auto_pattern, "<AutoScale>false</AutoScale>", block, count=1, flags=re.DOTALL)
    text = text[:span[0]] + block + text[span[1]:]
    temporary = f"{path}.{os.getpid()}.tmp"
    try:
        with open(temporary, "w", encoding="utf-8-sig") as handle:
            handle.write(text)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
        return {"ok": True, "settings": normalized}
    except Exception as error:
        try:
            if os.path.exists(temporary):
                os.remove(temporary)
        except Exception:
            pass
        return {"ok": False, "message": f"{type(error).__name__}: {error}"}


def _write_lossless_autoscale_profile(settings, app_id, title, filters):
    import re
    normalized = _normalize_lossless_profile(settings)
    path, text = _ls_read_text()
    if not text:
        return {"ok": False, "message": "Lossless Scaling Settings.xml was not found."}
    span = _ls_default_block_span(text)
    if not span:
        return {"ok": False, "message": "Lossless Scaling Default profile was not found."}

    managed_title = f"Playhub Quick Settings [{int(app_id or 0)}]"
    managed_pattern = (
        r"<Profile\b[^>]*>\s*<Title>\s*Playhub Quick Settings \[\d+\]\s*</Title>"
        r".*?</Profile>\s*"
    )
    text = re.sub(managed_pattern, "", text, flags=re.DOTALL)
    default_block = text[span[0]:span[1]]
    for key, element in LS_SETTING_ELEMENTS.items():
        value = _format_ls_value(normalized[key])
        pattern = r"<%s>.*?</%s>" % (element, element)
        replacement = "<%s>%s</%s>" % (element, _xml_escape(value), element)
        if re.search(pattern, default_block, re.DOTALL):
            default_block = re.sub(
                pattern,
                lambda _match, item=replacement: item,
                default_block,
                count=1,
                flags=re.DOTALL,
            )
        else:
            default_block = default_block.replace("</Profile>", replacement + "</Profile>", 1)
    text = text[:span[0]] + default_block + text[span[1]:]
    block = default_block

    profile_filters = _lossless_profile_filters(title, filters)
    replacements = {
        "Title": managed_title,
        "AutoScale": "true",
        "AutoScaleDelay": "0",
    }
    replacements.update({
        element: _format_ls_value(normalized[key])
        for key, element in LS_SETTING_ELEMENTS.items()
    })
    for element, value in replacements.items():
        pattern = r"<%s>.*?</%s>" % (element, element)
        replacement = "<%s>%s</%s>" % (element, _xml_escape(value), element)
        if re.search(pattern, block, re.DOTALL):
            block = re.sub(pattern, lambda _match, item=replacement: item, block, count=1, flags=re.DOTALL)
        else:
            block = block.replace("</Profile>", replacement + "</Profile>", 1)

    path_element = "<Path>%s</Path>" % _xml_escape(";".join(profile_filters))
    if re.search(r"<Path>.*?</Path>", block, re.DOTALL):
        block = re.sub(
            r"<Path>.*?</Path>",
            lambda _match: path_element,
            block,
            count=1,
            flags=re.DOTALL,
        )
    else:
        # XmlSerializer reads Profile properties in declaration order:
        # Title, Path, AutoScale. A Path appended at the end is well-formed XML
        # but Lossless Scaling rejects the whole settings file as corrupted.
        block = re.sub(
            r"(</Title>)",
            lambda match: match.group(1) + "\n      " + path_element,
            block,
            count=1,
        )

    closing = text.rfind("</GameProfiles>")
    if closing < 0:
        return {"ok": False, "message": "Lossless Scaling GameProfiles section was not found."}
    text = text[:closing] + "    " + block.strip() + "\n  " + text[closing:]
    temporary = f"{path}.{os.getpid()}.tmp"
    try:
        with open(temporary, "w", encoding="utf-8-sig") as handle:
            handle.write(text)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
        return {
            "ok": True,
            "settings": normalized,
            "profile_title": managed_title,
            "filters": profile_filters,
        }
    except Exception as error:
        try:
            if os.path.exists(temporary):
                os.remove(temporary)
        except Exception:
            pass
        return {"ok": False, "message": f"{type(error).__name__}: {error}"}


def _remove_lossless_autoscale_profiles():
    import re
    path, text = _ls_read_text()
    if not text:
        return {"ok": True, "removed": 0}
    pattern = (
        r"<Profile\b[^>]*>\s*<Title>\s*Playhub Quick Settings \[\d+\]\s*</Title>"
        r".*?</Profile>\s*"
    )
    cleaned, removed = re.subn(pattern, "", text, flags=re.DOTALL)
    if not removed:
        return {"ok": True, "removed": 0}
    temporary = f"{path}.{os.getpid()}.tmp"
    try:
        with open(temporary, "w", encoding="utf-8-sig") as handle:
            handle.write(cleaned)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
        return {"ok": True, "removed": removed}
    except Exception as error:
        try:
            if os.path.exists(temporary):
                os.remove(temporary)
        except Exception:
            pass
        return {"ok": False, "removed": 0, "message": f"{type(error).__name__}: {error}"}


def _lossless_hotkey_vks():
    import re
    path, text = _ls_read_text()
    key, mods = "S", "Alt Control"
    if text:
        km = re.search(r"<Hotkey>(.*?)</Hotkey>", text, re.DOTALL)
        if km:
            key = km.group(1).strip()
        mm = re.search(r"<HotkeyModifierKeys>(.*?)</HotkeyModifierKeys>", text, re.DOTALL)
        if mm:
            mods = mm.group(1).strip()
    vks = []
    for token in mods.split():
        vk = _MODIFIER_VK.get(token.lower())
        if vk and vk not in vks:
            vks.append(vk)
    key_vk = _key_to_vk(key)
    if key_vk:
        vks.append(key_vk)
    return vks or [0x11, 0x12, 0x53]


def _get_lossless_status_sync():
    if os.name != "nt":
        return {"ok": False, "available": False, "installed": False, "running": False, "message": "Windows only."}
    path = _find_lossless()
    installed = bool(path)
    core_initialized = bool(_LS_CORE.get("initialized"))
    ui_running = _lossless_running()
    running = core_initialized or ui_running
    if not running:
        _LS_RUNTIME["active"] = False
        _LS_SCALING_ACTIVE["on"] = False
    data = {
        "ok": True,
        "available": installed,
        "installed": installed,
        "running": running,
        "ui_running": ui_running,
        "core_initialized": core_initialized,
        "engine": "native_core" if core_initialized else ("ui" if ui_running else ""),
        "path": path,
        "message": "",
        "dpi_awareness": _dpi_awareness_snapshot(),
        "runtime": copy.deepcopy(_LS_RUNTIME),
        "autostart": copy.deepcopy(_LS_AUTOSTART_GATE),
        "steam_overlay": copy.deepcopy(_STEAM_OVERLAY_STATE),
        "session_control": copy.deepcopy(_LS_SESSION_CONTROL),
    }
    data.update(_read_lossless_settings())
    return data


def _launch_lossless_sync():
    if os.name != "nt":
        return {"ok": False, "running": False, "message": "Windows only."}
    if _lossless_running():
        return {"ok": True, "running": True, "message": "", "pid": _process_ids_by_name("LosslessScaling.exe")[0]}
    path = _find_lossless()
    try:
        if path:
            process = _start_lossless_hidden(path)
        else:
            return {"ok": False, "running": False, "message": "Lossless Scaling executable was not found."}
        deadline = time.time() + 6
        while not _lossless_running() and time.time() < deadline:
            time.sleep(0.1)
        running = _lossless_running()
        return {"ok": running, "running": running, "message": "" if running else "Lossless Scaling did not start.", "pid": process.pid}
    except Exception as error:
        return {"ok": False, "running": False, "message": str(error)}


def _terminate_process_pid(pid):
    if os.name != "nt" or not pid:
        return False
    PROCESS_TERMINATE = 0x0001
    handle = ctypes.windll.kernel32.OpenProcess(PROCESS_TERMINATE, False, int(pid))
    if not handle:
        return False
    try:
        return bool(ctypes.windll.kernel32.TerminateProcess(handle, 0))
    finally:
        ctypes.windll.kernel32.CloseHandle(handle)


def _deactivate_lossless_capture_sync(reason="disabled", preserve_runtime=False):
    with _LS_CONTROL_LOCK:
        errors = []
        stopped_status = {}
        if _LS_CORE.get("initialized"):
            try:
                if _LS_RUNTIME.get("active") or int(
                    _LS_CORE.get("last_status", {}).get("status", 0) or 0
                ) in (1, 2):
                    _LS_CORE_EVENT.clear()
                    with _lossless_dpi_scope():
                        _LS_CORE["dll"].Activate(ctypes.c_void_p(0))
                    deadline = time.time() + 5.0
                    while time.time() < deadline:
                        if _LS_CORE_EVENT.wait(0.1):
                            stopped_status = copy.deepcopy(_LS_CORE.get("last_status", {}))
                            if int(stopped_status.get("status", -1)) == 0:
                                break
                            _LS_CORE_EVENT.clear()
            except Exception as error:
                errors.append(f"core: {type(error).__name__}: {error}")
        runtime_update = {
            "active": False,
            "verified_capture": False,
            "core_status": stopped_status,
            "watchdog_token": "",
            "last_action": (
                f"suspend:{reason}" if preserve_runtime else f"stop:{reason}"
            ),
            "last_error": ", ".join(errors),
            "last_transition_at": _session_timestamp(),
        }
        if not preserve_runtime:
            runtime_update.update({
                "active_app_id": 0,
                "active_title": "",
                "managed_pid": 0,
                "core_host_pid": 0,
                "active_settings": {},
            })
        _LS_RUNTIME.update(runtime_update)
        _LS_SCALING_ACTIVE["on"] = False
        _LS_HIDE_ACTIVE["on"] = False
        return {
            "ok": not errors,
            "active": False,
            "running": bool(_LS_CORE.get("initialized")),
            "message": _LS_RUNTIME["last_error"],
            "core_status": stopped_status,
            "core_initialized": bool(_LS_CORE.get("initialized")),
        }


def _stop_lossless_managed_sync(reason="disabled", uninitialize=False):
    with _LS_CONTROL_LOCK:
        deactivated = _deactivate_lossless_capture_sync(reason)
        errors = []
        if deactivated.get("message"):
            errors.append(str(deactivated.get("message")))
        if uninitialize and _LS_CORE.get("initialized"):
            try:
                with _lossless_dpi_scope():
                    _LS_CORE["dll"].UnInit()
            except Exception as error:
                errors.append(f"core unload: {type(error).__name__}: {error}")
            finally:
                _LS_CORE["initialized"] = False
                _LS_CORE["last_status"] = {}
                _LS_CORE["target_monitor"] = {}

        pids = _process_ids_by_name("LosslessScaling.exe")
        for pid in pids:
            if not _terminate_process_pid(pid):
                errors.append(f"PID {pid}")
        deadline = time.time() + 4
        while _lossless_running() and time.time() < deadline:
            time.sleep(0.08)
        still_running = _lossless_running()
        if still_running:
            errors.append("Lossless Scaling is still running.")
        cleanup = _remove_lossless_autoscale_profiles()
        if not cleanup.get("ok"):
            errors.append(str(cleanup.get("message", "profile cleanup failed")))
        _LS_RUNTIME.update({
            "active": False,
            "active_app_id": 0,
            "active_title": "",
            "managed_pid": 0,
            "core_host_pid": 0,
            "verified_capture": False,
            "core_status": copy.deepcopy(deactivated.get("core_status", {})),
            "watchdog_token": "",
            "last_action": f"stop:{reason}",
            "last_error": ", ".join(errors),
            "last_transition_at": _session_timestamp(),
        })
        _LS_SCALING_ACTIVE["on"] = False
        _LS_HIDE_ACTIVE["on"] = False
        return {
            "ok": not still_running and bool(cleanup.get("ok")) and not errors,
            "active": False,
            "running": still_running,
            "message": _LS_RUNTIME["last_error"],
            "profile_cleanup": cleanup,
            "core_status": copy.deepcopy(deactivated.get("core_status", {})),
            "core_initialized": bool(_LS_CORE.get("initialized")),
        }


def _activate_lossless_managed_sync(
    settings=None,
    app_id=0,
    title="",
    force_restart=False,
    auto_scale=True,
    filters=None,
):
    with _LS_CONTROL_LOCK:
        path = _find_lossless()
        if not path:
            return {"ok": False, "active": False, "running": False, "message": "Lossless Scaling is not installed."}
        normalized = _normalize_lossless_profile(settings or _read_lossless_settings().get("settings", {}))
        previous_target = _LS_RUNTIME.get("activation_focus", {}).get("target", {})
        same_target = (
            bool(_LS_RUNTIME.get("active"))
            and int(_LS_RUNTIME.get("active_app_id", 0) or 0) == int(app_id or 0)
            and bool(_LS_CORE.get("initialized"))
            and _process_is_alive(int(previous_target.get("pid", 0) or 0))
        )
        if same_target and not force_restart:
            return {
                "ok": True,
                "active": True,
                "running": True,
                "recovered": False,
                "runtime": copy.deepcopy(_LS_RUNTIME),
            }

        if _LS_RUNTIME.get("active"):
            _deactivate_lossless_capture_sync("native_core_restart")

        loaded = _load_lossless_core()
        if not loaded.get("ok"):
            _LS_RUNTIME["last_error"] = str(loaded.get("message", ""))
            return {
                "ok": False, "active": False, "running": False,
                "message": _LS_RUNTIME["last_error"],
            }

        target = _lossless_core_target(filters)
        if not target:
            _LS_RUNTIME.update({
                "active": False,
                "active_app_id": int(app_id or 0),
                "active_title": title,
                "managed_pid": 0,
                "core_host_pid": 0,
                "verified_capture": False,
                "last_action": "native_core_waiting_for_game_window",
                "last_error": "",
                "activation_focus": {
                    "mode": "lossless_native_core",
                    "filters": list(filters or []),
                    "foreground": _foreground_window_snapshot(),
                },
                "last_transition_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            })
            return {
                "ok": True,
                "active": False,
                "running": False,
                "pending": True,
                "message": "Waiting for the game window.",
                "runtime": copy.deepcopy(_LS_RUNTIME),
            }

        target_monitor = _lossless_monitor_snapshot(
            int(target.get("hwnd", 0) or 0)
        )
        if not target_monitor.get("ok"):
            message = "The physical monitor for the game window could not be resolved."
            _LS_RUNTIME["last_error"] = message
            return {
                "ok": False,
                "active": False,
                "running": False,
                "message": message,
                "target": target,
                "target_monitor": target_monitor,
            }
        _LS_CORE["target_monitor"] = copy.deepcopy(target_monitor)
        _LS_RUNTIME["target_monitor"] = copy.deepcopy(target_monitor)
        _LS_RUNTIME["dpi_awareness"] = _dpi_awareness_snapshot()

        # A running UI instance owns another Lossless core. Close only that
        # process before initializing the in-process engine.
        for pid in _process_ids_by_name("LosslessScaling.exe"):
            _terminate_process_pid(pid)
        deadline = time.time() + 4.0
        while _lossless_running() and time.time() < deadline:
            time.sleep(0.08)
        if _lossless_running():
            return {
                "ok": False, "active": False, "running": True,
                "message": "Lossless Scaling could not be prepared.",
            }
        _remove_lossless_autoscale_profiles()

        initialized = _initialize_lossless_core()
        if not initialized.get("ok"):
            _LS_RUNTIME["last_error"] = str(initialized.get("message", ""))
            return {
                "ok": False, "active": False, "running": False,
                "message": _LS_RUNTIME["last_error"],
            }

        # WGC is the common capture path for native, Vulkan and OpenGL games.
        # The previous profile-driven implementation already selected it for
        # managed sessions; keep that compatibility while calling the core
        # directly.
        normalized = {**normalized, "capture_api": "WGC"}
        applied = _apply_lossless_core_settings(normalized)
        if not applied.get("ok"):
            _stop_lossless_managed_sync("native_core_settings_failed")
            return {
                "ok": False, "active": False, "running": False,
                "message": str(applied.get("message", "")),
            }

        _LS_CORE_EVENT.clear()
        _LS_CORE["last_status"] = {}
        try:
            with _lossless_dpi_scope():
                accepted = bool(
                    _LS_CORE["dll"].Activate(
                        ctypes.c_void_p(int(target.get("hwnd", 0) or 0))
                    )
                )
        except Exception as error:
            _stop_lossless_managed_sync("native_core_activate_exception")
            return {
                "ok": False, "active": False, "running": False,
                "message": f"{type(error).__name__}: {error}",
            }
        deadline = time.time() + 8.0
        status = {}
        while time.time() < deadline:
            if _LS_CORE_EVENT.wait(0.1):
                status = copy.deepcopy(_LS_CORE.get("last_status", {}))
                if int(status.get("error_code", 0) or 0) or int(
                    status.get("status", -1)
                ) in (1, 2):
                    break
                _LS_CORE_EVENT.clear()
        capture_confirmed = (
            not int(status.get("error_code", 0) or 0)
            and int(status.get("status", -1)) in (1, 2)
        )
        fullscreen_verified = (
            _lossless_output_matches_monitor(status, target_monitor)
            if int(status.get("status", -1) or -1) == 2
            else False
        )
        if not accepted or not capture_confirmed:
            message = (
                f"Lossless core error {int(status.get('error_code', 0))}."
                if int(status.get("error_code", 0) or 0)
                else "Lossless Scaling did not confirm the game capture."
            )
            _stop_lossless_managed_sync("native_core_not_confirmed")
            return {
                "ok": False, "active": False, "running": False,
                "message": message, "core_status": status,
            }
        if int(status.get("status", -1) or -1) == 2 and not fullscreen_verified:
            monitor = target_monitor.get("monitor", {})
            message = (
                "Lossless Scaling returned "
                f"{int(status.get('output_width', 0) or 0)}x"
                f"{int(status.get('output_height', 0) or 0)} instead of the "
                f"target monitor's {int(monitor.get('width', 0) or 0)}x"
                f"{int(monitor.get('height', 0) or 0)} physical pixels."
            )
            _stop_lossless_managed_sync("invalid_output_dimensions")
            return {
                "ok": False,
                "active": False,
                "running": False,
                "message": message,
                "core_status": status,
                "target_monitor": target_monitor,
            }

        suspended = int(status.get("status", 0) or 0) == 1
        _LS_RUNTIME.update({
            "active": True,
            "active_app_id": int(app_id or 0),
            "active_title": title or str(target.get("title", "") or ""),
            "managed_pid": 0,
            "core_host_pid": os.getpid(),
            "verified_capture": fullscreen_verified,
            "fullscreen_output_verified": fullscreen_verified,
            "capture_api": normalized["capture_api"],
            "active_settings": copy.deepcopy(normalized),
            "core_status": status,
            "last_action": (
                "native_core_capture_suspended"
                if suspended else "native_core_capture_active"
            ),
            "last_error": "",
            "activation_focus": {
                "mode": "lossless_native_core",
                "target": target,
                "target_monitor": target_monitor,
                "filters": list(filters or []),
                "accepted": accepted,
                "capture_confirmed": capture_confirmed,
                "fullscreen_verified": fullscreen_verified,
            },
            "last_transition_at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        })
        _LS_SCALING_ACTIVE["on"] = True
        _start_lossless_target_watchdog(
            int(target.get("pid", 0) or 0),
            int(target.get("hwnd", 0) or 0),
            int(app_id or 0),
        )
        return {
            "ok": True,
            "active": True,
            "running": True,
            "recovered": bool(force_restart),
            "verified": fullscreen_verified,
            "capture_confirmed": capture_confirmed,
            "suspended": suspended,
            "core_status": status,
            "runtime": copy.deepcopy(_LS_RUNTIME),
        }


def _restart_lossless():
    active = bool(_LS_RUNTIME.get("active"))
    app_id = int(_LS_RUNTIME.get("active_app_id", 0) or 0)
    title = str(_LS_RUNTIME.get("active_title", "") or "")
    settings = _read_lossless_settings().get("settings", {})
    _stop_lossless_managed_sync("settings_restart")
    if active:
        return _activate_lossless_managed_sync(settings, app_id, title, force_restart=True)
    return _launch_lossless_sync()


def _set_lossless_setting_sync(key, value):
    if os.name != "nt":
        return {"ok": False, "message": "Windows only."}
    aliases = {"frame_gen": "frame_generation", "multiplier": "lsfg3_multiplier"}
    normalized_key = aliases.get(key, key)
    if normalized_key not in LS_PROFILE_DEFAULTS:
        return {"ok": False, "message": "Unknown Lossless Scaling setting."}
    settings = _read_lossless_settings().get("settings", {})
    settings[normalized_key] = value
    written = _write_lossless_profile_settings(settings)
    if not written.get("ok"):
        return written
    runtime = None
    if _LS_SESSION_CONTROL.get("resume_requested"):
        _LS_SESSION_CONTROL.update({
            "resume_settings": copy.deepcopy(written.get("settings", {})),
            "last_changed_at": _session_timestamp(),
        })
        runtime = {
            "ok": True,
            "active": True,
            "suspended": True,
            "pending": True,
            "message": "",
        }
    elif _LS_RUNTIME.get("active") and normalized_key != "draw_fps":
        runtime = _activate_lossless_managed_sync(
            written.get("settings", {}),
            int(_LS_RUNTIME.get("active_app_id", 0) or 0),
            str(_LS_RUNTIME.get("active_title", "") or ""),
            force_restart=True,
        )
    elif _LS_RUNTIME.get("active"):
        runtime = {
            "ok": True,
            "active": True,
            "pending": True,
            "applies_on_next_activation": True,
            "message": "",
        }
    return {"ok": True, "message": "", "settings": written.get("settings"), "runtime": runtime}


def _set_lossless_scaling_sync(enabled, app_id=0, title=""):
    if os.name != "nt":
        return {"ok": False, "active": False, "message": "Windows only."}
    if bool(enabled):
        target_app_id = int(
            app_id
            or _LS_RUNTIME.get("active_app_id", 0)
            or _LS_SESSION_CONTROL.get("resume_app_id", 0)
            or 0
        )
        if not target_app_id or target_app_id == int(LOSSLESS_APPID):
            return {
                "ok": False,
                "active": False,
                "running": bool(
                    _LS_CORE.get("initialized") or _lossless_running()
                ),
                "game_required": True,
                "message": "Lossless Scaling can only be enabled while a game is running.",
                "runtime": copy.deepcopy(_LS_RUNTIME),
            }
        _cancel_lossless_autostart("manual_enable")
        _clear_lossless_manual_override(target_app_id)
        settings = _read_lossless_settings().get("settings", {})
        filters = _lossless_profile_filters(title)
        if _STEAM_OVERLAY_STATE.get("active"):
            _queue_lossless_overlay_resume(
                settings, target_app_id, str(title or ""), filters
            )
            return {
                "ok": True,
                "active": True,
                "running": bool(_LS_CORE.get("initialized")),
                "suspended": True,
                "pending": True,
                "message": "",
                "runtime": copy.deepcopy(_LS_RUNTIME),
                "session_control": copy.deepcopy(_LS_SESSION_CONTROL),
            }
        return _activate_lossless_managed_sync(
            settings,
            target_app_id,
            str(title or ""),
            force_restart=not bool(_LS_RUNTIME.get("active")),
            auto_scale=True,
            filters=filters,
        )
    target_app_id = int(
        app_id
        or _LS_RUNTIME.get("active_app_id", 0)
        or _LS_SESSION_CONTROL.get("resume_app_id", 0)
        or 0
    )
    _cancel_lossless_autostart("manual_disable")
    _LS_SESSION_CONTROL.update({
        "manual_disabled_app_id": target_app_id,
        "manual_disabled_title": str(
            title or _LS_RUNTIME.get("active_title", "") or ""
        ),
        "overlay_suspended": False,
        "overlay_suspend_in_progress": False,
        "resume_requested": False,
        "resume_app_id": 0,
        "resume_title": "",
        "resume_settings": {},
        "resume_filters": [],
        "last_changed_at": _session_timestamp(),
    })
    result = _stop_lossless_managed_sync("qam_toggle")
    result["manual_disabled"] = True
    result["session_control"] = copy.deepcopy(_LS_SESSION_CONTROL)
    return result


# ===================== AMD Radeon (ADLX helper) ====================== #
# Radeon features (RSR, AFMF, Anti-Lag, Chill, Image Sharpening) are driven by
# AMD's ADLX SDK, which is C++/COM. We ship a small C# helper (amd/Program.cs +
# the ADLX C# binding) that the user compiles once with amd/build_amd.bat using
# the in-box .NET Framework compiler - no Visual Studio. We then invoke
# adlx_helper.exe and parse its JSON.

def _adlx_dir():
    return os.path.join(os.path.dirname(__file__), "amd")


def _adlx_exe():
    return os.path.join(_adlx_dir(), "adlx_helper.exe")


def _adlx_source_present():
    return os.path.exists(os.path.join(_adlx_dir(), "Program.cs"))


def _normalize_amd_value(feature, value):
    feature = str(feature or "")
    if feature in {
        "rsr", "afmf", "antilag", "chill", "sharpening", "sharpening_desktop",
        "boost", "enhanced_sync",
    }:
        return _coerce_ls_bool(value)
    ranges = {
        "rsr_sharpness": (0, 100, 75),
        "chill_min": (30, 300, 60),
        "chill_max": (30, 300, 120),
        "sharpening_value": (0, 100, 80),
        "boost_resolution": (50, 100, 83),
    }
    minimum, maximum, default = ranges.get(feature, (0, 100, 0))
    normalized = _coerce_ls_int(value, default, minimum, maximum)
    if feature == "sharpening_value":
        normalized = int(round(normalized / 10.0) * 10)
        normalized = max(minimum, min(maximum, normalized))
    return normalized


def _normalize_amd_profile(settings):
    source = settings if isinstance(settings, dict) else {}
    result = {}
    for feature, default in AMD_PROFILE_DEFAULTS.items():
        result[feature] = _normalize_amd_value(feature, source.get(feature, default))
    if result["chill_min"] > result["chill_max"]:
        result["chill_max"] = result["chill_min"]
    if result["rsr"] and result["sharpening"]:
        result["sharpening"] = False
    if result["chill"]:
        result["antilag"] = False
        result["boost"] = False
    return result


def _amd_profile_snapshot(status):
    if not isinstance(status, dict) or not status.get("ok"):
        return {}
    result = {}
    for feature in AMD_PROFILE_DEFAULTS:
        value = _amd_feature_value(status, feature)
        if value is not None:
            result[feature] = _normalize_amd_value(feature, value)
    return _normalize_amd_profile(result) if result else {}


def _amd_feature_supported(status, feature):
    sections = {
        "rsr": "rsr",
        "rsr_sharpness": "rsr",
        "afmf": "afmf",
        "antilag": "antilag",
        "chill": "chill",
        "chill_min": "chill",
        "chill_max": "chill",
        "sharpening": "sharpening",
        "sharpening_value": "sharpening",
        "boost": "boost",
        "boost_resolution": "boost",
        "enhanced_sync": "enhanced_sync",
    }
    section = status.get(sections.get(feature, ""), {}) if isinstance(status, dict) else {}
    return bool(isinstance(section, dict) and section.get("supported"))


def _amd_cli_value(value):
    if isinstance(value, bool):
        return "on" if value else "off"
    return str(int(value))


def _apply_amd_profile_sync(settings, current_status=None):
    target = _normalize_amd_profile(settings)
    before = current_status if isinstance(current_status, dict) else _get_amd_status_sync()
    if not before.get("ok"):
        return {
            "ok": False,
            "message": before.get("message", "Could not read AMD driver state."),
            "requested": target,
            "before": before,
        }
    before_values = _amd_profile_snapshot(before)
    changed = {
        feature: value
        for feature, value in target.items()
        if _amd_feature_supported(before, feature) and before_values.get(feature) != value
    }
    if not changed:
        return {
            "ok": True,
            "unchanged": True,
            "requested": target,
            "before": before_values,
            "readback": before_values,
            "status": before,
        }

    order = ["rsr_sharpness", "sharpening_value", "boost_resolution"]
    current_chill_max = int(before_values.get("chill_max", target["chill_max"]))
    if target["chill_min"] > current_chill_max:
        order.extend(["chill_max", "chill_min"])
    else:
        order.extend(["chill_min", "chill_max"])
    order.extend(["rsr", "afmf", "antilag", "chill", "sharpening", "boost", "enhanced_sync"])
    pairs = [(feature, changed[feature]) for feature in order if feature in changed]
    args = ["set-batch"]
    for feature, value in pairs:
        args.extend([feature, _amd_cli_value(value)])
    result = _run_adlx(args, timeout=25)
    if not isinstance(result, dict):
        result = {"ok": False, "message": "ADLX helper returned no batch result."}
    readback_status = result if result.get("gpu") else _get_amd_status_sync()
    readback = _amd_profile_snapshot(readback_status)
    mismatches = {
        feature: {"requested": value, "applied": readback.get(feature)}
        for feature, value in changed.items()
        if readback.get(feature) != value
    }
    ok = bool(result.get("ok")) and not mismatches
    return {
        "ok": ok,
        "message": "" if ok else result.get("message", "AMD read-back did not match the profile."),
        "requested": target,
        "changed": changed,
        "before": before_values,
        "readback": readback,
        "mismatches": mismatches,
        "helper": result,
    }


def _run_adlx(args, timeout=25):
    exe = _adlx_exe()
    if os.name != "nt" or not os.path.exists(exe):
        return None
    with _ADLX_LOCK:
        try:
            completed = subprocess.run(
                [exe] + [str(a) for a in args],
                cwd=_adlx_dir(),
                text=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                timeout=timeout,
                creationflags=_NO_WINDOW,
            )
            out = (completed.stdout or "").strip()
            if not out:
                return {
                    "ok": False,
                    "message": (completed.stderr or "").strip() or "ADLX helper returned no data.",
                }
            line = [ln for ln in out.splitlines() if ln.strip()][-1]
            result = json.loads(line)
            helper_stderr = (completed.stderr or "").strip()
            if helper_stderr and isinstance(result, dict):
                result["helper_stderr"] = helper_stderr
            if completed.returncode != 0 and isinstance(result, dict):
                result["ok"] = False
                result["exit_code"] = completed.returncode
            return result
        except Exception as error:
            return {"ok": False, "message": f"{type(error).__name__}: {error}"}


def _get_amd_status_sync():
    if os.name != "nt":
        return {"ok": False, "available": False, "built": False, "source": False, "message": "Windows only."}
    if not os.path.exists(_adlx_exe()):
        return {"ok": False, "available": False, "built": False,
                "source": _adlx_source_present(),
                "message": "Run amd/build_amd.bat once to enable Radeon features."}
    data = _run_adlx(["status"])
    if not isinstance(data, dict):
        return {"ok": False, "available": False, "built": True, "source": True,
                "message": "ADLX helper returned no data."}
    data["available"] = bool(data.get("ok"))
    data["built"] = True
    data["source"] = True
    return data


def _set_amd_sync(feature, value, request_generation=0):
    if os.name != "nt":
        return {"ok": False, "message": "Windows only."}
    if not os.path.exists(_adlx_exe()):
        return {"ok": False, "message": "Radeon helper not built (run amd/build_amd.bat)."}
    feature = str(feature or "")
    started = time.perf_counter()
    with _ADLX_LOCK:
        with _AMD_REQUEST_LOCK:
            if (
                request_generation
                and int(_AMD_REQUEST_GENERATIONS.get(feature, 0) or 0)
                != int(request_generation)
            ):
                return {
                    "ok": True,
                    "superseded": True,
                    "feature": feature,
                    "requested_value": value,
                }
        before = _get_amd_status_sync()
        if not isinstance(before, dict) or not before.get("ok"):
            return {
                "ok": False,
                "scope": "global",
                "message": before.get("message", "Could not read AMD driver state.")
                if isinstance(before, dict) else "Could not read AMD driver state.",
            }

        if feature.startswith("display_"):
            section_name = feature[len("display_"):]
            section = before.get("display_color", {}).get(section_name, {})
            normalized = _coerce_ls_int(
                value,
                section.get("value", 0),
                section.get("min", -10000),
                section.get("max", 10000),
            )
        else:
            normalized = _normalize_amd_value(feature, value)

        before_values = _amd_profile_snapshot(before)
        requested_pairs = []
        if normalized is True:
            if feature == "rsr" and before_values.get("sharpening"):
                requested_pairs.append(("sharpening", False))
            elif feature == "sharpening" and before_values.get("rsr"):
                requested_pairs.append(("rsr", False))
            elif feature == "chill":
                if before_values.get("antilag"):
                    requested_pairs.append(("antilag", False))
                if before_values.get("boost"):
                    requested_pairs.append(("boost", False))
            elif feature in {"antilag", "boost"} and before_values.get("chill"):
                requested_pairs.append(("chill", False))
        if feature == "sharpening":
            desktop_supported = bool(
                before.get("sharpening", {}).get("desktop_supported")
            )
            if desktop_supported:
                requested_pairs.append(("sharpening_desktop", normalized))
        requested_pairs.append((feature, normalized))

        args = ["set-batch"]
        for requested_feature, requested_value in requested_pairs:
            args.extend([requested_feature, _amd_cli_value(requested_value)])
        helper = _run_adlx(args)
        readback = helper if isinstance(helper, dict) and helper.get("gpu") else _get_amd_status_sync()
        applied = _amd_feature_value(readback, feature)
        confirmed = applied == normalized
        ok = bool(isinstance(helper, dict) and helper.get("ok")) and bool(
            isinstance(readback, dict) and readback.get("ok")
        ) and confirmed

        tracked = [
            "rsr", "rsr_sharpness", "afmf", "antilag", "chill", "chill_min",
            "chill_max", "sharpening", "sharpening_value", "boost",
            "sharpening_desktop", "boost_resolution", "enhanced_sync",
        ]
        before_flat = {
            item: _amd_feature_value(before, item)
            for item in tracked
        }
        after_flat = {
            item: _amd_feature_value(readback, item)
            for item in tracked
        }
        explicitly_requested = {item for item, _ in requested_pairs}
        side_effects = {
            item: {"before": before_flat.get(item), "after": after_flat.get(item)}
            for item in tracked
            if item not in explicitly_requested and before_flat.get(item) != after_flat.get(item)
        }

        result = dict(readback) if isinstance(readback, dict) else {}
        result.update({
            "ok": ok,
            "available": bool(isinstance(readback, dict) and readback.get("ok")),
            "built": True,
            "source": True,
            "scope": "global",
            "applied_feature": feature,
            "requested_value": value,
            "normalized_value": normalized,
            "applied_value": applied,
            "confirmed": confirmed,
            "before_value": _amd_feature_value(before, feature),
            "requested_operations": [
                {"feature": item, "value": requested_value}
                for item, requested_value in requested_pairs
            ],
            "side_effects": side_effects,
            "helper": helper,
            "duration_ms": round((time.perf_counter() - started) * 1000),
        })
        if not ok:
            helper_message = helper.get("message", "") if isinstance(helper, dict) else ""
            result["message"] = helper_message or (
                f"AMD driver read-back is {applied!r}; requested {normalized!r}."
            )
        _AMD_LAST_TRANSACTIONS.append({
            "timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat(),
            "feature": feature,
            "requested_value": value,
            "normalized_value": normalized,
            "before_value": result.get("before_value"),
            "applied_value": applied,
            "confirmed": confirmed,
            "ok": ok,
            "requested_operations": result["requested_operations"],
            "side_effects": side_effects,
            "helper": helper,
            "duration_ms": result["duration_ms"],
        })
        del _AMD_LAST_TRANSACTIONS[:-30]
        return result


def _amd_feature_value(status, feature):
    if not isinstance(status, dict):
        return None
    paths = {
        "rsr": ("rsr", "enabled"),
        "rsr_sharpness": ("rsr", "sharpness"),
        "afmf": ("afmf", "enabled"),
        "antilag": ("antilag", "enabled"),
        "chill": ("chill", "enabled"),
        "chill_min": ("chill", "min"),
        "chill_max": ("chill", "max"),
        "sharpening": ("sharpening", "enabled"),
        "sharpening_desktop": ("sharpening", "desktop_enabled"),
        "sharpening_value": ("sharpening", "value"),
        "boost": ("boost", "enabled"),
        "boost_resolution": ("boost", "resolution"),
        "enhanced_sync": ("enhanced_sync", "enabled"),
    }
    path = paths.get(str(feature))
    if path:
        section = status.get(path[0])
        return section.get(path[1]) if isinstance(section, dict) else None
    if str(feature).startswith("display_"):
        key = str(feature)[len("display_"):]
        section = status.get("display_color", {}).get(key)
        return section.get("value") if isinstance(section, dict) else None
    return None
