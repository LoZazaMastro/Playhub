import asyncio
import ast
import pathlib
import sys
import tempfile
import types
import unittest
from concurrent.futures import ThreadPoolExecutor
from contextlib import nullcontext
from unittest.mock import Mock, patch

from quick_settings.device_profile import DeviceProfiles, detect_profile


class DeviceProfileTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.bios = Mock(return_value={"manufacturer": "LENOVO", "model": "83E1"})
        self.cim = Mock(return_value={})
        self.profiles = DeviceProfiles(self.temp.name, self.bios, self.cim)

    def test_exact_model_and_vendor_pairs(self):
        examples = (
            ("ASUSTeK COMPUTER INC.", "ROG Ally RC71L_RC71L", "rog-ally"),
            ("ASUS", "RC72LA", "rog-ally-x"),
            ("ASUS", "RC73YA", "rog-xbox-ally"),
            ("ASUS", "RC73XA", "rog-xbox-ally-x"),
            ("Lenovo", "83N6", "legion-go-s"),
            ("Lenovo", "83N0", "legion-go-2"),
            ("Micro-Star International Co., Ltd.", "MS-1T52", "msi-claw-8-ai-plus"),
            ("MSI", "MS-1T91", "msi-claw-8-ex"),
            ("Valve", "Jupiter", "steam-deck-lcd"),
            ("Valve", "Galileo", "steam-deck-oled"),
            ("AYANEO", "AIR 1S", "ayaneo"),
            ("AYANEO", "AB05-AMD", "ayaneo-air-plus"),
            ("AYANEO", "AB05-Mendocino", "ayaneo-air-plus"),
            ("AYANEO", "AB05-Intel", "ayaneo-air-plus"),
            ("AYANEO", "AB05NXX", "ayaneo-air-plus"),
            ("AYANEO", "SLIDE", "ayaneo-slide"),
            ("AYANEO", "AB07", "ayaneo-3"),
            ("AYANEO", "AYANEO AIR Pro 1S", "ayaneo"),
            ("AYA", "AYA NEO FOUNDER", "ayaneo"),
            ("AYA", "AYANEO 2021 Pro Retro Power", "ayaneo"),
            ("AYANEO", "AB09", "ayaneo-next-2"),
            ("AYANEO", "AYANEO NEXT II", "ayaneo-next-2"),
            ("AYANEO", "FLIP 1S DS", "ayaneo-flip-1s"),
            ("AYADEVICE", "FLIP 1S KB", "ayaneo-flip-1s"),
            ("AYANEO", "AYANEO FLIP 11", "ayaneo-flip-11"),
            ("AYN", "Loki MiniPro", "ayn-loki-mini-pro"),
            ("AYN", "Loki Zero", "ayn-loki-zero"),
            ("AYN", "Loki Max", "ayn-loki-max"),
            ("GPD", "WIN2", "gpd-win-2"),
            ("GPD", "G1618-03", "gpd-win-3"),
            ("GPD", "G1618-04", "gpd-win-4"),
            ("GPD", "G1618-05", "gpd-win-5"),
            ("GPD", "G1617-01", "gpd-win-mini"),
            ("GPD", "G1617-02", "gpd-win-mini"),
            ("GPD", "G1619-01", "gpd-win-max"),
            ("GPD", "G1619-03", "gpd-win-max-2"),
            ("GPD", "G1619-04", "gpd-win-max-2"),
            ("GPD", "G1619-05", "gpd-win-max-2"),
            ("ONE-NETBOOK", "ONE XPLAYER", "onexplayer"),
            ("ONE-NETBOOK", "ONEXPLAYER X1 mini", "onexplayer-x1-mini"),
            ("ONE-NETBOOK TECHNOLOGY CO., LTD.", "ONEXPLAYER 2 PRO ARP23P EVA-01", "onexplayer-2-pro"),
            ("ONE-NETBOOK", "ONEXPLAYER X2", "onexplayer-x2"),
            ("ONE-NETBOOK", "ONEXPLAYER X2Mini PRO", "onexplayer-x2-mini-pro"),
            ("ONE-NETBOOK", "ONEXPLAYER-3", "onexplayer-3"),
            ("ONE-NETBOOK", "ONEXPLAYERAPEX", "onexplayer-apex"),
            ("ONE-NETBOOK", "ONEXPLAYER F1", "onexfly-f1"),
            ("ONE-NETBOOK", "ONEXPLAYER F1Pro", "onexfly-f1-pro"),
            ("AOKZOE", "AOKZOE A1 Pro", "aokzoe"),
            ("Acer", "GN782U", "acer-nitro-blaze"),
            ("PC Partner Limited", "G0A1W", "zotac-zone"),
            ("Valve", "Fremont", "pc"),
            ("ASUS", "ROG Strix G16 RC71L", "pc"),
            ("GPD", "Pocket 4", "pc"),
            ("GPD", "G1618-050", "pc"),
            ("GPD", "G1619-06", "pc"),
            ("ONE-NETBOOK", "Super V", "pc"),
            ("ONE-NETBOOK", "ONEXPLAYER APEX Air", "pc"),
            ("MSI", "Claw 8 G3", "pc"),
            ("AYANEO", "AM02", "pc"),
            ("AYN", "Odin 2", "pc"),
            ("Other vendor", "Loki Max", "pc"),
            ("AYANEO", "FLIP 11 Pro", "pc"),
            ("Other vendor", "Jupiter", "pc"),
        )
        for vendor, model, expected in examples:
            with self.subTest(vendor=vendor, model=model):
                self.assertEqual(detect_profile({"manufacturer": vendor, "model": model}), expected)
        self.assertEqual(detect_profile({"manufacturer": "Lenovo", "model": "unknown", "family": "Legion Go 8APU1"}), "legion-go")
        self.assertEqual(detect_profile({"manufacturer": "Lenovo", "model": "83E1", "family": "Legion Go S 8ARP1"}), "legion-go")

    def test_x2_mini_board_fallback_requires_exact_board_and_matching_oem(self):
        identity = {
            "manufacturer": "ONE-NETBOOK TECHNOLOGY CO., LTD.",
            "model": "Unknown system model",
            "board_manufacturer": "ONE-NETBOOK",
            "board_model": "ONEXPLAYER X2Mini",
        }
        self.bios.return_value = identity
        detected = self.profiles.get()
        self.assertEqual(detected["effective"], "onexplayer-x2-mini")
        self.assertIn({"id": "onexplayer-x2-mini", "name": "ONEXPLAYER X2 Mini"}, detected["handhelds"])
        self.cim.assert_not_called()
        for fields, expected in (
            ({"model": "ONEXPLAYER X2Mini PRO"}, "onexplayer-x2-mini-pro"),
            ({"family": "ONEXPLAYER X2"}, "onexplayer-x2"),
            ({"manufacturer": "Dell Inc."}, "pc"),
            ({"manufacturer": ""}, "pc"),
            ({"board_manufacturer": ""}, "pc"),
            ({"board_manufacturer": "GPD"}, "pc"),
            ({"board_model": "ONEXPLAYER X2Mini PRO"}, "pc"),
        ):
            with self.subTest(fields=fields):
                self.assertEqual(detect_profile(identity | fields), expected)

    def test_distinct_type_two_boards_retain_their_own_profiles(self):
        for product, expected in (("ONEXPLAYER Apex i", "onexplayer-apex-i"),
                                  ("ONEXPLAYER Apex Air", "onexplayer-apex-air"),
                                  ("ONEXPLAYER SUPER V", "onexplayer-super-v"),
                                  ("ONEXPLAYER X2 EVA", "onexplayer-x2")):
            identity = {"manufacturer": "ONE-NETBOOK", "model": "Unknown system model",
                        "board_manufacturer": "ONE-NETBOOK", "board_model": product}
            self.assertEqual(detect_profile(identity), expected)
            self.assertEqual(detect_profile(identity | {"board_model": product + " PRO"}), "pc")

    def test_registry_reader_retains_board_fields_when_system_model_is_missing(self):
        path = pathlib.Path(__file__).resolve().parents[1] / "main.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        plugin = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "Plugin")
        plugin.bases = []
        plugin.body = [node for node in plugin.body if getattr(node, "name", "") == "_read_bios_identity"]
        scope = {"os": types.SimpleNamespace(name="nt")}
        exec(compile(ast.Module(body=[plugin], type_ignores=[]), str(path), "exec"), scope)
        registry = {
            "SystemManufacturer": "ONE-NETBOOK",
            "BaseBoardManufacturer": "ONE-NETBOOK",
            "BaseBoardProduct": "ONEXPLAYER X2Mini",
        }

        def query(_, field):
            if field not in registry:
                raise FileNotFoundError(field)
            return registry[field], 1

        fake_winreg = types.SimpleNamespace(
            HKEY_LOCAL_MACHINE=object(), OpenKey=Mock(return_value=nullcontext(object())), QueryValueEx=query,
        )
        self.cim.return_value = {"manufacturer": "ONE-NETBOOK", "model": "Unknown system model"}
        profiles = DeviceProfiles(self.temp.name, scope["Plugin"]._read_bios_identity, self.cim)
        with patch.dict(sys.modules, {"winreg": fake_winreg}):
            self.assertEqual(profiles.get()["detected"], "onexplayer-x2-mini")
        fake_winreg.OpenKey.assert_called_once()
        self.cim.assert_called_once()

    def test_saved_group_profiles_remain_selectable_after_detection_becomes_specific(self):
        for model, manufacturer, saved, detected in (
            ("G1618-05", "GPD", "gpd-win", "gpd-win-5"),
            ("ONEXPLAYER 3", "ONE-NETBOOK", "onexplayer", "onexplayer-3"),
        ):
            with self.subTest(saved=saved):
                self.profiles.save(saved)
                bios = Mock(return_value={"manufacturer": manufacturer, "model": model})
                reopened = DeviceProfiles(self.temp.name, bios, self.cim)
                profile = reopened.get()
                self.assertEqual(profile["effective"], saved)
                self.assertEqual(profile["detected"], detected)
                self.assertIn(saved, {handheld["id"] for handheld in profile["handhelds"]})
                reopened.save("auto")
                self.assertEqual(reopened.get()["effective"], detected)

    def test_persist_manual_override_and_restore_auto(self):
        self.assertEqual(self.profiles.get()["effective"], "legion-go")
        self.profiles.save("steam-deck-oled")
        reopened = DeviceProfiles(self.temp.name, self.bios, self.cim)
        profile = reopened.get()
        self.assertEqual(profile["selection"], "steam-deck-oled")
        self.assertEqual(profile["effective"], "steam-deck-oled")
        self.assertEqual(profile["detected"], "legion-go")
        self.assertEqual(profile["model"], "83E1")
        reopened.save("auto")
        self.assertEqual(self.profiles.get()["effective"], "legion-go")
        self.cim.assert_not_called()

    def test_cim_fallback_only_runs_on_first_identity_read(self):
        self.bios.return_value = {"manufacturer": "To be filled by O.E.M.", "model": ""}
        self.cim.return_value = {"manufacturer": "Valve", "model": "Galileo"}
        with ThreadPoolExecutor(max_workers=4) as executor:
            devices = list(executor.map(lambda _: self.profiles.get()["effective"], range(8)))
        self.assertEqual(devices, ["steam-deck-oled"] * 8)
        self.bios.assert_called_once()
        self.cim.assert_called_once()

    def test_corrupt_preference_and_failed_replace_preserve_safe_state(self):
        directory = pathlib.Path(self.temp.name)
        (directory / "device-profile.json").write_text("{invalid", encoding="utf-8")
        self.assertEqual(self.profiles.get()["selection"], "auto")
        self.profiles.save("pc")
        for value in (None, [], "tuner", "rog-ally\n", "../../device-profile.json"):
            with self.assertRaises(ValueError):
                self.profiles.save(value)
        with patch("quick_settings.device_profile.os.replace", side_effect=OSError("disk unavailable")):
            with self.assertRaises(OSError):
                self.profiles.save("rog-ally")
        self.assertEqual(self.profiles.get()["selection"], "pc")
        self.assertEqual(sorted(item.name for item in directory.iterdir()), ["device-profile.json"])

    def test_rpc_only_offers_capabilities_reported_by_windows(self):
        path = pathlib.Path(__file__).resolve().parents[1] / "main.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        plugin = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "Plugin")
        plugin.bases = []
        methods = {"get_device_profile", "_get_device_profile_sync", "set_device_profile", "_set_device_profile_sync"}
        plugin.body = [node for node in plugin.body if getattr(node, "name", "") in methods]
        runtime = types.SimpleNamespace(_read_current_devmode=Mock(return_value=None), _get_windows_power_profile_sync=Mock(return_value={"available": False}))
        scope = {"asyncio": asyncio, "os": types.SimpleNamespace(name="nt"), "_runtime": runtime}
        exec(compile(ast.Module(body=[plugin], type_ignores=[]), str(path), "exec"), scope)
        device = scope["Plugin"]()
        device._device_profiles = self.profiles
        device._get_power_status_sync = Mock(return_value={"available": False})
        selected = asyncio.run(device.set_device_profile("steam-deck-lcd"))
        self.assertFalse(selected["powerModeAvailable"])
        self.assertIsNone(selected["display"])
        self.assertEqual(selected["detected"], "legion-go")
        runtime._read_current_devmode.return_value = types.SimpleNamespace(dmPelsWidth=1280, dmPelsHeight=800, dmDisplayFrequency=90)
        runtime._get_windows_power_profile_sync.return_value = {"available": True}
        actual = asyncio.run(device.get_device_profile())
        self.assertTrue(actual["powerModeAvailable"])
        self.assertEqual(actual["display"], {"width": 1280, "height": 800, "hz": 90})
