"""Explicit ViGEm passthrough OS sink. No mapper, reader or auto activation."""
import ctypes as C
import hashlib
import math
import os
from pathlib import Path
import queue
import threading
import time

SDK_REVISION = "b66d02d57e32cc8595369c53418b843e958649b4"
SUCCESS = 0x20000000
BUTTONS = dict(Up=1, Down=2, Left=4, Right=8, Start=16, Back=32,
               LS=64, RS=128, LB=256, RB=512, Guide=1024,
               A=4096, B=8192, X=16384, Y=32768)


class OutputUnavailable(RuntimeError):
    pass


class XUSBReport(C.Structure):
    _fields_ = [("buttons", C.c_uint16), ("lt", C.c_uint8), ("rt", C.c_uint8),
                ("lx", C.c_int16), ("ly", C.c_int16), ("rx", C.c_int16), ("ry", C.c_int16)]


class DS4Report(C.Structure):
    _fields_ = [("lx", C.c_uint8), ("ly", C.c_uint8), ("rx", C.c_uint8), ("ry", C.c_uint8),
                ("buttons", C.c_uint16), ("special", C.c_uint8), ("lt", C.c_uint8), ("rt", C.c_uint8)]


class Lightbar(C.Structure):
    _fields_ = [("r", C.c_uint8), ("g", C.c_uint8), ("b", C.c_uint8)]


CALLBACK = getattr(C, "WINFUNCTYPE", C.CFUNCTYPE)
X360Feedback = CALLBACK(None, C.c_void_p, C.c_void_p, C.c_uint8, C.c_uint8, C.c_uint8, C.c_void_p)
DS4Feedback = CALLBACK(None, C.c_void_p, C.c_void_p, C.c_uint8, C.c_uint8, Lightbar, C.c_void_p)


def neutral(kind):
    return XUSBReport() if kind == "x360" else DS4Report(128, 128, 128, 128, 8, 0, 0, 0)


def encode(frame):
    """Encode standard normalized state using public XUSB ABI only; +Y is up."""
    def number(value, low, high):
        if type(value) not in (int, float) or not math.isfinite(value) or not low <= value <= high:
            raise OutputUnavailable("invalid_normalized_frame")
        return value
    buttons = frame["buttons"]
    if not isinstance(buttons, list) or any(b not in BUTTONS for b in buttons):
        raise OutputUnavailable("invalid_buttons")
    axes = []
    for side in ("left", "right"):
        stick = frame["sticks"][side]
        if not isinstance(stick, (tuple, list)) or len(stick) != 2:
            raise OutputUnavailable("explicit_stick_state_required")
        axes.extend(round(number(v, -1, 1) * (32768 if v < 0 else 32767)) for v in stick)
    triggers = [round(number(frame["triggers"][s], 0, 1) * 255) for s in ("left", "right")]
    return XUSBReport(sum(BUTTONS[b] for b in set(buttons)), *triggers, *axes)


def encode_ds4(frame):
    xusb = encode(frame)
    pressed = set(frame["buttons"])
    x = int("Right" in pressed) - int("Left" in pressed)
    y = int("Up" in pressed) - int("Down" in pressed)
    hat = {(0, 0): 8, (0, 1): 0, (1, 1): 1, (1, 0): 2, (1, -1): 3,
           (0, -1): 4, (-1, -1): 5, (-1, 0): 6, (-1, 1): 7}[(x, y)]
    mask = {"X": 0x10, "A": 0x20, "B": 0x40, "Y": 0x80,
            "LB": 0x100, "RB": 0x200, "Back": 0x1000, "Start": 0x2000, "LS": 0x4000, "RS": 0x8000}
    bits = hat | sum(bit for button, bit in mask.items() if button in pressed)
    if xusb.lt: bits |= 0x400
    if xusb.rt: bits |= 0x800
    def axis(value):
        return round(128 + value * (128 if value < 0 else 127))
    l, r = frame["sticks"]["left"], frame["sticks"]["right"]
    return DS4Report(axis(l[0]), axis(-l[1]), axis(r[0]), axis(-r[1]),
                     bits, int("Guide" in pressed), xusb.lt, xusb.rt)


