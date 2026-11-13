import assert from 'node:assert/strict';import fs from 'node:fs';import vm from 'node:vm';
import ts from'typescript';
const source=fs.readFileSync(new URL('../src/steamNativeGameOverlay.ts',import.meta.url),'utf8'),exports={};
vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports});
const {createSteamNativeGameOverlay,validNativeGame,bindNativeGameBrowser,readyForNativeMenu,coordinateNativeOriginalMenus,coordinateNativeOriginalClose,nativeInputProofAgeUpper}=exports,A=2979426017,B=3134433872,full=a=>((BigInt(a)<<32n)|0x02000000n).toString();
const base={protocol:1,canonicalAppId:A,fullGameId:full(A),observedGameId:String(A),sessionId:'a'.repeat(64),gamePid:45824,gameBirth:'134389494978336874',wrapperPid:45000,wrapperBirth:'134389494978336800',steamPid:32936,steamBirth:'134388877303141800',companionPid:53872,companionBirth:'134389494978336899',overlayPid:28788,overlayBirth:'134389494978336900',gameWindow:'21562522',observedAt:'1000',expiresAt:'2500',routingOnly:true};
function fixture({proofReply,prepare,closedReply,abortReply,cancelReply,infos:override,coordinateOriginalMenu,coordinateOriginalClose,nativeApi,inputReplyFields}={}){
 let app=A,observed=A,revision=0,clock=0,side=0,timerId=0,currentProof={...base};const calls=[],timers=new Map();
 const menu={GetOpenSideMenu(){return side},CloseSideMenus(){side=0},GetQuickAccessTab(){return 'power'},OpenMainMenu(){calls.push(['main']);side=1},OpenQuickAccessMenu(tab,activate){calls.push(['quick',tab,activate]);side=2}};
 const doc={},win={document:doc},info={unPID:base.overlayPid,nBrowserID:1310737,appID:9858785,gameID:base.observedGameId};
 const target={params:{browserInfo:{m_unPID:info.unPID,m_nBrowserID:info.nBrowserID,m_unAppID:info.appID,m_gameID:info.gameID}},IsGamepadUIOverlayWindow:()=>true,BrowserWindow:win,MenuStore:menu};
 const store={m_mapOverlayPopupByPID:new Map([[base.overlayPid,target]]),OverlayWindows:[target]};
 const ready=()=>({ok:true,inputMode:'owned-relay',inputReady:true,ownerToken:'b'.repeat(32),generation:'1',nativeGame:{...currentProof},...inputReplyFields});
 const route=createSteamNativeGameOverlay({coordinateOriginalMenu,coordinateOriginalClose,scope:()=>({app,observedApp:observed,revision,disposed:false}),now:()=>clock,
  proof:async value=>{calls.push(['proof',value]);return proofReply??{ok:true,kind:'native-game',nativeGame:{...currentProof}}},
  browserInfo:async()=>override??[info],windowStore:()=>store,input:async request=>{calls.push(['input',request]);if(request.action==='prepare')return prepare?await prepare(request,ready):ready();if(request.action==='closed'&&closedReply)return closedReply;if(request.action==='abort'&&abortReply)return abortReply;if(request.action==='cancel'&&cancelReply)return cancelReply;return{ownerToken:'b'.repeat(32),generation:'1',nativeGame:{...currentProof},ok:true,inputMode:'owned-relay',inputReady:true,closed:request.action==='closed',cleanupComplete:request.action==='closed',...inputReplyFields}},
  schedule:(fn,ms)=>{const id=++timerId;timers.set(id,{fn,ms});return id},cancel:id=>timers.delete(id),
  setOverlayState:async(token,state)=>{calls.push(['set',token,state]);return nativeApi?.SetOverlayState(token,state)},trace:s=>calls.push(['trace',s])});
 return{route,calls,store,target,menu,info,ready,timers,setApp:x=>{app=x;observed=x},setObserved:x=>observed=x,bump:()=>revision++,advance:ms=>clock+=ms,side:()=>side,
 replaceProof(p){currentProof=p;info.unPID=p.overlayPid;info.gameID=p.observedGameId;info.nBrowserID++;target.params.browserInfo={m_unPID:info.unPID,m_nBrowserID:info.nBrowserID,m_unAppID:info.appID,m_gameID:info.gameID};store.m_mapOverlayPopupByPID.clear();store.m_mapOverlayPopupByPID.set(p.overlayPid,target)},
 async tick(){const first=timers.entries().next().value;if(!first)return;const[id,{fn,ms}]=first;timers.delete(id);clock+=ms;fn();for(let i=0;i<40;i++)await Promise.resolve()}};
}
let checks=0;const check=(value,label)=>{assert.ok(value,label);checks++};
check(readyForNativeMenu({ok:true,inputMode:'owned-relay',inputReady:true}),'owned requires explicit worker Ready');check(readyForNativeMenu({ok:true,inputMode:'ordinary',ordinaryReady:true,noOwnedOverride:true}),'ordinary mode permits draw with explicit no-owned-override proof');for(const r of[{ok:true,inputReady:true},{ok:true,inputMode:'ordinary',ordinaryReady:true,noOwnedOverride:false},{ok:true,inputMode:'ordinary',ordinaryReady:true,noOwnedOverride:true,inputReady:true},{ok:true,inputMode:'owned-relay',inputReady:false}])check(!readyForNativeMenu(r),'unknown/ambiguous/missing readiness is no draw');
check(validNativeGame(base,A),'canonical proof valid');for(const [key,value]of[['overlayPid',base.companionPid],['fullGameId',String(A)],['gameBirth',134389494978336874],['routingOnly',false],['sessionId','bad'],['expiresAt','1050'],['expiresAt','999'],['expiresAt','2501']])check(!validNativeGame({...base,[key]:value},A),`invalid ${key}`);
let f=fixture();await f.route.guide('main');check(f.calls.findIndex(c=>c[0]==='input'&&c[1].action==='prepare')<f.calls.findIndex(c=>c[0]==='set'&&c[2]===2),'real prepare Ready precedes Set2');check(f.calls.some(c=>c[0]==='set'&&c[1]===String(A)&&c[2]===2)&&!f.calls.some(c=>c[0]==='set'&&c[1]===full(A)),'native observed compact token preserved distinct from canonical');check(f.side()===1&&f.route.active,'original Main only, no mirror');check(f.calls.find(c=>c[0]==='input')[1].browser.browserId===1310737,'actual browser bound in ready request');
f.route.activated(base.companionPid,A,true);f.route.activated(base.overlayPid,A,true);await f.route.eventsSettled();check(f.calls.filter(c=>c[0]==='input'&&c[1].action==='active').length===0,'companion/full canonical callback ignored');
f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(f.calls.filter(c=>c[0]==='input'&&c[1].action==='closed').length===0,'false before same original true cannot release');
f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(f.calls.filter(c=>c[0]==='input'&&c[1].action==='active').length===1&&f.calls.filter(c=>c[0]==='input'&&c[1].action==='closed').length===1&&!f.route.active,'true then false ordered through exact owner0 ack');check(f.calls.filter(c=>c[0]==='input'&&c[1].callback).map(c=>c[1].callback.serial).join(',')==='1,2','callback serial exact increasing decimal');
f=fixture();await f.route.guide('main');await f.route.guide('quick');check(f.calls.filter(c=>c[0]==='set'&&c[2]===2).length===1&&f.side()===2,'Main to Quick does not Set2 toggle again');await f.route.guide('quick');check(f.calls.some(c=>c[0]==='set'&&c[2]===0)&&f.route.active,'close keeps STOP owner until native false, not menu DOM');await f.route.guide('main');check(f.calls.filter(c=>c[0]==='set'&&c[2]===2).length===1,'no reopen from unresolved close');f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(!f.route.active,'same exact closed callback completes closing owner');
for(const reply of[{ok:false},{ok:true,kind:'native-game',nativeGame:{...base,overlayPid:base.companionPid}},{ok:true,kind:'absent',nativeGame:base}]){f=fixture({proofReply:reply});check((await f.route.guide('main')).handled===true&&!f.calls.some(c=>c[0]==='set'||c[0]==='input'),'unknown never fallback or writes');}
f=fixture({proofReply:{ok:true,kind:'absent'}});check((await f.route.guide('main')).handled===false&&!f.route.active,'only positive absent returns normal mirror fallback permission');check((await f.route.guide('main')).handled===false,'absent leaves no stale wanted toggle');
for(const change of[i=>({...i,unPID:base.companionPid}),i=>({...i,gameID:full(A)}),i=>({...i,nBrowserID:0})]){const i=change(f.info);f=fixture({infos:[i]});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='input'||c[0]==='set'),'wrong browser scope never prepares input');}
f=fixture({infos:[f.info,f.info]});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='input'),'duplicate native browser refuses');
let release;f=fixture({prepare:(_r,ready)=>new Promise(resolve=>release=()=>resolve(ready()))});const open=f.route.guide('main');for(let n=0;n<20&&!release;n++)await Promise.resolve();const close=f.route.guide('main');release();await open;await close;check(!f.calls.some(c=>c[0]==='set'&&c[2]===2)&&f.calls.filter(c=>c[0]==='input'&&c[1].action==='cancel').length===1,'late Ready adopted and cancelled once before Set2');
f=fixture({prepare:(_r,ready)=>new Promise(resolve=>release=()=>resolve(ready()))});const pending=f.route.guide('main');for(let n=0;n<20;n++)await Promise.resolve();const upgrade=f.route.guide('quick');release();await pending;await upgrade;check(f.side()===2&&f.calls.filter(c=>c[0]==='input'&&c[1].action==='prepare').length===1,'pending quick coalesces into same exact prepare');
f=fixture({prepare:async(_r,ready)=>{f.target.MenuStore={...f.menu};return ready()}});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set'&&c[2]===2),'menu replaced during Ready prevents dispatch');
f=fixture({prepare:async(_r,ready)=>{f.setApp(B);return ready()}});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set')&&f.calls.some(c=>c[0]==='input'&&c[1].action==='cancel'),'app replacement during Ready only cancels exact old token');
f=fixture({prepare:async(_r,ready)=>({...ready(),nativeGame:{...base,overlayBirth:'134389494978336901'}})});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set'),'fresh Ready hostbirth cannot replace authenticated original');
f=fixture({prepare:async()=>({ok:true,inputReady:false,ownerToken:'b'.repeat(32),generation:'1',nativeGame:base})});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set')&&f.calls.some(c=>c[0]==='input'&&c[1].action==='cancel'),'not Ready never Set2 or draw, exact receipt cancelled');
await f.route.guide('main');check(f.calls.filter(c=>c[0]==='input'&&c[1].action==='prepare').length===2,'failed ready without owner leaves no app-blind wanted toggle');
f=fixture({prepare:async(_r,ready)=>({...ready(),inputMode:'ordinary',inputReady:false,ordinaryReady:true,noOwnedOverride:true})});await f.route.guide('main');check(f.calls.some(c=>c[0]==='set'&&c[2]===2)&&f.side()===1,'ordinary no-owned-output lane draws original menu without invented769 ACK');
f=fixture({proofReply:{ok:true,kind:'packaged'}});check((await f.route.guide('main')).handled===false&&!f.calls.some(c=>c[0]==='input'),'positive packaged server classification preserves existing mirror path');
f=fixture();await f.route.guide('main');f.advance(6000);f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(f.calls.filter(c=>c[0]==='input'&&c[1].callback).length===2&&!f.route.active,'late actual callback pair remains cleanup provenance, never refreshed proof/Set2');
f=fixture();await f.route.guide('main');check(f.timers.size===0,'no readiness renewal from proof or UI alone');f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();check(f.timers.size===1,'actual matching true schedules explicit owner renewal');await f.tick();check(f.calls.filter(c=>c[0]==='input'&&c[1].action==='renew').length===1&&f.timers.size===1,'one settled renewal produces exactly one next timer');await f.tick();check(f.calls.filter(c=>c[0]==='set'&&c[2]===2).length===1&&f.calls.filter(c=>c[0]==='input'&&c[1].action==='prepare').length===1,'renew never reopens or reacquires generation');f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(f.timers.size===0&&!f.route.active,'native close cancels owned timer');
for(const nextApp of[A,B]){f=fixture();await f.route.guide('main');f.setApp(0);await f.route.invalidate();const before=f.calls.length;f.setApp(nextApp);f.replaceProof({...base,canonicalAppId:nextApp,fullGameId:full(nextApp),observedGameId:String(nextApp),sessionId:'c'.repeat(64),gamePid:45825,gameBirth:'134389494978336975',wrapperPid:45001,wrapperBirth:'134389494978336970',overlayPid:28789,overlayBirth:'134389494978336980'});await f.route.guide('main');check(f.calls.filter(c=>c[0]==='input'&&c[1].action==='prepare').length===2,'actual app0 then positive distinct same/cross-app session admits first Guide');check(!f.calls.slice(before).some(c=>c[0]==='set'&&c[2]===0||c[0]==='input'&&['closed','cancel'].includes(c[1].action)),'new-session local forget never emits Set0/Closed/resume for old or new');}
f=fixture();await f.route.guide('main');f.setObserved(null);await f.route.invalidate();f.replaceProof({...base,sessionId:'c'.repeat(64),gamePid:45825,gameBirth:'134389494978336975',wrapperPid:45001,wrapperBirth:'134389494978336970'});await f.route.guide('main');check(f.calls.filter(c=>c[0]==='input'&&c[1].action==='prepare').length===1,'unknown/null app is not actual idle invalidation');
check(!/SetForegroundWindow|FocusApplicationRoot|SetKeyFocus|SetFocus\(|PrepareSnapshot|\.navigation|actualScope/.test(source),'no foreground/snapshot/navigation/mirror scope API');
for(const reply of[{ok:true},{ok:true,closed:false,cleanupComplete:true},{ok:true,closed:true,cleanupComplete:false}]){f=fixture({closedReply:reply});await f.route.guide('main');f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(f.route.active,'accepted or incomplete native false keeps local STOP debt until actual Closed+cleanupComplete');}
f=fixture({closedReply:{ok:true,closed:true,cleanupComplete:true,resumeAllowed:false}});await f.route.guide('main');f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(!f.route.active,'actual0ACK closes UI ownership while worker independently waits newepoch/two released samples');
f=fixture();f.replaceProof({...base,observedGameId:'480'});f.info.appID=480;f.target.params.browserInfo.m_unAppID=480;await f.route.guide('main');check(f.calls.some(c=>c[0]==='set'&&c[1]==='480'&&c[2]===2)&&f.calls.find(c=>c[0]==='input')[1].expectedNative.fullGameId===full(A),'attested observed480 is used only as native overlay token, canonical identity remains unchanged');f.route.activated(base.overlayPid,480,true);await f.route.eventsSettled();check(f.calls.some(c=>c[0]==='input'&&c[1].action==='active'&&c[1].callback.appId===480),'alias callback matches exact actual bound native host/app, not global480');

// A9C saved tuple: full launch proof, compact game key on the exact original renderer.
const a9c={...base,observedGameId:full(A),gamePid:17324,gameBirth:'134389563762134310',wrapperPid:53512,wrapperBirth:'134389563761052038',overlayPid:5296};
f=fixture({prepare:async(_request,ready)=>({...ready(),inputMode:'ordinary',inputReady:false,ordinaryReady:true,noOwnedOverride:true})});f.replaceProof(a9c);f.info.gameID=String(A);f.info.nBrowserID=524295;f.target.params.browserInfo.m_gameID=String(A);f.target.params.browserInfo.m_nBrowserID=524295;
let binding=bindNativeGameBrowser(a9c,[f.info],f.store);
check(binding?.rendererToken===String(A)&&binding.browser.browserId===524295,'full launch proof binds exact actual compact original renderer');
await f.route.guide('main');check(f.calls.some(c=>c[0]==='set'&&c[1]===String(A)&&c[2]===2),'ordinary A9C Set2 uses actual compact renderer token');
check(f.calls.find(c=>c[0]==='input'&&c[1].action==='prepare')[1].expectedNative.observedGameId===full(A),'expected backend proof retains full observed launch token unchanged');
await f.route.guide('main');check(f.calls.some(c=>c[0]==='set'&&c[1]===String(A)&&c[2]===0)&&!f.calls.some(c=>c[0]==='set'&&c[1]===full(A)),'Set0 retains compact binding token, never full launch token');
f.info.gameID=full(A);f.target.params.browserInfo.m_gameID=full(A);check(!binding.current(),'old compact binding rejects changed renderer token');
binding=bindNativeGameBrowser(a9c,[f.info],f.store);check(binding?.rendererToken===full(A),'exact full observed renderer remains valid when actually reported');
f.info.nBrowserID++;f.target.params.browserInfo.m_nBrowserID=f.info.nBrowserID;check(!binding.current(),'browser ID stays immutable if API record and window both mutate');
f=fixture();f.replaceProof(a9c);f.info.gameID=String(A);f.target.params.browserInfo.m_gameID=String(A);
const second={...f.info,gameID:full(A),nBrowserID:f.info.nBrowserID+1};check(!bindNativeGameBrowser(a9c,[f.info,second],f.store),'both full and compact matching browsers remain ambiguous');
check(!bindNativeGameBrowser(a9c,[{...f.info,unPID:base.companionPid}],f.store),'compact token never permits companion renderer');
check(!bindNativeGameBrowser(a9c,[{...f.info,gameID:String(B)}],f.store),'different canonical compact token refused');
check(!bindNativeGameBrowser({...a9c,observedGameId:'480'},[f.info],f.store),'observed alias480 cannot adopt canonical compact renderer');
f.info.gameID='480';f.target.params.browserInfo.m_gameID='480';binding=bindNativeGameBrowser({...a9c,observedGameId:'480'},[f.info],f.store);check(binding?.rendererToken==='480','alias480 remains exact-observed only');
f.target.BrowserWindow={document:{}};check(!binding.current(),'replaced original window/document vetoes retained token');



// Exact-original system-source coordination; this never cancels controller messages.
function originalLane({isCurrent=()=>true,observer=()=>true}={}) {
 const calls=[],nav={m_rgGamepadInputSources:[]},context={m_controller:nav};
 const target={GetFocusNavContext:()=>context,OnHomeButtonPressed(...args){calls.push(['home',this,args]);return 'home-result'},OnQuickAccessButtonPressed(...args){calls.push(['quick',this,args]);return 'quick-result'}};
 const source={m_NavController:nav,HandleSystemKeyEvents(){},OnSystemButtonPress(menu,...args){calls.push(['source',this,args]);return menu==='main'?target.OnHomeButtonPressed(...args):target.OnQuickAccessButtonPressed(...args)}};nav.m_rgGamepadInputSources.push(source);
 const oldHome=target.OnHomeButtonPressed,oldQuick=target.OnQuickAccessButtonPressed,oldSource=source.OnSystemButtonPress;
 const stop=coordinateNativeOriginalMenus(target,isCurrent,observer,s=>calls.push(['trace',s]));
 return{calls,nav,context,target,source,stop,oldHome,oldQuick,oldSource};
}
let lane=originalLane();check(typeof lane.stop==='function','exact unique source/observer installs one original lane');
check(lane.target.OnQuickAccessButtonPressed(7)==='quick-result'&&lane.calls.filter(x=>x[0]==='quick').length===1,'direct original calls outside source frame preserve return/args/receiver');
lane.source.OnSystemButtonPress('quick',8);check(lane.calls.filter(x=>x[0]==='source').length===1&&lane.calls.filter(x=>x[0]==='quick').length===1,'synchronous duplicate menu is consumed while source original still runs once');
lane.source.OnSystemButtonPress('main');check(!lane.calls.some(x=>x[0]==='home'),'both original system menu mutations share one coordinator lane');
lane.stop();check(lane.source.OnSystemButtonPress===lane.oldSource&&lane.target.OnQuickAccessButtonPressed===lane.oldQuick&&lane.target.OnHomeButtonPressed===lane.oldHome,'stop restores exact owned method descriptors');
lane.source.OnSystemButtonPress('quick');check(lane.calls.filter(x=>x[0]==='quick').length===2,'normal original Steam route resumes after stop');
for(const options of[{isCurrent:()=>false},{observer:()=>false},{isCurrent:()=>{throw Error('unknown')}}]){lane=originalLane(options);check(!lane.stop,'unknown/unowned/observer-unavailable installs no functional hook');check(lane.source.OnSystemButtonPress('quick')==='quick-result','failed admission leaves original unaffected');}
lane=originalLane();lane.nav.m_rgGamepadInputSources=[];lane.source.OnSystemButtonPress('quick');check(lane.calls.some(x=>x[0]==='quick'),'input-source replacement delegates original');lane.stop();
lane=originalLane();lane.context.m_controller={};lane.source.OnSystemButtonPress('quick');check(lane.calls.some(x=>x[0]==='quick'),'context-controller replacement delegates original');lane.stop();
let allowed=true;lane=originalLane({isCurrent:()=>allowed});allowed=false;lane.source.OnSystemButtonPress('quick');check(lane.calls.some(x=>x[0]==='quick'),'expired proof delegates original');lane.stop();
lane=originalLane();const external=()=> 'third-party';lane.target.OnQuickAccessButtonPressed=external;lane.stop();check(lane.target.OnQuickAccessButtonPressed===external,'cleanup never overwrites a later third-party owner');
const nav={m_rgGamepadInputSources:[]},context={m_controller:nav},target={GetFocusNavContext:()=>context,OnHomeButtonPressed(){},OnQuickAccessButtonPressed(){}};const sourceA={m_NavController:nav,HandleSystemKeyEvents(){},OnSystemButtonPress(){}};nav.m_rgGamepadInputSources=[sourceA,{...sourceA}];check(!coordinateNativeOriginalMenus(target,()=>true,()=>true),'ambiguous gamepad source refuses installation');
let lateStop,reading=0;lane=originalLane({isCurrent:()=>{if(++reading===4)lateStop?.();return true}});lateStop=lane.stop;lane.source.OnSystemButtonPress('quick');check(lane.calls.some(x=>x[0]==='quick'),'reentrant cleanup during final current getter cannot consume original');lane.stop?.();
// Coordinator closes/restores the exact lane even while native0 cleanup remains pending.
let restored=0;f=fixture({coordinateOriginalMenu:(_target,current)=>{check(current(),'lane attaches only after exact Ready');return()=>restored++}});await f.route.guide('quick');await f.route.guide('quick');check(restored===1&&f.route.active,'Set0 releases menu lane without fabricating nativeClosed');
restored=0;f=fixture({coordinateOriginalMenu:()=>()=>restored++});await f.route.guide('main');f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(restored===1&&!f.route.active,'actual nativeFalse releases lane and preserves closed0ACK rule');
restored=0;f=fixture({coordinateOriginalMenu:()=>()=>restored++});await f.route.guide('main');await f.route.dispose();check(restored===1,'dispose releases owned menu lane');


// Diagnostic branch receipts never change draw/cleanup admission.
f=fixture({prepare:async()=>({ok:true,inputMode:'ordinary',ordinaryReady:true,noOwnedOverride:false,ownerToken:'b'.repeat(32),generation:'1',nativeGame:base})});await f.route.guide('main');check(f.calls.some(c=>c[0]==='trace'&&c[1].includes('prepare-receipt mode=ordinary'))&&f.calls.some(c=>c[0]==='trace'&&c[1].includes('stage=input-ready-receipt')),'not-ready actual receipt has a bounded refusal stage');check(!f.calls.some(c=>c[0]==='set'&&c[2]===2),'diagnostic cannot turn ordinary refusal into draw');
f=fixture({prepare:async(_r,ready)=>({...ready(),nativeGame:{...base,observedAt:'1000',expiresAt:'1049'}})});await f.route.guide('main');check(f.calls.some(c=>c[0]==='trace'&&c[1].includes('stage=input-ready-receipt retainedReady=true'))&&!f.calls.some(c=>c[0]==='set'&&c[2]===2),'Ready booleans are distinct from expired routing receipt');
f=fixture({prepare:async(_r,ready)=>{f.target.MenuStore={...f.menu};return ready()}});await f.route.guide('main');check(f.calls.some(c=>c[0]==='trace'&&c[1].includes('ui-current-refused store=true browser=false'))&&f.calls.some(c=>c[0]==='trace'&&c[1].includes('stage=post-ready-ui')),'UI binding rejection distinct from readiness refusal');
f=fixture();await f.route.guide('main');check(f.calls.some(c=>c[0]==='trace'&&c[1].startsWith('set2-returned app=')),'actual Set2 completion logged separately from opened/menu/pixels');check(f.calls.filter(c=>c[0]==='proof').length===1&&f.calls.filter(c=>c[0]==='input'&&c[1].action==='prepare').length===1&&f.calls.filter(c=>c[0]==='set'&&c[2]===2).length===1,'diagnostics introduce no RPC, Set2 retry or new input authority');


// Real owned prepare1770ms: the authenticated final proof is produced AFTER worker wait.
for (const marker of [1700,undefined]) {
 f=fixture({prepare:async(_r,ready)=>{f.advance(1770);return{...ready(),nativeGame:{...base,observedAt:'2770',expiresAt:'4270'},...(marker===undefined?{}:{proofProducedAfterMs:marker})}}});await f.route.guide('main');
 check(f.calls.some(c=>c[0]==='set'&&c[2]===2)===(marker!==undefined),'1770ms owned prepare draws only with authenticated producer duration and fresh final proof');
}
for(const marker of[-1,1771,1.5,NaN,Infinity,'1700']){f=fixture({prepare:async(_r,ready)=>{f.advance(1770);return{...ready(),nativeGame:{...base,observedAt:'2770',expiresAt:'4270'},proofProducedAfterMs:marker}}});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set'&&c[2]===2),'malformed producer duration never resets routing age');}
f=fixture({prepare:async(_r,ready)=>{f.advance(3300);return{...ready(),nativeGame:{...base,observedAt:'2770',expiresAt:'4270'},proofProducedAfterMs:1700}}});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set'&&c[2]===2),'delayed return remains in proof age upper bound');
f=fixture({prepare:async(_r,ready)=>{f.advance(1770);return{...ready(),nativeGame:{...base,observedAt:'2770',expiresAt:'4270',overlayBirth:'134389494978336901'},proofProducedAfterMs:1700}}});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set'&&c[2]===2),'fresh proof cannot replace exact authenticated host');
f=fixture({prepare:async(_r,ready)=>{f.advance(1770);return{...ready(),inputReady:false,nativeGame:{...base,observedAt:'2770',expiresAt:'4270'},proofProducedAfterMs:1700}}});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set'&&c[2]===2),'worker revoke/no Ready is never repaired by timing metadata');
f=fixture({prepare:async(_r,ready)=>{f.advance(4950);return{...ready(),nativeGame:{...base,observedAt:'5950',expiresAt:'7450'},proofProducedAfterMs:4900}}});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set'&&c[2]===2),'routing proof freshness cannot extend original5s prepare input horizon');
check(nativeInputProofAgeUpper({ok:true,proofProducedAfterMs:1700},1770.2)===71,'ceil RTT minus floor producer duration stays conservative');
check(nativeInputProofAgeUpper({ok:true},1770.2)===1771,'legacy or missing producer field keeps full round trip');


const abortConfirmed={ok:true,aborted:true,closed:false,cleanupComplete:true,resumeAllowed:false,ownerToken:'b'.repeat(32),generation:'1',nativeGame:base};
let abortReady;f=fixture({abortReply:abortConfirmed,prepare:(_r,ready)=>new Promise(resolve=>abortReady=()=>resolve(ready()))});const beforeDispatch=f.route.guide('main');for(let i=0;i<20&&!abortReady;i++)await Promise.resolve();const beforeCancel=f.route.guide('main');abortReady();await beforeDispatch;await beforeCancel;
check(f.calls.some(c=>c[0]==='input'&&c[1].action==='abort')&&!f.route.active,'exact confirmed predispatch abort retires local owner');
check(!f.calls.some(c=>c[0]==='set')&&!f.calls.some(c=>c[0]==='input'&&['active','closed','cancel'].includes(c[1].action)),'predispatch abort needs no Set0/Set2/nativeFalse or genericcancel');
for(const bad of[{...abortConfirmed,aborted:false},{...abortConfirmed,cleanupComplete:false},{...abortConfirmed,closed:true},{...abortConfirmed,ownerToken:'c'.repeat(32)},{...abortConfirmed,generation:'2'}]){f=fixture({abortReply:bad,prepare:(_r,ready)=>new Promise(resolve=>abortReady=()=>resolve(ready()))});const p=f.route.guide('main');for(let i=0;i<20;i++)await Promise.resolve();const c=f.route.guide('main');abortReady();await p;await c;check(f.route.active&&f.calls.some(x=>x[0]==='input'&&x[1].action==='cancel'),'unknown/foreign/incomplete abort cannot discard cleanup debt');}
f=fixture({abortReply:abortConfirmed});await f.route.guide('main');await f.route.invalidate();check(!f.calls.some(c=>c[0]==='input'&&c[1].action==='abort')&&f.route.active,'attempted Set2 can onlycancel/wait actualfalse, never predispatchabort');
f=fixture({abortReply:abortConfirmed,prepare:(_r,ready)=>new Promise(resolve=>abortReady=()=>resolve(ready()))});const ptrue=f.route.guide('main');for(let i=0;i<20;i++)await Promise.resolve();f.route.activated(base.overlayPid,9858785,true);const ctrue=f.route.guide('main');abortReady();await ptrue;await ctrue;check(!f.calls.some(c=>c[0]==='input'&&c[1].action==='abort')&&f.calls.some(c=>c[0]==='trace'&&c[1]==='pre-ready-native-true-cleanup-only'),'actual native TRUE during prepare latches cleanup-only and vetoes abort');


// Genuine nativeFALSE observed while prepare is pending must survive receipt adoption.
f=fixture({prepare:(_r,ready)=>new Promise(resolve=>abortReady=()=>resolve(ready()))});const pairOpen=f.route.guide('main');for(let i=0;i<20;i++)await Promise.resolve();f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);const pairCancel=f.route.guide('main');abortReady();await pairOpen;await pairCancel;await f.route.eventsSettled();
check(!f.route.active&&!f.calls.some(c=>c[0]==='set'),'actual pre-readyTRUE/FALSE closes only after its realFALSE/0ACK with no Set0/Set2');
const pairActions=f.calls.filter(c=>c[0]==='input').map(c=>c[1].action);check(pairActions.join(',')==='prepare,cancel,closed','cancelsticky precedes deferred genuineFALSE; no activeTRUE/769 replay');
check(f.calls.find(c=>c[0]==='input'&&c[1].action==='closed')[1].callback.serial==='2','deferred actualFALSE keeps original observed serial');
f=fixture({prepare:(_r,ready)=>new Promise(resolve=>abortReady=()=>resolve(ready()))});const newerOpen=f.route.guide('main');for(let i=0;i<20;i++)await Promise.resolve();f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);f.route.activated(base.overlayPid,9858785,true);abortReady();await newerOpen;check(f.route.active&&!f.calls.some(c=>c[0]==='input'&&c[1].action==='closed'),'newer actualTRUE prevents forwarding an earlierFALSE as current cleanup');


