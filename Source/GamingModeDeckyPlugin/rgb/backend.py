"""Synchronous public API backed by one serialized worker; no automatic replay."""

from concurrent.futures import ThreadPoolExecutor
from copy import deepcopy
import json
import os
from pathlib import Path
import tempfile
import threading

from .protocols import classify, static_report, validate_color


class RgbBackend:
    def __init__(self, config_path=None, *, transport=None):
        self._transport = transport
        self._config_path = Path(config_path) if config_path is not None else None
        self._gate = threading.Lock()
        self._pool = ThreadPoolExecutor(max_workers=1, thread_name_prefix="playhub-rgb")
        self._closed = False
        self._last = None

    def _call(self, operation):
        with self._gate:
            if self._closed:
                return {"ok": False, "error": "closed"}
            return self._pool.submit(operation).result()

    def _get_transport(self):
        if self._transport is None:
            from .win32 import Win32HidTransport
            self._transport = Win32HidTransport()
        return self._transport

    def get_status(self):
        """Read descriptor metadata only. Never sends HID reports or restores config."""
        return self._call(self._status)

    def _status(self):
        try:
            devices = self._get_transport().enumerate()
        except (OSError, RuntimeError) as exc:
            return {"ok": False, "error": str(exc), "devices": [],
                    "hardware_tested": False}
        result = []
        for device in devices:
            family, reason = classify(device)
            if family is None:
                continue
            result.append({"id": device.id, "family": family,
                           "vid": device.vid, "pid": device.pid,
                           "usage_page": device.usage_page, "usage": device.usage,
                           "release": device.release,
                           "output_length": device.output_length,
                           "input_length": device.input_length,
                           "input_report_ids": list(device.input_report_ids),
                           "output_report_ids": list(device.output_report_ids),
                           "blocked_reason": reason,
                           "hardware_tested": False,
                           "requires_explicit_request": True,
                           "capabilities": {"static_color": reason is None,
                                            "brightness": reason is None,
                                            "off": reason is None,
                                            "readback": False, "rollback": False,
                                            "per_zone": False, "effects": False}})
        return {"ok": True, "devices": result, "last_request": deepcopy(self._last),
                "hardware_tested": False, "automatic_replay": False}

    def set_color(self, device_id, color, brightness=100, power=True, *,
                  user_requested=False):
        """Parent must set user_requested=True only for an explicit UI action.

        A successful result means a complete OS write, not a device readback.
        This can overwrite the device's first RGB profile; persistence is unknown.
        """
        if user_requested is not True:
            return {"ok": False, "error": "explicit_user_request_required"}
        try:
            color = validate_color(color, brightness, power)
            if not isinstance(device_id, str) or len(device_id) != 64:
                raise ValueError("invalid device_id")
        except ValueError as exc:
            return {"ok": False, "error": str(exc)}
        return self._call(lambda: self._set(device_id, color, brightness, power))

    def _set(self, device_id, color, brightness, power):
        try:
            transport = self._get_transport()
            found = [d for d in transport.enumerate() if d.id == device_id]
            if len(found) != 1:
                return {"ok": False, "error": "device_missing_or_ambiguous"}
            device = found[0]
            report = static_report(device, color, brightness, power)
        except (OSError, RuntimeError, ValueError) as exc:
            return {"ok": False, "error": str(exc)}
        try:
            written = transport.write(device, report)
            if written != len(report):
                raise OSError("short_write")
        except (OSError, RuntimeError) as exc:
            self._last = {"device_id": device_id, "state": "unknown_after_write_error"}
            return {"ok": False, "error": str(exc), "hardware_state": "unknown",
                    "retried": False, "rollback_available": False}
        self._last = {"device_id": device_id, "color": list(color),
                      "brightness": brightness, "power": power,
                      "state": "sent_not_verified"}
        result = {"ok": True, "hardware_state": "sent_not_verified",
                  "hardware_tested": False, "rollback_available": False,
                  "persisted": False}
        if self._config_path is not None:
            try:
                self._save({"schema": 1, "last_explicit_request": self._last})
                result["persisted"] = True
            except OSError as exc:
                result["persistence_error"] = str(exc)
        return result

    def _save(self, config):
        path = self._config_path
        path.parent.mkdir(parents=True, exist_ok=True)
        name = None
        try:
            with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8",
                                             dir=path.parent, prefix=path.name + ".",
                                             suffix=".tmp", delete=False) as handle:
                name = handle.name
                json.dump(config, handle, sort_keys=True)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(name, path)
        finally:
            if name is not None:
                try:
                    os.unlink(name)
                except FileNotFoundError:
                    pass

    def close(self):
        """Drain the worker. No implicit off/reset/restore hardware writes."""
        with self._gate:
            if self._closed:
                return
            self._closed = True
            try:
                if self._transport is not None:
                    self._pool.submit(self._transport.close).result()
            finally:
                self._pool.shutdown(wait=True)
