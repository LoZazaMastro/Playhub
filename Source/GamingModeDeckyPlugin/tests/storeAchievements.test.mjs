import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createRequire} from 'node:module';
import vm from 'node:vm';
import test from 'node:test';
const require=createRequire(import.meta.url),ts=require('typescript');
const source=readFileSync(new URL('../src/StoreAchievementsRuntime.ts',import.meta.url),'utf8');
function fixture(options={}){
 let revision=4,locale='it',fresh,resolveFresh,hold=options.hold===true,focused=true,lifetime,focus,now=Date.now();const requests=[],toasts=[],timers=[],timeouts=[],subscribers=new Set(),data=new Map(),modules=[],routes=new Map();
 const row={appId:101,key:{store:'xbox',id:'9NJRX71M5X9P'},accountCurrent:true,title:'Fixture Xbox',game:{isInstalled:true,accountRevision:4}};
 const rows=new Map(options.empty?[]:[[101,row]]),empty={nTotal:0,nAchieved:0};
 class Overview{constructor(){this.appid=101;}BIsShortcut(){return true;}BIsModOrShortcut(){return true;}BHasStoreCategory(){return false;}GetGameID(){return this.BIsModOrShortcut()?'13866673885107191808':'101';}GetPrimaryAppID(){return this.BIsModOrShortcut()?0:101;}}
 class Details{constructor(){this.data={details:{achievements:empty}};}GetAppData(){return this.data;}GetAchievements(){return empty;}}
 class Sections{GetSections(){return new Set(['activity']);}}
 const overview=new Overview(),details=new Details(),client={getQueryCache:()=>({}),getQueryData:key=>data.get(JSON.stringify(key)),setQueryData:(key,value)=>data.set(JSON.stringify(key),value),cancelQueries:async()=>{},removeQueries:({queryKey})=>data.delete(JSON.stringify(queryKey))};
 modules.push({client},{user:{logged_in:true,steamid:'76561198000000001'}},{Sections});
 const ClockDate=class extends Date{static now(){return now;}};
 const context=vm.createContext({exports:{},Symbol,Map,Set,WeakSet,URL,Date:ClockDate,Number,Object,Array,Promise,queueMicrotask,setInterval:fn=>{timers.push(fn);return timers.length;},clearInterval(){},setTimeout:(fn,ms)=>{timeouts.push({fn,ms});return timeouts.length;},clearTimeout(){},
  appStore:{allApps:[overview],GetAppOverviewByAppID:()=>overview},appDetailsStore:details,appDetailsCache:{SetCachedDataForApp(){}},SteamUIStore:{m_runningAppIDs:new Set()},document:{hasFocus:()=>focused},
  SteamClient:{GameSessions:{RegisterForAppLifetimeNotifications:fn=>{lifetime=fn;return{unregister(){lifetime=undefined;}};}},System:{UI:{RegisterForFocusChangeEvents:fn=>{focus=fn;return{unregister(){focus=undefined;}};}}}},
  require:name=>name==='./decky'?{DFL:{findModuleChild:fn=>{for(const mod of modules){const hit=fn(mod);if(hit)return hit;}}},toaster:{toast:x=>toasts.push(x)},routerHook:{addPatch:(path,fn)=>{routes.set(path,fn);return fn;},removePatch(){}}}:name==='./StoreDigitalSteamLocale'?{getSteamLocale:()=>locale}:{},
 });
 vm.runInContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,context);
 const snapshot=(unlocked=['first'])=>({key:row.key,accountRevision:revision,scope:`xbox:${row.key.id}:${revision}:it-IT`,title:'Fixture Xbox',observedAt:new Date().toISOString(),achievements:['first','second'].map((key,index)=>({key,name:key,description:'Fixture',imageUnlocked:'https://images-eds-ssl.xboxlive.com/'+key,imageLocked:'https://images-eds-ssl.xboxlive.com/locked-'+key,hidden:index===1,unlocked:unlocked.includes(key),unlockedAt:unlocked.includes(key)?1700000000:null,rarity:index===0?20:null}))});
 fresh=snapshot();let disk=options.cache===false?null:fresh;
 const request=async(method,args)=>{requests.push({method,args});if(args.cachedOnly)return disk;if(hold)return new Promise(resolve=>{resolveFresh=resolve;});return fresh;};
 const stop=context.exports.installStoreAchievementsRuntime({rows:()=>rows,subscribe:fn=>{subscribers.add(fn);return()=>subscribers.delete(fn);},request});
 const settle=async()=>{for(let i=0;i<50;i++)await Promise.resolve();};
 return{exports:context.exports,context,row,rows,details,overview,empty,requests,toasts,data,timers,stop,snapshot,settle,
  setDisk:x=>disk=x,setFresh:x=>fresh=x,hold:()=>hold=true,release:x=>{hold=false;resolveFresh(x??fresh);},changeAccount(){revision++;row.game.accountRevision=revision;for(const fn of subscribers)fn();},tick:async()=>{timers[0]();await settle();},setLocale:x=>locale=x,
  lifetime:(id,running)=>lifetime?.({unAppID:id,bRunning:running}),focus:value=>{focused=value;focus?.({focusedApp:{appid:value?0:101}});},notify:()=>{for(const fn of subscribers)fn();},expireImportWait:()=>{const timeout=timeouts.find(value=>value.ms===8000);assert.ok(timeout);timeout.fn();},advance:ms=>{now+=ms;},sections:new Sections(),
  route:(pathname,id)=>{context.Router={WindowStore:{GamepadUIMainWindowInstance:{m_history:{location:{pathname}}}}};if(id)routes.get('/library/app/:appid')?.({match:{params:{appid:String(id)}}});}};
}

