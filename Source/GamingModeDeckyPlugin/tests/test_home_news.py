import unittest,tempfile,time,threading,gzip
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from quick_settings.home_news import HomeNews,parse_feed,LIMIT
FEED=b'<rss><channel><item><title>Game &amp; title</title><link>https://multiplayer.it/story</link><description>&lt;img src="https://example.com/cover.jpg" /&gt;</description></item></channel></rss>'
class NewsTests(unittest.TestCase):
 def test_reported_publishers_use_their_working_direct_feeds(self):
  import quick_settings.home_news as module
  configured={name:feed for sources in module.SOURCES.values() for name,_domain,feed in sources}
  self.assertEqual(configured['GameSpot'],'https://www.gamespot.com/feeds/mashup/')
  self.assertEqual(configured['3DJuegos'],'https://www.3djuegos.com/feedburner.xml')
  self.assertEqual(configured['Vandal'],'https://vandal.elespanol.com/xml.cgi?rss=2')
  self.assertEqual(configured['Jeuxvideo.com'],'https://www.jeuxvideo.com/rss/rss.xml')
  self.assertNotIn('PC Gamer',configured)
  self.assertEqual(configured['GameVicio'],'')
  self.assertEqual(configured['Игромания'],'https://www.igromania.ru/rss/news.xml')
  self.assertEqual(configured['GameMAG'],'https://gamemag.ru/rss/feed')
  self.assertEqual(configured['Inven'],'https://feeds.feedburner.com/inven')
  self.assertEqual(configured['遊戲基地'],'https://news.gamebase.com.tw/rss.xml')
  self.assertNotIn('Gameverse',configured)

 def test_fetch_accepts_a_gzipped_publisher_feed(self):
  import quick_settings.home_news as module
  class Headers:
   def get(self,key,default=''): return 'gzip' if key=='Content-Encoding' else default
  class Response:
   headers=Headers()
   def read(self,_): return gzip.compress(FEED)
   def __enter__(self): return self
   def __exit__(self,*_): return False
  from unittest.mock import patch
  with patch.object(module.urllib.request,'urlopen',return_value=Response()):
   self.assertEqual(module.HomeNews._fetch('https://vandal.elespanol.com/feed'),FEED)
 def test_home_can_show_48_distinct_news_with_publisher_balance(self):
  from unittest.mock import patch
  import quick_settings.home_news as module
  sources=[('One','one.example','https://one.example/rss'),('Two','two.example','https://two.example/rss')]
  def fetch(url):
   domain='one.example' if 'one.example' in url else 'two.example'
   return ('<rss><channel>'+''.join(f'<item><title>Game review {domain} {i}</title><link>https://{domain}/game-{i}</link></item>' for i in range(40))+'</channel></rss>').encode()
  with tempfile.TemporaryDirectory() as directory,patch.dict(module.SOURCES,{'IT':sources}):
   result=HomeNews(directory,fetch=fetch).get('it')
   self.assertEqual(len(result['items']),48)
   self.assertEqual(len({item['url'] for item in result['items']}),48)
   self.assertEqual(sum(item['source']=='One' for item in result['items']),24)
 def test_opt_in_and_validation(self):
  with tempfile.TemporaryDirectory() as d:
   service=HomeNews(d,fetch=lambda _:self.fail('disabled network'))
   self.assertTrue(service.settings()['enabled'])
   service.save(False,'auto')
   self.assertTrue(service.get('it')['disabled'])
   with self.assertRaises(ValueError):service.save(True,'XX')
   with self.assertRaises(ValueError):service.save('true','IT')
 def test_cache_and_network_failure_preserve_cards(self):
  with tempfile.TemporaryDirectory() as d:
   now=[1000];calls=[]
   def fetch(url):calls.append(url);return FEED
   service=HomeNews(d,fetch=fetch,clock=lambda:now[0]);service.save(True,'auto')
   first=service.get('it');self.assertEqual(len(first['items']),1)
   count=len(calls);service.get('it');self.assertEqual(len(calls),count)
   now[0]+=1801
   def fail(url):raise OSError('offline')
   service.fetch=fail;last=service.get('it');self.assertTrue(last['stale']);self.assertEqual(last['items'],first['items'])
 def test_safe_feed_metadata(self):
  row=parse_feed(FEED,'Publisher','https://example.com/feed')[0]
  self.assertEqual(row['title'],'Game & title');self.assertEqual(row['image'],'https://example.com/cover.jpg')
  for bad in [b'<!DOCTYPE rss><rss/>',b'<!ENTITY x "evil"><rss/>',b'x'*(LIMIT+1)]:
   with self.assertRaises(ValueError):parse_feed(bad,'x','https://example.com')
  self.assertEqual(parse_feed(FEED.replace(b'https://multiplayer.it/story',b'javascript:alert(1)'),'x','https://example.com'),[])
 def test_parser_runs_without_xml_or_expat_and_handles_cdata_atom(self):
  from unittest.mock import patch
  import builtins, importlib.util
  original = builtins.__import__
  def restricted(name, *args, **kwargs):
   if name.startswith(('xml', 'pyexpat')): raise ModuleNotFoundError(name)
   return original(name, *args, **kwargs)
  with patch('builtins.__import__', side_effect=restricted):
   spec=importlib.util.spec_from_file_location('news_frozen_fixture',Path(__file__).resolve().parents[1]/'quick_settings/home_news.py')
   module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
   data=b'<feed><entry><title><![CDATA[A &amp; B]]></title><link rel="alternate" href="https://example.com/a"/><media:thumbnail url="https://example.com/image.jpg"/></entry></feed>'
   row=module.parse_feed(data,'Site','https://example.com')[0]
   self.assertEqual(row['title'],'A & B');self.assertEqual(row['image'],'https://example.com/image.jpg')
 def test_country_selects_only_specialist_sources(self):
  with tempfile.TemporaryDirectory() as d:
   urls=[]
   def fetch(url):urls.append(url);return FEED
   service=HomeNews(d,fetch=fetch);service.save(True,'JP');service.get('it')
   self.assertTrue(any('gl=JP' in url and 'site%3A' in url for url in urls))
   self.assertTrue(all('q=' not in url or 'site%3A' in url for url in urls))
 def test_syndicated_general_outlet_is_rejected(self):
  with tempfile.TemporaryDirectory() as d:
   bad=FEED.replace(b'</item>',b'<source url="https://general-news.example">General newspaper</source></item>')
   service=HomeNews(d,fetch=lambda _:bad);service.save(True,'DE')
   self.assertEqual(service.get('de')['items'],[])
 def test_article_image_fallback_matches_news_metadata_variants(self):
  html=b'''<html><head><meta content="/cover-small.jpg" property="og:image"><meta property="twitter:image" content="/cover-twitter.jpg"></head><body><img data-lazy-srcset="/cover-640.jpg 640w, /cover-1600.jpg 1600w"></body></html>'''
  with tempfile.TemporaryDirectory() as d:
   service=HomeNews(d,fetch=lambda url:html)
   self.assertEqual(service._article_image('https://gamespot.com/news/story'),'https://gamespot.com/cover-small.jpg')
  html=b'<html><body><img data-lazy-srcset="/cover-640.jpg 640w, /cover-1600.jpg 1600w"></body></html>'
  with tempfile.TemporaryDirectory() as d:
   service=HomeNews(d,fetch=lambda url:html)
   self.assertEqual(service._article_image('https://gamespot.com/news/story'),'https://gamespot.com/cover-1600.jpg')

 def test_feed_parser_rejects_video_media_content_and_reads_lazy_images(self):
  feed=b'''<rss><channel>
   <item><title>Video first</title><link>https://gamevicio.com/a</link>
    <media:content url="https://youtube.com/embed/demo" type="text/html" />
    <description>&lt;img data-srcset="/small.jpg 480w, /cover.jpg 1440w" /&gt;</description></item>
   <item><title>Bing image</title><link>https://www.bing.com/news/apiclick.aspx?url=https%3A%2F%2Fgamestar.de%2Fb</link>
    <News:Image>http://www.bing.com/th?id=cover</News:Image></item>
   </channel></rss>'''
  rows=parse_feed(feed,'Publisher','https://example.com/feed')
  self.assertEqual(rows[0]['image'],'https://gamevicio.com/cover.jpg')
  self.assertEqual(rows[1]['image'],'https://www.bing.com/th?id=cover')

 def test_bing_transport_keeps_only_allowlisted_publisher_urls(self):
  from unittest.mock import patch
  import quick_settings.home_news as module
  good='https://www.bing.com/news/apiclick.aspx?url=https%3A%2F%2Fwww.gamepro.de%2Fartikel%2Freview%2C1.html'
  bad='https://www.bing.com/news/apiclick.aspx?url=https%3A%2F%2Fexample.com%2Fborrowed'
  bing=(f'<rss><channel><item><title>Allowed</title><link>{good.replace("&","&amp;")}</link>'
        '<News:Image>http://www.bing.com/th?id=allowed</News:Image></item>'
        f'<item><title>Rejected</title><link>{bad.replace("&","&amp;")}</link>'
        '<News:Image>http://www.bing.com/th?id=rejected</News:Image></item></channel></rss>').encode()
  def fetch(url):
   if 'gamepro.de/news/rss' in url: raise OSError('publisher blocks automated RSS')
   if 'bing.com/news/search' in url: return bing
   raise AssertionError(url)
  with tempfile.TemporaryDirectory() as directory,patch.dict(module.SOURCES,{'DE':[('GamePro','gamepro.de','https://www.gamepro.de/news/rss/news.rss')]}):
   items=HomeNews(directory,fetch=fetch).get('de')['items']
  self.assertEqual(len(items),1)
  self.assertEqual(items[0]['url'],'https://www.gamepro.de/artikel/review,1.html')
  self.assertEqual(items[0]['image'],'https://www.bing.com/th?id=allowed')
