import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';
const source=n=>fs.readFileSync(n==='translationController.ts'&&process.env.PLAYHUB_TRANSLATION_MATCHER_SOURCE
 ?process.env.PLAYHUB_TRANSLATION_MATCHER_SOURCE:new URL('../src/'+n,import.meta.url),'utf8');
const compile=s=>ts.transpileModule(s,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React}}).outputText;
const load=(name,deps={},globals={})=>{const exports={};vm.runInNewContext(compile(source(name)),{exports,require:n=>deps[n],...globals});return exports};
const {translationController:detect}=load('translationController.ts');
// Match SharedJSContext: no k_EControllerType_* globals or lexical bindings.
const device={nControllerIndex:3,eControllerType:10,strSerialNumber:'same-physical'};
const flush=()=>new Promise(resolve=>setImmediate(resolve));
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b});return{promise,resolve,reject}};
function fixture(initial={steamControllerTranslationEnabled:true}){
 const state=[],refs=[],effects=[],events=new Map(),timers=new Map(),calls=[];
 let si=0,ri=0,ei=0,devices=[device],reader=async()=>initial,writer=async changes=>changes,timer=0;
 const jsx=(type,props,...children)=>({type,props:{...props,children}});
 const React={createElement:jsx,useState(value){const i=si++;if(!(i in state))state[i]=value;return[state[i],v=>{state[i]=v}]},
  useRef(value){return refs[ri++]??={current:value}},useEffect(fn,deps){const i=ei++;if(!effects[i])effects[i]={fn,pending:true}}};
 const win={ControllerStore:{GetControllers:()=>devices},setInterval(fn){timers.set(++timer,fn);return timer},clearInterval:i=>timers.delete(i),
  addEventListener:(n,fn)=>events.set(n,fn),removeEventListener:(n,fn)=>{if(events.get(n)===fn)events.delete(n)}};
 const native=load('translationController.ts');
 const component=load('TranslationControllerToggle.tsx',{'./decky':{DFL:{PanelSectionRow:'row',ToggleField:'toggle'},SP_REACT:React},'./translationController':native,
  './api':{readSettings:()=>{calls.push(['read']);return reader()},writeSettings:request=>{calls.push(['write',request]);return writer(request)}}},{window:win});
 const render=(locale='en')=>{si=ri=ei=0;const tree=component.TranslationControllerToggle({locale});for(const e of effects)if(e.pending){e.pending=false;e.cleanup=e.fn()}return tree};
 return {render,calls,state,devices:v=>devices=v,read:fn=>reader=fn,write:fn=>writer=fn,focus:()=>events.get('focus')?.(),poll:()=>[...timers.values()].forEach(f=>f()),
  unmount(){for(const e of effects)e.cleanup?.()},field:tree=>tree?.props?.children?.find(n=>n?.type==='toggle')};
}
test('current native Steam Controller or Triton qualifies without nonexistent globals',()=>{
 const store={GetControllers:()=>[device]};assert.ok(detect(store));
 assert.ok(detect({GetControllers:()=>[{...device,eControllerType:2}]}));
 for(const replacement of [4,17,31,34,45,48,null].map(eControllerType=>({...device,eControllerType,strName:'Steam Controller',unVendorID:0x28de})))
  assert.equal(detect({GetControllers:()=>[replacement]}),null);
 assert.equal(detect({GetControllers:()=>[]}),null);
 assert.equal(detect({GetControllers:()=>{throw Error('inert')}}),null);
});
test('current controller and real Agent preference are both needed before rendering',async()=>{
 const f=fixture();assert.equal(f.render(),null);await flush();assert.equal(f.field(f.render()).props.checked,true);
 f.devices([]);f.poll();assert.equal(f.render(),null);
 const old=fixture({});old.render();await flush();assert.equal(old.render(),null,'old Agents cannot advertise a UI-only setting');
});
test('Italian description is exact and all existing interface locales have a description',async()=>{
 const f=fixture();f.render();await flush();assert.equal(f.field(f.render('it')).props.description,'Fa riconoscere lo Steam Controller come un pad Xbox.');
 for(const locale of ['en','it','es','fr','de','pt','uk','zh','ja','ko','hi','ru'])assert.ok(f.field(f.render(locale)).props.description);
});
test('write is single-flight and stays at the actual value until server readback',async()=>{
 const f=fixture();f.render();await flush();const d=deferred();f.write(()=>d.promise);
 const onChange=f.field(f.render()).props.onChange;const first=onChange(false);onChange(true);
 assert.equal(f.calls.filter(c=>c[0]==='write').length,1);assert.equal(f.field(f.render()).props.checked,true);assert.equal(f.field(f.render()).props.disabled,true);
 d.resolve({steamControllerTranslationEnabled:false});await first;
 assert.equal(f.field(f.render()).props.checked,false);assert.equal(f.field(f.render()).props.disabled,false);
});
test('unknown write readback keeps the actual value disabled and focus recovers it',async()=>{
 const f=fixture();f.render();await flush();f.write(async()=>null);await f.field(f.render()).props.onChange(false);
 assert.equal(f.field(f.render()).props.checked,true);assert.equal(f.field(f.render()).props.disabled,true);
 f.read(async()=>({steamControllerTranslationEnabled:false}));f.focus();await flush();assert.equal(f.field(f.render()).props.checked,false);assert.equal(f.field(f.render()).props.disabled,false);
});
test('disconnect at activation does not send a preference write',async()=>{
 const f=fixture();f.render();await flush();const onChange=f.field(f.render()).props.onChange;f.devices([]);await onChange(false);
 assert.equal(f.calls.filter(c=>c[0]==='write').length,0);
});
test('controller capability polling never polls the Agent settings API',async()=>{
 const f=fixture();f.render();await flush();for(let i=0;i<6;i++)f.poll();assert.equal(f.calls.filter(c=>c[0]==='read').length,1);
});
test('late response after unmount cannot change UI or issue another command',async()=>{
 const f=fixture();f.render();await flush();const d=deferred();f.write(()=>d.promise);const pending=f.field(f.render()).props.onChange(false);
 f.unmount();const snapshot=structuredClone(f.state);d.resolve({steamControllerTranslationEnabled:false});await pending;
 assert.deepEqual(f.state,snapshot);assert.equal(f.calls.filter(c=>c[0]==='write').length,1);
});
