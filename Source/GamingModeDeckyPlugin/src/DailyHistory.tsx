import { SP_REACT as React, DFL } from './decky';
import { HistoryConfirmHint, historyActionCopy, openHistoryImage } from './historyInteractions';
import { call } from './controlBackend';
import { historyImages } from './historyImages';
import { orderHistoryImages, historyHeadingCover } from './historyMedia';
const { Focusable, DialogButton, Navigation } = DFL as any;
const labels: Record<string,string[]> = {
 it:['Accade oggi','La storia dei videogiochi, un giorno alla volta','In questo giorno','','Dietro la storia','Continua su Wikipedia','Immagine: crediti e licenza','Prepariamo la storia di oggi…','Storia non disponibile. Riprova','Ultima storia disponibile','Fonte: Wikipedia · testo CC BY-SA 4.0'],
 en:['On this day','Video game history, one day at a time','On this day','Today’s discovery','Behind the story','Read on Wikipedia','Image: credits and license','Finding today’s story…','Story unavailable. Retry','Last available story','Source: Wikipedia · text CC BY-SA 4.0'],
 de:['Heute in der Geschichte','Videospielgeschichte, Tag für Tag','An diesem Tag','Die heutige Entdeckung','Hinter der Geschichte','Auf Wikipedia weiterlesen','Bild: Quelle und Lizenz','Die heutige Geschichte wird geladen…','Nicht verfügbar. Erneut versuchen','Letzte verfügbare Geschichte','Quelle: Wikipedia · Text CC BY-SA 4.0'],
 es:['Tal día como hoy','La historia de los videojuegos, día a día','Tal día como hoy','El descubrimiento de hoy','Detrás de la historia','Leer en Wikipedia','Imagen: créditos y licencia','Buscando la historia de hoy…','No disponible. Reintentar','Última historia disponible','Fuente: Wikipedia · texto CC BY-SA 4.0'],
 fr:['Ce jour-là','L’histoire du jeu vidéo, jour après jour','Ce jour-là','La découverte du jour','Derrière l’histoire','Lire sur Wikipédia','Image : crédits et licence','Recherche de l’histoire du jour…','Indisponible. Réessayer','Dernière histoire disponible','Source : Wikipédia · texte CC BY-SA 4.0'],
 pt:['Neste dia','A história dos videogames, um dia de cada vez','Neste dia','A descoberta de hoje','Por trás da história','Ler na Wikipédia','Imagem: créditos e licença','Buscando a história de hoje…','Indisponível. Tentar novamente','Última história disponível','Fonte: Wikipédia · texto CC BY-SA 4.0'],
 ru:['В этот день','История видеоигр день за днём','В этот день','Открытие дня','За кулисами истории','Читать в Википедии','Изображение: авторство и лицензия','Загружаем историю дня…','Недоступно. Повторить','Последняя доступная история','Источник: Википедия · текст CC BY-SA 4.0'],
 uk:['Цього дня','Історія відеоігор день за днем','Цього дня','Відкриття дня','За лаштунками історії','Читати у Вікіпедії','Зображення: авторство та ліцензія','Завантажуємо історію дня…','Недоступно. Спробувати знову','Остання доступна історія','Джерело: Вікіпедія · текст CC BY-SA 4.0'],
 ja:['今日は何の日','一日ずつたどるゲームの歴史','この日に','今日の発見','物語の背景','ウィキペディアで読む','画像の出典とライセンス','今日の物語を読み込み中…','再試行','最後に取得した物語','出典：ウィキペディア · CC BY-SA 4.0'],
 ko:['오늘의 역사','하루씩 만나는 비디오 게임의 역사','이날의 역사','오늘의 발견','이야기 속 인물과 회사','위키백과에서 읽기','이미지 출처 및 라이선스','오늘의 이야기를 불러오는 중…','다시 시도','마지막으로 불러온 이야기','출처: 위키백과 · CC BY-SA 4.0'],
 zh:['歷史上的今天','每天探索一段電子遊戲歷史','歷史上的今天','今日發現','故事背後','在維基百科閱讀','圖片來源與授權','正在尋找今天的故事…','重試','最近的故事','來源：維基百科 · CC BY-SA 4.0'],
 hi:['आज के दिन','वीडियो गेम का इतिहास, हर दिन एक कहानी','आज के दिन','आज की खोज','कहानी के पीछे','विकिपीडिया पर पढ़ें','चित्र: श्रेय और लाइसेंस','आज की कहानी खोज रहे हैं…','फिर कोशिश करें','पिछली उपलब्ध कहानी','स्रोत: विकिपीडिया · CC BY-SA 4.0'],
};

