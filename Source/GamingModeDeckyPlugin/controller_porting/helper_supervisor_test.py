import subprocess
import sys
import time
import unittest
from .helper_supervisor import HelperSupervisor
from .helper_ownership import physical_identity, OwnershipError


class UnresponsiveClient:
    timeout = 0.1

    def call(self, *args, **kwargs):
        raise TimeoutError("mock native call stuck")


class SupervisorTests(unittest.TestCase):
    def run_child(self, absent):
        # Explicit executable mock only; no SDL, driver, or hardware API imported.
        process = subprocess.Popen([sys.executable, "-B", "-c", "import time; time.sleep(60)"],
                                   stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                   stderr=subprocess.DEVNULL,
                                   creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        supervisor = HelperSupervisor(process, UnresponsiveClient(), absent,
                                      interval=0.05, cleanup_timeout=0.05)
        try:
            deadline = time.monotonic() + 3
            while supervisor.state == "watching" and time.monotonic() < deadline:
                time.sleep(0.01)
            supervisor.close()
            self.assertIsNotNone(process.poll())
            self.assertFalse(supervisor.thread.is_alive())
            return supervisor
        finally:
            if process.poll() is None:
                process.kill()
            process.wait(timeout=2)

    def test_stuck_owned_process_terminated_and_absence_confirmed(self):
        supervisor = self.run_child(lambda: True)
        self.assertEqual(supervisor.state, "closed")
        self.assertEqual(supervisor.reason, "helper_unresponsive")

    def test_process_death_alone_does_not_claim_bus_cleanup(self):
        supervisor = self.run_child(lambda: False)
        self.assertEqual(supervisor.state, "cleanup_unconfirmed")
        with self.assertRaisesRegex(RuntimeError, "quarantined"):
            supervisor.call("start")

    def test_same_vidpid_physical_and_virtual_are_not_same_source(self):
        physical = ["HID\\VID_045E&PID_028E\\A", "USB\\VID_045E&PID_028E\\PHYSICAL",
                    "PCI\\ROOT", "HTREE\\ROOT\\0"]
        virtual = ["HID\\VID_045E&PID_028E\\B", "USB\\VID_045E&PID_028E\\01",
                   "ROOT\\VIGEMBUS\\0000", "HTREE\\ROOT\\0"]
        self.assertTrue(physical_identity(physical).startswith("pnp:"))
        with self.assertRaises(OwnershipError):
            physical_identity(virtual)


if __name__ == "__main__":
    unittest.main()
