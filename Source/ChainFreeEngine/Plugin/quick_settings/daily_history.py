"""Daily Wikimedia editorial selection; optional and isolated from device controls."""
import datetime
import hashlib
import html
import json
import os
import re
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
if __package__:
    from .history_imports import load_catalog
else:
    # Decky loads this file directly via spec_from_file_location, without
    # adding quick_settings to sys.path or assigning a package.
    import importlib.util
    _imports_spec = importlib.util.spec_from_file_location(
        'playhub_history_imports', os.path.join(os.path.dirname(__file__), 'history_imports.py'))
    _imports_module = importlib.util.module_from_spec(_imports_spec)
    _imports_spec.loader.exec_module(_imports_module)
    load_catalog = _imports_module.load_catalog

LANGUAGES = {'en','it','de','es','fr','pt','ru','uk','ja','ko','zh','hi'}
# The current calendar has no approved Japanese, Korean, Chinese or Hindi
# editorial copy. Those interfaces keep their localized controls, while the
# Accade oggi content deliberately uses the English source text.
ENGLISH_EDITORIAL_FALLBACK = {'ja','ko','zh','hi'}
RELATED_THEMES = {
    'mario':['miyamoto','nintendo'], 'mario64':['miyamoto','nintendo'], 'zelda':['miyamoto','kondo'],
    'metalgear':['kojima'], 'sonic':['naka','sega'], 'dreamcast':['sega'],
    'nintendo':['mario','zelda','mario64'], 'miyamoto':['mario','zelda','mario64'],
    'kondo':['zelda','mario'], 'kojima':['metalgear'], 'naka':['sonic'],
    'sega':['sonic','dreamcast'], 'christmas':['nintendo'], 'newyear':['nintendo'],
}
RELATED_LABELS = {
    ('mario','miyamoto'):'Creatore di Mario', ('mario64','miyamoto'):'Creatore di Mario', ('zelda','miyamoto'):'Co-creatore della serie',
    ('mario','nintendo'):'La casa che ha pubblicato Mario', ('mario64','nintendo'):'La casa che ha pubblicato Mario',
    ('metalgear','kojima'):'Regista e autore della serie', ('sonic','naka'):'Programmatore e co-creatore', ('sonic','sega'):'La casa di Sonic',
    ('dreamcast','sega'):'La console e lo studio che l\'ha creata', ('nintendo','mario'):'Una delle serie che ha definito Nintendo',
    ('nintendo','zelda'):'Una delle serie che ha definito Nintendo', ('nintendo','mario64'):'Una delle serie che ha definito Nintendo',
    ('miyamoto','mario'):'Opera fondamentale', ('miyamoto','zelda'):'Opera fondamentale', ('miyamoto','mario64'):'Opera fondamentale',
    ('kondo','zelda'):'Una delle sue colonne sonore più celebri', ('kondo','mario'):'Una delle sue colonne sonore più celebri',
    ('kojima','metalgear'):'Opera fondamentale', ('naka','sonic'):'Opera fondamentale', ('sega','sonic'):'Serie simbolo',
    ('sega','dreamcast'):'Una console della sua storia', ('christmas','nintendo'):'Un capitolo della storia dell\'azienda',
    ('newyear','nintendo'):'Un capitolo della storia dell\'azienda'
}
EDITORIAL_QUOTES = {
    # These are editorial pauses, not quotations. Attribution stays empty so the
    # interface never presents Playhub as a person or invents a speaker.
    'mario': ('Un salto può insegnare il linguaggio di un mondo.', ''),
    'zelda': ('Una mappa comincia a vivere quando scegliamo una deviazione.', ''),
    'doom': ('Anche il ritmo di un corridoio racconta una storia.', ''),
    'crash': ('I limiti della macchina diventano parte del progetto.', ''),
    'metalgear': ('La regia cambia quando lo spettatore può rispondere.', ''),
    'kondo': ('Una melodia può continuare oltre il bordo dello schermo.', ''),
    'kojima': ('Ogni sistema racconta qualcosa quando ci costringe a scegliere.', ''),
    'miyamoto': ('Un mondo diventa memorabile quando impariamo a leggerlo giocando.', ''),
    'nfs': ('La notte ha il suo suono.', ''),
}
EDITORIAL_QUOTE_AFTER = {
    'mario': 2, 'zelda': 2, 'doom': 1, 'crash': 1, 'metalgear': 1,
    'kondo': 2, 'kojima': 2, 'miyamoto': 2, 'nfs': 1,
}
EDITORIAL_INTERLUDE_TRANSLATIONS = {
    'mario': {'en':'A jump can teach the language of a world.', 'de':'Ein Sprung kann die Sprache einer Welt lehren.', 'es':'Un salto puede enseñar el lenguaje de un mundo.', 'fr':"Un saut peut enseigner le langage d'un monde.", 'pt':'Um salto pode ensinar a linguagem de um mundo.', 'ru':'Прыжок может научить языку целого мира.', 'uk':'Стрибок може навчити мови цілого світу.', 'ja':'一度のジャンプが、世界の言葉を教えてくれる。', 'ko':'한 번의 점프가 세계의 언어를 가르쳐 준다.', 'zh':'一次跳跃，也能教会我们一个世界的语言。', 'hi':'एक छलाँग किसी दुनिया की भाषा सिखा सकती है।'},
    'zelda': {'en':'A map comes alive when we choose a detour.', 'de':'Eine Karte wird lebendig, wenn wir einen Umweg wählen.', 'es':'Un mapa cobra vida cuando elegimos un desvío.', 'fr':"Une carte prend vie quand nous choisissons un détour.", 'pt':'Um mapa ganha vida quando escolhemos um desvio.', 'ru':'Карта оживает, когда мы выбираем обходной путь.', 'uk':'Мапа оживає, коли ми обираємо обхідний шлях.', 'ja':'寄り道を選ぶとき、地図は生き始める。', 'ko':'돌아가는 길을 고를 때 지도는 살아난다.', 'zh':'当我们选择绕路，地图才开始鲜活起来。', 'hi':'जब हम दूसरा रास्ता चुनते हैं, नक्शा जीवंत हो उठता है।'},
    'doom': {'en':'Even the rhythm of a corridor tells a story.', 'de':'Auch der Rhythmus eines Korridors erzählt eine Geschichte.', 'es':'Hasta el ritmo de un pasillo cuenta una historia.', 'fr':"Même le rythme d'un couloir raconte une histoire.", 'pt':'Até o ritmo de um corredor conta uma história.', 'ru':'Даже ритм коридора рассказывает историю.', 'uk':'Навіть ритм коридору розповідає історію.', 'ja':'廊下のリズムさえ、物語を語る。', 'ko':'복도의 리듬조차 이야기를 들려준다.', 'zh':'连走廊的节奏，也在讲述故事。', 'hi':'गलियारे की लय भी एक कहानी कहती है।'},
    'crash': {'en':'The limits of the machine become part of the design.', 'de':'Die Grenzen der Maschine werden Teil des Entwurfs.', 'es':'Los límites de la máquina forman parte del diseño.', 'fr':'Les limites de la machine deviennent une matière de conception.', 'pt':'Os limites da máquina passam a fazer parte do projeto.', 'ru':'Ограничения машины становятся частью замысла.', 'uk':'Обмеження машини стають частиною задуму.', 'ja':'機械の限界が、設計の一部になる。', 'ko':'기계의 한계가 설계의 일부가 된다.', 'zh':'机器的限制，也成为设计的一部分。', 'hi':'मशीन की सीमाएँ डिज़ाइन का हिस्सा बन जाती हैं।'},
    'metalgear': {'en':'Direction changes when the audience can answer.', 'de':'Regie verändert sich, wenn das Publikum antworten kann.', 'es':'La dirección cambia cuando el espectador puede responder.', 'fr':'La mise en scène change quand le spectateur peut répondre.', 'pt':'A direção muda quando o espectador pode responder.', 'ru':'Режиссура меняется, когда зритель может ответить.', 'uk':'Режисура змінюється, коли глядач може відповісти.', 'ja':'観客が応えられるとき、演出は変わる。', 'ko':'관객이 응답할 수 있을 때 연출은 달라진다.', 'zh':'当观众能够回应，导演的手法也随之改变。', 'hi':'जब दर्शक जवाब दे सकता है, निर्देशन बदल जाता है।'},
    'kondo': {'en':'A melody can continue beyond the edge of the screen.', 'de':'Eine Melodie kann über den Bildschirmrand hinaus weiterklingen.', 'es':'Una melodía puede continuar más allá del borde de la pantalla.', 'fr':"Une mélodie peut se prolonger au-delà du bord de l'écran.", 'pt':'Uma melodia pode continuar além da borda da tela.', 'ru':'Мелодия может продолжаться за краем экрана.', 'uk':'Мелодія може тривати за краєм екрана.', 'ja':'旋律は画面の端を越えて続いていく。', 'ko':'선율은 화면의 가장자리를 넘어 이어진다.', 'zh':'一段旋律，可以延续到屏幕之外。', 'hi':'धुन स्क्रीन के किनारे से आगे भी चल सकती है।'},
    'kojima': {'en':'Every system tells a story when it forces us to choose.', 'de':'Jedes System erzählt eine Geschichte, wenn es uns zu einer Entscheidung zwingt.', 'es':'Todo sistema cuenta una historia cuando nos obliga a elegir.', 'fr':'Chaque système raconte une histoire lorsqu’il nous oblige à choisir.', 'pt':'Todo sistema conta uma história quando nos obriga a escolher.', 'ru':'Любая система рассказывает историю, когда заставляет нас выбирать.', 'uk':'Кожна система розповідає історію, коли змушує нас обирати.', 'ja':'選択を迫るとき、あらゆるシステムは物語を語り始める。', 'ko':'선택을 요구하는 순간, 모든 시스템은 이야기를 들려준다.', 'zh':'当一个系统迫使我们选择时，它便开始讲述故事。', 'hi':'हर प्रणाली तब कहानी कहती है जब वह हमें चुनने पर मजबूर करती है।'},
    'miyamoto': {'en':'A world becomes memorable when we learn to read it through play.', 'de':'Eine Welt bleibt im Gedächtnis, wenn wir spielend lernen, sie zu lesen.', 'es':'Un mundo se vuelve memorable cuando aprendemos a leerlo jugando.', 'fr':'Un monde devient mémorable lorsque nous apprenons à le lire en jouant.', 'pt':'Um mundo se torna memorável quando aprendemos a lê-lo jogando.', 'ru':'Мир запоминается, когда мы учимся читать его в игре.', 'uk':'Світ запам’ятовується, коли ми вчимося читати його у грі.', 'ja':'遊びながら読み方を学ぶとき、世界は記憶に残る。', 'ko':'플레이하며 읽는 법을 배울 때 세계는 기억에 남는다.', 'zh':'当我们在游玩中学会阅读一个世界，它便会留在记忆里。', 'hi':'जब हम खेलते हुए किसी दुनिया को पढ़ना सीखते हैं, वह यादगार बन जाती है।'},
    'nfs': {'en':'The night has its own sound.', 'de':'Die Nacht hat ihren eigenen Klang.', 'es':'La noche tiene su propio sonido.', 'fr':'La nuit a son propre son.', 'pt':'A noite tem seu próprio som.', 'ru':'У ночи свой звук.', 'uk':'Ніч має власний звук.', 'ja':'夜には、夜だけの音がある。', 'ko':'밤에는 밤만의 소리가 있다.', 'zh':'夜晚有自己的声音。', 'hi':'रात की अपनी आवाज़ है।'},
}

