from contextlib import contextmanager
from dataclasses import replace
import hashlib
from pathlib import Path
import struct
import tempfile
import unittest

from cpu_power.pawnio import (
    AuditedArtifacts, EXECUTE_FN, LOAD_BINARY, VERSION, RaphaelDiagnostic,
)


class FakeDevice:
    def __init__(self):
        self.opened = False
        self.closed = False
        self.locked = False
        self.calls = []
        self.codename = 16
        self.version = 0x20000
        self.truncated = False

    def open(self, _):
        self.opened = True

    @contextmanager
    def exclusive(self):
        self.locked = True
        try:
            yield
        finally:
            self.locked = False

    def ioctl(self, code, payload, size):
        self.calls.append((code, payload, size))
        if code == VERSION:
            return struct.pack("<I", self.version)
        assert self.locked
        if code == LOAD_BINARY:
            return b""
        assert code == EXECUTE_FN
        assert len(payload) == 32
        name = payload.rstrip(b"\0").decode("ascii")
        values = {
            "ioctl_get_code_name": (self.codename,),
            "ioctl_get_smu_version": (0x123456,),
            "ioctl_resolve_pm_table": (0x540005, 0x12340000),
        }[name]
        if self.truncated:
            return b""
        return struct.pack("<" + "Q" * len(values), *values)

    def close(self):
        self.closed = True


class PawnIoTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        path = Path(self.temp.name) / "fixture.bin"
        path.write_bytes(b"synthetic fixture, never a hardware module")
        self.artifacts = AuditedArtifacts(
            str(path), "0" * 64, str(path),
            hashlib.sha256(path.read_bytes()).hexdigest(), 0x20000)
        self.device = FakeDevice()
        self.diagnostic = RaphaelDiagnostic(self.artifacts, lambda: self.device)
        self.addCleanup(self.diagnostic.close)

    def test_constructor_does_not_open_or_load(self):
        self.assertFalse(self.device.opened)
        self.assertEqual(self.device.calls, [])

    def test_authorization_required_before_access(self):
        for value in (False, 1, "true"):
            with self.assertRaises(PermissionError):
                self.diagnostic.open(authorized=value)
        self.assertFalse(self.device.opened)

    def test_module_hash_mismatch_rejected_before_access(self):
        diagnostic = RaphaelDiagnostic(replace(self.artifacts, module_sha256="0" * 64), lambda: self.device)
        with self.assertRaisesRegex(ValueError, "artifact_hash_mismatch"):
            diagnostic.open(authorized=True)
        self.assertFalse(self.device.opened)

    def test_empty_pin_rejected(self):
        diagnostic = RaphaelDiagnostic(replace(self.artifacts, module_sha256=""), lambda: self.device)
        with self.assertRaisesRegex(ValueError, "artifact_pin_required"):
            diagnostic.open(authorized=True)

    def test_wrong_abi_never_loads_module(self):
        self.device.version = 0x30000
        with self.assertRaisesRegex(OSError, "driver_abi_not_reviewed"):
            self.diagnostic.open(authorized=True)
        self.assertTrue(self.device.closed)
        self.assertEqual([c[0] for c in self.device.calls], [VERSION])

    def test_dragon_range_not_accepted_as_raphael(self):
        self.device.codename = 28
        with self.assertRaisesRegex(OSError, "processor_is_not_raphael"):
            self.diagnostic.open(authorized=True)
        self.assertTrue(self.device.closed)

    def test_actual_protocol_metadata_not_ppt(self):
        self.diagnostic.open(authorized=True)
        status = self.diagnostic.snapshot()
        self.assertTrue(status["hardware_accessed"])
        self.assertTrue(status["diagnostic_read_supported"])
        self.assertEqual(status["smu_version"], 0x123456)
        self.assertEqual(status["pm_table_version"], 0x540005)
        self.assertFalse(status["supported_read"])
        self.assertFalse(status["supported_write"])
        self.assertIsNone(status["observed_ppt_w"])
        names = [c[1].rstrip(b"\0") for c in self.device.calls if c[0] == EXECUTE_FN]
        self.assertNotIn(b"ioctl_send_smu_command", names)
        self.assertNotIn(b"ioctl_write_smu_register", names)
        self.assertNotIn(b"ioctl_update_pm_table", names)

    def test_short_output_is_not_decoded_as_zero(self):
        self.diagnostic.open(authorized=True)
        self.device.truncated = True
        with self.assertRaises(struct.error):
            self.diagnostic.snapshot()

    def test_close_is_idempotent(self):
        self.diagnostic.open(authorized=True)
        self.diagnostic.close()
        self.diagnostic.close()
        self.assertTrue(self.device.closed)
        with self.assertRaisesRegex(RuntimeError, "diagnostic_closed"):
            self.diagnostic.snapshot()


if __name__ == "__main__":
    unittest.main()
