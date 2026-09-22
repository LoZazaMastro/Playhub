#!/usr/bin/env python3
"""Merge a batch of day records into quick_settings/history_days.json.

Reads one JSON object of {"MM-DD": {...}} on stdin and folds it in, validating that
each record is complete and that nothing claims a date twice.
"""
import io, json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, 'quick_settings', 'history_days.json')
REQUIRED = ('subject', 'topic', 'kind', 'title', 'intro', 'chapters')

def main():
    batch = json.load(sys.stdin)
    try:
        with io.open(PATH, encoding='utf-8-sig') as handle:
            document = json.load(handle)
    except (OSError, ValueError):
        document = {'version': 1, 'days': {}}
    days = document.setdefault('days', {})
    for date, entry in batch.items():
        for field in REQUIRED:
            if not entry.get(field):
                raise SystemExit(f'{date}: missing {field}')
        if entry['kind'] != 'feature' and not entry.get('year'):
            raise SystemExit(f'{date}: kind {entry["kind"]!r} claims an anniversary without a year')
        for language in ('it', 'en'):
            if not entry['title'].get(language) or not entry['intro'].get(language):
                raise SystemExit(f'{date}: title/intro missing {language}')
        days[date] = entry
    with io.open(PATH, 'w', encoding='utf-8', newline='\n') as handle:
        json.dump(document, handle, ensure_ascii=False, indent=1, sort_keys=True)
        handle.write('\n')
    print(f'{len(batch)} merged, {len(days)}/365 days on file.')

if __name__ == '__main__':
    main()
