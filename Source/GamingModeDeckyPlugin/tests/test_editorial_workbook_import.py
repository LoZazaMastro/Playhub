import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('workbook_import', Path(__file__).parents[1] / 'tools' / 'import-editorial-workbook.py')
importer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(importer)


def day(subject, priority):
    return dict(date_key='09-09', priority=priority, subject=subject, subject_type='console',
                event_kind='launch', wikipedia_topic_en=subject, year=1995, status='PRONTO',
                target_chapters=1, occasion_it='Esce oggi', occasion_en='Released today',
                editorial_title_it=subject, editorial_title_en=subject, intro_it='Introduzione', intro_en='Introduction')


def chapter(subject, order, role='editorial'):
    return dict(date_key='09-09', chapter_order=order, subject=subject, kicker_it=subject, kicker_en=subject,
                title_it='Titolo', title_en='Title', body_it='Testo', body_en='Body',
                image_role=role, image_brief='A console photograph', source_url='https://example.com', status='PRONTO')


class WorkbookImportTests(unittest.TestCase):
    def test_same_date_preserves_distinct_subjects_and_hero(self):
        rows = [chapter('PlayStation', 0, 'hero'), chapter('PlayStation', 1),
                chapter('Dreamcast', 2, 'hero'), chapter('Dreamcast', 3)]
        result = importer.convert([day('Dreamcast', 2), day('PlayStation', 1)], rows,
                                  [dict(date_key='09-09', editorial_title_fr='La PlayStation', intro_fr='Texte'),
                                   dict(date_key='09-09', editorial_title_fr=None, intro_fr=None)])
        first, second = result['stories']
        self.assertEqual(first['subject'], 'PlayStation')
        self.assertEqual(first['title']['fr'], 'La PlayStation')
        self.assertEqual(second['hero']['subject'], 'Dreamcast')
        self.assertEqual(second['chapters'][0]['subject'], 'Dreamcast')
        self.assertNotIn('fr', second['title'])
        self.assertFalse(first['publication_ready'])  # PRONTO is not proof of verified images.

    def test_unwritten_rows_never_replace_live_stories(self):
        entry = day('Unwritten', 1)
        entry['intro_it'] = entry['intro_en'] = None
        result = importer.convert([entry], [])
        self.assertEqual(result['stories'], [])
        self.assertEqual(len(result['candidates']), 1)

    def test_duplicate_chapter_ids_are_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Duplicate chapter'):
            importer.convert([day('PlayStation', 1)], [chapter('PlayStation', 1), chapter('Dreamcast', 1)])

    def test_day_intro_is_opening_card_when_workbook_has_no_hero_row(self):
        result = importer.convert([day('PlayStation', 1)], [chapter('PlayStation', 1)])
        self.assertEqual(result['stories'][0]['complete_languages'], ['it', 'en'])
        self.assertEqual(len(result['stories'][0]['chapters']), 1)
        self.assertFalse(result['stories'][0]['publication_ready'])

    def test_quotes_require_both_author_and_source(self):
        row = chapter('PlayStation', 1)
        row['is_real_quote'] = 1
        with self.assertRaisesRegex(ValueError, 'Unattributed quotation'):
            importer.convert([day('PlayStation', 1)], [row])


if __name__ == '__main__':
    unittest.main()