test('Digital Stores publishes cached native achievements before waiting for the live provider',async()=>{
 const f=fixture();f.hold();assert.equal(f.details.GetAchievements(101).nTotal,0);await f.settle();
 const cached=f.details.GetAchievements(101);assert.equal(cached.nTotal,2);assert.equal(cached.nAchieved,1);
 assert.equal(f.requests.length,2);assert.equal(f.requests[0].args.cachedOnly,true);assert.equal(f.requests[1].args.forceRefresh,true);
 assert.equal(f.requests[0].args.language,'it-IT');assert.equal(f.overview.GetGameID(),'13866673885107191808');assert.equal(f.overview.GetPrimaryAppID(),0);
 const schema=f.data.get(JSON.stringify(['achievements',101,'schema']));assert.equal(schema.groups[0].achievements.length,2);
 const progress=f.data.get(JSON.stringify(['achievements',101,'user_achievements','76561198000000001']));assert.equal(progress.groups[0].completed_achievements,1);
 f.release();await f.settle();assert.equal(f.toasts.length,0);f.stop();
});

test('account exchange and teardown reject late progress and preserve a foreign native query replacement',async()=>{
 for(const kind of ['account','unload']){
  const f=fixture();f.hold();f.details.GetAchievements(101);await f.settle();
  const key=JSON.stringify(['achievements',101,'schema']),foreign={foreign:true};f.data.set(key,foreign);
  if(kind==='account')f.changeAccount();else f.stop();f.release(f.snapshot(['first','second']));await f.settle();
  assert.equal(f.data.get(key),foreign);assert.equal(f.toasts.length,0);if(kind==='account')assert.equal(f.details.GetAchievements(101).nTotal,0);f.stop();
 }
});

test('live and post-play polling toast only new unlocks and never clear the progress at game exit',async()=>{
 const f=fixture();f.details.GetAchievements(101);await f.settle();
 f.context.SteamUIStore.m_runningAppIDs.add(101);f.setFresh(f.snapshot(['first','second']));await f.tick();
 assert.deepEqual(f.toasts.map(x=>x.body),['second']);assert.equal(f.details.GetAchievements(101).nAchieved,2);
 f.context.SteamUIStore.m_runningAppIDs.clear();const before=f.requests.length;await f.tick();assert.equal(f.requests.length,before+1);
 assert.equal(f.details.GetAchievements(101).nAchieved,2);assert.deepEqual(f.toasts.map(x=>x.body),['second']);f.stop();
});