# Every day of the year is a written record in history_days.json: its own subject,
# its own Wikipedia article and its own text. Nothing here is composed by pairing
# lists, and a day that carries no verified anniversary uses kind "feature"
# instead of inventing a birthday or a release date.
_DAYS_PATH = os.path.join(os.path.dirname(__file__), 'history_days.json')
_days_cache = {'mtime': None, 'data': {}}
_days_lock = threading.RLock()


def load_days():
    """Read (and cache) the day table. A missing or broken file must not take the tab down."""
    with _days_lock:
        try:
            stamp = os.stat(_DAYS_PATH).st_mtime
        except OSError:
            return {}
        if _days_cache['mtime'] != stamp:
            try:
                with open(_DAYS_PATH, encoding='utf-8-sig') as handle:
                    data = json.load(handle).get('days', {})
                _days_cache['data'] = data if isinstance(data, dict) else {}
            except (OSError, ValueError, AttributeError):
                _days_cache['data'] = {}
            _days_cache['mtime'] = stamp
        return _days_cache['data']


def _localised(value, fallback=''):
    """Italian and English are written per day; the rest fall back to English."""
    if not isinstance(value, dict):
        return {language: str(value or fallback) for language in LANGUAGES}
    english = value.get('en') or value.get('it') or fallback
    return {language: value.get(language) or english for language in LANGUAGES}


