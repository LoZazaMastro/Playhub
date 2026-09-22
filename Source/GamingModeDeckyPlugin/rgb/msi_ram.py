"""Offline-verifiable Claw 8 EX RAM protocol, not yet enabled for live devices.

Protocol facts adapted from ClawConfigurator (MIT); see LICENSE-ClawConfigurator.txt.
Transport injection is for protocol verification, not a public hardware enable switch.
"""

from dataclasses import dataclass
import threading
import time

from .protocols import validate_color


LIGHT_OFFSET = 586
LIGHT_LENGTH = 883
STATIC_LENGTH = 32
MAX_PAYLOAD = 55


@dataclass(frozen=True)
class Snapshot:
    identity: str
    data: bytes


def frame(opcode, offset, payload):
    """Only RAM writes and profile reads exist here; ROM sync has no encoder."""
    if opcode not in (0x04, 0x21):
        raise ValueError("opcode_not_permitted")
    if type(offset) is not int:
        raise ValueError("invalid_offset")
    if opcode == 0x04:
        if type(payload) is not int:
            raise ValueError("invalid_read_length")
        length, data = payload, b""
    else:
        if not isinstance(payload, bytes):
            raise ValueError("invalid_write_payload")
        length, data = len(payload), payload
        # Only active index + first static keyframe, never other controller settings.
        if offset != LIGHT_OFFSET or length != STATIC_LENGTH:
            raise ValueError("write_outside_owned_static_range")
    if not 1 <= length <= MAX_PAYLOAD or not LIGHT_OFFSET <= offset < LIGHT_OFFSET + LIGHT_LENGTH:
        raise ValueError("range_not_permitted")
    if offset + length > LIGHT_OFFSET + LIGHT_LENGTH:
        raise ValueError("range_not_permitted")
    return (bytes((0x0F, 0, 0, 0x3C, opcode, 0,
                   offset >> 8, offset & 255, length)) + data).ljust(64, b"\x00")


def validate_snapshot(data):
    if not isinstance(data, bytes) or len(data) != LIGHT_LENGTH:
        raise ValueError("invalid_light_block_length")
    if data[0] > 3:
        raise ValueError("unknown_active_animation")
    for index in range(4):
        start = 1 + index * 220
        count, effect, speed, brightness = data[start:start + 4]
        if count > 8 or speed > 20 or brightness > 100:
            raise ValueError("unknown_animation_layout")
        if count and effect != 9:
            raise ValueError("unknown_animation_effect")
        if index == data[0] and not count:
            raise ValueError("empty_active_animation")
    if data[881] != 0:
        raise ValueError("audio_rhythm_active_or_unknown")
    return data


def static_block(current, color, brightness, power):
    validate_snapshot(current)
    color = validate_color(color, brightness, power)
    result = bytearray(current)
    result[:STATIC_LENGTH] = (bytes((0, 1, 9, 3, brightness if power else 0))
                              + bytes(color if power else (0, 0, 0)) * 9)
    return bytes(result)


