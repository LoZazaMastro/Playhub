import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from datetime import datetime

spec = importlib.util.spec_from_file_location("ipc_health", Path(__file__).resolve().parents[1] / "decky_ipc_health.py")
ipc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ipc)
FAILURE = """Task exception was never retrieved
future: <Task finished coro=<PortSocket.setup_server() done, defined at decky_loader\\localplatform\\localsocket.py:139> exception=PermissionError(13, "error while attempting to bind on address ('127.0.0.1', 49674)")>
Traceback (most recent call last):
  File "decky_loader\\localplatform\\localsocket.py", line 142, in setup_server
PermissionError: [Errno 13] error while attempting to bind on address ('127.0.0.1', 49674)
"""


class IpcTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        plugin = self.root / "plugins" / "Playhub-Artworks"
        plugin.mkdir(parents=True)
        (plugin / "main.py").write_text("", encoding="utf-8")
        (plugin / "plugin.json").write_text(json.dumps({"name": "Playhub Artworks"}), encoding="utf-8")
        self.logs = self.root / "logs" / "Playhub-Artworks"
        self.logs.mkdir(parents=True)
        self.session = {"id": "11884:100", "started": datetime(2026, 10, 3, 10, 42, 53).timestamp()}
        self.health = ipc.DeckyIpcHealth(self.root / "plugins", self.root / "logs", self.root / "runtime", self.session)

    def log(self, name="2026-10-03 10.42.56.log", content=FAILURE):
        (self.logs / name).write_text(content, encoding="utf-8")

    def claim(self, health=None):
        return (health or self.health).claim("Playhub-Artworks", self.session["id"], "2026-10-03 10.42.56.log")

    def test_exact_failure_only(self):
        self.assertTrue(ipc.is_pre_main_bind_failure(FAILURE))
        for text in [FAILURE.replace("127.0.0.1", "0.0.0.0"), FAILURE.replace("49674", "4799"),
                     FAILURE.replace("PermissionError", "OSError"), FAILURE.replace("PortSocket.setup_server", "plugin.start"),
                     FAILURE + "backend.started", FAILURE + ' File "main.py", line 1', FAILURE + "_main failed"]:
            self.assertFalse(ipc.is_pre_main_bind_failure(text))

    def test_previous_session_log_ignored_even_with_fresh_mtime(self):
        self.log("2026-10-03 09.36.31.log")
        self.assertEqual(self.health.failures(), [])
        self.assertFalse(self.claim()["ok"])

    def test_newer_healthy_log_suppresses_older_failure(self):
        self.log()
        self.log("2026-10-03 10.43.00.log", "")
        self.assertEqual(self.health.failures(), [])

    def test_claim_once_across_instances_and_log_rollover(self):
        self.log()
        self.assertEqual(self.health.failures()[0]["name"], "Playhub Artworks")
        self.assertEqual(self.claim(), {"ok": True, "name": "Playhub Artworks"})
        same_session = ipc.DeckyIpcHealth(self.root / "plugins", self.root / "logs", self.root / "runtime", self.session)
        self.assertFalse(self.claim(same_session)["ok"])
        self.log("2026-10-03 10.43.00.log")
        self.assertFalse(self.health.claim("Playhub-Artworks", self.session["id"], "2026-10-03 10.43.00.log")["ok"])

    def test_claim_revalidates_current_log_and_session(self):
        self.log()
        self.assertFalse(self.health.claim("../Playhub-Artworks", self.session["id"], "2026-10-03 10.42.56.log")["ok"])
        self.assertFalse(self.health.claim("Playhub-Artworks", "old-session", "2026-10-03 10.42.56.log")["ok"])
        self.log(content="backend.started")
        self.assertFalse(self.claim()["ok"])

    def test_new_loader_session_can_claim_new_failure(self):
        self.log()
        self.assertTrue(self.claim()["ok"])
        newer = {"id": "11884:200", "started": self.session["started"] + 60}
        self.log("2026-10-03 10.44.00.log")
        next_session = ipc.DeckyIpcHealth(self.root / "plugins", self.root / "logs", self.root / "runtime", newer)
        self.assertTrue(next_session.claim("Playhub-Artworks", newer["id"], "2026-10-03 10.44.00.log")["ok"])

    def test_corrupt_guard_fails_closed(self):
        self.log()
        self.health.journal.parent.mkdir(parents=True)
        self.health.journal.write_text("broken", encoding="utf-8")
        self.assertFalse(self.claim()["ok"])

    def test_unknown_session_fails_closed(self):
        self.log()
        self.health.session = None
        self.assertEqual(self.health.failures(), [])
        self.assertFalse(self.claim()["ok"])

    def test_oversized_and_uninstalled_logs_are_ignored(self):
        self.log(content=FAILURE + "x" * 131072)
        self.assertEqual(self.health.failures(), [])
        self.log()
        (self.root / "plugins" / "Playhub-Artworks" / "main.py").unlink()
        self.assertEqual(self.health.failures(), [])


if __name__ == "__main__":
    unittest.main()
