"""Collect Nintendo screenshot candidates and a contact sheet for manual review."""
import concurrent.futures
import io
import json
import pathlib
import re
import urllib.request
from PIL import Image, ImageDraw

ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'tools'/'zelda-candidates'
PAGES={
 'nes':'https://www.nintendo.com/es-es/Juegos/NES/The-Legend-of-Zelda-796345.html',
 'past':'https://www.nintendo.com/it-it/Giochi/Super-Nintendo/The-Legend-of-Zelda-A-Link-to-the-Past-841179.html',
 'ocarina':'https://www.nintendo.com/en-gb/Games/Nintendo-64/The-Legend-of-Zelda-Ocarina-of-Time-269536.html',
 'wind':'https://www.nintendo.com/en-gb/Games/Wii-U-games/The-Legend-of-Zelda-The-Wind-Waker-HD-765386.html',
 'wild':'https://www.nintendo.com/en-gb/Games/Nintendo-Switch-games/The-Legend-of-Zelda-Breath-of-the-Wild-1173609.html'
}
def collect(item):
    key,page=item
    html=urllib.request.urlopen(page,timeout=30).read().decode()
    urls=list(dict.fromkeys(re.findall(r'https://[^\s"<>]+/06_screenshots/[^\s"<>]+(?:png|jpg|bmp)',html)))
    urls=[u for u in urls if not re.search(r'_TM_|_image\d+w',u)]
    result=[]
    for i,url in enumerate(urls[:12]):
        try:
            data=urllib.request.urlopen(url,timeout=30).read()
            im=Image.open(io.BytesIO(data)); im.verify()
            file=OUT/f'{key}-{i}{pathlib.Path(url).suffix}'
            file.write_bytes(data)
            result.append(dict(file=str(file),url=url,sourceUrl=page))
        except Exception as e: print(key,i,str(e),flush=True)
    print(key,len(result),flush=True)
    return result
if __name__=='__main__':
    OUT.mkdir(exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        rows=[r for group in pool.map(collect,PAGES.items()) for r in group]
    (OUT/'sources.json').write_text(json.dumps(rows,indent=2),encoding='utf-8')
    sheet=Image.new('RGB',(1000,200*((len(rows)+3)//4)), '#202020')
    draw=ImageDraw.Draw(sheet)
    for i,r in enumerate(rows):
        im=Image.open(r['file']).convert('RGB'); im.thumbnail((242,170))
        x=(i%4)*250; y=(i//4)*200
        sheet.paste(im,(x,y)); draw.text((x,y+172),pathlib.Path(r['file']).name,fill='white')
    sheet.save(OUT/'contact.jpg')
