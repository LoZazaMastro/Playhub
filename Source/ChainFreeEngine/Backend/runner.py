"""Isolated Playhub preference backend. No hardware lifecycle or live Decky RPC."""
from __future__ import annotations
import argparse
import asyncio
import importlib.util
import inspect
import json
import logging
import os
from pathlib import Path
import sys
import types

READ_METHODS = frozenset({"get_panel_preferences", "get_qam_preferences", "get_home_news_settings", "get_daily_history_settings"})
WRITE_METHODS = frozenset({"save_panel_preferences", "set_qam_preferences", "set_home_news_settings", "set_daily_history_settings"})
RUNTIME_READ_METHODS = frozenset({"get_capabilities", "get_initial_state", "get_audio_devices", "get_hdr_status", "get_display_status", "get_power_status", "get_device_info", "get_performance_status", "get_lossless_status", "get_lossless_profile", "get_agent_status", "get_tdp_status", "get_amd_status", "get_display_change", "get_daily_history", "get_history_wikipedia_url", "get_home_news"})
MAX_LINE = 1024 * 1024


class BackendError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def checked_path(path, workspace, *, must_exist=True, allow_outside=False):
    raw = Path(os.path.abspath(path))
    root = Path(os.path.abspath(workspace))
    if not allow_outside and (raw == root or not raw.is_relative_to(root)):
        raise BackendError("path_outside_workspace", "The path must be inside the isolated workspace.")
    for part in (raw, *raw.parents):
        if part.exists() and (part.is_symlink() or getattr(part.lstat(), "st_file_attributes", 0) & 0x400):
            raise BackendError("linked_path", "Linked workspace paths are not supported.")
    if must_exist and not raw.exists():
        raise BackendError("path_missing", "A required isolated path is missing.")
    return raw


