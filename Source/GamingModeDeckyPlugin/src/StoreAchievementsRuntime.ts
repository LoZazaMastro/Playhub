// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
// Original Steam adapters, moved from Playhub Metadata into Digital Stores.
import {DFL, toaster, routerHook} from './decky';
import {getSteamLocale} from './StoreDigitalSteamLocale';
import type {StoreKey,StoreOwnedGame} from './storeLibraryRuntime';

export type StoreAchievement={key:string;name:string;description:string;imageUnlocked:string|null;imageLocked:string|null;hidden:boolean;unlocked:boolean;unlockedAt:number|null;rarity:number|null};
export type StoreAchievementSnapshot={key:StoreKey;accountRevision:number;scope:string;title:string;observedAt:string;achievements:StoreAchievement[]};
type Dependencies={rows:()=>ReadonlyMap<number,StoreOwnedGame>;subscribe:(fn:()=>void)=>()=>void;request:<T>(method:string,args?:Record<string,unknown>)=>Promise<T>};
type NativeRow={strID:string;strName:string;strDescription:string;strImage:string;bHidden:boolean;bAchieved:boolean;rtUnlocked:number;flMinProgress:number;flMaxProgress:number;flCurrentProgress:number;flAchieved:number|null};
type Payload={scope:string;title:string;rows:StoreAchievement[];steam:{nTotal:number;nAchieved:number;vecAchievedHidden:NativeRow[];vecHighlight:NativeRow[];vecUnachieved:NativeRow[]}};
const runtimeKey=Symbol.for('playhub.store.achievements.v1');
const root=()=>globalThis as any;
export async function prepareStoreImportedAchievements(row:StoreOwnedGame,current:()=>boolean):Promise<void>{
 if(!current()||!supported(row))return;
 const prepare=root()[runtimeKey]?.imported;if(typeof prepare!=='function')return;
 let timer:ReturnType<typeof setTimeout>|undefined;
 try{await Promise.race([Promise.resolve(prepare(row,current)).catch(()=>{}),new Promise<void>(resolve=>{timer=setTimeout(resolve,8000);})]);}
 finally{if(timer!==undefined)clearTimeout(timer);}
}
const entries=(mod:any):any[]=>{const values=[];if(mod&&(typeof mod==='object'||typeof mod==='function'))for(const key of Object.keys(mod))try{values.push(mod[key]);}catch{}return values;};
const language=()=>(({en:'en-US',it:'it-IT',fr:'fr-FR',de:'de-DE',es:'es-ES',pt:'pt-PT',uk:'uk-UA',ru:'ru-RU',zh:'zh-CN',ja:'ja-JP',ko:'ko-KR',hi:'hi-IN'} as Record<string,string>)[getSteamLocale()]??'en-US');
const supported=(row:StoreOwnedGame|undefined)=>!!row&&['xbox','epic','gog'].includes(row.key.store)&&row.accountCurrent===true&&row.game?.isInstalled===true;
const icon=(url:string|null)=>{try{if(!url)return '';const u=new URL(url);return u.protocol==='https:'&&!u.username&&!u.password&&(!u.port||u.port==='443')?url:'';}catch{return '';}};
export function validStoreAchievementSnapshot(value:any,row:StoreOwnedGame,expected:string):value is StoreAchievementSnapshot{
 return value?.scope===expected&&value?.key?.store===row.key.store&&value.key.id===row.key.id&&value.accountRevision===row.game?.accountRevision
  &&typeof value.title==='string'&&value.title.length<=2048&&Array.isArray(value.achievements)&&value.achievements.length<=5000
  &&value.achievements.every((a:any)=>a&&typeof a.key==='string'&&a.key.length>0&&a.key.length<=512&&typeof a.name==='string'&&a.name.length<=2048
   &&typeof a.description==='string'&&a.description.length<=16384&&(a.imageUnlocked==null||typeof a.imageUnlocked==='string')&&(a.imageLocked==null||typeof a.imageLocked==='string')
   &&typeof a.hidden==='boolean'&&typeof a.unlocked==='boolean'&&(a.unlockedAt==null||Number.isFinite(a.unlockedAt)&&a.unlockedAt>=0)
   &&(a.rarity==null||Number.isFinite(a.rarity)&&a.rarity>=0&&a.rarity<=100))
  &&new Set(value.achievements.map((a:any)=>a.key)).size===value.achievements.length;
}
export function storeAchievementPayload(snapshot:StoreAchievementSnapshot):Payload{
 const rows=snapshot.achievements.slice().sort((a,b)=>a.key.localeCompare(b.key,'en'));
 const native=rows.map(a=>({strID:a.key,strName:a.name,strDescription:a.description,strImage:icon(a.unlocked?a.imageUnlocked:a.imageLocked)||icon(a.imageUnlocked),
  bHidden:a.hidden,bAchieved:a.unlocked,rtUnlocked:a.unlocked?Math.floor(a.unlockedAt??0):0,flMinProgress:0,flMaxProgress:1,flCurrentProgress:a.unlocked?1:0,flAchieved:a.rarity}));
 const unlocked=native.filter(a=>a.bAchieved);
 return{scope:snapshot.scope,title:snapshot.title,rows,steam:{nTotal:native.length,nAchieved:unlocked.length,vecAchievedHidden:unlocked.filter(a=>a.bHidden),vecHighlight:unlocked,vecUnachieved:native.filter(a=>!a.bAchieved)}};
}
export function storeAchievementQueryData(id:number,steamid:string,payload:Payload){
 const achievements=payload.rows.map((a,index)=>({internal_key:index+1,api_name:a.key,name:a.name,description:a.description,hidden:a.hidden,archived:false,groupid:0,
  icon_achieved:icon(a.imageUnlocked),icon_unachieved:icon(a.imageLocked)||icon(a.imageUnlocked),min_progress:0,max_progress:1}));
 const progress=Object.fromEntries(payload.rows.map((a,index)=>[index+1,{internal_key:index+1,unlocked:a.unlocked,unlock_time:a.unlocked?Math.floor(a.unlockedAt??0):undefined,progress:a.unlocked?1:0}]));
 const percentages=Object.fromEntries(payload.rows.flatMap((a,index)=>a.rarity==null?[]:[[index+1,a.rarity]]));
 return[
  {key:['achievements',id,'schema'],data:{appid:id,language:'',schema_hash:0,schema_version:1,groups:[{id:0,archived:false,developeronly:false,ispublic:true,dlcappid:0,order:-1,achievements}]}},
  {key:['achievements',id,'globalpercentages'],data:{percentages}},
  {key:['achievements',id,'user_achievements',steamid],data:{appid:id,steamid,schema_hash:0,achievements:progress,groups:{0:{groupid:0,is_achievable:true,completed_achievements:payload.steam.nAchieved,is_completed:payload.steam.nTotal>0&&payload.steam.nTotal===payload.steam.nAchieved}}}},
 ];
}
function queryClient(){return (DFL as any).findModuleChild((mod:any)=>entries(mod).find(v=>v&&typeof v.getQueryCache==='function'&&typeof v.getQueryData==='function'&&typeof v.setQueryData==='function'&&typeof v.cancelQueries==='function'&&typeof v.removeQueries==='function'));}
function steamId():string|null{return(DFL as any).findModuleChild((mod:any)=>entries(mod).find(v=>v?.logged_in===true&&typeof v.steamid==='string'&&/^7656119\d{10}$/.test(v.steamid)))?.steamid??null;}
function patch(target:any,name:string,replace:(self:any,original:(...args:any[])=>any,args:any[])=>any){
 if(typeof target?.[name]!=='function')return()=>{};
 const descriptor=Object.getOwnPropertyDescriptor(target,name),original=target[name];let active=true;
 const wrapped=function(this:any,...args:any[]){return active?replace(this,original.bind(this),args):original.apply(this,args);};
 try{if(descriptor&&!descriptor.configurable){if(!descriptor.writable)return()=>{};target[name]=wrapped;}else Object.defineProperty(target,name,{configurable:true,writable:true,enumerable:descriptor?.enumerable??false,value:wrapped});}catch{return()=>{};}
 return()=>{active=false;if(target[name]===wrapped){if(descriptor)Object.defineProperty(target,name,descriptor);else delete target[name];}};
}

