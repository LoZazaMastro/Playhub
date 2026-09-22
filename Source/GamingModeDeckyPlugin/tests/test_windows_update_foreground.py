"""Run the real foreground helper against injected WinAPI fakes; no native UI."""
import ast
import ctypes
from ctypes import wintypes
import ntpath
from pathlib import Path
import types
import unittest
from unittest.mock import Mock, patch


SETTINGS = r"C:\Windows\ImmersiveControlPanel\SystemSettings.exe"
FRAME = r"C:\Windows\System32\ApplicationFrameHost.exe"


class Clock:
    def __init__(self):
        self.now = 0

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.now += max(0.01, seconds)


class Kernel:
    def __init__(self):
        self.images = {10: r"C:\Steam\steam.exe", 20: SETTINGS, 30: FRAME, 99: r"C:\Other\SystemSettings.exe"}
        self.opened, self.closed = [], []

    def GetWindowsDirectoryW(self, buffer, size):
        buffer.value = r"C:\Windows"
        return len(buffer.value)

    def OpenProcess(self, access, inherit, pid):
        assert access == 0x1000
        if pid not in self.images:
            return None
        self.opened.append(pid)
        return pid

    def QueryFullProcessImageNameW(self, handle, flags, buffer, length):
        buffer.value = self.images[handle]
        return True

    def CloseHandle(self, handle):
        self.closed.append(handle)
        return True

    def GetCurrentThreadId(self):
        return 100


class User:
    def __init__(self, clock):
        self.clock = clock
        self.pids = {1: 10, 2: 20}
        self.tops = [1, 2]
        self.children = {}
        self.foreground = 1
        self.actions, self.attachments = [], []
        self.delayed = 0
        self.denied = False
        self.iconic = False
        self.recycle_on_attach = False
        self.raise_on_focus = False

    def EnumWindows(self, callback, parameter):
        for hwnd in self.tops:
            if hwnd == 2 and self.clock() < self.delayed:
                continue
            callback(hwnd, parameter)
        return True

    def EnumChildWindows(self, hwnd, callback, parameter):
        for child in self.children.get(hwnd, []):
            callback(child, parameter)
        return True

    def IsWindowVisible(self, hwnd):
        return True

    def IsIconic(self, hwnd):
        return self.iconic

    def GetWindowThreadProcessId(self, hwnd, pid):
        value = self.pids.get(hwnd, 0)
        if pid is not None:
            pid._obj.value = value
        return value + 1000 if value else 0

    def GetForegroundWindow(self):
        return self.foreground

    def ShowWindowAsync(self, hwnd, mode):
        self.actions.append(("restore", hwnd, mode))
        return True

    def PeekMessageW(self, *args):
        return False

    def AttachThreadInput(self, current, target, attach):
        self.attachments.append((current, target, bool(attach)))
        if attach and self.recycle_on_attach:
            self.pids[2] = 99
        return True

    def BringWindowToTop(self, hwnd):
        self.actions.append(("bring", hwnd))
        return True

    def SetForegroundWindow(self, hwnd):
        self.actions.append(("foreground", hwnd))
        if self.raise_on_focus:
            raise OSError("denied")
        if not self.denied:
            self.foreground = hwnd
        return True

    def SwitchToThisWindow(self, hwnd, alt_tab):
        self.actions.append(("switch", hwnd))
        if not self.denied:
            self.foreground = hwnd


class WindowsUpdateForegroundTests(unittest.TestCase):
    def setUp(self):
        path = Path(__file__).resolve().parents[1] / "main.py"
        tree = ast.parse(path.read_text(encoding="utf-8"))
        plugin = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == "Plugin")
        helper = next(n for n in plugin.body if getattr(n, "name", "") == "_windows_update_foreground")
        scope = {"ctypes": ctypes, "wintypes": wintypes, "os": types.SimpleNamespace(path=ntpath)}
        exec(compile(ast.Module(body=[helper], type_ignores=[]), str(path), "exec"), scope)
        self.helper = scope["_windows_update_foreground"]
        self.clock = Clock()
        self.kernel = Kernel()
        self.user = User(self.clock)

    def run_helper(self):
        return self.helper(None, self.user, self.kernel, self.clock, self.clock.sleep)

    def test_settings_foreground_verified_above_steam(self):
        self.assertTrue(self.run_helper())
        self.assertEqual(self.user.foreground, 2)
        self.assertTrue(all(action[1] == 2 for action in self.user.actions))
        self.assertEqual(sorted(self.kernel.opened), sorted(self.kernel.closed))
        attached = {(a, b) for a, b, on in self.user.attachments if on}
        detached = {(a, b) for a, b, on in self.user.attachments if not on}
        self.assertEqual(attached, detached)

    def test_delayed_settings_window_is_retried(self):
        self.user.delayed = 0.6
        self.assertTrue(self.run_helper())
        self.assertGreaterEqual(self.clock(), 0.6)

    def test_minimized_settings_is_restored_without_topmost(self):
        self.user.iconic = True
        self.assertTrue(self.run_helper())
        self.assertIn(("restore", 2, 9), self.user.actions)

    def test_foreign_executable_with_same_name_is_never_activated(self):
        self.user.pids[2] = 99
        self.assertFalse(self.run_helper())
        self.assertEqual(self.user.actions, [])
        self.assertLessEqual(self.clock(), 5.1)

    def test_frame_host_requires_actual_settings_child(self):
        self.user.pids[2] = 30
        self.assertFalse(self.run_helper())
        self.assertEqual(self.user.actions, [])
        self.user.children[2] = [3]
        self.user.pids[3] = 20
        self.clock.now = 0
        self.assertTrue(self.run_helper())

    def test_api_success_without_foreground_readback_is_failure(self):
        self.user.denied = True
        self.assertFalse(self.run_helper())
        self.assertEqual(self.user.foreground, 1)
        self.assertLessEqual(self.clock(), 5.1)

    def test_threads_detach_even_if_activation_raises(self):
        self.user.raise_on_focus = True
        with self.assertRaises(OSError):
            self.run_helper()
        attached = {(a, b) for a, b, on in self.user.attachments if on}
        detached = {(a, b) for a, b, on in self.user.attachments if not on}
        self.assertEqual(attached, detached)

    def test_recycled_window_after_thread_attachment_is_not_activated(self):
        self.user.recycle_on_attach = True
        self.assertFalse(self.run_helper())
        self.assertEqual(self.user.actions, [])

    def test_pointer_sized_signatures_without_loading_native_dlls(self):
        def dll(fake):
            return types.SimpleNamespace(**{name: Mock(side_effect=getattr(fake, name))
                for name in dir(type(fake)) if not name.startswith("_")})
        user, kernel = dll(self.user), dll(self.kernel)
        with patch.object(ctypes, "WinDLL", side_effect=[user, kernel]):
            self.assertTrue(self.helper(None, clock=self.clock, sleep=self.clock.sleep))
        self.assertEqual(user.GetForegroundWindow.restype, wintypes.HWND)
        self.assertEqual(kernel.OpenProcess.restype, wintypes.HANDLE)
        self.assertEqual(kernel.CloseHandle.argtypes, [wintypes.HANDLE])
        self.assertFalse(hasattr(user, "SetWindowPos"))


if __name__ == "__main__":
    unittest.main()
