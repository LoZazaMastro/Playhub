import test from 'node:test';import assert from 'node:assert/strict';import {loadDigitalSource,snapshot,flush} from './storeDigitalFixture.mjs';
const load=loadDigitalSource({storeLibraryRuntime:{}}),{reportStoreSteamMigration}=load('storeSteamMigration');
async function run(mode='valid'){
 const s=snapshot(1,1),game=s.games[0];game.isInstalled=true;game.isLaunchable=true;game.installPath='F:\\Games\\Fixture';const native={key:game.key,accountRevision:1,installPath:game.installPath,runtimeExecutable:'C:\\Playhub\\Playhub.GameSession.exe',startDirectory:'C:\\Playhub',gameExecutablePath:game.installPath+'\\Game.exe'};
 const row={appId:3000000001,key:game.key,accountCurrent:true,title:game.title,game:{...game},executable:native.runtimeExecutable,arguments:'--game "'+native.gameExecutablePath+'"',startDirectory:native.startDirectory};
 const candidate={appId:row.appId,title:row.title,proof:{unAppID:row.appId,strShortcutExe:row.executable,strShortcutLaunchOptions:row.arguments,strShortcutStartDir:row.startDirectory}};
 const migration={requestId:'01234567-89ab-cdef-0123-456789abcdef',version:'2.7.0:store-collections-v2',steamAccountId:'76561198000000001',state:'pending',confirmed:0,total:0};let pendingReads=0,save=0,active=true;const calls=[];
 const collection={allApps:[{appid:row.appId}],async Save(){save++;if(mode==='save-failed')throw Error('disk');if(mode==='readback-missing')this.allApps=[];if(mode==='account-change')s.accounts[0].revision++;if(mode==='uninstalled')game.isInstalled=false;}};
 const index={observe(){},ready:()=>mode!=='unknown',candidates:()=>mode==='ambiguous'?[candidate,{...candidate,appId:row.appId+1,proof:{...candidate.proof,unAppID:row.appId+1}}]:[candidate],read:()=>mode==='edited'?{...candidate,proof:{...candidate.proof,strShortcutLaunchOptions:'--manual'}}:candidate};
 const collections={GetCollectionIDByUserTag:()=> 'collection',GetCollection:()=>collection};const store={GetAppOverviewByAppID:()=>({appid:row.appId})};
 const request=async(method,args)=>{calls.push({method,args});if(method==='steam-migration-pending')return mode==='no-pending'?[]:++pendingReads===2&&mode==='steam-change'?[{...migration,steamAccountId:'76561198000000002'}]:[migration];if(method==='snapshot')return structuredClone(s);if(method==='refresh')return{accepted:true,snapshot:structuredClone(s)};if(method==='steam-native-identities')return mode==='partial'?[]:[native];if(method==='steam-owned')return mode==='not-adopted'?[]:[row];if(method==='steam-migration-report')return{...migration,...args,state:args.complete?'completed':'pending'};throw Error(method);};
 if(mode==='disconnected')s.accounts[0].state='disconnected';if(mode==='not-installed')row.game.isInstalled=false;
 try{await reportStoreSteamMigration(request,index,collections,store,()=>active);await flush();await reportStoreSteamMigration(request,index,collections,store,()=>active);}catch(error){if(mode!=='save-failed')throw error;}
 return{calls,save,reports:calls.filter(call=>call.method==='steam-migration-report')};
}
test('migration completes only after exact adopted native tuple and saved collection readback',async()=>{const result=await run();assert.equal(result.save,1);assert.deepEqual(JSON.parse(JSON.stringify(result.reports[0].args)),{requestId:'01234567-89ab-cdef-0123-456789abcdef',steamAccountId:'76561198000000001',confirmed:1,total:1,complete:true});});
test('partial, disconnected, unknown and switched Steam identities never complete migration',async()=>{for(const mode of ['no-pending','unknown','partial','not-adopted','disconnected','not-installed','ambiguous','edited','save-failed','readback-missing','account-change','uninstalled','steam-change']){const result=await run(mode);assert.equal(result.reports.some(report=>report.args.complete),false,mode);}});

