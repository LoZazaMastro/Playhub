"""Integrated helper tests: real local IPC/UDP, fake SDL and fake ViGEm only."""
import ctypes as C
import json
import os
from pathlib import Path
import queue
import secrets
import socket
import subprocess
import sys
import threading
import time
import unittest
from unittest.mock import patch, Mock

from .helper_client import HelperClient
from .helper_mock import pipeline
from .helper_native import native_pipeline
from .helper_ownership import OwnershipError, WindowsAncestry, WindowsLease, physical_identity
from .dsu_test import request, decode
from .dsu import DATA_REQUEST


class PipelineTests(unittest.TestCase):
    def setUp(self):
        self.pipeline = pipeline()
        self.addCleanup(self.pipeline.stop)

    def test_reader_to_native_output_encoder_and_real_udp_same_tick(self):
        p = self.pipeline
        p.start({"authorized": True, "output": "x360", "dsu": True})
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.addCleanup(sock.close)
        sock.bind(("127.0.0.1", 0))
        sock.settimeout(1)
        sock.sendto(request(DATA_REQUEST, bytes((1, 0)) + bytes(6)), p.dsu_address)
        p.tick()
        raw = sock.recv(1024)
        self.assertEqual(len(raw), 100)
        self.assertEqual(decode(raw)[0], DATA_REQUEST)
        updates = [data for name, data in p.mock_output.calls if name == "vigem_target_x360_update"]
        self.assertEqual(updates[0], bytes(12))
        self.assertNotEqual(updates[-1], bytes(12))
        p.stop()
        updates = [data for name, data in p.mock_output.calls if name == "vigem_target_x360_update"]
        self.assertEqual(updates[-1], bytes(12))
        self.assertTrue(any(name == "vigem_target_remove" for name, _ in p.mock_output.calls))
        self.assertFalse(p.reader)

    def test_ds4_passes_actual_encoder(self):
        p = self.pipeline
        p.start({"authorized": True, "output": "ds4"})
        p.tick()
        self.assertEqual(p.frames, 1)
        self.assertTrue(any(n == "vigem_target_ds4_update" for n, _ in p.mock_output.calls))

    def test_host_disallows_viewer_mapping_and_unauthorized_activation(self):
        for options in ({}, {"authorized": True}, {"authorized": True, "output": "x360", "mapping": {}}):
            with self.assertRaises(ValueError):
                self.pipeline.start(options)
        self.pipeline.mock_api.SDL_InitSubSystem.assert_not_called()

    def test_hotplug_and_lost_lease_neutralize_owned_output(self):
        for fail in ("hotplug", "lease"):
            p = pipeline()
            p.start({"authorized": True, "output": "x360"})
            if fail == "hotplug":
                p.mock_api.SDL_GamepadConnected.return_value = False
            else:
                p.lease.held = False
            p.tick()
            self.assertEqual(p.state, "idle")
            self.assertIsNotNone(p.error)
            self.assertTrue(any(n == "vigem_target_remove" for n, _ in p.mock_output.calls))

    def test_missing_sensor_is_disconnected_not_fabricated(self):
        p = self.pipeline
        p.start({"authorized": True, "dsu": True})
        p.mock_api.sensor_events.return_value = []
        p.mock_api.sensor_events.side_effect = None
        p.tick()
        self.assertEqual(p.dsu.status()["freshSlots"], [])

    def test_native_factory_is_inert(self):
        with patch("ctypes.CDLL") as cdll, patch("ctypes.WinDLL", create=True) as windll:
            native_pipeline(source_identity="unapproved", source_instance=1,
                sdl_approval={}, output_approval={}, verify_target=lambda *_: False)
            cdll.assert_not_called()
            windll.assert_not_called()


