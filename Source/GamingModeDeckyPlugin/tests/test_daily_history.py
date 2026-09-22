import datetime
import json
import pathlib
import tempfile
import unittest
import urllib.error
from unittest.mock import patch

from quick_settings.daily_history import DailyHistory, LANGUAGES, select_editorial


class DailyHistoryTests(unittest.TestCase):
    def test_yearless_commemorations_resolve_in_all_twelve_languages(self):
        from quick_settings import daily_history
        directory = str(pathlib.Path(daily_history.__file__).parent)
        expected = {'12-03': 'workbook-12-03-xbox-adaptive-controller',
                    '12-25': 'workbook-12-25-christmas-nights',
                    '12-31': 'workbook-12-31-the-sims-2-holiday-party-pack'}
        for language in LANGUAGES:
            catalog = daily_history.load_catalog(directory, language)
            for date, theme_id in expected.items():
                with self.subTest(language=language, date=date):
                    theme, marker, _ = select_editorial(catalog, datetime.date.fromisoformat('2026-' + date))
                    self.assertEqual(theme['id'], theme_id)
                    self.assertEqual(marker['kind'], 'commemoration')

    def test_empty_translation_does_not_blank_chapter_or_switch_its_media(self):
        from quick_settings import daily_history
        theme = {'id': 'fixture', 'title': {'it': 'Titolo', 'en': '', 'ja': ''},
                 'intro': {'it': 'Introduzione'}, 'chapters': [
                     {'title': {'it': 'Capitolo', 'en': 'Chapter', 'ja': ''},
                      'body': {'it': 'Italiano', 'en': 'English', 'ja': ''},
                      'image_file': 'assigned.png', 'image_subject': 'Canonical'}]}
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(daily_history, 'load_catalog', return_value={'themes': [theme]}), patch.object(daily_history, 'select_editorial', return_value=(theme, {}, [])):
                story = DailyHistory(directory).get('ja')['editorial']
        self.assertEqual(story['title'], 'Titolo')
        self.assertEqual(story['chapters'][0]['body'], 'English')
        self.assertEqual(story['chapters'][0]['image_file'], 'assigned.png')
        self.assertEqual(story['chapters'][0]['image_subject'], 'Canonical')

    def test_opening_and_heading_image_bindings_survive_localization(self):
        from quick_settings.daily_history import localize_theme
        theme = {'id': 'game', 'kind': 'uscita_regionale',
                 'title': {'it': 'Titolo'}, 'intro': {'it': 'Testo'},
                 'intro_image_file': 'opening.jpg', 'intro_image_role': 'editorial',
                 'cover_image_file': 'box.jpg'}
        result = localize_theme(theme, 'it')
        self.assertEqual(result['intro_image_file'], 'opening.jpg')
        self.assertEqual(result['intro_image_role'], 'editorial')
        self.assertEqual(result['cover_image_file'], 'box.jpg')

    def test_localized_chapter_keeps_canonical_media_subject(self):
        from quick_settings import daily_history
        theme = {'id': 'binding-fixture', 'topic': 'Game', 'subject': 'Game',
                 'title': {'it': 'Titolo'}, 'intro': {'it': 'Testo'},
                 'chapters': [{'title': {'it': 'Capitolo'}, 'body': {'it': 'Testo'},
                               'subject': 'Original Subject', 'image_subject': 'SOURCE SUBJECT',
                               'subject_localized': {'it': 'Soggetto tradotto'},
                               'image_file': 'photo.jpg'}]}
        with tempfile.TemporaryDirectory() as directory:
            with patch.object(daily_history, 'load_catalog', return_value={'themes': [theme]}), patch.object(daily_history, 'select_editorial', return_value=(theme, {}, [])):
                chapter = DailyHistory(directory).get('it')['editorial']['chapters'][0]
        self.assertEqual(chapter['subject'], 'Soggetto tradotto')
        self.assertEqual(chapter['image_subject'], 'SOURCE SUBJECT')
        self.assertEqual(chapter['image_file'], 'photo.jpg')

    def test_wikipedia_link_uses_localized_sitelink(self):
        with tempfile.TemporaryDirectory() as folder:
            reader=DailyHistory(folder)
            reader.summary=lambda *args:{'wikibase_item':'Q123'}
            reader.entity=lambda *args:{'sitelinks':{'itwiki':{'title':'Doom (videogioco 1993)'}}}
            url=reader.wikipedia_url('doom','it-IT')
            self.assertEqual(url,'https://it.wikipedia.org/wiki/Doom_%28videogioco_1993%29')
            with self.assertRaises(ValueError): reader.wikipedia_url('doom','ja')
            with self.assertRaises(ValueError): reader.wikipedia_url('unknown','it')

    @classmethod
    def setUpClass(cls):
        cls.catalog = json.loads((pathlib.Path(__file__).parents[1] / 'quick_settings/history_editorial.json').read_text(encoding='utf-8-sig'))

    def test_every_day_has_localized_editorial_and_specials_do_not_leak(self):
        for index in range(366):
            day = datetime.date(2028, 1, 1) + datetime.timedelta(days=index)
            theme, occasion, _ = select_editorial(self.catalog, day)
            self.assertIsNotNone(theme, day)
            self.assertEqual(set(theme['title']), LANGUAGES)
            self.assertEqual(set(theme['intro']), LANGUAGES)
            if not occasion:
                self.assertFalse(theme.get('calendar_only'))
                self.assertFalse(theme.get('event_only'))

    def test_anniversary_intro_overrides_holiday_copy(self):
        catalog = {'themes':[{'id':'a','title':{'en':'Base'},'intro':{'en':'Holiday'},'calendar_only':True}],
                   'anniversaries':[{'date':'05-17','year':2009,'kind':'release','theme_id':'a','intro':{'en':'Birthday'}}]}
        theme, _, _ = select_editorial(catalog, datetime.date(2026,5,17))
        self.assertEqual(theme['intro']['en'], 'Birthday')
        self.assertEqual(catalog['themes'][0]['intro']['en'], 'Holiday')

    def test_scheduled_event_is_not_repeated_next_year(self):
        event = self.catalog['events'][0]
        day = datetime.date.fromisoformat(event['date'])
        theme, _, anniversaries = select_editorial(self.catalog, day)
        self.assertEqual(theme['id'], event['theme_id'])
        self.assertTrue(anniversaries)
        theme, _, _ = select_editorial(self.catalog, day.replace(year=day.year+1))
        self.assertNotEqual(theme['id'], event['theme_id'])

    def test_birthdays_are_selected_but_death_dates_are_excluded(self):
        day=datetime.date(2026,8,24)
        _,occasion,_=select_editorial(self.catalog,day)
        self.assertEqual(occasion['kind'],'birth')
        catalog={'themes':[{'id':'regular'}], 'anniversaries':[
            {'date':'08-24','year':2000,'kind':'death','theme_id':'regular'}]}
        # This synthetic catalog intentionally removes the real birthday. Give
        # it a real daily essay so the assertion tests exclusion, not packaging.
        with patch('quick_settings.daily_history.daily_feature', return_value={'kind':'feature'}):
            _,occasion,anniversaries=select_editorial(catalog,day)
        self.assertEqual(occasion.get('kind'), 'feature')
        self.assertEqual(anniversaries,[])

    def test_offline_keeps_current_day_editorial_in_selected_language(self):
        def offline(url): self.fail('Daily editorial must not depend on a network request')
        with patch('quick_settings.daily_history.load_catalog', return_value=self.catalog), tempfile.TemporaryDirectory() as directory:
            service = DailyHistory(directory, fetch=offline, today=lambda:datetime.date(2026,4,22))
            result = service.get('it')
            self.assertEqual(result['date'], '2026-04-22')
            self.assertFalse(result['stale'])
            self.assertEqual(result['editorial']['title'], 'Quanto costa una città accesa')

    def test_september_ninth_keeps_all_three_anniversaries(self):
        with tempfile.TemporaryDirectory() as directory:
            service=DailyHistory(directory,fetch=lambda url:self.fail('Network request'),today=lambda:datetime.date(2026,9,9))
            result=service.get('it')
            self.assertEqual({item['editorial']['id'] for item in [result]+result['also']},{'crash','playstation','dreamcast'})

    def test_disabled_makes_no_request_and_day_navigation_is_available(self):
        with tempfile.TemporaryDirectory() as directory:
            service = DailyHistory(directory, fetch=lambda url:self.fail('Unexpected network request'))
            self.assertTrue(service.settings()['enabled'])
            self.assertEqual(service.get('it', 1)['date'], (datetime.date.today() + datetime.timedelta(days=1)).isoformat())
            service.save(False)
            self.assertEqual(service.get(), {'disabled':True})

    def test_rate_limit_stops_following_requests(self):
        requests = []
        def limited(url):
            requests.append(url)
            raise urllib.error.HTTPError(url, 429, 'rate limited', {}, None)
        with tempfile.TemporaryDirectory() as directory:
            service = DailyHistory(directory, fetch=limited)
            for title in ('A','B','C'):
                with self.assertRaises(Exception): service.summary(title)
            self.assertEqual(len(requests), 1)

    def test_header_keeps_subject_separate_from_editorial_title(self):
        with patch('quick_settings.daily_history.load_catalog', return_value=self.catalog), tempfile.TemporaryDirectory() as directory:
            service = DailyHistory(directory, today=lambda:datetime.date(2026,10,31))
            result = service.get('it')
        self.assertEqual(result['article']['title'], 'SimCity')
        self.assertEqual(result['editorial']['title'], 'La città sotto la città')
        self.assertEqual(result['occasion']['kind'], 'commemoration')
        self.assertIsNone(result['year'], 'World Cities Day is not a SimCity release anniversary')

    def test_release_header_keeps_name_and_year(self):
        with tempfile.TemporaryDirectory() as directory:
            result = DailyHistory(directory, today=lambda:datetime.date(2026,9,13)).get('it')
        self.assertEqual(result['article']['title'], 'Super Mario Bros.')
        self.assertEqual(result['year'], 1985)
        self.assertEqual(result['occasion']['kind'], 'release')

    def test_image_selection_survives_payload_and_day_changes_invalidate_edition(self):
        from quick_settings import daily_history
        entry = dict(daily_history.load_days()['03-03'])
        entry['chapters'] = [dict(entry['chapters'][0], image_file='chapter-photo.png')]
        with patch('quick_settings.daily_history.load_catalog', return_value=self.catalog), tempfile.TemporaryDirectory() as directory:
            service = DailyHistory(directory, today=lambda:datetime.date(2026,3,3))
            with patch.object(daily_history, 'load_days', return_value={'03-03': entry}):
                before = service.get('it')
                entry['intro'] = dict(entry['intro'], it='Testo corretto nella tabella giornaliera.')
                after = service.get('it')
        self.assertEqual(before['editorial']['chapters'][0]['image_file'], 'chapter-photo.png')
        self.assertNotEqual(before['edition'], after['edition'])

    def test_editorial_interludes_are_not_presented_as_quotations(self):
        from quick_settings.daily_history import EDITORIAL_QUOTES, EDITORIAL_INTERLUDE_TRANSLATIONS
        self.assertTrue(all(author == '' for _, author in EDITORIAL_QUOTES.values()))
        for theme in EDITORIAL_QUOTES:
            self.assertEqual(set(EDITORIAL_INTERLUDE_TRANSLATIONS[theme]) | {'it'}, LANGUAGES)
        with patch('quick_settings.daily_history.load_catalog', return_value=self.catalog), tempfile.TemporaryDirectory() as directory:
            service = DailyHistory(directory, today=lambda:datetime.date(2026,9,13))
            interlude = service.get('de')['quote']
            self.assertEqual(interlude['text'], EDITORIAL_INTERLUDE_TRANSLATIONS['mario']['de'])
            self.assertFalse(interlude['quoted'])
            self.assertEqual(interlude['attribution'], '')

    def test_long_creator_essays_keep_a_positioned_editorial_interlude(self):
        with patch('quick_settings.daily_history.load_catalog', return_value=self.catalog), tempfile.TemporaryDirectory() as directory:
            for date in (datetime.date(2026,8,24), datetime.date(2026,11,16), datetime.date(2026,9,13)):
                story = DailyHistory(directory, today=lambda date=date: date).get('it')
                self.assertTrue(story['quote']['text'])
                self.assertGreaterEqual(story['quote']['after'], 1)
                self.assertLessEqual(story['quote']['after'], len(story['editorial']['chapters']))

    def test_metadata_cache_survives_service_restart(self):
        with tempfile.TemporaryDirectory() as directory:
            service = DailyHistory(directory, fetch=lambda url:{'title':'Cached'})
            self.assertEqual(service.summary('A')['title'], 'Cached')
            restarted = DailyHistory(directory, fetch=lambda url:self.fail('Cache missed'))
            self.assertEqual(restarted.summary('A')['title'], 'Cached')


if __name__ == '__main__': unittest.main()
