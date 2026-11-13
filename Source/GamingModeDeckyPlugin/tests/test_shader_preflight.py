import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import Mock, patch


_path = Path(__file__).resolve().parents[1] / "quick_settings" / "shader_preflight.py"
_spec = importlib.util.spec_from_file_location("shader_preflight_test", _path)
shader = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(shader)


def _adapter(name, vendor, driver):
    return {"Name": name, "PNPDeviceID": f"PCI\\VEN_{vendor}&DEV_0000", "DriverVersion": driver}


class ShaderPreflightTests(unittest.TestCase):
    def setUp(self):
        build = patch.object(shader, "_windows_build", return_value=26300)
        self.build = build.start()
        self.addCleanup(build.stop)
        self.run_cmd = Mock()
        self.registry = Mock(return_value="")
        self.preflight = shader.ShaderPreflight(self.run_cmd, lambda: "powershell.exe", self.registry)

    def _inventory(self, adapters=(), steam=()):
        self.run_cmd.return_value = (0, json.dumps({"adapters": list(adapters), "steam": list(steam)}), "")

    def test_windows_hardware_eligibility_and_provider_are_independent(self):
        self._inventory([
            _adapter("AMD Radeon RX 9070 XT", "1002", "32.0.32015.7029"),
            _adapter("Intel(R) Arc(TM) B580 Graphics", "8086", "32.0.101.8626"),
            _adapter("NVIDIA GeForce RTX 5060", "10DE", "32.0.16.2101"),
        ])
        result = self.preflight.get(0)
        self.assertEqual(result["windows_build"], 26300)
        self.assertEqual([gpu["vendor"] for gpu in result["adapters"]], ["amd", "intel", "nvidia"])
        self.assertEqual([gpu["microsoft_asd"] for gpu in result["adapters"]], ["eligible", "unsupported", "eligible"])
        self.assertEqual([gpu["intel_precompiled"] for gpu in result["adapters"]], ["unverified", "eligible", "unverified"])
        self.assertEqual(result["game"]["state"], "no_game")
        self.assertEqual(result["game"]["cache"], "unverified")
        self.assertEqual(result["benchmark"]["state"], "manual_reference")

    def test_older_windows_and_unverified_devices_do_not_report_asd_eligibility(self):
        self.build.return_value = 19045
        self._inventory([_adapter("Intel(R) Arc(TM) 140V GPU", "8086", "32.0.101.9150")])
        adapter = self.preflight.get()["adapters"][0]
        self.assertEqual(adapter["microsoft_asd"], "unsupported")
        self.assertEqual(adapter["intel_precompiled"], "eligible")
        for row in (
            _adapter("AMD Radeon RX 9070 XT", "1002", "32.0.39999.0000"),
            _adapter("AMD Radeon RX 580", "1002", "32.0.32015.7029"),
            _adapter("Intel(R) Arc(TM) A770 Graphics", "8086", "32.0.101.9150"),
            _adapter("NVIDIA GeForce GTX 1660", "10DE", "32.0.16.2101"),
            _adapter("NVIDIA GeForce RTX 5060", "FFFF", "32.0.16.2101"),
        ):
            with self.subTest(adapter=row["Name"]):
                self.assertEqual(shader._adapter(row, 26300)["microsoft_asd"], "unverified")

    def test_wmi_failure_and_no_steam_root_are_reported_as_unknown(self):
        self.run_cmd.return_value = (1, "", "provider error")
        game = self.preflight.get(1091500)
        self.assertEqual(game["adapters"], [])
        self.assertEqual(game["game"]["state"], "unverified")
        self.assertEqual(game["game"]["provider"], "intel_precompiled")
        self.preflight.get()
        self.run_cmd.assert_called_once()
        self.assertEqual(self.run_cmd.call_args.kwargs["timeout"], 4)

    def test_manifest_match_uses_running_steam_root_and_exact_app_id(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "running Steam"
            extra = Path(tmp) / "second library"
            steam = root / "steam.exe"
            (root / "steamapps").mkdir(parents=True)
            steam.touch()
            escaped = str(extra).replace("\\", "\\\\")
            (root / "steamapps" / "libraryfolders.vdf").write_text(f'"libraryfolders"\n{{\n"path" "{escaped}"\n}}')
            install = extra / "steamapps" / "common" / "Cyberpunk 2077"
            install.mkdir(parents=True)
            manifest = extra / "steamapps" / "appmanifest_1091500.acf"
            self.registry.return_value = str(Path(tmp) / "bogus Steam")
            self._inventory(steam=[str(steam)])

            def set_manifest(app_id, flags, folder):
                manifest.write_text(f'"AppState"\n{{\n"appid" "{app_id}"\n"StateFlags" "{flags}"\n"installdir" "{folder}"\n}}')

            self.assertEqual(self.preflight.get(1091500)["game"]["state"], "not_installed")
            for app, flags, folder in ((990080, 4, "Cyberpunk 2077"), (1091500, 2, "Cyberpunk 2077"),
                                       (1091500, 4, "../common"), (1091500, 4, "Missing Folder")):
                with self.subTest(app=app, flags=flags, folder=folder):
                    set_manifest(app, flags, folder)
                    self.assertEqual(self.preflight.get(1091500)["game"]["state"], "not_installed")

            set_manifest(1091500, 4, "Cyberpunk 2077")
            game = self.preflight.get(1091500)["game"]
            self.assertEqual(game["state"], "provider_listed")
            self.assertEqual(game["title"], "Cyberpunk 2077")
            self.assertIsNone(game["graphics_api"])
            self.assertEqual(game["cache"], "unverified")
            self.assertEqual(self.preflight.get(1091501)["game"]["state"], "unverified")
            self.assertEqual(self.preflight.get("1091500")["game"]["state"], "no_game")

    def test_provider_catalog_matches_individually_identifiable_steam_titles(self):
        self._inventory()
        game = self.preflight.get(2358720)["game"]
        self.assertEqual(game["provider"], "intel_precompiled")
        self.assertEqual(game["title"], "Black Myth: Wukong")
        shared_launcher = self.preflight.get(1938090)["game"]
        self.assertEqual(shared_launcher["state"], "unverified")
        self.assertIsNone(shared_launcher["provider"])


if __name__ == "__main__":
    unittest.main()
