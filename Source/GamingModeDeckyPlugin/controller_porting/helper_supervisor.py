"""Bounded control of one owned Popen helper; never removes arbitrary bus devices."""
import subprocess
import threading
import time


class HelperSupervisor:
    def __init__(self, process, client, confirm_absent, *, interval=0.25,
                 exit_timeout=1.0, cleanup_timeout=1.0):
        if not callable(confirm_absent) or not 0.05 <= interval <= 0.5:
            raise ValueError("supervisor_configuration_invalid")
        if not 0 < client.timeout <= 1 or not 0 < exit_timeout <= 5 or not 0 < cleanup_timeout <= 5:
            raise ValueError("supervisor_timeout_invalid")
        self.process, self.client, self.confirm_absent = process, client, confirm_absent
        self.interval, self.exit_timeout, self.cleanup_timeout = interval, exit_timeout, cleanup_timeout
        self.state, self.reason = "watching", None
        self.lock, self.cancel = threading.RLock(), threading.Event()
        self.thread = threading.Thread(target=self._watch, name="PlayhubHelperSupervisor", daemon=True)
        self.thread.start()

    def call(self, command, **options):
        with self.lock:
            if self.state != "watching":
                raise RuntimeError("helper_quarantined_or_closed")
            try:
                return self.client.call(command, **options)
            except (OSError, ValueError):
                self._terminate("control_timeout_or_invalid_response")
                raise

    def _watch(self):
        while not self.cancel.wait(self.interval):
            with self.lock:
                if self.state != "watching":
                    return
                if self.process.poll() is not None:
                    self._terminate("unexpected_process_exit")
                    return
                try:
                    self.client.call("heartbeat")
                except (OSError, ValueError):
                    self._terminate("helper_unresponsive")
                    return

    def _terminate(self, reason):
        self.cancel.set()
        self.reason, self.state = reason, "cleanup_unconfirmed"
        try:
            if self.process.poll() is None:
                self.process.kill()  # Popen owns the Windows process handle, not a recycled PID.
            self.process.wait(timeout=self.exit_timeout)
        except (OSError, subprocess.TimeoutExpired):
            self.state = "process_exit_unconfirmed"
            return
        deadline = time.monotonic() + self.cleanup_timeout
        while True:
            try:
                if self.confirm_absent() is True:
                    self.state = "closed"
                    return
            except (OSError, ValueError, RuntimeError):
                break
            if time.monotonic() >= deadline:
                break
            time.sleep(0.02)
        # Fail closed: no automatic replacement helper or broad bus reset.

    def close(self):
        self.cancel.set()
        with self.lock:
            if self.state == "watching":
                try:
                    self.client.call("shutdown")
                    self.process.wait(timeout=self.exit_timeout)
                except (OSError, ValueError, subprocess.TimeoutExpired):
                    pass
                self._terminate("shutdown")
        if threading.current_thread() is not self.thread:
            self.thread.join(timeout=self.client.timeout + self.exit_timeout + self.cleanup_timeout + 1)
