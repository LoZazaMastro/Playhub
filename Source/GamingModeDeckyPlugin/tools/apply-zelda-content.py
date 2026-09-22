"""Merge the reviewed Zelda essay and exact chapter media, preserving other work."""
import json
import pathlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
content=read(ROOT/'tools/zelda-content.json')
images=read(ROOT/'tools/zelda-images.json')
chapters=content['chapters']
for chapter in chapters:
    if not chapter.get('image_file'):
        chapter['image_file']=next(i['file'] for i in images if i['subject']==chapter['subject'])
assert len({c['image_file'] for c in chapters})==6
catalog_path=ROOT/'quick_settings/history_editorial.json'
catalog=read(catalog_path)
theme=next(t for t in catalog['themes'] if t['id']=='zelda')
theme['chapters']=chapters
theme['sources']=list(dict.fromkeys(theme.get('sources',[])+content['sources']))
catalog_path.write_text(json.dumps(catalog,ensure_ascii=False,indent=1)+'\n',encoding='utf-8')
manifest_path=ROOT/'quick_settings/history_images.json'
manifest=read(manifest_path)
planning=dict(file='zelda-planning.png',subject='The Legend of Zelda',kind='game',provider='User supplied',url='local://zelda-planning.png',sourceUrl=content['sources'][0],credit='Nintendo; image supplied by project owner',rights='Copyright Nintendo; development document shown for editorial discussion.')
retained=[m for m in manifest['zelda'] if m['file']=='zelda-0.jpg' or m.get('kind')=='cover']
manifest['zelda']=retained+[planning]+images
manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=1)+'\n',encoding='utf-8')
print('Merged Zelda: 6 chapters, explicit images, other catalog and manifest entries preserved.')
