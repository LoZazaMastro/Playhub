#!/usr/bin/env python3
"""Fetch the editorial images the catalog is missing, with credits and licence.

Run this from a shell that has real internet access — the sandboxed shells used
during development do not. It reads tools/history-wanted.json, downloads only what
is missing, records provenance in quick_settings/history_images.json, regenerates
src/historyImages.ts and then runs tools/audit-history.py.

    python tools\\fetch-history-images.py            # download what is missing
    python tools\\fetch-history-images.py --dry-run  # only show what it would take
    python tools\\fetch-history-images.py --force    # re-download existing files

Providers:
  commons  ref = a subject to search on Wikimedia Commons. Freely licensed; the
           author and licence returned by the API are stored next to the file.
  steam    ref = a Steam appid. Uses the public appdetails endpoint.
  igdb     ref = the game's igdb.com slug. Takes that page's own cover image.
  local    ref = a file already in src/assets/history, copied under a new name so
           two themes can carry the same picture without sharing one entry.

Nothing is overwritten without --force, and a download that fails leaves the
catalog exactly as it was.
"""
import argparse
import html
import io
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, 'src', 'assets', 'history')
MANIFEST = os.path.join(ROOT, 'quick_settings', 'history_images.json')
WANTED = os.path.join(ROOT, 'tools', 'history-wanted.json')
AGENT = 'Playhub/1.4 (editorial image fetcher; https://github.com/)'
MAX_BYTES = 12 * 1024 * 1024
RIGHTS_GAME = 'Game imagery: respective publisher and rights holders.'


def get(url, accept='application/json'):
    headers = {
        # Commons and Steam accept a descriptive client UA; IGDB's CDN also
        # requires a browser-like referer for image derivatives.
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 '
                      '(KHTML, like Gecko) Chrome/131.0 Safari/537.36 ' + AGENT,
        'Accept': accept,
    }
    if urllib.parse.urlsplit(url).hostname == 'images.igdb.com':
        headers['Referer'] = 'https://www.igdb.com/'
    request = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read(MAX_BYTES + 1)
    if len(data) > MAX_BYTES:
        raise ValueError(f'response larger than {MAX_BYTES} bytes')
    return data


def get_json(url):
    return json.loads(get(url).decode('utf8'))


def commons(subject):
    """Best freely licensed Commons image for a subject, with its credit line."""
    search = ('https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search'
              '&gsrnamespace=6&gsrlimit=50&gsrsearch=' + urllib.parse.quote(subject) +
              '&prop=imageinfo&iiprop=url|size|mime|extmetadata&iiurlwidth=2000')
    pages = (get_json(search).get('query') or {}).get('pages') or {}
    best = None
    for page in pages.values():
        page_title = re.sub(r'^File:', '', page.get('title', ''), flags=re.I)
        # Commons search is deliberately broad. Require at least two meaningful
        # subject words (or an exact one-word subject) before accepting a file;
        # otherwise a query such as “Adventure (Atari 2600)” happily returns a
        # photograph of an unrelated ship called Adventure.
        wanted = set(simplify(subject).split())
        found = set(simplify(os.path.splitext(page_title)[0]).split())
        overlap = wanted & found
        if not (wanted == found or (len(wanted) == 1 and overlap) or len(overlap) >= 2):
            continue
        wanted_numbers = {token for token in wanted if token.isdigit()}
        if wanted_numbers and not wanted_numbers.intersection(found):
            continue
        distinctive = {token for token in wanted if len(token) >= 6 and not token.isdigit()}
        if distinctive and not distinctive.issubset(found):
            continue
        info = (page.get('imageinfo') or [{}])[0]
        if info.get('mime') not in ('image/jpeg', 'image/png'):
            continue
        width = info.get('width') or 0
        if width < 800:
            continue
        meta = info.get('extmetadata') or {}
        licence = (meta.get('LicenseShortName') or {}).get('value', '')
        if 'fair use' in licence.lower() or 'non-free' in licence.lower():
            continue
        author = re.sub(r'<[^>]+>', '', (meta.get('Artist') or {}).get('value', '')).strip()
        candidate = {
            'url': info.get('thumburl') or info.get('url'),
            'credit': ' · '.join(part for part in (author, licence) if part) or 'Wikimedia Commons',
            'sourceUrl': info.get('descriptionurl', ''),
            'width': width,
        }
        if not best or candidate['width'] > best['width']:
            best = candidate
    if not best:
        raise LookupError(f'no freely licensed Commons image for {subject!r}')
    return best


