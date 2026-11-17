import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import test from 'node:test';
import vm from 'node:vm';
const require=createRequire(new URL('../package.json',import.meta.url)),ts=require('typescript');
const sourceRoot=process.env.STORE_FIX_SOURCE_ROOT??new URL('../src/',import.meta.url);
const read=name=>readFileSync(typeof sourceRoot==='string'?sourceRoot+'/'+name:new URL(name,sourceRoot),'utf8');
const activityPath=process.env.STORE_ACTIVITY_SOURCE??new URL('../src/storeDownloadActivity.ts',import.meta.url);
const compile=(s,extra='')=>ts.transpileModule(s+'\n'+extra,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React}}).outputText;
const activity={};vm.runInNewContext(compile(readFileSync(activityPath,'utf8')),{exports:activity,TextEncoder});
const appId=3000000000,exe='C:\\Apps\\Playhub.GameSession.exe',dir='C:\\Apps';
const row=(id=appId)=>({appId:id,key:{store:'epic',id:'game-'+id},executable:exe,arguments:'--store-library epic game-'+id,startDirectory:dir,title:'Fixture',accountCurrent:true,game:{accountRevision:7,isInstalled:false,isLaunchable:false}});
const plan=()=>({requestId:'request-1',claimId:'claim-1',key:{store:'epic',id:'game'},accountRevision:7,title:'Fixture',executable:exe,arguments:'--store-library epic game',startDirectory:dir});
const details=(r,id=r.appId)=>({unAppID:id,strShortcutExe:'"'+r.executable+'"',strShortcutLaunchOptions:r.arguments,strShortcutStartDir:'"'+r.startDirectory+'"'});
function fixture(options={}){
  const existing=new Map(),subscriptions=[],calls=[],intervals=[],sets=[],native=new Map();let adds=0,acks=0,now=100000,autoDetails=true,failAcks=options.failAcks??0,failDrafts=options.failDrafts??0;
  let bodyPlan=plan(),api;const nativeImports={},models={},migration={};
  const store={GetAppOverviewByAppID:id=>existing.get(id)};
  const collectionMap=new Map();let collectionSaves=0,failCollectionSaves=options.failCollectionSaves??0;
  const collections={GetCollectionIDByUserTag:name=>collectionMap.has(name)?name:undefined,GetCollection:id=>collectionMap.get(id),
    NewUnsavedCollection(name,_filter,overviews){const collection={displayName:name,allApps:[...overviews],AsDragDropCollection(){return{AddApps(apps){for(const app of apps)if(!collection.allApps.some(a=>a.appid===app.appid))collection.allApps.push(app);},RemoveApps(apps){collection.allApps=collection.allApps.filter(app=>!apps.some(remove=>remove.appid===app.appid));}};},async Save(){++collectionSaves;if(failCollectionSaves-->0)throw Error('collection save failed');collectionMap.set(name,collection);}};return collection;}};
  const liveDetails=id=>options.nativeDefaults?native.get(id):details(bodyPlan,id);
  const emit=id=>{for(const sub of subscriptions.filter(sub=>sub.id===id&&sub.active))sub.cb(liveDetails(id));};
  const Apps={async AddShortcut(title,executable,startDirectory,args){++adds;const id=options.returnedId??activity.shortcutAppId(executable,title);existing.set(id,{appid:id,display_name:options.nativeDefaults?'Playhub.GameSession':title});native.set(id,{unAppID:id,strShortcutExe:executable,strShortcutLaunchOptions:options.nativeDefaults?'':args,strShortcutStartDir:dir+'\\'});options.onAdd?.(id);return id;},
    RegisterForAppDetails(id,cb){const entry={id,cb,active:true};subscriptions.push(entry);if(autoDetails){if(options.emptyFirst){cb({});setTimeout(()=>entry.active&&cb(liveDetails(id)),5);}else cb(liveDetails(id));}return{unregister(){entry.active=false;}};}};
  for(const [method,field]of [['SetShortcutName','display_name'],['SetShortcutExe','strShortcutExe'],['SetShortcutStartDir','strShortcutStartDir'],['SetShortcutLaunchOptions','strShortcutLaunchOptions']])
    Apps[method]=async(id,value)=>{sets.push({method,id,value});if(options.setterDelay)await new Promise(resolve=>setTimeout(resolve,options.setterDelay));if(field==='display_name')existing.get(id).display_name=value;else native.get(id)[field]=value;emit(id);};
  const context=vm.createContext({TextEncoder,Map,Set,Symbol,performance:{now:()=>now},navigator:{language:'en'},SteamUIStore:{MainRunningAppID:0,RunningApps:[]},appStore:store,
    SteamClient:{Apps,Downloads:{RegisterForDownloadOverview(cb){cb({remote_client_id:'0',paused:false,update_appid:0,update_state:'None'});return{unregister(){}};}}},collectionStore:collections,
    Date:class extends Date{constructor(...a){super(...(a.length?a:[now]));}static now(){return now;}},
    setTimeout,clearTimeout,setInterval(fn){intervals.push(fn);return intervals.length;},clearInterval(){},crypto:{randomUUID:()=> 'request-guid'},
  });
  const control={async call(name,method,args){assert.equal(name,'store_library_request');calls.push({method,args});
    if(method==='steam-sync-created'){if(failDrafts-->0)throw Error('draft transport failure');return{ok:true,data:{requestId:args.requestId,state:'pending'}};}
    if(method==='steam-sync-ack'){++acks;if(acks<=failAcks)throw Error('ack transport failure');return{ok:true,data:{}};}
    if(options.rpc){const custom=await options.rpc(method,args);if(custom!==undefined)return{ok:true,data:custom};}
    const data=method==='downloads'?[]:method==='steam-register'?{ownerId:'owner'}:method==='steam-activity'?{accepted:true}:method==='steam-sync-pending'?{plans:[]}:method==='steam-owned'?[row()]:{};
    return{ok:true,data};
  }};
  vm.runInNewContext(compile(read('storeNativeImports.ts')),{exports:nativeImports,require:()=>activity,Map,Set,Object});
  vm.runInNewContext(compile(read('StoreDigitalModels.ts')),{exports:models,Date,Map,Set});
  vm.runInNewContext(compile(read('storeSteamMigration.ts')),{exports:migration,require:name=>name==='./StoreDigitalModels'?models:name==='./storeNativeImports'?nativeImports:activity,Map,Set,BigInt,Date});
  function loadRuntime(){const exports={};context.runtimeExports=exports;context.require=name=>name==='./controlBackend'?control:name==='./storeNativeImports'?nativeImports:name==='./storeSteamMigration'?migration:activity;
    const original=context.require;context.require=name=>name==='./storeCompletionNotifications'?{createStoreCompletionNotifications:()=>()=>{}}:name==='./storeInstallRecents'?{installStoreInstallRecents:()=>()=>{}}:name==='./StoreAchievementsRuntime'?{installStoreAchievementsRuntime:()=>()=>{},prepareStoreImportedAchievements:async()=>{}}:original(name);
    vm.runInContext('(function(exports,require){'+compile(read('storeLibraryRuntime.ts'),'exports.__warmImportedAchievements=warmImportedAchievements;exports.__applyPlan=applyPlan;exports.__reconcileNative=reconcileNativeImports;exports.__removeUninstalled=removeUninstalledSteamShortcuts;exports.__applyPendingPlans=applyPendingPlans;exports.__reconcile=reconcileStoreCollection;exports.__seed=rows=>{owned=new Map(rows.map(r=>[r.appId,r]));};')+'})(runtimeExports,require)',context);api=exports;return api;}
  loadRuntime();
  return{context,store,Apps,nativeImports,existing,subscriptions,calls,intervals,loadRuntime,collectionMap,sets,native,get collectionSaves(){return collectionSaves;},get api(){return api;},get adds(){return adds;},get acks(){return acks;},
    setPlan(v){bodyPlan=v;},setNow(v){now=v;},setAuto(v){autoDetails=v;},seed(rows){api.__seed(rows);},
    emit(id,value,includeClosed=false){for(const s of subscriptions.filter(s=>s.id===id&&(s.active||includeClosed)))s.cb(value);},
  };
}
function component(f){
  const state=[],deps=[],cleanup=[];let index=0,effects=[],sets=0;
  const React={createElement:(type,props,...children)=>({type,props:{...props,children}}),
    useSyncExternalStore:(_subscribe,snapshot)=>snapshot(),
    useState(initial){const i=index++;if(!(i in state))state[i]=initial;return[state[i],value=>{++sets;state[i]=value;}];},
    useEffect(effect,next){const i=index++;if(!deps[i]||next.some((v,n)=>v!==deps[i][n]))effects.push(()=>{cleanup[i]?.();deps[i]=next;cleanup[i]=effect();});},
  };
  const modules={'./decky':{SP_REACT:React,DFL:{DialogButton:'DialogButton'},routerHook:{}},'./reactOutputPatch':{},'./StoreDigitalNavigation':{openStoreDigitalGame:(key,action)=>f.calls.push({method:'portal',args:{key,action}})},'./storeLibraryRuntime':f.api,'./storeDownloadMenu':{StoreDownloadMenu:()=>null},'./storeLibraryLocale':{storeLibraryLocale:()=>({})}};
  const exports={};f.context.uiExports=exports;f.context.uiRequire=name=>modules[name];
  vm.runInContext('(function(exports,require){'+compile(read('storeLibrarySteam.tsx'),'exports.__GameAction=GameAction;')+'})(uiExports,uiRequire)',f.context);
  return{render(id=appId,native={kind:'native'}){index=0;effects=[];const output=exports.__GameAction({appId:id,native});effects.forEach(fn=>fn());return output;},unmount(){cleanup.forEach(fn=>fn?.());},get sets(){return sets;}};
}

