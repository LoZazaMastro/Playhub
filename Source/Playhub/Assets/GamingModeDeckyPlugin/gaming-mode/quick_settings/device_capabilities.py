"""Actual device controls, separate from the user's presentation profile."""
from concurrent.futures import Future, wait
from collections import deque
import re
import threading
import time

_ROG = ("rog-ally", "rog-ally-x", "rog-xbox-ally", "rog-xbox-ally-x")
_ONEX_TYPE2 = ("onexplayer-3", "onexplayer-x2", "onexplayer-x2-mini", "onexplayer-apex-i", "onexplayer-apex-air", "onexplayer-super-v")
_EXPECTED = {model: (("presets", "asus"), ("fans", "asus-fan"), ("temperatures", "asus-fan")) for model in _ROG}
_EXPECTED.update({model: (("fans", "onex-fan"), ("temperatures", "onex-fan")) for model in _ONEX_TYPE2})
_EXPECTED["rog-xbox-ally-x"] += (("power-limits", "asus-tdp"),)
_EXPECTED["ayaneo-3"] = (("rgb", "ayaneo3"), ("vibration", "ayaneo3"))
_EXPECTED.update({model: (("vibration", "gpd-v1"),) for model in ("gpd-win-4", "gpd-win-mini")})
_EXPECTED.update({"legion-go": (("presets", "lenovo"),),
                  "legion-go-s": (("presets", "lenovo-other"), ("power-limits", "lenovo-other")),
                  "legion-go-2": (("presets", "lenovo-other"), ("power-limits", "lenovo-other")),
                  "msi-claw-8-ex": (("presets", "msi"), ("power-limits", "msi"), ("charge-limit", "msi"))})
_TOKEN = re.compile(r"[a-f0-9]{64}")


def expected_controls(model):
    return tuple(control for control, _ in _EXPECTED.get(model, ()))


def _number(value, low, high):
    return type(value) is int and low <= value <= high


def _proof(report, provider):
    return (isinstance(report, dict) and report.get("available") is True and report.get("ok") is True
            and report.get("provider") == provider and isinstance(report.get("token"), str)
            and _TOKEN.fullmatch(report["token"]) is not None)


def _temperature_identity(identity, model=None):
    normalize = lambda value: re.sub(r"[^a-z0-9]", "", value.lower()) if isinstance(value, str) else ""
    physical = normalize(identity.get("model")) if isinstance(identity, dict) else ""
    identifiers = (("rc71l", "rogallyrc71lrc71l"), ("rc72la", "rogallyxrc72larc72la"),
                   ("rc73ya", "rogxboxallyrc73yarc73ya"), ("rc73xa", "rogxboxallyxrc73xarc73xa"))
    return (isinstance(identity, dict) and normalize(identity.get("manufacturer")) in ("asus", "asustekcomputerinc")
            and any(physical in aliases and (model is None or model == profile) for profile, aliases in zip(_ROG, identifiers)))


def ayaneo3_identity(identity):
    return (isinstance(identity, dict) and type(identity.get("manufacturer")) is str
            and type(identity.get("model")) is str
            and identity["manufacturer"].strip().upper() == "AYANEO"
            and identity["model"].strip().upper() == "AYANEO 3")


def gpd_v1_identity(identity):
    return (isinstance(identity, dict) and type(identity.get("manufacturer")) is str
            and identity["manufacturer"].strip().upper() == "GPD"
            and type(identity.get("board_model")) is str
            and identity["board_model"].strip().upper() in ("G1618-04", "G1617-01"))


