"""Exercise the actual ctypes reader/writer against the Windows SDK packet layout."""
import ctypes
import unittest
from unittest.mock import patch
import test_display_entrypoints as entrypoints


class HdrNativeContract(unittest.TestCase):
    def setUp(self):
        self.fixture = entrypoints.DisplayEntrypointTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.runtime = self.fixture.module._runtime
        self.target = self.runtime.DISPLAYCONFIG_PATH_TARGET_INFO()
        self.target.id = 42

    def read(self, flags, mode):
        def native(pointer):
            packet = pointer._obj
            self.assertEqual(packet.header.type, 15)
            self.assertEqual(packet.header.size, 36)
            packet.value = flags
            packet.colorEncoding = 2
            packet.bitsPerColorChannel = 10
            packet.activeColorMode = mode
            return 0
        return self.runtime._query_advanced_color(native, self.target)

    def test_sdk_26100_packet_size_and_field_offsets(self):
        packet = self.runtime.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
        self.assertEqual(ctypes.sizeof(packet), 36)
        self.assertEqual(packet.colorEncoding.offset, 24)
        self.assertEqual(packet.bitsPerColorChannel.offset, 28)
        self.assertEqual(packet.activeColorMode.offset, 32)

    def test_sdr_wide_color_is_not_hdr(self):
        state = self.read(0xC3, 1)
        self.assertFalse(state['supported'])
        self.assertFalse(state['enabled'])

    def test_hdr_user_preference_does_not_fake_active_hdr(self):
        state = self.read(0x31, 0)
        self.assertTrue(state['supported'])
        self.assertTrue(state['user_enabled'])
        self.assertFalse(state['enabled'])

    def test_actual_hdr_and_color_depth(self):
        state = self.read(0x33, 2)
        self.assertTrue(state['enabled'])
        self.assertEqual(state['bits_per_color_channel'], 10)
        self.assertEqual(state['color_encoding'], 2)

    def test_modern_rejection_never_retries_legacy_color_write(self):
        with patch.object(self.runtime, '_query_advanced_color', return_value={'supported': True, 'color_api': 'advanced_color_info_2'}), \
             patch.object(self.runtime, '_write_color_state', return_value=50) as write:
            result = self.runtime._write_hdr_target(self.target, True)
        self.assertFalse(result['accepted'])
        self.assertEqual(write.call_count, 1)
        self.assertEqual(write.call_args.args[2], 16)

    def test_legacy_reader_selects_legacy_writer(self):
        with patch.object(self.runtime, '_query_advanced_color', return_value={'supported': True, 'color_api': 'advanced_color_info'}), \
             patch.object(self.runtime, '_write_color_state', return_value=0) as write:
            result = self.runtime._write_hdr_target(self.target, False)
        self.assertTrue(result['accepted'])
        self.assertEqual(write.call_args.args[2], 10)

    def test_policy_bit_is_preserved(self):
        self.assertTrue(self.read(0x19, 0)['force_disabled'])

if __name__ == '__main__':
    unittest.main()