def steam(appid, subject=''):
    data = get_json('https://store.steampowered.com/api/appdetails?appids='
                    + str(appid) + '&filters=basic,screenshots').get(str(appid), {})
    if not data.get('success'):
        raise LookupError(f'Steam appid {appid} returned no details')
    game = data.get('data', {})
    verify(subject, game.get('name', ''), f'Steam appid {appid}')
    shots = [shot.get('path_full', '') for shot in game.get('screenshots', [])]
    url = next((s for s in shots if s), '') or game.get('header_image', '')
    if not url:
        raise LookupError(f'Steam appid {appid} has no usable image')
    return {'url': url, 'credit': RIGHTS_GAME,
            'sourceUrl': f'https://store.steampowered.com/app/{appid}/'}


def steam_search(subject):
    """Find an exact Steam work when an IGDB page/CDN is unavailable."""
    url = ('https://steamcommunity.com/actions/SearchApps/' + urllib.parse.quote(subject))
    rows = get_json(url)
    for row in rows[:12] if isinstance(rows, list) else []:
        appid, name = row.get('appid'), row.get('name', '')
        if not appid or not name:
            continue
        try:
            verify(subject, name, f'Steam search for {subject}')
            return steam(appid, subject)
        except Exception:
            continue
    raise LookupError(f'Steam has no exact image for {subject!r}')


NOISE = re.compile(r'[^a-z0-9]+')


ROMAN = {'ii': '2', 'iii': '3', 'iv': '4', 'v': '5', 'vi': '6', 'vii': '7', 'viii': '8',
         'ix': '9', 'x': '10', 'xi': '11', 'xii': '12'}


def simplify(value):
    """Compare titles without punctuation, articles or edition noise.

    A number is never noise: "2" and "1986" are exactly what tells two different works
    apart, so they survive and force a mismatch.
    """
    value = NOISE.sub(' ', str(value or '').lower())
    drop = ('the', 'a', 'goty', 'edition', 'remastered', 'definitive', 'hd', 'video',
            'game', 'complete', 'final', 'cut', 'pc', 'anniversary')
    words = [ROMAN.get(word, word) for word in value.split() if word not in drop]
    return ' '.join(words)


def verify(subject, found, where):
    """Refuse an image whose source is a different work.

    A slug is not an identifier: igdb.com/games/portal is a 1986 Activision game, not
    Valve's. Downloading it silently puts the wrong screenshots under the right title,
    which is the one failure nobody notices. Better to refuse and say what was found.
    """
    if not subject or not found:
        return
    # Exact match only. Substring matching is what lets "Portal" accept "Portal (1986)"
    # and "Half-Life" accept "Half-Life 2".
    wanted = simplify(subject).split()
    actual = simplify(found).split()
    if wanted == actual:
        return
    raise LookupError(f'{where} is "{found}", not "{subject}" — refusing to use it')


def igdb(slug, subject=''):
    page = 'https://www.igdb.com/games/' + slug
    try:
        body = get(page, accept='text/html').decode('utf8', errors='replace')
    except Exception:
        # IGDB rate limits both its HTML pages and image CDN. Steam's public
        # catalogue is a safe, title-verified fallback for works that exist there.
        if subject:
            return steam_search(subject)
        raise
    titles = re.findall(r'<meta\b[^>]+property=["\']og:title["\'][^>]+content=["\']([^"\']+)', body, re.I)
    if titles:
        verify(subject, html.unescape(titles[0]).split(' - ')[0].strip(), f'igdb.com/games/{slug}')
    for tag in re.findall(r'<meta\b[^>]{0,4096}>', body, re.I):
        attrs = {key.lower(): html.unescape(value)
                 for key, _, value in re.findall(r'([\w:-]+)\s*=\s*(["\'])(.*?)\2', tag)}
        if attrs.get('property') == 'og:image' and attrs.get('content'):
            url = attrs['content']
            # Ask IGDB for the largest derivative rather than the page thumbnail.
            url = re.sub(r'/t_[a-z0-9_]+/', '/t_1080p_2x/', url)
            return {'url': url, 'credit': RIGHTS_GAME, 'sourceUrl': page}
    if subject:
        try:
            return steam_search(subject)
        except Exception:
            pass
    raise LookupError(f'igdb.com/games/{slug} exposes no cover image')