// A newer TRUE during deferred0ACK must retain debt and reach the revoked bridge.
let releaseClosed;const pendingClosed=new Promise(resolve=>releaseClosed=resolve);f=fixture({closedReply:pendingClosed,prepare:(_r,ready)=>new Promise(resolve=>abortReady=()=>resolve(ready()))});const racedOpen=f.route.guide('main');for(let i=0;i<20;i++)await Promise.resolve();f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);abortReady();for(let i=0;i<40&&!f.calls.some(c=>c[0]==='input'&&c[1].action==='closed');i++)await Promise.resolve();f.route.activated(base.overlayPid,9858785,true);releaseClosed({ok:true,closed:true,cleanupComplete:true});await racedOpen;await f.route.eventsSettled();check(f.route.active,'actualTRUE during0ACK prevents stale localclosed claim');check(f.calls.some(c=>c[0]==='input'&&c[1].action==='active'&&c[1].callback.serial==='3')&&f.timers.size===0,'new actualTRUE forwarded only after confirmedcancel with no input renewal');
f=fixture({cancelReply:{ok:false},prepare:(_r,ready)=>new Promise(resolve=>abortReady=()=>resolve(ready()))});const uncertainOpen=f.route.guide('main');for(let i=0;i<20;i++)await Promise.resolve();f.route.activated(base.overlayPid,9858785,true);f.route.activated(base.overlayPid,9858785,false);abortReady();await uncertainOpen;check(f.route.active&&!f.calls.some(c=>c[0]==='input'&&['active','closed'].includes(c[1].action)),'unconfirmed revocation cannot forward a deferred event as cleanup or revive input');

