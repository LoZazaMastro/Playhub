import importlib.util
from pathlib import Path
import unittest
spec = importlib.util.spec_from_file_location('backfill', Path(__file__).with_name('backfill-plugin-dates.py'))
backfill = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backfill)

def section(tag, published, committed='2020-01-01T00:00:00Z'):
    return ('<section><relative-time datetime="' + committed + '"></relative-time>'
            '<a href="/fixture/plugin/releases/tag/' + tag + '">tag</a> released this\n'
            '<relative-time datetime="' + published + '"></relative-time></section>')

class PublicationBackfillTests(unittest.TestCase):
    def test_publication_header_is_not_commit_or_atom_edit_time(self):
        rows = backfill.release_rows(section('v1', '2026-10-19T10:18:40Z'), 'fixture/plugin')
        self.assertEqual(rows[0]['publishedAt'], '2026-10-19T10:18:40Z')

    def test_first_is_minimum_of_complete_paginated_history(self):
        class Reader:
            def read(self, url):
                if '?page=2' in url:
                    return section('v1', '2025-01-01T12:00:00Z')
                return section('v2', '2026-01-01T12:00:00Z') + '<a href="/fixture/plugin/releases?page=2">Next</a>'
        value = backfill.github_dates({'repository': 'fixture/plugin'}, Reader(), 5)
        self.assertEqual(value['firstPublishedAt'], '2025-01-01T12:00:00Z')
        self.assertEqual(value['updatedAt'], '2026-01-01T12:00:00Z')
        self.assertTrue(value['pagesComplete'])

    def test_page_budget_does_not_guess_a_first_publication(self):
        class Reader:
            def read(self, url):
                return section('v2', '2026-01-01T12:00:00Z') + '<a href="/fixture/plugin/releases?page=2">Next</a>'
        value = backfill.github_dates({'repository': 'fixture/plugin'}, Reader(), 1)
        self.assertEqual(value['firstPublishedAt'], '')
        self.assertFalse(value['pagesComplete'])

    def test_unparseable_later_page_is_not_proof_of_history_end(self):
        class Reader:
            def read(self, url):
                return '<html>temporary failure</html>' if '?page=2' in url else section('v2', '2026-01-01T12:00:00Z') + '<a href="/fixture/plugin/releases?page=2">Next</a>'
        value = backfill.github_dates({'repository': 'fixture/plugin'}, Reader(), 5)
        self.assertIn('error', value)
        self.assertNotIn('firstPublishedAt', value)

    def test_renderer_release_cannot_date_a_decky_plugin(self):
        rows = [{'tag': 'renderer-v9', 'publishedAt': '2026-10-19T10:00:00Z'},
                {'tag': 'plugin-v3', 'publishedAt': '2026-10-01T10:00:00Z'}]
        entry = {'catalogReleaseUrl': 'https://github.com/fixture/plugin/releases/download/plugin-v3/plugin.zip'}
        self.assertEqual(backfill.qualifying(rows, entry), [rows[1]])

    def test_unknown_or_display_dates_never_become_iso_source_dates(self):
        for value in ['', None, '19/10/2026', '2026-10-19', '2026-10-19T12:00:00']:
            self.assertEqual(backfill.stamp(value), '')

if __name__ == '__main__':
    unittest.main()
