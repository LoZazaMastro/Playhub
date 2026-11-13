import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../GamingModeAgent/GamingMode.Services/SteamTaskbarMainProof.cs',import.meta.url),'utf8');
const expression=source.match(/ReadCurrentMain="""([\s\S]*?)""";/)[1];
function setup(){
  const document={body:{classList:{contains:n=>n==='WindowFocus'}},documentElement:{},hasFocus:()=>true};
  const view={document,closed:false,devicePixelRatio:2,innerWidth:1920,innerHeight:1080,SteamClient:{Browser:{GetBrowserID:()=>35}}};
  const main={IsMainGamepadUIWindow:()=>true,IsGamepadUIOverlayWindow:()=>false,IsAnyVRWindow:()=>false,BrowserWindow:view,params:{browserInfo:{m_unPID:123}}};
  const store={GamepadUIMainWindowInstance:main};let calls=0;
  const window={SteamUIStore:{WindowStore:store,ActiveWindowInstance:main},SteamClient:{UI:{GetUIMode:async()=>{calls++;return 4;}}}};
  return{document,view,main,store,window,run:()=>vm.runInNewContext(expression,{window,Promise,Number,Math}),modeCalls:()=>calls};
}
test('synchronous browser ID, native mode and physical dimensions',async()=>{const s=setup(),p=await s.run();assert.equal(p.browserId,35);assert.equal(p.physicalWidth,3840);assert.equal(s.modeCalls(),2);});
test('actual Promise browser ID is awaited',async()=>{const s=setup();s.view.SteamClient.Browser.GetBrowserID=()=>Promise.resolve(35);assert.equal((await s.run()).browserId,35);});
for(const [name,mutate] of [
  ['document',s=>s.view.document={...s.document}],['body',s=>s.document.body={...s.document.body}],
  ['root',s=>s.document.documentElement={}],['store',s=>s.window.SteamUIStore.WindowStore={...s.store}],
  ['main',s=>s.store.GamepadUIMainWindowInstance={...s.main}],['view',s=>s.main.BrowserWindow={...s.view}],
  ['active',s=>s.window.SteamUIStore.ActiveWindowInstance={}],['closed',s=>s.view.closed=true],
  ['main flag',s=>s.main.IsMainGamepadUIWindow=()=>false],['overlay flag',s=>s.main.IsGamepadUIOverlayWindow=()=>true]
])test(`browser await revalidates ${name}`,async()=>{
  const s=setup();let release;let started;
  const browserStarted=new Promise(r=>started=r);const pending=new Promise(r=>release=r);
  s.view.SteamClient.Browser.GetBrowserID=()=>{started();return pending;};const result=s.run();await browserStarted;mutate(s);release(35);assert.equal(await result,null);
});
test('mode changed across browser await is refused',async()=>{const s=setup();let n=0;s.window.SteamClient.UI.GetUIMode=async()=>++n===1?4:7;assert.equal(await s.run(),null);});
test('unknown browser ID and invalid DPR are refused',async()=>{for(const id of [0,-1,undefined,1.5]){const s=setup();s.view.SteamClient.Browser.GetBrowserID=()=>id;assert.equal(await s.run(),null);}const s=setup();s.view.devicePixelRatio=Infinity;assert.equal(await s.run(),null);});
test('getter failure returns no receipt, invokes no mutation',async()=>{const s=setup();s.view.SteamClient.Browser.GetBrowserID=()=>{throw Error('loading');};assert.equal(await s.run(),null);});