function freshnessFixture(){
 const s=snapshot(1,1),game=s.games[0],path='F:\\Official\\Fixture',wrapper='C:\\Playhub\\Playhub.GameSession.exe';
 game.isInstalled=false;game.isLaunchable=false;game.installPath=null;
 for(const account of s.accounts)if(account.store!=='epic')account.state='disconnected';
 const migration={requestId:'11234567-89ab-cdef-0123-456789abcdef',version:'2.7.0:store-collections-v4',steamAccountId:'76561198000000001',state:'pending',confirmed:0,total:0};
 const candidate={appId:3000000011,title:game.title,proof:{unAppID:3000000011,strShortcutExe:wrapper,strShortcutLaunchOptions:'--epic test-space test-item 1 "'+path+'\\Game.exe"',strShortcutStartDir:path}};
 const native=()=>({key:game.key,accountRevision:game.accountRevision,installPath:path,runtimeExecutable:wrapper,startDirectory:path,gameExecutablePath:path+'\\Game.exe',epicNamespace:'test-space',epicCatalogItemId:'test-item'});
 const row=()=>({appId:candidate.appId,key:game.key,accountCurrent:true,title:game.title,game:{...game},executable:wrapper,arguments:candidate.proof.strShortcutLaunchOptions,startDirectory:path});
 let active=true,refresh=()=>normalRefresh();const calls=[];
 function normalRefresh(){game.isInstalled=true;game.isLaunchable=true;game.installPath=path;game.accountRevision=s.accounts[0].revision;s.revision++;return{accepted:true,snapshot:structuredClone(s)};}
 const request=async(method,args)=>{
  calls.push({method,args});
  if(method==='steam-migration-pending')return[{...migration}];
  if(method==='snapshot')return structuredClone(s);
  if(method==='refresh')return refresh(args);
  if(method==='steam-native-identities')return game.isInstalled?[native()]:[];
  if(method==='steam-owned')return game.isInstalled?[row()]:[];
  if(method==='steam-migration-report')return{...migration,...args,state:args.complete?'completed':'pending'};
  throw Error(method);
 };
 const index={observe(){},ready:()=>true,candidates:()=>[candidate],read:()=>candidate};
 const collection={allApps:[{appid:candidate.appId}],async Save(){calls.push({method:'collection-save'});}};
 const invoke=()=>reportStoreSteamMigration(request,index,{GetCollectionIDByUserTag:()=> 'collection',GetCollection:()=>collection},{GetAppOverviewByAppID:()=>({appid:candidate.appId})},()=>active);
 return{s,game,migration,calls,request,normalRefresh,setRefresh(fn){refresh=fn;},leave(){active=false;},
  run:invoke,poll:async()=>{await invoke();await flush();await invoke();},
  refreshes:()=>calls.filter(call=>call.method==='refresh'),reports:()=>calls.filter(call=>call.method==='steam-migration-report')};
}
const deferred=()=>{let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});return{promise,resolve,reject};};

test('stale restored zero-game inventory is refreshed before reporting the existing official game as 1/1',async()=>{
 const f=freshnessFixture();assert.equal(f.game.isInstalled,false);assert.equal(f.game.installPath,null);
 await f.poll();assert.equal(f.reports()[0]?.args.total,1);assert.equal(f.reports().length,1);assert.equal(f.reports()[0].args.confirmed,1);assert.equal(f.reports()[0].args.complete,true);
 assert.deepEqual(f.refreshes().map(call=>JSON.parse(JSON.stringify(call.args))),[{store:'epic',expectedAccountRevision:1}]);
 assert.ok(f.calls.findIndex(call=>call.method==='refresh')<f.calls.findIndex(call=>call.method==='steam-native-identities'));
 assert.ok(f.calls.findIndex(call=>call.method==='collection-save')<f.calls.findIndex(call=>call.method==='steam-migration-report'));
});

test('migration awaits a real refresh ACK and an unmounted observer never reports a late reply',async()=>{
 const f=freshnessFixture(),entered=deferred(),ack=deferred();let settled=false;
 f.setRefresh(()=>{entered.resolve();return ack.promise;});const task=f.run().then(()=>{settled=true;});
 await entered.promise;await flush();assert.equal(settled,true);assert.equal(f.reports().length,0);
 f.leave();ack.resolve(f.normalRefresh());await task;assert.equal(f.reports().length,0);
});