def display_subject(theme):
    """Return the human-facing subject, without Wikimedia disambiguation text."""
    topic = theme.get('subject') or theme.get('topic') or ''
    # Wikipedia topics use trailing parentheses for technical disambiguation
    # ("(console)", "(1989 video game)", "(company)"). They are useful for
    # resolving the page, but they are noise in the editorial header.
    return re.sub(r'\s+\([^()]*\)\s*$', '', str(topic)).strip()


def daily_feature(day):
    """The record for a date with no catalog anniversary of its own.

    Subject, article and images come from one entry, so the title, the Wikipedia page
    behind the card and the pictures always describe the same thing.
    """
    entry = load_days().get(day.strftime('%m-%d'))
    if not entry:
        # Packaging or data errors must stay visible. Reusing a generic essay
        # would silently bring back the repeated-content defect.
        raise RuntimeError('Missing editorial record for ' + day.strftime('%m-%d'))
    kind = entry.get('kind', 'feature')
    record = {
        'id': f'daily-{day.isoformat()}',
        'kind': kind,
        'topic': entry.get('topic', ''),
        'subject': entry.get('subject', ''),
        'subject_localized': _localised(entry.get('subject_localized'), entry.get('subject', '')),
        'occasion_headline': _localised(entry.get('occasion_headline')),
        'media_id': entry.get('media_id') or f"day-{day.strftime('%m-%d')}",
        'display_title': (entry.get('title') or {}).get('it') or entry.get('subject', ''),
        'title': _localised(entry.get('title'), entry.get('subject', '')),
        'intro': _localised(entry.get('intro')),
        'chapters': [],
    }
    if entry.get('year'):
        record['year'] = int(entry['year'])
    for chapter in entry.get('chapters', []):
        record['chapters'].append({
            'kicker': _localised(chapter.get('kicker')),
            'title': _localised(chapter.get('title')),
            'body': _localised(chapter.get('body')),
            'image_role': chapter.get('image_role', 'game'),
            'image_file': chapter.get('image_file', ''),
            # The subject binds the picture: a chapter is illustrated by its own
            # subject or by nothing at all.
            'subject': chapter.get('subject') or entry.get('subject', ''),
            'subject_localized': _localised(chapter.get('subject_localized'), chapter.get('subject') or entry.get('subject', '')),
        })
    return record


