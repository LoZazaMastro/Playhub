"""Real UDP loopback integration with explicitly MOCK sensor fixtures; no hardware."""

from dataclasses import replace
import math
import socket
import struct
import unittest
import zlib

from controller_porting.dsu import (DsuServer, MotionSample, sample_from_si, parse_request,
                                   VERSION_REQUEST, PORTS_REQUEST, DATA_REQUEST)


def request(kind, body=b"", client_id=123):
    raw = bytearray(struct.pack("<4sHHIII", b"DSUC", 1001, len(body) + 4, 0, client_id, kind) + body)
    struct.pack_into("<I", raw, 8, zlib.crc32(raw) & 0xffffffff)
    return bytes(raw)


def decode(raw):
    magic, version, size, crc, server, kind = struct.unpack_from("<4sHHIII", raw)
    assert magic == b"DSUS" and version == 1001 and len(raw) == size + 16
    clean = bytearray(raw)
    clean[8:12] = bytes(4)
    assert zlib.crc32(clean) & 0xffffffff == crc
    return kind, raw[20:], server


class UDPTests(unittest.TestCase):
    def setUp(self):
        self.now = 1_000_000_000
        self.server = DsuServer(port=0, test_mode=True, clock=lambda: self.now)
        self.address = self.server.start(authorized=True)
        self.addCleanup(self.server.close)
        self.client = self.new_client()

    def new_client(self):
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.bind(("127.0.0.1", 0))
        sock.settimeout(.06)
        self.addCleanup(sock.close)
        return sock

    def sample(self, timestamp=1000, **changes):
        value = MotionSample("mock-session", timestamp, self.now, (0., 1., 0.), (10., 20., 30.), "mock")
        return replace(value, **changes)

    def tick(self, advance=10_000_000):
        self.now += advance
        self.server.tick()

    def subscribe(self, flags=1, slot=0, mac=bytes(6), sock=None):
        (sock or self.client).sendto(request(DATA_REQUEST, bytes((flags, slot)) + mac), self.address)

    def quiet(self, sock=None):
        with self.assertRaises(socket.timeout):
            (sock or self.client).recvfrom(1024)

    def test_version_discovery_and_absent_ports_over_real_udp(self):
        self.client.sendto(request(VERSION_REQUEST), self.address)
        self.client.sendto(request(PORTS_REQUEST, struct.pack("<i4B", 4, 0, 1, 2, 3)), self.address)
        self.tick()
        kind, body, server_id = decode(self.client.recv(1024))
        self.assertEqual((kind, body), (VERSION_REQUEST, struct.pack("<H", 1001)))
        for slot in range(4):
            kind, body, other_id = decode(self.client.recv(1024))
            self.assertEqual(kind, PORTS_REQUEST)
            self.assertEqual(body, bytes((slot,)) + bytes(11))
            self.assertEqual(server_id, other_id)
        self.subscribe()
        self.tick()
        self.quiet()
        self.assertEqual(self.server.status()["freshSlots"], [])

    def test_motion_packet_wire_offsets_crc_units_and_neutral_controls(self):
        self.server.publish(0, self.sample(), connection=1)
        self.subscribe()
        self.tick()
        raw = self.client.recv(1024)
        self.assertEqual(len(raw), 100)
        kind, body, _ = decode(raw)
        self.assertEqual(kind, DATA_REQUEST)
        self.assertEqual(body[:4], bytes((0, 2, 2, 1)))
        self.assertEqual(body[11], 1)
        self.assertEqual(body[16:20], bytes(4))
        self.assertEqual(body[20:24], bytes((128,) * 4))
        self.assertEqual(body[24:48], bytes(24))
        self.assertEqual(struct.unpack_from("<Q6f", body, 48), (1000, 0., 1., 0., 10., 20., 30.))
        self.assertTrue(self.server.status()["testMode"])
        self.assertFalse(self.server.status()["hardwareValidated"])

    def test_rate_latest_sample_and_no_resend_without_publish(self):
        self.server.publish(0, self.sample())
        self.subscribe()
        self.tick()
        self.client.recv(1024)
        self.server.publish(0, self.sample(1001))
        self.tick(1_000_000)
        self.quiet()
        self.server.publish(0, self.sample(1002))
        self.tick()
        _, body, _ = decode(self.client.recv(1024))
        self.assertEqual(struct.unpack_from("<Q", body, 48)[0], 1002)
        self.assertEqual(struct.unpack_from("<I", body, 12)[0], 1)
        self.tick()
        self.quiet()

    def test_subscription_expires_even_after_new_sensor_data(self):
        self.server.publish(0, self.sample())
        self.subscribe()
        self.tick()
        self.client.recv(1024)
        self.now += 5_000_000_001
        self.server.publish(0, self.sample(1001))
        self.tick()
        self.quiet()
        self.assertEqual(self.server.status()["clients"], 0)

    def test_stalled_accel_not_kept_alive_by_gyro_only_updates(self):
        self.server.publish(0, self.sample())
        self.subscribe()
        self.tick()
        self.client.recv(1024)
        self.now += 240_000_000
        self.server.publish(0, self.sample(gyro_dps=(1., 2., 3.)))
        self.tick()
        self.quiet()
        self.assertEqual(self.server.status()["freshSlots"], [])

    def test_mac_and_slot_filtering_with_multiple_clients(self):
        mac = bytes.fromhex("021122334455")
        self.server.publish(1, self.sample(), mac=mac)
        self.subscribe(slot=0)
        second = self.new_client()
        self.subscribe(flags=2, mac=mac, sock=second)
        self.tick()
        self.quiet()
        self.assertEqual(decode(second.recv(1024))[1][0], 1)
        self.assertEqual(self.server.status()["clients"], 1)

    def test_malformed_crc_unknown_commands_and_bad_slots_are_ignored(self):
        self.server.publish(0, self.sample())
        bad_crc = bytearray(request(VERSION_REQUEST))
        bad_crc[8] ^= 1
        for data in [b"", b"DSUC", bytes(bad_crc), request(0x110002, bytes(10)),
                     request(PORTS_REQUEST, struct.pack("<iB", -1, 0)),
                     request(DATA_REQUEST, bytes((1, 9)) + bytes(6))]:
            self.client.sendto(data, self.address)
        self.tick()
        self.quiet()

    def test_bad_sample_invalidates_source_without_synthetic_fallback(self):
        self.server.publish(0, self.sample())
        for sample in [self.sample(accel_g=(math.nan, 0., 1.)),
                       self.sample(gyro_dps=(0., math.inf, 0.)),
                       self.sample(coordinate_frame="SDL"),
                       self.sample(sampled_at_ns=self.now + 1)]:
            with self.assertRaises(ValueError):
                self.server.publish(0, sample)
            self.assertEqual(self.server.status()["freshSlots"], [])

    def test_mock_guard_optin_guard_and_port_exclusion(self):
        production = DsuServer(port=0, clock=lambda: self.now)
        with self.assertRaises(ValueError):
            production.publish(0, self.sample())
        with self.assertRaises(PermissionError):
            production.start()
        with self.assertRaises(ValueError):
            DsuServer(test_mode=True)
        collision = DsuServer(port=self.address[1])
        self.addCleanup(collision.close)
        with self.assertRaises(OSError):
            collision.start(authorized=True)
        self.assertFalse(collision.status()["running"])

    def test_disconnect_requires_new_subscription_and_timestamp_regression_rejected(self):
        self.server.publish(0, self.sample())
        with self.assertRaises(ValueError):
            self.server.publish(0, self.sample(999))
        self.server.publish(0, self.sample())
        self.subscribe()
        self.tick()
        self.client.recv(1024)
        self.server.disconnect(0)
        self.server.publish(0, self.sample(1, source_session="new-mock-session"))
        self.tick()
        self.quiet()

    def test_request_budget_and_client_limit_bound_real_udp_work(self):
        for _ in range(20):
            self.client.sendto(request(VERSION_REQUEST), self.address)
        self.tick()
        for _ in range(16):
            self.assertEqual(decode(self.client.recv(1024))[0], VERSION_REQUEST)
        self.quiet()
        self.tick()
        for _ in range(4):
            self.client.recv(1024)
        self.server.publish(0, self.sample())
        clients = [self.new_client() for _ in range(9)]
        for client in clients:
            self.subscribe(sock=client)
        self.tick()
        for client in clients[:8]:
            self.assertEqual(decode(client.recv(1024))[0], DATA_REQUEST)
        self.quiet(clients[8])
        self.assertEqual(self.server.status()["clients"], 8)

    def test_reader_schema_to_real_udp_has_no_control_mapping(self):
        from controller_porting.dsu_bridge import sample_from_reader_frame, CEMU_PARITY, SDL_FRAME
        frame = {"schemaVersion": 1, "session": "mock-reader", "identity": "mock-device",
                 "sourceKind": "mock", "motionCoordinateFrame": SDL_FRAME,
                 "buttons": ["A"], "sticks": [1, 1],
                 "sensorCapabilities": {"accel": True, "gyro": True},
                 "motion": {"accel": {"unit": "m/s^2", "value": (9.80665, 0., 0.),
                                      "timestampNs": 9876543, "receivedMonotonicNs": self.now},
                            "gyro": {"unit": "rad/s", "value": (0., math.pi, 0.),
                                     "timestampNs": 9876555, "receivedMonotonicNs": self.now}}}
        sample = sample_from_reader_frame(frame, mapping=CEMU_PARITY, now_ns=self.now)
        self.server.publish(0, sample)
        self.subscribe()
        self.tick()
        _, body, _ = decode(self.client.recv(1024))
        self.assertEqual(struct.unpack_from("<Q6f", body, 48), (9876, -1., 0., 0., 0., -180., 0.))
        self.assertEqual(body[16:20], bytes(4))
        self.assertEqual(body[20:24], bytes((128,) * 4))

    def test_counter_wrap_and_close_release_bound_port(self):
        self.server.publish(0, self.sample())
        self.subscribe()
        self.tick()
        self.client.recv(1024)
        self.server._clients[self.client.getsockname()].counter = 0xffffffff
        for timestamp, expected in [(1001, 0xffffffff), (1002, 0)]:
            self.server.publish(0, self.sample(timestamp))
            self.tick()
            _, body, _ = decode(self.client.recv(1024))
            self.assertEqual(struct.unpack_from("<I", body, 12)[0], expected)
        self.server.close()
        self.server.close()
        self.assertFalse(self.server.status()["running"])
        replacement = DsuServer(port=self.address[1])
        self.addCleanup(replacement.close)
        self.assertEqual(replacement.start(authorized=True), self.address)


class PureTests(unittest.TestCase):
    def test_si_units_preserve_timestamp_and_explicit_basis(self):
        sample = sample_from_si(source_session="fixture", timestamp_us=42, sampled_at_ns=123,
                                accel_m_s2=(9.80665, 0., -9.80665), gyro_rad_s=(math.pi, 0., -math.pi),
                                coordinate_frame="dsu", source_kind="mock")
        self.assertEqual(sample.accel_g, (1., 0., -1.))
        self.assertEqual(sample.gyro_dps, (180., 0., -180.))
        self.assertEqual(sample.timestamp_us, 42)
        with self.assertRaises(ValueError):
            sample_from_si(source_session="fixture", timestamp_us=42, sampled_at_ns=123,
                           accel_m_s2=(0, 0, 0), gyro_rad_s=(0, 0, 0), coordinate_frame="unknown")

    def test_framing_truncates_declared_padding_but_rejects_short_payload(self):
        raw = request(VERSION_REQUEST)
        self.assertEqual(parse_request(raw + b"padding"), (123, VERSION_REQUEST, ()))
        self.assertIsNone(parse_request(raw[:-1]))
        self.assertIsNone(parse_request(raw + bytes(1025)))
