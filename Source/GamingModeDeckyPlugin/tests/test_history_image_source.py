"""A slug is not an identifier.

igdb.com/games/portal is a 1986 Activision game, not Valve's: downloading it put the
wrong screenshots under the right title, and nothing in the pipeline noticed. These
pin the check that now refuses a source whose own title names a different work.
"""
import importlib.util
import os
import unittest

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
spec = importlib.util.spec_from_file_location(
    'fetch_history_images', os.path.join(ROOT, 'tools', 'fetch-history-images.py'))
fetcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetcher)


class ImageSourceVerificationTests(unittest.TestCase):
    def accepted(self, subject, found):
        try:
            fetcher.verify(subject, found, 'source')
            return True
        except LookupError:
            return False

    def test_a_different_work_is_refused(self):
        self.assertFalse(self.accepted('Portal', 'Portal (1986)'))
        self.assertFalse(self.accepted('Half-Life', 'Half-Life 2'))
        self.assertFalse(self.accepted('Myst', 'Myst III: Exile'))
        self.assertFalse(self.accepted('Grand Theft Auto III', 'Grand Theft Auto IV'))

    def test_the_same_work_is_accepted_through_edition_noise(self):
        self.assertTrue(self.accepted('Portal', 'Portal'))
        self.assertTrue(self.accepted('Doom', 'DOOM'))
        self.assertTrue(self.accepted('Resident Evil', 'Resident Evil HD Remastered'))
        self.assertTrue(self.accepted('Grand Theft Auto 2', 'Grand Theft Auto II'))

    def test_numbers_are_never_treated_as_noise(self):
        """A digit or a year is exactly what tells two works apart."""
        self.assertIn('2', fetcher.simplify('Half-Life 2'))
        self.assertIn('1986', fetcher.simplify('Portal (1986)'))

    def test_an_unknown_title_does_not_block_the_download(self):
        """Commons has no game title to compare; silence must not mean refusal."""
        self.assertTrue(self.accepted('Portal', ''))
        self.assertTrue(self.accepted('', 'anything'))


if __name__ == '__main__':
    unittest.main()
