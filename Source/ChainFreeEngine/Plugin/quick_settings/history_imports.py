"""Apply published workbook editions consistently across UI languages."""
import copy
import json
from pathlib import Path


def merge_editions(catalog, editions, language):
    selected = [entry for entry in editions.get('entries', [])
                if entry.get('published') is True]
    if not selected:
        return catalog
    result = copy.deepcopy(catalog)
    ids = {entry['theme']['id'] for entry in selected}
    topics = {entry['theme']['topic'] for entry in selected}
    dates = {entry['anniversary']['date'] for entry in selected}
    # An essay for a different observance can mention the same game without
    # being the release article. Keep those unrelated calendar assignments.
    other_observances = {theme_id for date, theme_id in result.get('calendar', {}).items() if date not in dates}
    replaced_ids = ids | {theme['id'] for theme in result.get('themes', [])
                          if theme.get('topic') in topics and theme['id'] not in other_observances}
    result['themes'] = [theme for theme in result.get('themes', []) if theme['id'] not in replaced_ids]
    result['themes'].extend(entry['theme'] for entry in selected)
    result['anniversaries'] = [event for event in result.get('anniversaries', []) if event.get('theme_id') not in replaced_ids]
    result['events'] = [event for event in result.get('events', []) if event.get('theme_id') not in replaced_ids]
    result['calendar'] = {date: theme_id for date, theme_id in result.get('calendar', {}).items()
                          if date not in dates and theme_id not in replaced_ids}
    for entry in selected:
        result['anniversaries'].append({**entry['anniversary'], 'theme_id': entry['theme']['id'],
                                        'priority': entry.get('priority', 1)})
    # Explicit workbook priorities stay authoritative. Within the legacy
    # fallback group, hardware releases precede games on the same date.
    themes_by_id = {theme['id']: theme for theme in result['themes']}
    def anniversary_order(event):
        theme = themes_by_id.get(event.get('theme_id'), {})
        hardware_release = (event.get('kind') == 'release' and
                            theme.get('kind') in ('history', 'hardware', 'console'))
        return event.get('priority', 100), 0 if hardware_release else 1
    result['anniversaries'].sort(key=anniversary_order)
    return result


def load_catalog(directory, language):
    directory = Path(directory)
    with (directory / 'history_editorial.json').open(encoding='utf-8-sig') as stream:
        catalog = json.load(stream)
    try:
        with (directory / 'history_imported.json').open(encoding='utf-8-sig') as stream:
            imported = json.load(stream)
        return merge_editions(catalog, imported, language)
    except (OSError, ValueError, KeyError, TypeError, AttributeError):
        # An interrupted optional edition update must not blank the daily tab.
        return catalog
