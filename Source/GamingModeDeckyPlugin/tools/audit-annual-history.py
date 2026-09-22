"""Audit actual selected subjects and renderable images, not date-based IDs."""
import datetime
import json
from pathlib import Path
import sys
import re
from collections import defaultdict

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
from quick_settings.daily_history import select_editorial, display_subject

def main():
    catalog=json.loads((ROOT/'quick_settings/history_editorial.json').read_text(encoding='utf-8-sig'))
    images=json.loads((ROOT/'quick_settings/history_images.json').read_text(encoding='utf-8-sig'))
    subjects=defaultdict(list)
    issues=[]
    for number in range(365):
        day=datetime.date(2026,1,1)+datetime.timedelta(days=number)
        theme,_,_=select_editorial(catalog,day)
        subjects[theme['topic']].append(day.isoformat())
        media=images.get(theme.get('media_id',theme['id']),[])
        for index,chapter in enumerate([{}]+theme.get('chapters',[])):
            candidates=[item for item in media if
                        (not chapter.get('subject') or chapter['subject']==item['subject']) and
                        (not chapter.get('image_file') or chapter['image_file']==item['file']) and
                        (chapter.get('image_role')=='cover' or
                         (item.get('kind')!='cover' and not re.search(r'images\.igdb\.com/.*/co\w+\.',item.get('url','')))) and
                        (ROOT/'src/assets/history'/item['file']).is_file()]
            if not candidates:
                issues.append({'date':day.isoformat(),'subject':display_subject(theme),'chapter':index,'problem':'no matching non-cover image'})
    duplicates={subject:dates for subject,dates in subjects.items() if len(dates)>1}
    report={'days':365,'unique_subjects':len(subjects),'repeated_subjects':duplicates,'image_failures':issues}
    output=ROOT/'work/annual-history-audit.json'
    output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    print(f"365 days; {len(subjects)} unique subjects; {len(duplicates)} repeated subjects; {len(issues)} card image failures")
    print(output)
    return bool(duplicates or issues)

if __name__=='__main__':sys.exit(main())