def request_json(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'Playhub/1.4 (Wikipedia gaming history reader)', 'Accept': 'application/json'})
    with urllib.request.urlopen(request, timeout=8) as response:
        data = response.read(4 * 1024 * 1024 + 1)
    if len(data) > 4 * 1024 * 1024: raise ValueError('Wikimedia response too large')
    if urllib.parse.urlsplit(url).hostname == 'www.igdb.com':
        # The public page's own cover only; related-game thumbnails are not candidates.
        for tag in re.findall(r'<meta\b[^>]{0,4096}>', data.decode('utf8', errors='replace'), re.I):
            attrs = {key.lower():html.unescape(value) for key, _, value in re.findall(r'([\w:-]+)\s*=\s*([\x22\x27])(.*?)\2', tag)}
            if attrs.get('property') == 'og:image': return {'image':attrs.get('content','')}
        return {}
    return json.loads(data)


def media_url(url, provider):
    if not isinstance(url,str): return ''
    if url.startswith('//'): url='https:'+url
    parsed=urllib.parse.urlsplit(url)
    if parsed.scheme!='https' or parsed.username or parsed.password: return ''
    host=parsed.hostname or ''
    allowed = host=='images.igdb.com' if provider=='IGDB' else any(host==domain or host.endswith('.'+domain) for domain in ('steamstatic.com','steamusercontent.com'))
    return url if allowed else ''


