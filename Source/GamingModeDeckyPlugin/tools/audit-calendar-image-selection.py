"""Audit selected annual card assets, independently of availability-only checks."""
import datetime,hashlib,json,re,sys
from pathlib import Path
from collections import defaultdict,Counter
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from quick_settings.daily_history import select_editorial
def main():
 catalog=json.loads((ROOT/'quick_settings/history_editorial.json').read_text(encoding='utf8'))
 media=json.loads((ROOT/'quick_settings/history_images.json').read_text(encoding='utf8'))
 report={'missing_files':[],'cover_card_candidates':[],'missing_card_candidates':[],'reused_selected_files':[],'hero_chapter_reuse':[],'selection':[],'unreviewed_semantic_coherence':[]}
 usage=defaultdict(list)
 for n in range(365):
  date=datetime.date(2026,1,1)+datetime.timedelta(days=n);theme=select_editorial(catalog,date)[0];key=theme.get('media_id',theme['id']);all=media.get(key,[]);picked=[]
  for index,ch in enumerate([{}]+theme.get('chapters',[])):
   candidates=[im for im in all if (not ch.get('subject') or ch['subject']==im['subject']) and (not ch.get('image_file') or ch['image_file']==im['file']) and (ch.get('image_role')=='cover' or im.get('kind')!='cover')]
   matching=[im for im in candidates if im.get('kind')==ch.get('image_role')] if ch.get('image_role') else []
   if matching:candidates=matching
   elif not ch.get('image_file') and candidates:
    offset=index%len(candidates);candidates=candidates[offset:]+candidates[:offset]
   label={'date':str(date),'media_id':key,'index':index,'subject':ch.get('subject') or theme.get('subject') or theme['topic'],'title':ch.get('title',{}).get('it','')}
   if not candidates:report['missing_card_candidates'].append(label);continue
   im=candidates[0];file=ROOT/'src/assets/history'/im['file'];row={**label,'file':im['file']};report['selection'].append(row)
   if not file.is_file():report['missing_files'].append(row);continue
   digest=hashlib.sha256(file.read_bytes()).hexdigest();usage[digest].append(row);picked.append((index,digest,row))
   if re.search(r'images\.igdb\.com/.*/co\w+\.',im.get('url','')):report['cover_card_candidates'].append(row)
   report['unreviewed_semantic_coherence'].append(row)
  if picked:
   for index,digest,row in picked[1:]:
    if digest==picked[0][1]:report['hero_chapter_reuse'].append(row)
 report['reused_selected_files']=[rows for rows in usage.values() if len(rows)>1]
 report['counts']={k:len(v) for k,v in report.items()}
 out=ROOT/'work/calendar-image-selection-audit.json';out.write_text(json.dumps(report,ensure_ascii=False,indent=1),encoding='utf8');print(json.dumps(report['counts']))
 if '--priority' in sys.argv:
  for d in ('09-09','09-05','08-29','08-31','09-02'):
   theme=select_editorial(catalog,datetime.date.fromisoformat('2026-'+d))[0];key=theme.get('media_id',theme['id']);print(json.dumps({'date':d,'key':key,'chapters':[{'title':c['title'].get('it'),'subject':c.get('subject'),'file':c.get('image_file')} for c in theme.get('chapters',[])],'images':media.get(key)},ensure_ascii=True))
if __name__=='__main__':main()
