"""One main-thread reader/output/DSU loop, authenticated loopback control only."""
import argparse
import hmac
import json
import os
import queue
import socket
import threading
import time

from .dsu import DsuServer
from .dsu_bridge import sample_from_reader_frame, CEMU_PARITY


class Pipeline:
    def __init__(self, reader_factory, output_factory, lease_factory, *, instance, identity,
                 dsu_port=26760, test_mode=False):
        self.reader_factory, self.output_factory = reader_factory, output_factory
        self.lease_factory = lease_factory
        self.instance, self.identity = instance, identity
        self.dsu_port, self.test_mode = dsu_port, test_mode
        self.reader = self.output = self.dsu = self.lease = None
        self.session = None
        self.dsu_address = None
        self.frames = 0
        self.state = "idle"
        self.error = None
        self.phase = None
        self.cleanup_errors = []
        self.owner = threading.get_ident()

    def _thread(self):
        if threading.get_ident() != self.owner or threading.current_thread() is not threading.main_thread():
            raise RuntimeError("helper_main_thread_required")

    def start(self, options):
        self._thread()
        if self.state != "idle" or options.get("authorized") is not True:
            raise ValueError("explicit_idle_activation_required")
        if set(options) - {"authorized", "output", "dsu", "feedback"}:
            raise ValueError("unknown_options")
        kind = options.get("output")
        if kind not in (None, "x360", "ds4") or type(options.get("dsu", False)) is not bool:
            raise ValueError("invalid_output_selection")
        if type(options.get("feedback", False)) is not bool:
            raise ValueError("invalid_feedback_selection")
        if kind is None and not options.get("dsu"):
            raise ValueError("output_required_no_viewer_mode")
        self.state, self.error, self.cleanup_errors = "starting", None, []
        try:
            self.phase = "source_lease"
            self.lease = self.lease_factory(self.identity)
            self.lease.acquire()
            self.phase = "source_open"
            self.reader = self.reader_factory(self.lease)
            caps = self.reader.start(self.instance, self.identity, authorize_device_io=True,
                                     authorize_sensors=options.get("dsu", False))
            self.session = caps["session"]
            if kind:
                self.phase = "virtual_output_create"
                self.output = self.output_factory(kind, self.reader, self.lease)
                self.output.start(self.session, self.identity, authorize_device_io=True,
                                  authorize_feedback=options.get("feedback", False))
            if options.get("dsu"):
                self.phase = "dsu_bind"
                self.dsu = DsuServer(port=self.dsu_port, test_mode=self.test_mode)
                self.dsu_address = self.dsu.start(authorized=True)
            self.frames, self.state, self.phase = 0, "active", None
            return self.status()
        except BaseException:
            self.error = "activation_failed"
            self.stop()
            raise

    def tick(self):
        self._thread()
        if self.state != "active":
            return
        try:
            self.phase = "source_lease"
            if not self.lease.valid():
                raise RuntimeError("source_lease_lost")
            self.phase = "source_read"
            frame = self.reader.poll(self.session, self.identity)
            if not self.test_mode and frame.get("sourceKind") != "live":
                raise RuntimeError("mock_frame_forbidden")
            if self.output:
                self.phase = "virtual_output_update"
                self.output.submit(frame)
                self.output.tick()
            if self.dsu:
                self.phase = "dsu_publish"
                try:
                    sample = sample_from_reader_frame(frame, mapping=CEMU_PARITY)
                    self.dsu.publish(0, sample)
                except ValueError:
                    self.dsu.disconnect(0)
                self.dsu.tick()
            self.frames += 1
            self.phase = None
        except Exception:
            self.error = "transport_failed"
            self.stop()

    def stop(self):
        self._thread()
        # Keep the reader/lease alive while output sends final neutral/feedback.
        for name, method in (("output", "stop"), ("dsu", "close"), ("reader", "close"), ("lease", "close")):
            obj = getattr(self, name)
            if obj:
                try:
                    result = getattr(obj, method)()
                    if getattr(obj, "errors", None) or isinstance(result, dict) and result.get("ok") is False:
                        self.cleanup_errors.append(name)
                except Exception:
                    self.cleanup_errors.append(name)
                setattr(self, name, None)
        self.session = None
        self.dsu_address = None
        self.state = "cleanup_error" if self.cleanup_errors else "idle"
        return self.status()

    def status(self):
        return {"ok": self.state != "cleanup_error" and self.error is None, "schemaVersion": 1,
                "state": self.state, "frames": self.frames, "testMode": self.test_mode,
                "error": self.error, "cleanupErrors": list(self.cleanup_errors),
                "failurePhase": self.phase if self.error else None,
                "sourceReady": self.reader is not None and self.state == "active",
                "outputState": self.output.state if self.output else "disabled",
                "dsu": self.dsu.status() if self.dsu else None,
                "dsuAddress": self.dsu_address}


