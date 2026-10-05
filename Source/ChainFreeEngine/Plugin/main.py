"""Playhub control center. Hardware controls retain the Quick Settings MIT notices."""
import json
import logging
import os
import shutil
import threading
import importlib.util
import sys
import ctypes
import platform
import asyncio
import time
import uuid
import base64
import random
import socket
import struct
import urllib.parse
import urllib.request
from ctypes import wintypes

_spec = importlib.util.spec_from_file_location("playhub_quick_settings", os.path.join(os.path.dirname(__file__), "quick_settings", "main.py"))
_runtime = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = _runtime
_spec.loader.exec_module(_runtime)
QuickSettingsPlugin = _runtime.Plugin
_qam_spec = importlib.util.spec_from_file_location("playhub_qam_preferences", os.path.join(os.path.dirname(__file__), "quick_settings", "qam_preferences.py"))
_qam_module = importlib.util.module_from_spec(_qam_spec)
_qam_spec.loader.exec_module(_qam_module)


# Retrying the restore forever re-toggles the panel every 30 s, which on an
# HDMI 2.1 sink is indistinguishable from a dead output. Give up re-arming
# after this many attempts and keep the transaction visible instead.
_DISPLAY_RECOVERY_ATTEMPTS = 8
_TOPBAR_CEF_PORT = 8080
_TOPBAR_DATE_BADGE_ID = "playhub-topbar-date"
_TOPBAR_DATE_STYLE_ID = "playhub-topbar-date-style"
_TOPBAR_CLOCK_SELECTORS = ["#header ._1HhLUvHH6BZLIOyOE80TVh", "._1HhLUvHH6BZLIOyOE80TVh", '#header [class*="Clock"]', "#header time"]


