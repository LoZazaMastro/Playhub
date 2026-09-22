"""Independent DSU v1001 sensor-only UDP transport; standard library only.

No reader, hardware access, controls, remapping, rumble, worker or auto-start.
Coordinates MUST already be in the DSU frame; see dsu-README.md.
"""

from dataclasses import dataclass, field
import math
import secrets
import socket
import struct
import threading
import time
import zlib

VERSION = 1001
VERSION_REQUEST = 0x100000
PORTS_REQUEST = 0x100001
DATA_REQUEST = 0x100002
HEADER = struct.Struct("<4sHHII")
MAX_CLIENTS = 8
MAX_REQUESTS_PER_TICK = 16
CLIENT_TTL_NS = 5_000_000_000
SAMPLE_TTL_NS = 250_000_000


@dataclass(frozen=True)
class MotionSample:
    source_session: str
    timestamp_us: int
    sampled_at_ns: int
    accel_g: tuple
    gyro_dps: tuple
    source_kind: str = "live"
    coordinate_frame: str = "dsu"

    def validate(self):
        if not isinstance(self.source_session, str) or not 1 <= len(self.source_session) <= 128:
            raise ValueError("invalid_source_session")
        if type(self.timestamp_us) is not int or not 0 < self.timestamp_us < 2 ** 64:
            raise ValueError("invalid_sensor_timestamp")
        if type(self.sampled_at_ns) is not int or self.sampled_at_ns <= 0:
            raise ValueError("invalid_observation_timestamp")
        if self.source_kind not in ("live", "mock") or self.coordinate_frame != "dsu":
            raise ValueError("unvalidated_sensor_basis")
        for vector in (self.accel_g, self.gyro_dps):
            if not isinstance(vector, tuple) or len(vector) != 3:
                raise ValueError("invalid_sensor_vector")
            if any(type(v) not in (int, float) or not math.isfinite(v) or abs(v) > 1e6 for v in vector):
                raise ValueError("invalid_sensor_value")


def sample_from_si(*, source_session, timestamp_us, sampled_at_ns,
                   accel_m_s2, gyro_rad_s, coordinate_frame, source_kind="live"):
    """Unit conversion only: caller must explicitly supply DSU-aligned vectors."""
    if coordinate_frame != "dsu":
        raise ValueError("coordinate_transform_required")
    result = MotionSample(source_session, timestamp_us, sampled_at_ns,
                          tuple(v / 9.80665 for v in accel_m_s2),
                          tuple(math.degrees(v) for v in gyro_rad_s), source_kind)
    result.validate()
    return result


def packet(message_type, body, server_id):
    payload = struct.pack("<I", message_type) + body
    data = bytearray(HEADER.pack(b"DSUS", VERSION, len(payload), 0, server_id) + payload)
    struct.pack_into("<I", data, 8, zlib.crc32(data) & 0xffffffff)
    return bytes(data)


def parse_request(data):
    if not 20 <= len(data) <= 1024:
        return None
    magic, version, size, crc, client_id = HEADER.unpack_from(data)
    if magic != b"DSUC" or version != VERSION or size < 4 or 16 + size > len(data):
        return None
    # The reference specifies truncating bytes beyond the declared length.
    data = bytearray(data[:16 + size])
    struct.pack_into("<I", data, 8, 0)
    if zlib.crc32(data) & 0xffffffff != crc:
        return None
    kind = struct.unpack_from("<I", data, 16)[0]
    body = bytes(data[20:])
    if kind == VERSION_REQUEST and not body:
        return client_id, kind, ()
    if kind == PORTS_REQUEST and 5 <= len(body) <= 8:
        count = struct.unpack_from("<i", body)[0]
        if 1 <= count <= 4 and len(body) == count + 4 and all(slot < 4 for slot in body[4:]):
            return client_id, kind, tuple(dict.fromkeys(body[4:]))
    if kind == DATA_REQUEST and len(body) == 8:
        flags, slot = body[:2]
        if flags <= 3 and (not flags & 1 or slot < 4):
            return client_id, kind, (flags, slot, body[2:])
    # Includes unofficial motor commands: never accepted or dispatched.
    return None


@dataclass
class _Source:
    sample: MotionSample
    mac: bytes
    connection: int
    accel_progress_ns: int
    revision: int = 1


@dataclass
class _Client:
    client_id: int
    expires: dict = field(default_factory=dict)
    sent: dict = field(default_factory=dict)
    counter: int = 0


