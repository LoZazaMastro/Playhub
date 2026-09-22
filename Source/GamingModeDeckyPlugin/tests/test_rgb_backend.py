"""Offline only: all backend operations use a fake HID transport."""

from dataclasses import replace
import ctypes
import json
from pathlib import Path
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

from rgb import RgbBackend
from rgb.protocols import Device, classify, static_report


MSI = Device("fake://msi", 0x0DB0, 0x1901, 0xFFA0, 1, 0x0163, 64)
LEGION = Device("fake://legion", 0x17EF, 0x6182, 0xFFA0, 1, 0x0100, 64)


class FakeTransport:
    def __init__(self, devices=None):
        self.devices = [MSI] if devices is None else devices
        self.writes = []
        self.enumerations = 0
        self.failure = None
        self.short = False
        self.closed = False
        self.active = 0
        self.max_active = 0

    def enumerate(self):
        self.enumerations += 1
        return self.devices[:]

    def write(self, device, report):
        self.active += 1
        self.max_active = max(self.max_active, self.active)
        try:
            time.sleep(0.001)
            self.writes.append((device, report))
            if self.failure:
                raise self.failure
            return len(report) - int(self.short)
        finally:
            self.active -= 1

    def close(self):
        self.closed = True


class BackendTests(unittest.TestCase):
    def setUp(self):
        # Test otherwise unreachable write orchestration only through fake transport.
        gate = patch("rgb.protocols._MSI_PROTOCOL_VALIDATED", True)
        gate.start()
        self.addCleanup(gate.stop)

    def make(self, devices=None, path=None):
        transport = FakeTransport(devices)
        backend = RgbBackend(path, transport=transport)
        self.addCleanup(backend.close)
        return backend, transport

    def test_import_and_constructor_do_not_load_native_transport(self):
        with patch("rgb.win32.Win32HidTransport", side_effect=AssertionError):
            backend = RgbBackend()
            backend.close()

    def test_status_is_read_only_and_close_never_writes(self):
        backend, transport = self.make([MSI, LEGION])
        state = backend.get_status()
        self.assertTrue(state["ok"])
        self.assertFalse(state["hardware_tested"])
        self.assertTrue(state["devices"][0]["capabilities"]["static_color"])
        self.assertEqual(state["devices"][1]["blocked_reason"], "protocol_provenance_unresolved")
        backend.close()
        backend.close()
        self.assertEqual(transport.writes, [])
        self.assertEqual(backend.get_status()["error"], "closed")

    def test_explicit_boolean_required_before_detection(self):
        backend, transport = self.make()
        for consent in (False, None, 1, "true"):
            self.assertFalse(backend.set_color(MSI.id, [1, 2, 3], user_requested=consent)["ok"])
        self.assertEqual(transport.enumerations, 0)

    def test_input_validation_before_detection(self):
        backend, transport = self.make()
        for color, brightness, power in (([True, 2, 3], 50, True), ([256, 0, 0], 50, True),
                                         ([1, 2], 50, True), ([1, 2, 3], 101, True),
                                         ([1, 2, 3], True, True), ([1, 2, 3], 50, 1)):
            self.assertFalse(backend.set_color(MSI.id, color, brightness, power,
                                               user_requested=True)["ok"])
        self.assertEqual(transport.enumerations, 0)

    def test_strict_descriptor_and_firmware_policy(self):
        for field, value in (("vid", 0x1234), ("pid", 0x1903), ("usage_page", 1),
                             ("usage", 2), ("release", 0xFFFF), ("output_length", 65)):
            device = replace(MSI, **{field: value})
            backend, transport = self.make([device])
            self.assertFalse(backend.set_color(device.id, [1, 2, 3], user_requested=True)["ok"])
            self.assertEqual(transport.writes, [])

    def test_legion_is_detection_only(self):
        backend, transport = self.make([LEGION])
        result = backend.set_color(LEGION.id, [1, 2, 3], user_requested=True)
        self.assertEqual(result["error"], "protocol_provenance_unresolved")
        self.assertEqual(transport.writes, [])

    def test_production_msi_gate_blocks_even_informed_request(self):
        backend, transport = self.make()
        with patch("rgb.protocols._MSI_PROTOCOL_VALIDATED", False):
            device = backend.get_status()["devices"][0]
            self.assertFalse(device["capabilities"]["static_color"])
            self.assertEqual(device["blocked_reason"], "firmware_write_semantics_unverified")
            result = backend.set_color(MSI.id, [1, 2, 3], user_requested=True)
        self.assertEqual(result["error"], "firmware_write_semantics_unverified")
        self.assertEqual(transport.writes, [])

    def test_static_packet_and_off(self):
        report = static_report(MSI, [10, 20, 30], 50, True)
        self.assertEqual(len(report), 64)
        self.assertEqual(report[:14], bytes.fromhex("0f00003c210101fa200001090332"))
        self.assertEqual(report[14:41], bytes([10, 20, 30]) * 9)
        self.assertEqual(report[41:], bytes(23))
        off = static_report(MSI, [10, 20, 30], 50, False)
        self.assertEqual(off[13:41], bytes(28))
        self.assertEqual(static_report(replace(MSI, release=0x0217), [0, 0, 0], 0, False)[6:8],
                         bytes.fromhex("024a"))

    def test_failed_and_short_writes_are_not_retried_or_saved(self):
        for failure, short in ((OSError("timeout"), False), (None, True)):
            with tempfile.TemporaryDirectory() as tmp:
                path = Path(tmp) / "rgb.json"
                backend, transport = self.make(path=path)
                transport.failure, transport.short = failure, short
                result = backend.set_color(MSI.id, [1, 2, 3], user_requested=True)
                self.assertFalse(result["ok"])
                self.assertEqual(result["hardware_state"], "unknown")
                self.assertEqual(len(transport.writes), 1)
                self.assertFalse(path.exists())

    def test_atomic_persistence_and_no_replay(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "rgb.json"
            backend, transport = self.make(path=path)
            result = backend.set_color(MSI.id, [1, 2, 3], 42, user_requested=True)
            self.assertTrue(result["persisted"])
            self.assertEqual(result["hardware_state"], "sent_not_verified")
            self.assertEqual(json.loads(path.read_text())["last_explicit_request"]["brightness"], 42)
            self.assertEqual(list(Path(tmp).glob("*.tmp")), [])
            backend.close()
            other, fake = self.make(path=path)
            other.get_status()
            self.assertEqual(fake.writes, [])

    def test_persistence_failure_does_not_claim_hardware_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "rgb.json"
            path.write_text('{"old": true}')
            backend, transport = self.make(path=path)
            with patch("rgb.backend.os.replace", side_effect=OSError("disk denied")):
                result = backend.set_color(MSI.id, [1, 2, 3], user_requested=True)
            self.assertTrue(result["ok"])
            self.assertFalse(result["persisted"])
            self.assertIn("persistence_error", result)
            self.assertEqual(json.loads(path.read_text()), {"old": True})
            self.assertEqual(list(Path(tmp).glob("*.tmp")), [])

    def test_missing_and_ambiguous_devices_never_write(self):
        for devices in ([], [MSI, MSI]):
            backend, transport = self.make(devices)
            self.assertFalse(backend.set_color(MSI.id, [1, 2, 3], user_requested=True)["ok"])
            self.assertEqual(transport.writes, [])

    def test_concurrent_requests_are_serialized(self):
        backend, transport = self.make()
        results = []
        threads = [threading.Thread(target=lambda: results.append(
            backend.set_color(MSI.id, [1, 2, 3], user_requested=True))) for _ in range(12)]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join(3)
            self.assertFalse(thread.is_alive())
        self.assertEqual(len(results), 12)
        self.assertTrue(all(result["ok"] for result in results))
        self.assertEqual(transport.max_active, 1)

    def test_status_snapshot_cannot_mutate_internal_request(self):
        backend, transport = self.make()
        backend.set_color(MSI.id, [1, 2, 3], user_requested=True)
        state = backend.get_status()
        state["last_request"]["color"][0] = 200
        self.assertEqual(backend.get_status()["last_request"]["color"], [1, 2, 3])

    def test_close_drains_running_write_without_implicit_command(self):
        backend, transport = self.make()
        entered, release = threading.Event(), threading.Event()
        original = transport.write

        def blocked_write(device, report):
            entered.set()
            if not release.wait(2):
                raise OSError("test synchronization timed out")
            return original(device, report)

        transport.write = blocked_write
        writer = threading.Thread(target=lambda: backend.set_color(
            MSI.id, [1, 2, 3], user_requested=True))
        writer.start()
        self.assertTrue(entered.wait(2))
        closer = threading.Thread(target=backend.close)
        closer.start()
        self.assertFalse(transport.closed)
        release.set()
        writer.join(3)
        closer.join(3)
        self.assertFalse(writer.is_alive())
        self.assertFalse(closer.is_alive())
        self.assertTrue(transport.closed)
        self.assertEqual(len(transport.writes), 1)


class NativeWriteTests(unittest.TestCase):
    """Exercise native orchestration using Python functions, never WinDLL."""

    def setUp(self):
        gate = patch("rgb.protocols._MSI_PROTOCOL_VALIDATED", True)
        gate.start()
        self.addCleanup(gate.stop)

    def test_native_transport_also_enforces_production_gate(self):
        transport = self.make()
        report = static_report(MSI, [1, 2, 3], 50, True)
        transport._open = lambda *args, **kwargs: self.fail("must not open a write handle")
        with patch("rgb.protocols._MSI_PROTOCOL_VALIDATED", False):
            with self.assertRaisesRegex(OSError, "write_policy_rejected"):
                transport.write(MSI, report)

    def make(self, wait=0, immediate=True):
        from rgb.win32 import Win32HidTransport

        class Kernel:
            closed = []
            cancelled = False
            drained = False

            def CreateEventW(self, *args):
                return 20

            def WriteFile(self, *args):
                return immediate

            def ReadFile(self, handle, buffer, length, unused, overlapped):
                ctypes.memmove(buffer, b"R" * length, length)
                return immediate

            def WaitForSingleObject(self, *args):
                return wait

            def CancelIoEx(self, *args):
                self.cancelled = True
                return True

            def GetOverlappedResult(self, handle, overlapped, output, drain):
                from ctypes import wintypes
                self.drained |= bool(drain)
                ctypes.cast(output, ctypes.POINTER(wintypes.DWORD))[0] = 64
                return True

            def CloseHandle(self, handle):
                self.closed.append(handle)

        transport = Win32HidTransport.__new__(Win32HidTransport)
        transport.k = Kernel()
        transport.k.closed = []
        transport._open = lambda *args, **kwargs: 10
        transport._describe = lambda *args: MSI
        return transport

    def test_immediate_write_and_cleanup(self):
        transport = self.make()
        self.assertEqual(transport.write(MSI, static_report(MSI, [1, 2, 3], 50, True)), 64)
        self.assertEqual(transport.k.closed, [20, 10])

    def test_duplex_read_and_explicit_channel_cleanup(self):
        from rgb.msi_ram import frame
        device = replace(MSI, input_length=64, input_report_ids=(0x10,), output_report_ids=(0x0F,))
        transport = self.make()
        transport._describe = lambda *args: device
        with transport.open_channel(device, user_requested=True) as channel:
            identity = channel.identity
            self.assertEqual(channel.send(frame(4, 586, 55)), 64)
            self.assertEqual(channel.read(0.5), b"R" * 64)
        self.assertNotEqual(channel.identity, identity)
        self.assertEqual(transport.k.closed, [20, 20, 10])

    def test_duplex_rejects_rom_and_noncanonical_reports(self):
        from rgb.msi_ram import frame
        device = replace(MSI, input_length=64, input_report_ids=(0x10,), output_report_ids=(0x0F,))
        transport = self.make()
        transport._describe = lambda *args: device
        with transport.open_channel(device, user_requested=True) as channel:
            report = bytearray(frame(4, 586, 55))
            report[4] = 0x22
            with self.assertRaises(ValueError):
                channel.send(bytes(report))
            report = bytearray(frame(4, 586, 55))
            report[5] = 1
            with self.assertRaises(ValueError):
                channel.send(bytes(report))
        self.assertEqual(transport.k.closed, [10])

    def test_duplex_stays_disabled_in_production(self):
        transport = self.make()
        transport._open = lambda *args, **kwargs: self.fail("must not open")
        with patch("rgb.protocols._MSI_PROTOCOL_VALIDATED", False):
            with self.assertRaisesRegex(OSError, "duplex_policy_rejected"):
                transport.open_channel(replace(MSI, input_length=64), user_requested=True)

    @unittest.skipUnless(hasattr(ctypes, "get_last_error"), "Windows ctypes error API")
    def test_read_timeout_cancels_and_drains(self):
        transport = self.make(wait=258, immediate=False)
        with patch("rgb.win32.c.get_last_error", return_value=997):
            with self.assertRaisesRegex(TimeoutError, "read_timeout"):
                transport._transfer(10, timeout_ms=50)
        self.assertTrue(transport.k.cancelled)
        self.assertTrue(transport.k.drained)
        self.assertEqual(transport.k.closed, [20])

    @unittest.skipUnless(hasattr(ctypes, "get_last_error"), "Windows ctypes error API")
    def test_timeout_cancels_and_drains_before_close(self):
        transport = self.make(wait=258, immediate=False)
        with patch("rgb.win32.c.get_last_error", return_value=997):
            with self.assertRaisesRegex(OSError, "write_timeout"):
                transport.write(MSI, static_report(MSI, [1, 2, 3], 50, True))
        self.assertTrue(transport.k.cancelled)
        self.assertTrue(transport.k.drained)
        self.assertEqual(transport.k.closed, [20, 10])

    def test_descriptor_rechecked_on_write_handle(self):
        transport = self.make()
        transport._describe = lambda *args: replace(MSI, release=0x0166)
        with self.assertRaisesRegex(OSError, "device_identity_changed"):
            transport.write(MSI, static_report(MSI, [1, 2, 3], 50, True))
        self.assertEqual(transport.k.closed, [10])

    def test_metadata_open_requests_no_read_or_write_access(self):
        from rgb.win32 import Win32HidTransport
        from types import SimpleNamespace
        calls = []
        transport = Win32HidTransport.__new__(Win32HidTransport)
        transport.k = SimpleNamespace(CreateFileW=lambda *args: calls.append(args) or 10)
        self.assertEqual(transport._open("fake://descriptor"), 10)
        self.assertEqual(calls[0][1], 0)
        self.assertEqual(calls[0][2], 3)
        self.assertEqual(calls[0][5], 0)

    def test_metadata_uses_only_attributes_and_preparsed_caps(self):
        from rgb.win32 import Win32HidTransport, Attributes, Caps

        class Hid:
            freed = False

            def HidD_GetAttributes(self, handle, pointer):
                attributes = ctypes.cast(pointer, ctypes.POINTER(Attributes)).contents
                attributes.vid, attributes.pid, attributes.release = MSI.vid, MSI.pid, MSI.release
                return True

            def HidD_GetPreparsedData(self, handle, pointer):
                ctypes.cast(pointer, ctypes.POINTER(ctypes.c_void_p))[0] = 30
                return True

            def HidP_GetCaps(self, preparsed, pointer):
                caps = ctypes.cast(pointer, ctypes.POINTER(Caps)).contents
                caps.usage, caps.usage_page, caps.output_length = MSI.usage, MSI.usage_page, 64
                return 0x00110000

            def HidD_FreePreparsedData(self, pointer):
                self.freed = True

        transport = Win32HidTransport.__new__(Win32HidTransport)
        transport.h = Hid()
        self.assertEqual(transport._describe(10, MSI.path), MSI)
        self.assertTrue(transport.h.freed)

    def test_report_ids_are_read_from_caps_not_assumed_from_length(self):
        from rgb.win32 import Win32HidTransport, Caps, ReportCaps
        from ctypes import wintypes
        from types import SimpleNamespace
        transport = Win32HidTransport.__new__(Win32HidTransport)
        caps = Caps()
        caps.counts[2] = 1
        caps.counts[5] = 1

        def fill(kind, items, count, preparsed):
            self.assertEqual(ctypes.cast(count, ctypes.POINTER(wintypes.USHORT))[0], 1)
            items[0].raw[2] = 0x10 if kind == 0 else 0x0F
            return 0x00110000

        transport.h = SimpleNamespace(HidP_GetValueCaps=fill)
        self.assertEqual(ctypes.sizeof(ReportCaps), 72)
        self.assertEqual(ctypes.alignment(ReportCaps), 4)
        self.assertEqual(transport._report_ids(None, caps, 0), (0x10,))
        self.assertEqual(transport._report_ids(None, caps, 1), (0x0F,))

    def test_duplex_rejects_matching_lengths_with_wrong_report_ids(self):
        transport = self.make()
        transport._open = lambda *args, **kwargs: self.fail("must not open")
        device = replace(MSI, input_length=64, input_report_ids=(1,), output_report_ids=(2,))
        with self.assertRaisesRegex(OSError, "duplex_policy_rejected"):
            transport.open_channel(device, user_requested=True)

    @unittest.skipUnless(hasattr(ctypes, "get_last_error"), "Windows WCHAR ABI")
    def test_enumeration_reads_metadata_and_releases_all_handles(self):
        from rgb.win32 import Win32HidTransport
        from ctypes import wintypes
        from types import SimpleNamespace

        error, closed, accesses = [0], [], []
        path = "fake://enumerated-msi"

        class Setup:
            def SetupDiGetClassDevsW(self, *args):
                return 100

            def SetupDiEnumDeviceInterfaces(self, info, unused, guid, index, entry):
                error[0] = 259 if index else 0
                return index == 0

            def SetupDiGetDeviceInterfaceDetailW(self, info, entry, buffer, size, required, unused):
                byte_length = 4 + (len(path) + 1) * 2
                if buffer is None:
                    ctypes.cast(required, ctypes.POINTER(wintypes.DWORD))[0] = byte_length
                    error[0] = 122
                    return False
                expected = 8 if ctypes.sizeof(ctypes.c_void_p) == 8 else 6
                assert ctypes.cast(buffer, ctypes.POINTER(wintypes.DWORD))[0] == expected
                assert size == byte_length
                encoded = ctypes.create_unicode_buffer(path)
                ctypes.memmove(ctypes.addressof(buffer) + 4, encoded, ctypes.sizeof(encoded))
                error[0] = 0
                return True

            def SetupDiDestroyDeviceInfoList(self, info):
                closed.append(info)

        transport = Win32HidTransport.__new__(Win32HidTransport)
        transport.s = Setup()
        transport.h = SimpleNamespace(HidD_GetHidGuid=lambda pointer: None)
        transport.k = SimpleNamespace(
            CreateFileW=lambda *args: accesses.append(args) or 200,
            CloseHandle=lambda handle: closed.append(handle))
        transport._describe = lambda handle, found_path: replace(MSI, path=found_path)
        with patch("rgb.win32.c.get_last_error", side_effect=lambda: error[0]):
            self.assertEqual(transport.enumerate(), [replace(MSI, path=path)])
        self.assertEqual(accesses[0][1], 0)
        self.assertEqual(closed, [200, 100])


if __name__ == "__main__":
    unittest.main()