def resolve(entry):
    provider = entry['provider']
    if provider == 'commons':
        return commons(entry['ref'])
    if provider == 'steam':
        return steam(entry['ref'], entry.get('subject', ''))
    if provider == 'igdb':
        return igdb(entry['ref'], entry.get('subject', ''))
    if provider == 'remote':
        return {'url': entry['ref'], 'credit': entry.get('credit', 'Source image'),
                'sourceUrl': entry.get('sourceUrl', '')}
    if provider == 'local':
        return {'local': os.path.join(ASSETS, entry['ref']), 'credit': RIGHTS_GAME, 'sourceUrl': ''}
    raise ValueError(f'unknown provider {provider!r}')


def download_source(source, target):
    if source.get('local'):
        shutil.copyfile(source['local'], target)
    else:
        payload = get(source['url'], accept='image/*')
        # IGDB serves a 512px purple placeholder with HTTP 200 when its CDN
        # rejects a derivative. It is a valid PNG, but never an editorial image.
        if len(payload) < 20 * 1024:
            raise ValueError('downloaded image is a placeholder or too small')
        with open(target + '.tmp', 'wb') as handle:
            handle.write(payload)
        os.replace(target + '.tmp', target)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dry-run', action='store_true')
    parser.add_argument('--force', action='store_true')
    parser.add_argument('--only', default='', help='only rows whose theme matches this')
    parser.add_argument('--limit', type=int, default=0, help='stop after N downloads (resume later)')
    parser.add_argument('--pause', type=float, default=0.4, help='seconds between requests')
    options = parser.parse_args()

    with io.open(WANTED, encoding='utf-8-sig') as handle:
        wanted = json.load(handle)['wanted']
    with io.open(MANIFEST, encoding='utf-8-sig') as handle:
        manifest = json.load(handle)

    taken, failed = 0, 0
    for entry in wanted:
        if options.only and entry['theme'] != options.only:
            continue
        target = os.path.join(ASSETS, entry['file'])
        listed = any(item.get('file') == entry['file'] for item in manifest.get(entry['theme'], []))
        if os.path.isfile(target) and listed and not options.force:
            print(f"= {entry['theme']}/{entry['file']}: already present")
            continue
        try:
            source = resolve(entry)
        except Exception as error:
            print(f"! {entry['theme']}/{entry['file']}: {error}")
            failed += 1
            continue
        if options.dry_run:
            print(f"+ {entry['theme']}/{entry['file']} <- {source.get('url') or source.get('local')}")
            continue
        try:
            download_source(source, target)
        except Exception as error:
            # A valid IGDB page can still return a CDN 403 for one derivative.
            # Retry through a title-verified Steam result before marking the
            # image missing; this keeps the day populated without borrowing an
            # unrelated game's artwork.
            if entry.get('provider') == 'igdb':
                try:
                    fallback = steam_search(entry.get('subject', ''))
                    download_source(fallback, target)
                    source = fallback
                except Exception:
                    print(f"! {entry['theme']}/{entry['file']}: {error}")
                    failed += 1
                    continue
            else:
                print(f"! {entry['theme']}/{entry['file']}: {error}")
                failed += 1
                continue
        record = {'file': entry['file'], 'subject': entry['subject'], 'kind': entry['kind'],
                  'credit': source['credit'], 'rights': source['credit']}
        if source.get('sourceUrl'):
            record['sourceUrl'] = source['sourceUrl']
        if source.get('url'):
            record['url'] = source['url']
        bucket = manifest.setdefault(entry['theme'], [])
        bucket[:] = [item for item in bucket if item.get('file') != entry['file']]
        bucket.append(record)
        taken += 1
        where = source.get('sourceUrl') or source.get('url') or ''
        print(f"+ {entry['theme']}/{entry['file']}: {os.path.getsize(target)} bytes — "
              f"{entry['subject']} — {source['credit']} {where}")
        if options.limit and taken >= options.limit:
            print(f'\n--limit {options.limit} reached; run again to continue where this stopped.')
            break
        # Be a polite client: these are public APIs serving everyone.
        time.sleep(max(0.0, options.pause))

    if taken and not options.dry_run:
        with io.open(MANIFEST, 'w', encoding='utf-8', newline='\n') as handle:
            json.dump(manifest, handle, ensure_ascii=False, indent=1)
            handle.write('\n')
        subprocess.run([sys.executable, os.path.join(ROOT, 'tools', 'build-history-images.py')], check=True)

    print(f'\n{taken} downloaded, {failed} failed.')
    if not options.dry_run:
        subprocess.run([sys.executable, os.path.join(ROOT, 'tools', 'audit-history.py')])
    return 1 if failed else 0


if __name__ == '__main__':
    raise SystemExit(main())
