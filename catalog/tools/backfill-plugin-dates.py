#!/usr/bin/env python3
"""Backfill public publication dates; no login, GitHub API, repository dates or commit dates.
Run from any directory with Python 3.10+. Default is a dry run. Cache HTML locally.
"""
import argparse
import concurrent.futures
import datetime as dt
import hashlib
import html
import json
from pathlib import Path
import re
import threading
import time
import urllib.error
import urllib.parse
import urllib.request

UTC = dt.timezone.utc
ROOT = Path(__file__).resolve().parents[2]
def stamp(value):
    if not isinstance(value, str) or 'T' not in value:
        return ''
    try:
        date = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
        if date.tzinfo is None:
            return ''
        return date.astimezone(UTC).isoformat(timespec='seconds').replace('+00:00', 'Z')
    except ValueError:
        return ''

def release_rows(text, repository):
    rows = []
    prefix = '/' + repository + '/releases/tag/'
    for block in re.split(r'<section\b[^>]*>', text)[1:]:
        tags = re.findall(r'href="([^"]+/releases/tag/[^"]+)"', block)
        tags = [html.unescape(tag) for tag in tags if html.unescape(tag).lower().startswith(prefix.lower())]
        match = re.search(r'released this\s*<relative-time\b[^>]*\bdatetime="([^"]+)"', block, re.I | re.S)
        if not tags or not match:
            continue
        published = stamp(match[1])
        if published:
            rows.append({'tag': urllib.parse.unquote(tags[0][len(prefix):]),
                         'publishedAt': published, 'url': 'https://github.com' + tags[0]})
    return rows

def tag_for(entry):
    path = urllib.parse.urlsplit(entry.get('catalogReleaseUrl', '')).path
    match = re.match(r'/[^/]+/[^/]+/releases/download/([^/]+)/', path)
    return urllib.parse.unquote(match[1]) if match else ''

def qualifying(rows, entry):
    current = tag_for(entry)
    # MAKO hosts independent renderer and Decky releases. Never date the plugin from a renderer tag.
    prefix = 'plugin-' if current.startswith('plugin-') else ''
    return [row for row in rows if not prefix or row['tag'].startswith(prefix)]

class Reader:
    def __init__(self, cache, refresh=False):
        self.cache, self.refresh = cache, refresh
        cache.mkdir(parents=True, exist_ok=True)
    def read(self, url):
        key = hashlib.sha256(url.encode()).hexdigest()
        body, receipt = self.cache / (key + '.body'), self.cache / (key + '.json')
        if not self.refresh and body.exists() and receipt.exists():
            metadata = json.loads(receipt.read_text(encoding='utf-8'))
            if metadata['url'] == url and time.time() - metadata['fetchedAt'] < 2 * 86400:
                return body.read_bytes().decode('utf-8')
        request = urllib.request.Request(url, headers={'User-Agent': 'Playhub-Publication-Backfill/1.0',
                                                       'Accept': 'text/html,application/json'})
        with urllib.request.urlopen(request, timeout=15) as response:
            if response.status != 200:
                raise ValueError('Unexpected HTTP status')
            raw = response.read(4 * 1024 * 1024 + 1)
            if len(raw) > 4 * 1024 * 1024:
                raise ValueError('Response exceeds 4 MiB')
        body.write_bytes(raw)
        receipt.write_text(json.dumps({'url': url, 'fetchedAt': time.time()}), encoding='utf-8')
        return raw.decode('utf-8')