test('refresh failures, missing ACKs, invalid snapshots and account changes cannot complete 0/0',async()=>{
 for(const mode of ['refused','missing','malformed','throws','account-change','steam-change']){
  const f=freshnessFixture();f.setRefresh(()=>{
   if(mode==='throws')throw Error('inventory IO unknown');
   if(mode==='account-change')f.s.accounts[0].revision++;
   if(mode==='steam-change')f.migration.steamAccountId='76561198000000002';
   if(mode==='missing')return{snapshot:structuredClone(f.s)};
   if(mode==='malformed')return{accepted:true,snapshot:{}};
   return{...f.normalRefresh(),accepted:mode!=='refused'};
  });
  await f.run();await flush();await f.run();assert.equal(f.reports().length,0,mode);
 }
});

test('failed inventory refresh is evicted for a future retry, while successful polling does not replay it',async()=>{
 const f=freshnessFixture();f.setRefresh(()=>({accepted:false,snapshot:structuredClone(f.s)}));
 await f.run();await flush();assert.equal(f.reports().length,0);assert.equal(f.refreshes().length,1);
 f.setRefresh(()=>f.normalRefresh());await f.poll();await f.run();
 assert.equal(f.refreshes().length,2);assert.equal(f.reports().length,2);assert.ok(f.reports().every(call=>call.args.total===1&&call.args.confirmed===1));
});

test('concurrent observers share one refresh flight for the same request and revisions',async()=>{
 const f=freshnessFixture(),entered=deferred(),ack=deferred();
 f.setRefresh(()=>{entered.resolve();return ack.promise;});const first=f.run(),second=f.run();
 await entered.promise;await flush();assert.equal(f.refreshes().length,1);assert.equal(f.reports().length,0);
 ack.resolve(f.normalRefresh());await Promise.all([first,second]);await flush();await f.run();
 assert.equal(f.refreshes().length,1);assert.ok(f.reports().length>0);assert.ok(f.reports().every(call=>call.args.complete&&call.args.total===1));
});

test('every connected refreshable store uses its own expected revision and unrefreshable stores are skipped',async()=>{
 const all=freshnessFixture();for(const account of all.s.accounts){account.state='connected';account.revision=account.store==='gog'?8:1;}
 await all.poll();assert.deepEqual(all.refreshes().map(call=>[call.args.store,call.args.expectedAccountRevision]),[['epic',1],['gog',8],['xbox',1],['amazon',1]]);
 const limited=freshnessFixture();limited.s.accounts[1].state='connected';limited.s.accounts[1].capabilities.canRefresh=false;
 await limited.poll();assert.deepEqual(limited.refreshes().map(call=>call.args.store),['epic']);
});

test('a new request, version, Steam account or provider revision requires fresh inventory again',async()=>{
 for(const change of ['request','version','steam','provider']){
  const f=freshnessFixture();await f.poll();
  if(change==='request')f.migration.requestId='21234567-89ab-cdef-0123-456789abcdef';
  if(change==='version')f.migration.version='2.7.0:store-collections-v5';
  if(change==='steam')f.migration.steamAccountId='76561198000000002';
  if(change==='provider')f.s.accounts[0].revision++;
  await f.poll();assert.equal(f.refreshes().length,2,change);
 }
});

test('a failing store flight stays coalesced until all concurrent store refreshes drain',async()=>{
 const f=freshnessFixture(),entered=deferred(),held=deferred();f.s.accounts[1].state='connected';
 f.setRefresh(args=>args.store==='epic'?{accepted:false,snapshot:structuredClone(f.s)}:(entered.resolve(),held.promise));
 const first=f.run();await entered.promise;const second=f.run();await flush();
 assert.equal(f.refreshes().length,2);assert.equal(f.reports().length,0);
 held.resolve(f.normalRefresh());await Promise.all([first,second]);assert.equal(f.reports().length,0);
});

test('a genuinely empty inventory completes 0/0 only after an accepted refresh and a later poll',async()=>{
 const f=freshnessFixture();f.setRefresh(()=>({accepted:true,snapshot:structuredClone(f.s)}));
 await f.run();assert.equal(f.reports().length,0);await flush();await f.run();
 assert.equal(f.refreshes().length,1);assert.equal(f.reports().length,1);
 assert.equal(f.reports()[0].args.total,0);assert.equal(f.reports()[0].args.confirmed,0);assert.equal(f.reports()[0].args.complete,true);
});
