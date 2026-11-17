import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const text=fs.readFileSync(new URL('../src/quickSettings/index.tsx',import.meta.url),'utf8');
const ast=ts.createSourceFile('index.tsx',text,ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX);
const declarations=new Map();
function visit(n){if(ts.isVariableStatement(n))for(const d of n.declarationList.declarations)if(ts.isIdentifier(d.name))declarations.set(d.name.text,n.getText(ast));ts.forEachChild(n,visit)}visit(ast);
const functions=new Map(ast.statements.filter(ts.isFunctionDeclaration).map(n=>[n.name?.text,n.getText(ast)]));
const flush=()=>new Promise(resolve=>setImmediate(resolve));
const deferred=()=>{let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no});return{promise,resolve,reject}};
const snapshot=(enabled=false)=>({ok:true,available:true,gpu:true,rsr:{supported:true,enabled,sharpness:0}});
const names=['amdWriteSlots','amdPendingValues','amdMounted','amdConfirmed','amdUnknown','amdEpoch','amdReadFlight',
 'applyPendingAmdValues','acceptAmdStatus','amdWritesBusy','invalidateAmd','flushAmdWrite','queueAmdWrite','loadAmd'];
function fixture(){
 let displayed=null,unknown=false,pending=false,reads=0,writes=0,notices=0,clock=10000,id=0;
 let read=async()=>snapshot(),write=async()=>({ok:true});const timers=new Map();
 const context={useRef:v=>({current:v}),setAmdState:v=>{displayed=typeof v==='function'?v(displayed):v},
  setAmdReadbackUnknown:v=>unknown=v,setAmdPending:v=>pending=v,Date:{now:()=>clock},AMD_SLIDER_SETTLE:220,
  window:{setTimeout(fn,ms){timers.set(++id,{fn,ms});return id},clearTimeout:id=>timers.delete(id)},
  local:{amdDriverRejected:'rejected'},notify:()=>notices++,setAmd:async(...args)=>{writes++;return write(...args)},getAmdStatus:async()=>{reads++;return read()}};
 const code=[functions.get('amdBoolean'),functions.get('patchAmdValue'),...names.filter(n=>declarations.has(n)).map(n=>declarations.get(n)),
  'globalThis.api={queue:queueAmdWrite,load:loadAmd,accept:acceptAmdStatus,busy:amdWritesBusy,epoch:typeof amdEpoch==="undefined"?{current:0}:amdEpoch,confirmed:typeof amdConfirmed==="undefined"?{current:null}:amdConfirmed,mounted:amdMounted};'].join('\n');
 vm.runInNewContext(ts.transpileModule(code,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,context);
 const a=context.api;a.accept(snapshot(),0);
 return{...a,onRead:f=>read=f,onWrite:f=>write=f,view:()=>({displayed,unknown,pending,reads,writes,notices}),
  run(ms){for(const[key,t]of[...timers])if(t.ms===ms){timers.delete(key);clock+=ms;t.fn()}},timers};
}
test('failed write and failed read restore confirmed state, show unknown, block queued old handlers',async()=>{
 const f=fixture();f.onWrite(async()=>{throw Error('write offline')});f.onRead(async()=>{throw Error('read offline')});
 f.queue('rsr','on',0);assert.equal(f.view().displayed.rsr.enabled,true);
 f.run(0);await flush();await flush();const v=f.view();assert.equal(v.displayed.rsr.enabled,false);assert.equal(v.unknown,true);assert.equal(v.pending,false);assert.equal(v.writes,1);assert.equal(v.reads,1);
 f.queue('rsr','on',0);f.run(0);assert.equal(f.view().writes,1);
});
test('only a fresh read from current epoch clears unknown, then edits can resume',async()=>{
 const f=fixture();f.onWrite(async()=>{throw Error('offline')});f.onRead(async()=>{throw Error('offline')});f.queue('rsr','on',0);f.run(0);await flush();
 const wait=deferred();f.onRead(()=>wait.promise);const p=f.load();f.load();assert.equal(f.view().reads,2);assert.equal(f.view().unknown,true);
 wait.resolve(snapshot(true));await p;assert.equal(f.view().displayed.rsr.enabled,true);assert.equal(f.view().unknown,false);
 f.queue('rsr','off',0);assert.equal(f.view().pending,true);
});
test('write result alone does not confirm a draft; final readback does',async()=>{
 const f=fixture(),read=deferred();f.onRead(()=>read.promise);f.queue('rsr','on',0);f.run(0);await flush();
 assert.equal(f.view().pending,true);assert.equal(f.confirmed.current.rsr.enabled,false);assert.equal(f.view().reads,1);
 read.resolve(snapshot(true));await flush();assert.equal(f.view().pending,false);assert.equal(f.confirmed.current.rsr.enabled,true);
});
test('old read cannot overwrite a newer queued write; slider writes remain coalesced',async()=>{
 const f=fixture(),old=deferred();f.onRead(()=>old.promise);const p=f.load();
 f.queue('rsr_sharpness',20,220);f.queue('rsr_sharpness',30,220);old.resolve(snapshot());await p;
 assert.equal(f.view().displayed.rsr.sharpness,30);assert.equal(f.view().pending,true);assert.equal(f.view().writes,0);
 f.onRead(async()=>({...snapshot(),rsr:{supported:true,enabled:false,sharpness:30}}));f.run(220);await flush();
 assert.equal(f.view().writes,1);assert.equal(f.view().pending,false);assert.equal(f.view().displayed.rsr.sharpness,30);
});
test('late timed-out read cannot overwrite successful Retry or authorize writes',async()=>{
 const f=fixture(),old=deferred();f.onRead(()=>old.promise);const p=f.load();f.run(16000);assert.equal(f.view().unknown,true);
 f.onRead(async()=>snapshot(true));await f.load();assert.equal(f.view().unknown,false);old.resolve(snapshot());await p;
 assert.equal(f.view().displayed.rsr.enabled,true);assert.equal(f.confirmed.current.rsr.enabled,true);
});
test('unmount and invalid GPU response never install unconfirmed state',async()=>{
 const f=fixture(),wait=deferred();f.onRead(()=>wait.promise);const p=f.load();f.mounted.current=false;wait.resolve(snapshot(true));await p;assert.equal(f.view().displayed.rsr.enabled,false);
 const g=fixture();g.onRead(async()=>({...snapshot(true),gpu:false}));await g.load();assert.equal(g.view().displayed.rsr.enabled,false);assert.equal(g.view().unknown,true);
});
test('actual AMD controls keep their section and all become readonly for unknown readback',()=>{
 const block=text.slice(text.indexOf('{caps?.amd_radeon && amd?.available && section('),text.indexOf('{caps?.lossless && section('));
 const controls=[];function scan(n){if(ts.isJsxSelfClosingElement(n)&&['ToggleField','QuickSlider'].includes(n.tagName.getText(ast))&&n.pos>=text.indexOf('{caps?.amd_radeon && amd?.available && section(')&&n.end<=text.indexOf('{caps?.lossless && section('))controls.push(n);ts.forEachChild(n,scan)}scan(ast);
 assert.ok(controls.length>=15);for(const n of controls)assert.ok(n.attributes.properties.some(p=>p.name?.getText(ast)==='disabled'&&p.initializer?.expression?.getText(ast)==='amdReadbackUnknown'));
 assert.match(block,/amdPending/);assert.match(block,/amdReadbackUnknown/);assert.match(block,/loadAmd/);assert.match(block,/controlLocale\(locale\)\.retry/);
});