def github_dates(entry, reader, max_pages):
    repository = entry['repository']
    if not re.fullmatch(r'[\w.-]+/[\w.-]+', repository, re.ASCII):
        return {'repository': repository, 'error': 'Unsupported repository identity'}
    records, visited, complete = {}, [], False
    try:
        for page in range(1, max_pages + 1):
            url = 'https://github.com/' + repository + '/releases' + ('' if page == 1 else '?page=' + str(page))
            text = reader.read(url)
            visited.append(url)
            rows = release_rows(text, repository)
            if not rows:
                raise ValueError('No explicit GitHub release publication timestamps; history completeness is unproven')
            for row in rows:
                records[row['tag']] = row
            # GitHub's page links, not page length or Atom's ten-entry window, establish completeness.
            pages = [int(n) for n in re.findall(r'/releases\?page=(\d+)', text)]
            if not any(number > page for number in pages):
                complete = True
                break
        selected = qualifying(list(records.values()), entry)
        if not selected:
            raise ValueError('No published releases for this plugin release family')
        oldest = min(selected, key=lambda row: row['publishedAt'])
        newest = max(selected, key=lambda row: row['publishedAt'])
        current = next((row for row in selected if row['tag'] == tag_for(entry)), None)
        return {'repository': repository, 'source': 'github-release-html', 'firstPublishedAt': oldest['publishedAt'] if complete else '',
                'updatedAt': newest['publishedAt'], 'firstEvidence': oldest['url'] if complete else '',
                'latestEvidence': newest['url'], 'currentReleasePublishedAt': current['publishedAt'] if current else '',
                'pagesComplete': complete, 'pages': len(visited), 'releaseCount': len(selected)}
    except (OSError, ValueError, KeyError) as error:
        return {'repository': repository, 'source': 'github-release-html', 'error': str(error), 'pages': len(visited)}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalog', type=Path, default=ROOT / 'catalog/plugins.json')
    parser.add_argument('--cache', type=Path, default=ROOT / '.local/plugin-publication-cache')
    parser.add_argument('--report', type=Path, default=ROOT / '.local/plugin-publication-backfill.json')
    parser.add_argument('--max-pages', type=int, default=100)
    parser.add_argument('--workers', type=int, default=3)
    parser.add_argument('--refresh', action='store_true')
    parser.add_argument('--apply', action='store_true', help='Write dates to catalog and external fallback snapshot; does not publish.')
    args = parser.parse_args()
    if not 1 <= args.workers <= 3 or not 1 <= args.max_pages <= 100:
        parser.error('workers must be 1..3 and max-pages 1..100')
    catalog = json.loads(args.catalog.read_text(encoding='utf-8-sig'))
    reader = Reader(args.cache, args.refresh)
    official = json.loads(reader.read('https://plugins.deckbrew.xyz/plugins'))
    by_id = {row['id']: row for row in official}
    evidence = []
    for entry in catalog['plugins']:
        if not entry.get('active', True) or entry.get('catalogSource') != 'decky-store':
            continue
        remote = by_id.get(entry.get('catalogPluginId'))
        if not remote:
            evidence.append({'repository': entry['repository'], 'error': 'Official Decky ID absent from current feed'})
            continue
        dates = [stamp(row.get('created')) for row in remote.get('versions', [])]
        dates = [value for value in dates if value]
        first = stamp(remote.get('created')) or min(dates, default='')
        latest = stamp(remote.get('updated')) or max(dates, default='')
        evidence.append({'repository': entry['repository'], 'source': 'decky-official-created-updated',
                         'firstPublishedAt': first, 'updatedAt': latest,
                         'firstEvidence': 'https://plugins.deckbrew.xyz/plugins',
                         'latestEvidence': 'https://plugins.deckbrew.xyz/plugins', 'deckyId': remote['id']})
    candidates = [entry for entry in catalog['plugins'] if entry.get('active', True) and entry.get('catalogSource') != 'decky-store']
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        jobs = {pool.submit(github_dates, entry, reader, args.max_pages): entry for entry in candidates}
        for job in concurrent.futures.as_completed(jobs):
            row = job.result()
            evidence.append(row)
            print(row['repository'], row.get('firstPublishedAt', ''), row.get('updatedAt', ''), row.get('error', ''), flush=True)
    by_repo = {row['repository'].lower(): row for row in evidence}
    for entry in catalog['plugins']:
        row = by_repo.get(entry['repository'].lower(), {})
        if row.get('firstPublishedAt'):
            entry['firstPublishedAt'] = row['firstPublishedAt']
        if row.get('updatedAt'):
            entry['updatedAt'] = row['updatedAt']
        if row.get('currentReleasePublishedAt'):
            entry['releasePublishedAt'] = row['currentReleasePublishedAt']
    summary = {'checkedAt': dt.datetime.now(UTC).isoformat(), 'sourceCatalogRevision': catalog['catalogRevision'],
               'activeRecords': sum(entry.get('active', True) for entry in catalog['plugins']),
               'firstDates': sum(bool(row.get('firstPublishedAt')) for row in evidence),
               'updatedDates': sum(bool(row.get('updatedAt')) for row in evidence),
               'incomplete': [row for row in evidence if not row.get('firstPublishedAt') or not row.get('updatedAt')],
               'evidence': sorted(evidence, key=lambda row: row['repository'].lower()),
               'limitations': ['Dates describe currently available published release history. Deleted releases cannot be reconstructed.',
                              'Decky dates are min/max added_on for retained Store versions.',
                              'No repository creation date, commit date, catalog-added date or Atom edit timestamp is used.']}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(summary, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    proposal = args.report.with_name(args.report.stem + '-catalog.json')
    proposal.write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    if args.apply:
        args.catalog.write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
        snapshot = ROOT / 'Source/Playhub/Assets/PluginCatalog/external-plugins.json'
        external = json.loads(snapshot.read_text(encoding='utf-8-sig'))
        by_repo = {entry['repository'].lower(): entry for entry in catalog['plugins']}
        external['plugins'] = [by_repo[entry['repository'].lower()] for entry in external['plugins']]
        snapshot.write_text(json.dumps(external, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print('SUMMARY', {key: summary[key] for key in ['activeRecords', 'firstDates', 'updatedDates']}, 'proposal', proposal, flush=True)

if __name__ == '__main__':
    main()
