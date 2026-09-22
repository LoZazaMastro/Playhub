#!/usr/bin/env python3
"""Stage the reviewed calendar proposal; default is a read-only dry run.

An application requires two different local images per topic and a projected year
with 365 different subjects. No creator record is changed. The image proposal is
either a direct media-id mapping or {"images": {media-id: [manifest entries]}}.
"""
import argparse
import copy
import datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import unicodedata

ROOT = Path(__file__).resolve().parents[1]
LOCALES = {'it', 'en', 'de', 'es', 'fr', 'pt', 'ru', 'uk', 'ja', 'ko', 'zh', 'hi'}
FIELDS = ('subject', 'subject_localized', 'topic', 'kind', 'media_id', 'title',
          'intro', 'chapters', 'sources', 'occasion_headline')


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def image_map(value):
    return value.get('images', value)


def validate_images(proposal, images, assets):
    errors = []
    all_hashes = {}
    for record in proposal['replacements']:
        media_id = record['media_id']
        rows = images.get(media_id, [])
        if len(rows) < 2:
            errors.append(f'{media_id}: requires two images, found {len(rows)}')
            continue
        for row in rows:
            name = row.get('file', '')
            path = (assets / name).resolve()
            if not name or Path(name).name != name or path.parent != assets.resolve():
                errors.append(f'{media_id}: invalid local asset name {name!r}')
                continue
            if not path.is_file():
                errors.append(f'{media_id}: asset missing: {name}')
                continue
            content = path.read_bytes()
            if not content:
                errors.append(f'{media_id}: empty asset: {name}')
                continue
            if not (content.startswith(b'\x89PNG\r\n\x1a\n') or content.startswith(b'\xff\xd8\xff')
                    or (content.startswith(b'RIFF') and content[8:12] == b'WEBP')):
                errors.append(f'{media_id}: invalid raster image signature: {name}')
                continue
            digest = hashlib.sha256(content).hexdigest()
            if digest in all_hashes:
                errors.append(f'{media_id}: repeated image bytes: {name} and {all_hashes[digest]}')
            all_hashes[digest] = name
            if row.get('kind') == 'cover':
                errors.append(f'{media_id}: cultural chapter must not use a game cover: {name}')
    return errors


def stage(proposal, days, catalog, manifest, images):
    """Pure staging, used by dry runs and regression tests. Does not write files."""
    days, catalog, manifest = copy.deepcopy((days, catalog, manifest))
    for record in proposal['replacements']:
        date, media_id = record['date'], record['media_id']
        entry = {key: copy.deepcopy(record[key]) for key in FIELDS if key in record}
        for field in [entry['title'], entry['intro'], entry['subject_localized'], entry['occasion_headline'],
                      *[value for chapter in entry['chapters']
                        for value in (chapter['title'], chapter['body'], chapter['kicker'], chapter['subject_localized'])]]:
            if not LOCALES.issubset(field) or any(not field[locale].strip() for locale in LOCALES):
                raise ValueError(f'{date}: incomplete localized field')
        rows = copy.deepcopy(images.get(media_id, []))
        for chapter_index, chapter in enumerate(entry['chapters']):
            # All these cultural chapters are aspects of their parent topic.
            # Keep display labels localized separately from image association.
            chapter['subject'] = entry['subject']
            if len(rows) > chapter_index + 1:
                chapter['image_file'] = rows[chapter_index + 1]['file']
        if rows:
            for row in rows:
                row['subject'] = entry['subject']
            manifest[media_id] = rows
        if record.get('target') == 'catalog theme education':
            target_id = 'calendar-education-logo'
            if date != '01-24' or catalog.get('calendar', {}).get(date) not in ('education', target_id):
                raise ValueError('education override no longer matches the catalog')
            # The education theme is also Minecraft's May release anniversary.
            # Give the observance its own theme; never mutate that shared story.
            updated = dict(entry, id=target_id)
            index = next((i for i, theme in enumerate(catalog['themes']) if theme['id'] == target_id), None)
            if index is None:
                catalog['themes'].append(updated)
            else:
                catalog['themes'][index] = updated
            catalog['calendar'][date] = target_id
        elif record.get('target'):
            raise ValueError(f'unsupported catalog target: {record["target"]}')
        else:
            if date not in days['days']:
                raise ValueError(f'day disappeared since proposal: {date}')
            days['days'][date] = entry
    return days, catalog, manifest


def normalized_topic(topic):
    text = unicodedata.normalize('NFKC', topic).casefold()
    return re.sub(r'\s+', ' ', text).strip()


def projected_year(days, catalog, year=2026):
    spec = importlib.util.spec_from_file_location('calendar_preview_daily_history', ROOT / 'quick_settings/daily_history.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.load_days = lambda: days['days']
    seen, duplicates = {}, []
    for index in range(365):
        date = datetime.date(year, 1, 1) + datetime.timedelta(days=index)
        record, _, _ = module.select_editorial(catalog, date)
        topic = normalized_topic(record['topic'])
        if topic in seen:
            duplicates.append({'date': date.isoformat(), 'first': seen[topic], 'topic': record['topic']})
        seen[topic] = date.isoformat()
    return {'days': 365, 'unique_subjects': len(seen), 'duplicates': duplicates}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--apply', action='store_true')
    mode.add_argument('--dry-run', action='store_true', help='default; never writes')
    args = parser.parse_args()
    image_path = ROOT / 'tools/calendar-replacement-images.json'
    proposal = read_json(ROOT / 'tools/calendar-replacements.json')
    images = image_map(read_json(image_path)) if image_path.exists() else {}
    paths = [ROOT / 'quick_settings' / name for name in
             ('history_days.json', 'history_editorial.json', 'history_images.json')]
    current = [read_json(path) for path in paths]
    staged = stage(proposal, *current, images)
    year = projected_year(*staged[:2])
    errors = validate_images(proposal, images, ROOT / 'src/assets/history')
    if year['unique_subjects'] != 365:
        errors.append('Projected year does not contain 365 unique subjects')
    print(json.dumps({'mode': 'apply' if args.apply else 'dry-run', 'year': year,
                      'image_errors': errors, 'ready_to_apply': not errors}, ensure_ascii=False, indent=2))
    if errors:
        return 1
    if args.apply:
        # Re-read immediately before writing: all other current catalog entries,
        # including concurrent creator work, must still match the staged input.
        if any(read_json(path) != before for path, before in zip(paths, current)):
            raise RuntimeError('Source changed during staging; retry against the current files')
        for path, data in zip(paths, staged):
            temporary = path.with_suffix(path.suffix + '.calendar-pending')
            temporary.write_text(json.dumps(data, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')
            os.replace(temporary, path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
