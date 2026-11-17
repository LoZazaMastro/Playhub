// SPDX-License-Identifier: BSD-3-Clause
export const supportedLocales=["en","it","de","es","fr","pt","uk","zh","ja","ko","hi","ru"] as const;
export type SteamLocale=typeof supportedLocales[number];
const aliases:Record<string,SteamLocale>={english:"en",en:"en",italian:"it",it:"it",german:"de",de:"de",spanish:"es",latam:"es",es:"es",french:"fr",fr:"fr",portuguese:"pt",brazilian:"pt",pt:"pt",ukrainian:"uk",uk:"uk",schinese:"zh",tchinese:"zh",zh:"zh",japanese:"ja",ja:"ja",koreana:"ko",korean:"ko",ko:"ko",hindi:"hi",hi:"hi",russian:"ru",ru:"ru"};
export function normalizeSteamLocale(value:unknown):SteamLocale|null{
  if(typeof value!=="string")return null;const key=value.trim().toLowerCase().replace(/_/g,"-");return aliases[key]??aliases[key.split("-")[0]]??null;
}
function detect():SteamLocale{
  const win=typeof window!=="undefined"?window as any:{};const candidates:unknown[]=[];
  for(const manager of [win.LocalizationManager,win.g_LocalizationManager,win.SteamLocalizationManager]){
    if(!manager)continue;candidates.push(manager.m_strLanguage,manager.m_language,manager.m_strCurrentLanguage,manager.language);
    for(const name of ["GetLanguage","GetCurrentLanguage"]){try{const value=manager[name]?.();if(typeof value==="string")candidates.push(value);else if(value?.catch)value.catch(()=>{});}catch{}}
  }
  candidates.push(win.g_strLanguage,win.g_rgAppContextData?.language);
  // An explicit unsupported Steam language falls back to English, rather than
  // silently changing to a different browser or Windows language.
  const explicit=candidates.find(value=>typeof value==="string"&&value.trim());
  if(explicit!==undefined)return normalizeSteamLocale(explicit)??"en";
  const fallback=typeof document!=="undefined"&&document.documentElement?.lang||typeof navigator!=="undefined"&&navigator.language;
  return normalizeSteamLocale(fallback)??"en";
}
const listeners=new Set<()=>void>();let current:SteamLocale|null=null,request=0,users=0,observation=0;
let timer:ReturnType<typeof setInterval>|undefined;
export const getSteamLocale=():SteamLocale=>current??detect();
export const subscribeSteamLocale=(listener:()=>void)=>{listeners.add(listener);return()=>{listeners.delete(listener);};};
async function read():Promise<SteamLocale>{
  const win=typeof window!=="undefined"?window as any:{};const settings=win.SteamClient?.Settings;
  if(typeof settings?.GetCurrentLanguage!=="function")return detect();
  let timeout:ReturnType<typeof setTimeout>|undefined;
  try{const value=await Promise.race([Promise.resolve().then(()=>settings.GetCurrentLanguage()),new Promise<undefined>(resolve=>{timeout=setTimeout(()=>resolve(undefined),1500);})]);
    return typeof value==="string"&&value.trim()?normalizeSteamLocale(value)??"en":detect();
  }catch{return detect();}finally{if(timeout!==undefined)clearTimeout(timeout);}
}
export async function refreshSteamLocale(){
  const held=++request,value=await read();if(held!==request)return getSteamLocale();const old=getSteamLocale();current=value;if(old!==value)for(const listener of [...listeners])listener();return value;
}
export function startSteamLocaleObservation(){
  ++users;let stopped=false;
  if(users===1){const held=++observation;let busy=false;const poll=()=>{if(busy||held!==observation)return;busy=true;void refreshSteamLocale().finally(()=>{busy=false;});};poll();timer=setInterval(poll,1000);}
  return()=>{if(stopped)return;stopped=true;if(--users===0){++request;++observation;if(timer!==undefined)clearInterval(timer);timer=undefined;}};
}