// Original full64 Hidden is lowered only inside the active authenticated native lease.
{
 let allowed=false,count=0,received,receiver;const result={get then(){throw Error('Return must not be inspected')}};
 const api={SetOverlayState:function(...args){count++;received=args;receiver=this;return result}},original=api.SetOverlayState;
 const stop=coordinateNativeOriginalClose(api,full(A),String(A),()=>allowed);
 check(typeof stop==='function','Exact close adapter installed structurally before its active grant');
 check(api.SetOverlayState(full(A),0)===result&&received[0]===full(A),'Inactive/nativeTRUE-unseen call delegates unchanged');
 allowed=true;const value=api.SetOverlayState(full(A),0);
 check(value===result&&receiver===api&&received[0]===String(A)&&received[1]===0&&count===2,'Active original Hidden forwarded once to authenticated renderer token with same return/receiver');
 for(const args of [[full(A),2],[String(A),0],[full(B),0],[full(A),1],[full(A),0,'extra']]){api.SetOverlayState(...args);check(received.length===args.length&&received.every((x,i)=>x===args[i]),'Open/foreign/alreadyrenderer/otherstate/unknownarity untouched')}
 const foreign={};api.SetOverlayState.call(foreign,full(A),0);check(receiver===foreign&&received[0]===full(A),'Borrowed foreign receiver is not lowered');
 const saved=api.SetOverlayState;stop();check(api.SetOverlayState===original,'Exact native method restored');allowed=true;saved.call(api,full(A),0);check(received[0]===full(A),'Retained wrapper after stop is passive');
}
{
 let stop,reads=0,received;const api={SetOverlayState:(...args)=>{received=args}};
 stop=coordinateNativeOriginalClose(api,full(A),String(A),()=>{reads++;stop();return true});api.SetOverlayState(full(A),0);
 check(reads===1&&received[0]===full(A),'Reentrant revocation during last grant getter prevents lowering');
}
{
 let calls=0;const failure=Error('native setter failed');const api={SetOverlayState(){calls++;throw failure}};
 const stop=coordinateNativeOriginalClose(api,full(A),String(A),()=>true);
 assert.throws(()=>api.SetOverlayState(full(A),0),e=>e===failure);check(calls===1,'Original synchronous throw is identical and never causes a second native call');stop();
}
{
 let reads=0;const api={};Object.defineProperty(api,'SetOverlayState',{get(){reads++;return()=>{}},configurable:true});
 check(coordinateNativeOriginalClose(api,full(A),String(A),()=>true)===undefined&&reads===0,'Unknown accessor is refused without invoking it');
 check(coordinateNativeOriginalClose({},full(A),full(A),()=>true)===undefined,'Normal same-token route needs no wrapper');
}
{
 const api={SetOverlayState(){}},original=api.SetOverlayState;const stop=coordinateNativeOriginalClose(api,full(A),String(A),()=>true),foreign=()=>{};api.SetOverlayState=foreign;stop();check(api.SetOverlayState===foreign,'Cleanup never overwrites third-party replacement');
 Object.defineProperty(api,'SetOverlayState',{value:original,configurable:false,writable:true});const stop2=coordinateNativeOriginalClose(api,full(A),String(A),()=>true);check(typeof stop2==='function','Writable nonconfigurable original descriptor supported without widening it');stop2();check(api.SetOverlayState===original&&Object.getOwnPropertyDescriptor(api,'SetOverlayState').configurable===false,'Original property flags restored unchanged');
}
{
 let received;const api={SetOverlayState:(...a)=>{received=a}},original=api.SetOverlayState;
 f=fixture({nativeApi:api,coordinateOriginalClose:(fullId,renderer,current)=>coordinateNativeOriginalClose(api,fullId,renderer,current)});
 await f.route.guide('main');api.SetOverlayState(full(A),0);check(received[0]===full(A),'Ready without actual TRUE cannot rewrite original closure');
 f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();api.SetOverlayState(full(A),0);
 check(received[0]===String(A)&&f.route.active&&!f.calls.some(c=>c[0]==='input'&&c[1].action==='closed'),'Full Hidden becomes exact renderer0; adapter fabricates no FALSE/closed/layout command');
 f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(!f.route.active&&api.SetOverlayState===original,'Only actual FALSE plus existing0ACK closes owner and removes close wrapper');
}
{
 let received;const api={SetOverlayState:(...a)=>{received=a}},original=api.SetOverlayState;
 f=fixture({coordinateOriginalClose:(fullId,renderer,current)=>coordinateNativeOriginalClose(api,fullId,renderer,current)});await f.route.guide('main');f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();
 f.advance(1500);api.SetOverlayState(full(A),0);check(received[0]===full(A),'Expired authenticated routing proof cannot lower a closure');
 await f.route.dispose();check(api.SetOverlayState===original,'Dispose removes close wrapper even while native cleanup debt remains');
}
{
 let received;const api={SetOverlayState:(...a)=>{received=a}};
 f=fixture({coordinateOriginalClose:(fullId,renderer,current)=>coordinateNativeOriginalClose(api,fullId,renderer,current)});await f.route.guide('main');f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();f.target.params.browserInfo.m_nBrowserID++;
 api.SetOverlayState(full(A),0);check(received[0]===full(A),'Replaced exact browser cannot borrow old closure token');await f.route.dispose();
}
{
 let received;const api={SetOverlayState:(...a)=>{received=a}};
 f=fixture({coordinateOriginalClose:(fullId,renderer,current)=>coordinateNativeOriginalClose(api,fullId,renderer,current)});await f.route.guide('main');f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();
 const disposing=f.route.dispose();api.SetOverlayState(full(A),0);check(received[0]===full(A),'Synchronous disposed flag revokes lowering before queued teardown runs');await disposing;
}
{
 f=fixture({coordinateOriginalClose:()=>undefined});await f.route.guide('main');check(!f.calls.some(c=>c[0]==='set'&&c[2]===2)&&f.calls.some(c=>c[0]==='trace'&&c[1].includes('original-close-routing')),'Production adapter unavailable refuses before Set2 with existing cleanup path');
}