test('edited foreign Steam shortcut stays native despite an old durable owned AppID',()=>{
  const f=fixture();f.seed([row()]);f.setAuto(false);const ui=component(f),native={kind:'native'};
  assert.equal(ui.render(appId,native),native);
  f.emit(appId,{...details(row()),strShortcutExe:'C:\\Manual\\DifferentGame.exe'});
  assert.equal(ui.render(appId,native),native);
});
test('successful own creation retries an ACK transport error without creating or adopting another shortcut',async()=>{
  const f=fixture({failAcks:1}),p=plan();f.setPlan(p);
  await assert.rejects(f.api.__applyPlan(p,()=>true),/ack transport failure/);
  await f.api.__applyPlan(p,()=>true);
  assert.equal(f.adds,1);assert.equal(f.acks,2);
});
test('sync saves each verified shortcut in its store collection and preserves the user collection contents',async()=>{
  for(const [store,name]of Object.entries({epic:'Epic Games',gog:'GOG',xbox:'Xbox Store',amazon:'Amazon Games'})){
    const f=fixture(),p={...plan(),key:{store,id:'game'},arguments:'--store-library '+store+' game'};f.setPlan(p);
    await f.api.__applyPlan(p,()=>true);const collection=f.collectionMap.get(name);
    assert.ok(collection);assert.deepEqual(collection.allApps.map(a=>a.appid),[activity.shortcutAppId(p.executable,p.title)]);
    assert.equal(f.collectionSaves,1);assert.equal(f.acks,1);
  }
});
test('failed collection save never acknowledges success or creates a duplicate shortcut on retry',async()=>{
  const f=fixture({failCollectionSaves:1}),p=plan();f.setPlan(p);
  await assert.rejects(f.api.__applyPlan(p,()=>true),/collection save failed/);assert.equal(f.acks,0);
  await f.api.__applyPlan(p,()=>true);assert.equal(f.adds,1);assert.equal(f.acks,1);
});
test('missing collection API leaves sync unconfirmed instead of reporting an absent collection as success',async()=>{
  const f=fixture(),p=plan();f.setPlan(p);delete f.context.collectionStore;
  await assert.rejects(f.api.__applyPlan(p,()=>true),/steam_collections_unavailable/);assert.equal(f.acks,0);
});

