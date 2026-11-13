import ast
import asyncio
import json
import os
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch


class PanelSectionPersistenceTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="playhub-panel-sections-")
        self.addCleanup(self.scratch.cleanup)
        source = Path(__file__).resolve().parents[1] / "main.py"
        tree = ast.parse(source.read_text(encoding="utf-8"))
        plugin = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "Plugin")
        methods = [node for node in plugin.body if isinstance(node, ast.AsyncFunctionDef)
                   and node.name in {"get_panel_preferences", "save_panel_preferences"}]
        fixture = ast.fix_missing_locations(ast.Module(body=[ast.ClassDef(
            name="Preferences", bases=[], keywords=[], body=methods, decorator_list=[])], type_ignores=[]))
        namespace = dict(json=json, os=os)
        exec(compile(fixture, str(source), "exec"), namespace)
        self.preferences = namespace["Preferences"]

    def instance(self, name="first"):
        value = self.preferences()
        value._panel_lock = threading.RLock()
        value._settings_dir = str(Path(self.scratch.name) / name)
        value._panel_path = str(Path(value._settings_dir) / "playhub-panel.json")
        return value

    def save(self, plugin, data):
        return asyncio.run(plugin.save_panel_preferences(data))

    def read(self, plugin):
        return asyncio.run(plugin.get_panel_preferences())

    def test_independent_open_and_closed_sections_survive_restart(self):
        plugin = self.instance()
        saved = self.save(plugin, dict(expandedSections=["power", "display", "device/details"],
                                       collapsed=["radeon"], topbarDateEnabled=False))
        self.assertEqual(self.read(self.instance()), saved)
        self.assertEqual(saved["expandedSections"], ["device/details", "display", "power"])
        self.assertEqual(saved["collapsed"], ["radeon"])
        saved["expandedSections"].remove("display")
        self.save(plugin, saved)
        self.assertEqual(self.read(self.instance())["expandedSections"], ["device/details", "power"])
        self.assertFalse(Path(plugin._panel_path + ".tmp").exists())

    def test_missing_legacy_or_invalid_expansion_defaults_closed(self):
        plugin = self.instance()
        for value in [None, True, "power", {}, 42]:
            self.assertEqual(self.save(plugin, dict(expandedSections=value))["expandedSections"], [])
        self.assertEqual(self.save(plugin, dict(collapsed=["power"]))["expandedSections"], [])
        self.assertEqual(self.save(plugin, dict(expandedSections=["power", {}, None, "power", "unknown", "device/details"]))["expandedSections"],
                         ["device/details", "power"])
        self.assertEqual(self.save(plugin, dict(expandedSections=["unknown"] * 64 + ["power"]))["expandedSections"], [])

    def test_backend_and_frontend_share_all_fifteen_stable_ids(self):
        expected = ["topbar-date", "device", "device/details", "news", "daily-history", "measurement",
                    "display", "mixer", "devices", "power", "game", "shaders", "radeon", "lossless", "agent"]
        self.assertEqual(self.save(self.instance(), dict(expandedSections=expected * 2))["expandedSections"], expected)

    def test_independent_local_settings_stores_do_not_share_section_state(self):
        self.save(self.instance("first"), dict(expandedSections=["mixer"]))
        self.assertEqual(self.read(self.instance("second")), {})
        self.save(self.instance("second"), dict(expandedSections=["display"]))
        self.assertEqual(self.read(self.instance("first"))["expandedSections"], ["mixer"])

    def test_failed_atomic_save_preserves_previous_preferences(self):
        plugin = self.instance()
        self.save(plugin, dict(expandedSections=["mixer"]))
        original = Path(plugin._panel_path).read_bytes()
        with patch.object(os, "replace", side_effect=OSError("Fixture write unavailable")):
            with self.assertRaises(OSError):
                self.save(plugin, dict(expandedSections=["power"]))
        self.assertEqual(Path(plugin._panel_path).read_bytes(), original)
        self.assertEqual(self.read(self.instance())["expandedSections"], ["mixer"])
        self.save(plugin, dict(expandedSections=["display"]))
        self.assertEqual(self.read(self.instance())["expandedSections"], ["display"])

    def test_malformed_file_is_ignored_without_opening_a_section(self):
        plugin = self.instance()
        Path(plugin._settings_dir).mkdir()
        for data in ["{", "null", "[]", '"power"']:
            Path(plugin._panel_path).write_text(data, encoding="utf-8")
            self.assertEqual(self.read(plugin), {})


if __name__ == "__main__":
    unittest.main()