class NativeViGEm:
    """Only trusted host approval may nominate a hash-pinned native DLL."""
    def __init__(self, approval, *, authorize_device_io=False):
        if authorize_device_io is not True:
            raise OutputUnavailable("device_io_not_authorized")
        if os.name != "nt" or C.sizeof(C.c_void_p) != 8:
            raise OutputUnavailable("windows_x64_required")
        path = Path(approval["path"])
        if (not path.is_absolute() or approval.get("sourceRevision") != SDK_REVISION
                or approval.get("license") != "MIT" or approval.get("distributionApproved") is not True
                or not approval.get("noticesPath")):
            raise OutputUnavailable("audited_runtime_required")
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        if digest != approval.get("sha256"):
            raise OutputUnavailable("runtime_hash_mismatch")
        self.dll = C.CDLL(str(path.resolve()), winmode=0x1100)
        pointer = C.c_void_p
        signatures = {
            "vigem_alloc": (pointer, []), "vigem_free": (None, [pointer]),
            "vigem_connect": (C.c_uint32, [pointer]), "vigem_disconnect": (None, [pointer]),
            "vigem_target_x360_alloc": (pointer, []), "vigem_target_free": (None, [pointer]),
            "vigem_target_add": (C.c_uint32, [pointer, pointer]),
            "vigem_target_remove": (C.c_uint32, [pointer, pointer]),
            "vigem_target_x360_update": (C.c_uint32, [pointer, pointer, XUSBReport]),
            "vigem_target_ds4_alloc": (pointer, []),
            "vigem_target_ds4_update": (C.c_uint32, [pointer, pointer, DS4Report]),
            "vigem_target_x360_register_notification": (C.c_uint32, [pointer, pointer, X360Feedback, pointer]),
            "vigem_target_ds4_register_notification": (C.c_uint32, [pointer, pointer, DS4Feedback, pointer]),
            "vigem_target_x360_unregister_notification": (None, [pointer]),
            "vigem_target_ds4_unregister_notification": (None, [pointer]),
        }
        for name, (result, args) in signatures.items():
            fn = getattr(self.dll, name)
            fn.restype, fn.argtypes = result, args
            setattr(self, name, fn)


