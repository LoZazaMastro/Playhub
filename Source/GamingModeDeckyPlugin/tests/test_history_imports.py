import copy
import unittest
from quick_settings.history_imports import merge_editions


class ImportedEditionTests(unittest.TestCase):
    def setUp(self):
        self.catalog = {'themes': [{'id': 'playstation', 'topic': 'PlayStation (console)', 'title': 'Old'}],
                        'anniversaries': [{'date': '09-09', 'theme_id': 'playstation'},
                                          {'date': '09-29', 'theme_id': 'playstation'}],
                        'calendar': {'09-09': 'old-feature'}, 'events': []}
        self.edition = {'published': True, 'locales': ['it', 'en'], 'priority': 1,
                        'theme': {'id': 'playstation', 'topic': 'PlayStation (console)', 'title': 'New'},
                        'anniversary': {'date': '09-09', 'year': 1995, 'kind': 'release'}}

    def test_import_replaces_old_anniversaries_without_mutating_base(self):
        before = copy.deepcopy(self.catalog)
        merged = merge_editions(self.catalog, {'entries': [self.edition]}, 'it')
        self.assertEqual(self.catalog, before)
        self.assertEqual(len(merged['anniversaries']), 1)
        self.assertEqual(merged['themes'][0]['title'], 'New')
        self.assertNotIn('09-09', merged['calendar'])

    def test_missing_translation_keeps_current_calendar_but_unpublished_editions_stay_hidden(self):
        merged = merge_editions(self.catalog, {'entries': [self.edition]}, 'ja')
        self.assertEqual(merged['themes'][0]['title'], 'New')
        self.assertNotIn('09-09', merged['calendar'])
        self.edition['published'] = False
        self.assertIs(merge_editions(self.catalog, {'entries': [self.edition]}, 'it'), self.catalog)

    def test_import_does_not_blank_an_unrelated_calendar_essay_about_same_game(self):
        self.catalog['themes'].append(dict(id='design-essay', topic='PlayStation (console)'))
        self.catalog['calendar']['04-22'] = 'design-essay'
        merged = merge_editions(self.catalog, {'entries': [self.edition]}, 'it')
        self.assertEqual(merged['calendar']['04-22'], 'design-essay')
        self.assertIn('design-essay', {t['id'] for t in merged['themes']})

    def test_console_priority_does_not_lose_other_same_day_stories(self):
        dreamcast = copy.deepcopy(self.edition)
        dreamcast.update(priority=2, theme={'id': 'dreamcast', 'topic': 'Dreamcast'})
        merged = merge_editions(self.catalog, {'entries': [dreamcast, self.edition]}, 'it')
        self.assertEqual([a['theme_id'] for a in merged['anniversaries']], ['playstation', 'dreamcast'])


if __name__ == '__main__':
    unittest.main()
