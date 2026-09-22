"""HDR changes require actual Windows capabilities, never a stale opt-in file."""
import unittest
from unittest.mock import patch
import test_display_entrypoints as entrypoints

class HdrSafetyGate(unittest.TestCase):
    def setUp(self):
        self.fixture=entrypoints.DisplayEntrypointTests();self.fixture.setUp();self.addCleanup(self.fixture.doCleanups)
        self.plugin=self.fixture.plugin;self.runtime=self.fixture.module._runtime
    def test_supported_native_target_is_available_without_flag_file(self):
        state={'real_state':True,'available':True,'targets':[{'supported':True,'force_disabled':False}]}
        with patch.object(self.runtime,'_get_hdr_status_sync',return_value=state):self.assertTrue(self.plugin._hdr_write_allowed())
    def test_unreadable_unsupported_or_policy_disabled_targets_fail_closed(self):
        for state in ({},{'available':True},{'available':True,'real_state':True,'targets':[]},
                      {'available':True,'real_state':True,'targets':[{'supported':True,'force_disabled':True}]}):
            with patch.object(self.runtime,'_get_hdr_status_sync',return_value=state):self.assertFalse(self.plugin._hdr_write_allowed())
    def test_read_failure_does_not_write(self):
        with patch.object(self.runtime,'_get_hdr_status_sync',side_effect=OSError('unavailable')):
            result=self.plugin._begin_display_change({'kind':'hdr','enabled':True})
            self.assertEqual(result['code'],'hdr_write_disabled')
    def test_hdr_preview_has_30_seconds_and_can_revert(self):
        state={'real_state':True,'available':True,'targets':[{'supported':True,'force_disabled':False}]}
        with patch.object(self.runtime,'_get_hdr_status_sync',return_value=state):
            result=self.plugin._begin_display_change({'kind':'hdr','enabled':True})
            self.assertTrue(result['ok']);self.assertEqual(result['secondsRemaining'],30)
            self.assertTrue(self.fixture.hdr['display-a'])
            reverted=self.plugin._finish_display_change({'token':result['token'],'keep':False})
            self.assertTrue(reverted['ok']);self.assertFalse(self.fixture.hdr['display-a'])

if __name__=='__main__':unittest.main()