test('Windows default shortcut metadata is configured by native setters and retains the returned AppID',async()=>{
  const f=fixture({nativeDefaults:true,returnedId:4088526249,emptyFirst:true,setterDelay:5}),p=plan();f.setPlan(p);
  await f.api.__applyPlan(p,()=>true);
  assert.equal(f.adds,1);assert.equal(f.acks,1);assert.equal(f.existing.get(4088526249).display_name,p.title);
  assert.deepEqual(f.sets.map(row=>row.method),['SetShortcutName','SetShortcutExe','SetShortcutStartDir','SetShortcutLaunchOptions']);
  assert.ok(f.sets.every(row=>row.id===4088526249));assert.equal(f.calls.find(row=>row.method==='steam-sync-ack').args.appId,4088526249);
});

test('retained own creation repairs its native default metadata after a rejected-setter retry',async()=>{
  const f=fixture({nativeDefaults:true,returnedId:4088526249}),p=plan();f.setPlan(p);const setter=f.Apps.SetShortcutName;f.Apps.SetShortcutName=()=>{throw Error('setter rejected');};
  await assert.rejects(f.api.__applyPlan(p,()=>true),/setter rejected/);assert.equal(f.adds,1);assert.equal(f.acks,0);
  f.Apps.SetShortcutName=setter;await f.api.__applyPlan(p,()=>true);assert.equal(f.adds,1);assert.equal(f.acks,1);
});

test('a durable unconfirmed draft resumes after JS context replacement without another AddShortcut',async()=>{
  const f=fixture({nativeDefaults:true,returnedId:4088526249}),p=plan();f.setPlan(p);const setter=f.Apps.SetShortcutName;f.Apps.SetShortcutName=()=>{throw Error('setter rejected');};
  await assert.rejects(f.api.__applyPlan(p,()=>true),/setter rejected/);
  const draft=f.calls.find(row=>row.method==='steam-sync-created');assert.equal(draft.args.appId,4088526249);assert.equal(f.acks,0);
  delete f.context[Symbol.for('playhub.store-library.own-created.v1')];f.loadRuntime();f.Apps.SetShortcutName=setter;
  await f.api.__applyPlan({...p,createdAppId:draft.args.appId},()=>true);
  assert.equal(f.adds,1);assert.equal(f.acks,1);assert.equal(f.existing.get(4088526249).display_name,p.title);
});

test('a durable draft whose live target changed never configures, acknowledges or creates a replacement',async()=>{
  const f=fixture({nativeDefaults:true,returnedId:4088526249}),p=plan();f.setPlan(p);const setter=f.Apps.SetShortcutName;f.Apps.SetShortcutName=()=>{throw Error('setter rejected');};
  await assert.rejects(f.api.__applyPlan(p,()=>true));f.Apps.SetShortcutName=setter;
  delete f.context[Symbol.for('playhub.store-library.own-created.v1')];f.loadRuntime();f.native.get(4088526249).strShortcutExe='C:\\Foreign.exe';
  await assert.rejects(f.api.__applyPlan({...p,createdAppId:4088526249},()=>true),/unconfirmed/);
  assert.equal(f.adds,1);assert.equal(f.acks,0);assert.equal(f.sets.length,0);
});

test('retained own creation cannot overwrite an edited target, arguments, folder or user title',async()=>{
  for(const change of ['target','args','folder','title']){
    const f=fixture({nativeDefaults:true,returnedId:4088526249}),p=plan();f.setPlan(p);const setter=f.Apps.SetShortcutName;f.Apps.SetShortcutName=()=>{throw Error('setter rejected');};
    await assert.rejects(f.api.__applyPlan(p,()=>true));f.Apps.SetShortcutName=setter;
    if(change==='target')f.native.get(4088526249).strShortcutExe='C:\\Foreign\\other.exe';
    if(change==='args')f.native.get(4088526249).strShortcutLaunchOptions='--user-args';
    if(change==='folder')f.native.get(4088526249).strShortcutStartDir='D:\\Foreign';
    if(change==='title')f.existing.get(4088526249).display_name='User title';
    await assert.rejects(f.api.__applyPlan(p,()=>true),/unconfirmed/);assert.equal(f.sets.length,0);assert.equal(f.acks,0);assert.equal(f.adds,1);
  }
});

test('installed-only collection removes only a verified own uninstalled wrapper and preserves every other app',async()=>{
  const f=fixture(),r=row();f.seed([r]);f.setPlan(r);f.existing.set(appId,{appid:appId});
  const collection=f.context.collectionStore.NewUnsavedCollection('Epic Games',undefined,[{appid:appId},{appid:42}]);await collection.Save();
  await f.api.__reconcile('epic',[r],()=>true);
  assert.deepEqual(collection.allApps.map(app=>app.appid),[42]);assert.equal(f.adds,0);
  assert.equal(f.api.readStoreLibrary().get(appId).appId,appId);
});

test('collection reconciliation refuses a manually edited shortcut and adds only an installed exact wrapper',async()=>{
  const f=fixture(),r=row();f.seed([r]);f.setPlan(r);f.existing.set(appId,{appid:appId});
  const collection=f.context.collectionStore.NewUnsavedCollection('Epic Games',undefined,[{appid:appId},{appid:42}]);await collection.Save();
  f.Apps.RegisterForAppDetails=(id,cb)=>{cb({...details(r,id),strShortcutLaunchOptions:'--manual'});return{unregister(){}};};
  await f.api.__reconcile('epic',[r],()=>true);assert.deepEqual(collection.allApps.map(app=>app.appid),[appId,42]);
  const g=fixture(),installed={...row(),game:{...row().game,isInstalled:true,isLaunchable:true}};g.seed([installed]);g.setPlan(installed);g.existing.set(appId,{appid:appId});
  await g.api.__reconcile('epic',[installed],()=>true);
  assert.deepEqual(g.collectionMap.get('Epic Games').allApps.map(app=>app.appid),[appId]);assert.equal(g.adds,0);
});

test('native uninstalled game action opens the exact game portal intent without a generic path dialog',()=>{
  const f=fixture();f.seed([row()]);f.setAuto(false);const ui=component(f);ui.render();f.emit(appId,details(row()));
  const rendered=ui.render();rendered.props.children[0].props.onClick();
  assert.deepEqual(f.calls.at(-1),{method:'portal',args:{key:row().key,action:'install'}});
});

