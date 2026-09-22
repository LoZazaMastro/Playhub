"""Modeled protocol fixtures, not claims of hardware captures."""

import unittest

from rgb.msi_ram import (LIGHT_LENGTH, LIGHT_OFFSET, STATIC_LENGTH,
                         RamTransaction, frame, static_block)


def initial_block():
    data = bytearray(LIGHT_LENGTH)
    # Recorded upstream example: animation 0, #7F00FF, brightness 100, speed 17.
    data[:32] = bytes((0, 1, 9, 3, 100)) + bytes((127, 0, 255)) * 9
    # Distinct inactive data and reserved bytes ensure preservation is tested.
    data[40:48] = b"preserve"
    data[882] = 72
    return bytes(data)


class FakeChannel:
    def __init__(self):
        self.identity = "modeled-ex-session"
        self.data = initial_block()
        self.sent = []
        self.pending = []
        self.noise = []
        self.fail_write = False
        self.ignore_write = False
        self.short_write = False
        self.mutate_after_write = False

    def send(self, report):
        self.sent.append(report)
        opcode = report[4]
        offset = int.from_bytes(report[6:8], "big")
        length = report[8]
        assert report[5] == 0
        assert len(report) == 64
        if opcode == 4:
            payload = self.data[offset - LIGHT_OFFSET:offset - LIGHT_OFFSET + length]
            response = (bytes((0x10, 0, 0, 0x3C, 5, 0))
                        + report[6:9] + payload).ljust(64, b"\0")
            self.pending.extend(self.noise)
            self.noise = []
            self.pending.append(response)
        elif opcode == 0x21:
            if self.fail_write:
                raise OSError("disconnected")
            if not self.ignore_write:
                data = bytearray(self.data)
                data[offset - LIGHT_OFFSET:offset - LIGHT_OFFSET + length] = report[9:9 + length]
                self.data = bytes(data)
            if self.mutate_after_write:
                data = bytearray(self.data)
                data[100] ^= 1
                self.data = bytes(data)
            self.pending.append(bytes((0x10, 0, 0, 0x3C, 6, 0)).ljust(64, b"\0"))
            if self.short_write:
                return 12
        else:
            raise AssertionError("forbidden opcode")
        return 64

    def read(self, timeout):
        if not self.pending:
            raise TimeoutError("no response")
        return self.pending.pop(0)

    @property
    def writes(self):
        return [r for r in self.sent if r[4] == 0x21]