def _control(control, provider, report, identity=None, model=None):
    row = {"id": control, "available": False, "status": "unavailable", "destination": "performance"}
    if control in ("charge-limit", "rgb", "vibration"):
        row["destination"] = "device"
    if isinstance(report, dict) and report.get("code") in ("busy", "timeout"):
        return {**row, "status": "pending"}
    if provider == "asus" and _proof(report, "asus-ally"):
        commands = report.get("commands")
        if isinstance(commands, list) and all(type(v) is int for v in commands) and sorted(commands) == [0, 1, 2]:
            row.update(available=True, status="manual-commands")
    elif provider == "asus-fan" and _proof(report, "asus-ally"):
        if control == "temperatures":
            temperatures = report.get("temperatures")
            values = [temperatures.get(key) for key in ("cpu_c", "gpu_c")] if isinstance(temperatures, dict) and set(temperatures) == {"cpu_c", "gpu_c"} else []
            if (_temperature_identity(identity, model) and isinstance(report.get("capabilities"), dict)
                    and report["capabilities"].get("temperature_c") is True and any(_number(v, 1, 125) for v in values)
                    and all(v is None or _number(v, 1, 125) for v in values)):
                row.update(available=True, status="read-only", values=values)
            return row
        fans = report.get("fans")
        values = [fans.get(key) for key in ("cpu_rpm", "gpu_rpm")] if isinstance(fans, dict) else []
        if (isinstance(report.get("capabilities"), dict) and report["capabilities"].get("fan_rpm") is True
                and any(_number(v, 0, 12000) and v % 100 == 0 for v in values)
                and all(v is None or _number(v, 0, 12000) and v % 100 == 0 for v in values)):
            row.update(available=True, status="read-only", values=values)
    elif provider == "asus-tdp" and _proof(report, "asus-rc73xa-tdp"):
        limits = report.get("limitsWatts")
        if (control == "power-limits" and report.get("readbackKind") == "firmware-register"
                and isinstance(limits, dict) and set(limits) == {"spl", "sppt", "fppt"}
                and all(_number(limits[key], 1, 255) for key in ("spl", "sppt", "fppt"))
                and limits["spl"] <= limits["sppt"] <= limits["fppt"]
                and type(report.get("manualRecoveryRequired")) is bool and type(report.get("writeAvailable")) is bool
                and type(report.get("oemControllerActive")) is bool
                and (not report["writeAvailable"] or not report["manualRecoveryRequired"] and not report["oemControllerActive"])):
            row.update(available=True, status="read-only" if not report["writeAvailable"] else "readback",
                       values=[limits[key] for key in ("spl", "sppt", "fppt")])
    elif provider == "onex-fan" and _proof(report, "onex-type2-fan"):
        state, telemetry = report.get("state"), report.get("telemetry")
        if (report.get("readbackKind") != "firmware-register" or not isinstance(state, dict)
                or type(state.get("mode")) is not int or state["mode"] not in (0, 1)
                or not _number(state.get("pwm"), 0, 184) or not isinstance(telemetry, dict)
                or type(report.get("manualRecoveryRequired")) is not bool):
            return row
        if control == "fans":
            row.update(available=True, status="read-only" if report["manualRecoveryRequired"] else "readback")
            if _number(telemetry.get("fanRpm"), 0, 20000):
                row["values"] = [telemetry["fanRpm"]]
        elif control == "temperatures" and _number(telemetry.get("cpuTemperatureC"), 1, 125):
            row.update(available=True, status="read-only", values=[telemetry["cpuTemperatureC"], None])
    elif provider == "lenovo" and _proof(report, "lenovo-legion-go"):
        modes = report.get("modes")
        if (type(report.get("mode")) is int and report["mode"] in (1, 2, 3, 255)
                and isinstance(modes, list) and all(type(v) is int for v in modes) and sorted(modes) == [1, 2, 3]):
            row.update(available=True, status="readback")
    elif provider == "lenovo-other" and _proof(report, "lenovo-other-tdp"):
        if (report.get("nativeUnit") != "W" or report.get("readbackKind") != "firmware-register"
                or type(report.get("mode")) is not int or report["mode"] not in (1, 2, 3, 224, 255)
                or type(report.get("modeControlAvailable")) is not bool or type(report.get("manualRecoveryRequired")) is not bool):
            return row
        if control == "presets":
            row.update(available=True, status="readback" if report["modeControlAvailable"] and not report["manualRecoveryRequired"] else "read-only")
        elif control == "power-limits" and report.get("tdpAvailable") is True:
            limits, bounds = report.get("limitsWatts"), report.get("boundsWatts")
            if (isinstance(limits, dict) and set(limits) == {"spl", "sppt", "fppt"}
                    and all(_number(limits[key], 1, 255) for key in ("spl", "sppt", "fppt"))
                    and limits["spl"] <= limits["sppt"] <= limits["fppt"] and isinstance(bounds, dict)
                    and all(isinstance(bounds.get(key), dict) and _number(bounds[key].get("minimum"), 1, 255)
                            and _number(bounds[key].get("maximum"), bounds[key]["minimum"], 255)
                            and _number(bounds[key].get("step"), 1, 255) for key in ("spl", "sppt", "fppt"))):
                status = "readback" if report.get("writable") is True else "manual-required" if report["mode"] in (1, 2, 3) else "read-only"
                row.update(available=True, status="read-only" if report["manualRecoveryRequired"] else status,
                           values=[limits[key] for key in ("spl", "sppt", "fppt")])
    elif provider == "ayaneo3" and _proof(report, "ayaneo3-hid"):
        state, modules = report.get("state"), report.get("modules")
        if (ayaneo3_identity(identity) and model == "ayaneo-3" and report.get("readbackKind") == "vendor-hid-status"
                and isinstance(state, dict) and isinstance(modules, dict)
                and type(modules.get("left")) is int and modules["left"] in (2,4,6,8,0x42,0x44,0x46,0x48)
                and type(modules.get("right")) is int and modules["right"] in (0x10,0x12,0x14,0x16,0x50,0x52,0x54,0x56)
                and type(report.get("writeAvailable")) is bool and type(report.get("manualRecoveryRequired")) is bool
                and all(state.get(key) in ("solid", "pulse", "rainbow", "off") for key in ("leftMode", "rightMode"))
                and all(isinstance(state.get(key), str) and re.fullmatch(r"[a-fA-F0-9]{6}", state[key]) for key in ("leftRgb", "rightRgb"))
                and state.get("vibration") in ("low", "medium", "high", "off")):
            row.update(available=True, status="readback" if report["writeAvailable"] and not report["manualRecoveryRequired"] else "read-only")
    elif provider == "gpd-v1" and _proof(report, "gpd-v1-hid"):
        state, firmware = report.get("state"), report.get("firmware")
        expected = {"G1618-04": ("gpd-win-4", [4, 7]), "G1617-01": ("gpd-win-mini", [5, 4])}
        device = expected.get(identity["board_model"].strip().upper()) if gpd_v1_identity(identity) else None
        if (control == "vibration" and device is not None and device[0] == model == report.get("profile")
                and report.get("readbackKind") == "gpd-v1-config128-checksum-double-read"
                and isinstance(state, dict) and state.get("rumble") in ("off", "low", "high")
                and isinstance(firmware, dict) and type(firmware.get("k")) is list and firmware["k"] == device[1]
                and all(type(v) is int for v in firmware["k"])
                and type(report.get("writeAvailable")) is bool and type(report.get("recoveryRequired")) is bool
                and not (report["writeAvailable"] and report["recoveryRequired"])):
            row.update(available=True, status="readback" if report["writeAvailable"] else "read-only")
    elif provider == "msi" and _proof(report, "msi-claw-8-ex"):
        valid = (_number(report.get("chargeLimit"), 20, 100) and type(report.get("mode")) is int and report["mode"] in (1, 2, 6))
        if valid and control in ("charge-limit", "presets"):
            row.update(available=True, status="readback")
        elif (valid and control == "power-limits" and report.get("tdpAvailable") is True
              and _number(report.get("pl1"), 8, 35) and _number(report.get("pl2"), report["pl1"] + 2, 45)):
            row.update(available=True, status="readback" if report["mode"] == 6 else "manual-required")
    return row