export function installStoreAchievementsRuntime(deps:Dependencies){
 const host=root();host[runtimeKey]?.close?.();let alive=true,busy=false,identityDepth=0,applying=0,warmWorkers=0;
 const warmed=new Set<string>(),warmQueue:Array<{id:number;scope:string;row:StoreOwnedGame}>=[],routePropsSeen=new WeakSet<object>();
 const warmPending=new Map<number,{scope:string;promise:Promise<StoreAchievementSnapshot|null>}>();
 const imported=new Map<number,{row:StoreOwnedGame;baseline:StoreOwnedGame|undefined;current:()=>boolean}>();
 const cached=new Map<number,Payload>(),pending=new Map<number,Promise<void>>(),lastRefresh=new Map<number,number>(),observed=new Map<number,{scope:string;unlocked:Set<string>}>(),runningBefore=new Set<number>();
 const sessions=new Map<number,string>(),closedGames=new Map<number,string>(),closing=new Map<number,Promise<void>>();
 const published=new Map<number,{client:any;steamid:string;scope:string;rows:ReturnType<typeof storeAchievementQueryData>}>(),unpatch:Array<()=>void>=[],patched=new WeakSet<object>();
 const empty={nTotal:0,nAchieved:0,vecAchievedHidden:[],vecHighlight:[],vecUnachieved:[]};
 let imageStyle:HTMLStyleElement|undefined;
 function updateXboxAchievementImageStyle(){
  const urls=new Set<string>();for(const[id,payload]of cached)if(rowFor(id)?.key.store==='xbox'&&current(id,payload))for(const row of payload.rows)for(const value of [row.imageUnlocked,row.imageLocked]){const url=icon(value);if(url)urls.add(url);}
  const quote=(value:string)=>`"${value.replace(/\\/g,'\\\\').replace(/"/g,'\\"').replace(/[\r\n\f]/g,'')}"`;
  const images=[...urls].map(url=>`img[src=${quote(url)}]`),backgrounds=[...urls].map(url=>`[style*=${quote(url)}][style*="background-image"]`);
  if(!imageStyle&&urls.size&&host.document?.head){imageStyle=host.document.createElement('style');imageStyle!.id='playhub-store-xbox-achievement-fit';host.document.head.appendChild(imageStyle);}
  // Metadata's contain treatment, scoped to actual managed Xbox badge URLs.
  // Native Steam and other providers retain their own image presentation.
  if(imageStyle)imageStyle.textContent=(images.length?`${images.join(',')}{object-fit:contain!important;object-position:center center!important;width:100%!important;height:100%!important;display:block!important;}`:'')+(backgrounds.length?`${backgrounds.join(',')}{background-size:contain!important;background-position:center center!important;background-repeat:no-repeat!important;background-color:rgba(0,0,0,.18);}`:'');
 }
 function rowFor(id:number){const staged=imported.get(id),row=deps.rows().get(id);return staged&&staged.current()&&row===staged.baseline?staged.row:row;}
 function scope(id:number){const row=rowFor(id);return supported(row)?`${row!.key.store}:${row!.key.id}:${row!.game!.accountRevision}:${language()}`:null;}
 const current=(id:number,value:Payload)=>alive&&scope(id)===value.scope&&cached.get(id)===value;
 function clear(id:number){lastRefresh.delete(id);const value=cached.get(id);cached.delete(id);observed.delete(id);const prior=published.get(id);published.delete(id);
  updateXboxAchievementImageStyle();
  if(prior)for(const row of prior.rows)if(prior.client.getQueryData(row.key)===row.data)prior.client.removeQueries({queryKey:row.key,exact:true});
  applying++;try{const data=host.appDetailsStore?.GetAppData?.(id);if(value&&data?.details?.achievements===value.steam)data.details.achievements=empty;}finally{applying--;}
 }
 async function publish(id:number,payload:Payload){
  if(!current(id,payload))return;
  const steamid=steamId(),client=queryClient();if(!steamid||!client)return;
  const rows=storeAchievementQueryData(id,steamid,payload),prior=published.get(id);
  if(prior?.scope===payload.scope&&prior.steamid===steamid&&prior.rows.every(row=>client.getQueryData(row.key)===row.data))return;
  await Promise.all(rows.map(row=>client.cancelQueries({queryKey:row.key,exact:true},{silent:true})));
  if(!current(id,payload)||steamId()!==steamid||queryClient()!==client)return;
  published.set(id,{client,steamid,scope:payload.scope,rows});for(const row of rows)client.setQueryData(row.key,row.data);
 }
 async function apply(id:number,snapshot:StoreAchievementSnapshot,notify:boolean){
  const expected=scope(id),row=rowFor(id);if(!alive||!row||!expected||!validStoreAchievementSnapshot(snapshot,row,expected))return;
  const payload=storeAchievementPayload(snapshot);cached.set(id,payload);updateXboxAchievementImageStyle();
  applying++;try{const data=host.appDetailsStore?.GetAppData?.(id);if(data?.details){data.details.achievements=payload.steam;data.bLoadingAchievments=false;}}finally{applying--;}
  try{host.appDetailsCache?.SetCachedDataForApp?.(id,'achievements',2,payload.steam);}catch{}
  const prior=observed.get(id),unlocked=new Set(payload.rows.filter(a=>a.unlocked).map(a=>a.key));
  if(notify&&prior?.scope===expected){let count=0;for(const a of payload.rows)if(a.unlocked&&!prior.unlocked.has(a.key)&&count++<5)toaster.toast({title:payload.title,body:a.name});}
  // Cached first-load is a real baseline; new account history is never toasted.
  observed.set(id,{scope:expected,unlocked});
  await publish(id,payload);
 }
 async function ensure(id:number,force=false,notify=false){
  if(!alive||!scope(id))return;
  if(pending.has(id))return pending.get(id);
  if(!force&&Date.now()-(lastRefresh.get(id)??0)<30000){const payload=cached.get(id);if(payload)void publish(id,payload).catch(()=>{});return;}
  const row=rowFor(id)!,expected=scope(id)!;lastRefresh.set(id,Date.now());
  const args={key:row.key,expectedAccountRevision:row.game!.accountRevision,language:language()};
  const request=(async()=>{
   try{
    if(!force&&!cached.has(id)){const warming=warmPending.get(id),first=await(warming?.scope===expected?warming.promise:deps.request<StoreAchievementSnapshot|null>('store-achievements',{...args,cachedOnly:true}));if(alive&&scope(id)===expected&&first&&!cached.has(id))await apply(id,first,false);}
    const fresh=await deps.request<StoreAchievementSnapshot|null>('store-achievements',{...args,forceRefresh:true});
    if(alive&&scope(id)===expected&&fresh)await apply(id,fresh,notify);
   }catch{/* Same-account cached progress remains available during an outage. */}
  })();pending.set(id,request);try{await request;}finally{if(pending.get(id)===request)pending.delete(id);}
 }
 async function prepareImported(row:StoreOwnedGame,guard:()=>boolean){
  if(!alive||!guard()||!supported(row))return;
  const id=row.appId;imported.set(id,{row,baseline:deps.rows().get(id),current:()=>alive&&guard()});
  const expected=scope(id);if(!expected)return;
  try{
   const existing=pending.get(id);if(existing)await existing;
   if(!alive||!guard()||scope(id)!==expected)return;
   let payload=cached.get(id);
   if(payload?.scope!==expected){
    const value=await deps.request<StoreAchievementSnapshot|null>('store-achievements',{key:row.key,expectedAccountRevision:row.game!.accountRevision,language:language(),cachedOnly:true});
    if(!alive||!guard()||scope(id)!==expected)return;
    if(value)await apply(id,value,false);
    payload=cached.get(id);
    if(payload?.scope!==expected)await ensure(id,true,false);
   }
   payload=cached.get(id);if(alive&&guard()&&payload?.scope===expected)await publish(id,payload);
  }catch{/* Import ownership is preserved if its achievement provider is offline. */}
 }
 function installPatches(){
  const details=host.appDetailsStore,overviews=host.appStore;const proto=details&&Object.getPrototypeOf(details);
  if(proto&&!patched.has(proto)){
   patched.add(proto);
   unpatch.push(patch(proto,'GetAchievements',(_self,original,args)=>{const id=Number(args[0]),value=cached.get(id);if(scope(id)){void ensure(id);return value&&current(id,value)?value.steam:empty;}return original(...args);}));
   unpatch.push(patch(proto,'GetAppData',(_self,original,args)=>{const value=original(...args),id=Number(args[0]),payload=cached.get(id);if(scope(id)){if(!applying&&detailsId()===id)queueMicrotask(()=>{if(detailsId()===id)void ensure(id);});if(payload&&current(id,payload)&&value?.details)value.details.achievements=payload.steam;}return value;}));
  }
  const sample=overviews?.allApps?.find?.((a:any)=>a?.BIsShortcut?.())??overviews?.m_mapAppOverview?.values?.().next?.().value,overviewProto=sample&&Object.getPrototypeOf(sample);
  if(overviewProto&&!patched.has(overviewProto)){
   patched.add(overviewProto);
   unpatch.push(patch(overviewProto,'BHasStoreCategory',(self,original,args)=>Number(args[0])===22&&scope(Number(self.appid))&&cached.get(Number(self.appid))?.steam.nTotal?true:original(...args)));
   for(const name of ['GetGameID','GetPrimaryAppID'])unpatch.push(patch(overviewProto,name,(_self,original,args)=>{identityDepth++;try{return original(...args);}finally{identityDepth--;}}));
   unpatch.push(patch(overviewProto,'BIsModOrShortcut',(self,original,args)=>{
    const id=Number(self.appid);if(!identityDepth&&scope(id)&&cached.get(id)?.steam.nTotal&&host.SteamUIStore?.m_runningAppIDs?.has?.(id)!==true)return false;
    return original(...args);
   }));
  }
  const section=(DFL as any).findModuleChild((mod:any)=>entries(mod).find(v=>typeof v?.prototype?.GetSections==='function'))?.prototype;
  if(section&&!patched.has(section)){
   patched.add(section);unpatch.push(patch(section,'GetSections',(self,original,args)=>{const result=original(...args),overview=args[0]?.appid?args[0]:self?.props?.overview,id=Number(overview?.appid);if(scope(id)){if(detailsId()===id)void ensure(id);if(cached.get(id)?.steam.nTotal&&result?.add)result.add('achievements');}return result;}));
  }
 }
 function detailsId(tree?:any){
  const own=(DFL as any).findInReactTree?.(tree,(node:any)=>Number(node?.props?.overview?.appid)>0);
  const id=Number(own?.props?.overview?.appid??tree?.props?.overview?.appid??tree?.match?.params?.appid);
  if(Number.isInteger(id)&&id>0)return id;
  const windows=[host.Router?.WindowStore,host.SteamUIStore?.WindowStore];
  for(const window of windows){const path=window?.GamepadUIMainWindowInstance?.m_history?.location?.pathname,match=typeof path==='string'&&path.match(/\/library\/(?:app|details|[^/]+\/app)\/(\d+)/);if(match)return Number(match[1]);}
  return 0;
 }
 function warm(){
  while(alive&&warmWorkers<3&&warmQueue.length){const next=warmQueue.shift()!;warmWorkers++;
   const promise=deps.request<StoreAchievementSnapshot|null>('store-achievements',{key:next.row.key,expectedAccountRevision:next.row.game!.accountRevision,language:language(),cachedOnly:true});
   const entry={scope:next.scope,promise};warmPending.set(next.id,entry);
   void promise
    .then(async value=>{if(alive&&value&&scope(next.id)===next.scope&&!cached.has(next.id))await apply(next.id,value,false);})
    .catch(()=>{}).finally(()=>{if(warmPending.get(next.id)===entry)warmPending.delete(next.id);warmWorkers--;warm();});
  }
 }
 function changed(){if(!alive)return;for(const[id,value]of imported)if(!value.current()||deps.rows().get(id)!==value.baseline)imported.delete(id);for(const[id,value]of cached)if(scope(id)!==value.scope)clear(id);installPatches();
  for(const[id,row]of deps.rows()){const expected=scope(id);if(expected&&!cached.has(id)&&!warmed.has(`${id}:${expected}`)){warmed.add(`${id}:${expected}`);warmQueue.push({id,scope:expected,row});}}
  warm();const id=detailsId();if(id)void ensure(id);
 }
 function runningIds(){return new Set<number>(Array.from(host.SteamUIStore?.m_runningAppIDs??[]).map(Number).filter(Number.isInteger));}
 function steamFocused(){const doc=host.document;return !doc||typeof doc.hasFocus!=='function'||doc.hasFocus();}
 async function refreshClosed(id:number,expected:string){
  if(closing.has(id))return closing.get(id);
  const task=(async()=>{
   const prior=pending.get(id);if(prior)await prior;
   if(!alive||scope(id)!==expected||runningBefore.has(id))return;
   await ensure(id,true,true);
  })();closing.set(id,task);try{await task;}finally{if(closing.get(id)===task)closing.delete(id);}
 }
 function flushClosed(){if(!alive||!steamFocused())return;for(const[id,expected]of closedGames){closedGames.delete(id);void refreshClosed(id,expected);}}
 function markRunning(id:number){const expected=scope(id);if(!expected)return;runningBefore.add(id);sessions.set(id,expected);closedGames.delete(id);}
 function markClosed(id:number){const expected=sessions.get(id);runningBefore.delete(id);sessions.delete(id);if(expected&&scope(id)===expected)closedGames.set(id,expected);flushClosed();}
 function observeRunning(){if(!alive)return;const running=runningIds();for(const id of running)if(!runningBefore.has(id))markRunning(id);for(const id of [...runningBefore])if(!running.has(id))markClosed(id);flushClosed();}
 function subscribeNative(){
  try{const subscription=host.SteamClient?.GameSessions?.RegisterForAppLifetimeNotifications?.((event:any)=>{
   const id=Number(event?.unAppID);if(!alive||!Number.isInteger(id)||id<=0||typeof event?.bRunning!=='boolean')return;
   if(event.bRunning)markRunning(id);else markClosed(id);
  });if(subscription)unpatch.push(()=>subscription.unregister?.());}catch{}
  try{const subscription=host.SteamClient?.System?.UI?.RegisterForFocusChangeEvents?.(()=>{observeRunning();flushClosed();});if(subscription)unpatch.push(()=>subscription.unregister?.());}catch{}
 }
 async function tick(){
  if(!alive||busy)return;busy=true;
  try{
   changed();observeRunning();for(const id of runningBefore)if(scope(id))await ensure(id,true,true);
  }finally{busy=false;}
 }
 const bridge={protocol:1,isManagedApp:(id:number)=>scope(id)!==null,getSteamPayload:(id:number)=>cached.get(id)?.steam??null,imported:prepareImported,close};host[runtimeKey]=bridge;
 const unsubscribe=deps.subscribe(changed),timer=setInterval(()=>void tick(),15000);
 const sessionTimer=setInterval(observeRunning,1000);subscribeNative();observeRunning();
 for(const route of ['/library/app/:appid','/library/details/:appid','/library/:collection/app/:appid'])try{
  const token=routerHook?.addPatch?.(route,(tree:any)=>{
   const props=(DFL as any).findInReactTree?.(tree,(value:any)=>typeof value?.renderFunc==='function');
   if(props&&!routePropsSeen.has(props)){routePropsSeen.add(props);unpatch.push(patch(props,'renderFunc',(_self,original,args)=>{const rendered=original(...args),id=detailsId(rendered);if(id)void ensure(id);return rendered;}));}
   const id=detailsId(tree);if(id)void ensure(id);return tree;
  });if(token)unpatch.push(()=>routerHook.removePatch(route,token));
 }catch{/* Native loaders still trigger the same account-scoped cache path. */}
 installPatches();changed();
 function close(){if(!alive)return;const retained=new Set([...cached].filter(([id,value])=>scope(id)===value.scope).map(([id])=>id));alive=false;clearInterval(timer);clearInterval(sessionTimer);unsubscribe();for(const stop of unpatch.reverse())stop();for(const id of [...published.keys()])if(!retained.has(id))clear(id);imageStyle?.remove();if(host[runtimeKey]===bridge)delete host[runtimeKey];}
 return close;
}
