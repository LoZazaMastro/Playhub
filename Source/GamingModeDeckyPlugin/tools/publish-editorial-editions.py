"""Publish reviewed workbook stories with explicit, verified local image bindings.

python tools/publish-editorial-editions.py --source review.json --plan plan.json
    --media media1.json media2.json --subjects "PlayStation" "Dreamcast"
"""
import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, data):
    path = Path(path)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(path)


def build(story, plan, mappings):
    from PIL import Image
    if not plan.get('verified') or len(plan.get('sources', [])) < 2:
        raise ValueError('Missing verified date sources: ' + story['subject'])
    if not story.get('hero') or len(story['chapters']) < story['target_chapters']:
        raise ValueError('Incomplete story: ' + story['subject'])
    locales = story['complete_languages']
    if not all(l in locales for l in ('it', 'en')):
        raise ValueError('Missing Italian/English narrative: ' + story['subject'])
    images, chapters, hashes = [], [], set()
    for card in [story['hero'], *story['chapters']]:
        mapping = mappings[(story['subject'], card['order'])]
        if mapping.get('visually_verified') is not True:
            raise ValueError('Image requires visual review: ' + mapping['file'])
        file = ROOT / 'src' / 'assets' / 'history' / mapping['file']
        if file.parent.resolve() != (ROOT / 'src' / 'assets' / 'history').resolve():
            raise ValueError('Invalid image path')
        digest = hashlib.sha256(file.read_bytes()).hexdigest()
        if digest in hashes:
            raise ValueError('Repeated image in ' + story['subject'])
        hashes.add(digest)
        with Image.open(file) as image:
            image.verify()
        subject = mapping.get('card_subject') or story['subject']
        images.append({'file': mapping['file'], 'url': mapping.get('image_url', ''),
                       'sourceUrl': mapping['source_url'], 'subject': subject,
                       'kind': mapping.get('kind', 'editorial'), 'credit': mapping.get('credit', ''),
                       'notes': mapping.get('notes', ''), 'sha256': digest})
        chapters.append({'subject': subject, 'kicker': card['kicker'], 'title': card['title'],
                         'body': card['body'], 'image_file': mapping['file'], 'image_role': 'editorial'})
    theme = {'id': plan['id'], 'media_id': 'workbook-' + plan['id'], 'subject': story['subject'],
             'topic': story['topic'], 'kind': story['subject_type'], 'curated_edition': True,
             'title': story['hero']['title'], 'intro': plan.get('intro', story['hero']['body']),
             'chapters': chapters[1:], 'sources': list(dict.fromkeys(story['source_urls'] + plan['sources']))}
    anniversary = {'date': plan.get('date', story['date']), 'year': plan.get('year', story['year']),
                   'kind': plan.get('kind', 'release' if story['kind'] == 'launch' else story['kind'])}
    if plan.get('headline'):
        anniversary['headline'] = plan['headline']
    if plan.get('region'):
        anniversary['region'] = plan['region']
    return {'published': True, 'locales': locales, 'priority': story['priority'],
            'theme': theme, 'anniversary': anniversary}, images


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--plan', type=Path, required=True)
    parser.add_argument('--media', type=Path, nargs='+', required=True)
    parser.add_argument('--subjects', nargs='+', required=True)
    args = parser.parse_args()
    source, plan = read(args.source), read(args.plan)
    mappings, headers = {}, {}
    for path in args.media:
        for entry in read(path):
            if 'order' in entry:
                key = (entry['subject'], entry['order'])
                if key in mappings:
                    raise ValueError('Duplicate media assignment: ' + str(key))
                mappings[key] = entry
            elif entry.get('kind') == 'cover' and entry.get('visually_verified') is True:
                headers[entry['subject']] = entry
    destination = ROOT / 'quick_settings' / 'history_imported.json'
    imported = read(destination) if destination.exists() else {'schema': 1, 'entries': []}
    manifest_path = ROOT / 'quick_settings' / 'history_images.json'
    manifest = read(manifest_path)
    done = []
    for subject in args.subjects:
        story = next(s for s in source['stories'] if s['subject'] == subject)
        entry, images = build(story, plan[subject], mappings)
        # Preserve actual box artwork only for the small game header.
        if entry['theme']['kind'] == 'game':
            existing = manifest.get(plan[subject]['id'], [])
            header = headers.get(subject)
            if header:
                from PIL import Image
                with Image.open(ROOT / 'src' / 'assets' / 'history' / header['file']) as image:
                    image.verify()
                images.append({'file': header['file'], 'subject': subject, 'kind': 'cover',
                               'url': header.get('image_url', ''), 'sourceUrl': header.get('source_url', ''),
                               'credit': header.get('credit', '')})
            else:
                images.extend(image for image in existing if image.get('kind') == 'cover')
        imported['entries'] = [old for old in imported['entries'] if old['theme']['id'] != entry['theme']['id']]
        imported['entries'].append(entry)
        manifest[entry['theme']['media_id']] = images
        done.append(subject)
    imported['source'] = source['source']
    # Validate every requested edition before touching either runtime file.
    write(manifest_path, manifest)
    write(destination, imported)
    print(json.dumps({'published': done, 'editions': len(imported['entries'])}))


if __name__ == '__main__':
    main()
