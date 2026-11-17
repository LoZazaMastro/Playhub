// SPDX-License-Identifier: MIT. Copyright (c) 2026 LoZazaMastro.
// Installation events affect only Steam's existing Home recent-games output.
// Native shortcut identities, playtime, launch state and overview fields stay intact.
import {SP_REACT as React,DFL,routerHook} from './decky';
import {createOutputPatch} from './reactOutputPatch';
import type {StoreOwnedGame,StoreJob} from './storeLibraryRuntime';

export type StoreInstallRecent={appId:number;identity:string;event:string;installedAt:number};
type Storage={getItem(key:string):string|null;setItem(key:string,value:string):void};
const retention=7*24*60*60,ownerKey=Symbol.for('playhub.store.install-recents.v1');
const validEntry=(v:any):v is StoreInstallRecent=>v&&Number.isInteger(v.appId)&&v.appId>0&&v.appId<=0xffffffff
 &&typeof v.identity==='string'&&v.identity.length>0&&v.identity.length<=16384&&typeof v.event==='string'&&v.event.length>0&&v.event.length<=2048&&Number.isFinite(v.installedAt)&&v.installedAt>0;
export function createStoreInstallRecentLedger(storage:Storage|undefined,key:string,now=()=>Date.now()/1000){
 const entries=new Map<string,StoreInstallRecent>(),seen=new Set<string>();
 try{const raw=storage?.getItem(key);if(raw&&raw.length<=1024*1024){const disk=JSON.parse(raw);if(disk?.version===1&&Array.isArray(disk.entries)&&disk.entries.length<=256&&Array.isArray(disk.seen)&&disk.seen.length<=2048){
  for(const row of disk.entries)if(validEntry(row)&&row.installedAt<=now()+60&&row.installedAt>=now()-retention)entries.set(row.event,row);
  for(const event of disk.seen)if(typeof event==='string'&&event.length>0&&event.length<=2048)seen.add(event);
 }}}catch{}
 const save=()=>{while(entries.size>256)entries.delete(entries.keys().next().value!);while(seen.size>2048)seen.delete(seen.keys().next().value!);try{storage?.setItem(key,JSON.stringify({version:1,entries:[...entries.values()],seen:[...seen]}));}catch{}};
 return{
  read:()=>[...entries.values()],
  remember(row:StoreInstallRecent){if(!validEntry(row)||row.installedAt>now()+60||row.installedAt<now()-retention||seen.has(row.event))return false;seen.add(row.event);entries.set(row.event,{...row});save();return true;},
  prune(keep:(entry:StoreInstallRecent)=>boolean){let changed=false;for(const[event,row]of entries)if(row.installedAt<now()-retention||!keep(row)){entries.delete(event);changed=true;}if(changed)save();return changed;},
 };
}
function nativeRecentTime(app:any){
 return Math.max(...['rt_last_time_locally_played','rt_last_time_played','rt_last_time_played_or_installed','rt_recent_activity_time','rt_purchased_time'].map(key=>Number.isFinite(app?.[key])?Number(app[key]):0));
}
/** Preserve native order except where a verified install event belongs ahead of it. */
export function projectStoreInstallRecents(games:readonly number[],entries:readonly StoreInstallRecent[],overview:(id:number)=>any):number[]{
 if(!Array.isArray(games)||games.length>1000||games.some(id=>!Number.isInteger(id)||id<0||id>0xffffffff))return games as number[];
 const byApp=new Map<number,StoreInstallRecent>();
 for(const row of entries){if(!validEntry(row)||!overview(row.appId))continue;const old=byApp.get(row.appId);if(!old||row.installedAt>old.installedAt)byApp.set(row.appId,row);}
 if(!byApp.size)return games as number[];
 // Steam keeps its currently running game and a native background sentinel.
 const first=overview(games[0]);
 const prefix=games.length>=2&&games[0]>0&&games[1]===0&&first?.BIsAppRunning===true?games.slice(0,2):[];
 for(const id of prefix)byApp.delete(id);
 // A stale sentinel belongs only to Steam's running-game pair. Never turn it into a card.
 const remaining=games.filter(id=>id!==0&&!prefix.includes(id)&&!byApp.has(id));
 const installs=[...byApp.values()].sort((a,b)=>Math.max(b.installedAt,nativeRecentTime(overview(b.appId)))-Math.max(a.installedAt,nativeRecentTime(overview(a.appId)))||b.appId-a.appId);
 for(const row of installs.reverse()){
  const stamp=Math.max(row.installedAt,nativeRecentTime(overview(row.appId)));
  const at=remaining.findIndex(id=>nativeRecentTime(overview(id))<=stamp);
  remaining.splice(at<0?remaining.length:at,0,row.appId);
 }
 const result=[...prefix,...remaining].slice(0,20);
 return result.length===games.length&&result.every((id,index)=>id===games[index])?games as number[]:result;
}
export function presentStoreInstallRecentTree(tree:any,project:(games:number[])=>number[]):any{
 if(Array.isArray(tree)){const next=tree.map(node=>presentStoreInstallRecentTree(node,project));return next.some((node,index)=>node!==tree[index])?next:tree;}
 if(!React.isValidElement<any>(tree))return tree;
 const props=tree.props;
 if(Array.isArray(props.games)&&(typeof props.onItemFocus==='function'&&'autoFocus'in props&&'showFeaturedItem'in props||'refOnItemFocus'in props)){
  const games=project(props.games);if(games===props.games)return tree;
  // Steam caches virtualized carousel cells; its background also initializes from
  // games only on mount. Rebuild both for a genuinely changed projected order.
  // A stable key keeps ordinary status polling from remounting either surface.
  const key=`${tree.key??'recent'}:playhub-install:${games.join(',')}`;
  return React.cloneElement(tree,{key,games,...('overscan'in props?{overscan:games.length}:{})});
 }
 if(props.children===undefined)return tree;
 const children=presentStoreInstallRecentTree(props.children,project);return children===props.children?tree:React.cloneElement(tree,{},children);
}
type Dependencies={
 rows:()=>ReadonlyMap<number,StoreOwnedGame>;jobs:()=>readonly StoreJob[];subscribe:(fn:()=>void)=>()=>void;
 identity:(row:StoreOwnedGame|undefined)=>string|undefined;observe:(row:StoreOwnedGame,matched:(value:boolean)=>void)=>()=>void;
};
function steamAccount():string|null{
 try{return(DFL as any).findModuleChild((mod:any)=>{if(!mod||typeof mod!=='object')return;for(const key of Object.keys(mod))try{const value=mod[key];if(value?.logged_in===true&&typeof value.steamid==='string'&&/^7656119\d{10}$/.test(value.steamid))return value.steamid;}catch{}})??null;}catch{return null;}
}
export function installStoreInstallRecents(deps:Dependencies){
 const host=globalThis as any;host[ownerKey]?.close?.();let alive=true,account:string|null=null,ledger:ReturnType<typeof createStoreInstallRecentLedger>|undefined,revision=0;
 const listeners=new Set<()=>void>(),probes=new Map<number,{identity:string;stop:()=>void;at:number}>(),matched=new Map<number,string>(),candidates=new Map<number,StoreInstallRecent[]>();
 const eligible=(row:StoreOwnedGame|undefined)=>!!row?.accountCurrent&&row.game?.isInstalled===true&&row.game.isLaunchable===true&&!!deps.identity(row);
 const changed=()=>{revision++;for(const fn of listeners)try{fn();}catch{}};
 function available(){
  if(!ledger||!account||steamAccount()!==account)return[];
  return ledger.read().filter(entry=>{const row=deps.rows().get(entry.appId);return eligible(row)&&deps.identity(row)===entry.identity&&matched.get(entry.appId)===entry.identity&&!!host.appStore?.GetAppOverviewByAppID?.(entry.appId);});
 }
 const project=(games:number[])=>projectStoreInstallRecents(games,available(),id=>host.appStore?.GetAppOverviewByAppID?.(id));
 function Surface({native}:{native:any}){React.useSyncExternalStore(fn=>{listeners.add(fn);return()=>{listeners.delete(fn);};},()=>revision);return presentStoreInstallRecentTree(native,project);}
 // Steam's route root renders an intermediate Home component before its recent section.
 const patch=createOutputPatch([
  node=>typeof node?.type!=='string',
  node=>typeof node?.type!=='string',
  node=>node?.props&&'autoFocus'in node.props&&'showBackground'in node.props,
 ],tree=>React.createElement(Surface,{native:tree}),()=>{});
 let routeToken:any;try{routeToken=routerHook?.addPatch?.('/library/home',(props:any)=>{if(!alive||!props?.children)return props;const child=patch.apply(props.children);if(child===props.children)return props;return{...props,children:child};});}catch{}
 function update(){
  if(!alive)return;const next=steamAccount();
  if(account!==next){for(const probe of probes.values())probe.stop();probes.clear();matched.clear();candidates.clear();account=next;ledger=undefined;
   if(next){let storage:Storage|undefined;try{storage=host.localStorage;}catch{}ledger=createStoreInstallRecentLedger(storage,'playhub.store.install-recents.v1:'+next);}changed();
  }
  if(!ledger||!account)return;
  const rows=deps.rows(),byKey=new Map([...rows.values()].map(row=>[JSON.stringify([row.key.store,row.key.id,row.game?.accountRevision]),row])),events=[...deps.jobs(),...[...rows.values()].flatMap(row=>row.job?[row.job]:[])],seen=new Set<string>();candidates.clear();
  for(const job of events){
   if(job.state!=='completed'||typeof job.jobId!=='string'||job.jobId.length>128||typeof job.updatedAt!=='string')continue;
   const stamp=Date.parse(job.updatedAt)/1000;if(!Number.isFinite(stamp)||stamp<Date.now()/1000-retention||stamp>Date.now()/1000+60)continue;
   const row=byKey.get(JSON.stringify([job.key?.store,job.key?.id,job.accountRevision]));
   if(!eligible(row))continue;const identity=deps.identity(row)!;const event=JSON.stringify([job.jobId,row!.appId,job.accountRevision]);if(seen.has(event))continue;seen.add(event);
   const entries=candidates.get(row!.appId)??[];entries.push({appId:row!.appId,identity,event,installedAt:stamp});candidates.set(row!.appId,entries);
  }
  for(const entry of ledger.read())if(!candidates.has(entry.appId))candidates.set(entry.appId,[]);
  let dirty=ledger.prune(entry=>{const row=rows.get(entry.appId);return !row||eligible(row)&&deps.identity(row)===entry.identity;});
  for(const[id,probe]of probes)if(!candidates.has(id)||!eligible(rows.get(id))||deps.identity(rows.get(id))!==probe.identity){probe.stop();probes.delete(id);matched.delete(id);dirty=true;}
  const commit=(id:number,identity:string)=>{if(!alive||!ledger||steamAccount()!==account||!eligible(deps.rows().get(id))||deps.identity(deps.rows().get(id))!==identity)return;
   let added=false;for(const entry of candidates.get(id)??[])added=ledger.remember(entry)||added;if(added)changed();
  };
  for(const[id]of candidates){
   const row=rows.get(id),identity=deps.identity(row);if(!eligible(row)||!identity)continue;const existing=probes.get(id);
   if(existing?.identity===identity&&(matched.get(id)===identity||Date.now()-existing.at<5000)){if(matched.get(id)===identity)commit(id,identity);continue;}
   existing?.stop();let active=true;const stop=deps.observe(row!,value=>{if(!alive||!active)return;if(value){matched.set(id,identity);commit(id,identity);}else matched.delete(id);changed();});
   probes.set(id,{identity,at:Date.now(),stop:()=>{active=false;stop();}});
  }
  if(dirty)changed();
 }
 const unsubscribe=deps.subscribe(update),timer=setInterval(update,3000);
 function close(){if(!alive)return;alive=false;unsubscribe();clearInterval(timer);patch.stop();for(const probe of probes.values())probe.stop();probes.clear();listeners.clear();if(routeToken)try{routerHook.removePatch('/library/home',routeToken);}catch{}if(host[ownerKey]===bridge)delete host[ownerKey];}
 const bridge={protocol:1,read:()=>available().map(({appId,installedAt})=>({appId,installedAt})),project,close};host[ownerKey]=bridge;update();return close;
}