class DeviceCapabilities:
    """Read-only provider aggregation; at most one bounded probe per provider.

    Providers contain (physical-identity predicate, native read callback).
    Neither accepts a model, path or firmware command supplied by the client.
    """
    def __init__(self, profile, identity, providers, timeout=7.5):
        self._profile, self._identity, self._providers = profile, identity, dict(providers)
        self._timeout = timeout
        self._lock = threading.Lock()
        self._slots = threading.BoundedSemaphore(2)
        self._closed = False
        self._pending = {}
        self._cache = {}
        self._queue = deque()

    def _dispatch_locked(self):
        while self._queue and not self._closed:
            if not self._slots.acquire(blocking=False):
                break
            future, probe = self._queue.popleft()
            def run(future=future, probe=probe):
                running, value, error = False, None, None
                try:
                    with self._lock:
                        if self._closed:
                            future.cancel()
                        running = future.set_running_or_notify_cancel()
                    if running:
                        try:
                            value = probe()
                        except Exception as caught:
                            error = caught
                finally:
                    # A waiter may submit the next round as soon as completion
                    # is published, so free the native slot before that signal.
                    with self._lock:
                        self._slots.release()
                        self._dispatch_locked()
                if running:
                    if error is None:
                        future.set_result(value)
                    else:
                        future.set_exception(error)
            # Queued and active reads must not delay Decky shutdown.
            try:
                threading.Thread(target=run, daemon=True, name="PlayhubDeviceControls").start()
            except RuntimeError as error:
                self._slots.release()
                future.set_exception(error)

    def _start(self, provider, identity):
        predicate, probe = self._providers.get(provider, (lambda _: False, lambda: None))
        if not predicate(identity):
            return None
        key = (provider, identity.get("manufacturer"), identity.get("model"), identity.get("board_manufacturer"), identity.get("board_model"))
        with self._lock:
            if self._closed:
                return None
            cached = self._cache.get(key)
            if cached and time.monotonic() - cached[0] < cached[2]:
                return cached[1]
            future = self._pending.get(key)
            if future is None:
                if sum(not item.done() for item in self._pending.values()) >= 8:
                    return {"code": "busy"}
                future = Future()
                self._pending[key] = future
                self._queue.append((future, probe))
                self._dispatch_locked()
            return key, future

    def get(self, expected_selection=None):
        deadline = time.monotonic() + self._timeout
        profile = self._profile()
        if expected_selection is not None and (not isinstance(expected_selection, str) or expected_selection != profile.get("selection")):
            return {"status": "changed", "controls": []}
        effective, detected = profile.get("effective", "pc"), profile.get("detected", "pc")
        mismatch = effective != detected
        power, display = profile.get("power", {}), profile.get("display")
        controls = [
            {"id": "windows-display", "available": isinstance(display, dict) and _number(display.get("width"), 1, 65535) and _number(display.get("height"), 1, 65535), "status": "windows", "destination": "video"},
            {"id": "windows-power", "available": profile.get("powerModeAvailable") is True, "status": "windows", "destination": "performance"},
            {"id": "windows-battery", "available": isinstance(power, dict) and power.get("available") is True and power.get("battery") is True, "status": "windows", "destination": "device"},
        ]
        for row in controls:
            if not row["available"]:
                row["status"] = "unavailable"
        expected = _EXPECTED.get(effective, ())
        reports, active, identity = {}, {}, None
        if expected and not mismatch:
            identity = self._identity()
            for provider in dict.fromkeys(provider for _, provider in expected):
                started = self._start(provider, identity)
                if isinstance(started, tuple):
                    active[provider] = started
                else:
                    reports[provider] = started
            if active:
                wait([item[1] for item in active.values()], timeout=max(0, deadline - time.monotonic()))
                for provider, (key, future) in active.items():
                    if future.done():
                        try:
                            reports[provider] = future.result()
                        except Exception:
                            reports[provider] = None
                        with self._lock:
                            # A delayed waiter for an older Future must not
                            # remove or overwrite a newly admitted same-key job.
                            if self._pending.get(key) is future:
                                self._pending.pop(key, None)
                                report = reports[provider]
                                ttl = 10 if isinstance(report, dict) and report.get("available") is True else 2
                                if isinstance(report, dict) and report.get("code") in ("busy", "timeout"):
                                    ttl = 0
                                self._cache[key] = (time.monotonic(), report, ttl)
        for control, provider in expected:
            row = _control(control, provider, reports.get(provider), identity, effective)
            if mismatch:
                row["status"] = "hardware-required"
            elif provider in active and not active[provider][1].done():
                row["status"] = "pending"
            controls.append(row)
        return {"status": "ready", "selection": profile.get("selection"), "effective": effective,
                "detected": detected, "mismatch": mismatch, "coverage": "oem" if expected else "windows",
                "controls": controls, "sampled_at_ms": int(time.time() * 1000)}

    def close(self):
        with self._lock:
            self._closed = True
            while self._queue:
                future, _ = self._queue.popleft()
                if future.cancel():
                    # No worker will visit a queued cancellation now. Notify
                    # wait() consumers just as a worker would have done.
                    future.set_running_or_notify_cancel()
            for future in self._pending.values():
                future.cancel()


