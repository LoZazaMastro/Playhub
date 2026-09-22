"""The 365 days must be written, not assembled.

The previous generator paired a list of 41 subjects with a list of 10 angles and drew
the Wikipedia topic and the images from two further, unrelated cycles. The identifiers
and titles came out unique, which is what the old check measured — and the title, the
article behind the card and the pictures could still describe three different things.
These tests measure the thing that actually matters.
"""
import datetime
import json
import os
import sys
import unittest
from unittest.mock import patch

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, 'quick_settings'))
import daily_history  # noqa: E402

with open(os.path.join(ROOT, 'quick_settings', 'history_editorial.json'), encoding='utf-8-sig') as handle:
    CATALOG = json.load(handle)
DAYS = daily_history.load_days()
YEAR = [datetime.date(2026, 1, 1) + datetime.timedelta(days=n) for n in range(365)]


class DayTableTests(unittest.TestCase):
    def test_every_day_of_the_year_has_a_record(self):
        for day in YEAR:
            with self.subTest(day=day):
                record, _, _ = daily_history.select_editorial(CATALOG, day)
                self.assertTrue(record['topic'])
                self.assertTrue(record['intro']['it'])

    def test_no_day_invents_an_anniversary(self):
        """A record may only claim a date if it carries the year it refers to."""
        for date, entry in DAYS.items():
            if entry.get('kind', 'feature') != 'feature':
                self.assertTrue(entry.get('year'), f'{date} claims {entry["kind"]} with no year')
                self.assertLess(int(entry['year']), 2026, f'{date}: year in the future')

    def test_title_article_and_images_describe_one_subject(self):
        """The defect the old generator had: three independent cycles, three subjects."""
        for date, entry in DAYS.items():
            self.assertTrue(entry.get('subject'), f'{date}: no subject')
            self.assertTrue(entry.get('topic'), f'{date}: no Wikipedia topic')
            for chapter in entry['chapters']:
                self.assertEqual(chapter.get('subject'), entry['subject'],
                                 f'{date}: a chapter illustrates a different subject')

    def test_no_subject_and_no_title_is_used_twice(self):
        subjects, titles = {}, {}
        for date, entry in sorted(DAYS.items()):
            subject, title = entry['subject'], entry['title']['it']
            self.assertNotIn(subject, subjects, f'{date} repeats {subject!r} from {subjects.get(subject)}')
            self.assertNotIn(title, titles, f'{date} repeats a title from {titles.get(title)}')
            subjects[subject] = date
            titles[title] = date

    def test_the_bodies_are_written_prose_not_a_template(self):
        """Two records must not share a sentence: that is what assembled text looks like."""
        sentences = {}
        for date, entry in sorted(DAYS.items()):
            for text in [entry['intro']['it']] + [c['body']['it'] for c in entry['chapters']]:
                self.assertGreaterEqual(len(text.split()), 20, f'{date}: body too short to be editorial')
                for sentence in (s.strip() for s in text.split('.') if len(s.split()) >= 6):
                    self.assertNotIn(sentence, sentences,
                                     f'{date} repeats a sentence from {sentences.get(sentence)}')
                    sentences[sentence] = date

    def test_the_whole_year_resolves_to_365_distinct_records(self):
        records = [daily_history.select_editorial(CATALOG, day)[0] for day in YEAR]
        self.assertEqual(len(records), 365)
        self.assertEqual(len({r['id'] for r in records}), 365)
        self.assertEqual(len({r.get('display_title') or json.dumps(r['title'], sort_keys=True)
                              for r in records}), 365)

    def test_a_missing_record_is_reported_instead_of_repeating_a_generic_story(self):
        with patch.object(daily_history, 'load_days', return_value={}):
            with self.assertRaisesRegex(RuntimeError, '03-03'):
                daily_history.daily_feature(datetime.date(2026, 3, 3))

    def test_day_table_preserves_explicit_release_and_chapter_image(self):
        entry = dict(DAYS['03-03'], kind='release', year=1999)
        entry['chapters'] = [dict(entry['chapters'][0], image_file='specific-scene.jpg')]
        with patch.object(daily_history, 'load_days', return_value={'03-03': entry}):
            record, marker, _ = daily_history.select_editorial({}, datetime.date(2026, 3, 3))
        self.assertEqual(marker, {'kind': 'release', 'year': 1999})
        self.assertEqual(record['chapters'][0]['image_file'], 'specific-scene.jpg')


if __name__ == '__main__':
    unittest.main()