if(!process.env.STORE_FIX_BEFORE){
test('live exact wrapper/args/startdir enables custom action; any changed or unknown field revokes it',()=>{
  const f=fixture();f.seed([row()]);f.setAuto(false);const ui=component(f),native={kind:'native'};ui.render(appId,native);
  for(const changed of [null,{unAppID:99},{strShortcutExe:'foreign.exe'},{strShortcutLaunchOptions:'--store-library gog other'},{strShortcutStartDir:'D:\\Other'},{strShortcutStartDir:undefined}]){
    f.emit(appId,details(row()));assert.notEqual(ui.render(appId,native),native);
    f.emit(appId,changed===null?null:{...details(row()),...changed});assert.equal(ui.render(appId,native),native);
  }
});
test('two AppIDs never reuse the first render proof, and late callbacks after unmount do not publish',()=>{
  const f=fixture();f.seed([row(),row(appId+1)]);f.setAuto(false);const ui=component(f),native={kind:'native'};
  ui.render(appId,native);f.emit(appId,details(row()));assert.notEqual(ui.render(appId,native),native);
  assert.equal(ui.render(appId+1,native),native);f.emit(appId,details(row()),true);assert.equal(ui.render(appId+1,native),native);
  f.emit(appId+1,details(row(appId+1)));assert.notEqual(ui.render(appId+1,native),native);
  ui.unmount();const count=ui.sets;f.emit(appId+1,details(row(appId+1)),true);assert.equal(ui.sets,count);
});
test('account replacement, missing explicit startdir and API context replacement cannot reuse UI proof',()=>{
  const f=fixture();f.seed([row()]);f.setAuto(false);const ui=component(f),native={kind:'native'};ui.render(appId,native);f.emit(appId,details(row()));assert.notEqual(ui.render(appId,native),native);
  f.seed([{...row(),game:{...row().game,accountRevision:8}}]);assert.equal(ui.render(appId,native),native);
  f.emit(appId,details(row()));assert.notEqual(ui.render(appId,native),native);
  f.context.SteamClient.Apps={...f.Apps};assert.equal(ui.render(appId,native),native);f.emit(appId,details(row()));assert.notEqual(ui.render(appId,native),native);
  f.seed([{...row(),startDirectory:undefined}]);assert.equal(ui.render(appId,native),native);
});
test('any currently running Steam overview remains native, including secondary running apps',()=>{
  const f=fixture();f.seed([row()]);f.setAuto(false);const ui=component(f),native={kind:'native'};ui.render(appId,native);f.emit(appId,details(row()));assert.notEqual(ui.render(appId,native),native);
  f.context.SteamUIStore.MainRunningAppID=10;f.context.SteamUIStore.RunningApps=[{appid:10},{appid:appId}];assert.equal(ui.render(appId,native),native);
});
test('missing initial AppDetails proof can retry only an actual own creation in the same current context',async()=>{
  const f=fixture(),p=plan();f.setPlan(p);f.setAuto(false);
  const first=f.api.__applyPlan(p,()=>true);for(let i=0;i<10&&!f.subscriptions.length;i++)await Promise.resolve();f.emit(activity.shortcutAppId('"'+p.executable+'"',p.title),null);await assert.rejects(first,/steam_shortcut_unconfirmed/);
  f.setAuto(true);await f.api.__applyPlan(p,()=>true);assert.equal(f.adds,1);assert.equal(f.acks,1);
});
test('unknown preexisting CRC is never adopted, even if its details would match',async()=>{
  const f=fixture(),p=plan();f.setPlan(p);const id=activity.shortcutAppId('"'+p.executable+'"',p.title);f.existing.set(id,{appid:id});
  await assert.rejects(f.api.__applyPlan(p,()=>true),/steam_shortcut_conflict/);assert.equal(f.adds,0);assert.equal(f.acks,0);assert.equal(f.subscriptions.length,0);
});
test('same claim cannot change request/key/account/title/executable/arguments/directory to reuse its created receipt',async()=>{
  for(const change of [{requestId:'new-request'},{key:{store:'gog',id:'game'}},{key:{store:'epic',id:'other'}},{accountRevision:8},{title:'New title'},{executable:'D:\\Elsewhere\\Playhub.GameSession.exe'},{arguments:'--store-library gog game'},{startDirectory:'D:\\Elsewhere'}]){
    const f=fixture({failAcks:1}),p=plan();f.setPlan(p);await assert.rejects(f.api.__applyPlan(p,()=>true));
    await assert.rejects(f.api.__applyPlan({...p,...change},()=>true),/steam_shortcut_identity_changed/);assert.equal(f.adds,1);assert.equal(f.acks,1);
  }
});
test('new claim, expired receipt, reversed clock and replaced API context cannot acknowledge an old unknown creation',async()=>{
  for(const change of ['claim','expiry','clock','context']){
    const f=fixture({failAcks:1}),p=plan();f.setPlan(p);await assert.rejects(f.api.__applyPlan(p,()=>true));
    if(change==='expiry')f.setNow(100000+20*60*1000+1);if(change==='clock')f.setNow(99999);if(change==='context')f.context.SteamClient.Apps={...f.Apps};
    await assert.rejects(f.api.__applyPlan(change==='claim'?{...p,claimId:'new-claim'}:p,()=>true),/steam_shortcut_conflict/);assert.equal(f.adds,1);assert.equal(f.acks,1);
  }
});
test('same live JS context can retry its pending receipt after module reload, but never after context replacement',async()=>{
  const f=fixture({failAcks:1}),p=plan();f.setPlan(p);await assert.rejects(f.api.__applyPlan(p,()=>true));f.loadRuntime();await f.api.__applyPlan(p,()=>true);assert.equal(f.adds,1);assert.equal(f.acks,2);
});
test('unload during AddShortcut records only provenance, never sends an old-owner ACK',async()=>{
  let current=true;const f=fixture({onAdd(){current=false;}}),p=plan();f.setPlan(p);await f.api.__applyPlan(p,()=>current);assert.equal(f.acks,0);current=true;await f.api.__applyPlan(p,()=>current);assert.equal(f.adds,1);assert.equal(f.acks,1);
});
test('edited creation readback refuses ACK and context drift before Add refuses new mutation',async()=>{
  const f=fixture({failAcks:1}),p=plan();f.setPlan(p);await assert.rejects(f.api.__applyPlan(p,()=>true));f.Apps.RegisterForAppDetails=(id,cb)=>{cb({...details(p,id),strShortcutLaunchOptions:'--foreign'});return{unregister(){}};};
  await assert.rejects(f.api.__applyPlan(p,()=>true),/steam_shortcut_unconfirmed/);assert.equal(f.acks,1);
  const g=fixture();g.store.GetAppOverviewByAppID=()=>{g.context.SteamClient.Apps={...g.Apps};return undefined;};await g.api.__applyPlan(p,()=>true);assert.equal(g.adds,0);
});
test('runtime stop clears published owned/job views and late old stop cannot clear a new runtime',async()=>{
  const f=fixture();const old=f.api.installStoreLibraryRuntime();for(let i=0;i<60;i++)await Promise.resolve();assert.equal(f.api.readStoreLibrary().size,1);
  old();assert.equal(f.api.readStoreLibrary().size,0);assert.equal(f.api.readStoreDownloads().length,0);
  const next=f.api.installStoreLibraryRuntime();for(let i=0;i<60;i++)await Promise.resolve();old();assert.equal(f.api.readStoreLibrary().size,1);next();
});
}