class RamTests(unittest.TestCase):
    def setUp(self):
        self.channel = FakeChannel()
        self.transaction = RamTransaction(self.channel)

    def apply(self, color=(10, 20, 30), **kwargs):
        return self.transaction.apply_static(color, user_requested=True, **kwargs)

    def test_no_operation_without_explicit_request(self):
        self.assertFalse(self.transaction.apply_static([1, 2, 3])["ok"])
        self.assertFalse(self.transaction.restore()["ok"])
        with self.assertRaises(ValueError):
            self.transaction.read_snapshot()
        self.assertEqual(self.channel.sent, [])

    def test_static_encoding_changes_exactly_owned_range(self):
        before = initial_block()
        after = static_block(before, [10, 20, 30], 50, True)
        self.assertEqual(len(after), 883)
        self.assertEqual(after[:32], bytes((0, 1, 9, 3, 50)) + bytes((10, 20, 30)) * 9)
        self.assertEqual(after[32:], before[32:])
        self.assertEqual(frame(0x21, 586, after[:32])[:9], bytes.fromhex("0f00003c2100024a20"))

    def test_forbidden_opcode_and_ranges(self):
        for opcode in (0x22, 0x23, 0x24, 0x28):
            with self.assertRaises(ValueError):
                frame(opcode, LIGHT_OFFSET, b"x")
        for offset, data in ((0, bytes(32)), (587, bytes(32)), (586, bytes(33))):
            with self.assertRaises(ValueError):
                frame(0x21, offset, data)
        with self.assertRaises(ValueError):
            frame(4, LIGHT_OFFSET + LIGHT_LENGTH - 1, 2)

    def test_chunked_read_correlates_header_selector_offset_and_length(self):
        valid_header = bytes.fromhex("1000003c0500024a37")
        noise = [b"short", bytes.fromhex("1000003c0600").ljust(64, b"\0")]
        for index in (0, 3, 4, 5, 6, 7, 8):
            bad = bytearray(valid_header.ljust(64, b"\0"))
            bad[index] ^= 1
            noise.append(bytes(bad))
        self.channel.noise = noise
        snapshot = self.transaction.read_snapshot(user_requested=True)
        self.assertEqual(snapshot.data, initial_block())
        self.assertEqual(len(self.channel.sent), 17)
        self.assertTrue(all(report[4] == 4 for report in self.channel.sent))

    def test_apply_readback_then_explicit_restore(self):
        original = self.channel.data
        result = self.apply()
        self.assertTrue(result["ok"])
        self.assertEqual(result["hardware_state"], "readback_matched")
        self.assertNotEqual(self.channel.data, original)
        result = self.transaction.restore(user_requested=True)
        self.assertEqual(result["hardware_state"], "restored_readback")
        self.assertEqual(self.channel.data, original)
        self.assertEqual(len(self.channel.writes), 2)
        self.assertTrue(all(r[4] in (4, 0x21) for r in self.channel.sent))

    def test_repeated_applies_keep_original_snapshot(self):
        original = self.channel.data
        self.assertTrue(self.apply()["ok"])
        self.assertTrue(self.apply((4, 5, 6))["ok"])
        self.assertTrue(self.transaction.restore(user_requested=True)["ok"])
        self.assertEqual(self.channel.data, original)

    def test_noop_does_not_write(self):
        result = self.apply((127, 0, 255))
        self.assertTrue(result["ok"])
        self.assertFalse(result["changed"])
        self.assertEqual(self.channel.writes, [])

    def test_off_is_black_zero_brightness_not_rom_sync(self):
        self.assertTrue(self.apply(power=False)["ok"])
        self.assertEqual(self.channel.data[4:32], bytes(28))
        self.assertFalse(any(report[4] == 0x22 for report in self.channel.sent))

    def test_ignored_write_does_not_succeed_on_bare_ack(self):
        self.channel.ignore_write = True
        result = self.apply()
        self.assertEqual(result["error"], "readback_mismatch")
        self.assertEqual(len(self.channel.writes), 1)
        self.assertEqual(self.apply()["error"], "restore_or_new_session_required")
        self.assertTrue(self.transaction.restore(user_requested=True)["ok"])
        self.assertEqual(len(self.channel.writes), 1)

    def test_partial_transport_completion_is_uncertain_not_retried(self):
        self.channel.short_write = True
        result = self.apply()
        self.assertEqual(result["error"], "short_write")
        self.assertEqual(len(self.channel.writes), 1)
        self.channel.short_write = False
        self.assertTrue(self.transaction.restore(user_requested=True)["ok"])

    def test_external_change_prevents_restore_overwrite(self):
        self.assertTrue(self.apply()["ok"])
        data = bytearray(self.channel.data)
        data[100] ^= 1
        self.channel.data = bytes(data)
        result = self.transaction.restore(user_requested=True)
        self.assertEqual(result["error"], "restore_conflict")
        self.assertEqual(len(self.channel.writes), 1)

    def test_external_change_during_write_prevents_false_success(self):
        self.channel.mutate_after_write = True
        self.assertEqual(self.apply()["error"], "readback_mismatch")
        self.assertEqual(self.transaction.restore(user_requested=True)["error"], "restore_conflict")

    def test_invalid_snapshot_and_audio_mode_never_write(self):
        for index, value in ((0, 4), (1, 9), (2, 8), (3, 21), (4, 101), (881, 1)):
            channel = FakeChannel()
            data = bytearray(channel.data)
            data[index] = value
            channel.data = bytes(data)
            result = RamTransaction(channel).apply_static([1, 2, 3], user_requested=True)
            self.assertFalse(result["ok"])
            self.assertEqual(channel.writes, [])

    def test_identity_change_never_writes(self):
        self.channel.identity = "another-controller"
        self.assertEqual(self.apply()["error"], "channel_identity_changed")
        self.assertEqual(self.channel.sent, [])

    def test_no_response_is_not_a_success(self):
        self.channel.read = lambda timeout: (_ for _ in ()).throw(TimeoutError("missing reply"))
        self.assertFalse(self.apply()["ok"])
        self.assertEqual(self.channel.writes, [])
        self.assertEqual(self.apply()["error"], "channel_requires_reopen")

    def test_restore_verifies_written_original(self):
        self.assertTrue(self.apply()["ok"])
        self.channel.ignore_write = True
        result = self.transaction.restore(user_requested=True)
        self.assertEqual(result["error"], "restore_readback_mismatch")
        self.assertEqual(len(self.channel.writes), 2)

    def test_moving_snapshot_is_rejected_before_write(self):
        original_read = self.channel.read
        final_reads = [0]

        def moving_read(timeout):
            report = original_read(timeout)
            if report[4] == 5 and report[6:8] == bytes.fromhex("05ba"):
                final_reads[0] += 1
                if final_reads[0] == 1:
                    changed = bytearray(self.channel.data)
                    changed[100] ^= 1
                    self.channel.data = bytes(changed)
            return report

        self.channel.read = moving_read
        result = self.apply()
        self.assertEqual(result["error"], "unstable_snapshot")
        self.assertEqual(self.channel.writes, [])

    def test_unrelated_report_flood_is_bounded(self):
        self.channel.read = lambda timeout: bytes(64)
        self.assertEqual(self.apply()["error"], "profile_read_timeout")
        self.assertEqual(self.channel.writes, [])


if __name__ == "__main__":
    unittest.main()
