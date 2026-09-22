"""Publish visually reviewed candidates to standalone importer contract."""
import json,shutil,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
SELECT={'editorial-akihabara':[2,4],'editorial-archaeogaming':[0,1],'editorial-brutalism':[2,4],'editorial-cdrom':[0,2],'editorial-cosplay':[2,3],'editorial-cyberpunk':[0,2],'editorial-diorama':[1,2],'editorial-inkwash':[0,2],'editorial-logo':[0,2],'editorial-pinball':[0,1],'editorial-underwater-sound':[0,2]}
def main():
 SELECT['editorial-heraldry']=[1,2]
 replacements=json.loads((ROOT/'tools/calendar-replacements.json').read_text(encoding='utf8'))['replacements']
 subjects={r['media_id']:r['chapters'][0]['subject'] for r in replacements}
 images={}
 for key,indices in SELECT.items():
  candidates=json.loads((ROOT/'work/replacement-candidates'/(key+'.json')).read_text(encoding='utf8'))
  images[key]=[]
  for i in indices:
   item=candidates[i];shutil.copyfile(ROOT/'work/replacement-candidates'/item['file'],ROOT/'src/assets/history'/item['file'])
   images[key].append({k:v for k,v in {**item,'subject':subjects[key],'kind':'artwork' if key in ('editorial-inkwash','editorial-cyberpunk') else 'photo','rights':item['credit']}.items() if k not in ('description','title')})
 spec=importlib.util.spec_from_file_location('fetch',ROOT/'tools/fetch-history-images.py');fetch=importlib.util.module_from_spec(spec);spec.loader.exec_module(fetch)
 key='editorial-lego';game=fetch.get_json('https://store.steampowered.com/api/appdetails?appids=32440')['32440']['data'];images[key]=[]
 for i in (0,1):
  url=game['screenshots'][i]['path_full'];name=f'{key}-{i}.jpg';(ROOT/'src/assets/history'/name).write_bytes(fetch.get(url,'image/*'))
  images[key].append({'file':name,'subject':subjects[key],'kind':'screenshot','url':url,'sourceUrl':'https://store.steampowered.com/app/32440/','credit':fetch.RIGHTS_GAME,'rights':fetch.RIGHTS_GAME})
 key='editorial-manuals';images[key]=[]
 for i,(url,source) in enumerate([
  ('https://quuxplusone.github.io/blog/images/2025-04-19-vecchitto-tetris-2.jpg','https://quuxplusone.github.io/blog/2025/04/19/tetromino-names/'),
  ('https://coregamingnh.com/cdn/shop/files/IMG_8784_68e5168f-6f39-458c-be68-702887a2f2e7.jpg?v=1728508040&width=1080','https://coregamingnh.com/products/super-mario-bros-2-nes-nintendo-entertainment-system-manual-only-du10924')]):
  name=f'{key}-{i}.jpg';(ROOT/'src/assets/history'/name).write_bytes(fetch.get(url,'image/*'))
  images[key].append({'file':name,'subject':subjects[key],'kind':'photo','url':url,'sourceUrl':source,'credit':'Nintendo / photograph or scan via linked source','rights':'Copyright of respective rights holders; editorial reference'})
 (ROOT/'tools/calendar-replacement-images.json').write_text(json.dumps({'images':images},ensure_ascii=False,indent=1)+'\n',encoding='utf8')
 print('Published',len(images),'subjects; no calendar/manifest changes.')
if __name__=='__main__':main()

