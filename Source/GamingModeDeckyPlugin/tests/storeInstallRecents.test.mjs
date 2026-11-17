import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createRequire} from 'node:module';
import test from 'node:test';
import vm from 'node:vm';
const require=createRequire(import.meta.url),ts=require('typescript');
const React=require('C:/Users/Andrea/AppData/Local/Temp/Playhub-AllAccess-Mounted-Independent-20261114/node_modules/react');
const compile=s=>ts.transpileModule(s,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React}}).outputText;
const moduleSource=fs.readFileSync(new URL('../src/storeInstallRecents.tsx',import.meta.url),'utf8');
function fixture(){
 const disk=new Map(),listeners=new Set(),intervals=new Set(),probes=new Map(),overviews=new Map(),rows=new Map();let jobs=[],account='76561198000000001',route,removed=0;
 const HookReact={...React,useSyncExternalStore:(_subscribe,snapshot)=>snapshot()};
 const decky={SP_REACT:HookReact,DFL:{findModuleChild:fn=>fn({account:{logged_in:true,steamid:account}})},routerHook:{addPatch(path,fn){assert.equal(path,'/library/home');route=fn;return fn;},removePatch(path,fn){assert.equal(path,'/library/home');assert.equal(fn,route);removed++;}}};
 const output={};vm.runInNewContext(compile(fs.readFileSync(new URL('../src/reactOutputPatch.ts',import.meta.url),'utf8')),{exports:output,require:()=>decky,Symbol,WeakMap});
 const context=vm.createContext({exports:{},Symbol,Map,Set,Date,Number,JSON,appStore:{GetAppOverviewByAppID:id=>overviews.get(id)},localStorage:{getItem:key=>disk.get(key)??null,setItem:(key,value)=>disk.set(key,value)},setInterval:fn=>{intervals.add(fn);return fn;},clearInterval:fn=>intervals.delete(fn),require:name=>name==='./decky'?decky:output});
 vm.runInContext(compile(moduleSource),context);
 const dependencies={rows:()=>rows,jobs:()=>jobs,identity:row=>row?JSON.stringify([row.appId,row.key.store,row.key.id,row.game.accountRevision]):undefined,subscribe:fn=>{listeners.add(fn);return()=>listeners.delete(fn);},observe(row,cb){let active=true;probes.set(row.appId,{emit:value=>active&&cb(value),get active(){return active;}});cb(false);return()=>{active=false;};}};
 const seed=(id,store='xbox',secondsAgo=20,revision=1)=>{const row={appId:id,key:{store,id:'game-'+id},accountCurrent:true,game:{isInstalled:true,isLaunchable:true,accountRevision:revision},job:{jobId:'job-'+id,key:{store,id:'game-'+id},state:'completed',accountRevision:revision,updatedAt:new Date(Date.now()-secondsAgo*1000).toISOString()}};rows.set(id,row);overviews.set(id,{appid:id,rt_recent_activity_time:0,rt_last_time_played:0,minutes_playtime_forever:0,BIsAppRunning:false});return row;};
 return{api:context.exports,context,dependencies,rows,overviews,probes,disk,intervals,seed,notify(){for(const fn of listeners)fn();},setAccount(v){account=v;},setJobs(v){jobs=v;},route:props=>route(props),removed:()=>removed,listeners};
}
const plain=value=>JSON.parse(JSON.stringify(value));
const entry=(appId,installedAt)=>({appId,identity:'identity-'+appId,event:'event-'+appId,installedAt});
test('new verified mixed-store installs precede idle native recents; later actual gameplay becomes first',()=>{
 const f=fixture(),now=Date.now()/1000;for(const id of[1,2,3,4])f.seed(id);
 f.overviews.get(1).rt_last_time_played=now-100;
 const ids=Object.freeze([1,2]),before=JSON.stringify([...f.overviews]);
 assert.deepEqual(plain(f.api.projectStoreInstallRecents(ids,[entry(3,now-20),entry(4,now-10)],id=>f.overviews.get(id))),[4,3,1,2]);
 assert.equal(JSON.stringify([...f.overviews]),before);assert.deepEqual(ids,[1,2]);
 f.overviews.get(1).rt_last_time_played=now+10;
 assert.deepEqual(plain(f.api.projectStoreInstallRecents(ids,[entry(3,now-20),entry(4,now-10)],id=>f.overviews.get(id))),[1,4,3,2]);
});
test('idle last-played sentinel cannot pin a stale game; a truly running game keeps its native pair',()=>{
 const f=fixture(),now=Date.now()/1000;for(const id of[1,2,3])f.seed(id);
 const installs=[entry(3,now)];
 assert.deepEqual(plain(f.api.projectStoreInstallRecents([1,0,2],installs,id=>f.overviews.get(id))),[3,1,2]);
 f.overviews.get(1).BIsAppRunning=true;
 assert.deepEqual(plain(f.api.projectStoreInstallRecents([1,0,2],installs,id=>f.overviews.get(id))),[1,0,3,2]);
});
test('hidden completed history bootstraps from authoritative owned.job only after native identity proof',()=>{
 const f=fixture();f.seed(11,'xbox',60);f.seed(12,'epic',40);f.seed(13,'gog',20);
 const stop=f.api.installStoreInstallRecents(f.dependencies),bridge=vm.runInContext("globalThis[Symbol.for('playhub.store.install-recents.v1')]",f.context);
 assert.deepEqual(plain(bridge.read()),[]);assert.deepEqual(plain(bridge.project([99])),[99]);
 for(const id of[11,12,13])f.probes.get(id).emit(true);
 assert.deepEqual(plain(bridge.project([99])),[13,12,11,99]);
 const saved=[...f.disk.values()][0];assert.equal(JSON.parse(saved).entries.length,3);
 stop();assert.equal(f.listeners.size,0);assert.equal(f.intervals.size,0);assert.equal(f.removed(),1);assert.equal(vm.runInContext("globalThis[Symbol.for('playhub.store.install-recents.v1')]",f.context),undefined);
 const stop2=f.api.installStoreInstallRecents(f.dependencies);for(const id of[11,12,13])f.probes.get(id).emit(true);
 assert.equal([...f.disk.values()][0],saved,'reload must not re-date or duplicate install events');stop2();
});
test('uninstalled, wrong-account and changed native shortcut identities stop promotions and cannot replay old completion',()=>{
 const f=fixture(),row=f.seed(22);const stop=f.api.installStoreInstallRecents(f.dependencies),bridge=vm.runInContext("globalThis[Symbol.for('playhub.store.install-recents.v1')]",f.context);
 f.probes.get(22).emit(true);assert.deepEqual(plain(bridge.project([99])),[22,99]);
 f.probes.get(22).emit(false);assert.deepEqual(plain(bridge.project([99])),[99]);
 f.probes.get(22).emit(true);row.game.isInstalled=false;f.notify();assert.deepEqual(plain(bridge.project([99])),[99]);
 row.game.isInstalled=true;f.notify();f.probes.get(22).emit(true);assert.deepEqual(plain(bridge.project([99])),[99],'removed old completion does not replay');
 row.job.jobId='new-install';row.job.updatedAt=new Date().toISOString();f.notify();f.probes.get(22).emit(true);assert.deepEqual(plain(bridge.project([99])),[22,99]);
 f.setAccount('76561198000000002');f.notify();assert.deepEqual(plain(bridge.read()),[]);assert.equal(f.probes.get(22).active,true);stop();
});
test('ledger expires after seven days, bounds storage and fences malformed/future entries',()=>{
 const f=fixture();let now=1000000;const ledger=f.api.createStoreInstallRecentLedger(f.context.localStorage,'isolated',()=>now);
 assert.equal(ledger.remember(entry(1,now+61)),false);assert.equal(ledger.remember(entry(1,now-604801)),false);assert.equal(ledger.remember(entry(1,now)),true);assert.equal(ledger.remember(entry(1,now)),false);
 now+=604801;assert.equal(ledger.prune(()=>true),true);assert.equal(ledger.read().length,0);
 assert.equal(ledger.remember(entry(1,now)),false,'same event never re-dated on repeated polling');
});
function actualNativeRecent(games){
 const source=fs.readFileSync('C:/Program Files (x86)/Steam/steamui/sp.js','utf8');
 const start=source.indexOf('const An=B.memo(function('),end=source.indexOf(';function yn()',start);assert.ok(start>0&&end>start);
 const context={B:{memo:fn=>React.memo(fn),useState:()=>[undefined,()=>{}],useMemo:fn=>fn(),useRef:()=>({current:null}),useCallback:fn=>fn},e:{jsx:(type,props)=>React.createElement(type,props),jsxs:(type,props)=>React.createElement(type,props)},b:{rP:()=>({IN_VR:false})},er:{A:class{}},yn:()=>({bHeaderVisible:true,setHeaderVisible(){}}),yr:()=>JSON.stringify(games),l:{q3:fn=>fn()},i:{oy:{MainRunningApp:null}},ve:{ak3:()=>false},A:{A:(...items)=>items.filter(Boolean).join(' ')},bt:()=>({}),g:{Z:'native-root'},Oi:'native-background',ur:{D:'native-header'},ee:{we:value=>value},Xr:'native-carousel',mn:'native-empty'};
 return vm.runInNewContext(source.slice(start+'const An='.length,end),context);
}
function render(node){if(!React.isValidElement(node))return node;let type=node.type;if(type?.$$typeof===Symbol.for('react.memo'))type=type.type;if(typeof type==='function')return render(type(node.props));return node;}
function find(tree,type){if(Array.isArray(tree))return tree.flatMap(node=>find(node,type));if(!React.isValidElement(tree))return[];return[...(tree.type===type?[tree]:[]),...find(tree.props.children,type)];}
test('installed native Home component receives one projected numeric list for carousel and background while Artwork flags/focus remain native',()=>{
 const f=fixture();f.seed(30,'amazon');const stop=f.api.installStoreInstallRecents(f.dependencies);f.probes.get(30).emit(true);
 const native=actualNativeRecent([1,0,2]);function NativeHomeBody(){return React.createElement('section',null,React.createElement(native,{autoFocus:true,showBackground:true}));}function Home(){return React.createElement(NativeHomeBody,{});}
 const element=React.createElement(Home,{});const originalType=element.type,originalNativeType=native.type;
 const patched=f.route({children:element}).children;const rendered=render(patched);const surface=React.cloneElement(rendered,{},...React.Children.toArray(rendered.props.children).map(render));
 const carousel=find(surface,'native-carousel')[0],background=find(surface,'native-background')[0];
 assert.ok(carousel);assert.ok(background);assert.deepEqual(plain(carousel.props.games),[30,1,2]);assert.deepEqual(plain(background.props.games),[30,1,2]);assert.equal(carousel.props.autoFocus,true);assert.equal(typeof carousel.props.onItemFocus,'function');assert.equal(element.type,originalType);assert.equal(native.type,originalNativeType);
 const cover=React.cloneElement(carousel,{showFeaturedItem:false});const output=f.api.presentStoreInstallRecentTree(cover,ids=>[30,...ids.filter(id=>id!==30)]);assert.equal(output.props.showFeaturedItem,false);assert.equal(output.props.onItemFocus,carousel.props.onItemFocus);assert.equal(output.type,carousel.type);
 stop();const restored=render(patched);const nativeRestored=React.cloneElement(restored,{},...React.Children.toArray(restored.props.children).map(render));assert.deepEqual(plain(find(nativeRestored,'native-carousel')[0].props.games),[1,0,2]);
});
