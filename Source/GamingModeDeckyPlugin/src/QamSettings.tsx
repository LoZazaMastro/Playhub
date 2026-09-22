import { DFL, SP_REACT as React } from './decky';
import { call } from './controlBackend';
import { QamShortcutsView, qamNativeLabel } from './QamShortcutsView';
import { getQamLayoutPreferences, getQamLayoutSnapshot, setQamLayoutPreferences, subscribeQamLayout, type QamLayoutPreferences } from './qamLayoutRuntime';

const text:Record<string,string[]>={
 it:['Generali','Personalizza le tab di Steam e i collegamenti ai plugin nel menu di accesso rapido.','Ordine e visibilità','Collegamenti ai plugin','Icona','Predefinita','Sposta su','Sposta giù','Salvataggio non riuscito. Riprova.','Riprova','La gestione QAM è in attesa della migrazione o dell’apertura del menu.'],
 en:['General','Customize Steam tabs and plugin shortcuts in the quick access menu.','Order and visibility','Plugin shortcuts','Icon','Default','Move up','Move down','Could not save. Retry.','Retry','QAM management is waiting for migration or for the menu to open.'],
 de:['Allgemein','Steam-Tabs und Plugin-Verknüpfungen im Schnellzugriffsmenü anpassen.','Reihenfolge und Sichtbarkeit','Plugin-Verknüpfungen','Symbol','Standard','Nach oben','Nach unten','Speichern fehlgeschlagen. Erneut versuchen.','Erneut versuchen','QAM wartet auf die Migration oder das Öffnen des Menüs.'],
 es:['General','Personaliza las pestañas de Steam y los accesos a plugins del menú rápido.','Orden y visibilidad','Accesos a plugins','Icono','Predeterminado','Subir','Bajar','No se pudo guardar. Reintenta.','Reintentar','QAM espera la migración o la apertura del menú.'],
 fr:['Général','Personnalisez les onglets Steam et les raccourcis des plugins du menu rapide.','Ordre et visibilité','Raccourcis des plugins','Icône','Par défaut','Monter','Descendre','Échec de l’enregistrement. Réessayez.','Réessayer','QAM attend la migration ou l’ouverture du menu.'],
 pt:['Geral','Personalize as abas Steam e os atalhos de plugins no menu rápido.','Ordem e visibilidade','Atalhos de plugins','Ícone','Padrão','Mover acima','Mover abaixo','Não foi possível salvar. Tente novamente.','Tentar novamente','QAM aguarda a migração ou a abertura do menu.'],
 ru:['Общие','Настройте вкладки Steam и ярлыки плагинов в меню быстрого доступа.','Порядок и видимость','Ярлыки плагинов','Значок','По умолчанию','Выше','Ниже','Не удалось сохранить. Повторите.','Повторить','QAM ожидает переноса или открытия меню.'],
 uk:['Загальні','Налаштуйте вкладки Steam і ярлики плагінів у меню швидкого доступу.','Порядок і видимість','Ярлики плагінів','Значок','Типовий','Вище','Нижче','Не вдалося зберегти. Повторіть.','Повторити','QAM очікує перенесення або відкриття меню.'],
 ja:['一般','クイックアクセスメニューのSteamタブとプラグインをカスタマイズします。','順序と表示','プラグインのショートカット','アイコン','標準','上へ','下へ','保存できませんでした。再試行してください。','再試行','QAMは移行またはメニューが開くのを待っています。'],
 ko:['일반','빠른 액세스 메뉴의 Steam 탭과 플러그인 바로가기를 설정합니다.','순서 및 표시','플러그인 바로가기','아이콘','기본값','위로','아래로','저장하지 못했습니다. 다시 시도하세요.','다시 시도','QAM이 이전 또는 메뉴 열기를 기다리고 있습니다.'],
 zh:['一般','自訂快速存取選單中的 Steam 分頁與外掛捷徑。','順序與顯示','外掛捷徑','圖示','預設','上移','下移','儲存失敗，請重試。','重試','QAM 正在等待移轉或開啟選單。'],
 hi:['सामान्य','त्वरित पहुँच मेनू में Steam टैब और प्लगइन शॉर्टकट अनुकूलित करें।','क्रम और दृश्यता','प्लगइन शॉर्टकट','चिह्न','डिफ़ॉल्ट','ऊपर ले जाएँ','नीचे ले जाएँ','सहेजा नहीं जा सका। फिर कोशिश करें।','फिर कोशिश करें','QAM स्थानांतरण या मेनू खुलने की प्रतीक्षा कर रहा है।'],
};
export const qamSettingsCopy=(locale:string)=>text[locale.split(/[-_]/)[0]]??text.en;
let hydration:Promise<void>|undefined;
let saving=Promise.resolve();
export function initializeQamPreferences():Promise<void> {
 if(!hydration)hydration=(async()=>{
  let saved=await call<[],QamLayoutPreferences&{exists?:boolean}>('get_qam_preferences');
  try {
   const cached=JSON.parse(localStorage.getItem('shortcuts:preferences:v1')??'null');
   const value=Array.isArray(cached)?{selected:cached}:cached;
   if(value&&(!saved.exists||Number(value.updatedAt??value.updated_at)>saved.updated_at)){
    saved=await call<[unknown],QamLayoutPreferences>('set_qam_preferences',{...value,updated_at:Number(value.updatedAt??value.updated_at)||0});
   }
  }catch { /* An invalid legacy cache must not override saved preferences. */ }
  setQamLayoutPreferences(saved);
 })().catch(error=>{hydration=undefined;throw error;});
 return hydration;
}

