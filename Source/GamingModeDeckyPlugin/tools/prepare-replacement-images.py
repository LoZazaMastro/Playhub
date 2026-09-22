"""Find Commons candidates; selection is explicitly visual, not automatic publication."""
import importlib.util,json,concurrent.futures,re
from pathlib import Path
spec=importlib.util.spec_from_file_location('fetch',Path(__file__).with_name('fetch-history-images.py'))
fetch=importlib.util.module_from_spec(spec);spec.loader.exec_module(fetch)
OUT=Path(fetch.ROOT)/'work/replacement-candidates';OUT.mkdir(exist_ok=True)
QUERIES={'editorial-cdrom':'CD-ROM drive','editorial-brutalism':'brutalist architecture','editorial-logo':'Logo turtle graphics','editorial-manuals':'video game manual','editorial-pinball':'pinball playfield','editorial-inkwash':'Chinese ink landscape painting','editorial-cosplay':'video game cosplay','editorial-diorama':'diorama miniature','editorial-archaeogaming':'Atari landfill excavation','editorial-akihabara':'Akihabara street','editorial-cyberpunk':'cyberpunk city','editorial-underwater-sound':'hydrophone','editorial-heraldry':'heraldry coat of arms'}
def run(pair):
 key,query=pair
 try:
  url='https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=12&gsrsearch='+fetch.urllib.parse.quote(query)+'&prop=imageinfo&iiprop=url|size|mime|extmetadata&iiurlwidth=1200'
  pages=fetch.get_json(url).get('query',{}).get('pages',{})
  rows=[]
  for page in sorted(pages.values(),key=lambda p:p.get('index',0)):
   info=page.get('imageinfo',[{}])[0]
   if info.get('mime') not in ('image/jpeg','image/png') or info.get('width',0)<700:continue
   meta=info.get('extmetadata',{});imageurl=info.get('thumburl',info.get('url'))
   data=fetch.get(imageurl,'image/*');name=f'{key}-{len(rows)}.jpg';(OUT/name).write_bytes(data)
   rows.append({'file':name,'url':imageurl,'sourceUrl':info['descriptionurl'],'credit':re.sub('<[^>]+>','',meta.get('Artist',{}).get('value',''))+' / '+meta.get('LicenseShortName',{}).get('value',''),'description':re.sub('<[^>]+>','',meta.get('ImageDescription',{}).get('value','')),'title':page['title']})
   if len(rows)==6:break
  (OUT/(key+'.json')).write_text(json.dumps(rows,ensure_ascii=False,indent=1),encoding='utf8');print(key,len(rows),flush=True)
 except Exception as exc:print(key,str(exc),flush=True)
if __name__=='__main__':
 with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:list(pool.map(run,QUERIES.items()))
