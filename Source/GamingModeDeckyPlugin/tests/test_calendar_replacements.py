"""Regression coverage for the proposed annual de-duplication, before publishing it."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('calendar_replacements', ROOT / 'tools/apply-calendar-replacements.py')
api = importlib.util.module_from_spec(spec)
spec.loader.exec_module(api)


class CalendarReplacementTests(unittest.TestCase):
    def setUp(self):
        self.proposal = api.read_json(ROOT / 'tools/calendar-replacements.json')
        self.days = api.read_json(ROOT / 'quick_settings/history_days.json')
        self.catalog = api.read_json(ROOT / 'quick_settings/history_editorial.json')
        self.manifest = api.read_json(ROOT / 'quick_settings/history_images.json')

    def test_projected_calendar_has_365_unique_subjects(self):
        staged = api.stage(self.proposal, self.days, self.catalog, self.manifest, {})
        result = api.projected_year(*staged[:2])
        self.assertEqual(result['duplicates'], [])
        self.assertEqual(result['unique_subjects'], 365)

    def test_stage_is_pure_and_preserves_creators_and_anniversaries(self):
        original = copy.deepcopy((self.days, self.catalog, self.manifest))
        _, changed, _ = api.stage(self.proposal, self.days, self.catalog, self.manifest, {})
        self.assertEqual((self.days, self.catalog, self.manifest), original)
        self.assertEqual(changed['anniversaries'], self.catalog['anniversaries'])
        self.assertEqual(changed['events'], self.catalog['events'])
        self.assertEqual([x for x in changed['themes'] if x['id'] != 'calendar-education-logo'],
                         [x for x in self.catalog['themes'] if x['id'] != 'calendar-education-logo'])
        education = next(x for x in changed['themes'] if x['id'] == 'calendar-education-logo')
        self.assertEqual(education['topic'], 'Logo (programming language)')
        self.assertEqual(education['media_id'], 'editorial-logo')

    def test_staging_twice_is_idempotent(self):
        staged = api.stage(self.proposal, self.days, self.catalog, self.manifest, {})
        self.assertEqual(api.stage(self.proposal, *staged, {}), staged)

    def test_missing_or_repeated_images_block_application(self):
        with tempfile.TemporaryDirectory() as temporary:
            assets = Path(temporary)
            self.assertEqual(len(api.validate_images(self.proposal, {}, assets)), 14)
            one = {'replacements': [self.proposal['replacements'][0]]}
            images = {one['replacements'][0]['media_id']: [
                {'file': 'one.png', 'kind': 'hardware'}, {'file': 'two.png', 'kind': 'hardware'}]}
            self.assertTrue(api.validate_images(one, images, assets))
            for name in ('one.png', 'two.png'):
                (assets / name).write_bytes(b'\x89PNG\r\n\x1a\n' + b'identical test bytes')
            self.assertTrue(any('repeated image' in error for error in api.validate_images(one, images, assets)))


if __name__ == '__main__':
    unittest.main()