{
 let received,calls=0;const api={SetOverlayState:(...a)=>{calls++;received=a}};const stop=coordinateNativeOriginalClose(api,full(A),String(A),()=>{throw Error('unknown')});api.SetOverlayState(full(A),0);check(calls===1&&received[0]===full(A),'Unknown current getter delegates original once without lowering');stop();
}
for(const change of[f=>f.bump(),f=>{f.target.BrowserWindow.closed=true},f=>f.setApp(B)]){
 let received;const api={SetOverlayState:(...a)=>{received=a}};
 f=fixture({coordinateOriginalClose:(fullId,renderer,current)=>coordinateNativeOriginalClose(api,fullId,renderer,current)});await f.route.guide('main');f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();change(f);
 api.SetOverlayState(full(A),0);check(received[0]===full(A),'Scope revision/Steam-document/current-app replacement cannot borrow old closure');await f.route.dispose();
}
{
 const api={SetOverlayState(){}},original=api.SetOverlayState;
 f=fixture({closedReply:{ok:true,closed:true,cleanupComplete:false},coordinateOriginalClose:(fullId,renderer,current)=>coordinateNativeOriginalClose(api,fullId,renderer,current)});await f.route.guide('main');f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();
 check(f.route.active&&api.SetOverlayState===original,'ActualFALSE retires wrapper immediately but cannot clear owner before real cleanup0ACK');await f.route.dispose();
}