class HelperHost:
    def __init__(self, pipeline, token, *, lease_seconds=2):
        if not isinstance(token, str) or len(token) < 32:
            raise ValueError("strong_control_token_required")
        self.pipeline, self.token, self.lease_seconds = pipeline, token, lease_seconds
        self.commands = queue.Queue(maxsize=16)
        self.done = threading.Event()
        self.socket = None
        self.deadline = 0

    def _serve(self):
        while not self.done.is_set():
            try:
                connection, _ = self.socket.accept()
            except socket.timeout:
                continue
            except OSError:
                return
            with connection:
                connection.settimeout(0.2)
                try:
                    raw = connection.makefile("rb").readline(4097)
                    if len(raw) > 4096 or not raw.endswith(b"\n"):
                        raise ValueError()
                    request = json.loads(raw)
                    if not isinstance(request, dict) or not isinstance(request.get("token"), str) or not hmac.compare_digest(request["token"], self.token):
                        raise ValueError()
                    response = queue.Queue(maxsize=1)
                    self.commands.put_nowait((request, response))
                    reply = response.get(timeout=1)
                except (ValueError, OSError, queue.Empty, queue.Full):
                    reply = {"ok": False, "error": "invalid_or_unavailable_control"}
                try:
                    connection.sendall(json.dumps(reply, allow_nan=False).encode() + b"\n")
                except OSError:
                    pass

    def run(self, ready):
        self.pipeline._thread()
        self.socket = socket.socket()
        self.socket.bind(("127.0.0.1", 0))
        self.socket.listen(4)
        self.socket.settimeout(0.1)
        thread = threading.Thread(target=self._serve, daemon=True)
        thread.start()
        try:
            ready(self.socket.getsockname()[1])
            while not self.done.is_set():
                try:
                    request, response = self.commands.get_nowait()
                except queue.Empty:
                    request = None
                if request:
                    command, options = request.get("command"), request.get("options", {})
                    try:
                        if not isinstance(options, dict):
                            raise ValueError()
                        if command == "start":
                            result = self.pipeline.start(options)
                            self.deadline = time.monotonic() + self.lease_seconds
                        elif command == "heartbeat" and not options:
                            self.deadline = time.monotonic() + self.lease_seconds
                            result = self.pipeline.status()
                        elif command == "status" and not options:
                            result = self.pipeline.status()
                        elif command in ("stop", "shutdown") and not options:
                            result = self.pipeline.stop()
                            if command == "shutdown":
                                self.done.set()
                        else:
                            raise ValueError()
                    except Exception as error:
                        result = {"ok": False, "error": type(error).__name__}
                    response.put_nowait(result)
                if self.pipeline.state == "active" and time.monotonic() > self.deadline:
                    self.pipeline.error = "control_lease_expired"
                    self.pipeline.phase = "control_heartbeat"
                    self.pipeline.stop()
                self.pipeline.tick()
                self.done.wait(0.01)
        finally:
            self.pipeline.stop()
            self.done.set()
            self.socket.close()
            thread.join(timeout=1)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mock", action="store_true")
    args = parser.parse_args()
    if not args.mock:
        parser.error("Native mode requires an audited host factory; no auto-install or implicit hardware start")
    from .helper_mock import pipeline
    token = os.environ.get("PLAYHUB_HELPER_TOKEN", "")
    host = HelperHost(pipeline(), token)
    host.run(lambda port: print(json.dumps({"port": port, "testMode": True}), flush=True))


if __name__ == "__main__":
    main()
