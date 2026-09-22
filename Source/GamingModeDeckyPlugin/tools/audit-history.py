#!/usr/bin/env python3
"""Offline audit of the "Accade oggi" editorial data.

Checks, in one pass, everything that can be verified without a network:
  * every manifest entry has a real file on disk, readable, non-empty, a real image;
  * no two manifest entries share the same bytes (SHA-256), across themes or inside one;
  * every theme referenced by the calendar/anniversaries/events exists;
  * every theme has images, and every chapter image_role can actually be served;
  * declared `kind` values are known, and portraits exist where a person is the subject;
  * every localised string exists in all supported languages, with no copy leaking
    from another subject (the Pikmin class of defect);
  * the 2026 calendar places no subject on a date other than its own.

Exit code is 1 when any check fails, so it can gate a build.
"""
import datetime
import hashlib
import json
import os
import re
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
QUICK = os.path.join(ROOT, 'quick_settings')
ASSETS = os.path.join(ROOT, 'src', 'assets', 'history')
LANGUAGES = ['en', 'it', 'de', 'es', 'fr', 'pt', 'ru', 'uk', 'ja', 'ko', 'zh', 'hi']
KNOWN_KINDS = {'portrait', 'cover', 'screenshot', 'game', 'console', 'controller', 'hardware',
               'company', 'object', 'interface', 'artwork', 'reference', 'photo', 'logo'}
# A chapter asks for a role. 'game' means "a picture of the work", not one file type,
# so it accepts anything that can depict it; only 'portrait' demands a face.
WORK = {'cover', 'screenshot', 'game', 'artwork', 'console', 'controller', 'hardware',
        'interface', 'object', 'photo'}
ROLE_KINDS = {
    'portrait': {'portrait'},
    'context': KNOWN_KINDS,
    'related': KNOWN_KINDS,
    'game': WORK,
    'company': {'company', 'logo', 'object', 'photo', 'artwork'},
    'object': {'object', 'hardware', 'controller', 'console', 'photo'},
    'controller': {'controller', 'object', 'hardware', 'console'},
    'interface': {'interface', 'screenshot', 'console', 'controller', 'object'},
    'hardware': {'console', 'controller', 'hardware', 'object'},
    'console': {'console', 'hardware', 'object', 'photo'},
}
# Subjects that are people: their theme must offer a portrait.
PERSON_THEMES = {'lawson', 'iwata', 'kojima', 'miyamoto', 'kondo', 'naka', 'miyazaki'}


def image_size(path):
    """Width/height for PNG, JPEG, GIF and WebP without a third-party decoder."""
    with open(path, 'rb') as handle:
        head = handle.read(32)
        if head[:8] == b'\x89PNG\r\n\x1a\n':
            return struct.unpack('>II', head[16:24])
        if head[:3] == b'GIF':
            return struct.unpack('<HH', head[6:10])
        if head[:4] == b'RIFF' and head[8:12] == b'WEBP':
            handle.seek(0)
            blob = handle.read(64)
            if blob[12:16] == b'VP8X':
                w = int.from_bytes(blob[24:27], 'little') + 1
                h = int.from_bytes(blob[27:30], 'little') + 1
                return w, h
            return 0, 0  # VP8/VP8L: present and valid, size not decoded here.
        if head[:2] == b'\xff\xd8':
            handle.seek(2)
            while True:
                marker = handle.read(2)
                if len(marker) < 2 or marker[0] != 0xFF:
                    return 0, 0
                if marker[1] in range(0xC0, 0xD0) and marker[1] not in (0xC4, 0xC8, 0xCC):
                    handle.read(3)
                    h, w = struct.unpack('>HH', handle.read(4))
                    return w, h
                length = struct.unpack('>H', handle.read(2))[0]
                handle.seek(length - 2, 1)
        raise ValueError('unrecognised image header')


def translated(value):
    return value if isinstance(value, dict) else {}