def support_document(models):
    rows = ["# Handheld controls", "", "The device selector identifies a presentation profile. OEM controls require the physical device and its verified Windows provider. Manual selection does not grant hardware access.", "", "Windows controls are detected individually: display settings, Windows power mode and battery status. OEM integrations are experimental; protocol fixtures do not establish validation on every device or firmware revision.", "", "| Device profile | Integrated OEM controls | Verification |", "| --- | --- | --- |"]
    for model, name, _, _ in models:
        controls = expected_controls(model)
        if model == "rog-xbox-ally-x":
            features, verification = "Silent / Balanced / Turbo commands; fan RPM; CPU/GPU temperatures; sustained / boost power limits", "Experimental RC73XA power limits with firmware readback; writes require exclusive OEM control"
        elif model in _ROG:
            features, verification = "Silent / Balanced / Turbo commands; fan RPM; CPU/GPU temperatures", "Command acknowledgement; independent sensor readback"
        elif model == "legion-go":
            features, verification = "Quiet / Balanced / Performance presets", "Firmware readback; original 83E1 / 8APU1 only"
        elif model in ("legion-go-s", "legion-go-2"):
            features, verification = "Quiet / Balanced / Performance / Custom presets; sustained / short / peak power limits", "Experimental Windows WMI readback and firmware bounds; Go S 83L3 / 83N6 and Go 2 83N0 / 83N1 only"
        elif model == "msi-claw-8-ex":
            features, verification = "Firmware mode; PL1 / PL2 in Manual mode; charge limit", "Firmware readback; MS-1T91 / CG3EM only"
        elif model in _ONEX_TYPE2:
            features, verification = "Automatic / manual fan speed; fan RPM; CPU temperature", "Experimental type-2 baseboard WMI provider; firmware readback; manual fan range 30-100%"
        elif model in ("gpd-win-4", "gpd-win-mini"):
            features, verification = "Controller rumble intensity (off / low / high)", "Experimental Windows HID config readback; Win4 G1618-04 K4.07 and Mini 2024 G1617-01 K5.04 only; PID 0137 / Mini 2025 unsupported; hardware validation pending"
        elif model == "ayaneo-3":
            features, verification = "RGB lighting modes and colour; controller vibration intensity", "Experimental Windows vendor HID with status readback; exact AYANEO 3 identity and supported controller modules only; AB07 remains Windows-only"
        else:
            features, verification = "Windows controls", "Per-control Windows capability detection"
        rows.append(f"| {name} | {features} | {verification} |")
    rows += ["", "All rows retain access to the Windows controls available on that machine. A missing or incompatible firmware provider is reported in the device panel; no substitute firmware command is sent.", ""]
    return "\n".join(rows)
