"""Download hand-selected screenshots, merge only their daily manifest entries."""
import importlib.util
import json
import os

spec=importlib.util.spec_from_file_location('fetch',os.path.join(os.path.dirname(__file__),'fetch-history-images.py'))
fetch=importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetch)
ROWS={
 '01-13':[
  ('https://s3-eu-west-1.amazonaws.com/games.snapshot/596/73844-PrinceofPersia.jpg','https://www.squakenet.com/game/prince-of-persia/'),
  ('https://s2.dmcdn.net/v/6BGgl1ep1XHzoJ_sD/x720','https://www.dailymotion.com/video/x1potyn')],
 '01-19':[
  ('https://thekingofgrabs.com/wp-content/uploads/2020/02/lemmings-amiga-07.png','https://thekingofgrabs.com/2020/02/22/lemmings-amiga/'),
  ('https://thekingofgrabs.com/wp-content/uploads/2020/02/lemmings-amiga-19.png','https://thekingofgrabs.com/2020/02/22/lemmings-amiga/')],
 '11-30':[
  ('https://www.gtabase.com/images/gta-5/gta-online-credits.png','https://www.gtabase.com/articles/grand-theft-auto-v/how-to-afk-in-gta-online-after-the-money-fronts-update'),
  ('https://www.artofthetitle.com/assets/sm/upload/yl/dv/yf/7h/doom_c.jpg?k=98a7d74505','https://www.artofthetitle.com/title/doom/')],
 '08-15':[
  ('https://openlab.citytech.cuny.edu/retrocomputingatcitytech/files/2018/07/IMG_20180716_155206.jpg','https://openlab.citytech.cuny.edu/retrocomputingatcitytech/retrocomputing-software-archive/'),
  ('https://dhlz08bawgtqj.cloudfront.net/media/images/FloppyDiscs.width-1200.format-jpeg.jpg','https://www.ngataonga.org.nz/explore-stories/stories/new-zealand-history/forward-to-the-past-engaging-with-new-zealands-early-computer-games/')],
 '12-12':[
  ('https://cdn.mos.cms.futurecdn.net/v2/t%3A0%2Cl%3A0%2Ccw%3A1057%2Cch%3A595%2Cq%3A80/RFVLHToMEUnxVvtdn8ZwHe.png','https://www.pcgamer.com/software/platforms/9-big-things-steam-needs-to-improve-in-2026/'),
  ('https://storage.ghost.io/c/c5/ae/c5ae6bf5-37e7-4705-a7d2-7198ae312da0/content/images/2025/07/Screenshot-2025-07-19-at-20.32.38.png','https://www.eyespark.net/little-rocket-man/')],
 '12-20':[
  ('https://www.sell.fr/sites/default/files/styles/paragraphe_img_paragraphe_xs_x1/public/logos_pegi.jpg?itok=7scLqSqP','https://www.sell.fr/news/campagne-dinformation-pegi'),
  ('https://kidsafe.gamified.uk/images/age-ratings.png','https://kidsafe.gamified.uk/')],
 '11-01':[
  ('https://milestone.it/wp-content/uploads/2025/04/milestone-careers-header.webp','https://milestone.it/careers/'),
  ('https://images2.corriereobjects.it/methode_image/2018/10/04/Tecnologia/Foto%20Tecnologia%20-%20Trattate/careers-milestone-kqfB-U3040241036914a8-1224x916%40Corriere-Web-Sezioni-593x443.jpg?v=20181011125516','https://www.corriere.it/tecnologia/milano-games-week/notizie/viaggio-nell-italia-videogiochi-nord-sud-9e448e42-c80d-11e8-95ee-ea5556d06e7a.shtml')],
 '11-12':[
  ('https://cdn.psxe.net/2023/06/Remedy-Headquarters-1024x576.jpg','https://psxextreme.com/news/alan-wake-2-and-the-digital-only-future/'),
  ('https://pbs.twimg.com/media/G1yApimWIAAU68e.jpg','https://x.com/remedygames/status/1971592210173808752')],
 '11-15':[
  ('https://gameinformer.com/sites/default/files/styles/no_compression/public/2025/05/14/acd7d0ef/devolver.jpeg.webp','https://gameinformer.com/photo-gallery/2025/05/15/take-a-photo-tour-of-my-favorite-booths-at-pax-east-2025'),
  ('https://agirls.aottercdn.com/media/aae7e2de-9b37-49bd-92bc-6c2aeadc7131.jpg','https://agirls.aotter.net/post/62894')],
 '09-07':[
  ('https://image.api.playstation.com/cdn/JP0005/CUSA01557_00/FREE_CONTENThw8sn8I4ZCPyR5kphyK0/302149.jpg','https://www.playstation.com/ja-jp/games/persona-5/'),
  ('https://i3.ruliweb.com/img/16/09/08/15707dd488f471761.jpg','https://m.ruliweb.com/ps/board/300001/read/2107400')],
 '09-10':[
  ('https://cdn.mobygames.com/screenshots/3157208-monster-hunter-playstation-2-on-the-first-quest-looking-for-velo.jpg','https://www.mobygames.com/game/26011/monster-hunter/screenshots/ps2/796923/'),
  ('https://www.gamespark.jp/imgs/zoom/194293.jpg','https://www.gamespark.jp/article/2017/07/30/74906.html')],
 '09-20':[
  ('https://www.dazeland.com/images/PC-consoles/Command_and_Conquer-15.png','https://www.dazeland.com/en/PC-consoles/Command_and_Conquer.html'),
  ('https://www.dazeland.com/images/PC-consoles/Command_and_Conquer-12.png','https://www.dazeland.com/en/PC-consoles/Command_and_Conquer.html')],
 '09-24':[
  ('https://cdn.mobygames.com/screenshots/10392831-shogun-total-war-windows-attacking-across-a-bridge-in-a-heavy-th.jpg','https://www.mobygames.com/game/1692/shogun-total-war/screenshots/windows/11438/'),
  ('https://cdn.mobygames.com/screenshots/10392825-shogun-total-war-windows-breaking-up-archer-formations.jpg','https://www.mobygames.com/game/1692/shogun-total-war/screenshots/windows/11436/')],
 '02-14':[
  ('https://images.cgames.de/images/gsgp/4/tekken-3_2799541.jpg','https://www.gamepro.de/artikel/mein-herz-fuer-klassiker-ich-tekken-3-das-move-heft-ein-eintrag-ins-klassenbuch,3314920.html'),
  ('https://images.launchbox-app.com/r2_7e841374-829b-4c15-89c4-7ac9caf8a566.png','https://gamesdb.launchbox-app.com/games/images/2414-tekken-3')],
 '02-25':[
  ('https://www.sega.co.jp/en/release/images/250422_1/250422_1_02.jpg','https://www.sega.co.jp/en/release/250422_1.html'),
  ('https://regmedia.co.uk/2013/12/09/outrun_5.jpg','https://www.theregister.com/2013/12/18/antique_code_show_sega_out_run/')],
 '10-07':[
  ('https://images.nintendolife.com/screenshots/2310/900x.jpg','https://www.nintendolife.com/games/gamecube/eternal_darkness_sanitys_requiem/screenshots'),
  ('https://cdn.mobygames.com/screenshots/10185214-eternal-darkness-sanitys-requiem-gamecube-casting-a-spell.jpg','https://www.mobygames.com/game/6825/eternal-darkness-sanitys-requiem/screenshots/gamecube/28458/')]
}

