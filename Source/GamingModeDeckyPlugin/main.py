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
import urllib.error
from ctypes import wintypes

_ipc_spec = importlib.util.spec_from_file_location("playhub_decky_ipc_health", os.path.join(os.path.dirname(__file__), "decky_ipc_health.py"))
_ipc_module = importlib.util.module_from_spec(_ipc_spec)
_ipc_spec.loader.exec_module(_ipc_module)

_spec = importlib.util.spec_from_file_location("playhub_quick_settings", os.path.join(os.path.dirname(__file__), "quick_settings", "main.py"))
_runtime = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = _runtime
_spec.loader.exec_module(_runtime)
QuickSettingsPlugin = _runtime.Plugin
_qam_spec = importlib.util.spec_from_file_location("playhub_qam_preferences", os.path.join(os.path.dirname(__file__), "quick_settings", "qam_preferences.py"))
_qam_module = importlib.util.module_from_spec(_qam_spec)
_qam_spec.loader.exec_module(_qam_module)
_device_spec = importlib.util.spec_from_file_location("playhub_device_profile", os.path.join(os.path.dirname(__file__), "quick_settings", "device_profile.py"))
_device_module = importlib.util.module_from_spec(_device_spec)
_device_spec.loader.exec_module(_device_module)
_device_capabilities_spec = importlib.util.spec_from_file_location("playhub_device_capabilities", os.path.join(os.path.dirname(__file__), "quick_settings", "device_capabilities.py"))
_device_capabilities_module = importlib.util.module_from_spec(_device_capabilities_spec)
_device_capabilities_spec.loader.exec_module(_device_capabilities_module)
_battery_spec = importlib.util.spec_from_file_location("playhub_battery_health", os.path.join(os.path.dirname(__file__), "quick_settings", "battery_health.py"))
_battery_module = importlib.util.module_from_spec(_battery_spec)
_battery_spec.loader.exec_module(_battery_module)
_boost_spec = importlib.util.spec_from_file_location("playhub_processor_boost", os.path.join(os.path.dirname(__file__), "quick_settings", "processor_boost.py"))
_boost_module = importlib.util.module_from_spec(_boost_spec)
_boost_spec.loader.exec_module(_boost_module)
_handheld_power_spec = importlib.util.spec_from_file_location("playhub_lenovo_power_mode", os.path.join(os.path.dirname(__file__), "quick_settings", "lenovo_power_mode.py"))
_handheld_power_module = importlib.util.module_from_spec(_handheld_power_spec)
_handheld_power_spec.loader.exec_module(_handheld_power_module)
_msi_firmware_spec = importlib.util.spec_from_file_location("playhub_msi_claw_firmware", os.path.join(os.path.dirname(__file__), "quick_settings", "msi_claw_firmware.py"))
_msi_firmware_module = importlib.util.module_from_spec(_msi_firmware_spec)
_msi_firmware_spec.loader.exec_module(_msi_firmware_module)
_asus_commands_spec = importlib.util.spec_from_file_location("playhub_asus_performance_commands", os.path.join(os.path.dirname(__file__), "quick_settings", "asus_performance_commands.py"))
_asus_commands_module = importlib.util.module_from_spec(_asus_commands_spec)
_asus_commands_spec.loader.exec_module(_asus_commands_module)
_asus_controls_spec = importlib.util.spec_from_file_location("playhub_asus_device_controls", os.path.join(os.path.dirname(__file__), "quick_settings", "asus_device_controls.py"))
_asus_controls_module = importlib.util.module_from_spec(_asus_controls_spec)
_asus_controls_spec.loader.exec_module(_asus_controls_module)
_handheld_tdp_spec = importlib.util.spec_from_file_location("playhub_asus_handheld_tdp", os.path.join(os.path.dirname(__file__), "quick_settings", "asus_handheld_tdp.py"))
_handheld_tdp_module = importlib.util.module_from_spec(_handheld_tdp_spec)
_handheld_tdp_spec.loader.exec_module(_handheld_tdp_module)
_onex_fan_spec = importlib.util.spec_from_file_location("playhub_onex_type2_fan", os.path.join(os.path.dirname(__file__), "quick_settings", "onex_type2_fan.py"))
_onex_fan_module = importlib.util.module_from_spec(_onex_fan_spec)
_onex_fan_spec.loader.exec_module(_onex_fan_module)
_lenovo_tdp_spec = importlib.util.spec_from_file_location("playhub_lenovo_other_tdp", os.path.join(os.path.dirname(__file__), "quick_settings", "lenovo_other_tdp.py"))
_lenovo_tdp_module = importlib.util.module_from_spec(_lenovo_tdp_spec)
_lenovo_tdp_spec.loader.exec_module(_lenovo_tdp_module)
_ayaneo3_spec = importlib.util.spec_from_file_location("playhub_ayaneo3_controller", os.path.join(os.path.dirname(__file__), "quick_settings", "ayaneo3_controller.py"))
_ayaneo3_module = importlib.util.module_from_spec(_ayaneo3_spec)
sys.modules[_ayaneo3_spec.name] = _ayaneo3_module
_ayaneo3_spec.loader.exec_module(_ayaneo3_module)
_gpd_v1_spec = importlib.util.spec_from_file_location("playhub_gpd_v1_controller", os.path.join(os.path.dirname(__file__), "quick_settings", "gpd_v1_controller.py"))
_gpd_v1_module = importlib.util.module_from_spec(_gpd_v1_spec)
sys.modules[_gpd_v1_spec.name] = _gpd_v1_module
_gpd_v1_spec.loader.exec_module(_gpd_v1_module)
_shader_spec = importlib.util.spec_from_file_location("playhub_shader_preflight", os.path.join(os.path.dirname(__file__), "quick_settings", "shader_preflight.py"))
_shader_module = importlib.util.module_from_spec(_shader_spec)
_shader_spec.loader.exec_module(_shader_module)
_shader_cache_spec = importlib.util.spec_from_file_location("playhub_shader_cache", os.path.join(os.path.dirname(__file__), "quick_settings", "shader_cache.py"))
_shader_cache_module = importlib.util.module_from_spec(_shader_cache_spec)
_shader_cache_spec.loader.exec_module(_shader_cache_module)

def _firmware_board(identity):
    return {"manufacturer": identity.get("board_manufacturer"), "product": identity.get("board_model")}
_execution_spec = importlib.util.spec_from_file_location("playhub_game_execution_profiles", os.path.join(os.path.dirname(__file__), "quick_settings", "game_execution_profiles.py"))
_execution_module = importlib.util.module_from_spec(_execution_spec)
_execution_spec.loader.exec_module(_execution_module)