test('all native finish APIs are checked before creating any temporary default shortcut',async()=>{
  for(const method of ['RegisterForAppDetails','AddShortcut','SetShortcutName','SetShortcutExe','SetShortcutStartDir','SetShortcutLaunchOptions']){
    const f=fixture({nativeDefaults:true});delete f.Apps[method];
    await assert.rejects(f.api.__applyPlan(plan(),()=>true),/configuration_unavailable/);assert.equal(f.adds,0);
  }
  const f=fixture({nativeDefaults:true});delete f.context.collectionStore;
  await assert.rejects(f.api.__applyPlan(plan(),()=>true),/steam_collections_unavailable/);assert.equal(f.adds,0);
});

test('an unconfirmed creation blocks every later plan including polls during its retry backoff',async()=>{
  const f=fixture({nativeDefaults:true,failDrafts:1}),p=plan(),second={...plan(),requestId:'request-2',claimId:'claim-2',title:'Second',key:{store:'epic',id:'second'},arguments:'--store-library epic second'},deferred=new Map();
  await f.api.__applyPendingPlans([p,second],()=>true,deferred);assert.equal(f.adds,1);assert.equal(f.acks,0);
  await f.api.__applyPendingPlans([p,second],()=>true,deferred);assert.equal(f.adds,1);assert.equal(f.acks,0);
  f.setNow(131000);await f.api.__applyPendingPlans([p,second],()=>true,deferred);assert.equal(f.adds,2);assert.equal(f.acks,2);
  assert.ok([...f.existing.values()].every(row=>row.display_name!=='Playhub.GameSession'));
});

const nativeIdentity=(store='xbox')=>({key:{store,id:store==='xbox'?'9NBLGGH1ZB7X':store+'-game'},accountRevision:7,nativeApplicationId:store==='xbox'?'Fixture.Game_abcdefghijklm!App':null,manifestExecutable:store==='xbox'?'Game.exe':null,installPath:'F:\\Games\\Fixture',runtimeExecutable:exe,startDirectory:dir,gameExecutablePath:'F:\\Games\\Fixture\\Game.exe',epicNamespace:'namespace',epicCatalogItemId:'catalog'});
function importFixture(identity,options={}){let adopted;const f=fixture({nativeDefaults:true,rpc(method,args){if(method==='steam-adopt-import'){adopted=args;if(options.reject)throw Error('identity changed');return{accepted:true,owned:{...args,key:identity.key,accountCurrent:true,game:{accountRevision:7,isInstalled:true,isLaunchable:true},nativeApplicationId:identity.nativeApplicationId,manifestExecutable:identity.manifestExecutable}};}}});f.store.allApps=f.existing;f.store.m_mapAppOverview=f.existing;const id=3100000001,args=identity.key.store==='xbox'?f.nativeImports.nativeUwpArguments(identity):identity.key.store==='epic'?`--epic "namespace" "catalog" "${identity.key.id}" "${identity.gameExecutablePath}"`:`--game "${identity.gameExecutablePath}" --preserve-user-arguments`;
 f.existing.set(id,{appid:id,app_type:1073741824,display_name:'User curated title',artwork:'custom'});f.native.set(id,{unAppID:id,strShortcutExe:'"'+exe+'"',strShortcutLaunchOptions:args,strShortcutStartDir:'"'+dir+'\\"'});const index=f.nativeImports.createNativeImportIndex(f.Apps,f.store);index.observe();return{f,index,id,get adopted(){return adopted;}};}

test('exact Import Games identities adopt the original AppID and user title without AddShortcut or metadata setters',async()=>{
 for(const store of ['xbox','gog','epic','amazon']){const identity=nativeIdentity(store),probe=importFixture(identity),{f,index,id}=probe;try{assert.equal(index.ready(),true);const allowed=await f.api.__reconcileNative(index,[identity],[],()=>true);assert.equal(allowed.size,1);assert.equal(probe.adopted.appId,id);assert.equal(probe.adopted.title,'User curated title');assert.equal(f.adds,0);assert.equal(f.sets.length,0);assert.equal(f.existing.get(id).artwork,'custom');assert.ok(f.collectionMap.get(store==='xbox'?'Xbox Store':store==='gog'?'GOG':store==='epic'?'Epic Games':'Amazon Games'));}finally{index.close();}}
});

test('wrong or ambiguous original identity and refused adoption never authorize another shortcut',async()=>{
 for(const scenario of ['ambiguous','rejected','unknown','wrong-aumid','wrong-target']){const identity=nativeIdentity(),probe=importFixture(identity,{reject:scenario==='rejected'}),{f,index,id}=probe;try{
  if(scenario==='ambiguous'){f.existing.set(id+1,{...f.existing.get(id),appid:id+1});f.native.set(id+1,{...f.native.get(id),unAppID:id+1});index.observe();}
  if(scenario==='unknown')f.emit(id,{});
  if(scenario==='wrong-aumid')f.emit(id,{...f.native.get(id),strShortcutLaunchOptions:'--uwp "Other!App" "Game.exe" ""'});
  if(scenario==='wrong-target')f.emit(id,{...f.native.get(id),strShortcutExe:'C:\\Foreign\\Playhub.GameSession.exe'});
  const old={...row(id),key:identity.key,game:{accountRevision:7,isInstalled:true,isLaunchable:true}};
  const allowed=await f.api.__reconcileNative(index,[identity],[old],()=>true);assert.equal(allowed.size,0,scenario);assert.equal(f.adds,0);assert.equal(f.sets.length,0);assert.equal(f.acks,0);
 }finally{index.close();}}
});

