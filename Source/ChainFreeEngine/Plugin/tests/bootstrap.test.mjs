import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
const source=readFileSync(new URL('../src/standalone-bootstrap.js',import.meta.url),'utf8');
function fixture({missingReact=false,withoutModuleCache=false}={}) {
  const React={createElement(){},useState(){}};
  const ReactDOM={createPortal(){},createRoot(){}};
  function Focusable(){ /* flow-children onActivate focusWithinClassName */ }
  function DialogButton(){ /* "DialogButton" "_DialogLayout" */ }
  function ToggleField(){ /* ToggleField,fallback */ }
  function DropdownItem(){ /* dropDownControlRef description */ }
  const document={title:'Steam Big Picture',body:{},head:{}};
  const exportsByIdRef={value:{}};
  const req=(id)=>{ if(id==='unrelated')throw new Error('Unrelated module'); return exportsByIdRef.value[id]; };
  const exportsById={...(!missingReact?{react:React}:{}),dom:ReactDOM,ui:{Focusable,DialogButton,DropdownItem,ToggleField}};
  // Alcune build di Steam non espongono req.c: li' l'unico modo di vedere un
  // modulo e' il valore restituito da req(id). Il fixture riproduce entrambi i casi.
  exportsByIdRef.value=exportsById;
  if (!withoutModuleCache) req.c=Object.fromEntries(Object.entries(exportsById).map(([id,value])=>[id,{exports:value}]));
  req.m={...Object.fromEntries(Object.keys(exportsById).map(id=>[id,()=>{}])),unrelated:()=>{throw new Error('Unrelated module');}};
  const calls=[];
  const window={__PLAYHUB_HOST_CONFIG__:{baseUrl:'http://127.0.0.1:12345',token:'fixture-token',instanceId:'test'},
    webpackChunksteamui:{},SteamUIStore:{WindowStore:{GamepadUIMainWindowInstance:{BrowserWindow:{document}}}},
  };
  window.webpackChunksteamui=[];
  window.webpackChunksteamui.push=entry=>entry[2](req);
  for(const key of ['DFL','DeckyPluginLoader','DeckyBackend'])Object.defineProperty(window,key,{get(){throw new Error('Decky global must not be read: '+key);}});
  const context=vm.createContext({window,document:{title:'SharedJSContext'},URL,Symbol,Map,console,
    setTimeout: callback => { window.__PLAYHUB_BOOTSTRAP__.cancel(); callback(); },
    fetch:async(url,options)=>{calls.push({url,options});return {ok:true,json:async()=>({ok:true,result:{enabled:true}})};},
  });
  vm.runInContext(source+'\nglobalThis.bootstrap=bootstrapPlayhubStandalone;',context);
  return {context,window,React,ReactDOM,document,calls};
}
test('bootstrap resolves Steam modules without reading any Decky-provided global',async()=>{
  const f=fixture();await f.context.bootstrap();
  assert.equal(f.window.__PLAYHUB_HOST__.React,f.React);
  assert.equal(f.window.__PLAYHUB_HOST__.ReactDOM.createRoot,f.ReactDOM.createRoot);
  assert.equal(f.window.__PLAYHUB_HOST__.targetDocument,f.document);
  assert.equal(f.window.__PLAYHUB_HOST__.diagnostics.moduleCount,3);
  assert.equal(f.window.__PLAYHUB_HOST__.diagnostics.documentTitle,'Steam Big Picture');
});
test('RPC forwards method and arguments through the authenticated isolated host',async()=>{
  const f=fixture();await f.context.bootstrap();
  const result=await f.window.__PLAYHUB_HOST__.call('set_daily_history_settings',false);
  assert.equal(result.enabled,true);
  assert.equal(f.calls[0].url,'http://127.0.0.1:12345/rpc');
  assert.equal(f.calls[0].options.headers.Authorization,'Bearer fixture-token');
  assert.deepEqual(JSON.parse(f.calls[0].options.body),{method:'set_daily_history_settings',args:[false]});
});
test('Steam builds without a module cache are still discovered',async()=>{
  const f=fixture({withoutModuleCache:true});await f.context.bootstrap();
  assert.equal(f.window.__PLAYHUB_HOST__.React,f.React);
  assert.equal(f.window.__PLAYHUB_HOST__.diagnostics.moduleCount,3);
});
test('the renderer is required: no simulated React is substituted',async()=>{
  const f=fixture({missingReact:true});await assert.rejects(f.context.bootstrap(),/cancelled/);
  assert.equal(f.window.__PLAYHUB_HOST__.React, undefined);
  assert.ok(f.calls.some(call => JSON.parse(call.options.body).reason?.includes('React renderer')));
});
test('the globals the component library needs are published, then handed back',async()=>{
  const f=fixture();await f.context.bootstrap();
  assert.equal(f.window.SP_REACT,f.React,'the component library reads window.SP_REACT while loading');
  assert.equal(f.window.SP_REACTDOM.createRoot,f.ReactDOM.createRoot);
  assert.ok(f.window.SP_JSX);
  f.window.__PLAYHUB_HOST__.releaseOwnedGlobals();
  assert.equal('SP_REACT' in f.window,false,'a global we created must not be left behind');
});
test('an existing loader keeps its own globals',async()=>{
  const f=fixture();
  const theirs={createElement(){},useState(){}};
  f.window.SP_REACT=theirs;
  await assert.rejects(f.context.bootstrap(), /Another renderer/);
  assert.equal(f.window.SP_REACT,theirs,'another loader owns that name');
  f.window.__PLAYHUB_HOST__.releaseOwnedGlobals();
  assert.equal(f.window.SP_REACT,theirs,'and keeps it after we detach');
});
test('components are left to the verified adapter, not matched here',async()=>{
  const f=fixture();await f.context.bootstrap();
  assert.equal(f.window.__PLAYHUB_HOST__.DFL,undefined);
  assert.equal(f.window.__PLAYHUB_HOST__.qamAvailable,undefined);
});
test('non-loopback host configuration is refused',async()=>{
  const f=fixture();f.window.__PLAYHUB_HOST_CONFIG__.baseUrl='https://example.com';
  await assert.rejects(f.context.bootstrap(),/loopback/);assert.equal(f.calls.length,0);
});