class DsuServer:
    def __init__(self, *, port=26760, rate_hz=100, test_mode=False, clock=time.monotonic_ns):
        if type(port) is not int or not 0 <= port <= 65535:
            raise ValueError("invalid_port")
        if type(rate_hz) is not int or not 1 <= rate_hz <= 100:
            raise ValueError("rate_must_be_1_to_100_hz")
        if type(test_mode) is not bool or test_mode and port != 0:
            raise ValueError("mock_mode_requires_ephemeral_port")
        self._port, self._test_mode, self._clock = port, test_mode, clock
        self._period_ns = (1_000_000_000 + rate_hz - 1) // rate_hz
        self._socket = None
        self._lock = threading.RLock()
        self._sources, self._clients = {}, {}
        self._next_tick = 0
        self._server_id = 0
        self._send_errors = 0

    def start(self, *, authorized=False):
        with self._lock:
            if authorized is not True:
                raise PermissionError("dsu_not_authorized")
            if self._socket is not None:
                raise RuntimeError("already_started")
            sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            try:
                # No reuse: do not share an emulator's existing motion server port.
                if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
                    sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
                sock.bind(("127.0.0.1", self._port))
                sock.setblocking(False)
            except Exception:
                sock.close()
                raise
            self._socket = sock
            self._server_id = secrets.randbits(32)
            self._next_tick = 0
            return sock.getsockname()

    @staticmethod
    def _slot(slot):
        if type(slot) is not int or not 0 <= slot < 4:
            raise ValueError("invalid_slot")

    def publish(self, slot, sample, *, mac=b"\0" * 6, connection=0):
        """Called only by trusted reader glue, never from UDP/RPC client data."""
        self._slot(slot)
        with self._lock:
            try:
                if not isinstance(sample, MotionSample):
                    raise ValueError("motion_sample_required")
                sample.validate()
                if sample.source_kind == "mock" and not self._test_mode:
                    raise ValueError("mock_data_forbidden")
                if type(mac) is not bytes or len(mac) != 6 or type(connection) is not int or connection not in (0, 1, 2):
                    raise ValueError("invalid_source_metadata")
                now = self._clock()
                if not 0 <= now - sample.sampled_at_ns <= SAMPLE_TTL_NS:
                    raise ValueError("stale_or_future_sample")
                old = self._sources.get(slot)
                if old and (old.sample.source_session != sample.source_session or old.mac != mac):
                    raise ValueError("disconnect_before_source_change")
                if old and (sample.timestamp_us < old.sample.timestamp_us or sample.sampled_at_ns < old.sample.sampled_at_ns):
                    raise ValueError("sensor_timestamp_regressed")
                # Gyro-only updates may reuse accel timestamp, but cannot keep a
                # stalled accelerometer alive indefinitely with newer host time.
                progress = now if not old or sample.timestamp_us > old.sample.timestamp_us else old.accel_progress_ns
                revision = old.revision + 1 if old else 1
                self._sources[slot] = _Source(sample, mac, connection, progress, revision)
            except (ValueError, TypeError, OverflowError):
                self.disconnect(slot)
                raise

    def disconnect(self, slot):
        self._slot(slot)
        with self._lock:
            self._sources.pop(slot, None)
            for client in self._clients.values():
                client.expires.pop(slot, None)
                client.sent.pop(slot, None)

    def _fresh(self, source, now):
        return source is not None and 0 <= now - source.sample.sampled_at_ns <= SAMPLE_TTL_NS and 0 <= now - source.accel_progress_ns <= SAMPLE_TTL_NS

    def _metadata(self, slot, now):
        source = self._sources.get(slot)
        if not self._fresh(source, now):
            return bytes((slot,)) + bytes(10)
        # Model=full motion protocol class, not a claim of physical DS4 hardware.
        return bytes((slot, 2, 2, source.connection)) + source.mac + b"\0"

    def _send(self, kind, body, address):
        try:
            self._socket.sendto(packet(kind, body, self._server_id), address)
            return True
        except OSError:
            self._send_errors += 1
            return False

    def tick(self):
        """Nonblocking, host-scheduled work; no timers/threads created here."""
        with self._lock:
            if self._socket is None:
                return
            now = self._clock()
            if now < self._next_tick:
                return
            self._next_tick = now + self._period_ns
            for address, client in list(self._clients.items()):
                client.expires = {slot: until for slot, until in client.expires.items() if until > now}
                if not client.expires:
                    del self._clients[address]
            for _ in range(MAX_REQUESTS_PER_TICK):
                try:
                    data, address = self._socket.recvfrom(1025)
                except BlockingIOError:
                    break
                except OSError:
                    break
                if address[0] != "127.0.0.1":
                    continue
                request = parse_request(data)
                if request is None:
                    continue
                client_id, kind, args = request
                if kind == VERSION_REQUEST:
                    self._send(kind, struct.pack("<H", VERSION), address)
                elif kind == PORTS_REQUEST:
                    for slot in args:
                        self._send(kind, self._metadata(slot, now) + b"\0", address)
                else:
                    flags, requested_slot, mac = args
                    slots = [slot for slot, source in self._sources.items()
                             if self._fresh(source, now) and (flags == 0 or
                                 flags & 1 and slot == requested_slot or
                                 flags & 2 and mac != bytes(6) and source.mac == mac)]
                    if not slots:
                        continue
                    client = self._clients.get(address)
                    if client is None or client.client_id != client_id:
                        if client is None and len(self._clients) >= MAX_CLIENTS:
                            continue
                        client = self._clients[address] = _Client(client_id)
                    for slot in slots:
                        client.expires[slot] = now + CLIENT_TTL_NS
            for address, client in self._clients.items():
                for slot in client.expires:
                    source = self._sources.get(slot)
                    if not self._fresh(source, now) or client.sent.get(slot) == source.revision:
                        continue
                    body = bytearray(80)
                    body[:11] = self._metadata(slot, now)
                    body[11] = 1
                    struct.pack_into("<I", body, 12, client.counter)
                    body[20:24] = bytes((128, 128, 128, 128))
                    sample = source.sample
                    struct.pack_into("<Q6f", body, 48, sample.timestamp_us, *sample.accel_g, *sample.gyro_dps)
                    if self._send(DATA_REQUEST, body, address):
                        client.sent[slot] = source.revision
                        client.counter = (client.counter + 1) & 0xffffffff

    def status(self):
        with self._lock:
            now = self._clock()
            return {"running": self._socket is not None, "testMode": self._test_mode,
                    "clients": sum(any(t > now for t in c.expires.values()) for c in self._clients.values()),
                    "freshSlots": [slot for slot, source in self._sources.items() if self._fresh(source, now)],
                    "sendErrors": self._send_errors, "hardwareValidated": False,
                    "sensorOnly": True, "aim": False, "remapping": False}

    def close(self):
        with self._lock:
            if self._socket is not None:
                self._socket.close()
                self._socket = None
            self._sources.clear()
            self._clients.clear()
