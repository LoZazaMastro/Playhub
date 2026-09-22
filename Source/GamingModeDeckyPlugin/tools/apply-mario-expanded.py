"""Apply the curated Mario expansion, preserving all unrelated catalog entries."""
import json
import hashlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=1) + '\n', encoding='utf-8')

def main():
    proposal_path = ROOT / 'tools/mario-expanded-content.json'
    proposal = read(proposal_path)
    theme = proposal['theme']
    fixes = {'mario-opening-run.jpg':'mario-0.jpg','mario-jump-gap.jpg':'mario-jump-gap.png','mario-level-planning-sheet.jpg':'mario-level-planning-sheet.png','mario-kondo-composing.jpg':'mario-kondo-score.png','mario-maker-legacy.jpg':'mario-maker-legacy.png'}
    for chapter in theme['chapters']:
        chapter['image_file'] = fixes.get(chapter['image_file'], chapter['image_file'])
    theme['chapters'][4]['image_role'] = 'document'
    theme['chapters'][3]['title'].update(dict(de='Vor der Landschaft war ein Kästchen',es='Antes del paisaje, una casilla',fr='Avant le paysage, un carreau de papier',pt='Antes da paisagem, um quadradinho',ru='До пейзажа была клетка на бумаге',uk='До краєвиду була клітинка на папері',ja='風景の始まりは方眼の一マス',ko='풍경은 모눈 한 칸에서 시작됐다',zh='風景始於方格紙上的一格',hi='दृश्य से पहले कागज़ पर एक खाना था'))
    theme['chapters'][5]['title'].update(ja='遊びの文法がプレイヤーの手に渡るとき',ko='놀이의 문법이 플레이어의 손으로 넘어갈 때')
    for item in proposal['image_plan']:
        item['file'] = fixes.get(item['file'], item['file'])
    proposal['image_plan'][3]['brief'] = 'Original World 1-1 gameplay: Mario mid-jump between two pipes, distinct from the first Goomba scene.'
    proposal['image_plan'][5]['brief'] = 'Original Koji Kondo signed score reproduced on page 3 of the official Super Mario Maker booklet; complete left page.'
    proposal['image_plan'][6]['brief'] = 'Official booklet page 8: stylus assembling blocks and a mushroom on graph paper, illustrating handing design tools to players.'
    files = ['mario-0.jpg'] + [c['image_file'] for c in theme['chapters']]
    assert len(set(files)) == 7
    assert len({hashlib.sha256((ROOT/'src/assets/history'/f).read_bytes()).hexdigest() for f in files}) == 7
    locales = proposal['translation_review']['locales']
    for record, fields in [(theme,['title','intro'])] + [(c,['title','body','kicker']) for c in theme['chapters']]:
        for field in fields:
            assert all(record[field].get(locale,'').strip() for locale in locales), field
    catalog_path = ROOT/'quick_settings/history_editorial.json'
    manifest_path = ROOT/'quick_settings/history_images.json'
    catalog, manifest = read(catalog_path), read(manifest_path)
    old = next(t for t in catalog['themes'] if t['id']=='mario')
    old.update(theme)
    for anniversary in catalog['anniversaries']:
        if anniversary['theme_id']=='mario':
            anniversary['title'] = theme['title']
            anniversary['intro'] = theme['intro']
    existing = manifest['mario']
    hero = next(m for m in existing if m['file']=='mario-0.jpg')
    booklet='https://www.nintendo.co.jp/wiiu/amaj/booklet/SuperMarioMakerBooklet.pdf'
    sources=[
      ('https://cdn.mos.cms.futurecdn.net/91dbb774202d472221504fc4a0e30811.jpg','https://www.gamesradar.com/history-shiny-things-video-games/','screenshot'),
      ('https://assets.nintendo.eu/video/private/f_auto%2Cq_auto/tcdfs9jlonu8ybpbeyqf.jpg','https://www.nintendo.com/en-za/News/2023/October/Ask-the-Developer-Vol-11-Super-Mario-Bros-Wonder-Chapter-2-2460633.html','screenshot'),
      ('https://cdn.mos.cms.futurecdn.net/n4PYpg4zyge4svaZRJeobH.png','https://www.creativebloq.com/features/video-games-of-the-80s','screenshot'),
      (booklet+'#page=44',booklet,'document'),
      (booklet+'#page=3',booklet,'document'),
      (booklet+'#page=8',booklet,'document')]
    entries=[hero]
    for chapter,(url,source,kind) in zip(theme['chapters'],sources):
        entries.append(dict(file=chapter['image_file'],url=url,sourceUrl=source,subject=chapter['subject'],kind=kind,provider='Nintendo' if 'nintendo.' in source else 'Editorial screenshot source',rights='Nintendo and respective rights holders; editorial illustration.'))
    entries += [m for m in existing if m.get('kind')=='cover']
    manifest['mario']=entries
    proposal['status']='Applied: seven distinct local narrative images, twelve locales, explicit subjects and kickers.'
    write(proposal_path,proposal)
    write(catalog_path,catalog)
    write(manifest_path,manifest)
    print('Mario applied: 7 narrative cards, 12 locales, 7 unique assets')

if __name__=='__main__':
    main()