def external_id(entity, prop):
    for claim in entity.get('claims',{}).get(prop,[]):
        if claim.get('rank')=='deprecated': continue
        value=claim.get('mainsnak',{}).get('datavalue',{}).get('value')
        if isinstance(value,str): return value
    return ''

def select_daily(items, day):
    if not items: return None
    index = int(hashlib.sha256(day.isoformat().encode()).hexdigest()[:12], 16) % len(items)
    return items[index]

def gaming_page(page):
    description = page.get('description', '').lower()
    return any(word in description for word in ('video game', 'videogame', 'game designer', 'game developer', 'gaming company'))

def select_editorial(catalog,day):
    themes=catalog.get('themes',[])
    by_id={t['id']:t for t in themes}
    day_key=day.strftime('%m-%d')
    anniversaries=[a for a in catalog.get('anniversaries',[]) if a.get('kind') in ('birth','release','foundation','commemoration') and a.get('date')==day_key and
                   ((a.get('kind')=='commemoration' and not a.get('year')) or
                    (isinstance(a.get('year'), int) and a['year']<day.year))]
    launches=[e for e in catalog.get('events',[]) if e.get('date')==day.isoformat()]
    special=catalog.get('calendar',{}).get(day_key)
    if launches:
        selected=launches[0]
        marker={**selected, 'kind': selected.get('kind', 'release'), 'year': selected.get('year', day.year)}
        return by_id.get(selected.get('theme_id')),marker,anniversaries
    if special and special in by_id:
        # Calendar observances get a date-scoped record. This prevents a shared
        # catalog theme (for example “education”) from becoming the same story
        # when another release happens to use that theme elsewhere in the year.
        theme={**by_id[special], 'id':f'calendar-{day_key}-{special}', 'media_id':by_id[special].get('media_id',special),
               'display_title':f"{by_id[special].get('title',{}).get('it','')} · {day.day:02d}/{day.month:02d}"}
        return theme,{'kind':'commemoration','region':'WORLD'},anniversaries
    if anniversaries:
        selected=anniversaries[0]
        theme=by_id.get(selected.get('theme_id'))
        if theme:
            theme={**theme,**{key:selected[key] for key in ('title','intro') if selected.get(key)}}
        return theme,selected,anniversaries
    # No date link was verified: use a deterministic, unique cultural essay.
    # This must never fall back to a creator or game from another date.
    record = daily_feature(day)
    marker = {'kind': record['kind']}
    if any(record.get('occasion_headline', {}).values()):
        marker['headline'] = record['occasion_headline']
    if record.get('year'):
        marker['year'] = record['year']
    return record, marker, anniversaries