function cancelFixture(options={}){
 let side=1,allowed=true,retained=true;const calls=[],root={},doc={hasFocus:()=>true},child={document:doc};
 const menu={GetOpenSideMenu:()=>side};const ctx={RootWindow:root,ActiveWindow:child,BIsGamepadInputSuppressed:()=>false};
 const tree={id:'MainNavMenuContainer',Window:child,WindowContext:ctx,Root:{Element:{ownerDocument:doc}}};ctx.m_LastActiveNavTree=tree;
 const api={SetOverlayState(...a){calls.push(a);if(options.nativeThrow)throw options.nativeThrow;return options.nativePromise}};
 const target={BrowserWindow:root,MenuStore:menu,GetFocusNavContext:()=>ctx};
 let count=0;const marker={},nav={GetActiveContext:()=>ctx,OnButtonDown(button,source,index,repeat,type,style,element,context,force){return this.OnButtonActionInternal(true,button,source,index,repeat,element,context,force,type,style)},OnButtonActionInternal(...args){count++;check(this===nav,'original cancel receiver unchanged');
   options.during?.({api,target,ctx,tree,doc,menu,nav,setSide:x=>side=x,setAllowed:x=>allowed=x,setRetained:x=>retained=x});
   if(options.originalThrow)throw options.originalThrow;
   if(!options.keepMenu)side=0;
   if(options.originalSet0)api.SetOverlayState(options.originalToken??full(A),0);
   return marker;}};ctx.m_controller=nav;
 const original=nav.OnButtonActionInternal,captured=nav.OnButtonDown.bind(nav),stop=coordinateNativeOriginalClose(api,full(A),'480',()=>allowed,target,()=>retained);
 return{api,target,ctx,tree,doc,nav,menu,calls,original,captured,stop,marker,count:()=>count,setSide:x=>side=x,setAllowed:x=>allowed=x};
}
{
 const c=cancelFixture();check(typeof c.stop==='function','exact original cancel lane installed');
 check(c.nav.OnButtonDown(2,1,6,false)===c.marker&&c.count()===1,'original cancel return and exactly-once preserved');
 check(c.calls.length===1&&c.calls[0][0]==='480'&&c.calls[0][1]===0,'menu-only B emits exact observed480 Hidden0');
 c.setSide(1);c.nav.OnButtonDown(2,1,6,false);check(c.calls.length===1,'uncertain native dispatch is not repeated by another B');
 c.stop();check(c.nav.OnButtonActionInternal===c.original,'cancel wrapper restored exactly');
}
for(const source of[1,2]){
 const c=cancelFixture();c.tree.id='QuickAccess-NA';c.setSide(2);c.nav.OnButtonDown(2,source,6,false);
 check(c.calls.length===1,'exact QAM cancel with original gamepad/keyboard source closes native once');c.stop();
}
for(const setup of[
 c=>c.setAllowed(false),c=>{c.tree.id='AppRoot'},c=>{c.ctx.ActiveWindow={document:c.doc}},
 c=>{c.ctx.BIsGamepadInputSuppressed=()=>true},c=>{c.doc.hasFocus=()=>false},c=>{c.tree.WindowContext={}},
 c=>c.setSide(2),c=>{c.nav.GetActiveContext=()=>({})},c=>{c.target.BrowserWindow={}}
]){
 const c=cancelFixture();setup(c);c.nav.OnButtonDown(2,1,6,false);check(c.calls.length===0,'stale/foreign/suppressed/unfocused cancel delegates without Hidden');c.stop();
}
for(const args of[[1,1,6,false],[2,3,6,false],[2,1,6,true],[2,1,6,false,undefined,undefined,{},{}]]){
 const c=cancelFixture();c.nav.OnButtonDown(...args);check(c.calls.length===0,'other/repeat/override input has no native effect');c.stop();
}
{
 const c=cancelFixture({keepMenu:true});c.nav.OnButtonDown(2,1,6,false);check(c.calls.length===0,'child dialog/back that keeps menu open does not close native');c.stop();
}
for(const originalToken of[full(A),'480']){
 const c=cancelFixture({originalSet0:true,originalToken});c.nav.OnButtonDown(2,1,6,false);
 check(c.calls.length===1&&c.calls[0][0]==='480','original Hidden dispatch is forwarded/lowered once without corrective duplicate');c.stop();
}
for(const during of[
 x=>x.setRetained(false),x=>{x.target.MenuStore={GetOpenSideMenu:()=>0}},x=>{x.target.BrowserWindow={}},
 x=>{x.api.SetOverlayState=()=>{}},x=>{x.nav.OnButtonActionInternal=()=>{}}
]){
 const c=cancelFixture({during});c.nav.OnButtonDown(2,1,6,false);check(c.calls.length===0,'late owner/window/method replacement vetoes corrective close');c.stop();
}
{
 const error={},c=cancelFixture({originalThrow:error});check((()=>{try{c.nav.OnButtonDown(2,1,6,false)}catch(e){return e===error}})(),'original cancel thrown value preserved');
 check(c.count()===1&&c.calls.length===0,'original throw never creates corrective dispatch');c.stop();
}
{
 const error={},c=cancelFixture({nativeThrow:error});c.nav.OnButtonDown(2,1,6,false);c.setSide(1);c.nav.OnButtonDown(2,1,6,false);
 check(c.calls.length===1,'invoked Hidden throw remains unknown and is never retried');c.stop();
}
{
 const c=cancelFixture({nativePromise:Promise.reject(Error('unknown native result'))});c.nav.OnButtonDown(2,1,6,false);await Promise.resolve();c.setSide(1);c.nav.OnButtonDown(2,1,6,false);
 check(c.calls.length===1,'invoked Hidden rejection remains unknown and does not alter original event return');c.stop();
}

