"""Validate published workbook editions through the actual daily endpoint."""
import datetime
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

from quick_settings.daily_history import DailyHistory

ROOT = Path(__file__).resolve().parents[1]


class ImportedEditionTests(unittest.TestCase):
    def test_decky_direct_file_loading(self):
        spec = importlib.util.spec_from_file_location('playhub_daily_history_test', ROOT / 'quick_settings/daily_history.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as directory:
            payload = module.DailyHistory(directory, today=lambda: datetime.date(2026, 9, 9)).get('it')
            self.assertEqual(payload['editorial']['id'], 'playstation')

    def test_complete_media_editions_have_bound_unique_images(self):
        editions = json.loads((ROOT / 'quick_settings/history_imported.json').read_text('utf-8'))['entries']
        manifest = json.loads((ROOT / 'quick_settings/history_images.json').read_text('utf-8-sig'))
        with tempfile.TemporaryDirectory() as directory:
            for edition in editions:
                if edition.get('media_status') == 'incomplete':
                    continue  # Partial workbook images are audited separately below.
                date = datetime.date.fromisoformat('2026-' + edition['anniversary']['date'])
                for locale in edition['locales']:
                    reader = DailyHistory(directory, today=lambda: date)
                    payload = reader.get(locale)
                    stories = [payload, *payload.get('also', [])]
                    story = next(s for s in stories if s['editorial']['id'] == edition['theme']['id'])
                    self.assertEqual(story['year'], edition['anniversary'].get('year'))
                    self.assertEqual(story['connections'], [])
                    self.assertTrue(story['editorial']['intro'])
                    media = manifest[story['editorial']['media_id']]
                    files = [story['editorial']['intro_image_file'], *[c['image_file'] for c in story['editorial']['chapters']]]
                    self.assertEqual(len(files), len(set(files)))
                    hashes = set()
                    for filename in files:
                        self.assertTrue(any(m['file'] == filename and m.get('kind') != 'cover' for m in media))
                        hashes.add(hashlib.sha256((ROOT / 'src/assets/history' / filename).read_bytes()).hexdigest())
                    self.assertEqual(len(hashes), len(files))
                    if story['editorial']['kind'] == 'game':
                        self.assertTrue(any(m.get('kind') == 'cover' for m in media), story['article']['title'])

    def test_partial_media_are_declared_and_all_texts_load(self):
        editions = json.loads((ROOT / 'quick_settings/history_imported.json').read_text('utf-8'))['entries']
        manifest = json.loads((ROOT / 'quick_settings/history_images.json').read_text('utf-8-sig'))
        with tempfile.TemporaryDirectory() as directory:
            for edition in editions:
                if edition.get('media_status') != 'incomplete':
                    continue
                missing = {item['order'] for item in edition['missing_media']}
                self.assertTrue(missing)
                theme = edition['theme']
                files = {m['file']: m for m in manifest.get(theme['media_id'], [])}
                for index, chapter in enumerate(theme['chapters'], 1):
                    if chapter['image_file'] == '__pending__':
                        self.assertIn(index, missing)
                    else:
                        self.assertNotIn(index, missing)
                        image = files[chapter['image_file']]
                        self.assertEqual(image['subject'], chapter['subject'])
                        self.assertNotEqual(image['kind'], 'cover')
                        self.assertTrue((ROOT / 'src/assets/history' / image['file']).is_file())
                date = datetime.date.fromisoformat('2026-' + edition['anniversary']['date'])
                reader = DailyHistory(directory, today=lambda: date)
                for locale in edition['locales']:
                    payload = reader.get(locale)
                    self.assertEqual(payload['editorial']['id'], theme['id'])
                    self.assertEqual(len(payload['editorial']['chapters']), len(theme['chapters']))
                    self.assertEqual(payload['article']['title'], theme['subject'])
                    self.assertEqual(payload['editorial']['intro'], theme['intro'][locale])
                    self.assertEqual(payload['year'], edition['anniversary'].get('year'))
                    for chapter in payload['editorial']['chapters']:
                        self.assertTrue(chapter['kicker'])
                        self.assertTrue(chapter['body'])

    def test_console_priority_on_september_ninth(self):
        with tempfile.TemporaryDirectory() as directory:
            payload = DailyHistory(directory, today=lambda: datetime.date(2026, 9, 9)).get('it')
            self.assertEqual(payload['editorial']['id'], 'playstation')
            self.assertEqual([s['editorial']['id'] for s in payload['also']], ['dreamcast', 'crash'])


if __name__ == '__main__':
    unittest.main()