test('the one retained exact Xbox DS wrapper converts only its arguments and keeps its AppID and artwork',async()=>{
 const identity=nativeIdentity(),probe=importFixture(identity),{f,index,id}=probe,old={...row(id),key:identity.key,arguments:'--store-library xbox '+identity.key.id,game:{accountRevision:7,isInstalled:true,isLaunchable:true}};
 f.native.get(id).strShortcutLaunchOptions=old.arguments;f.emit(id,f.native.get(id));try{const allowed=await f.api.__reconcileNative(index,[identity],[old],()=>true);assert.equal(allowed.size,1);assert.deepEqual(f.sets.map(row=>row.method),['SetShortcutLaunchOptions']);assert.equal(f.sets[0].id,id);assert.equal(probe.adopted.expectedOwnedAppId,id);assert.equal(probe.adopted.arguments,f.nativeImports.nativeUwpArguments(identity));assert.equal(f.existing.get(id).artwork,'custom');assert.equal(f.adds,0);}finally{index.close();}
});

test('native identity index blocks blank hydration and late callbacks after disposal',()=>{
 const f=fixture({nativeDefaults:true});f.store.allApps=f.existing;const index=f.nativeImports.createNativeImportIndex(f.Apps,f.store);index.observe();assert.equal(index.ready(),false);const identity=nativeIdentity();const args=f.nativeImports.nativeUwpArguments(identity);assert.deepEqual(JSON.parse(JSON.stringify(f.nativeImports.windowsArguments(args))),['--uwp',identity.nativeApplicationId,identity.manifestExecutable,'']);assert.equal(f.nativeImports.windowsArguments('--uwp "unterminated'),null);index.close();assert.equal(index.ready(),false);
});

test('uninstall removes only its exact current owned shortcut and forgets ownership after native absence',async()=>{
 const r=row(),f=fixture({rpc(method){if(method==='steam-owned')return[r];}});f.setPlan(r);f.existing.set(appId,{appid:appId,display_name:'Fixture'});const removals=[];f.Apps.RemoveShortcut=id=>{removals.push(id);f.existing.delete(id);};
 await f.api.__removeUninstalled([r],()=>true);assert.deepEqual(removals,[appId]);assert.equal(f.calls.filter(call=>call.method==='steam-forget-uninstalled').length,1);assert.equal(f.calls.at(-1).args.appId,appId);assert.equal(f.adds,0);
});

test('manual edits, reinstallation, account/context changes and unknown states never remove a native shortcut',async()=>{
 for(const scenario of ['edited','reinstalled','disconnected','unknown','running-job','context']){const r=row();let current=true;const f=fixture({rpc(method){if(method==='steam-owned'){if(scenario==='context')current=false;return[{...r,game:{...r.game,isInstalled:scenario==='reinstalled'}}];}}});f.setPlan(r);f.existing.set(appId,{appid:appId});const removals=[];f.Apps.RemoveShortcut=id=>removals.push(id);
  if(scenario==='edited')f.Apps.RegisterForAppDetails=(id,cb)=>{cb({...details(r,id),strShortcutLaunchOptions:'--user-edited'});return{unregister(){}};};
  const candidate=scenario==='disconnected'?{...r,accountCurrent:false}:scenario==='unknown'?{...r,game:null}:scenario==='running-job'?{...r,job:{state:'downloading'}}:r;
  await f.api.__removeUninstalled([candidate],()=>current);assert.equal(removals.length,0,scenario);assert.equal(f.calls.some(call=>call.method==='steam-forget-uninstalled'),false,scenario);
 }
});

test('a missing owned native shortcut is forgotten only for a confirmed uninstalled game',async()=>{
 const r=row(),f=fixture();f.Apps.RemoveShortcut=()=>assert.fail('no native removal for absent ID');await f.api.__removeUninstalled([r],()=>true);assert.equal(f.calls.at(-1).method,'steam-forget-uninstalled');assert.equal(f.calls.at(-1).args.appId,appId);
});

test('fresh repair of a missing installed link retires only the expected old AppID and waits for normal sync',async()=>{
 const identity=nativeIdentity('epic'),old={...row(),key:identity.key,game:{accountRevision:7,isInstalled:true,isLaunchable:true}},f=fixture({rpc(method){if(method==='steam-native-identities')return[identity];if(method==='steam-owned')return[old];if(method==='steam-repair-missing')return{requestId:'repair-1',store:'epic',accountRevision:7,state:'pending'};}});
 f.store.allApps=f.existing;f.existing.set(1,{appid:1,app_type:1,display_name:'Existing Steam game'});const stop=f.api.installStoreLibraryRuntime();try{for(let i=0;i<150;i++)await Promise.resolve();assert.equal(f.api.readStoreSteamLink(identity.key),'absent');assert.equal(await f.api.repairStoreSteamShortcut(identity.key),true);const call=f.calls.find(call=>call.method==='steam-repair-missing');assert.equal(call.args.expectedOwnedAppId,appId);assert.equal(call.args.expectedAccountRevision,7);assert.equal(f.api.readStoreSteamLink(identity.key),'unknown');assert.equal(f.adds,0);}finally{stop();}
});

