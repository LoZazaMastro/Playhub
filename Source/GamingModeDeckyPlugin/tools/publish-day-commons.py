"""Merge reviewed culture images; preserve concurrent unrelated manifest keys."""
import json,shutil,importlib.util
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
CHOICES={'day-03-26':[1,3],'day-03-27':[0,1],'day-03-28':[2,3],'day-03-29':[0,4],'day-03-30':[1,3],'day-03-31':[1,3]}
days=json.loads((ROOT/'quick_settings/history_days.json').read_text(encoding='utf8'))['days']
CHOICES.update({'day-01-01':[0],'day-01-02':[0,2],'day-01-03':[0,3],'day-01-04':[0,3]})
updates={}
for key,choices in CHOICES.items():
 rows=json.loads((ROOT/'work/replacement-candidates'/(key+'.json')).read_text(encoding='utf8'));updates[key]=[]
 for i in choices:
  row=rows[i];name=key+'-culture-'+str(i)+'.jpg';shutil.copyfile(ROOT/'work/replacement-candidates'/row['file'],ROOT/'src/assets/history'/name)
  updates[key].append({k:v for k,v in {**row,'file':name,'subject':days[key[4:]]['subject'],'kind':'screenshot' if key=='day-03-26' else 'photo','rights':row['credit']}.items() if k not in ('description','title')})
spec=importlib.util.spec_from_file_location('fetch',ROOT/'tools/fetch-history-images.py');fetch=importlib.util.module_from_spec(spec);spec.loader.exec_module(fetch)
url='https://www.shillyash.com/images/games/xyzzy.jpg';name='day-01-01-maze.jpg';(ROOT/'src/assets/history'/name).write_bytes(fetch.get(url,'image/*'))
updates['day-01-01'].append({'file':name,'subject':days['01-01']['subject'],'kind':'screenshot','url':url,'sourceUrl':'https://www.shillyash.com/games/adventure/','credit':'Colossal Cave Adventure / Shillyash','rights':'Editorial screenshot; respective rights holders'})
path=ROOT/'quick_settings/history_images.json';data=json.loads(path.read_text(encoding='utf8'));data.update(updates)
for row in data.get('editorial-manuals',[]):row['kind']='photo'
path.write_text(json.dumps(data,ensure_ascii=False,indent=1)+'\n',encoding='utf8')
print('Published',len(updates),'reviewed daily image sets')