export function QamSettings({locale,children}:{locale:string;children?:React.ReactNode}) {
 const copy=qamSettingsCopy(locale),{Focusable,DialogButton,ToggleField,DropdownItem}=DFL as any;
 const prefs=React.useSyncExternalStore(subscribeQamLayout,getQamLayoutPreferences);
 const snapshot=React.useSyncExternalStore(subscribeQamLayout,getQamLayoutSnapshot);
 const [ready,setReady]=React.useState(false),[failed,setFailed]=React.useState(false);
 React.useEffect(()=>{let alive=true;void initializeQamPreferences().then(()=>{if(alive)setReady(true);}).catch(()=>{if(alive)setFailed(true);});return()=>{alive=false;};},[]);
 const save=(next:QamLayoutPreferences)=>{
  next={...next,updated_at:Date.now()};setQamLayoutPreferences(next);setFailed(false);
  saving=saving.catch(()=>{}).then(async()=>{await call('set_qam_preferences',next);}).catch(()=>{setFailed(true);});
 };
 const integratedNames=['Shortcuts','Quick Settings','Gaming Mode','Playhub Gaming Mode','Playhub'];
 const entries=snapshot.plugins.filter(p=>!integratedNames.includes(p.name)).map(p=>({...p,available:true,selected:prefs.selected.includes(p.name),iconChoice:prefs.icons[p.name]??'original'}));
 for(const name of prefs.selected)if(!entries.some(p=>p.name===name)&&!integratedNames.includes(name))entries.push({name,available:false,selected:true,iconChoice:prefs.icons[name]??'original'} as any);
 const tabs=[{key:'shortcut:Playhub',name:'Playhub',pluginName:'Playhub',kind:'decky',available:true,icon:snapshot.plugins.find(p=>p.name==='Playhub')?.icon},
 ...snapshot.native.map(n=>({...n,name:qamNativeLabel(n.key,n.label,locale),kind:n.key.startsWith('steam:')?'steam':'decky',available:true,hidden:prefs.hidden.includes(n.key)})),
 ...entries.filter(p=>p.selected&&p.name!=='Playhub').map(p=>({...p,key:`shortcut:${p.name}`,pluginName:p.name,kind:'shortcut'}))];
 tabs.sort((a,b)=>{const x=prefs.order.indexOf(a.key),y=prefs.order.indexOf(b.key);return(x<0?1e6:x)-(y<0?1e6:y);});
 const viewSnapshot={entries,tabs,selected:prefs.selected,fingerprint:JSON.stringify(prefs)};
 const current=React.useRef(viewSnapshot);current.current=viewSnapshot;
 const adapter=React.useMemo(()=>({subscribe:subscribeQamLayout,getSnapshot:()=>current.current,
  moveTab:(key:string,delta:number)=>{const p=getQamLayoutPreferences(),all=current.current.tabs;const index=all.findIndex(t=>t.key===key),other=all[index+delta];if(!other)return;
   const order=[...p.order];for(const t of all)if(!order.includes(t.key))order.push(t.key);const a=order.indexOf(key),b=order.indexOf(other.key);[order[a],order[b]]=[order[b],order[a]];save({...p,order});},
  toggleNativeTab:(key:string)=>{const p=getQamLayoutPreferences();save({...p,hidden:p.hidden.includes(key)?p.hidden.filter(k=>k!==key):[...p.hidden,key]});},
  add:(name:string)=>{const p=getQamLayoutPreferences();save({...p,selected:[...new Set([...p.selected,name])]});},
  remove:(name:string)=>{if(name==='Playhub')return;const p=getQamLayoutPreferences();save({...p,selected:p.selected.filter(n=>n!==name)});},
  setIcon:(name:string,id:string)=>{const p=getQamLayoutPreferences(),icons={...p.icons};if(id==='original')delete icons[name];else icons[name]=id;save({...p,icons});}
 }),[]);
 return <Focusable className="ph-qam-settings" flow-children="column" style={{width:"calc(100% + 32px)",maxWidth:"none",marginInline:-16,minWidth:0}}><div style={{paddingInline:16}}>{children}</div>
  {!snapshot.active&&<p role="status">{copy[10]}</p>}
  {failed&&<DialogButton onClick={()=>{void initializeQamPreferences().then(()=>{setReady(true);save(getQamLayoutPreferences());}).catch(()=>setFailed(true));}}>{copy[8]} {copy[9]}</DialogButton>}
  {ready&&<QamShortcutsView runtime={adapter} locale={locale}/>}
 </Focusable>;
}