test('an original appearing during repair metadata prevents retiring ownership or creating a duplicate',async()=>{
 const identity=nativeIdentity(),old={...row(),key:identity.key,game:{accountRevision:7,isInstalled:true,isLaunchable:true}};let reads=0;
 const f=fixture({nativeDefaults:true,rpc(method){if(method==='steam-native-identities'){if(++reads===2){f.existing.set(3100000001,{appid:3100000001,app_type:1073741824,display_name:'Original'});f.native.set(3100000001,{unAppID:3100000001,strShortcutExe:exe,strShortcutLaunchOptions:f.nativeImports.nativeUwpArguments(identity),strShortcutStartDir:dir});}return[identity];}if(method==='steam-owned')return[old];}});
 f.store.allApps=f.existing;f.existing.set(1,{appid:1,app_type:1,display_name:'Existing Steam game'});const stop=f.api.installStoreLibraryRuntime();try{for(let i=0;i<150;i++)await Promise.resolve();assert.equal(f.api.readStoreSteamLink(identity.key),'absent');assert.equal(await f.api.repairStoreSteamShortcut(identity.key),false);assert.equal(f.calls.some(call=>call.method==='steam-repair-missing'),false);assert.equal(f.adds,0);}finally{stop();}
});

test('cancel remains available without cleanup capability and sends only the latest exact job revision without old control fallback',async()=>{
 const j={jobId:'job',key:{store:'epic',id:'game'},accountRevision:7,installPath:'F:\\Games\\Fixture',title:'Fixture',state:'failed',removalKind:'cleanup',revision:8,downloadedBytes:10,canPause:false,canResume:false,canCancel:true};let ready=false;const f=fixture({rpc(method,args){if(method==='downloads')return[{...j,canRemove:ready}];if(method==='cancel-cleanup')return{accepted:false,errorCode:'partial_cleanup_ownership_lost'};}});
 assert.equal((await f.api.storeJobAction(j,'cancel')).accepted,false);assert.equal(f.calls.filter(call=>call.method==='cancel-cleanup').length,1,'missing cleanup capability cannot hide the cancellation command');ready=true;const result=await f.api.storeJobAction({...j,revision:2},'cancel');assert.equal(result.accepted,false);assert.equal(result.errorCode,'partial_cleanup_ownership_lost');const call=f.calls.find(call=>call.method==='cancel-cleanup');assert.equal(call.args.expectedRevision,8);assert.equal(call.args.action,undefined);assert.equal(f.calls.some(call=>call.method==='control'),false);
});

test('an unavailable native identity cannot starve a verified later store plan before any creation starts',async()=>{
 const f=fixture({nativeDefaults:true}),first=plan(),second={...plan(),claimId:'second',key:{store:'xbox',id:'known'},arguments:'--uwp "Known!App" "Game.exe" ""'};await f.api.__applyPendingPlans([first,second],()=>true,new Map(),p=>p.key.store==='xbox');assert.equal(f.adds,1);assert.equal(f.acks,1);assert.equal(f.calls.find(call=>call.method==='steam-sync-ack').args.claimId,'second');
});

test('only explicit Game Pass provenance selects its separate collection before ACK and membership cannot steal a purchased entry',async()=>{
 const f=fixture(),p={...plan(),key:{store:'xbox',id:'game'},libraryKind:'gamePass'};f.setPlan(p);await f.api.__applyPlan(p,()=>true);assert.ok(f.collectionMap.get('Xbox Game Pass'));assert.equal(f.collectionMap.has('Xbox Store'),false);assert.equal(f.acks,1);
 const r={...row(),key:{store:'xbox',id:'game'},game:{...row().game,isInstalled:true,libraryKind:'store'}};const g=fixture();g.seed([r]);g.setPlan(r);g.existing.set(appId,{appid:appId});await g.api.__reconcile('xbox',[r],()=>true,'gamePass');assert.equal(g.collectionMap.has('Xbox Game Pass'),false);await g.api.__reconcile('xbox',[r],()=>true);assert.ok(g.collectionMap.get('Xbox Store'));
});

test('the actual global runtime reports migration only after its native adoption and collection pass',async()=>{
 const identity=nativeIdentity('epic'),r={...row(),key:identity.key,arguments:'--game "'+identity.gameExecutablePath+'"',game:{accountRevision:7,isInstalled:true,isLaunchable:true}};
 const request={requestId:'01234567-89ab-cdef-0123-456789abcdef',version:'2.7.0:store-collections-v2',steamAccountId:'76561198000000001',state:'pending',confirmed:0,total:0};
 const caps={installKind:'direct',canConnect:true,canRefresh:true,canPause:false,canResume:false,canCancel:false};const snapshot={revision:1,observedAt:'2026-11-15T12:00:00Z',accounts:[{store:'epic',state:'connected',revision:7,capabilities:caps}],games:[{key:identity.key,title:'Fixture',accountRevision:7,capabilities:caps,isInstalled:true,isLaunchable:true,installPath:identity.installPath}],jobs:[]};
 const f=fixture({rpc(method,args){if(method==='steam-native-identities')return[identity];if(method==='steam-owned')return[r];if(method==='snapshot')return snapshot;if(method==='refresh')return{accepted:true,snapshot};if(method==='steam-adopt-import')return{accepted:true,owned:r};if(method==='steam-migration-pending')return[request];if(method==='steam-migration-report')return{...request,...args,state:'completed'};}});f.store.allApps=f.existing;f.setPlan(r);f.existing.set(appId,{appid:appId,app_type:1073741824,display_name:'Fixture'});const stop=f.api.installStoreLibraryRuntime();try{for(let i=0;i<500;i++)await Promise.resolve();f.intervals[0]();for(let i=0;i<500;i++)await Promise.resolve();const report=f.calls.find(call=>call.method==='steam-migration-report');assert.ok(report);assert.equal(report.args.complete,true);assert.equal(report.args.confirmed,1);assert.ok(f.collectionMap.get('Epic Games'));assert.equal(f.adds,0);}finally{stop();}
});