class SettingsLease:
    """Cross-process lock for this isolated settings directory, held until exit."""
    def __init__(self, settings):
        self.path = settings / ".playhub-backend.lock"
        self.file = None

    def __enter__(self):
        self.file = open(self.path, "a+b", buffering=0)
        if self.file.seek(0, 2) == 0:
            self.file.write(b"\0")
        self.file.seek(0)
        try:
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(self.file.fileno(), msvcrt.LK_NBLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.file, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except OSError:
            self.file.close()
            self.file = None
            raise BackendError("settings_owner_exists", "Another backend owns these isolated settings.")
        return self

    def __exit__(self, *_):
        if self.file is not None:
            self.file.seek(0)
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(self.file.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl
                fcntl.flock(self.file, fcntl.LOCK_UN)
            self.file.close()


def load_plugin(plugin_root, settings):
    # This is an explicit compatibility environment, not an installed Decky loader.
    decky = types.ModuleType("decky")
    decky.logger = logging.getLogger("playhub.standalone.preferences")
    decky.DECKY_PLUGIN_SETTINGS_DIR = str(settings)
    decky.DECKY_PLUGIN_DIR = str(plugin_root)
    decky.DECKY_PLUGIN_RUNTIME_DIR = str(settings / "runtime")
    sys.modules["decky"] = decky
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location("playhub_standalone_plugin", plugin_root / "main.py")
    if spec is None or spec.loader is None:
        raise BackendError("plugin_import", "The plugin entry point cannot be loaded.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    plugin = module.Plugin()
    missing = [name for name in READ_METHODS if not callable(getattr(plugin, name, None))]
    if missing:
        raise BackendError("plugin_contract", "The plugin does not expose the required preference API.")
    return plugin


class PreferenceBackend:
    def __init__(self, plugin, instance_id, allow_writes=False, runtime_reads=False):
        self.plugin = plugin
        self.instance_id = instance_id
        self.allow_writes = allow_writes
        self.read_methods = READ_METHODS | (RUNTIME_READ_METHODS if runtime_reads else frozenset())
        self.shutdown = False

    def status(self):
        return {"instanceId": self.instance_id, "processId": os.getpid(), "backendReady": True,
                "mode": "standalone-read" if self.read_methods != READ_METHODS else "isolated-preferences", "lifecycleStarted": False, "hardwareEnabled": False,
                "settingsLeaseHeld": True, "writerActive": self.allow_writes,
                "readMethods": sorted(self.read_methods), "writeMethods": sorted(WRITE_METHODS) if self.allow_writes else []}

    async def dispatch(self, request):
        if not isinstance(request, dict):
            raise BackendError("invalid_request", "A JSON object is required.")
        request_id = request.get("id")
        if isinstance(request_id, bool) or not isinstance(request_id, (int, str)) or len(str(request_id)) > 128:
            raise BackendError("invalid_request", "A bounded request identity is required.")
        method = request.get("method")
        args = request.get("args")
        if method == "host.status":
            return self.status()
        if method == "host.shutdown":
            self.shutdown = True
            return {"stopped": True, "lifecycleStarted": False}
        if not isinstance(method, str) or method not in self.read_methods | WRITE_METHODS:
            raise BackendError("method_not_allowed", "This operation is not enabled by the isolated preference backend.")
        if method in WRITE_METHODS and not self.allow_writes:
            raise BackendError("write_disabled", "Preference writes were not enabled for this isolated session.")
        target = getattr(self.plugin, method, None)
        if not callable(target):
            raise BackendError("method_unavailable", "The plugin does not provide this preference operation.")
        if args is None:
            result = target()
        elif isinstance(args, list) and len(args) <= 32:
            result = target(*args)
        elif isinstance(args, dict) and len(args) <= 32 and all(isinstance(k, str) and not k.startswith("_") for k in args):
            result = target(**args)
        else:
            raise BackendError("invalid_arguments", "Arguments must be null, a bounded array or an object.")
        return await result if inspect.isawaitable(result) else result


async def serve(backend, input_stream, output_stream):
    while not backend.shutdown:
        line = await asyncio.to_thread(input_stream.readline, MAX_LINE + 1)
        if not line:
            return
        request_id = None
        try:
            if len(line) > MAX_LINE:
                while line and not line.endswith(b"\n"):
                    line = await asyncio.to_thread(input_stream.readline, MAX_LINE + 1)
                raise BackendError("request_too_large", "Request exceeds the 1 MiB limit.")
            request = json.loads(line)
            if isinstance(request, dict):
                candidate = request.get("id")
                if isinstance(candidate, (int, str)) and not isinstance(candidate, bool) and len(str(candidate)) <= 128:
                    request_id = candidate
            result = await backend.dispatch(request)
            response = {"id": request_id, "ok": True, "result": result}
        except BackendError as error:
            response = {"id": request_id, "ok": False, "error": {"code": error.code, "message": str(error)}}
        except Exception as error:
            # Never echo request data, preference values, paths or exception content.
            response = {"id": request_id, "ok": False, "error": {"code": "plugin_error", "message": type(error).__name__}}
        encoded = json.dumps(response, ensure_ascii=False).encode("utf-8")
        if len(encoded) > MAX_LINE:
            encoded = json.dumps({"id": request_id, "ok": False, "error": {"code": "response_too_large", "message": "Response exceeds the 1 MiB limit."}}).encode()
        output_stream.write(encoded + b"\n")
        output_stream.flush()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--plugin-root", required=True)
    parser.add_argument("--settings-dir", required=True)
    parser.add_argument("--instance-id", required=True)
    parser.add_argument("--allow-isolated-writes", action="store_true")
    parser.add_argument("--runtime-reads", action="store_true", help="Expose original read-only control APIs without starting the hardware lifecycle.")
    # An installed Playhub has no Decky: the plugin and its settings live under the
    # user profile, not inside this workspace. The flag says that is intended.
    parser.add_argument("--allow-installed-plugin", action="store_true")
    options = parser.parse_args()
    # Keep a dedicated protocol FD; redirect even native/plugin stdout to stderr.
    protocol = os.fdopen(os.dup(sys.stdout.fileno()), "wb", buffering=0)
    os.dup2(sys.stderr.fileno(), sys.stdout.fileno())
    logging.basicConfig(stream=sys.stderr, level=logging.WARNING)
    try:
        workspace = Path(os.path.abspath(options.workspace))
        if not workspace.is_dir() or workspace == Path(workspace.anchor):
            raise BackendError("invalid_workspace", "An existing isolated workspace is required.")
        installed = bool(options.allow_installed_plugin)
        plugin_root = checked_path(options.plugin_root, workspace, allow_outside=installed)
        checked_path(plugin_root / "main.py", workspace, allow_outside=installed)
        settings = checked_path(options.settings_dir, workspace, must_exist=False, allow_outside=installed)
        if not installed and ".local" not in settings.relative_to(workspace).parts:
            raise BackendError("invalid_settings", "Settings must be inside the workspace's .local experiment area.")
        if settings.is_relative_to(plugin_root) or plugin_root.is_relative_to(settings):
            raise BackendError("overlapping_paths", "Plugin and settings directories must be separate.")
        if not options.instance_id or len(options.instance_id) > 128:
            raise BackendError("invalid_instance", "A bounded instance identity is required.")
        settings.mkdir(parents=True, exist_ok=True)
        checked_path(settings, workspace, allow_outside=installed)
        checked_path(settings / ".playhub-backend.lock", workspace, must_exist=False, allow_outside=installed)
        with SettingsLease(settings):
            plugin = load_plugin(plugin_root, settings)
            backend = PreferenceBackend(plugin, options.instance_id, options.allow_isolated_writes, options.runtime_reads)
            asyncio.run(serve(backend, sys.stdin.buffer, protocol))
        return 0
    except BackendError as error:
        print(json.dumps({"startupError": error.code, "message": str(error)}), file=sys.stderr)
        return 2
    except Exception as error:
        print(json.dumps({"startupError": "plugin_import", "message": type(error).__name__}), file=sys.stderr)
        return 3
    finally:
        protocol.close()


if __name__ == "__main__":
    raise SystemExit(main())