test('normalization uses stable achievement keys, distinct locked art and real rarity',()=>{
 const f=fixture(),snapshot=f.snapshot(),payload=f.exports.storeAchievementPayload(snapshot),query=f.exports.storeAchievementQueryData(101,'76561198000000001',payload);
 assert.equal(payload.steam.vecUnachieved[0].strImage,'https://images-eds-ssl.xboxlive.com/locked-second');
 const percentages=query.find(x=>x.key[2]==='globalpercentages').data.percentages;assert.deepEqual(JSON.parse(JSON.stringify(percentages)),{'1':20});
 const reordered={...snapshot,achievements:snapshot.achievements.slice().reverse()},other=f.exports.storeAchievementQueryData(101,'76561198000000001',f.exports.storeAchievementPayload(reordered));
 assert.deepEqual(JSON.parse(JSON.stringify(other[0].data)),JSON.parse(JSON.stringify(query[0].data)));f.stop();
});

test('store/account mismatches, duplicate IDs and non-HTTPS artwork never enter a native payload',()=>{
 const f=fixture(),s=f.snapshot();assert(f.exports.validStoreAchievementSnapshot(s,f.row,s.scope));
 for(const bad of [{...s,key:{store:'gog',id:s.key.id}},{...s,accountRevision:99},{...s,achievements:[s.achievements[0],s.achievements[0]]}])assert.equal(f.exports.validStoreAchievementSnapshot(bad,f.row,s.scope),false);
 s.achievements[0].imageUnlocked='file:///private';assert.equal(f.exports.storeAchievementPayload(s).steam.vecHighlight[0].strImage,'');f.stop();
});

test('a new import waits for a real initial fetch and publishes Your stuff before resolving',async()=>{
 const f=fixture({empty:true,cache:false,hold:true});let ready=false;
 const prepared=f.exports.prepareStoreImportedAchievements(f.row,()=>true).then(()=>{ready=true;});await f.settle();
 assert.equal(ready,false);assert.equal(f.requests.length,2);assert.equal(f.requests[0].args.cachedOnly,true);assert.equal(f.requests[1].args.forceRefresh,true);
 assert.equal(f.data.size,0);f.release();await prepared;assert.equal(ready,true);
 const schema=f.data.get(JSON.stringify(['achievements',101,'schema']));assert.equal(schema.groups[0].achievements.length,2);
 assert.equal(f.details.GetAchievements(101).nAchieved,1);f.rows.set(101,{...f.row});f.notify();await f.settle();
 assert.equal(f.details.GetAchievements(101).nTotal,2);f.stop();
});
test('an imported cached game publishes immediately without waiting on a network refresh',async()=>{
 const f=fixture({empty:true,hold:true});await f.exports.prepareStoreImportedAchievements(f.row,()=>true);
 assert.equal(f.requests.length,1);assert.equal(f.requests[0].args.cachedOnly,true);
 assert.ok(f.data.has(JSON.stringify(['achievements',101,'schema'])));f.stop();
});
test('import ownership changing during the initial fetch cannot publish into another account',async()=>{
 const f=fixture({empty:true,cache:false,hold:true});let current=true;
 const prepared=f.exports.prepareStoreImportedAchievements(f.row,()=>current);await f.settle();current=false;f.release();await prepared;
 assert.equal(f.data.size,0);assert.equal(f.toasts.length,0);f.stop();
});
test('a rapid native game start and exit refreshes only that closed game when Steam regains focus',async()=>{
 const f=fixture();await f.settle();f.details.GetAchievements(101);await f.settle();
 const other={...f.row,appId:202,key:{store:'gog',id:'other'},game:{isInstalled:true,accountRevision:4}};f.rows.set(202,other);f.notify();await f.settle();
 const before=f.requests.length;f.focus(false);f.lifetime(101,true);f.lifetime(101,false);await f.settle();assert.equal(f.requests.length,before);
 f.setFresh(f.snapshot(['first','second']));f.focus(true);await f.settle();
 const requested=f.requests.slice(before);assert.equal(requested.length,1);assert.equal(requested[0].args.key.id,f.row.key.id);assert.equal(requested[0].args.forceRefresh,true);
 assert.equal(f.details.GetAchievements(101).nAchieved,2);f.focus(true);await f.settle();assert.equal(f.requests.length,before+1);f.stop();
});
test('exit during a pending refresh forces one trailing read instead of reusing pre-exit progress',async()=>{
 const f=fixture();f.details.GetAchievements(101);await f.settle();f.context.SteamUIStore.m_runningAppIDs.add(101);f.lifetime(101,true);f.hold();await f.tick();
 const before=f.requests.length;f.context.SteamUIStore.m_runningAppIDs.clear();f.lifetime(101,false);f.focus(true);await f.settle();assert.equal(f.requests.length,before);
 f.setFresh(f.snapshot(['first','second']));f.release(f.snapshot(['first']));await f.settle();
 assert.equal(f.requests.length,before+1);assert.equal(f.requests.at(-1).args.forceRefresh,true);assert.equal(f.details.GetAchievements(101).nAchieved,2);
 assert.deepEqual(f.toasts.map(value=>value.body),['second']);f.stop();
});
test('a pending closed-game return is fenced by account generation and disposed callbacks do nothing',async()=>{
 const f=fixture();f.details.GetAchievements(101);await f.settle();f.focus(false);f.lifetime(101,true);f.lifetime(101,false);f.changeAccount();await f.settle();
 const before=f.requests.length;f.focus(true);await f.settle();assert.equal(f.requests.length,before);
 f.stop();f.lifetime(101,true);f.lifetime(101,false);f.focus(true);await f.settle();assert.equal(f.requests.length,before);
});