test('a pending migration refresh longer than the Host activity lifetime keeps the actual runtime heartbeat running',async()=>{
 const identity=nativeIdentity('epic'),r={...row(),key:identity.key,arguments:'--game "'+identity.gameExecutablePath+'"',game:{accountRevision:7,isInstalled:true,isLaunchable:true}};
 const request={requestId:'31234567-89ab-cdef-0123-456789abcdef',version:'2.7.0:store-collections-v4',steamAccountId:'76561198000000001',state:'pending',confirmed:0,total:0};
 const caps={installKind:'direct',canConnect:true,canRefresh:true,canPause:false,canResume:false,canCancel:false};let discovered=false,release;
 const held=new Promise(resolve=>{release=resolve;});
 const snapshot=()=>({revision:discovered?2:1,observedAt:'2026-11-15T12:00:00Z',accounts:[{store:'epic',state:'connected',revision:7,capabilities:caps}],games:[{key:identity.key,title:'Fixture',accountRevision:7,capabilities:caps,isInstalled:discovered,isLaunchable:discovered,installPath:discovered?identity.installPath:null}],jobs:[]});
 const f=fixture({rpc(method){if(method==='steam-native-identities')return discovered?[identity]:[];if(method==='steam-owned')return discovered?[r]:[];if(method==='snapshot')return snapshot();if(method==='refresh')return held;if(method==='steam-adopt-import')return{accepted:true,owned:r};if(method==='steam-migration-pending')return[request];if(method==='steam-migration-report')return{...request,state:'completed'};}});
 f.store.allApps=f.existing;f.setPlan(r);f.existing.set(appId,{appid:appId,app_type:1073741824,display_name:'Fixture'});
 const drain=async()=>{for(let i=0;i<500;i++)await Promise.resolve();},stop=f.api.installStoreLibraryRuntime();
 try{
  await drain();assert.equal(f.calls.filter(call=>call.method==='refresh').length,1);
  // Host ActivityLifetime is 15 seconds. Advance 20 seconds while the actual
  // refresh promise remains unresolved; no real clock, network or Steam input.
  for(let i=1;i<=10;i++){f.setNow(100000+i*2000);f.intervals[0]();await drain();}
  const activity=f.calls.filter(call=>call.method==='steam-activity');
  assert.ok(activity.length>=11,'pending metadata IO must not retain the runtime busy lock');
  assert.ok(Date.parse(activity.at(-1).args.observedAt)-Date.parse(activity[0].args.observedAt)>15000);
  assert.equal(f.calls.filter(call=>call.method==='refresh').length,1,'all heartbeat polls reuse one inventory flight');
  assert.equal(f.calls.some(call=>call.method==='steam-migration-report'),false,'no completion before inventory ACK');
  discovered=true;release({accepted:true,snapshot:snapshot()});await drain();f.intervals[0]();await drain();
  const report=f.calls.find(call=>call.method==='steam-migration-report');assert.ok(report);assert.equal(report.args.total,1);assert.equal(report.args.confirmed,1);assert.equal(report.args.complete,true);
 }finally{release({accepted:false,snapshot:snapshot()});stop();}
});

test('a removal-kind change during review cannot turn metadata-only removal into file cleanup',async()=>{
 const j={jobId:'job',key:{store:'epic',id:'game'},accountRevision:7,installPath:'F:\\Games\\Fixture',title:'Fixture',state:'failed',revision:8,downloadedBytes:10,canPause:false,canResume:false,canCancel:false,canRemove:true,removalKind:'history'};
 for(const kind of ['cleanup','windowsRequest',null]){const f=fixture({rpc(method){if(method==='downloads')return[{...j,removalKind:kind}];if(method==='cancel-cleanup')return{accepted:true};}});assert.equal((await f.api.storeJobAction(j,'cancel')).accepted,true);assert.equal(f.calls.find(call=>call.method==='cancel-cleanup').args.deletePartial,false,'a metadata-only review can never authorize file deletion');}
});


test('different store games keep the exact original title when Steam CRC identities collide',async()=>{
 const p=plan(),foreignId=activity.shortcutAppId('"'+p.executable+'"',p.title);
 const other={...row(),appId:foreignId,key:{store:'gog',id:'other'},title:p.title};
 const f=fixture({nativeDefaults:true,rpc(method){if(method==='steam-owned')return[other];}});f.setPlan(p);
 f.existing.set(foreignId,{appid:foreignId,display_name:p.title,artwork:'preserved'});
 const foreignProof={unAppID:foreignId,strShortcutExe:exe,strShortcutLaunchOptions:'--store-library gog other',strShortcutStartDir:dir};f.native.set(foreignId,{...foreignProof});
 await f.api.__applyPlan(p,()=>true);
 const added=f.calls.find(call=>call.method==='steam-sync-ack').args.appId;
 assert.notEqual(added,foreignId);assert.equal(f.existing.get(added).display_name,p.title);assert.equal(f.existing.get(foreignId).artwork,'preserved');assert.deepEqual(f.native.get(foreignId),foreignProof);
 assert.equal(f.adds,1);assert.equal(f.acks,1);
});

test('generated store suffix is removed from a proven owned shortcut without changing AppID or artwork',async()=>{
 const identity=nativeIdentity('gog');const {f,index,id}=importFixture(identity);
 f.existing.get(id).display_name='DREDGE (Gog)';
 const proof=f.native.get(id),owned={...row(),appId:id,key:identity.key,title:'DREDGE (Gog)',executable:proof.strShortcutExe,arguments:proof.strShortcutLaunchOptions,startDirectory:proof.strShortcutStartDir,game:{accountRevision:7,title:'DREDGE',isInstalled:true,isLaunchable:true}};
 const allowed=await f.api.__reconcileNative(index,[identity],[owned],()=>true);
 assert.equal(f.existing.get(id).display_name,'DREDGE');assert.equal(f.existing.get(id).artwork,'custom');assert.equal(f.adds,0);assert.ok(allowed.has(f.nativeImports.nativeStoreKey(identity)));
 assert.equal(f.calls.find(call=>call.method==='steam-adopt-import').args.title,'DREDGE');index.close();
});

// A dead optional achievement worker must never starve the next native import.
test('optional authoritative read/provider warmup has a finite deadline and isolates failures',async()=>{
 const f=fixture(),delays=[],oldTimeout=f.context.setTimeout;
 f.context.setTimeout=(fn,ms)=>{delays.push(ms);return oldTimeout(fn,1);};
 await f.api.__warmImportedAchievements(()=>new Promise(()=>{}));
 assert.deepEqual(delays,[8000]);
 await f.api.__warmImportedAchievements(async()=>{throw Error('provider offline');});
 let completed=false;await f.api.__warmImportedAchievements(async()=>{completed=true;});assert.equal(completed,true);
});
