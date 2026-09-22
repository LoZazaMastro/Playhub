from pathlib import Path
p=Path('quick_settings/daily_history.py');s=p.read_text(encoding='utf8');anchor='    def summary(self, title, language=\'en\'):'
method='''    def wikipedia_url(self, theme_id, locale="en"):
        language = str(locale).lower().split('-')[0].split('_')[0]
        if language not in LANGUAGES: language = 'en'
        with open(os.path.join(os.path.dirname(__file__), 'history_editorial.json'), encoding='utf-8-sig') as file:
            catalog = json.load(file)
        theme = next((item for item in catalog.get('themes', []) if item['id'] == theme_id), None)
        if not theme: raise ValueError('Unknown editorial')
        topic = theme['topic']
        if language != 'en':
            original = self.summary(topic, 'en')
            entity = self.entity(original.get('wikibase_item'))
            title = entity.get('sitelinks', {}).get(language + 'wiki', {}).get('title')
            if not title: raise ValueError('Wikipedia article unavailable in selected language')
        else: title = topic
        return 'https://' + language + '.wikipedia.org/wiki/' + urllib.parse.quote(title.replace(' ', '_'), safe='')

'''
assert anchor in s;s=s.replace(anchor,method+anchor);p.write_text(s,encoding='utf8')
p=Path('main.py');s=p.read_text(encoding='utf8');anchor='    async def get_daily_history(self, locale="en", offset=0):';s=s.replace(anchor,'''    async def get_history_wikipedia_url(self, theme_id, locale="en"):
        if self._daily_history is None: raise RuntimeError("Daily history unavailable")
        return await asyncio.to_thread(self._daily_history.wikipedia_url, theme_id, locale)

'''+anchor);p.write_text(s,encoding='utf8')
p=Path('src/DailyHistory.tsx');s=p.read_text(encoding='utf8');s=s.replace("SP_REACT as React, DFL }","SP_REACT as React, DFL, toaster }")
s=s.replace(" const title=chapter?.title??story.editorial.title;",''' const title=chapter?.title??story.editorial.title;
 const opening=React.useRef(false);
 const open=async()=>{if(opening.current)return;opening.current=true;try{const url=await call<[string,string],string>('get_history_wikipedia_url',story.editorial.id,locale);if(new URL(url).hostname!==`${lang}.wikipedia.org`)throw Error('Invalid article language');DFL.Navigation.NavigateToExternalWeb(url);}catch{toaster.toast({title:words(locale)[0],body:words(locale)[8]});}finally{opening.current=false;}};''')
s=s.replace('role="group" aria-label={title} focusable={true}>','role="link" aria-label={title} focusable={true} onActivate={()=>void open()} onClick={()=>void open()}>')
# Give simultaneous anniversaries their own clear context, instead of suggesting a relation.
s=s.replace('.map(item=><div className="ph-history-story"', '.map((item,storyIndex)=><div className="ph-history-story"')
s=s.replace('key={`${item.date}-${item.editorial.id}`}><Block', 'key={`${item.date}-${item.editorial.id}`}>{storyIndex>0&&<header className="ph-history-head"><div className="ph-history-sub">{item.occasion?.kind===\'birth\'?(birth[lang]??birth.en):item.occasion?.kind===\'foundation\'?(foundation[lang]??foundation.en):item.occasion?.kind===\'release\'?(release[lang]??release.en):copy[3]}<strong>{item.article.title}</strong></div></header>}<Block')
p.write_text(s,encoding='utf8')
