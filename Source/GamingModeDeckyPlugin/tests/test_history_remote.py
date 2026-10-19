import copy
import datetime
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from quick_settings import daily_history, history_imports, history_remote as remote


def fixture():
    days = {(datetime.date(2024, 1, 1) + datetime.timedelta(days=i)).strftime('%m-%d'):
            {'topic': 'Fixture', 'subject': 'Fixture', 'title': {'en': 'Remote title'},
             'intro': {'en': 'Remote story'}, 'media_id': 'fixture'} for i in range(366)}
    return {'schemaVersion': 1, 'revision': 1, 'editorial': {'themes': [{'id': 'fixture', 'topic': 'Fixture'}]},
            'editions': {'entries': []}, 'days': days,
            'images': {'fixture': [{'file': 'fixture.jpg',
                'url': 'https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/assets/on-this-day/v1/fixture.jpg',
                'subject': 'Fixture', 'bytes': 12, 'sha256': 'a' * 64}]}}


class Response(io.BytesIO):
    status = 200
    headers = {}


class RemoteEditorialTests(unittest.TestCase):
    def setUp(self):
        remote._states.clear()
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.data = fixture()

    def payload(self):
        return json.dumps(self.data).encode()

    def opener(self, payload=None):
        outer = self
        class Opener:
            def open(self, request, timeout):
                outer.assertEqual(request.full_url, remote.CATALOG_URL)
                outer.assertNotIn('api.github.com', request.full_url)
                outer.assertFalse(request.has_header('Authorization'))
                outer.assertFalse(request.has_header('Cookie'))
                outer.assertEqual(timeout, 10)
                return Response(outer.payload() if payload is None else payload)
        return Opener()

    def test_success_is_public_throttled_and_persisted_offline(self):
        with patch.object(remote.urllib.request, 'build_opener', return_value=self.opener()) as transport:
            self.assertEqual(remote.load_remote(self.directory.name)['revision'], 1)
            self.assertEqual(remote.load_remote(self.directory.name)['revision'], 1)
            self.assertEqual(transport.call_count, 1)
        remote._states.clear()
        with patch.object(remote.urllib.request, 'build_opener', side_effect=OSError('offline')) as transport:
            self.assertEqual(remote.load_remote(self.directory.name)['revision'], 1)
            transport.assert_not_called()

    def test_bad_response_and_revision_rollback_preserve_cache(self):
        with patch.object(remote.urllib.request, 'build_opener', return_value=self.opener()):
            remote.load_remote(self.directory.name)
        path = Path(self.directory.name) / 'editorial-catalog-v1.json'
        good = path.read_bytes()
        remote._states[str(path.resolve())]['next'] = 0
        with patch.object(remote.urllib.request, 'build_opener', return_value=self.opener(b'{"schemaVersion":2}')):
            self.assertEqual(remote.load_remote(self.directory.name)['revision'], 1)
        self.assertEqual(path.read_bytes(), good)
        state = remote._states[str(path.resolve())]
        state['data']['revision'] = 10
        state['next'] = 0
        with patch.object(remote.urllib.request, 'build_opener', return_value=self.opener()):
            self.assertEqual(remote.load_remote(self.directory.name)['revision'], 10)
        self.assertEqual(path.read_bytes(), good)

    def test_edited_document_is_applied_after_refresh_without_client_update(self):
        with patch.object(remote.urllib.request, 'build_opener', return_value=self.opener()):
            remote.load_remote(self.directory.name)
        path = Path(self.directory.name) / 'editorial-catalog-v1.json'
        remote._states[str(path.resolve())]['next'] = 0
        self.data['revision'] = 2
        self.data['days']['10-19']['title']['en'] = 'Changed remotely'
        with patch.object(remote.urllib.request, 'build_opener', return_value=self.opener()):
            catalog = remote.apply_remote(self.directory.name, 'en', history_imports.merge_editions)
        theme, _, _ = daily_history.select_editorial(catalog, datetime.date(2026, 10, 19))
        self.assertEqual(theme['title']['en'], 'Changed remotely')

    def test_invalid_media_duplicate_keys_and_oversize_are_rejected(self):
        for mutate in (
            lambda d: d['images']['fixture'][0].update(url='https://evil.invalid/fixture.jpg'),
            lambda d: d['images']['fixture'][0].update(bytes=100000000),
            lambda d: d.update(revision=True),
            lambda d: d.update(days={}),
            lambda d: d['editorial'].update(calendar=[]),
            lambda d: d['editorial'].update(anniversaries=None),
            lambda d: d['editorial']['themes'][0].update(subject_localized=None),
            lambda d: d['editorial']['themes'][0].update(chapters=[{'body': {'en': []}}]),
        ):
            data = copy.deepcopy(self.data)
            mutate(data)
            with self.assertRaises(ValueError):
                remote.parse_document(json.dumps(data).encode())
        with self.assertRaises(ValueError):
            remote.parse_document(b'{"schemaVersion":1,"schemaVersion":1}')
        with self.assertRaises(ValueError):
            remote.parse_document(b' ' * (remote.MAX_BYTES + 1))

    def test_public_snapshot_covers_every_day_and_every_media_byte(self):
        root = Path(__file__).resolve().parents[3]
        public = remote.parse_document((root / 'catalog/on-this-day.json').read_bytes())
        catalog = history_imports.merge_editions(public['editorial'], public['editions'], 'en')
        catalog['_remote_days'] = public['days']
        for offset in range(366):
            day = datetime.date(2024, 1, 1) + datetime.timedelta(days=offset)
            theme, _, _ = daily_history.select_editorial(catalog, day)
            self.assertTrue(theme.get('topic'), day)
        import hashlib
        for group in public['images'].values():
            for image in group:
                raw = (root / 'assets/on-this-day/v1' / image['file']).read_bytes()
                self.assertEqual(hashlib.sha256(raw).hexdigest(), image['sha256'])
                self.assertEqual(len(raw), image['bytes'])

    def test_response_attaches_remote_media_to_dynamic_day(self):
        service = daily_history.DailyHistory(self.directory.name, today=lambda: datetime.date(2026, 10, 19))
        history_imports._remote_module = remote
        with patch.object(remote.urllib.request, 'build_opener', return_value=self.opener()):
            story = service.get('en')
        self.assertEqual(story['editorial']['title'], 'Remote title')
        self.assertEqual(story['media'][0]['file'], 'fixture.jpg')
        self.assertEqual(story['media'][0]['sha256'], 'a' * 64)


if __name__ == '__main__':
    unittest.main()