class Plugin(QuickSettingsPlugin):
    def __init__(self):
        super().__init__()
        # News is optional: its failure must never take down device controls.
        self._home_news = None
        try:
            news_spec = importlib.util.spec_from_file_location("playhub_home_news", os.path.join(os.path.dirname(__file__), "quick_settings", "home_news.py"))
            news_module = importlib.util.module_from_spec(news_spec)
            news_spec.loader.exec_module(news_module)
            self._home_news = news_module.HomeNews(self._settings_dir)
        except Exception:
            logging.exception("Playhub News unavailable; core controls remain active")
        self._daily_history = None
        try:
            history_spec = importlib.util.spec_from_file_location("playhub_daily_history", os.path.join(os.path.dirname(__file__), "quick_settings", "daily_history.py"))
            history_module = importlib.util.module_from_spec(history_spec)
            history_spec.loader.exec_module(history_module)
            self._daily_history = history_module.DailyHistory(self._settings_dir)
        except Exception:
            logging.exception("Playhub Daily History unavailable; core controls remain active")
        self._panel_lock = threading.RLock()
        self._qam_preferences = _qam_module.QamPreferences(self._settings_dir)
        self._panel_path = os.path.join(self._settings_dir, "playhub-panel.json")
        self._display_lock = threading.RLock()
        self._display_pending = None
        self._display_timer = None
        self._display_results = {}
        self._display_journal = os.path.join(self._settings_dir, "display-recovery.json")
        self._display_recovered = False
        self._display_recovery_error = None
        # Motivo di un journal messo da parte: va mostrato, non trasformato in un blocco.
        self._display_recovery_note = None
        self._display_stopping = False
        self._topbar_date_task = None
        self._topbar_date_stopping = False
        # One-time migration; never replace profiles already saved in Playhub.
        if not os.path.exists(self._settings_path):
            root = os.path.dirname(self._settings_dir)
            for name in ("quick-settings", "Quick Settings"):
                previous = os.path.join(root, name, "quick-settings-2.3.json")
                if os.path.isfile(previous):
                    os.makedirs(self._settings_dir, exist_ok=True)
                    shutil.copy2(previous, self._settings_path)
                    self._settings = self._load_plugin_settings()
                    break

    async def get_daily_history_settings(self):
        if self._daily_history is None: raise RuntimeError("Daily history unavailable")
        return await asyncio.to_thread(self._daily_history.settings)

    async def get_qam_preferences(self):
        return await asyncio.to_thread(self._qam_preferences.get)

    async def set_qam_preferences(self, preferences):
        return await asyncio.to_thread(self._qam_preferences.save, preferences)

    async def set_daily_history_settings(self, enabled):
        if self._daily_history is None: raise RuntimeError("Daily history unavailable")
        return await asyncio.to_thread(self._daily_history.save, enabled)

    async def get_history_wikipedia_url(self, theme_id, locale="en"):
        if self._daily_history is None: raise RuntimeError("Daily history unavailable")
        return await asyncio.to_thread(self._daily_history.wikipedia_url, theme_id, locale)

    async def get_daily_history(self, locale="en", offset=0):
        if self._daily_history is None:
            raise RuntimeError("Daily history unavailable")
        return await asyncio.to_thread(self._daily_history.get, locale, offset)

    async def get_home_news_settings(self):
        if self._home_news is None:
            raise RuntimeError("News unavailable; see Playhub log")
        return await asyncio.to_thread(self._home_news.settings)

    async def set_home_news_settings(self, enabled, country):
        if self._home_news is None:
            raise RuntimeError("News unavailable; see Playhub log")
        return await asyncio.to_thread(self._home_news.save, enabled, country)

    async def get_home_news(self, locale="en"):
        if self._home_news is None:
            raise RuntimeError("News unavailable; see Playhub log")
        return await asyncio.to_thread(self._home_news.get, locale)

    def _save_display_recovery(self, record=None):
        os.makedirs(self._settings_dir, exist_ok=True)
        temporary = self._display_journal + ".tmp"
        with open(temporary, "w", encoding="utf-8") as handle:
            json.dump(record if record is not None else self._display_pending, handle)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, self._display_journal)

    def _complete_display(self, token, kept):
        result = {"ok": True, "terminal": True, "kept": kept,
                  "expired": bool(self._display_pending.get("expired"))}
        # Commit the decision before releasing ownership. Startup never undoes a
        # confirmed transaction, even if the renderer lost the RPC response.
        self._save_display_recovery({"state": "completed", "token": token, "result": result})
        if self._display_timer:
            self._display_timer.cancel()
        self._display_timer = None
        self._display_pending = None
        self._display_results[token] = result
        while len(self._display_results) > 16:
            del self._display_results[next(iter(self._display_results))]
        return result

    def _arm_display_timer(self, seconds, token):
        if self._display_timer:
            self._display_timer.cancel()
        if not self._display_stopping:
            self._display_timer = threading.Timer(seconds, self._revert_display, args=(token,))
            self._display_timer.daemon = True
            self._display_timer.start()

    @staticmethod
    def _valid_display_mode(mode):
        return isinstance(mode, dict) and all(type(mode.get(k)) is int and mode[k] > 0
                                              for k in ("width", "height", "hz"))

    def _recover_display_sync(self):
        with self._display_lock:
            if self._display_recovered:
                return
            self._display_recovered = True
            try:
                with open(self._display_journal, encoding="utf-8") as handle:
                    pending = json.load(handle)
                if not isinstance(pending, dict) or not isinstance(pending.get("token"), str) or not pending["token"]:
                    raise ValueError("Invalid display recovery record")
                if pending.get("state") == "completed":
                    result = pending.get("result", {})
                    if not isinstance(result, dict) or result.get("ok") is not True or result.get("terminal") is not True or type(result.get("kept")) is not bool:
                        raise ValueError("Invalid display recovery result")
                    self._display_results[pending["token"]] = result
                    return
                previous = pending.get("previous")
                if pending.get("kind") == "display":
                    valid = (self._valid_display_mode(previous) and isinstance(pending.get("device"), str)
                             and bool(pending["device"]) and (not pending.get("persistence_attempted")
                             or self._valid_display_mode(pending.get("registered"))))
                else:
                    valid = (pending.get("kind") == "hdr" and isinstance(previous, dict) and bool(previous)
                             and all(isinstance(k, str) and k and type(v) is bool for k, v in previous.items()))
                if (not valid or pending.get("state") not in ("applying", "previewing", "confirming", "reverting", "recovery_required")
                        or type(pending.get("retries", 0)) is not int or pending.get("retries", 0) < 0):
                    raise ValueError("Invalid display recovery snapshot")
                self._display_pending = pending
                self._revert_display(pending["token"])
            except FileNotFoundError:
                pass
            except (ValueError, TypeError) as error:
                # Un journal incoerente e' esattamente quello che resta dopo uno
                # spegnimento forzato a meta' scrittura. Bloccare per sempre ogni
                # cambio schermo e' peggio del difetto: lo si mette da parte, si
                # conserva il motivo e si riparte.
                self._display_recovery_note = str(error)
                self._display_pending = None
                self._quarantine_display_journal()
            except OSError as error:
                self._display_recovery_error = str(error)

    def _quarantine_display_journal(self):
        """Sposta di lato un journal inutilizzabile invece di riprovarlo all'infinito."""
        for attempt in (lambda: os.replace(self._display_journal, self._display_journal + ".corrupt"),
                        lambda: os.remove(self._display_journal)):
            try:
                attempt()
                return
            except OSError:
                continue

    def _revert_display(self, token):
        with self._display_lock:
            pending = self._display_pending
            if not pending or pending["token"] != token:
                return self._display_results.get(token, {"ok": False, "terminal": False, "expired": True})
            if self._display_timer:
                self._display_timer.cancel()
                self._display_timer = None
            previous = pending["previous"]
            if pending["state"] == "previewing" and time.monotonic() >= pending.get("deadline", 0):
                pending["expired"] = True
            pending["state"] = "reverting"
            try:
                if pending["kind"] == "hdr":
                    _runtime._set_advanced_color_states(states=previous)
                    verified, hdr = _runtime._wait_for_hdr_states(previous)
                    result = {"ok": verified, "hdr": hdr}
                else:
                    registry_ok = True
                    if pending.get("persistence_attempted"):
                        mode = pending["registered"]
                        registry_ok = _runtime._set_display_mode_sync(mode["width"], mode["height"], mode["hz"], True, pending["device"]).get("ok", False)
                        registry_ok = registry_ok and _runtime._get_registered_display_mode_sync(pending["device"]) == mode
                    result = _runtime._set_display_mode_sync(previous["width"], previous["height"], previous["hz"], False, pending.get("device"))
                    actual = _runtime._get_display_status_sync(pending["device"]).get("current")
                    result["ok"] = bool(result.get("ok") and registry_ok and actual == previous)
                if result.get("ok"):
                    return {**result, **self._complete_display(token, False)}
            except Exception as error:
                result = {"ok": False, "message": str(error)}
            pending["state"] = "recovery_required"
            pending["retries"] = pending.get("retries", 0) + 1
            try:
                self._save_display_recovery()
            except OSError as error:
                result["message"] = str(error)
            if pending["retries"] <= _DISPLAY_RECOVERY_ATTEMPTS:
                self._arm_display_timer(2 if pending["retries"] <= 3 else 30, token)
            return {**result, "ok": False, "terminal": False, "state": "recovery_required",
                    "token": token, "attempts": pending["retries"],
                    "exhausted": pending["retries"] > _DISPLAY_RECOVERY_ATTEMPTS}

    def _hdr_write_allowed(self):
        """Require a real readable HDR target; no persistent development opt-in."""
        try:
            status = _runtime._get_hdr_status_sync()
            targets = [target for target in status.get("targets", []) if target.get("supported")]
            return bool(status.get("real_state") and status.get("available") and targets
                        and all(not target.get("force_disabled") for target in targets))
        except Exception:
            return False

    def _begin_display_change(self, request):
        with self._display_lock:
            if self._display_stopping:
                return {"ok": False, "state": "stopping"}
            self._recover_display_sync()
            if self._display_recovery_error:
                return {"ok": False, "state": "recovery_required", "message": self._display_recovery_error}
            pending = self._display_pending
            if pending:
                if pending.get("retries", 0) > _DISPLAY_RECOVERY_ATTEMPTS:
                    # Il ripristino ha smesso di provarci: tenere bloccato l'utente non
                    # ripara niente e gli toglie l'unico modo di rimettere a posto lo schermo.
                    self._display_recovery_note = "display_recovery_exhausted"
                    self._display_pending = None
                    self._quarantine_display_journal()
                else:
                    return {"ok": False, "busy": True}
            logging.info("Playhub display begin request=%s", request)
            kind = request.get("kind")
            device = None
            if kind not in ("hdr", "display"):
                raise ValueError("Invalid display change")
            if kind == "hdr":
                if type(request.get("enabled")) is not bool:
                    raise ValueError("Invalid HDR value")
                if not self._hdr_write_allowed():
                    return {"ok": False, "code": "hdr_write_disabled",
                            "message": "Windows does not expose an available, readable HDR display."}
                status = _runtime._get_hdr_status_sync()
                if not status.get("available"):
                    return {"ok": False, "unavailable": True}
                previous = _runtime._hdr_target_states(status)
                if not previous:
                    return {"ok": False, "unavailable": True}
                desired = {key: request["enabled"] for key in previous}
            else:
                device = _runtime._primary_display_name()
                if not device:
                    return {"ok": False, "unavailable": True}
                previous = _runtime._get_display_status_sync(device).get("current")
                if not self._valid_display_mode(previous):
                    return {"ok": False, "unavailable": True}
                desired = {key: request.get(key, previous[key]) for key in ("width", "height", "hz")}
                if not self._valid_display_mode(desired):
                    raise ValueError("Invalid display mode")
                # A resolution-only request carries the current refresh rate and a
                # refresh-only request the current resolution. Windows rejects the
                # pair outright when the panel has no such mode, which used to look
                # like "the dropdown does nothing".
                resolved, message, code = _runtime._resolve_display_mode_sync(desired, device)
                if not self._valid_display_mode(resolved):
                    return {"ok": False, "code": code or "display_mode_unsupported",
                            "message": message or "The display does not support the requested mode."}
                desired = resolved
                registered = _runtime._get_registered_display_mode_sync(device)
                if not self._valid_display_mode(registered):
                    return {"ok": False, "unavailable": True}
            token = uuid.uuid4().hex
            self._display_pending = {"token": token, "kind": kind, "previous": previous, "desired": desired,
                                     "device": device, "state": "applying"}
            if kind == "display":
                self._display_pending["registered"] = registered
            try:
                self._save_display_recovery()
            except OSError as error:
                self._display_pending = None
                return {"ok": False, "message": str(error)}
            try:
                if kind == "hdr":
                    write = _runtime._set_advanced_color_states(states=desired)
                    verified, hdr = _runtime._wait_for_hdr_states(desired)
                    # Every target accepted the write: the sink is still bringing
                    # the link back up. Reverting now would renegotiate on top of
                    # a renegotiation and strand the panel on "no signal"; the
                    # confirmation timer is what undoes a change nobody can see.
                    accepted = bool(write.get("all_accepted"))
                    result = {"ok": verified or accepted, "verified": verified,
                              "write": write, "hdr": hdr}
                    if not result["ok"]:
                        result["message"] = write.get("message") or "Windows rejected the HDR request."
                else:
                    result = _runtime._set_display_mode_sync(desired["width"], desired["height"], desired["hz"], False, device)
                    result["ok"] = bool(result.get("ok") and _runtime._get_display_status_sync(device).get("current") == desired)
                if not result.get("ok"):
                    return {"ok": False, "code": "display_change_rejected",
                            "message": result.get("message", ""),
                            "rollback": self._revert_display(token)}
            except Exception as error:
                return {"ok": False, "message": str(error), "rollback": self._revert_display(token)}
            # The rollback belongs to the backend, not the renderer showing the dialog.
            preview_seconds = 30 if kind == "hdr" else 15
            expires = time.time() + preview_seconds
            self._display_pending["expires"] = expires
            self._display_pending["deadline"] = time.monotonic() + preview_seconds
            self._display_pending["state"] = "previewing"
            try:
                self._save_display_recovery()
                self._arm_display_timer(preview_seconds, token)
            except Exception as error:
                return {"ok": False, "message": str(error), "rollback": self._revert_display(token)}
            return {"ok": True, "token": token, "expiresAt": expires * 1000, "secondsRemaining": preview_seconds, "result": result}

    async def begin_display_change(self, request):
        if not isinstance(request, dict):
            raise ValueError("Invalid display request")
        return await asyncio.get_running_loop().run_in_executor(None, self._begin_display_change, request)

    def _finish_display_change(self, request):
        if not isinstance(request, dict) or not isinstance(request.get("token"), str) or type(request.get("keep")) is not bool:
            raise ValueError("Invalid display confirmation")
        self._recover_display_sync()
        token = request.get("token")
        logging.info("Playhub display confirmation token=%s keep=%s", token, request.get("keep"))
        if not request.get("keep"):
            return self._revert_display(token)
        with self._display_lock:
            pending = self._display_pending
            if not pending or pending["token"] != token:
                return self._display_results.get(token, {"ok": False, "terminal": False, "expired": True})
            if pending["state"] != "previewing" or time.monotonic() >= pending.get("deadline", 0):
                pending["expired"] = True
                return self._revert_display(token)
            pending["state"] = "confirming"
            try:
                mode = pending["desired"]
                if pending["kind"] == "display":
                    if _runtime._get_display_status_sync(pending["device"]).get("current") != mode:
                        return self._revert_display(token)
                    pending["persistence_attempted"] = True
                    self._save_display_recovery()
                    result = _runtime._set_display_mode_sync(mode["width"], mode["height"], mode["hz"], True, pending["device"])
                    verified = (result.get("ok") and _runtime._get_display_status_sync(pending["device"]).get("current") == mode
                                and _runtime._get_registered_display_mode_sync(pending["device"]) == mode)
                else:
                    verified, _ = _runtime._wait_for_hdr_states(mode)
                if not verified:
                    return self._revert_display(token)
                return self._complete_display(token, True)
            except Exception as error:
                return {**self._revert_display(token), "message": str(error)}

    async def finish_display_change(self, request):
        return await asyncio.get_running_loop().run_in_executor(None, self._finish_display_change, request)

    def _get_display_change_sync(self, token=None):
        with self._display_lock:
            self._recover_display_sync()
            if self._display_recovery_error:
                return {"state": "recovery_required", "message": self._display_recovery_error}
            if token and token in self._display_results:
                return {"state": "completed", "token": token, **self._display_results[token]}
            pending = self._display_pending
            if not pending:
                return {"state": "idle"}
            return {"state": pending["state"], "token": pending["token"],
                    "secondsRemaining": max(0, pending.get("deadline", 0) - time.monotonic())}

    async def get_display_change(self, request=None):
        token = request.get("token") if isinstance(request, dict) else None
        if token is not None and not isinstance(token, str):
            raise ValueError("Invalid display token")
        return await asyncio.get_running_loop().run_in_executor(None, self._get_display_change_sync, token)

    # Public legacy RPCs must not bypass the preview/confirmation coordinator.
    async def set_display_mode(self, request):
        return {"ok": False, "code": "display_transaction_required"}

    async def set_refresh_rate(self, request):
        return {"ok": False, "code": "display_transaction_required"}

    async def set_hdr_enabled(self, request):
        return {"ok": False, "code": "display_transaction_required"}

    async def dump_display_diagnostics(self, request=None):
        """Fotografia di SOLA LETTURA del sottosistema schermo, scritta su file.

        Nessuna scrittura all'hardware. Serve a sostituire le ipotesi con i fatti
        dopo la perdita di segnale: quanti percorsi attivi ci sono, quali target
        dichiarano il colore avanzato, cosa risponde ognuno e cosa avremmo scritto.
        """
        loop = asyncio.get_running_loop()
        return await loop.run_in_executor(None, self._dump_display_diagnostics_sync)

    def _dump_display_diagnostics_sync(self):
        report = {"schema_version": 1, "written_at": time.time(), "hardware_written": False}
        for name, probe in (
            ("hdr_status", lambda: _runtime._get_hdr_status_sync()),
            ("primary_display", lambda: _runtime._primary_display_name()),
        ):
            try:
                report[name] = probe()
            except Exception as error:
                report[name] = {"error": "%s: %s" % (type(error).__name__, error)}
        try:
            device = report.get("primary_display")
            report["display_status"] = _runtime._get_display_status_sync(device) if device else None
            report["registered_mode"] = _runtime._get_registered_display_mode_sync(device) if device else None
            report["modes"] = _runtime._enum_display_modes(device) if device else None
        except Exception as error:
            report["display_status_error"] = "%s: %s" % (type(error).__name__, error)
        # I bersagli su cui una scrittura HDR andrebbe davvero: se sono piu' del
        # pannello acceso, e' li' che si perde il segnale.
        try:
            report["hdr_write_targets"] = _runtime._hdr_target_states(report.get("hdr_status") or {})
        except Exception as error:
            report["hdr_write_targets_error"] = "%s: %s" % (type(error).__name__, error)
        report["hdr_write_enabled"] = self._hdr_write_allowed()
        report["recovery_note"] = self._display_recovery_note
        report["recovery_error"] = self._display_recovery_error
        path = os.path.join(self._settings_dir, "display-diagnostics.json")
        try:
            with open(path, "w", encoding="utf-8") as handle:
                json.dump(report, handle, indent=2, default=str)
        except OSError as error:
            return {"ok": False, "message": "%s: %s" % (type(error).__name__, error), "report": report}
        return {"ok": True, "path": path, "report": report}

    async def _main(self):
        logging.getLogger(__name__).info("Playhub backend starting pid=%s", os.getpid())
        await asyncio.get_running_loop().run_in_executor(None, self._recover_display_sync)
        logging.getLogger(__name__).info("Playhub display recovery checked pid=%s pending=%s error=%s",
                                       os.getpid(), bool(self._display_pending), bool(self._display_recovery_error))
        self._topbar_date_stopping = False
        self._topbar_date_task = asyncio.create_task(self._topbar_date_loop())
        await super()._main()

    def _install_circles_screensaver(self):
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam") as key:
                root = winreg.QueryValueEx(key, "SteamPath")[0]
            target = os.path.join(root, "config", "uioverrides", "screensavers", "playhub-circles")
            os.makedirs(target, exist_ok=True)
            self._screensaver_asset_dir = target
            for name in ("index.html", "main.js"):
                source = os.path.join(os.path.dirname(__file__), "screensaver", name)
                temporary = os.path.join(target, name + ".tmp")
                shutil.copyfile(source, temporary)
                os.replace(temporary, os.path.join(target, name))
            return {"ok": True}
        except Exception as error:
            logging.warning("Playhub Circles registration failed: %s", error)
            return {"ok": False, "error": str(error)}

    async def install_circles_screensaver(self):
        return await asyncio.to_thread(self._install_circles_screensaver)

    async def cache_circles_font(self, data: str, appid: int = 0):
        return await asyncio.to_thread(self._cache_circles_font, data, appid)

    def _cache_circles_font(self, data, appid=0):
        try:
            if not isinstance(data, str) or len(data) > 3000000:
                return {"ok": False}
            raw = base64.b64decode(data, validate=True)
            appid = int(appid)
            if appid:
                if not 0 < appid < 2**32 or raw[:4] != b"RIFF" or raw[8:12] != b"WEBP" or len(raw) > 500000:
                    return {"ok": False}
                name = f"logo-{appid}.webp"
            else:
                if raw[:4] not in (b"\x00\x01\x00\x00", b"OTTO", b"wOFF", b"wOF2"):
                    return {"ok": False}
                name = "steam-ui-font.ttf"
            target = getattr(self, "_screensaver_asset_dir", None)
            if not target:
                return {"ok": False}
            destination = os.path.join(target, name)
            temporary = destination + ".tmp"
            with open(temporary, "wb") as file:
                file.write(raw)
            os.replace(temporary, destination)
            return {"ok": True, "path": name}
        except (ValueError, TypeError, OSError):
            return {"ok": False}

    async def sync_circles_screensaver(self, state):
        return await asyncio.to_thread(self._sync_circles_screensaver, state)

    def _sync_circles_screensaver(self, state):
        if not isinstance(state, dict) or len(json.dumps(state)) > 16000:
            return {"ok": False}
        # Target only the registered native screensaver, never other browser pages.
        with urllib.request.urlopen("http://127.0.0.1:8080/json", timeout=3) as response:
            targets = json.loads(response.read().decode("utf-8"))
        script = "window.__playhubCircles?.update(" + json.dumps(state) + ")"
        for target in targets:
            if urllib.parse.urlparse(target.get("url", "")).hostname == "uioverride-playhub-circles.steamscreensavers.host" and target.get("webSocketDebuggerUrl"):
                self._eval_date_target(target["webSocketDebuggerUrl"], script)
        return {"ok": True}

    def _stop_display_sync(self):
        # Wait for an in-flight begin/finish before deciding what needs recovery.
        with self._display_lock:
            self._display_stopping = True
            if self._display_timer:
                self._display_timer.cancel()
                self._display_timer = None
            if self._display_pending:
                self._revert_display(self._display_pending["token"])

    async def _unload(self):
        logging.getLogger(__name__).info("Playhub backend unloading pid=%s", os.getpid())
        await asyncio.get_running_loop().run_in_executor(None, self._stop_display_sync)
        self._topbar_date_stopping = True
        if self._topbar_date_task:
            self._topbar_date_task.cancel()
            self._topbar_date_task = None
        await super()._unload()
        logging.getLogger(__name__).info("Playhub backend unloaded pid=%s", os.getpid())

    async def set_microphone_volume(self, request):
        return await super().set_microphone_volume(request)


    async def get_power_status(self):
        class PowerStatus(ctypes.Structure):
            _fields_ = [("ac", wintypes.BYTE), ("flags", wintypes.BYTE),
                        ("percent", wintypes.BYTE), ("saving", wintypes.BYTE),
                        ("remaining", wintypes.DWORD), ("full", wintypes.DWORD)]
        status = PowerStatus()
        if os.name != "nt" or not ctypes.windll.kernel32.GetSystemPowerStatus(ctypes.byref(status)):
            return {"available": False}
        return {"available": True, "connected": status.ac == 1,
                "battery": status.flags != 255 and not bool(status.flags & 128),
                "percent": status.percent if status.percent <= 100 else None}

    async def get_device_info(self):
        return await asyncio.get_running_loop().run_in_executor(None, self._get_device_info_sync)

    @staticmethod
    def _get_windows_device_details():
        script = r'''
$ErrorActionPreference = 'Stop'
$result = @{}
try { $result.device_name = (Get-CimInstance Win32_ComputerSystem).Name } catch {}
try { $result.gpu = @((Get-CimInstance Win32_VideoController).Name | Sort-Object -Unique) } catch {}
try { $result.storage_total_bytes = (Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Measure-Object -Property Size -Sum).Sum } catch {}
try { $os = Get-CimInstance Win32_OperatingSystem; $result.windows_edition = $os.Caption; $result.windows_version = $os.Version } catch {}
$result | ConvertTo-Json -Compress
'''
        code, output, _ = _runtime._run_cmd([_runtime._powershell_path(), "-NoProfile", "-NonInteractive", "-Command", script], timeout=15)
        if code != 0:
            return {}
        try:
            raw = json.loads(output.lstrip("\ufeff"))
            if not isinstance(raw, dict):
                return {}
            details = {key: raw[key].strip() for key in ("device_name", "windows_edition", "windows_version")
                       if isinstance(raw.get(key), str) and raw[key].strip()}
            gpus = raw.get("gpu", [])
            if isinstance(gpus, str):
                gpus = [gpus]
            details["gpu"] = list(dict.fromkeys(g.strip() for g in gpus if isinstance(g, str) and g.strip())) if isinstance(gpus, list) else []
            total = raw.get("storage_total_bytes")
            if type(total) in (int, float) and 0 < total < 2 ** 63:
                details["storage_total_bytes"] = int(total)
            return details
        except (ValueError, TypeError, OverflowError):
            return {}

    def _get_device_info_sync(self):
        info = {"os": platform.platform(), "architecture": platform.machine(), "windows_update_available": os.name == "nt"}
        if os.name != "nt":
            return info
        import winreg
        for key, path, name in (
            ("model", r"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName"),
            ("manufacturer", r"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer"),
            ("cpu", r"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString"),
        ):
            try:
                with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, path) as handle:
                    info[key] = str(winreg.QueryValueEx(handle, name)[0]).strip()
            except OSError:
                pass
        class MemoryStatus(ctypes.Structure):
            _fields_ = [("length", wintypes.DWORD), ("load", wintypes.DWORD)] + [
                (name, ctypes.c_ulonglong) for name in ("total", "available", "page_total", "page_available", "virtual_total", "virtual_available", "extended")]
        memory = MemoryStatus()
        memory.length = ctypes.sizeof(memory)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(memory)):
            info["ram"] = round(memory.total / (1024 ** 3), 1)
        info.update(self._get_windows_device_details())
        return info

    def _windows_update_foreground(self, user32=None, kernel32=None, clock=None, sleep=None):
        clock = clock or time.monotonic
        sleep = sleep or time.sleep
        callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
        if user32 is None:
            user32 = ctypes.WinDLL("user32", use_last_error=True)
            kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
            signatures = [
                (user32, "EnumWindows", [callback_type, wintypes.LPARAM], wintypes.BOOL),
                (user32, "EnumChildWindows", [wintypes.HWND, callback_type, wintypes.LPARAM], wintypes.BOOL),
                (user32, "IsWindowVisible", [wintypes.HWND], wintypes.BOOL),
                (user32, "IsIconic", [wintypes.HWND], wintypes.BOOL),
                (user32, "GetWindowThreadProcessId", [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)], wintypes.DWORD),
                (user32, "GetForegroundWindow", [], wintypes.HWND),
                (user32, "ShowWindowAsync", [wintypes.HWND, ctypes.c_int], wintypes.BOOL),
                (user32, "BringWindowToTop", [wintypes.HWND], wintypes.BOOL),
                (user32, "SetForegroundWindow", [wintypes.HWND], wintypes.BOOL),
                (user32, "SwitchToThisWindow", [wintypes.HWND, wintypes.BOOL], None),
                (user32, "AttachThreadInput", [wintypes.DWORD, wintypes.DWORD, wintypes.BOOL], wintypes.BOOL),
                (user32, "PeekMessageW", [ctypes.POINTER(wintypes.MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT, wintypes.UINT], wintypes.BOOL),
                (kernel32, "GetCurrentThreadId", [], wintypes.DWORD),
                (kernel32, "OpenProcess", [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD], wintypes.HANDLE),
                (kernel32, "QueryFullProcessImageNameW", [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)], wintypes.BOOL),
                (kernel32, "CloseHandle", [wintypes.HANDLE], wintypes.BOOL),
                (kernel32, "GetWindowsDirectoryW", [wintypes.LPWSTR, wintypes.UINT], wintypes.UINT),
            ]
            for dll, name, args, result in signatures:
                function = getattr(dll, name)
                function.argtypes, function.restype = args, result
        windows = ctypes.create_unicode_buffer(32768)
        size = kernel32.GetWindowsDirectoryW(windows, len(windows))
        if not size or size >= len(windows):
            return False
        settings_path = os.path.normcase(os.path.join(windows.value, "ImmersiveControlPanel", "SystemSettings.exe"))
        frame_path = os.path.normcase(os.path.join(windows.value, "System32", "ApplicationFrameHost.exe"))

        def image_path(hwnd):
            pid = wintypes.DWORD()
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            handle = kernel32.OpenProcess(0x1000, False, pid.value)
            if not handle:
                return None
            try:
                buffer = ctypes.create_unicode_buffer(32768)
                length = wintypes.DWORD(len(buffer))
                if kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(length)):
                    return os.path.normcase(buffer.value)
            finally:
                kernel32.CloseHandle(handle)
            return None

        def is_settings(hwnd):
            path = image_path(hwnd)
            if path == settings_path:
                return True
            if path != frame_path:
                return False
            found = []
            @callback_type
            def child(window, _):
                if image_path(window) == settings_path:
                    found.append(window)
                return True
            user32.EnumChildWindows(hwnd, child, 0)
            return bool(found)

        deadline = clock() + 5.0
        while clock() < deadline:
            candidates = []
            @callback_type
            def collect(hwnd, _):
                if user32.IsWindowVisible(hwnd) and is_settings(hwnd):
                    candidates.append(hwnd)
                return True
            user32.EnumWindows(collect, 0)
            for hwnd in candidates:
                # Revalidate after enumeration; never activate a recycled/foreign HWND.
                if not is_settings(hwnd):
                    continue
                if user32.GetForegroundWindow() == hwnd:
                    return True
                if user32.IsIconic(hwnd):
                    user32.ShowWindowAsync(hwnd, 9)
                message = wintypes.MSG()
                user32.PeekMessageW(ctypes.byref(message), None, 0, 0, 0)
                current = kernel32.GetCurrentThreadId()
                threads = {user32.GetWindowThreadProcessId(hwnd, None),
                           user32.GetWindowThreadProcessId(user32.GetForegroundWindow(), None)} - {0, current}
                attached = []
                try:
                    for thread in threads:
                        if user32.AttachThreadInput(current, thread, True):
                            attached.append(thread)
                    if not is_settings(hwnd):
                        continue
                    user32.BringWindowToTop(hwnd)
                    if not is_settings(hwnd):
                        continue
                    user32.SetForegroundWindow(hwnd)
                    if not is_settings(hwnd):
                        continue
                    if user32.GetForegroundWindow() != hwnd:
                        user32.SwitchToThisWindow(hwnd, True)
                    if user32.GetForegroundWindow() == hwnd and is_settings(hwnd):
                        return True
                finally:
                    for thread in reversed(attached):
                        user32.AttachThreadInput(current, thread, False)
            sleep(min(0.2, max(0, deadline - clock())))
        return False

    async def open_windows_update(self):
        if os.name != "nt":
            return {"ok": False, "code": "windows_only"}
        try:
            await asyncio.get_running_loop().run_in_executor(None, os.startfile, "ms-settings:windowsupdate")
        except OSError:
            return {"ok": False, "code": "open_failed"}
        try:
            foreground = await asyncio.get_running_loop().run_in_executor(None, self._windows_update_foreground)
            return {"ok": True} if foreground else {"ok": False, "code": "foreground_failed"}
        except OSError:
            return {"ok": False, "code": "foreground_failed"}

    async def get_panel_preferences(self):
        with self._panel_lock:
            try:
                with open(self._panel_path, encoding="utf-8") as handle:
                    data = json.load(handle)
                return data if isinstance(data, dict) else {}
            except (OSError, ValueError):
                return {}

    async def save_panel_preferences(self, preferences):
        if not isinstance(preferences, dict):
            raise ValueError("Invalid panel preferences")
        allowed = ("home", "store", "audio", "performance", "graphics", "controller", "decky")
        order = list(dict.fromkeys(x for x in preferences.get("order", []) if x in allowed))
        order.extend(x for x in allowed if x not in order)
        hidden = list(dict.fromkeys(x for x in preferences.get("hidden", []) if x in allowed))
        decky_host_enabled = preferences.get("deckyHostEnabled", True) is True
        eligible = [x for x in allowed if x != "decky" or decky_host_enabled]
        if all(x in hidden for x in eligible):
            hidden.remove("home")
        active = preferences.get("active")
        if active not in eligible or active in hidden:
            active = next(x for x in order if x in eligible and x not in hidden)
        collapsed = [x for x in preferences.get("collapsed", []) if isinstance(x, str) and len(x) < 80][:64]
        date_formats = ("auto", "dd_mm_yyyy", "dd_mm_yy", "yyyy_mm_dd", "dd_month_yyyy",
                        "weekday_dd_month", "weekday_short_dd_month", "month_dd_yyyy",
                        "month_short_dd_yyyy", "iso")
        date_format = preferences.get("topbarDateFormat", "auto")
        if date_format not in date_formats:
            date_format = "auto"
        data = dict(order=order, hidden=hidden, active=active, collapsed=collapsed,
                    deckyHostEnabled=decky_host_enabled,
                    topbarDateEnabled=preferences.get("topbarDateEnabled", True) is True,
                    topbarDateFormat=date_format, topbarClockLeft=preferences.get("topbarClockLeft", False) is True)
        with self._panel_lock:
            os.makedirs(self._settings_dir, exist_ok=True)
            temporary = self._panel_path + ".tmp"
            with open(temporary, "w", encoding="utf-8") as handle:
                json.dump(data, handle, ensure_ascii=False)
                handle.flush()
                os.fsync(handle.fileno())
            os.replace(temporary, self._panel_path)
        return data

    def _topbar_date_script(self, enabled, date_format, move_left=False):
        cfg = json.dumps({"enabled": bool(enabled), "format": date_format or "auto", "moveLeft": bool(move_left),
                          "badge": _TOPBAR_DATE_BADGE_ID, "style": _TOPBAR_DATE_STYLE_ID,
                          "selectors": _TOPBAR_CLOCK_SELECTORS}, ensure_ascii=False)
        return ("(function(){var C=" + cfg + ";function remove(){var n=document.getElementById(C.badge);if(n)n.remove();var s=document.getElementById(C.style);if(s)s.remove();}"
                "var roots=Array.prototype.slice.call(document.querySelectorAll('#header,._1E_SL1bTibeQ3PQRBZoS_-,[class*=GamepadHeader],[class*=HeaderStatus],[class*=TopBar]'));"
                "function inside(n){for(var i=0;i<roots.length;i++)if(roots[i]===n||roots[i].contains(n))return true;return false;}var clock=null;"
                "for(var i=0;i<C.selectors.length;i++){var q=document.querySelector(C.selectors[i]);if(q&&inside(q)){clock=q;break;}}"
                "if(!clock){for(var r=0;r<roots.length&&!clock;r++){var ns=roots[r].querySelectorAll('div,span');for(var j=0;j<ns.length;j++){if(/^\\d{1,2}:\\d{2}(\\s|$)/.test((ns[j].textContent||'').trim())){clock=ns[j];break;}}}}"
                "if(!clock){remove();return;}clock.toggleAttribute('data-playhub-clock-left',C.moveLeft);var ps=document.getElementById('playhub-clock-position-style');if(!ps){ps=document.createElement('style');ps.id='playhub-clock-position-style';document.head.appendChild(ps);}ps.textContent='[data-playhub-clock-left]{order:-2!important;}';if(!C.enabled){remove();return;}var d=new Date(),pad=function(v){return String(v).padStart(2,'0')},value;"
                "if(C.format==='iso')value=d.getFullYear()+'-'+pad(d.getMonth()+1)+'-'+pad(d.getDate());else{var o={};"
                "if(C.format==='dd_mm_yyyy')o={day:'2-digit',month:'2-digit',year:'numeric'};else if(C.format==='dd_mm_yy')o={day:'2-digit',month:'2-digit',year:'2-digit'};"
                "else if(C.format==='yyyy_mm_dd')o={year:'numeric',month:'2-digit',day:'2-digit'};else if(C.format==='dd_month_yyyy')o={day:'numeric',month:'long',year:'numeric'};"
                "else if(C.format==='weekday_dd_month')o={weekday:'long',day:'numeric',month:'long'};else if(C.format==='weekday_short_dd_month')o={weekday:'short',day:'numeric',month:'short'};"
                "else if(C.format==='month_dd_yyyy')o={month:'long',day:'numeric',year:'numeric'};else if(C.format==='month_short_dd_yyyy')o={month:'short',day:'numeric',year:'numeric'};else o={weekday:'short',day:'numeric',month:'short'};"
                "try{value=new Intl.DateTimeFormat(navigator.language||undefined,o).formatToParts(d).map(function(p){return p.type==='weekday'||p.type==='month'?p.value.charAt(0).toLocaleUpperCase(navigator.language)+p.value.slice(1):p.value;}).join('');}catch(e){value=d.toLocaleDateString();}}"
                "var s=document.getElementById(C.style);if(!s){s=document.createElement('style');s.id=C.style;document.head.appendChild(s);}s.textContent='#'+C.badge+'{display:inline-flex;align-items:center;margin-left:.55em;opacity:.9;white-space:nowrap;font:inherit;line-height:inherit;color:#fff;pointer-events:none;vertical-align:baseline;align-self:baseline;}#'+C.badge+':has(+#decky-weather-topbar-badge){margin-right:.34em;}';"
                "var b=document.getElementById(C.badge);if(!b){b=document.createElement('span');b.id=C.badge;b.setAttribute('aria-hidden','true');}b.textContent=value;if(b.parentNode!==clock)clock.appendChild(b);var first=clock.firstElementChild;if(first!==b)clock.insertBefore(b,first);})();")

    def _steam_browser_targets_for_date(self):
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{_TOPBAR_CEF_PORT}/json/list", timeout=2) as response:
                targets = json.loads(response.read().decode("utf-8"))
        except Exception:
            return []
        return [t.get("webSocketDebuggerUrl") for t in targets if isinstance(t, dict) and t.get("webSocketDebuggerUrl") and (not t.get("type") or t.get("type") == "page")]

    @staticmethod
    def _websocket_date_frame(payload):
        frame = bytearray([0x81]); length = len(payload)
        if length < 126: frame.append(0x80 | length)
        elif length < 65536: frame.append(0x80 | 126); frame.extend(struct.pack("!H", length))
        else: frame.append(0x80 | 127); frame.extend(struct.pack("!Q", length))
        mask = os.urandom(4); frame.extend(mask); frame.extend(byte ^ mask[i % 4] for i, byte in enumerate(payload)); return bytes(frame)

    def _eval_date_target(self, websocket_url, script):
        try:
            parsed = urllib.parse.urlparse(websocket_url); host = parsed.hostname or "127.0.0.1"; port = parsed.port or _TOPBAR_CEF_PORT
            path = parsed.path or "/"; path += (("?" + parsed.query) if parsed.query else "")
            with socket.create_connection((host, port), timeout=3) as sock:
                sock.settimeout(3); key = base64.b64encode(os.urandom(16)).decode("ascii")
                sock.sendall((f"GET {path} HTTP/1.1\r\nHost: {host}:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode("ascii"))
                headers = b""
                while b"\r\n\r\n" not in headers and len(headers) < 8192: headers += sock.recv(1024)
                if b" 101 " not in headers.split(b"\r\n", 1)[0]: return False
                command = json.dumps({"id": random.randint(1, 2_000_000_000), "method":"Runtime.evaluate", "params":{"expression":script,"awaitPromise":False,"returnByValue":True}}, ensure_ascii=False).encode("utf-8")
                sock.sendall(self._websocket_date_frame(command)); return True
        except Exception:
            return False

    def _inject_topbar_date(self, enabled, date_format, move_left=False):
        script = self._topbar_date_script(enabled, date_format, move_left)
        for target in self._steam_browser_targets_for_date(): self._eval_date_target(target, script)

    async def _topbar_date_loop(self):
        while not self._topbar_date_stopping:
            try:
                prefs = await self.get_panel_preferences()
                await asyncio.to_thread(self._inject_topbar_date, prefs.get("topbarDateEnabled", True), prefs.get("topbarDateFormat", "auto"), prefs.get("topbarClockLeft", False))
            except Exception:
                logging.exception("Playhub topbar date refresh failed")
            await asyncio.sleep(1.5)
