import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('editorial_quality', Path(__file__).parents[1] / 'tools/audit-editorial-quality.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class EditorialQualityTests(unittest.TestCase):
    def test_detects_recycled_passages_across_stories(self):
        repeated = 'Questo paragrafo generico viene riciclato in tre articoli senza aggiungere alcuna informazione specifica.'
        stories = [dict(date=f'01-0{i}', topic=f'Gioco {i}', title={'it': f'Gioco {i}'},
                        intro={'it': repeated}, chapters=[]) for i in (1, 2, 3)]
        report = module.audit({'stories': stories})
        self.assertEqual(report['dates_with_repetition'], ['01-01', '01-02', '01-03'])
        self.assertEqual(len(report['repeated_passages']), 1)
        self.assertTrue(any(item['issue'] == 'missing_translation' for item in report['findings']))

    def test_reports_duplicate_subject_without_collapsing_dates(self):
        stories = [dict(date=date, topic='Never Alone', title={'it': 'Titolo'}, intro={'it': 'Testo'}, chapters=[])
                   for date in ('05-21', '08-22')]
        report = module.audit({'stories': stories})
        self.assertEqual(report['duplicate_topics'], [{'topic': 'Never Alone', 'dates': ['05-21', '08-22']}])


if __name__ == '__main__':
    unittest.main()
