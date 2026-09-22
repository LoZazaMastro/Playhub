"""Extract an editorial workbook into reviewable source data, never the live calendar.

Usage: python import-editorial-workbook.py workbook.xlsx --output review.json
Requires openpyxl for read-only XLSX extraction. The original is never saved.
"""
import argparse
import collections
import datetime
import hashlib
import json
from pathlib import Path

LANGUAGES = ('it', 'en', 'de', 'es', 'fr', 'pt', 'ru', 'uk', 'ja', 'ko', 'zh', 'hi')


def table(sheet):
    rows = iter(sheet.values)
    for row_number, row in enumerate(rows, 1):
        if row and row[0] == 'date_key':
            headers = row
            break
    else:
        raise ValueError(f'Missing date_key header: {sheet.title}')
    records = []
    for number, row in enumerate(rows, row_number + 1):
        if not row or not row[0]:
            continue
        item = dict(zip(headers, row))
        item['_row'] = number
        records.append(item)
    return records


def localized(row, field, translations=None, translation_field=None):
    result = {}
    for language in LANGUAGES:
        value = row.get(f'{field}_{language}')
        if not value and translations:
            value = translations.get(f'{translation_field or field}_{language}')
        if isinstance(value, str) and value.strip():
            result[language] = value.strip()
    return result


def convert(days, chapters, day_translations=(), chapter_translations=()):
    def translation_index(rows, keys):
        result = {}
        for row in rows:
            key = tuple(row[k] for k in keys)
            merged = result.setdefault(key, {})
            for field, value in row.items():
                if field in keys or field == '_row' or value in (None, ''):
                    continue
                if field in merged and merged[field] != value:
                    raise ValueError(f'Conflicting translation: {key}/{field}')
                merged[field] = value
        return result

    translations_by_day = translation_index(day_translations, ('date_key',))
    translations_by_chapter = translation_index(chapter_translations, ('date_key', 'chapter_order'))
    grouped = collections.defaultdict(list)
    seen_chapters = set()
    for row in chapters:
        key = (row['date_key'], row['chapter_order'])
        if key in seen_chapters:
            raise ValueError(f'Duplicate chapter key: {key}')
        seen_chapters.add(key)
        grouped[(row['date_key'], row['subject'])].append(row)
    stories, candidates, seen_days = [], [], set()
    for row in days:
        date = row['date_key']
        datetime.datetime.strptime('2001-' + date, '%Y-%m-%d')
        identity = (date, row['priority'], row['subject'])
        if identity in seen_days:
            raise ValueError(f'Duplicate story: {identity}')
        seen_days.add(identity)
        # Day translations belong to the primary story only. On 09-09 the
        # PlayStation translations must never leak into Dreamcast or Crash.
        translations = translations_by_day.get((date,)) if row['priority'] == 1 else None
        story = {
            'date': date, 'priority': row['priority'], 'subject': row['subject'],
            'subject_type': row['subject_type'], 'kind': row['event_kind'],
            'topic': row['wikipedia_topic_en'], 'year': row['year'],
            'occasion': localized(row, 'occasion', translations),
            'title': localized(row, 'editorial_title', translations),
            'intro': localized(row, 'intro', translations),
            'source_urls': [row[k] for k in ('source_url_1', 'source_url_2') if row.get(k)],
            'source_status': row['status'], 'verification_note': row.get('verification_note'),
            'target_chapters': row['target_chapters'],
            'source_row': row.get('_row'), 'chapters': [], 'hero': None,
        }
        for chapter in sorted(grouped[(date, row['subject'])], key=lambda c: c['chapter_order']):
            if not chapter.get('body_it') and not chapter.get('body_en'):
                continue
            translated = translations_by_chapter.get((date, chapter['chapter_order']))
            record = {
                'order': chapter['chapter_order'], 'subject': chapter['subject'],
                'kicker': localized(chapter, 'kicker', translated),
                'title': localized(chapter, 'title', translated),
                'body': localized(chapter, 'body', translated),
                'image_role': chapter['image_role'], 'image_brief': chapter['image_brief'],
                'image_file': None, 'source_url': chapter['source_url'],
                'quote': None, 'source_status': chapter['status'], 'source_row': chapter.get('_row'),
            }
            if chapter.get('is_real_quote'):
                if not chapter.get('quote_author') or not chapter.get('quote_source_url'):
                    raise ValueError(f'Unattributed quotation: {date}/{chapter["chapter_order"]}')
                record['quote'] = {'author': chapter['quote_author'], 'source_url': chapter['quote_source_url']}
            if chapter['image_role'] == 'hero':
                if story['hero']:
                    raise ValueError(f'Duplicate hero: {identity}')
                story['hero'] = record
            else:
                story['chapters'].append(record)
        if not story['intro'] or not story['chapters']:
            candidates.append(story)
            continue
        # Recent workbooks use the day's introduction as the opening card.
        # A separate chapter with image_role=hero is optional in that format.
        records = ([story['hero']] if story['hero'] else []) + story['chapters']
        complete_languages = [language for language in LANGUAGES
            if all(story[field].get(language) for field in ('occasion', 'title', 'intro'))
            and all(c and all(c[field].get(language) for field in ('kicker', 'title', 'body')) for c in records)]
        blockers = ['Images must be assigned and visually verified']
        if row['status'] != 'PRONTO':
            blockers.append('Editorial source requires review: ' + row['status'])
        if len(complete_languages) != len(LANGUAGES):
            blockers.append('Missing translations: ' + ', '.join(l for l in LANGUAGES if l not in complete_languages))
        if len(story['chapters']) < int(row['target_chapters']):
            blockers.append('Fewer chapters than requested')
        story.update(complete_languages=complete_languages, publication_ready=False, blockers=blockers)
        stories.append(story)
    stories.sort(key=lambda s: (s['date'], s['priority']))
    return {
        'schema': 1, 'stories': stories, 'candidates': candidates,
        'audit': {'dates': len({r['date_key'] for r in days}), 'subjects': len(days),
                  'written_stories': len(stories), 'narrative_chapters': sum(len(s['chapters']) for s in stories),
                  'heroes': sum(s['hero'] is not None for s in stories),
                  'candidate_subjects': len(candidates), 'publication_ready': 0},
    }


def main():
    import openpyxl
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('workbook', type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    if args.output.resolve() == args.workbook.resolve():
        raise ValueError('Output cannot overwrite the source workbook')
    with args.workbook.open('rb') as source:
        digest = hashlib.file_digest(source, 'sha256').hexdigest()
    workbook = openpyxl.load_workbook(args.workbook, read_only=True, data_only=True)
    try:
        result = convert(*(table(workbook[name]) for name in
            ('Giorni', 'Capitoli', 'Traduzioni Giorni', 'Traduzioni Capitoli')))
    finally:
        workbook.close()
    result['source'] = {'filename': args.workbook.name, 'sha256': digest}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result['audit']))


if __name__ == '__main__':
    main()
