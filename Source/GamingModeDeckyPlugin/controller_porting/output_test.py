"""Hardware-free output lifecycle and public ABI tests."""
import ctypes
import math
import queue
import threading
import unittest
from .output_vigem import OutputLease, OutputUnavailable, XUSBReport, DS4Report, Lightbar, encode, encode_ds4, neutral, run_output_worker, SUCCESS


class Backend:
    def __init__(self): self.calls, self.fail = [], None
    def __getattr__(self, name):
        def call(*args):
            self.calls.append((name, bytes(args[-1]) if args and isinstance(args[-1], (XUSBReport, DS4Report)) else args))
            if self.fail == name: raise RuntimeError(name)
            return 11 if name == "vigem_alloc" else 22 if name.endswith(("x360_alloc", "ds4_alloc")) else SUCCESS
        return call


def frame(**changes):
    return dict(schemaVersion=1, session="session", identity="physical", sequence=1,
                observedMonotonicNs=0, buttons=["A"], sticks={"left": [-1, 1], "right": [0, 0]},
                triggers={"left": 0, "right": 1}, **changes)


class OutputTests(unittest.TestCase):
    def make(self, source=True, target=True):
        backend, now = Backend(), [0]
        lease = OutputLease(backend, lambda *args: source, lambda *args: target, clock=lambda: now[0])
        return lease, backend, now

    def test_abi_and_normalized_encoding(self):
        self.assertEqual(ctypes.sizeof(XUSBReport), 12)
        value = encode(frame())
        self.assertEqual((value.buttons, value.lx, value.ly, value.lt, value.rt), (4096, -32768, 32767, 0, 255))
        self.assertEqual(bytes(XUSBReport()), bytes(12))

    def test_explicit_authorization_before_io(self):
        lease, backend, _ = self.make()
        with self.assertRaises(OutputUnavailable): lease.start("session", "physical")
        self.assertEqual(backend.calls, [])

    def test_neutral_first_real_update_neutral_last_remove(self):
        lease, backend, _ = self.make()
        lease.start("session", "physical", authorize_device_io=True)
        lease.submit(frame()); lease.stop(); lease.stop()
        reports = [data for name, data in backend.calls if name.endswith("x360_update")]
        self.assertEqual(reports, [bytes(12), bytes(encode(frame())), bytes(12)])
        self.assertEqual([n for n, _ in backend.calls][-4:], ["vigem_target_remove", "vigem_disconnect", "vigem_target_free", "vigem_free"])

    def test_missing_or_virtual_source_prevents_creation(self):
        lease, backend, _ = self.make(source=False)
        with self.assertRaises(OutputUnavailable): lease.start("session", "physical", authorize_device_io=True)
        self.assertEqual(backend.calls, [])

    def test_failed_enumeration_rolls_back(self):
        lease, backend, _ = self.make(target=False)
        with self.assertRaises(OutputUnavailable): lease.start("session", "physical", authorize_device_io=True)
        self.assertIn("vigem_target_remove", [n for n, _ in backend.calls])
        self.assertEqual(lease.state, "closed")

    def test_partial_allocation_failure_still_disconnects(self):
        lease, backend, _ = self.make(); backend.fail = "vigem_target_add"
        with self.assertRaises(RuntimeError): lease.start("session", "physical", authorize_device_io=True)
        self.assertIn("vigem_disconnect", [n for n, _ in backend.calls])
        self.assertEqual(lease.state, "closed")

    def test_invalid_frames_stop_output(self):
        for key, value in [("session", "other"), ("identity", "virtual"), ("sequence", True),
                           ("observedMonotonicNs", -100_000_001), ("observedMonotonicNs", 1),
                           ("buttons", ["unknown"]), ("sticks", {"left": [math.nan, 0], "right": [0, 0]})]:
            with self.subTest(key=key, value=value):
                lease, _, _ = self.make(); lease.start("session", "physical", authorize_device_io=True)
                bad = frame(); bad[key] = value
                with self.assertRaises((OutputUnavailable, ValueError)): lease.submit(bad)
                self.assertEqual(lease.state, "closed")

    def test_duplicate_frame_stops(self):
        lease, _, _ = self.make(); lease.start("session", "physical", authorize_device_io=True)
        lease.submit(frame())
        with self.assertRaises(OutputUnavailable): lease.submit(frame())
        self.assertEqual(lease.state, "closed")

    def test_watchdog_no_new_frames(self):
        lease, _, now = self.make(); lease.start("session", "physical", authorize_device_io=True)
        now[0] = 100_000_001; lease.tick()
        self.assertEqual(lease.state, "closed")

    def test_remove_failure_reports_unverified_cleanup_and_continues(self):
        lease, backend, _ = self.make(); lease.start("session", "physical", authorize_device_io=True)
        backend.fail = "vigem_target_remove"; lease.stop()
        self.assertEqual(lease.state, "cleanup_error")
        self.assertEqual(backend.calls[-1][0], "vigem_free")
        self.assertTrue(lease.errors)

    def test_cancelled_worker_releases(self):
        lease, _, _ = self.make(); cancel = threading.Event(); cancel.set()
        result = run_output_worker(lambda: lease, queue.Queue(maxsize=1), cancel, "session", "physical", authorize_device_io=True)
        self.assertEqual(result["state"], "closed")

    def test_ds4_public_abi_neutral_and_passthrough(self):
        self.assertEqual(ctypes.sizeof(DS4Report), 10)
        value = encode_ds4(frame())
        self.assertEqual((value.lx, value.ly, value.rx, value.ry), (0, 0, 128, 128))
        self.assertEqual(value.buttons, 8 | 0x20 | 0x800)
        self.assertEqual(neutral("ds4").buttons, 8)
        backend = Backend()
        lease = OutputLease(backend, lambda *a: True, lambda *a: True, kind="ds4", clock=lambda: 0)
        lease.start("session", "physical", authorize_device_io=True)
        lease.submit(frame()); lease.stop()
        reports = [v for n, v in backend.calls if n == "vigem_target_ds4_update"]
        self.assertEqual(reports, [bytes(neutral("ds4")), bytes(value), bytes(neutral("ds4"))])

    def test_feedback_explicit_bounded_owner_drain_and_late_callback_ignored(self):
        for kind in ("x360", "ds4"):
            backend, motors = Backend(), []
            lease = OutputLease(backend, lambda *a: True, lambda *a: True, kind=kind,
                                feedback_sink=lambda *a: motors.append(a), clock=lambda: 0)
            lease.start("session", "physical", authorize_device_io=True, authorize_feedback=True)
            callback = lease.feedback_callback
            extra = 0 if kind == "x360" else Lightbar()
            callback(11, 22, 255, 128, extra, None)
            callback(11, 22, 128, 255, extra, None)
            self.assertEqual(motors, [], "native callback cannot perform physical I/O")
            lease.tick()
            self.assertEqual(motors, [("session", "physical", 128 / 255, 1)])
            lease.stop()
            self.assertEqual(motors[-1], ("session", "physical", 0.0, 0.0))
            callback(11, 22, 255, 255, extra, None)
            self.assertTrue(lease.feedback.empty())
            self.assertIn(f"vigem_target_{kind}_unregister_notification", [n for n, _ in backend.calls])

    def test_no_feedback_registration_without_separate_authorization(self):
        lease, backend, _ = self.make()
        lease.start("session", "physical", authorize_device_io=True)
        lease.stop()
        self.assertFalse(any("notification" in n for n, _ in backend.calls))

    def test_source_loss_stops_before_feedback_forwarding(self):
        valid, motors, backend = [True], [], Backend()
        lease = OutputLease(backend, lambda *a: valid[0], lambda *a: True,
                            feedback_sink=lambda *a: motors.append(a), clock=lambda: 0)
        lease.start("session", "physical", authorize_device_io=True, authorize_feedback=True)
        lease.feedback_callback(11, 22, 255, 255, 0, None)
        valid[0] = False
        with self.assertRaises(OutputUnavailable): lease.tick()
        self.assertEqual(motors, [("session", "physical", 0.0, 0.0)])


if __name__ == "__main__": unittest.main()
