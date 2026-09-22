"""Publish the five visually reviewed candidates to standalone integration data."""
import hashlib
import json
import pathlib
import shutil
from PIL import Image
ROOT=pathlib.Path(__file__).resolve().parents[1]
CANDIDATES=ROOT/'tools/zelda-candidates'
ASSETS=ROOT/'src/assets/history'
sources={pathlib.Path(r['file']).name:r for r in json.loads((CANDIDATES/'sources.json').read_text())}
selections=[
 ('nes-1.png','zelda-nes-exploration.png','The Legend of Zelda'),
 ('past-7.bmp','zelda-past-dark-world.png','The Legend of Zelda: A Link to the Past'),
 ('ocarina-2.jpg','zelda-ocarina-targeting.jpg','The Legend of Zelda: Ocarina of Time'),
 ('wind-3.jpg','zelda-wind-waker-sea.jpg','The Legend of Zelda: The Wind Waker'),
 ('wild-climb.jpg','zelda-breath-climbing.jpg','The Legend of Zelda: Breath of the Wild')
]
rows=[]
for source,dest,subject in selections:
    im=Image.open(CANDIDATES/source)
    assert im.width>im.height
    if source.endswith('.bmp'):
        im.save(ASSETS/dest,format='PNG')
    else:
        shutil.copyfile(CANDIDATES/source,ASSETS/dest)
    origin=sources.get(source,dict(url='https://images-fe.ssl-images-amazon.com/images/I/71NRIA6pnqL.jpg',sourceUrl='https://gamesoft.bestgamearea.com/gamesoft/901'))
    rows.append(dict(file=dest,subject=subject,kind='screenshot',provider='Nintendo' if source!='wild-climb.jpg' else 'Publisher promotional screenshot',url=origin['url'],sourceUrl=origin['sourceUrl'],credit='Nintendo',rights='Copyright Nintendo. Game screenshot; retained for editorial use.',width=im.width,height=im.height,sha256=hashlib.sha256((ASSETS/dest).read_bytes()).hexdigest(),visual_review='Reviewed: gameplay, landscape, exact named game; no cover.'))
assert len({r['sha256'] for r in rows})==5
(ROOT/'tools/zelda-images.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print([(r['file'],r['width'],r['height']) for r in rows])
