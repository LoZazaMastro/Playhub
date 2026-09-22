"""Read-only Commons discovery for the replacement essays."""
import concurrent.futures
import json
import urllib.parse
import urllib.request

QUERIES = ['CD-ROM','brutalist building','Logo programming turtle','video game manual',
           'pinball machine playfield','LEGO video game','sumi-e landscape','video game cosplay costume',
           'diorama room','Atari video game burial excavation','Akihabara street','cyberpunk city',
           'hydrophone underwater','heraldry shields']

def search(query):
    url='https://commons.wikimedia.org/w/api.php?'+urllib.parse.urlencode(dict(action='query',format='json',generator='search',gsrnamespace=6,gsrlimit=8,gsrsearch=query,prop='imageinfo',iiprop='url|size|mime|extmetadata',iiurlwidth=1600))
    req=urllib.request.Request(url,headers={'User-Agent':'Playhub Editorial/1.0'})
    data=json.load(urllib.request.urlopen(req,timeout=30))
    return query, [{'title':p['title'],'width':p['imageinfo'][0].get('width'),'mime':p['imageinfo'][0].get('mime')} for p in data.get('query',{}).get('pages',{}).values()]

if __name__=='__main__':
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        for q, result in pool.map(search,QUERIES):
            print(q, json.dumps(result,ensure_ascii=False),flush=True)