def main():
    with open(os.path.join(fetch.ROOT,'quick_settings','history_days.json'),encoding='utf-8-sig') as f:
        days=json.load(f)['days']
    updates={}
    for day,rows in ROWS.items():
        entries=[]
        for i,(url,source) in enumerate(rows):
            try:
                payload=fetch.get(url,'image/*')
                if not payload.startswith((b'\xff\xd8',b'\x89PNG',b'RIFF')): raise ValueError('not an image')
                file=f'day-{day}-curated-{i}.jpg'
                with open(os.path.join(fetch.ASSETS,file),'wb') as f:f.write(payload)
                kind='company' if day in ('11-01','11-12','11-15') else 'photo' if day=='08-15' else 'reference' if day=='12-20' else 'screenshot'
                entries.append({'file':file,'subject':days[day]['subject'],'kind':kind,'url':url,'sourceUrl':source,'credit':source,'rights':'Editorial photography and game imagery: respective photographers, publishers and rights holders.'})
                print(file,len(payload),flush=True)
            except Exception as e:print(day,i,e,flush=True)
        if entries:updates['day-'+day]=entries
    with open(fetch.MANIFEST,encoding='utf-8-sig') as f:manifest=json.load(f)
    manifest.update(updates)
    with open(fetch.MANIFEST,'w',encoding='utf-8') as f:json.dump(manifest,f,ensure_ascii=False,indent=1);f.write('\n')

if __name__=='__main__':main()