if __name__=='__main__':unittest.main()

class NewsCardImageTests(unittest.TestCase):
 """The reported defect: cards from GameSpot, GameStar, Famitsu, 4Gamer, Inven and the
 rest showed only a logo. Two causes, both pinned here."""

 def test_syndicated_card_takes_its_image_from_the_publisher_not_googles_redirector(self):
  from unittest.mock import patch
  import quick_settings.home_news as module
  # A publisher with no direct feed: items arrive through Google News, whose links
  # point at news.google.com and whose page carries no OpenGraph image at all.
  google=('<rss><channel><item><title>A review</title>'
    '<link>https://news.google.com/rss/articles/CBMiOGh0dHBz</link>'
    '<description>&lt;a href="https://gamespot.com/articles/a-review/1100-6400000/"&gt;A review&lt;/a&gt;'
    '&lt;font&gt;GameSpot&lt;/font&gt;</description>'
    '<source url="https://gamespot.com">GameSpot</source></item></channel></rss>').encode()
  asked=[]
  def fetch(url):
   asked.append(url)
   if 'news.google.com/rss/search' in url: return google
   raise AssertionError('no direct feed exists for this publisher')
  def page(url):
   asked.append(url)
   if url.startswith('https://news.google.com'):
    return b'<html><head><title>Redirecting</title></head></html>'
   return b'<html><head><meta property="og:image" content="https://cdn.example/lead.jpg"></head></html>'
  with tempfile.TemporaryDirectory() as directory, \
       patch.dict(module.SOURCES,{'US':[('GameSpot','gamespot.com','')]}):
   result=HomeNews(directory,fetch=fetch,page=page).get('en')
  self.assertEqual(len(result['items']),1)
  self.assertEqual(result['items'][0]['image'],'https://cdn.example/lead.jpg')
  self.assertIn('https://gamespot.com/articles/a-review/1100-6400000/',asked)
  self.assertNotIn('https://news.google.com/rss/articles/CBMiOGh0dHBz',asked)

 def test_real_google_shape_is_resolved_before_open_graph_enrichment(self):
  from unittest.mock import patch
  import quick_settings.home_news as module
  google=('<rss><channel><item><title>A review - GamePro</title>'
    '<link>https://news.google.com/rss/articles/opaque-current-token</link>'
    '<description>&lt;a href="https://news.google.com/rss/articles/opaque-current-token"&gt;A review&lt;/a&gt;'
    '&lt;font&gt;GamePro&lt;/font&gt;</description>'
    '<source url="https://www.gamepro.de">GamePro</source></item></channel></rss>').encode()
  pages=[]
  with tempfile.TemporaryDirectory() as directory, \
       patch.dict(module.SOURCES,{'DE':[('GamePro','gamepro.de','')]}), \
       patch.object(module.HomeNews,'_publisher_article_url',return_value='https://www.gamepro.de/artikel/review,1.html'):
   result=HomeNews(directory,fetch=lambda _:google,
      page=lambda url:(pages.append(url),b'<meta property="og:image" content="https://img.gamepro.de/hero.jpg">')[1]).get('de')
  self.assertEqual(result['items'][0]['image'],'https://img.gamepro.de/hero.jpg')
  self.assertEqual(pages,['https://www.gamepro.de/artikel/review,1.html'])

 def test_article_pages_are_requested_as_html(self):
  """Asking an article for application/rss+xml is what publishers answered 403 to."""
  import quick_settings.home_news as module
  captured={}
  class FakeResponse:
   headers=type('H',(),{'get_content_type':staticmethod(lambda:'text/html')})()
   def read(self,_size): return b'<html></html>'
   def __enter__(self): return self
   def __exit__(self,*_): return False
  def urlopen(request,timeout=None):
   captured['accept']=request.get_header('Accept')
   captured['agent']=request.get_header('User-agent')
   return FakeResponse()
  original=module.urllib.request.urlopen
  module.urllib.request.urlopen=urlopen
  try: module.HomeNews._fetch_page('https://famitsu.com/news/1')
  finally: module.urllib.request.urlopen=original
  self.assertIn('text/html',captured['accept'])
  self.assertNotIn('rss',captured['accept'])
  self.assertNotIn('RSS Reader',captured['agent'])

 def test_a_publishers_own_feed_never_borrows_a_link_from_its_description(self):
  """In a real feed the description can link anywhere; only a syndicated item
  hides its publisher behind a redirector."""
  from unittest.mock import patch
  import quick_settings.home_news as module
  feed=('<rss><channel><item><title>A preview</title>'
   '<link>https://gamekult.com/jeux/a-preview.html</link>'
   '<description>&lt;a href="https://gamekult.com/totally-other-article.html"&gt;see also&lt;/a&gt;</description>'
   '</item></channel></rss>').encode()
  pages=[]
  with tempfile.TemporaryDirectory() as directory, \
       patch.dict(module.SOURCES,{'FR':[('Gamekult','gamekult.com','https://gamekult.com/feed.xml')]}):
   result=HomeNews(directory,fetch=lambda _:feed,
                   page=lambda url:(pages.append(url), b'<html></html>')[1]).get('fr')
  self.assertEqual(result['items'][0]['article_url'],'')
  self.assertEqual(pages,['https://gamekult.com/jeux/a-preview.html'])