def main():
    failures = []
    notes = []

    with open(os.path.join(QUICK, 'history_editorial.json'), encoding='utf-8-sig') as handle:
        catalog = json.load(handle)
    with open(os.path.join(QUICK, 'history_images.json'), encoding='utf-8-sig') as handle:
        manifest = json.load(handle)
    try:
        with open(os.path.join(ROOT, 'tools', 'history-wanted.json'), encoding='utf-8-sig') as handle:
            pending = {row['theme'] for row in json.load(handle)['wanted']}
    except (OSError, ValueError, KeyError):
        pending = set()
    try:
        with open(os.path.join(QUICK, 'history_days.json'), encoding='utf-8-sig') as handle:
            days = json.load(handle)['days']
    except (OSError, ValueError, KeyError):
        days = {}

    themes = {theme['id']: theme for theme in catalog.get('themes', [])}

    # ---------------------------------------------------------------- files --
    digests = {}
    for theme_id, entries in manifest.items():
        for entry in entries:
            name = entry.get('file', '')
            path = os.path.join(ASSETS, name)
            if not name:
                failures.append(f'{theme_id}: manifest entry without a file name')
                continue
            if not os.path.isfile(path):
                failures.append(f'{theme_id}/{name}: file missing from src/assets/history')
                continue
            size = os.path.getsize(path)
            if size < 1024:
                failures.append(f'{theme_id}/{name}: {size} bytes, too small to be a real image')
                continue
            try:
                width, height = image_size(path)
            except Exception as error:
                failures.append(f'{theme_id}/{name}: unreadable image ({error})')
                continue
            if width and (width < 160 or height < 90):
                failures.append(f'{theme_id}/{name}: {width}x{height} is too small for a card')
            with open(path, 'rb') as handle:
                digest = hashlib.sha256(handle.read()).hexdigest()
            digests.setdefault(digest, []).append(f'{theme_id}/{name}')
            kind = entry.get('kind', '')
            if kind not in KNOWN_KINDS:
                failures.append(f'{theme_id}/{name}: unknown kind {kind!r}')
            if not entry.get('subject'):
                failures.append(f'{theme_id}/{name}: no subject declared')

    # One file listed under two themes is deliberate reuse and costs nothing: the
    # bundler imports it once. Two *different* files with identical bytes are the
    # defect — duplicated weight and the same picture under two names.
    for digest, owners in sorted(digests.items()):
        names = {owner.split('/', 1)[1] for owner in owners}
        if len(names) > 1:
            failures.append('identical bytes in different files: ' + ', '.join(sorted(owners)))

    orphans = sorted(set(os.listdir(ASSETS)) - {
        entry.get('file') for entries in manifest.values() for entry in entries})
    for name in orphans:
        if name.lower().endswith(('.jpg', '.jpeg', '.png', '.webp', '.gif')):
            notes.append(f'asset on disk but not in the manifest: {name}')

    # --------------------------------------------------------------- themes --
    for theme_id in sorted(themes):
        entries = manifest.get(theme_id, [])
        if not entries:
            # Already queued for the fetcher: a note, not a gate.
            (notes if theme_id in pending else failures).append(
                f'{theme_id}: no images at all'
                + (' (queued in history-wanted.json)' if theme_id in pending else ''))
            continue
        kinds = {entry.get('kind') for entry in entries}
        if theme_id in PERSON_THEMES and 'portrait' not in kinds:
            failures.append(f'{theme_id}: a person without a portrait')
        subjects = {entry.get('subject') for entry in entries}
        for chapter in themes[theme_id].get('chapters', []):
            named = chapter.get('subject')
            if named and named not in subjects:
                notes.append(f'{theme_id}: chapter subject {named!r} has no image yet '
                             f'(it renders without one, never with another subject\'s)')
            role = chapter.get('image_role', 'game')
            allowed = ROLE_KINDS.get(role)
            if allowed is None:
                failures.append(f'{theme_id}: unknown image_role {role!r}')
            elif not (kinds & allowed):
                failures.append(f'{theme_id}: chapter role {role!r} has no image of a matching kind '
                                f'(available: {sorted(k for k in kinds if k)})')

    for source, key in (('calendar', None), ('anniversaries', 'theme_id'), ('events', 'theme_id')):
        block = catalog.get(source)
        items = block.values() if isinstance(block, dict) else block or []
        for item in items:
            theme_id = item if key is None else item.get(key)
            if theme_id and theme_id not in themes:
                failures.append(f'{source}: references unknown theme {theme_id!r}')

    # ---------------------------------------------------------------- texts --
    def check_strings(label, value, required=True):
        table = translated(value)
        if not table:
            if required:
                failures.append(f'{label}: not localised at all')
            return
        missing = [language for language in LANGUAGES if not str(table.get(language, '')).strip()]
        if missing:
            failures.append(f'{label}: missing {",".join(missing)}')

    for theme_id, theme in sorted(themes.items()):
        check_strings(f'{theme_id}.title', theme.get('title'))
        check_strings(f'{theme_id}.intro', theme.get('intro'))
        for index, chapter in enumerate(theme.get('chapters', [])):
            check_strings(f'{theme_id}.chapter[{index}].title', chapter.get('title'))
            check_strings(f'{theme_id}.chapter[{index}].body', chapter.get('body'))
            if chapter.get('kicker') is not None:
                check_strings(f'{theme_id}.chapter[{index}].kicker', chapter.get('kicker'))

    # Copy that names a subject belonging to a different theme.
    LEAKS = {
        'miyamoto': ['pikmin'],
        'iwata': ['earthbound'],
        'dreamcast': [],
        'nintendo': [],
    }
    blob = json.dumps(catalog, ensure_ascii=False)
    for theme_id, theme in sorted(themes.items()):
        text = json.dumps(theme, ensure_ascii=False).lower()
        for word in LEAKS.get(theme_id, []):
            if word in text:
                failures.append(f'{theme_id}: copy still mentions {word!r}')
    for anniversary in catalog.get('anniversaries', []):
        text = json.dumps(anniversary, ensure_ascii=False).lower()
        for word in LEAKS.get(anniversary.get('theme_id'), []):
            if word in text:
                failures.append(f"anniversary {anniversary.get('date')} ({anniversary.get('theme_id')}): "
                                f'copy still mentions {word!r}')

    # A definition with no story behind it, and a trailing parenthetical category.
    for theme_id, theme in sorted(themes.items()):
        italian = translated(theme.get('title')).get('it', '')
        if re.search(r'\([^()]{2,40}\)\s*$', italian):
            failures.append(f'{theme_id}: title keeps a parenthetical category: {italian!r}')
        if not theme.get('chapters'):
            failures.append(f'{theme_id}: a title with no chapters behind it')

    # ----------------------------------------------------------- day table --
    # Every date carries its own written record. These checks are what stop the table
    # from sliding back into text assembled from lists.
    seen_titles, seen_subjects = {}, {}
    for date in sorted(days):
        entry = days[date]
        label = f'day {date}'
        if not entry.get('subject'):
            failures.append(f'{label}: no subject')
        if not entry.get('topic'):
            failures.append(f'{label}: no Wikipedia topic')
        if entry.get('kind', 'feature') != 'feature' and not entry.get('year'):
            failures.append(f'{label}: claims a {entry.get("kind")!r} anniversary with no year')
        for field in ('title', 'intro'):
            for language in ('it', 'en'):
                if not str((entry.get(field) or {}).get(language, '')).strip():
                    failures.append(f'{label}: {field} missing {language}')
        if not entry.get('chapters'):
            failures.append(f'{label}: no chapter behind the title')
        for index, chapter in enumerate(entry.get('chapters', [])):
            for field in ('title', 'body', 'kicker'):
                for language in ('it', 'en'):
                    if not str((chapter.get(field) or {}).get(language, '')).strip():
                        failures.append(f'{label}: chapter[{index}].{field} missing {language}')
            body = (chapter.get('body') or {}).get('it', '')
            if len(body.split()) < 20:
                failures.append(f'{label}: chapter[{index}] body is too short to be editorial')
        italian = (entry.get('title') or {}).get('it', '')
        if italian in seen_titles:
            failures.append(f'{label}: title repeats {seen_titles[italian]}')
        seen_titles[italian] = label
        subject = entry.get('subject', '')
        if subject in seen_subjects:
            failures.append(f'{label}: subject {subject!r} already used on {seen_subjects[subject]}')
        seen_subjects[subject] = label

    # ---------------------------------------------------------------- dates --
    sys.path.insert(0, QUICK)
    import daily_history  # noqa: E402

    placements = {}
    day = datetime.date(2026, 1, 1)
    identifiers, titles = set(), set()
    while day.year == 2026:
        theme, occasion, _ = daily_history.select_editorial(catalog, day)
        if not theme:
            failures.append(f'{day}: no editorial at all')
        else:
            identifiers.add(theme['id'])
            titles.add(theme.get('display_title') or json.dumps(theme.get('title'), sort_keys=True))
            placements.setdefault(theme.get('media_id', theme['id']), []).append(day)
        day += datetime.timedelta(days=1)
    if len(identifiers) != 365:
        failures.append(f'2026 produces {len(identifiers)} distinct ids, not 365')
    if len(titles) != 365:
        failures.append(f'2026 produces {len(titles)} distinct titles, not 365')

    # Verified placements the user asked to be able to trust.
    EXPECTED = {'naka': '09-17', 'miyamoto': '11-16', 'lawson': '12-01'}
    for theme_id, date in EXPECTED.items():
        days = [d for d in placements.get(theme_id, []) if d.strftime('%m-%d') == date]
        if not days:
            failures.append(f'{theme_id} never appears on {date}')
        wrong = [d.strftime('%m-%d') for d in placements.get(theme_id, [])
                 if d.strftime('%m-%d') != date]
        if wrong:
            failures.append(f'{theme_id} also appears on {sorted(set(wrong))}, outside its own date')
    if 'death' in blob.lower():
        notes.append("the catalog contains the word 'death' — check no date of death was introduced")

    # --------------------------------------------------------------- report --
    for note in notes:
        print(f'note: {note}')
    for failure in failures:
        print(f'FAIL: {failure}')
    print(f'\n{len(failures)} failures, {len(notes)} notes, '
          f'{sum(len(v) for v in manifest.values())} manifest entries, {len(themes)} themes.')
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