class OwnershipTests(unittest.TestCase):
    def test_mutex_busy_abandoned_and_owned_cleanup_with_mock_win32(self):
        for wait in (0, 0x80, 0x102):
            api = Mock()
            api.CreateMutexW.return_value = 123
            api.WaitForSingleObject.return_value = wait
            api.ReleaseMutex.return_value = True
            lease = WindowsLease("physical")
            with patch("controller_porting.helper_ownership.os.name", "nt"), \
                    patch("ctypes.WinDLL", return_value=api, create=True):
                if wait:
                    with self.assertRaises(OwnershipError):
                        lease.acquire()
                else:
                    lease.acquire()
                    self.assertTrue(lease.valid())
                    lease.close()
                self.assertFalse(lease.valid())
                api.CloseHandle.assert_called_once_with(123)
                self.assertEqual(api.ReleaseMutex.call_count, int(wait != 0x102))

    def test_real_ancestry_resolver_calls_mock_cm_and_requires_root(self):
        resolver = WindowsAncestry()
        api = Mock()
        ids = {1: "HID\\VID_1234&PID_0001\\PAD", 2: "USB\\VID_1234&PID_0001\\SERIAL", 3: "HTREE\\ROOT\\0"}
        def locate(pointer, name, flags):
            C.cast(pointer, C.POINTER(C.c_uint32))[0] = 1
            return 0
        def get_id(node, buffer, length, flags):
            buffer.value = ids[node]
            return 0
        def parent(pointer, node, flags):
            if node == 3:
                return 0x0D
            C.cast(pointer, C.POINTER(C.c_uint32))[0] = node + 1
            return 0
        api.CM_Locate_DevNodeW.side_effect = locate
        api.CM_Get_Device_IDW.side_effect = get_id
        api.CM_Get_Parent.side_effect = parent
        api.CM_Get_DevNode_Registry_PropertyW.return_value = 0x25
        resolver.api = api
        path = b"\\\\?\\hid#vid_1234&pid_0001#pad#{12345678-1234-1234-1234-123456789abc}"
        self.assertEqual(resolver(path, 42), physical_identity(list(ids.values())))
        ids[2] = "ROOT\\SYSTEM\\0001"
        def service(node, key, kind, buffer, size, flags):
            if node != 2:
                return 0x25
            raw = "ViGEmBus\0".encode("utf-16-le")
            C.memmove(buffer, raw, len(raw))
            kind._obj.value, size._obj.value = 1, len(raw)
            return 0
        api.CM_Get_DevNode_Registry_PropertyW.side_effect = service
        with self.assertRaisesRegex(OwnershipError, "virtual_source_rejected"):
            resolver(path, 42)
        api.CM_Get_DevNode_Registry_PropertyW.side_effect = None
        ids[2] = "ROOT\\VIGEMBUS\\0000"
        with self.assertRaises(OwnershipError):
            resolver(path, 42)

    def test_physical_identity_rejects_virtual_ancestry_not_just_leaf_vid(self):
        leaf = "HID\\VID_045E&PID_028E\\PAD"
        real = [leaf, "USB\\VID_045E&PID_028E\\SERIAL", "PCI\\REAL"]
        self.assertTrue(physical_identity(real).startswith("pnp:"))
        self.assertEqual(physical_identity(real), physical_identity([x.lower() for x in real]))
        for ancestry in ([], [leaf], real + ["ROOT\\VIGEMBUS\\0000"], real + ["ROOT\\USBIP\\0000"]):
            with self.assertRaises(OwnershipError):
                physical_identity(ancestry)

    def test_unknown_sdl_path_never_loads_configuration_manager(self):
        with patch("ctypes.WinDLL", create=True) as load:
            with self.assertRaises(OwnershipError):
                WindowsAncestry()(b"not-a-device-interface", 1)
            load.assert_not_called()


class ExecutableHostTests(unittest.TestCase):
    def test_real_process_ipc_auth_stream_lease_and_shutdown(self):
        token = secrets.token_hex(32)
        environment = dict(os.environ, PLAYHUB_HELPER_TOKEN=token, PYTHONDONTWRITEBYTECODE="1")
        root = str(Path(__file__).resolve().parents[1])
        process = subprocess.Popen([sys.executable, "-B", "-m", "controller_porting.helper_host", "--mock"],
            cwd=root, env=environment, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, text=True)
        def cleanup():
            if process.poll() is None:
                process.terminate()
            process.communicate(timeout=3)
        self.addCleanup(cleanup)
        startup = queue.Queue()
        threading.Thread(target=lambda: startup.put(process.stdout.readline()), daemon=True).start()
        ready = json.loads(startup.get(timeout=5))
        self.assertTrue(ready["testMode"])
        client = HelperClient(ready["port"], token)
        self.assertFalse(HelperClient(ready["port"], "wrong" * 8).call("start")["ok"])
        self.assertEqual(client.call("status")["state"], "idle")
        self.assertFalse(client.call("start", authorized=True, output="x360", path="foreign")["ok"])
        self.assertTrue(client.call("start", authorized=True, output="x360", dsu=True)["ok"])
        time.sleep(.08)
        self.assertGreater(client.call("status")["frames"], 0)
        self.assertFalse(client.call("submit", frame={})["ok"])
        self.assertTrue(client.call("heartbeat")["ok"])
        time.sleep(2.15)
        state = client.call("status")
        self.assertEqual(state["state"], "idle")
        self.assertEqual(state["cleanupErrors"], [])
        self.assertEqual(state["error"], "control_lease_expired")
        self.assertEqual(client.call("shutdown")["state"], "idle")
        process.wait(timeout=3)
        self.assertEqual(process.returncode, 0)


if __name__ == "__main__":
    unittest.main()