class RamTransaction:
    """One channel/identity and an in-memory original-state lease.

    channel.send(bytes) returns an exact byte count; channel.read(timeout_seconds)
    returns one report or raises TimeoutError. identity must identify the same
    device/firmware/session throughout. The production backend does NOT construct
    this class until a documented exact identity can be approved.
    """

    def __init__(self, channel, *, clock=time.monotonic):
        self._channel = channel
        self._identity = channel.identity
        self._clock = clock
        self._lock = threading.Lock()
        self._original = None
        self._expected = None
        self._uncertain = False
        self._tainted = False

    def _check_identity(self):
        if self._channel.identity != self._identity:
            raise OSError("channel_identity_changed")

    def _send(self, report):
        self._check_identity()
        if self._channel.send(report) != len(report):
            raise OSError("short_write")

    def _read(self):
        if self._tainted:
            raise OSError("channel_requires_reopen")
        try:
            return self._read_impl()
        except (OSError, ValueError):
            # A timed-out response could match a subsequent request at the same offset.
            self._tainted = True
            raise

    def _read_impl(self):
        deadline = self._clock() + 3.0
        block = bytearray()
        ignored = 0
        for done in range(0, LIGHT_LENGTH, MAX_PAYLOAD):
            offset = LIGHT_OFFSET + done
            length = min(MAX_PAYLOAD, LIGHT_LENGTH - done)
            if self._clock() >= deadline:
                raise TimeoutError("profile_read_timeout")
            self._send(frame(0x04, offset, length))
            while True:
                remaining = deadline - self._clock()
                if remaining <= 0 or ignored >= 256:
                    raise TimeoutError("profile_read_timeout")
                report = self._channel.read(remaining)
                self._check_identity()
                # Bare 06h ACKs and unsolicited events do not establish write success.
                if (isinstance(report, bytes) and len(report) == 64
                        and report[:6] == bytes((0x10, 0, 0, 0x3C, 0x05, 0))
                        and report[6:9] == bytes((offset >> 8, offset & 255, length))):
                    block.extend(report[9:9 + length])
                    break
                ignored += 1
        data = validate_snapshot(bytes(block))
        return Snapshot(self._identity, data)

    def read_snapshot(self, *, user_requested=False):
        if user_requested is not True:
            raise ValueError("explicit_user_request_required")
        with self._lock:
            return self._read()

    def apply_static(self, color, brightness=100, power=True, *, user_requested=False):
        if user_requested is not True:
            return {"ok": False, "error": "explicit_user_request_required"}
        try:
            color = validate_color(color, brightness, power)
        except ValueError as exc:
            return {"ok": False, "error": str(exc)}
        with self._lock:
            if self._uncertain:
                return {"ok": False, "error": "restore_or_new_session_required"}
            try:
                before = self._read()
                if self._expected is not None and before.data != self._expected.data:
                    return {"ok": False, "error": "external_state_changed"}
                desired = static_block(before.data, color, brightness, power)
                # Detect changes while a multi-report snapshot was assembled.
                if self._read() != before:
                    return {"ok": False, "error": "unstable_snapshot"}
            except (OSError, ValueError) as exc:
                return {"ok": False, "error": str(exc)}
            if desired == before.data:
                return {"ok": True, "hardware_state": "readback_matched",
                        "changed": False, "rom_sync_sent": False}
            if self._original is None:
                self._original = before
            self._expected = Snapshot(self._identity, desired)
            self._uncertain = True
            try:
                self._send(frame(0x21, LIGHT_OFFSET, desired[:STATIC_LENGTH]))
                if self._read() != self._expected:
                    raise OSError("readback_mismatch")
                self._uncertain = False
                return {"ok": True, "hardware_state": "readback_matched",
                        "changed": True, "rom_sync_sent": False,
                        "restore_requires_explicit_request": True}
            except (OSError, ValueError) as exc:
                return {"ok": False, "error": str(exc), "hardware_state": "unknown",
                        "rom_sync_sent": False, "automatic_retry": False}

    def restore(self, *, user_requested=False):
        if user_requested is not True:
            return {"ok": False, "error": "explicit_user_request_required"}
        with self._lock:
            if self._original is None:
                return {"ok": False, "error": "no_snapshot"}
            try:
                current = self._read()
                if current == self._original:
                    self._original = self._expected = None
                    self._uncertain = False
                    return {"ok": True, "hardware_state": "restored_readback", "changed": False}
                if current != self._expected:
                    return {"ok": False, "error": "restore_conflict", "hardware_state": "unknown"}
                if self._read() != current:
                    return {"ok": False, "error": "unstable_snapshot"}
                self._uncertain = True
                self._send(frame(0x21, LIGHT_OFFSET, self._original.data[:STATIC_LENGTH]))
                if self._read() != self._original:
                    raise OSError("restore_readback_mismatch")
                self._original = self._expected = None
                self._uncertain = False
                return {"ok": True, "hardware_state": "restored_readback", "changed": True}
            except (OSError, ValueError) as exc:
                return {"ok": False, "error": str(exc), "hardware_state": "unknown"}