test('an unavailable first achievement request cannot hold import forever or publish invented zero progress',async()=>{
 const f=fixture({empty:true,cache:false,hold:true});let ready=false;
 const prepared=f.exports.prepareStoreImportedAchievements(f.row,()=>true).then(()=>{ready=true;});await f.settle();assert.equal(ready,false);
 f.expireImportWait();await prepared;assert.equal(ready,true);assert.equal(f.data.size,0);
 f.release();await f.settle();assert.ok(f.data.has(JSON.stringify(['achievements',101,'schema'])));f.stop();
});

test('normal runtime teardown retains confirmed same-account native Your stuff data',async()=>{
 const f=fixture();f.details.GetAchievements(101);await f.settle();const schema=f.data.get(JSON.stringify(['achievements',101,'schema']));
 const native=f.details.GetAppData(101).details.achievements;assert.equal(native.nTotal,2);f.stop();
 assert.equal(f.data.get(JSON.stringify(['achievements',101,'schema'])),schema);assert.equal(f.details.GetAppData(101).details.achievements,native);
});

test('Home cards never refresh twenty unrelated games or the previously viewed A after closing game B',async()=>{
 const f=fixture();await f.settle();f.route('/library/app/101',101);await f.settle();
 const others=Array.from({length:20},(_,index)=>({...f.row,appId:202+index,key:{store:'gog',id:'owned-'+index},title:'Other '+index,game:{isInstalled:true,accountRevision:4}}));
 for(const other of others)f.rows.set(other.appId,other);f.notify();await f.settle();
 const closed=others[0];f.route('/');f.advance(31000);f.focus(false);f.lifetime(closed.appId,true);
 f.setFresh({...f.snapshot(['first','second']),key:closed.key,scope:`gog:${closed.key.id}:4:it-IT`,title:closed.title});
 const before=f.requests.length;
 for(const id of f.rows.keys()){f.details.GetAppData(id);f.sections.GetSections({appid:id});}
 f.lifetime(closed.appId,false);f.focus(true);f.notify();await f.tick();await f.settle();
 const remote=f.requests.slice(before).filter(request=>!request.args.cachedOnly);
 assert.deepEqual(remote.map(request=>request.args.key.id),[closed.key.id]);
 assert.equal(remote[0].args.expectedAccountRevision,4);assert.equal(remote[0].args.language,'it-IT');f.stop();
});

test('native data reads only refresh the visible detail route while an explicit Your stuff request remains available',async()=>{
 const f=fixture();await f.settle();f.route('/');f.details.GetAppData(101);await f.settle();assert.equal(f.requests.filter(request=>!request.args.cachedOnly).length,0);
 f.route('/library/app/101',101);await f.settle();assert.equal(f.requests.filter(request=>!request.args.cachedOnly).length,1);
 f.route('/');f.advance(31000);f.details.GetAchievements(101);await f.settle();assert.equal(f.requests.filter(request=>!request.args.cachedOnly).length,2);f.stop();
});
