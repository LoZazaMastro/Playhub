"""Public, unauthenticated editorial feed with a validated last-good disk cache."""
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import threading
import time
import urllib.request
import urllib.parse

CATALOG_URL = 'https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/on-this-day.json'
MAX_BYTES = 24 * 1024 * 1024
REFRESH_SECONDS = 6 * 60 * 60
RETRY_SECONDS = 5 * 60
_lock = threading.RLock()
_states = {}


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, file, code, message, headers, newurl):
        return None


def parse_document(raw):
    if len(raw) > MAX_BYTES:
        raise ValueError('Editorial document exceeds size limit')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError('Duplicate editorial key')
            result[key] = value
        return result
    data = json.loads(raw, object_pairs_hook=unique)
    if not isinstance(data, dict) or type(data.get('schemaVersion')) is not int or data['schemaVersion'] != 1 or type(data.get('revision')) is not int or data['revision'] < 1:
        raise ValueError('Unsupported editorial document')
    def bounded(value, depth=0):
        if depth > 24:
            raise ValueError('Editorial document nesting limit')
        if isinstance(value, dict):
            if len(value) > 5000 or any(not isinstance(key, str) or len(key) > 512 for key in value):
                raise ValueError('Editorial object limit')
            for item in value.values():
                bounded(item, depth + 1)
        elif isinstance(value, list):
            if len(value) > 5000:
                raise ValueError('Editorial array limit')
            for item in value:
                bounded(item, depth + 1)
        elif isinstance(value, str) and (len(value) > 131072 or any(ord(c) < 32 and c not in '\r\n\t' for c in value)):
            raise ValueError('Editorial text limit')
    bounded(data)
    editorial, editions, days, images = (data.get(key) for key in ('editorial', 'editions', 'days', 'images'))
    if not isinstance(editorial, dict) or not isinstance(editorial.get('themes'), list) or not editorial['themes'] or not isinstance(editions, dict) or not isinstance(editions.get('entries'), list):
        raise ValueError('Missing editorial records')
    def translated(value):
        return isinstance(value, str) or isinstance(value, dict) and all(isinstance(text, str) for text in value.values())
    calendar = editorial.get('calendar', {})
    if not isinstance(calendar, dict) or any(not re.fullmatch(r'\d{2}-\d{2}', date) or not isinstance(identity, str) for date, identity in calendar.items()):
        raise ValueError('Invalid editorial calendar assignments')
    for key in ('anniversaries', 'events'):
        events = editorial.get(key, [])
        if not isinstance(events, list) or any(not isinstance(event, dict) or type(event.get('priority', 100)) is not int for event in events):
            raise ValueError('Invalid editorial event list')
    def theme_record(theme, needs_id=True):
        if not isinstance(theme, dict) or not isinstance(theme.get('topic'), str) or not theme['topic'].strip():
            raise ValueError('Invalid editorial subject')
        if needs_id and (not isinstance(theme.get('id'), str) or not re.fullmatch(r'[a-zA-Z0-9_-]+', theme['id'])):
            raise ValueError('Invalid editorial identity')
        for key in ('title', 'intro'):
            text = theme.get(key, {})
            if not translated(text):
                raise ValueError('Invalid editorial copy')
        for key in ('subject_localized', 'occasion_headline'):
            if key in theme and (not isinstance(theme[key], dict) or not translated(theme[key])):
                raise ValueError('Invalid editorial translation map')
        chapters = theme.get('chapters', [])
        if not isinstance(chapters, list) or any(not isinstance(chapter, dict) for chapter in chapters):
            raise ValueError('Invalid editorial chapters')
        for chapter in chapters:
            if any(not translated(chapter.get(key, {})) for key in ('title', 'body', 'kicker')):
                raise ValueError('Invalid editorial chapter copy')
            if 'subject_localized' in chapter and (not isinstance(chapter['subject_localized'], dict) or not translated(chapter['subject_localized'])):
                raise ValueError('Invalid editorial chapter translations')
    for theme in editorial['themes']:
        theme_record(theme)
    for entry in editions['entries']:
        if not isinstance(entry, dict):
            raise ValueError('Invalid editorial edition')
        if entry.get('published') is True:
            if type(entry.get('priority', 1)) is not int:
                raise ValueError('Invalid editorial edition priority')
            theme_record(entry.get('theme'))
            marker = entry.get('anniversary')
            if not isinstance(marker, dict) or not isinstance(marker.get('date'), str) or not re.fullmatch(r'\d{2}-\d{2}', marker['date']):
                raise ValueError('Invalid editorial anniversary')
    # Anniversary and calendar assignments cover dates omitted from this fallback table.
    if not isinstance(days, dict) or not 300 <= len(days) <= 366 or not all(re.fullmatch(r'\d{2}-\d{2}', key) and isinstance(value, dict) and value.get('topic') for key, value in days.items()):
        raise ValueError('Incomplete editorial calendar')
    for day in days.values():
        theme_record(day, needs_id=False)
    if not isinstance(images, dict) or not images or len(images) > 1000:
        raise ValueError('Invalid editorial media')
    for theme, entries in images.items():
        if not isinstance(entries, list) or len(entries) > 32:
            raise ValueError('Invalid editorial media group')
        for image in entries:
            if not isinstance(image, dict) or not isinstance(image.get('file'), str) or not re.fullmatch(r'[\w.()-]+\.(jpg|jpeg|png|webp|gif)', image['file'], re.ASCII | re.IGNORECASE):
                raise ValueError('Invalid editorial image filename')
            if not isinstance(image.get('url'), str) or not re.fullmatch(r'https://raw\.githubusercontent\.com/LoZazaMastro/Playhub/(main|[a-f0-9]{40})/assets/on-this-day/v1/' + re.escape(image['file']), urllib.parse.unquote(image['url'])):
                raise ValueError('Untrusted editorial image URL')
            if not re.fullmatch(r'[a-f0-9]{64}', str(image.get('sha256', ''))) or type(image.get('bytes')) is not int or not 0 < image['bytes'] <= 32 * 1024 * 1024:
                raise ValueError('Invalid editorial image integrity')
    data['_content_version'] = hashlib.sha256(raw).hexdigest()[:16]
    return data