{
 const native=[];const api={SetOverlayState(...args){native.push(args)}};
 const f=fixture({nativeApi:api,coordinateOriginalClose:(fullId,renderer,current,target,cleanup)=>coordinateNativeOriginalClose(api,fullId,renderer,current,target,cleanup)});
 const canonical=2262950469;f.setApp(canonical);f.replaceProof({...base,canonicalAppId:canonical,fullGameId:full(canonical),observedGameId:'480'});
 f.info.appID=480;f.target.params.browserInfo.m_unAppID=480;
 const root=f.target.BrowserWindow,doc={hasFocus:()=>true},child={document:doc};
 const ctx={RootWindow:root,ActiveWindow:child,BIsGamepadInputSuppressed:()=>false};
 const tree={id:'MainNavMenuContainer',Window:child,WindowContext:ctx,Root:{Element:{ownerDocument:doc}}};ctx.m_LastActiveNavTree=tree;
 const nav={GetActiveContext:()=>ctx,OnButtonDown(button,source,index,repeat,type,style,element,context,force){return this.OnButtonActionInternal(true,button,source,index,repeat,element,context,force,type,style)},OnButtonActionInternal(){f.menu.CloseSideMenus()}};ctx.m_controller=nav;f.target.GetFocusNavContext=()=>ctx;
 await f.route.guide('main');f.route.activated(base.overlayPid,480,true);await f.route.eventsSettled();
 nav.OnButtonDown(2,1,6,false);
 check(native.filter(x=>x[0]==='480'&&x[1]===0).length===1,'Forza actual480 shape receives one corrective native Hidden');
 check(f.route.active&&!f.calls.some(x=>x[0]==='input'&&x[1].action==='closed'),'Hidden dispatch never invents false/closed or reader resume');
 f.route.activated(base.overlayPid,480,false);await f.route.eventsSettled();
 check(!f.route.active&&f.calls.some(x=>x[0]==='input'&&x[1].action==='closed'&&x[1].expectedNative.canonicalAppId===canonical),'real exact false and cleanup ACK retire canonical owner without identity rewrite');
}
{
 const c=cancelFixture();c.captured(2,1,6,false);
 check(c.calls.length===1&&c.calls[0][0]==='480','Steam callback captured before registration still enters dynamic original action seam');c.stop();
}
{
 const c=cancelFixture({originalSet0:true,during:x=>x.setAllowed(false)});c.captured(2,1,6,false);
 check(c.calls.length===1&&c.calls[0][0]==='480','same authenticated cancel dispatch lowers original Hidden after UI scope revision changes, without duplication');c.stop();
}