class GameSpotImageResolution(unittest.TestCase):
 def test_wordpress_feed_thumbnails_ask_for_a_card_sized_rendition(self):
  from quick_settings.home_news import upgrade_image_url
  self.assertEqual(upgrade_image_url('https://www.gamespot.com/wp-content/uploads/2026/09/destiny.jpg?w=300'),'https://www.gamespot.com/wp-content/uploads/2026/09/destiny.jpg?w=1600')
  self.assertEqual(upgrade_image_url('https://site.com/wp-content/uploads/a.jpg?resize=300%2C169&ssl=1'),'https://site.com/wp-content/uploads/a.jpg?ssl=1&w=1600')
  self.assertEqual(upgrade_image_url('https://example.com/cover.jpg?w=300'),'https://example.com/cover.jpg?w=300')
  self.assertEqual(upgrade_image_url('https://site.com/wp-content/uploads/a.jpg'),'https://site.com/wp-content/uploads/a.jpg')
  self.assertEqual(upgrade_image_url(''),'')
 def test_gamespot_feed_item_uses_the_larger_rendition(self):
  data=b'<rss><channel><item><title>T</title><link>https://www.gamespot.com/articles/t/</link><media:content url="https://www.gamespot.com/wp-content/uploads/2026/09/x.jpg?w=300" type="image/jpeg" width="300" height="169"/></item></channel></rss>'
  rows=parse_feed(data,'GameSpot','https://www.gamespot.com/feeds/mashup/')
  self.assertEqual(rows[0]['image'],'https://www.gamespot.com/wp-content/uploads/2026/09/x.jpg?w=1600')