def load_remote(cache_directory):
    """No GitHub API, token, cookies or credentials are involved."""
    path = Path(cache_directory) / 'editorial-catalog-v1.json'
    key = str(path.resolve())
    with _lock:
        state = _states.get(key)
        if state is None:
            state = {'data': None, 'next': 0}
            try:
                with path.open('rb') as stream:
                    state['data'] = parse_document(stream.read(MAX_BYTES + 1))
                state['next'] = min(path.stat().st_mtime, time.time()) + REFRESH_SECONDS
            except (OSError, ValueError, TypeError, KeyError, RecursionError):
                pass
            _states[key] = state
        if time.time() < state['next']:
            return state['data']
        try:
            request = urllib.request.Request(CATALOG_URL, headers={'User-Agent': 'Playhub-Editorial/1.0', 'Accept': 'application/json'})
            with urllib.request.build_opener(NoRedirect()).open(request, timeout=10) as response:
                if response.status != 200:
                    raise ValueError('Editorial HTTP status')
                if int(response.headers.get('Content-Length', 0)) > MAX_BYTES:
                    raise ValueError('Editorial content-length limit')
                raw = response.read(MAX_BYTES + 1)
            data = parse_document(raw)
            if state['data'] and data['revision'] < state['data']['revision']:
                raise ValueError('Editorial revision rollback')
            state['data'] = data
            state['next'] = time.time() + REFRESH_SECONDS
            temporary = path.with_suffix('.tmp')
            try:
                path.parent.mkdir(parents=True, exist_ok=True)
                temporary.write_bytes(raw)
                os.replace(temporary, path)
            except OSError:
                pass  # Valid in-memory content remains usable when the cache is unwritable.
            finally:
                try:
                    temporary.unlink(missing_ok=True)
                except OSError:
                    pass
        except (OSError, ValueError, TypeError, KeyError, RecursionError):
            state['next'] = time.time() + RETRY_SECONDS
        return state['data']


def apply_remote(cache_directory, language, merge_editions):
    data = load_remote(cache_directory)
    if data is None:
        return None
    catalog = merge_editions(copy.deepcopy(data['editorial']), data['editions'], language)
    catalog['_remote_days'] = data['days']
    catalog['_remote_images'] = data['images']
    catalog['_remote_revision'] = data['revision']
    catalog['_remote_version'] = data['_content_version']
    return catalog