{
 const f=fixture({closedReply:{ok:true,closed:true,cleanupComplete:false}});
 check(f.route.visibility(A)===undefined,'no display binding before authenticated admission');await f.route.guide('main');
 const at=f.calls.length,prepared=f.route.visibility(A);
 check(prepared?.entry===f.target&&prepared.active===false&&prepared.serial==='0','admitted dispatched binding can hide a visible menu without inventing a native TRUE');
 check(f.route.visibility(B)===undefined,'another canonical game cannot borrow native visibility');
 f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();const visible=f.route.visibility(A);
 check(visible?.active===true&&visible.serial==='1'&&Object.isFrozen(visible),'only actual exact TRUE marks copied native visibility active');
 f.route.activated(999,9858785,false);check(f.route.visibility(A)?.active===true,'foreign host cannot clear exact native activity');
 f.advance(4000);check(f.route.visibility(A)?.active===true,'expired routing proof may only retain exact display hiding, never renewed input authority');
 const before=f.calls.length;f.route.visibility(A);check(f.calls.length===before,'visibility getter creates no RPC/input/layout/focus action');
 f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();check(f.route.active&&f.route.visibility(A)?.active===false,'actual FALSE can update display state while cleanup ACK is still pending');
 f.target.params.browserInfo.m_nBrowserID++;check(f.route.visibility(A)===undefined,'replacement native browser cannot inherit old visibility');await f.route.dispose();
 check(f.route.visibility(A)===undefined,'disposed native root cannot lend display binding');
}