# Retrying the restore forever re-toggles the panel every 30 s, which on an
# HDMI 2.1 sink is indistinguishable from a dead output. Give up re-arming
# after this many attempts and keep the transaction visible instead.
_DISPLAY_RECOVERY_ATTEMPTS = 8
_TOPBAR_CEF_PORT = 8080
_TOPBAR_DATE_BADGE_ID = "playhub-topbar-date"
_TOPBAR_DATE_STYLE_ID = "playhub-topbar-date-style"
_TOPBAR_CLOCK_SELECTORS = ["#header ._1HhLUvHH6BZLIOyOE80TVh", "._1HhLUvHH6BZLIOyOE80TVh", '#header [class*="Clock"]', "#header time"]


class Plugin(QuickSettingsPlugin):
    @staticmethod
    def _native_session_post(path, query, body=None, *, timeout=2.5, maximum=2048):
        try:
            token_path = os.path.join(os.environ["APPDATA"], "GamingMode", "xbox-shell-token")
            with open(token_path, "r", encoding="utf-8") as token_file:
                token = token_file.read(513).strip()
            if not token or len(token) > 512:
                return {}
            uri = "http://127.0.0.1:47991" + path
            if query:
                uri += "?" + urllib.parse.urlencode(query)
            headers = {"X-Playhub-Shell-Token": token}
            data = b""
            if body is not None:
                headers["Content-Type"] = "application/json"
                data = json.dumps(body).encode("utf-8")
            request = urllib.request.Request(uri, data=data, method="POST", headers=headers)
            client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            with client.open(request, timeout=timeout) as response:
                data = json.loads(response.read(maximum))
            return data if isinstance(data, dict) else {}
        except (OSError, ValueError, KeyError):
            return {}

    def _game_execution_state(self, result):
        return {**result, "available": os.name == "nt" and sys.getwindowsversion().build >= 22000,
                "batteryAvailable": self._get_power_status_sync().get("battery") is True}

    async def get_game_execution_profile(self, app_id):
        return await asyncio.to_thread(lambda: self._game_execution_state(self._execution_profiles.get(app_id)))

    async def set_game_execution_profile(self, app_id, ac, battery):
        return await asyncio.to_thread(lambda: self._game_execution_state(self._execution_profiles.save(app_id, ac, battery)))

    async def game_audio_volume(self, app_id, level=None, token=None):
        if not _execution_module.valid_app(app_id):
            return {"status": "Unavailable"}
        query = {"appId": app_id}
        if level is not None:
            if (type(level) is not int or not 0 <= level <= 100 or
                    type(token) is not str or len(token) != 64 or any(char not in "0123456789abcdef" for char in token)):
                return {"status": "Unavailable"}
            query.update(level=level, token=token)
        elif token is not None:
            return {"status": "Unavailable"}
        result = await asyncio.to_thread(self._native_session_post, "/session/steam/game-audio", query)
        return result or {"status": "Unavailable"}

    async def set_steam_download_power(self, request):
        if not isinstance(request, dict):
            return {"ok": False}
        action, app_id, lease = request.get("action"), request.get("appId"), request.get("lease")
        if action not in ("start", "renew", "stop") or not _execution_module.valid_app(app_id):
            return {"ok": False}
        query = {"action": action, "appId": app_id}
        if action != "start":
            if not isinstance(lease, str) or len(lease) != 32 or any(char not in "0123456789abcdef" for char in lease):
                return {"ok": False}
            query["lease"] = lease
        result = await asyncio.to_thread(self._native_session_post, "/session/steam/download-power", query)
        return result or {"ok": False}

    async def game_runtime_changed(self, request):
        app_id = _runtime._arg(request, "app_id", 0)
        result, _ = await asyncio.gather(super().game_runtime_changed(request),
            asyncio.to_thread(self._execution_profiles.observe, app_id))
        return result

    async def get_shader_preflight(self, app_id=0):
        return await asyncio.to_thread(self._shader_preflight.get, app_id)

    async def get_shader_cache(self, app_id=0):
        return await asyncio.to_thread(self._shader_cache.get, app_id)

    async def set_nvidia_shader_cache(self, request):
        return await asyncio.to_thread(self._shader_cache.set_nvidia, request)

    def _arm_big_picture_return(self, app_id):
        if type(app_id) is not int or not 0 < app_id <= 0xffffffff:
            return {"ok": False}
        try:
            token_path = os.path.join(os.environ["APPDATA"], "GamingMode", "xbox-shell-token")
            with open(token_path, "r", encoding="utf-8") as token_file:
                token = token_file.read(513).strip()
            if not token or len(token) > 512:
                return {"ok": False}
            request = urllib.request.Request(f"http://127.0.0.1:47991/session/steam/arm-return?appId={app_id}",
                data=b"", headers={"X-Playhub-Shell-Token": token}, method="POST")
            local_client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            with local_client.open(request, timeout=0.8) as response:
                result = json.loads(response.read(1024))
            return {"ok": isinstance(result, dict) and result.get("ok") is True}
        except (OSError, ValueError, KeyError):
            return {"ok": False}

    async def arm_big_picture_return(self, app_id):
        return await asyncio.to_thread(self._arm_big_picture_return, app_id)

    async def resume_xbox_game(self, app_id):
        if not _execution_module.valid_app(app_id) or app_id < 0x80000000:
            return {"ok": False}
        result = await asyncio.to_thread(self._native_session_post, "/session/uwp/resume", {"appId": app_id})
        return {"ok": result.get("ok") is True}

    async def verify_xbox_steam_overlay(self, app_id, overlay_pid, keyboard, menu=False):
        if (not _execution_module.valid_app(app_id) or app_id < 0x80000000
                or type(overlay_pid) is not int or not 0 <= overlay_pid <= 0xffffffff
                or type(keyboard) is not bool or type(menu) is not bool or (overlay_pid == 0 and (keyboard or menu))):
            return {"ok": False}
        result = await asyncio.to_thread(self._native_session_post, "/session/uwp/steam-overlay",
            {"appId": app_id, "overlayPid": overlay_pid, "keyboard": str(keyboard).lower(), "menu": str(menu).lower()})
        return result or {"ok": False}

    async def read_xbox_steam_overlay(self, app_id, overlay_pid):
        if (not _execution_module.valid_app(app_id) or app_id < 0x80000000
                or type(overlay_pid) is not int or not 0 < overlay_pid <= 0xffffffff):
            return {"ok": False, "kind": "owned-session"}
        result = await asyncio.to_thread(self._native_session_post, "/session/uwp/steam-overlay/snapshot",
            {"appId": app_id, "overlayPid": overlay_pid})
        return result if result.get("kind") == "owned-session" else {"ok": False, "kind": "owned-session"}

    async def request_xbox_steam_overlay(self, app_id):
        if not _execution_module.valid_app(app_id) or app_id < 0x80000000:
            return {"ok": False}
        result = await asyncio.to_thread(self._native_session_post, "/session/uwp/steam-overlay/open", None,
            {"appId": app_id})
        return result if result.get("ok") is True else {"ok": False}

    async def get_owned_game_route(self, observed_app_id, candidate_app_id=None):
        if observed_app_id is not None and (type(observed_app_id) is not int or not 0 <= observed_app_id <= 0xffffffff):
            return {"ok": False}
        if candidate_app_id is not None and (type(candidate_app_id) is not int or not 0x80000000 <= candidate_app_id <= 0xffffffff):
            return {"ok": False}
        if observed_app_id in (None, 0) and candidate_app_id is None:
            return {"ok": False}
        query = {"observedAppId": observed_app_id} if candidate_app_id is None else {"uiObservedApp": "null" if observed_app_id is None else observed_app_id, "candidateAppId": candidate_app_id}
        result = await asyncio.to_thread(self._native_session_post, "/session/steam/owned-launch", query)
        if result.get("ok") is True and result.get("kind") == "absent":
            if result.get("receiver") not in ("steam-ui", "ordinary-game"):
                return {"ok": False}
            return {"ok": True, "kind": "absent", "receiver": result["receiver"]}
        return result if result.get("ok") is True and isinstance(result.get("route"), dict) else {"ok": False}

    async def prepare_owned_mirror_presentation(self, app_id, expected_route):
        if (type(app_id) is not int or not 0x80000000 <= app_id <= 0xffffffff or not isinstance(expected_route, dict)
                or expected_route.get("canonicalShortcutAppId") != app_id or expected_route.get("routingOnly") is not True):
            return {"ok": False}
        integer_keys = ("gamePid", "wrapperPid", "steamPid")
        text_keys = ("originalGameId", "gameBirth", "wrapperBirth", "steamBirth")
        if any(type(expected_route.get(k)) is not int or not 0 < expected_route[k] <= 0xffffffff for k in integer_keys):
            return {"ok": False}
        for key in text_keys:
            value = expected_route.get(key)
            if (type(value) is not str or not value.isascii() or not value.isdecimal() or value.startswith("0")
                    or len(value) > 20 or int(value) > 0xffffffffffffffff):
                return {"ok": False}
        if expected_route["originalGameId"] != str((app_id << 32) | 0x02000000):
            return {"ok": False}
        if "observedSteamGameId" not in expected_route:
            return {"ok": False}
        observed_game = expected_route.get("observedSteamGameId")
        provenance = expected_route.get("launchProvenance", "steam-child")
        if provenance not in ("steam-child", "epic-protocol"):
            return {"ok": False}
        if observed_game is None:
            if provenance != "epic-protocol" or expected_route.get("observedFresh") is not False:
                return {"ok": False}
        elif (type(observed_game) is not str or not observed_game.isascii() or not observed_game.isdecimal()
                or observed_game.startswith("0") or len(observed_game) > 20 or int(observed_game) > 0xffffffffffffffff):
            return {"ok": False}
        fields = {k: expected_route[k] for k in integer_keys + text_keys}
        fields.update(observedSteamGameId=observed_game, launchProvenance=provenance)
        ui_observed = expected_route.get("uiObservedApp")
        if ui_observed is not None and (type(ui_observed) is not int or not 0 <= ui_observed <= 0xffffffff):
            return {"ok": False}
        fields.update({"uiObservedApp": ui_observed, "canonicalShortcutAppId": app_id, "routingOnly": True,
                       "observedFresh": expected_route.get("observedFresh") is True})
        result = await asyncio.to_thread(self._native_session_post, "/session/steam/owned-launch/prepare", None,
                                        {"appId": app_id, "expectedRoute": fields})
        return result if result.get("ok") is True and isinstance(result.get("mirror"), dict) else {"ok": False}

    @staticmethod
    def _native_game_identity(app_id, value):
        if type(app_id) is not int or not 0x80000000 <= app_id <= 0xffffffff or type(value) is not dict:
            return None
        if type(value.get("protocol")) is not int or value["protocol"] != 1 or value.get("routingOnly") is not True or value.get("canonicalAppId") != app_id:
            return None
        integers = ("gamePid", "wrapperPid", "steamPid", "overlayPid", "companionPid")
        text = ("fullGameId", "observedGameId", "gameBirth", "wrapperBirth", "steamBirth", "overlayBirth", "companionBirth", "gameWindow", "observedAt", "expiresAt")
        if any(type(value.get(k)) is not int or not 0 < value[k] <= 0xffffffff for k in integers):
            return None
        for key in text:
            item = value.get(key)
            if type(item) is not str or not item.isascii() or not item.isdecimal() or not 0 < len(item) <= 20 or item.startswith("0") or int(item) > 0xffffffffffffffff:
                return None
        session = value.get("sessionId")
        if type(session) is not str or len(session) != 64 or any(c not in "0123456789abcdefABCDEF" for c in session) or value["fullGameId"] != str((app_id << 32) | 0x02000000):
            return None
        result = {key: value[key] for key in integers + text}
        result.update(protocol=1, canonicalAppId=app_id, sessionId=session, routingOnly=True)
        return result

    async def get_native_game_overlay(self, app_id, expected_route=None):
        if type(app_id) is not int or not 0x80000000 <= app_id <= 0xffffffff:
            return {"ok": False}
        body = {"appId": app_id}
        if expected_route is not None:
            if type(expected_route) is not dict or expected_route.get("canonicalShortcutAppId") != app_id or expected_route.get("routingOnly") is not True:
                return {"ok": False}
            keys = ("canonicalShortcutAppId", "originalGameId", "observedSteamGameId", "launchProvenance", "gamePid", "gameBirth", "wrapperPid", "wrapperBirth", "steamPid", "steamBirth", "observedFresh", "routingOnly", "uiObservedApp")
            body["expectedRoute"] = {key: expected_route[key] for key in keys if key in expected_route}
        result = await asyncio.to_thread(self._native_session_post, "/session/steam/native-game-overlay", None, body)
        if result.get("ok") is True and result.get("kind") in ("absent", "packaged"):
            return {"ok": True, "kind": result["kind"]}
        if result.get("ok") is True and result.get("kind") == "native-game" and self._native_game_identity(app_id, result.get("nativeGame")) is not None:
            return result
        return {"ok": False, "error": result.get("error", "native-game-unavailable")}

    async def set_native_game_overlay_input(self, app_id, action, expected_native, browser=None, owner_token=None, generation=None, callback=None):
        fields = self._native_game_identity(app_id, expected_native)
        if fields is None or action not in ("prepare", "active", "renew", "closed", "cancel", "abort") or type(browser) is not dict:
            return {"ok": False}
        if any(type(browser.get(k)) is not int or not 0 < browser[k] <= 0xffffffff for k in ("overlayPid", "browserId", "appId")) or browser["overlayPid"] != fields["overlayPid"]:
            return {"ok": False}
        body = {"appId": app_id, "action": action, "expectedNative": fields, "browser": {k: browser[k] for k in ("overlayPid", "browserId", "appId")}}
        if action != "prepare":
            if type(owner_token) is not str or len(owner_token) != 32 or any(c not in "0123456789abcdef" for c in owner_token):
                return {"ok": False}
            if type(generation) is not str or not generation.isascii() or not generation.isdecimal() or generation.startswith("0") or len(generation) > 20 or int(generation) > 0xffffffffffffffff:
                return {"ok": False}
            body.update(ownerToken=owner_token, generation=generation)
        if action in ("active", "renew", "closed"):
            if type(callback) is not dict or any(callback.get(k) != browser[k] or type(callback.get(k)) is not int for k in ("overlayPid", "browserId", "appId")) or type(callback.get("active")) is not bool or callback["active"] != (action != "closed"):
                return {"ok": False}
            serial = callback.get("serial")
            if type(serial) is not str or not serial.isascii() or not serial.isdecimal() or serial.startswith("0") or len(serial) > 20 or int(serial) > 0xffffffffffffffff:
                return {"ok": False}
            body["callback"] = {k: callback[k] for k in ("overlayPid", "browserId", "appId", "active", "serial")}
        # Only this bounded stop/ACK transaction needs the longer HTTP budget.
        result = await asyncio.to_thread(self._native_session_post, "/session/steam/native-game-overlay-input", None, body, timeout=7.0, maximum=8192)
        # Keep issued owner receipts even on a failure: the frontend must be
        # able to revoke/finish that exact epoch, never adopt a new one.
        return result or {"ok": False}

    async def prepare_mirror_presentation(self, app_id):
        if not _execution_module.valid_app(app_id) or app_id < 0x80000000:
            return {"ok": False}
        result = await asyncio.to_thread(self._native_session_post, "/session/uwp/steam-overlay/mirror/prepare", None,
            {"appId": app_id})
        return result if result.get("ok") is True and isinstance(result.get("mirror"), dict) else {"ok": False}

    async def get_mirror_presentation(self, app_id, overlay_pid):
        if (not _execution_module.valid_app(app_id) or app_id < 0x80000000
                or type(overlay_pid) is not int or not 0 <= overlay_pid <= 0xffffffff):
            return {"ok": False}
        result = await asyncio.to_thread(self._native_session_post, "/session/uwp/steam-overlay/mirror",
            {"appId": app_id, "overlayPid": overlay_pid})
        if isinstance(result.get("mirror"), dict):
            return result
        # A current authenticated session may not have a mirror generation yet.
        # This proves session replacement only; it grants no presentation/input.
        session = result.get("mirrorSessionId")
        if (result.get("ok") is False and result.get("error") == "mirror-not-prepared"
                and result.get("gameId") == str((app_id << 32) | 0x02000000)
                and type(session) is str and len(session) == 64
                and all(char in "0123456789abcdef" for char in session)
                and type(result.get("companionPid")) is int and 0 < result["companionPid"] <= 0xffffffff
                and type(result.get("overlayPid")) is int and 0 < result["overlayPid"] <= 0xffffffff
                and (overlay_pid == 0 or result["overlayPid"] == overlay_pid)):
            return {key: result[key] for key in ("ok", "error", "gameId", "companionPid", "overlayPid", "mirrorSessionId")}
        return {"ok": False}

    async def set_mirror_presentation(self, app_id, overlay_pid, action, expected_mirror):
        if (not _execution_module.valid_app(app_id) or app_id < 0x80000000
                or type(overlay_pid) is not int or not 0 < overlay_pid <= 0xffffffff
                or action not in ("show", "close") or type(expected_mirror) is not dict):
            return {"ok": False}
        # Decimal strings preserve native uint64 identities through JavaScript.
        # The agent reauthenticates the complete tuple before queuing a command.
        if type(expected_mirror.get("protocol")) is not int or expected_mirror["protocol"] != 1:
            return {"ok": False}
        identity = ("generation", "gameId", "sourceHwnd", "sourceCookie", "destinationHwnd",
            "destinationCookie", "ownerBirth", "gameBirth")
        for key in identity:
            value = expected_mirror.get(key)
            if (type(value) is not str or not value.isascii() or not value.isdecimal()
                    or not 0 < len(value) <= 20 or value.startswith("0") or int(value) > 0xffffffffffffffff):
                return {"ok": False}
        session_id = expected_mirror.get("sessionId")
        if type(session_id) is not str or len(session_id) != 64 or any(char not in "0123456789abcdefABCDEF" for char in session_id):
            return {"ok": False}
        for key in ("ownerPid", "gamePid"):
            if type(expected_mirror.get(key)) is not int or not 0 < expected_mirror[key] <= 0xffffffff:
                return {"ok": False}
        fields = {key: expected_mirror[key] for key in identity + ("sessionId", "ownerPid", "gamePid")}
        fields["protocol"] = 1
        result = await asyncio.to_thread(self._native_session_post, "/session/uwp/steam-overlay/mirror/control", None,
            {"appId": app_id, "overlayPid": overlay_pid, "action": action, "expectedMirror": fields})
        return result if result.get("ok") is True else {"ok": False}

    def _measure_game_frames(self, app_id, gameplay=False):
        if type(app_id) is not int or not 0 < app_id <= 0xffffffff:
            return {"status": "TargetUnavailable"}
        try:
            token_path = os.path.join(os.environ["APPDATA"], "GamingMode", "xbox-shell-token")
            with open(token_path, "r", encoding="utf-8") as token_file:
                token = token_file.read(513).strip()
            if not token or len(token) > 512:
                return {"status": "ProviderUnavailable"}
            url = f"http://127.0.0.1:47991/session/frame-measurement?appId={app_id}"
            if gameplay is True:
                url += "&gameplay=1"
            request = urllib.request.Request(url, data=b"", headers={"X-Playhub-Shell-Token": token}, method="POST")
            local_client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            with local_client.open(request, timeout=14.0 if gameplay is True else 11.0) as response:
                measurement = json.loads(response.read(4096))
            return measurement if isinstance(measurement, dict) else {"status": "Failed"}
        except (OSError, ValueError, KeyError):
            return {"status": "ProviderUnavailable"}

    async def measure_game_frames(self, app_id, gameplay=False):
        return await asyncio.to_thread(self._measure_game_frames, app_id, gameplay)

    @staticmethod
    def _live_performance_request(body=None):
        try:
            token_path = os.path.join(os.environ["APPDATA"], "GamingMode", "xbox-shell-token")
            with open(token_path, "r", encoding="utf-8") as token_file:
                token = token_file.read(513).strip()
            if not token or len(token) > 512:
                return {"ok": False, "error": "Unavailable"}
            headers = {"X-Playhub-Shell-Token": token}
            data = None
            if body is not None:
                data = json.dumps(body).encode("utf-8")
                headers["Content-Type"] = "application/json"
            request = urllib.request.Request("http://127.0.0.1:47991/session/live-performance",
                data=data, headers=headers, method="GET" if body is None else "POST")
            client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            with client.open(request, timeout=2.5) as response:
                result = json.loads(response.read(8193))
            return result if isinstance(result, dict) else {"ok": False, "error": "Unavailable"}
        except urllib.error.HTTPError as error:
            # Native validation failures carry a typed result, never a shell token.
            try:
                result = json.loads(error.read(8193))
                return result if isinstance(result, dict) else {"ok": False, "error": "Unavailable"}
            except (OSError, ValueError):
                return {"ok": False, "error": "Unavailable"}
        except (OSError, ValueError, KeyError):
            return {"ok": False, "error": "Unavailable"}

    async def get_live_performance(self):
        return await asyncio.to_thread(self._live_performance_request)

    async def set_live_performance(self, request):
        invalid = {"ok": False, "error": "InvalidRequest"}
        if not isinstance(request, dict) or set(request) - {
                "action", "appId", "lease", "enabled", "detail", "corner", "transparent", "refreshMs", "suspended", "textColor"}:
            return invalid
        action, lease = request.get("action"), request.get("lease")
        if action not in ("configure", "renew", "stop"):
            return invalid
        if lease is not None and (type(lease) is not str or len(lease) != 32 or
                any(char not in "0123456789abcdef" for char in lease)):
            return invalid
        if action != "configure":
            allowed = {"action", "lease", "suspended"} if action == "renew" else {"action", "lease"}
            if lease is None or set(request) - allowed:
                return invalid
        else:
            if (type(request.get("appId")) is not int or not 0 <= request["appId"] <= 0xffffffff
                    or type(request.get("enabled")) is not bool or type(request.get("transparent")) is not bool
                    or type(request.get("detail")) is not int or not 1 <= request["detail"] <= 4
                    or type(request.get("corner")) is not int or not 1 <= request["corner"] <= 6
                    or ("refreshMs" in request and (type(request["refreshMs"]) is not int
                        or request["refreshMs"] not in (20, 50, 100)))):
                return invalid
            request = {**request, "refreshMs": 20}
            if "textColor" in request and (type(request["textColor"]) is not str or request["textColor"] not in ("white", "yellow", "red", "green")):
                return invalid
        if "suspended" in request and type(request["suspended"]) is not bool:
            return invalid
        return await asyncio.to_thread(self._live_performance_request, request)

    def _request_uwp_gamebar(self, request):
        if (not isinstance(request, dict) or type(request.get("appId")) is not int
                or not 0 < request["appId"] <= 0xffffffff
                or type(request.get("pressed")) is not bool
                or not isinstance(request.get("requestId"), str)
                or not 1 <= len(request["requestId"]) <= 160):
            return {"ok": False}
        try:
            token_path = os.path.join(os.environ["APPDATA"], "GamingMode", "xbox-shell-token")
            with open(token_path, "r", encoding="utf-8") as token_file:
                token = token_file.read(513).strip()
            if not token or len(token) > 512:
                return {"ok": False}
            payload = json.dumps({key: request[key] for key in ("appId", "requestId", "pressed")}).encode("utf-8")
            message = urllib.request.Request("http://127.0.0.1:47991/session/uwp/gamebar", data=payload,
                headers={"Content-Type": "application/json", "X-Playhub-Shell-Token": token}, method="POST")
            with urllib.request.urlopen(message, timeout=0.8) as response:
                result = json.loads(response.read(4096))
            return {"ok": isinstance(result, dict) and result.get("ok") is True}
        except (OSError, ValueError, KeyError):
            return {"ok": False}

    async def request_uwp_gamebar(self, request):
        return await asyncio.get_running_loop().run_in_executor(None, self._request_uwp_gamebar, request)

    def __init__(self):
        super().__init__()
        import decky
        self._decky_ipc_health = None
        if all(getattr(decky, key, None) for key in ("DECKY_PLUGIN_DIR", "DECKY_PLUGIN_LOG_DIR", "DECKY_PLUGIN_RUNTIME_DIR")):
            self._decky_ipc_health = _ipc_module.DeckyIpcHealth(
                os.path.dirname(decky.DECKY_PLUGIN_DIR),
                os.path.dirname(decky.DECKY_PLUGIN_LOG_DIR), decky.DECKY_PLUGIN_RUNTIME_DIR)
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
        self._device_profiles = _device_module.DeviceProfiles(self._settings_dir, self._read_bios_identity, self._read_cim_identity)
        self._battery_health = _battery_module.BatteryHealth(self._get_power_status_sync, _runtime._run_cmd, _runtime._powershell_path)
        self._processor_boost = _boost_module.ProcessorBoost(self._get_power_status_sync)
        self._handheld_power_mode = _handheld_power_module.LenovoPowerMode(
            self._device_profiles.identity, self._run_handheld_firmware_command, _runtime._powershell_path)
        self._msi_claw_firmware = _msi_firmware_module.MsiClawFirmware(
            self._device_profiles.identity, self._run_handheld_firmware_command, _runtime._powershell_path)
        self._asus_performance_commands = _asus_commands_module.AsusPerformanceCommands(
            self._device_profiles.identity, self._run_handheld_firmware_command, _runtime._powershell_path)
        self._asus_device_controls = _asus_controls_module.AsusDeviceControls(
            self._device_profiles.identity, self._run_handheld_firmware_command, _runtime._powershell_path)
        self._handheld_tdp = _handheld_tdp_module.AsusHandheldTdp(
            self._device_profiles.identity, self._run_handheld_firmware_command, _runtime._powershell_path)
        self._onex_fan = _onex_fan_module.OneXTypeTwoFan(
            lambda: _firmware_board(self._device_profiles.identity()), self._run_handheld_firmware_command, _runtime._powershell_path)
        self._lenovo_tdp = _lenovo_tdp_module.LenovoOtherTdp(
            self._device_profiles.identity, self._run_handheld_firmware_command, _runtime._powershell_path)
        self._ayaneo3_controller = _ayaneo3_module.Ayaneo3Controller()
        self._gpd_v1_controller = _gpd_v1_module.GpdV1Controller()
        self._device_capabilities = _device_capabilities_module.DeviceCapabilities(
            self._get_device_profile_sync, self._device_profiles.identity, {
                "asus": (_asus_commands_module.supports_identity, self._asus_performance_commands.get),
                "asus-fan": (_asus_controls_module.supports_identity, self._asus_device_controls.get),
                "asus-tdp": (_handheld_tdp_module.supports_identity, self._handheld_tdp.get),
                "onex-fan": (lambda identity: _onex_fan_module.supports_board(_firmware_board(identity)), self._onex_fan.snapshot),
                "lenovo-other": (_lenovo_tdp_module.supports_identity, self._lenovo_tdp.read_current),
                "ayaneo3": (_device_capabilities_module.ayaneo3_identity, self._ayaneo3_controller.get),
                "gpd-v1": (_device_capabilities_module.gpd_v1_identity, self._gpd_v1_controller.get),
                "lenovo": (_handheld_power_module.supports_identity, self._handheld_power_mode.get),
                "msi": (_msi_firmware_module.supports_identity, self._msi_claw_firmware.get),
            })
        self._shader_preflight = _shader_module.ShaderPreflight(_runtime._run_cmd, _runtime._powershell_path, _runtime._steam_path)
        self._shader_cache = _shader_cache_module.ShaderCacheManager(self._shader_preflight, _runtime._run_cmd, _runtime._powershell_path, _runtime._steam_path)
        self._execution_profiles = _execution_module.GameExecutionProfiles(self._settings_dir, self._native_session_post)
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

    async def get_decky_ipc_failures(self):
        if self._decky_ipc_health is None:
            return []
        return await asyncio.to_thread(self._decky_ipc_health.failures)

    async def claim_decky_ipc_recovery(self, folder, session, log):
        if self._decky_ipc_health is None:
            return {"ok": False}
        return await asyncio.to_thread(self._decky_ipc_health.claim, folder, session, log)

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
        capabilities = getattr(self, "_device_capabilities", None)
        if capabilities is not None:
            capabilities.close()
        ayaneo3 = getattr(self, "_ayaneo3_controller", None)
        if ayaneo3 is not None:
            await asyncio.to_thread(ayaneo3.dispose)
        gpd_v1 = getattr(self, "_gpd_v1_controller", None)
        if gpd_v1 is not None:
            await asyncio.to_thread(gpd_v1.dispose)
        await asyncio.to_thread(self._execution_profiles.close)
        await asyncio.get_running_loop().run_in_executor(None, self._stop_display_sync)
        self._topbar_date_stopping = True
        if self._topbar_date_task:
            self._topbar_date_task.cancel()
            self._topbar_date_task = None
        await asyncio.to_thread(self._inject_topbar_date, False, "auto", False)
        await super()._unload()
        logging.getLogger(__name__).info("Playhub backend unloaded pid=%s", os.getpid())

    async def _uninstall(self):
        await asyncio.to_thread(self._execution_profiles.close)
        await super()._uninstall()

    async def set_microphone_volume(self, request):
        return await super().set_microphone_volume(request)


    @staticmethod
    def _get_power_status_sync():
        class PowerStatus(ctypes.Structure):
            _fields_ = [("ac", ctypes.c_ubyte), ("flags", ctypes.c_ubyte),
                        ("percent", ctypes.c_ubyte), ("saving", ctypes.c_ubyte),
                        ("remaining", wintypes.DWORD), ("full", wintypes.DWORD)]
        status = PowerStatus()
        if os.name != "nt" or not ctypes.windll.kernel32.GetSystemPowerStatus(ctypes.byref(status)):
            return {"available": False}
        battery = status.flags != 255 and not bool(status.flags & 128)
        # Only a known offline battery without charging/reserved flags can
        # supply this estimate. DWORD -1 means unknown, never zero minutes.
        discharging = battery and status.ac == 0 and not bool(status.flags & 0x78)
        remaining = int(status.remaining) if discharging and status.remaining != 0xffffffff else None
        return {"available": True, "connected": status.ac == 1,
                "source": {0: "battery", 1: "ac"}.get(status.ac),
                "battery": battery, "discharging": discharging,
                "remainingSeconds": remaining,
                "percent": status.percent if status.percent <= 100 else None}

    async def get_power_status(self):
        return self._get_power_status_sync()

    async def get_windows_power_profile(self):
        return await asyncio.to_thread(_runtime._get_windows_power_profile_sync, self._get_power_status_sync)

    async def set_windows_power_profile(self, request):
        return await asyncio.to_thread(_runtime._set_windows_power_profile_sync, self._get_power_status_sync, request)

    async def get_processor_boost(self):
        return await asyncio.to_thread(self._processor_boost.get)

    async def set_processor_boost(self, request):
        return await asyncio.to_thread(self._processor_boost.set, request)

    async def get_handheld_power_mode(self):
        return await asyncio.to_thread(self._handheld_power_mode.get)

    async def set_handheld_power_mode(self, request):
        return await asyncio.to_thread(self._handheld_power_mode.set, request)

    async def get_msi_claw_firmware(self):
        return await asyncio.to_thread(self._msi_claw_firmware.get)

    async def set_msi_claw_charge_limit(self, request):
        return await asyncio.to_thread(self._msi_claw_firmware.set_charge_limit, request)

    async def set_msi_claw_power_mode(self, request):
        return await asyncio.to_thread(self._msi_claw_firmware.set_power_mode, request)

    async def set_msi_claw_power_limits(self, request):
        return await asyncio.to_thread(self._msi_claw_firmware.set_power_limits, request)

    async def get_asus_performance_commands(self):
        return await asyncio.to_thread(self._asus_performance_commands.get)

    async def apply_asus_performance_command(self, request):
        return await asyncio.to_thread(self._asus_performance_commands.apply, request)

    async def get_asus_device_controls(self):
        return await asyncio.to_thread(self._asus_device_controls.get)

    async def get_handheld_tdp(self):
        return await asyncio.to_thread(self._handheld_tdp.get)

    async def set_handheld_tdp(self, request):
        return await asyncio.to_thread(self._handheld_tdp.apply, request)

    async def restore_handheld_tdp(self, request):
        return await asyncio.to_thread(self._handheld_tdp.restore, request)

    async def get_onex_fan_controls(self):
        return await asyncio.to_thread(self._onex_fan.snapshot)

    async def get_lenovo_power_limits(self):
        return await asyncio.to_thread(self._lenovo_tdp.read_current)

    async def get_gpd_v1_controller(self):
        return await asyncio.to_thread(self._gpd_v1_controller.get)

    async def set_gpd_v1_controller(self, request):
        return await asyncio.to_thread(self._gpd_v1_controller.apply, request)

    async def restore_gpd_v1_controller(self, request):
        return await asyncio.to_thread(self._gpd_v1_controller.restore, request)

    async def get_ayaneo3_controller(self):
        return await asyncio.to_thread(self._ayaneo3_controller.get)

    async def set_ayaneo3_controller(self, request):
        return await asyncio.to_thread(self._ayaneo3_controller.apply, request)

    async def restore_ayaneo3_controller(self, request):
        return await asyncio.to_thread(self._ayaneo3_controller.restore, request)

    async def set_lenovo_power_limits(self, request):
        if not isinstance(request, dict) or request.get("confirmed") is not True:
            return {"ok": False, "code": "invalid_request"}
        return await asyncio.to_thread(self._lenovo_tdp.set_limits, request.get("limitsWatts"), request.get("token"), True)

    async def set_lenovo_performance_mode(self, request):
        if not isinstance(request, dict) or request.get("confirmed") is not True:
            return {"ok": False, "code": "invalid_request"}
        return await asyncio.to_thread(self._lenovo_tdp.set_mode, request.get("mode"), request.get("token"), True)

    async def restore_lenovo_power_limits(self, request):
        if not isinstance(request, dict) or request.get("confirmed") is not True:
            return {"ok": False, "code": "invalid_request"}
        return await asyncio.to_thread(self._lenovo_tdp.restore, request.get("token"), True)

    async def set_onex_fan_controls(self, request):
        if not isinstance(request, dict) or request.get("confirmed") is not True or type(request.get("mode")) is not int:
            return {"ok": False, "code": "invalid_request"}
        if request["mode"] == 0:
            return await asyncio.to_thread(self._onex_fan.set_auto, request.get("token"), True)
        if request["mode"] == 1:
            return await asyncio.to_thread(self._onex_fan.set_manual, request.get("percent"), request.get("token"), True)
        return {"ok": False, "code": "invalid_request"}

    async def restore_onex_fan_controls(self, request):
        if not isinstance(request, dict) or request.get("confirmed") is not True:
            return {"ok": False, "code": "invalid_request"}
        return await asyncio.to_thread(self._onex_fan.restore, request.get("token"), True)

    @staticmethod
    def _run_handheld_firmware_command(arguments, timeout):
        # Independent Windows helpers must release Decky's extraction directory.
        try:
            with _runtime._independent_process_dll_directory():
                child = _runtime.subprocess.Popen(arguments, text=True,
                    stdout=_runtime.subprocess.PIPE, stderr=_runtime.subprocess.PIPE,
                    env=_runtime._independent_process_environment(), creationflags=_runtime._NO_WINDOW)
            try:
                output, error = child.communicate(timeout=timeout)
            except _runtime.subprocess.TimeoutExpired:
                child.kill()
                child.communicate()
                return -1, "", "Firmware request timed out."
            return child.returncode, output or "", error or ""
        except OSError:
            return -1, "", "Firmware helper unavailable."

    @staticmethod
    def _read_bios_identity():
        if os.name != "nt":
            return {}
        import winreg
        identity = {}
        try:
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"HARDWARE\DESCRIPTION\System\BIOS") as handle:
                for key, name in (
                    ("manufacturer", "SystemManufacturer"), ("model", "SystemProductName"), ("family", "SystemFamily"), ("version", "SystemVersion"),
                    ("board_manufacturer", "BaseBoardManufacturer"), ("board_model", "BaseBoardProduct"),
                ):
                    try:
                        identity[key] = winreg.QueryValueEx(handle, name)[0]
                    except OSError:
                        pass
        except OSError:
            pass
        return identity

    @staticmethod
    def _read_cim_identity():
        if os.name != "nt":
            return {}
        script = "Get-CimInstance Win32_ComputerSystem -Property Manufacturer,Model,SystemFamily | Select-Object @{n='manufacturer';e={$_.Manufacturer}},@{n='model';e={$_.Model}},@{n='family';e={$_.SystemFamily}} | ConvertTo-Json -Compress"
        try:
            code, output, _ = _runtime._run_cmd([_runtime._powershell_path(), "-NoProfile", "-NonInteractive", "-Command", script], timeout=4)
            result = json.loads(output.lstrip("\ufeff")) if code == 0 else None
            return result if isinstance(result, dict) else {}
        except (OSError, ValueError):
            return {}

    def _get_device_profile_sync(self):
        profile = self._device_profiles.get()
        power = self._get_power_status_sync()
        display = None
        power_mode = False
        if os.name == "nt":
            try:
                mode = _runtime._read_current_devmode()
                if mode and mode.dmPelsWidth > 0 and mode.dmPelsHeight > 0:
                    display = {"width": int(mode.dmPelsWidth), "height": int(mode.dmPelsHeight),
                               "hz": int(mode.dmDisplayFrequency) if mode.dmDisplayFrequency > 1 else None}
            except OSError:
                pass
            power_mode = _runtime._get_windows_power_profile_sync(lambda: power).get("available") is True
        return {**profile, "power": power, "display": display, "powerModeAvailable": power_mode}

    async def get_device_profile(self):
        return await asyncio.to_thread(self._get_device_profile_sync)

    async def get_device_capabilities(self, selection=None):
        return await asyncio.to_thread(self._device_capabilities.get, selection)

    async def get_battery_health(self):
        return await asyncio.to_thread(self._battery_health.get)

    def _set_device_profile_sync(self, profile):
        self._device_profiles.save(profile)
        return self._get_device_profile_sync()

    async def set_device_profile(self, profile):
        return await asyncio.to_thread(self._set_device_profile_sync, profile)

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
        info.update(self._device_profiles.identity())
        try:
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"HARDWARE\DESCRIPTION\System\CentralProcessor\0") as handle:
                info["cpu"] = str(winreg.QueryValueEx(handle, "ProcessorNameString")[0]).strip()
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
        section_ids = ("topbar-date", "device", "device/details", "news", "daily-history", "measurement",
                       "display", "mixer", "devices", "power", "game", "shaders", "radeon", "lossless", "agent")
        requested_sections = preferences.get("expandedSections", [])
        expanded_sections = set(x for x in requested_sections[:64] if isinstance(x, str)) if isinstance(requested_sections, list) else set()
        expanded_sections = [x for x in section_ids if x in expanded_sections]
        date_formats = ("auto", "dd_mm_yyyy", "dd_mm_yy", "yyyy_mm_dd", "dd_month_yyyy",
                        "weekday_dd_month", "weekday_short_dd_month", "month_dd_yyyy",
                        "month_short_dd_yyyy", "iso")
        date_format = preferences.get("topbarDateFormat", "auto")
        if date_format not in date_formats:
            date_format = "auto"
        data = dict(order=order, hidden=hidden, active=active, collapsed=collapsed, expandedSections=expanded_sections,
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
        return "(function(C){" + r"""
const key = '__playhubTopbarDateRuntime';
if (window[key]) { window[key].update(C); return; }
let config = C, badge = null, previousClock = null, queued = null, stopped = false;
function ownText(node) { return Array.from(node.childNodes).filter(child => child.nodeType === 3).map(child => child.textContent || '').join(' ').trim(); }
function findClock() {
  for (const selector of config.selectors) { const clock = document.querySelector(selector); if (clock) return clock; }
  const header = document.querySelector('#header') || document.querySelector('.BasicUIHeader_Header_1E_SL');
  if (!header) return null;
  return Array.from(header.querySelectorAll('div,span,button,time')).find(node => /^\d{1,2}:\d{2}(?:\s|$)/.test(ownText(node))) || null;
}
function text() {
  const d = new Date(), pad = n => String(n).padStart(2, '0');
  if (config.format === 'iso') return d.getFullYear() + '-' + pad(d.getMonth()+1) + '-' + pad(d.getDate());
  const formats = {
    dd_mm_yyyy: {day:'2-digit',month:'2-digit',year:'numeric'}, dd_mm_yy: {day:'2-digit',month:'2-digit',year:'2-digit'},
    yyyy_mm_dd: {year:'numeric',month:'2-digit',day:'2-digit'}, dd_month_yyyy: {day:'numeric',month:'long',year:'numeric'},
    weekday_dd_month: {weekday:'long',day:'numeric',month:'long'}, weekday_short_dd_month: {weekday:'short',day:'numeric',month:'short'},
    month_dd_yyyy: {month:'long',day:'numeric',year:'numeric'}, month_short_dd_yyyy: {month:'short',day:'numeric',year:'numeric'}
  };
  try { return new Intl.DateTimeFormat(navigator.language || undefined, formats[config.format] || formats.weekday_short_dd_month).formatToParts(d).map(p => p.type === 'weekday' || p.type === 'month' ? p.value.charAt(0).toLocaleUpperCase(navigator.language)+p.value.slice(1) : p.value).join(''); }
  catch { return d.toLocaleDateString(); }
}
function ensure() {
  if (stopped) return;
  const clock = findClock();
  // Navigation can temporarily remove the header; retain our badge until the new clock mounts.
  if (!clock) {
    if (previousClock && !previousClock.isConnected) previousClock = null;
    return;
  }
  if (previousClock && previousClock !== clock) previousClock.removeAttribute('data-playhub-clock-left');
  previousClock = clock;
  if (clock.hasAttribute('data-playhub-clock-left') !== config.moveLeft) clock.toggleAttribute('data-playhub-clock-left', config.moveLeft);
  let style = document.getElementById(config.style);
  if (!style) {
    style = document.createElement('style'); style.id = config.style;
    style.textContent = '[data-playhub-clock-left]{order:-2!important;}#'+config.badge+'{display:inline-flex;align-items:baseline;margin-left:.55em;opacity:.9;white-space:nowrap;font:inherit;line-height:inherit;color:inherit;pointer-events:none;vertical-align:baseline;align-self:baseline;}#'+config.badge+':has(+#decky-weather-topbar-badge){margin-right:.34em;}';
    document.head.appendChild(style);
  }
  // Steam creates the QuickAccess BrowserView header only on AppRunning.
  // Its compact clock stays intact; the actual Steam overlay retains the date.
  const inGameQamHeader = /^QuickAccess(?:_|$)/.test(document.title || '');
  if (!config.enabled || inGameQamHeader) { (badge || document.getElementById(config.badge))?.remove(); return; }
  badge = badge || document.getElementById(config.badge) || document.createElement('span');
  if (!badge.id) { badge.id = config.badge; badge.setAttribute('aria-hidden','true'); }
  const value = text(); if (badge.textContent !== value) badge.textContent = value;
  // The weather badge follows the date; unchanged nodes are never removed/recreated.
  const weather = clock.querySelector('#decky-weather-topbar-badge');
  if (badge.parentNode !== clock || weather && badge.nextSibling !== weather) clock.insertBefore(badge, weather || null);
}
function queue() { if (stopped || queued !== null) return; queued = window.setTimeout(() => { queued = null; ensure(); }, 60); }
// React may replace the clock's children during a render. Mutation observers
// run before the next paint: restore the retained badge here, not in a delayed
// timer that would leave the date visibly absent for several frames. ensure()
// makes no DOM changes once ownership, content and ordering already match.
const observer = new MutationObserver(records => {
  if (stopped) return;
  // Most Steam mutations belong to games, news or chat. Avoid clock searches
  // and date formatting when none of our header ownership has changed.
  if (!previousClock) { queue(); return; }
  const wanted = config.enabled && !/^QuickAccess(?:_|$)/.test(document.title || '');
  if (!previousClock.isConnected || wanted !== Boolean(badge?.parentNode)
      || (wanted && badge.parentNode !== previousClock)
      || records.some(record => record.target === previousClock || record.target === badge)) ensure();
});
const interval = window.setInterval(ensure, 60000);
function cleanup() {
  stopped = true; observer.disconnect(); window.clearInterval(interval); if (queued !== null) window.clearTimeout(queued);
  document.removeEventListener('visibilitychange', queue, true); window.removeEventListener('focus', queue, true);
  (badge || document.getElementById(config.badge))?.remove(); document.getElementById(config.style)?.remove();
  previousClock?.removeAttribute('data-playhub-clock-left');
  delete window[key];
}
window[key] = { update(next) { config = next; if (!next.enabled && !next.moveLeft) cleanup(); else ensure(); }, cleanup };
if (!config.enabled && !config.moveLeft) { cleanup(); return; }
observer.observe(document.documentElement, {childList:true,subtree:true});
document.addEventListener('visibilitychange', queue, true); window.addEventListener('focus', queue, true);
ensure();
""" + "})(" + cfg + ");"

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
            await asyncio.sleep(15)