def localize_theme(theme,language):
    if not theme: return None
    def translated(value):
        return value.get(language) or value.get('en') or value.get('it','') if isinstance(value,dict) else str(value or '')
    return {'id':theme['id'],'title':translated(theme.get('title')),'intro':translated(theme.get('intro')),'sources':theme.get('sources',[]),'kind':theme.get('kind','history'),'media_id':theme.get('media_id',theme.get('id')),'topic':theme.get('topic',''),'cover_image_file':theme.get('cover_image_file',''),'intro_image_file':theme.get('intro_image_file',''),'intro_image_role':theme.get('intro_image_role','')}

class DailyHistory:
    def __init__(self, directory, fetch=request_json, today=datetime.date.today):
        self.directory = directory
        self.fetch = fetch
        self.today = today
        self.lock = threading.RLock()
        self.cache = {}
        self.retry_after = {}

    def cached_request(self, url):
        host=urllib.parse.urlsplit(url).hostname
        path = os.path.join(self.directory, 'history-metadata', hashlib.sha256(url.encode()).hexdigest() + '.json')
        cached = None
        try:
            with open(path, encoding='utf8') as file: cached = json.load(file)
            if time.time() - cached['saved'] < 7 * 86400: return cached['data']
        except (OSError, ValueError, KeyError, TypeError): cached = None
        if time.time() < self.retry_after.get(host,0):
            if cached: return cached['data']
            raise RuntimeError('Wikimedia temporarily unavailable')
        try:
            data = self.fetch(url)
        except Exception as error:
            if isinstance(error, urllib.error.HTTPError) and error.code in (429, 503):
                self.retry_after[host] = time.time() + 1800
            if cached: return cached['data']
            raise
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path + '.tmp', 'w', encoding='utf8') as file:
                json.dump({'saved': time.time(), 'data': data}, file, ensure_ascii=False)
            os.replace(path + '.tmp', path)
        except OSError: pass
        return data

    def settings(self):
        try:
            with open(os.path.join(self.directory,'daily-history-settings.json'),encoding='utf8') as file:
                value=json.load(file).get('enabled',True)
                return {'enabled':value if type(value) is bool else True}
        except (OSError,ValueError,AttributeError): return {'enabled':True}

    def save(self,enabled):
        if type(enabled) is not bool: raise ValueError('Invalid history setting')
        with self.lock:
            os.makedirs(self.directory,exist_ok=True)
            path=os.path.join(self.directory,'daily-history-settings.json')
            with open(path+'.tmp','w',encoding='utf8') as file: json.dump({'enabled':enabled},file)
            os.replace(path+'.tmp',path)
        return {'enabled':enabled}

    def wikipedia_url(self, theme_id, locale="en"):
        language = str(locale).lower().split('-')[0].split('_')[0]
        if language not in LANGUAGES: language = 'en'
        catalog = load_catalog(os.path.dirname(__file__), language)
        theme = next((item for item in catalog.get('themes', []) if item['id'] == theme_id), None)
        if not theme and str(theme_id).startswith('daily-'):
            try: theme = daily_feature(datetime.date.fromisoformat(str(theme_id)[6:]))
            except ValueError: theme = None
        if not theme and str(theme_id).startswith('calendar-'):
            source_id=str(theme_id).rsplit('-', 1)[-1]
            theme=next((item for item in catalog.get('themes', []) if item['id']==source_id), None)
        if not theme: raise ValueError('Unknown editorial')
        topic = theme['topic']
        if language != 'en':
            original = self.summary(topic, 'en')
            entity = self.entity(original.get('wikibase_item'))
            title = entity.get('sitelinks', {}).get(language + 'wiki', {}).get('title')
            if not title: raise ValueError('Wikipedia article unavailable in selected language')
        else: title = topic
        return 'https://' + language + '.wikipedia.org/wiki/' + urllib.parse.quote(title.replace(' ', '_'), safe='')

    def summary(self, title, language='en'):
        return self.cached_request('https://' + language + '.wikipedia.org/api/rest_v1/page/summary/' + urllib.parse.quote(title.replace(' ', '_'), safe=''))

    def entity(self, qid):
        if not isinstance(qid,str) or not qid.startswith('Q') or not qid[1:].isdigit(): return {}
        return self.cached_request('https://www.wikidata.org/wiki/Special:EntityData/' + qid + '.json').get('entities', {}).get(qid, {})

    def article(self, original, language):
        try: entity = self.entity(original.get('wikibase_item'))
        except Exception: entity = {}
        data = original
        title = entity.get('sitelinks', {}).get(language + 'wiki', {}).get('title')
        actual_language = 'en'
        if language != 'en' and title:
            try: data = self.summary(title, language); actual_language = language
            except Exception: pass
        # Plain text only. Wikipedia retains the full article and revision history.
        image = data.get('originalimage', {}).get('source') or data.get('thumbnail', {}).get('source') or ''
        url = data.get('content_urls', {}).get('desktop', {}).get('page', '')
        if not url.startswith('https://'): raise ValueError('Missing Wikipedia source')
        extract = data.get('extract', '')
        words = extract.split()
        excerpt = ' '.join(words[:180]) + ('…' if len(words)>180 else '')
        images=[]
        if image.startswith('https://'):
            filename=urllib.parse.urlsplit(image).path.split('/')[-1]
            if '/thumb/' in image: filename=urllib.parse.urlsplit(image).path.split('/')[-2]
            images.append({'url':image,'provider':'Wikimedia','kind':'reference',
                           'sourceUrl':'https://'+actual_language+'.wikipedia.org/wiki/File:'+filename})
        steam_id=external_id(entity,'P1733')
        if steam_id.isascii() and steam_id.isdigit():
            try:
                details=self.cached_request('https://store.steampowered.com/api/appdetails?appids='+steam_id+'&filters=basic,screenshots').get(steam_id,{})
                if details.get('success'):
                    game=details.get('data',{})
                    candidates=[(s.get('path_full',''),'screenshot') for s in game.get('screenshots',[])[:3]]
                    candidates.append((game.get('header_image',''),'cover'))
                    for candidate,kind in candidates:
                        valid=media_url(candidate,'Steam')
                        if valid: images.append({'url':valid,'provider':'Steam','kind':kind,'sourceUrl':'https://store.steampowered.com/app/'+steam_id+'/'})
            except Exception: pass
        slug=external_id(entity,'P5794')
        if len(images)<2 and re.fullmatch(r'[a-z0-9][a-z0-9~$-]{0,160}',slug):
            try:
                page='https://www.igdb.com/games/'+slug
                candidate=media_url(self.cached_request(page).get('image',''),'IGDB')
                if candidate: images.append({'url':candidate,'provider':'IGDB','kind':'cover','sourceUrl':page})
            except Exception: pass
        return {'title':data.get('title','').replace('_',' '), 'description':data.get('description',''), 'excerpt':excerpt, 'url':url, 'image':images[0]['url'] if images else '', 'images':images, 'language':actual_language}, entity

    def get(self, locale='en', offset=0):
        if not self.settings()['enabled']: return {'disabled':True}
        language = str(locale).lower().split('-')[0].split('_')[0]
        if language not in LANGUAGES: language = 'en'
        content_language = 'en' if language in ENGLISH_EDITORIAL_FALLBACK else language
        # Day navigation is part of the public Accade oggi experience. Keep the
        # compatibility field true so the frontend exposes Previous/Next day
        # commands even while the tab itself is focused in Big Picture.
        preview = True
        if type(offset) is not int or abs(offset)>366: raise ValueError('Invalid preview day')
        day = self.today() + datetime.timedelta(days=offset)
        try:
            catalog = load_catalog(os.path.dirname(__file__), content_language)
        except (OSError,ValueError): catalog={}
        edition=hashlib.sha256(json.dumps({'catalog':catalog,'days':load_days()},sort_keys=True).encode()).hexdigest()[:12]
        theme, occasion, anniversaries = select_editorial(catalog, day)
        if not theme: raise RuntimeError('Editorial catalog unavailable')
        def story_for(selected, marker):
            topic=selected.get('topic','')
            editorial=localize_theme(selected,content_language)
            def chapter_text(value):
                return (value.get(content_language) or value.get('en') or value.get('it', '')) if isinstance(value, dict) else str(value or '')
            editorial['chapters']=[{'title':chapter_text(chapter.get('title',{})),
                                    'kicker':chapter.get('kicker',{}).get(content_language,chapter.get('kicker',{}).get('en','')) if isinstance(chapter.get('kicker'),dict) else str(chapter.get('kicker','')),
                                    'body':chapter_text(chapter.get('body',{})),
                                    'image_role':chapter.get('image_role','game'),
                                    'image_file':chapter.get('image_file',''),
                                    'image_subject':str(chapter.get('image_subject') or chapter.get('subject','') or ''),
                                    # A chapter that names its subject may only be illustrated by
                                    # images of that subject: this is what stops a later chapter
                                    # from borrowing the opening game's screenshots.
                                    'subject':(chapter.get('subject_localized',{}).get(content_language) or chapter.get('subject_localized',{}).get('en') or str(chapter.get('subject','') or ''))}
                                   for chapter in selected.get('chapters',[])]
            quote=EDITORIAL_QUOTES.get(selected.get('id'))
            if quote:
                quote = (EDITORIAL_INTERLUDE_TRANSLATIONS.get(selected['id'], {}).get(content_language, quote[0]), quote[1])
            subject=(selected.get('subject_localized',{}).get(content_language) or selected.get('subject_localized',{}).get('en') or display_subject(selected))
            localized_marker=dict(marker or {})
            if isinstance(localized_marker.get('headline'),dict):
                localized_marker['headline']=localized_marker['headline'].get(content_language) or localized_marker['headline'].get('en','')
            return {'date':day.isoformat(),'anniversary':bool(marker and marker.get('year')),
                    'year':marker.get('year') if marker else None,'event':'','eventLanguage':content_language,
                    'article':{'title':subject,'description':'','excerpt':'','image':'','language':language,
                               'url':'https://en.wikipedia.org/wiki/'+urllib.parse.quote(topic.replace(' ','_'),safe='')},
                    'related':None,'stale':False,'preview':preview,'edition':edition,'editorial':editorial,
                    'quote':({'text':quote[0],'attribution':quote[1],'quoted':bool(quote[1]),
                              'after':EDITORIAL_QUOTE_AFTER.get(selected.get('id'), 1)} if quote else None),
                    'occasion':localized_marker}
        result=story_for(theme,occasion)
        by_id={item['id']:item for item in catalog.get('themes',[])}
        extras=[]
        for anniversary in anniversaries:
            selected=by_id.get(anniversary.get('theme_id'))
            if not selected or selected['id']==theme['id']: continue
            selected={**selected,**{key:anniversary[key] for key in ('title','intro') if anniversary.get(key)}}
            extras.append(story_for(selected,anniversary))
        result['also']=extras
        shown={item['editorial']['id'] for item in [result]+extras}
        for item in [result]+extras:
            item['connections']=[]
            if by_id.get(item['editorial']['id'], {}).get('curated_edition'):
                continue
            for related in RELATED_THEMES.get(item['editorial']['id'],[]):
                if related in by_id and related not in shown:
                    linked=story_for(by_id[related],None)
                    linked['relationship']=RELATED_LABELS.get((item['editorial']['id'],related),'Collegamento editoriale')
                    if linked['relationship'] == 'Opera fondamentale':
                        linked['relationship'] += f" · {linked['article']['title']}"
                    linked['related_to']=item['article']['title']
                    item['connections'].append(linked)
                    shown.add(related)
        return result
