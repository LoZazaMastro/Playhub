"""Repair daily game cards with provider screenshots, preserving unrelated manifest edits."""
import concurrent.futures
import importlib.util
import json
import os

spec = importlib.util.spec_from_file_location('fetch', os.path.join(os.path.dirname(__file__), 'fetch-history-images.py'))
fetch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetch)

# Explicit catalogue identities; sequels and modern remakes must not satisfy a
# title merely because the store search returned them first.
APPS = {'01-14':233550,'01-06':553960,'01-15':63660,'01-18':2203860,'01-20':70640,
        '01-25':70,'01-26':6910,'01-27':211600,'01-28':238210,
        '02-02':403430,'02-04':403410,'02-08':403400,'02-24':253750,
        '04-29':26800,'09-01':613830,'09-26':1250410,
        '05-07':400,'05-08':220,'06-14':6910,'12-06':400,'12-23':1426210}
COMMONS = {'07-22':('Atari 2600 console','console'),
           '08-20':('Pixel art','artwork'),
           '07-01':('Sprite sheet','artwork'),
           '03-07':('File:Xbox-console.jpg','console'),
           '08-28':('Global Game Jam','photo')}

def repair(item):
    key, day, entries = item
    subject = day['subject']
    try:
        source = None
        if key[4:] not in APPS and key[4:] not in COMMONS:
            raise ValueError('requires a curated source')
        kind = 'screenshot'
        if key[4:] in COMMONS:
            query, kind = COMMONS[key[4:]]
            if query.startswith('File:'):
                pages = fetch.get_json('https://commons.wikimedia.org/w/api.php?action=query&format=json&titles='+fetch.urllib.parse.quote(query)+'&prop=imageinfo&iiprop=url&iiurlwidth=1600')['query']['pages']
                info = next(iter(pages.values()))['imageinfo'][0]
                source = {'url':info.get('thumburl',info['url']),'sourceUrl':info['descriptionurl'],'credit':'Evan-Amos / Wikimedia Commons, public domain'}
            else:
                source = fetch.commons(query)
        else:
            appid = APPS[key[4:]]
            game = fetch.get_json('https://store.steampowered.com/api/appdetails?appids='+str(appid)+'&filters=basic,screenshots')[str(appid)]['data']
            shots = game.get('screenshots', [])
            if not shots:
                raise ValueError('no screenshots')
            shot_index = {'06-14':4,'12-06':5,'12-23':4}.get(key[4:],6 if appid in (403430, 403410, 403400) else 2 if appid == 400 else 0)
            source = {'url': shots[shot_index]['path_full'], 'sourceUrl':'https://store.steampowered.com/app/'+str(appid)+'/', 'credit':fetch.RIGHTS_GAME}
        if not source:
            raise ValueError('no verified screenshot')
        data = fetch.get(source['url'], 'image/*')
        if len(data) < 20000:
            raise ValueError('image too small')
        name = key + '-gameplay.jpg'
        with open(os.path.join(fetch.ASSETS, name), 'wb') as f:
            f.write(data)
        entry = {'file': name, 'subject': subject, 'kind': kind, 'url': source['url'], 'sourceUrl': source['sourceUrl'], 'credit': source['credit'], 'rights': source['credit']}
        entries = [entry]
        if key[4:] in APPS and len(shots) > shot_index + 1:
            alternative = shots[shot_index+1]['path_full']
            payload = fetch.get(alternative,'image/*')
            if len(payload)>20000 and payload != data:
                alternate_name=key+'-gameplay-detail.jpg'
                with open(os.path.join(fetch.ASSETS,alternate_name),'wb') as f:f.write(payload)
                entries.append({**entry,'file':alternate_name,'url':alternative})
        return key, entries, None
    except Exception as exc:
        return key, None, str(exc)

def main():
    with open(fetch.MANIFEST, encoding='utf-8-sig') as f:
        manifest = json.load(f)
    with open(os.path.join(fetch.ROOT, 'quick_settings', 'history_days.json'), encoding='utf-8-sig') as f:
        days = json.load(f)['days']
    wanted = []
    for date, day in days.items():
        key = day['media_id']
        entries = manifest.get(key, [])
        if date not in APPS and date not in COMMONS:
            continue
        wanted.append((key, day, entries))
    results = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        for result in pool.map(repair, wanted):
            results.append(result)
            print(result[0], 'OK' if result[1] else result[2], flush=True)
    with open(fetch.MANIFEST, encoding='utf-8-sig') as f:
        latest = json.load(f)
    for key, entry, error in results:
        if entry:
            latest[key] = entry
    with open(fetch.MANIFEST, 'w', encoding='utf-8') as f:
        json.dump(latest, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print('REPAIRED', sum(bool(r[1]) for r in results), 'OF', len(results))

if __name__ == '__main__':
    main()
