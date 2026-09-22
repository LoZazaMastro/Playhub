import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('edition_merge', Path(__file__).parents[1] / 'tools/integrate-editorial-editions.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Step06IntegrationTests(unittest.TestCase):
    def story(self, kind='annuncio_hardware', subject_type='console'):
        text = {'it': 'Testo completo', 'en': 'Complete text'}
        return dict(date='01-07', topic='Game Boy Advance SP', subject='Game Boy Advance SP',
            subject_type=subject_type, kind=kind, year=2003, source_status='PRONTO',
            complete_languages=['it', 'en'], priority=1, title=text, intro=text, occasion=text,
            source_urls=['https://www.nintendo.co.jp/'], source_row=9, verification_note='Source note',
            chapters=[dict(order=1, subject='Game Boy Advance SP', kicker=text, title=text,
                           body=text, image_brief='Front-lit screen', source_row=20, source_url='source')])

    def test_preserves_existing_editions_and_is_idempotent(self):
        old = {'entries': [dict(theme=dict(id='playstation', topic='PlayStation (console)'), published=True)]}
        before = copy.deepcopy(old)
        extracted = dict(stories=[self.story()], candidates=[dict(date='09-09')])
        result = module.integrate(old, extracted, {})
        self.assertEqual(old, before)
        self.assertEqual(result['entries'][0], before['entries'][0])
        self.assertEqual(len(result['entries']), 2)
        self.assertEqual(module.integrate(result, extracted, {}), result)

    def test_announcement_is_not_a_release_and_missing_image_is_explicit(self):
        entry = module.integrate({'entries': []}, dict(stories=[self.story()]), {})['entries'][0]
        self.assertEqual(entry['anniversary']['kind'], 'commemoration')
        self.assertIn('annunciato', entry['anniversary']['headline']['it'])
        self.assertEqual(entry['theme']['chapters'][0]['image_file'], '__pending__')
        self.assertEqual(entry['media_status'], 'incomplete')

    def test_observance_does_not_show_games_release_year(self):
        entry = module.integrate({'entries': []}, dict(stories=[self.story('global_observance', 'game')]), {})['entries'][0]
        self.assertNotIn('year', entry['anniversary'])

    def test_unwritten_and_unreviewed_stories_are_not_published(self):
        story = self.story()
        story['complete_languages'] = []
        self.assertEqual(module.integrate({'entries': []}, dict(stories=[story]), {})['entries'], [])

    def test_step08_event_kinds_keep_their_meaning(self):
        for kind, expected in [('fondazione', 'foundation'), ('release_regional', 'release'), ('public_debut', 'commemoration')]:
            with self.subTest(kind=kind):
                entry = module.integrate({'entries': []}, dict(stories=[self.story(kind, 'game')]), {})['entries'][0]
                self.assertEqual(entry['anniversary']['kind'], expected)
                if kind == 'public_debut':
                    self.assertIn('pubblico', entry['anniversary']['headline']['it'])
                    self.assertNotIn('beta', entry['anniversary']['headline']['it'])

    def test_pending_editorial_review_stays_unpublished(self):
        story = self.story()
        story['publication_ready'] = False
        entry = module.integrate({'entries': []}, dict(stories=[story]), {})['entries'][0]
        self.assertFalse(entry['published'])

    def test_draft_revision_cannot_unpublish_an_approved_edition(self):
        story = self.story()
        approved = module.integrate({'entries': []}, dict(stories=[story]), {})
        story['publication_ready'] = False
        story['intro'] = {'it': 'Bozza ancora da verificare'}
        self.assertEqual(module.integrate(approved, dict(stories=[story]), {}), approved)

    def test_observances_do_not_inherit_unrelated_holiday_labels(self):
        for kind in ('ricorrenza', 'ricorrenza_globale', 'global_observance'):
            story = self.story(kind, 'game')
            story['date'] = '08-08'
            story['topic'] = 'Stray'
            entry = module.integrate({'entries': []}, dict(stories=[story]), {})['entries'][0]
            self.assertNotIn('headline', entry['anniversary'])
            self.assertNotIn('year', entry['anniversary'])

    def test_same_topic_on_another_date_does_not_erase_existing_edition(self):
        first = self.story()
        second = self.story()
        second['date'] = '02-14'
        result = module.integrate({'entries': []}, dict(stories=[first, second]), {})
        self.assertEqual(len(result['entries']), 2)
        self.assertEqual(module.integrate(result, dict(stories=[second]), {}), result)

    def test_new_workbook_kinds_and_explicit_headline(self):
        for kind, subject_type in [('lancio_hardware', 'console'), ('debutto_tecnologia', 'tecnologia'),
                                   ('evento', 'evento'), ('uscita_hardware_software', 'game')]:
            story = self.story(kind, subject_type)
            story['headline'] = {'it': 'Project Reality viene annunciato'}
            entry = module.integrate({'entries': []}, dict(stories=[story]), {})['entries'][0]
            self.assertEqual(entry['anniversary']['headline'], story['headline'])


if __name__ == '__main__':
    unittest.main()
