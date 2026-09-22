import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parents[1]

class CreatorEditorialTests(unittest.TestCase):
    def test_kojima_has_a_chronological_multichapter_metal_gear_arc(self):
        catalog=json.loads((ROOT/'quick_settings/history_editorial.json').read_text(encoding='utf-8-sig'))
        theme=next(t for t in catalog['themes'] if t['id']=='kojima')
        subjects=[chapter['subject'] for chapter in theme['chapters']]
        expected=[
            'Metal Gear', 'Metal Gear Solid', 'Metal Gear Solid 2: Sons of Liberty',
            'Metal Gear Solid 3: Snake Eater', 'Metal Gear Solid V: The Phantom Pain',
        ]
        self.assertGreaterEqual(len(theme['chapters']),10)
        self.assertEqual([subject for subject in subjects if subject in expected], expected)
        self.assertLess(subjects.index('Hideo Kojima'), subjects.index('Metal Gear'))
        self.assertLess(subjects.index('Metal Gear Solid V: The Phantom Pain'), subjects.index('Kojima Productions'))
        self.assertLess(subjects.index('Kojima Productions'), subjects.index('Death Stranding'))

    def test_kojima_metal_gear_arc_is_fully_localized_and_explicit(self):
        catalog=json.loads((ROOT/'quick_settings/history_editorial.json').read_text(encoding='utf-8-sig'))
        theme=next(t for t in catalog['themes'] if t['id']=='kojima')
        languages={'it','en','de','es','fr','pt','ru','uk','ja','ko','zh','hi'}
        arc=[chapter for chapter in theme['chapters'] if chapter['subject'].startswith('Metal Gear')]
        self.assertGreaterEqual(len(arc),5)
        for chapter in arc:
            for field in ('kicker','title','body'):
                self.assertEqual(set(chapter[field]), languages, (chapter['subject'], field))
                self.assertTrue(all(chapter[field][language].strip() for language in languages))
            self.assertIn(chapter['subject'].split(':')[0].upper(), chapter['kicker']['it'])
            self.assertNotEqual(chapter['body']['en'], chapter['body']['it'])
            self.assertNotIn('—', json.dumps(chapter, ensure_ascii=False))

    def test_creator_prose_never_describes_the_selected_photograph(self):
        catalog=json.loads((ROOT/'quick_settings/history_editorial.json').read_text(encoding='utf-8-sig'))
        banned = (
            'in questa fotografia', 'la fotografia del premio', 'this photograph',
            'the award photograph', 'in this portrait', 'auf dem foto',
            'la fotografía reúne', 'la photographie réunit', 'a fotografia reúne',
            'на фотографии', 'на цьому фото', '写真では', '사진 속에서',
            '照片让创作者', 'तस्वीर में',
        )
        for creator in ('kojima', 'miyamoto', 'kondo'):
            theme = next(item for item in catalog['themes'] if item['id'] == creator)
            prose = json.dumps(theme, ensure_ascii=False).casefold()
            for phrase in banned:
                self.assertNotIn(phrase.casefold(), prose, (creator, phrase))
            self.assertNotIn('—', prose, creator)
    def test_curated_creator_chapters_have_distinct_existing_subject_images(self):
        catalog=json.loads((ROOT/'quick_settings/history_editorial.json').read_text(encoding='utf-8-sig'))
        manifest=json.loads((ROOT/'quick_settings/history_images.json').read_text(encoding='utf-8-sig'))
        for creator in ('kondo','kojima','miyamoto'):
            theme=next(t for t in catalog['themes'] if t['id']==creator)
            self.assertGreaterEqual(len(theme['chapters']),5)
            used={manifest[creator][0]['file']}
            for chapter in theme['chapters']:
                name=chapter['image_file']
                self.assertNotIn(name,used,(creator,chapter['title']['it']))
                used.add(name)
                image=next(i for i in manifest[creator] if i['file']==name)
                self.assertEqual(image['subject'],chapter['subject'])
                self.assertNotEqual(image['kind'],'cover')
                self.assertTrue((ROOT/'src/assets/history'/name).is_file())
                self.assertTrue(chapter.get('kicker'))

if __name__=='__main__':unittest.main()