const words=(locale:string)=>labels[locale.split(/[-_]/)[0]]??labels.en;
export const historyTitle=(locale:string)=>words(locale)[0];
const birth:Record<string,string>={it:'In questo giorno nasce',en:'Born on this day',de:'An diesem Tag geboren',es:'Tal día como hoy nace',fr:'Naissance ce jour-là',pt:'Neste dia nasce',ru:'В этот день родились',uk:'Цього дня народилися',ja:'この日に誕生',ko:'이날 태어난 인물',zh:'在這一天誕生',hi:'इस दिन जन्म हुआ'};
const foundation:Record<string,string>={it:'In questo giorno viene fondata',en:'Founded on this day',de:'An diesem Tag gegründet',es:'Tal día como hoy se funda',fr:'Fondation ce jour-là',pt:'Neste dia é fundada',ru:'В этот день основана',uk:'Цього дня заснована',ja:'この日に設立',ko:'이날 설립',zh:'在這一天成立',hi:'इस दिन स्थापना हुई'};
const release:Record<string,string>={it:'In questo giorno esce',en:'Released on this day',de:'An diesem Tag erschienen',es:'Tal día como hoy se publica',fr:'Sortie ce jour-là',pt:'Neste dia é lançado',ru:'В этот день вышла',uk:'Цього дня вийшла',ja:'この日に発売',ko:'이날 출시',zh:'在這一天推出',hi:'इस दिन जारी हुआ'};
// A date-linked entry already explains why it is present. Generic calendar
// essays have their own editorial title, so no redundant “feature of the day”
// banner is shown above them.
const feature:Record<string,string>={it:'',en:'',de:'',es:'',fr:'',pt:'',ru:'',uk:'',ja:'',ko:'',zh:'',hi:''};
const commemoration:Record<string,string>={it:'In occasione di',en:'On the occasion of',de:'Anlässlich',es:'Con motivo de',fr:"À l’occasion de",pt:'Por ocasião de',ru:'По случаю',uk:'З нагоди',ja:'この日に寄せて',ko:'이 날을 기념하며',zh:'值此之际',hi:'इस अवसर पर'};
const previewWords:Record<string,string[]>={it:['Giorno precedente','Giorno successivo'],en:['Previous day','Next day'],de:['Vorheriger Tag','Nächster Tag'],es:['Día anterior','Día siguiente'],fr:['Jour précédent','Jour suivant'],pt:['Dia anterior','Dia seguinte'],ru:['Предыдущий день','Следующий день'],uk:['Попередній день','Наступний день'],ja:['前の日','次の日'],ko:['이전 날','다음 날'],zh:['前一天','後一天'],hi:['पिछला दिन','अगला दिन']};
type Chapter={title:string;body:string;image_role?:string;image_file?:string;image_subject?:string;kicker?:string;subject?:string};
type Story={date:string;year?:number;article:{title:string};editorial:{id:string;title:string;intro:string;kind?:string;media_id?:string;cover_image_file?:string;intro_image_file?:string;intro_image_role?:string;topic?:string;chapters?:Chapter[]};occasion?:{kind?:string;region?:string;headline?:string};relationship?:string;related_to?:string;quote?:{text:string;attribution:string;after?:number;quoted?:boolean};preview?:boolean;also?:Story[];connections?:Story[]};
const css=`
.ph-history{color:#f0edd8;padding:0 0 26px;max-width:1480px;margin:auto;font-family:inherit}
 .ph-history-head{display:grid;grid-template-columns:auto minmax(0,1fr) auto;align-items:center;gap:14px;padding:20px 28px;min-height:84px;margin:16px 5px 24px;background:rgba(12,15,19,.76);border:1px solid rgba(240,237,216,.12);border-radius:5px;box-sizing:border-box}
 .ph-history-day-card{cursor:pointer;outline:2px solid transparent;outline-offset:3px;transition:background .2s,outline-color .2s}.ph-history-day-card:hover,.ph-history-day-card.gpfocus,.ph-history-day-card:focus{background:rgba(28,31,37,.86);outline-color:#f0c75a}
.ph-history-heading-media{position:relative;isolation:isolate;width:68px;height:86px;flex:0 0 68px;display:flex;align-items:center;justify-content:center;background:#0a1114;border-radius:3px;overflow:hidden;padding:4px;box-sizing:border-box;--ph-history-blur:10px}.ph-history-heading-media .ph-history-image-fg{position:relative;z-index:1;width:100%;height:100%;object-fit:contain;display:block}
.ph-history-date{font-family:inherit;font-size:clamp(22px,2.2vw,30px);font-weight:300;text-transform:capitalize}
 .ph-history-sub{font-size:12px;color:#f0c75a;text-align:right;justify-self:end;width:100%;max-width:none;line-height:1.35}.ph-history-sub strong{display:block;text-align:right;font-size:clamp(20px,2.3vw,30px);line-height:1.08;font-weight:400;color:#f0edd8;margin-top:3px}.ph-history-date{white-space:nowrap}
.ph-history-card{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.12fr);gap:32px;align-items:center;padding:24px;background:#29271f;border-radius:5px;margin:0 5px 24px;outline:2px solid transparent;outline-offset:3px;scroll-margin:30px;transition:background .2s,outline-color .2s}
.ph-history-card:hover,.ph-history-card.gpfocus,.ph-history-card:focus{background:#302d22;outline-color:#f0c75a}
.ph-history-card.reverse .ph-history-image{grid-column:1;grid-row:1}.ph-history-card.reverse .ph-history-copy{grid-column:2;grid-row:1}
.ph-history-copy{align-self:center;min-width:0}.ph-history-eyebrow{font-size:11px;letter-spacing:.13em;text-transform:uppercase;color:#f0c75a;line-height:1.7;margin-bottom:18px}.ph-history-subject{display:block;color:#f0c75a;font-size:10px;letter-spacing:.16em;margin-bottom:3px}
.ph-history h2{font-family:inherit;font-size:clamp(32px,3.7vw,52px);line-height:1.12;font-weight:400;letter-spacing:-.045em;margin:0 0 24px;color:#f0edd8}
.ph-history h2 .ph-history-accent{color:#f0c75a;font-style:normal}
.ph-history h3{font-family:inherit;font-size:clamp(26px,3vw,38px);line-height:1.14;font-weight:400;letter-spacing:-.025em;margin:0 0 18px;color:#f0edd8}
.ph-history p{font-size:16px;line-height:1.75;margin:0;color:#d1cfb8}
.ph-history-quote{margin:8px 5px 24px;padding:30px 8%;text-align:center;background:#29271f;border:1px solid #f0c75a66;border-radius:5px;outline:2px solid transparent;outline-offset:3px;transition:background .2s,outline-color .2s;cursor:pointer}.ph-history-quote:hover,.ph-history-quote.gpfocus,.ph-history-quote:focus{background:#302d22;outline-color:#f0c75a}.ph-history-quote p{font-size:clamp(24px,3vw,42px);line-height:1.25;color:#f0edd8;margin:0}.ph-history-quote span{display:block;margin-top:14px;color:#f0c75a;font-size:11px;letter-spacing:.14em;text-transform:uppercase}
.ph-history-image{position:relative;isolation:isolate;min-width:0;align-self:center;display:flex;align-items:center;justify-content:center;overflow:hidden;background:#0a1114;border-radius:3px;height:320px;padding:16px;box-sizing:border-box;--ph-history-blur:28px}
.ph-history-image-bg{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;display:block;z-index:0;pointer-events:none;transform:scale(1.2);filter:blur(var(--ph-history-blur,28px)) brightness(.4) saturate(1.1)!important}
.ph-history-image-fg{display:block;position:relative;z-index:1;max-width:100%;width:100%;height:100%;object-fit:contain;filter:none!important}
.ph-history-hero .ph-history-image{height:360px}.ph-history-year{font-size:12px;color:#a1aaa8;margin-top:22px;letter-spacing:.06em}
.ph-history-card.no-image{grid-template-columns:1fr}.ph-history-card.no-image .ph-history-copy{grid-column:1;grid-row:auto}
.ph-history-story+.ph-history-story{margin-top:54px}.ph-history-connections-title{font-size:20px;font-weight:400;margin:30px 5px 20px}.ph-history-empty{min-height:200px;display:flex;align-items:center;justify-content:center}
@media(max-width:900px){.ph-history-image{padding:12px;--ph-history-blur:20px}.ph-history-card{padding:20px;gap:24px}.ph-history h2{font-size:42px}.ph-history h3{font-size:28px}.ph-history p{font-size:14px;line-height:1.7}.ph-history-image{height:280px}.ph-history-hero .ph-history-image{height:310px}.ph-history-sub{font-size:12px;max-width:240px}}
.ph-history-related-summary.ph-history-card{min-height:0;padding:20px 24px;grid-template-columns:minmax(0,1fr) 30%;gap:24px}.ph-history-related-summary .ph-history-image{height:148px;min-height:0}.ph-history-related-summary h3{font-size:clamp(24px,2.6vw,38px);margin:10px 0 16px}.ph-history-confirm-hint{display:inline-flex;align-items:center;gap:6px;font-size:14px;color:#d5d0bd}.ph-history-confirm-hint img,.ph-history-confirm-hint svg{height:22px!important;width:auto}.ph-history-related-content{margin-top:20px}.ph-history-related+.ph-history-related{margin-top:20px}
@media(max-width:550px){.ph-history-card{grid-template-columns:1fr}.ph-history-card.reverse .ph-history-image,.ph-history-card.reverse .ph-history-copy{grid-column:auto;grid-row:auto}.ph-history-sub{max-width:180px}}
@media(prefers-reduced-motion:reduce){.ph-history *{transition:none!important}}
`;
const normalizeGameTitle=(value:unknown)=>String(value??'').normalize('NFKD').replace(/[\u0300-\u036f]/g,'').toLowerCase().replace(/[™®©]/g,'').replace(/\b(edition|deluxe|complete|goty|game of the year|remastered|remake)\b/g,'').replace(/[^a-z0-9]+/g,' ').trim().replace(/\s+/g,' ');
const plausibleAppId=(value:unknown)=>{const id=Number(value);return Number.isSafeInteger(id)&&id>0&&id<=0xffffffff?id:null;};
const libraryEntries=()=>{
 const store=(window as any)?.appStore??(window as any)?.SteamClient?.Apps;
 const values:any[]=[];
 const add=(value:any)=>{if(!value)return;if(value instanceof Map){value.forEach((entry,key)=>add({...entry,appid:entry?.appid??entry?.app_id??key}));return;}if(Array.isArray(value)){value.forEach(add);return;}values.push(value);};
 add(store?.allApps);add(store?.m_mapAppOverview);
 return values;
};
const libraryAppIdForStory=(story:Story)=>{
 const kind=String(story.editorial.kind??'');
 if(!/^(game|release(?:_|$)|uscita(?:_|$)|prima_versione_pubblica|rilascio_shareware|accesso_anticipato)/i.test(kind))return null;
 const wanted=[story.article.title,story.editorial.title].map(normalizeGameTitle).filter(value=>value.length>=3);
 if(!wanted.length)return null;
 const candidates=libraryEntries().map(entry=>({id:plausibleAppId(entry?.appid??entry?.app_id??entry?.unAppID??entry?.nAppID),name:normalizeGameTitle(entry?.display_name??entry?.localized_name??entry?.name??entry?.title)})).filter(item=>item.id&&item.name);
 for(const target of wanted){const exact=candidates.find(item=>item.name===target);if(exact)return exact.id;}
 for(const target of wanted){if(target.length<6)continue;const partial=candidates.find(item=>item.name.length>=6&&(item.name.includes(target)||target.includes(item.name)));if(partial)return partial.id;}
 return null;
};
function Block({story,chapter,index,locale}:{story:Story;chapter?:Chapter;index:number;locale:string}){
 const mediaThemeId=story.editorial.media_id??story.editorial.id;
 const all=historyImages[mediaThemeId]??[];
 const [failed,setFailed]=React.useState<string[]>([]);
 const ordered=orderHistoryImages(all,chapter,index,story.editorial.chapters,{image_file:story.editorial.intro_image_file,image_role:story.editorial.intro_image_role});
 const media=ordered.find(image=>!failed.includes(image.url));
 const lang=locale.split(/[-_]/)[0];
 const occasion=story.occasion?.kind==='birth'?(birth[lang]??birth.en):story.occasion?.kind==='foundation'?(foundation[lang]??foundation.en):'';
 const title=chapter?.title??story.editorial.title;
 const renderTitle=()=>{if(chapter)return <h3>{title}</h3>;const parts=title.split(/\s+/);const last=parts.pop()??'';return <h2>{parts.join(' ')}{parts.length?' ':''}<span className="ph-history-accent">{last}</span></h2>;};
 const lastActivation=React.useRef(0);
 const cardRef=React.useRef<HTMLDivElement>(null);
 const open=()=>{const now=Date.now();if(now-lastActivation.current<350)return;lastActivation.current=now;if(!media)return;openHistoryImage(media.url,media.subject,locale,cardRef.current?.ownerDocument.defaultView??window);};
 return <Focusable ref={cardRef} className={`ph-history-card ${index%2?'reverse':''} ${media?'':'no-image'}`} role={media?'button':'article'} aria-label={title} focusable={true} onOKActionDescription={media?historyActionCopy(locale)[3]:undefined} onActivate={open} onClick={open}>
 <div className="ph-history-copy"><div className="ph-history-eyebrow">{chapter?<span className="ph-history-subject">{chapter.subject||story.article.title}</span>:story.relationship??(occasion?`${occasion} · ${story.article.title}`:story.article.title)}</div>{renderTitle()}<p>{chapter?.body??story.editorial.intro}</p>{!chapter&&story.year&&<div className="ph-history-year">{story.year}</div>}</div>
 {media&&<div className="ph-history-image"><img className="ph-history-image-bg" src={media.url} alt="" aria-hidden={true} role="presentation"/><img className="ph-history-image-fg" src={media.url} alt={media.subject} onError={()=>setFailed(previous=>[...previous,media.url])}/></div>}
 </Focusable>;
}
const expandedRelatedStories=new Set<string>();
function RelatedStory({story,locale}:{story:Story;locale:string}) {
 const storyKey=`${story.date}-${story.editorial.id}`;
 const [expanded,setExpanded]=React.useState(()=>expandedRelatedStories.has(storyKey));
 const lastActivation=React.useRef(0);
 const all=historyImages[story.editorial.media_id??story.editorial.id]??[];
 const media=orderHistoryImages(all,undefined,0,story.editorial.chapters,{image_file:story.editorial.intro_image_file,image_role:story.editorial.intro_image_role})[0];
 const toggle=()=>{const now=Date.now();if(now-lastActivation.current<350)return;lastActivation.current=now;setExpanded(value=>{if(value)expandedRelatedStories.delete(storyKey);else expandedRelatedStories.add(storyKey);return !value;});};
 return <div className="ph-history-related">
  <Focusable className="ph-history-card ph-history-related-summary" role="button" aria-expanded={expanded} focusable={true}
    onActivate={toggle} onClick={toggle} onOKActionDescription={historyActionCopy(locale)[expanded?2:1]}>
   <div className="ph-history-copy"><div className="ph-history-eyebrow">{story.relationship??words(locale)[4]}</div>
    <h3>{story.article.title}</h3><HistoryConfirmHint locale={locale} expanded={expanded}/></div>
   {media&&<div className="ph-history-image"><img className="ph-history-image-fg" src={media.url} alt={media.subject}/></div>}
  </Focusable>
  {expanded&&<div className="ph-history-related-content"><Block story={story} index={0} locale={locale}/>
   {story.editorial.chapters?.map((chapter,index)=><React.Fragment key={index}>
    <Block story={story} chapter={chapter} index={index+1} locale={locale}/>
    {story.quote&&(story.quote.after??1)===index+1&&<Focusable className="ph-history-quote" role="note" focusable={true}>
     <p>{story.quote.quoted?`«${story.quote.text}»`:story.quote.text}</p>{story.quote.attribution&&<span>{story.quote.attribution}</span>}
    </Focusable>}
   </React.Fragment>)}</div>}
 </div>;
}
let historyPreviewOffset=0;
export function DailyHistory({getLocale}:{getLocale:()=>string}){
 const locale=getLocale(),copy=words(locale),lang=locale.split(/[-_]/)[0];
 const [offset,setOffsetState]=React.useState(historyPreviewOffset),[story,setStory]=React.useState<Story>(),[failed,setFailed]=React.useState(false),[retry,setRetry]=React.useState(0);
 const setOffset=(update:(value:number)=>number)=>setOffsetState(value=>{historyPreviewOffset=update(value);return historyPreviewOffset;});
 const busy=React.useRef(false),root=React.useRef<HTMLDivElement>(null);
 React.useEffect(()=>{let alive=true;let running=false;const refresh=async()=>{if(running)return;running=true;busy.current=true;try{const next=await call<[string,number],Story>('get_daily_history',locale,offset);if(!next?.editorial)throw Error('Missing editorial');if(alive){setStory(next);setFailed(false);}}catch{if(alive)setFailed(true);}finally{running=false;if(alive)busy.current=false;}};void refresh();const timer=window.setInterval(()=>void refresh(),60000);return()=>{alive=false;window.clearInterval(timer);busy.current=false;};},[locale,retry,offset]);
 const date=story?new Date(`${story.date}T12:00:00`):new Date(),buttons=(DFL as any).GamepadButton;
 const hints=previewWords[lang]??previewWords.en;
 const headingFor=(item:Story)=>item.occasion?.headline|| (item.occasion?.kind==='birth'?(birth[lang]??birth.en):item.occasion?.kind==='foundation'?(foundation[lang]??foundation.en):item.occasion?.kind==='release'?(release[lang]??release.en):item.occasion?.kind==='commemoration'?(commemoration[lang]??commemoration.en):item.occasion?.kind==='feature'?(feature[lang]??feature.en):copy[3]);
 const heading=story?headingFor(story):'';
 const headingMedia=(item:Story)=>{const mediaThemeId=item.editorial.media_id??item.editorial.id;const media=historyHeadingCover(historyImages[mediaThemeId]??[],item.editorial.kind,item.editorial.cover_image_file);return media?<div className="ph-history-heading-media"><img className="ph-history-image-bg" src={media.url} alt="" aria-hidden={true} role="presentation"/><img className="ph-history-image-fg" src={media.url} alt={media.subject}/></div>:null;};
 const navigate=(event:any)=>{const b=event.detail?.button;if(story?.preview&&(b===buttons.SECONDARY||b===buttons.OPTIONS)){event.preventDefault?.();event.stopPropagation?.();if(!busy.current){busy.current=true;setOffset(v=>Math.max(-366,Math.min(366,v+(b===buttons.OPTIONS?1:-1))));}return;}
 if(![buttons.DIR_LEFT,buttons.DIR_RIGHT].includes(b))return;const doc=event.currentTarget?.ownerDocument??document;const selector='.ph-history-card,.ph-history-quote';const blocks=Array.from(root.current?.querySelectorAll<HTMLElement>(selector)??[]);const active=doc.activeElement?.closest(selector);const index=blocks.indexOf(active as HTMLElement);const next=blocks[index+(b===buttons.DIR_RIGHT?1:-1)];if(index>=0&&next){event.preventDefault?.();event.stopPropagation?.();next.focus();}};
 const [,refreshLibrary]=React.useState(0);
 React.useEffect(()=>{let ticks=0;const timer=window.setInterval(()=>{refreshLibrary(value=>value+1);if(++ticks>=10)window.clearInterval(timer);},1000);return()=>window.clearInterval(timer);},[story?.date,story?.editorial.id]);
 const dayAppId=story?libraryAppIdForStory(story):null;
 const openDayGame=()=>{if(!dayAppId)return;try{Navigation?.Navigate?.(`/library/app/${dayAppId}`);}catch{}};
 const headingContent=<><div className="ph-history-date">{new Intl.DateTimeFormat(locale,{day:'numeric',month:'long'}).format(date)}</div>{story?<><div className="ph-history-sub">{heading&&<span>{heading}</span>}<strong>{story.article.title}</strong>{story.year&&story.occasion?.kind!=='feature'&&<span className="ph-history-year">{story.year}</span>}</div>{headingMedia(story)}</>:<div className="ph-history-sub">{copy[1]}</div>}</>;
 return <Focusable className="ph-history" ref={root} flow-children="column" onButtonDown={navigate} actionDescriptionMap={story?.preview?{[buttons.SECONDARY]:hints[0],[buttons.OPTIONS]:hints[1]}:{}}><style>{css}</style>
 <Focusable className="ph-history-head ph-history-day-card" role="button" aria-label={story?.article.title??copy[0]} focusable={true} onActivate={dayAppId?openDayGame:undefined} onClick={dayAppId?openDayGame:undefined}>{headingContent}</Focusable>
 {!story?<div className="ph-history-empty">{failed?<DialogButton onClick={()=>setRetry(v=>v+1)}>{copy[8]}</DialogButton>:<span role="status">{copy[7]}</span>}</div>:[story,...(story.also??[])].map(item=><div className="ph-history-story" key={`${item.date}-${item.editorial.id}`}><Block story={item} index={0} locale={locale}/>{item.editorial.chapters?.map((chapter,index)=><React.Fragment key={index}><Block story={item} chapter={chapter} index={index+1} locale={locale}/>{item.quote&&(item.quote.after??1)===index+1&&<Focusable className="ph-history-quote" role="note" focusable={true}><p>{item.quote.quoted?`«${item.quote.text}»`:item.quote.text}</p>{item.quote.attribution&&<span>{item.quote.attribution}</span>}</Focusable>}</React.Fragment>)}{item.quote&&!item.editorial.chapters?.length&&<Focusable className="ph-history-quote" role="note" focusable={true}><p>{item.quote.quoted?`«${item.quote.text}»`:item.quote.text}</p>{item.quote.attribution&&<span>{item.quote.attribution}</span>}</Focusable>}{item.connections?.length?<><div className="ph-history-connections-title">{copy[4]}</div>{item.connections.map(related=><RelatedStory key={`${related.date}-${related.editorial.id}`} story={related} locale={locale}/>)}</>:null}</div>)}
 </Focusable>;
}
let historyConfig = { enabled: true, revision: 0 };
let historyReady = false;
const listeners = new Set<() => void>();
const subscribe = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener); }; };
const notify = () => listeners.forEach(listener => listener());
export const notifyHistoryLanguageChanged = () => { historyConfig = { ...historyConfig, revision: historyConfig.revision + 1 }; notify(); };
async function loadHistorySettings() { const next = await call<[], {enabled:boolean}>("get_daily_history_settings"); historyConfig = { ...next, revision: historyConfig.revision + 1 }; historyReady = true; notify(); }
export function HistoryTabs({native,getLocale}:{native:any;getLocale:()=>string}) {
 const config = React.useSyncExternalStore(subscribe,()=>historyConfig);
 React.useEffect(()=>{if(!historyReady)void loadHistorySettings().catch(error=>console.warn('[Playhub History] settings unavailable',error));},[]);
 if(!config.enabled)return native;
 return React.cloneElement(native, {tabs:native.props.tabs.map((tab:any)=>tab.id==='Recommended'?{...tab,title:historyTitle(getLocale()),content:<DailyHistory getLocale={getLocale}/>} :tab)});
}
export function HomeHistorySettings({locale}:{locale:string}) {
 const config = React.useSyncExternalStore(subscribe,()=>historyConfig);
 const [busy,setBusy]=React.useState(false);const [failed,setFailed]=React.useState(false);
 React.useEffect(()=>{if(!historyReady)void loadHistorySettings().catch(()=>setFailed(true));},[]);
 const save=async(enabled:boolean)=>{if(busy)return;setBusy(true);try{const next=await call<[boolean],{enabled:boolean}>('set_daily_history_settings',enabled);historyConfig={...next,revision:config.revision+1};notify();setFailed(false);}catch{setFailed(true);}finally{setBusy(false);}};
 const Toggle=(DFL as any).ToggleField;
 return <><Toggle label={historyTitle(locale)} description={words(locale)[1]} checked={config.enabled} disabled={!historyReady||busy} onChange={(enabled:boolean)=>void save(enabled)} bottomSeparator="none"/>{failed&&<DialogButton onClick={()=>void loadHistorySettings().then(()=>setFailed(false)).catch(()=>{})}>{words(locale)[8]}</DialogButton>}</>;
}