class OutputLease:
    """Single-thread native ownership; every rejected frame tears down own target."""
    def __init__(self, backend, verify_source, verify_target, *, kind="x360", feedback_sink=None, clock=time.monotonic_ns):
        if kind not in ("x360", "ds4"): raise OutputUnavailable("unsupported_target")
        self.backend, self.verify_source, self.verify_target = backend, verify_source, verify_target
        self.kind, self.feedback_sink = kind, feedback_sink
        self.feedback = queue.Queue(maxsize=1)
        self.feedback_callback = None
        self.feedback_registered = self.feedback_authorized = False
        self.generation = 0
        self.clock = clock
        self.client = self.target = None
        self.connected = self.added = False
        self.state = "idle"
        self.owner = threading.get_ident()
        self.errors = []
        self.sequence = -1
        self.deadline = 0

    def _thread(self):
        if threading.get_ident() != self.owner:
            raise OutputUnavailable("output_owner_thread_required")

    def _check(self, result):
        if result != SUCCESS:
            raise OutputUnavailable(f"vigem_error_{result:08x}")

    def start(self, session, source_identity, *, authorize_device_io=False, authorize_feedback=False):
        self._thread()
        if self.state != "idle" or authorize_device_io is not True:
            raise OutputUnavailable("explicit_idle_activation_required")
        if not session or not source_identity or self.verify_source(session, source_identity, None) is not True:
            raise OutputUnavailable("physical_source_not_verified")
        self.session, self.identity = session, source_identity
        if authorize_feedback and not callable(self.feedback_sink):
            raise OutputUnavailable("feedback_sink_required")
        self.feedback_authorized = authorize_feedback is True
        self.state = "creating"
        b = self.backend
        try:
            self.client = b.vigem_alloc()
            if not self.client: raise OutputUnavailable("client_allocation_failed")
            self._check(b.vigem_connect(self.client)); self.connected = True
            self.target = getattr(b, f"vigem_target_{self.kind}_alloc")()
            if not self.target: raise OutputUnavailable("target_allocation_failed")
            self._check(b.vigem_target_add(self.client, self.target)); self.added = True
            self._update(neutral(self.kind))
            # Verifier must correlate the owned target with real OS enumeration.
            if self.verify_target(self.client, self.target) is not True:
                raise OutputUnavailable("target_enumeration_unverified")
            if self.verify_source(session, source_identity, self.target) is not True:
                raise OutputUnavailable("virtual_input_loop_or_source_lost")
            self.deadline = self.clock() + 100_000_000
            self.state = "active"
            if self.feedback_authorized:
                generation = self.generation
                def callback(client, target, large, small, _extra, _user):
                    if (generation != self.generation or self.state != "active"
                            or client != self.client or target != self.target): return
                    try: self.feedback.put_nowait((large / 255, small / 255))
                    except queue.Full:
                        try: self.feedback.get_nowait()
                        except queue.Empty: pass
                        try: self.feedback.put_nowait((large / 255, small / 255))
                        except queue.Full: pass
                self.feedback_callback = (X360Feedback if self.kind == "x360" else DS4Feedback)(callback)
                self._check(getattr(b, f"vigem_target_{self.kind}_register_notification")(
                    self.client, self.target, self.feedback_callback, None))
                self.feedback_registered = True
        except Exception:
            self.stop(); raise

    def submit(self, frame):
        self._thread()
        if self.state != "active": raise OutputUnavailable("not_active")
        try:
            now = self.clock()
            stamp, seq = frame["observedMonotonicNs"], frame["sequence"]
            if (frame.get("schemaVersion") != 1 or frame.get("session") != self.session
                    or frame.get("identity") != self.identity
                    or type(seq) is not int or seq <= self.sequence
                    or type(stamp) is not int or not 0 <= now - stamp <= 100_000_000
                    or now > self.deadline):
                raise OutputUnavailable("stale_or_foreign_frame")
            if self.verify_source(self.session, self.identity, self.target) is not True:
                raise OutputUnavailable("source_ownership_lost")
            report = encode(frame) if self.kind == "x360" else encode_ds4(frame)
            self._update(report)
            self.sequence, self.deadline = seq, stamp + 100_000_000
            self._feedback()
        except Exception:
            self.stop(); raise

    def tick(self):
        self._thread()
        if self.state == "active" and self.clock() > self.deadline:
            self.stop()
        elif self.state == "active":
            try: self._feedback()
            except Exception:
                self.stop(); raise

    def _update(self, report):
        self._check(getattr(self.backend, f"vigem_target_{self.kind}_update")(self.client, self.target, report))

    def _feedback(self):
        if not self.feedback_authorized: return
        try: motors = self.feedback.get_nowait()
        except queue.Empty: return
        if self.verify_source(self.session, self.identity, self.target) is not True:
            raise OutputUnavailable("feedback_source_lost")
        self.feedback_sink(self.session, self.identity, *motors)

    def stop(self):
        self._thread()
        if self.state == "closed": return
        self.state = "restoring"
        self.generation += 1
        b = self.backend
        def attempt(fn, *args, check=False):
            try:
                result = fn(*args)
                if check: self._check(result)
            except Exception as error: self.errors.append(str(error))
        if self.added:
            if self.feedback_registered:
                attempt(getattr(b, f"vigem_target_{self.kind}_unregister_notification"), self.target)
            if self.feedback_authorized:
                attempt(self.feedback_sink, self.session, self.identity, 0.0, 0.0)
            attempt(self._update, neutral(self.kind))
            attempt(b.vigem_target_remove, self.client, self.target, check=True)
        if self.connected: attempt(b.vigem_disconnect, self.client)
        if self.target: attempt(b.vigem_target_free, self.target)
        if self.client: attempt(b.vigem_free, self.client)
        self.client = self.target = None
        self.added = self.connected = False
        self.state = "cleanup_error" if self.errors else "closed"


def run_output_worker(factory, frames, cancel, session, source_identity, *, authorize_device_io=False, authorize_feedback=False):
    """Local bounded queue only, never frontend RPC polling; no implicit restart."""
    if authorize_device_io is not True:
        raise OutputUnavailable("device_io_not_authorized")
    if cancel.is_set():
        return {"state": "closed", "cleanupErrors": []}
    if not isinstance(frames, queue.Queue) or frames.maxsize != 1:
        raise OutputUnavailable("bounded_local_single_frame_queue_required")
    lease = factory()
    try:
        lease.start(session, source_identity, authorize_device_io=True, authorize_feedback=authorize_feedback)
        while not cancel.is_set() and lease.state == "active":
            try: frame = frames.get(timeout=0.01)
            except queue.Empty: lease.tick(); continue
            lease.submit(frame)
    finally:
        lease.stop()
    return {"state": lease.state, "cleanupErrors": lease.errors}