// An ordinary menu can have its own temporary layout override without claiming a physical SC relay.
const ordinaryMenuReady={inputMode:'ordinary-menu',inputReady:false,ordinaryReady:true,noOwnedOverride:false,menuLayoutReady:true};
check(readyForNativeMenu({ok:true,...ordinaryMenuReady}),'ordinary-menu requires its own explicit temporary menu-layout receipt');
for(const fields of[{ok:false},{ordinaryReady:false},{ordinaryReady:undefined},{inputReady:true},{inputReady:undefined},{noOwnedOverride:true},{noOwnedOverride:undefined},{menuLayoutReady:false},{menuLayoutReady:undefined},{inputMode:'ordinary'}])
 check(!readyForNativeMenu({ok:true,...ordinaryMenuReady,...fields}),'ordinary-menu missing or contradictory readiness cannot draw');
f=fixture({inputReplyFields:ordinaryMenuReady,closedReply:{ok:true,inputMode:'ordinary-menu',inputReady:false,ordinaryReady:false,noOwnedOverride:true,menuLayoutReady:false,ownerToken:'b'.repeat(32),generation:'1',nativeGame:base,closed:true,cleanupComplete:true}});
await f.route.guide('main');
check(f.calls.findIndex(c=>c[0]==='input'&&c[1].action==='prepare')<f.calls.findIndex(c=>c[0]==='set'&&c[2]===2)&&f.side()===1,'ordinary-menu local readiness precedes original Set2 and Main');
check(f.calls.some(c=>c[0]==='trace'&&c[1].includes('mode=ordinary-menu')&&c[1].includes('menuLayoutReady=true')),'ordinary-menu trace reports the truthful separate layout receipt');
f.route.activated(base.overlayPid,9858785,true);await f.route.eventsSettled();
check(f.route.active&&f.timers.size===1,'actual original TRUE preserves ordinary-menu mode for bounded renewals');
await f.tick();
check(f.calls.some(c=>c[0]==='input'&&c[1].action==='renew')&&f.calls.filter(c=>c[0]==='set'&&c[2]===2).length===1,'ordinary-menu renew preserves owner without opening another overlay');
f.route.activated(base.overlayPid,9858785,false);await f.route.eventsSettled();
check(!f.route.active&&f.calls.some(c=>c[0]==='input'&&c[1].action==='closed'),'actual FALSE plus exact cleanup receipt retires ordinary-menu owner');
f=fixture({prepare:async(_request,ready)=>({...ready(),...ordinaryMenuReady,menuLayoutReady:false})});await f.route.guide('main');
check(!f.calls.some(c=>c[0]==='set'&&c[2]===2),'ordinary-menu cannot Set2 without its local menu-layout readiness');

console.log(JSON.stringify({checks,actualModule:true,noSteamOrNativeCalls:true,passed:true}));
