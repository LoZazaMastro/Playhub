import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import {fileURLToPath} from 'node:url';
import ts from 'typescript';
import test from 'node:test';
const project=fileURLToPath(new URL('../',import.meta.url));
const file=new URL('../src/quickSettings/Ayaneo3ControllerControls.tsx',import.meta.url);
const transpile=file=>ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React}}).outputText;
function localeModule(name){const exports={};vm.runInNewContext(transpile(path.join(project,'src',name)),{exports});return exports;}
const locale=localeModule('deviceInfoLocale.ts'),control=localeModule('controlCenterLocale.ts');
function load(extra={}){
  const exports={};
  const deps={react:{},'@decky/ui':{},'../controlBackend':{},'../deviceInfoLocale':locale,'../controlCenterLocale':control,...extra};
  vm.runInNewContext(transpile(file),{exports,require:name=>deps[name],React:{createElement:(type,props,...children)=>({type,props:props??{},children})},...extra.globals});
  return exports;
}
const {AyaneoControlModel:Model,validAyaneoSnapshot,hexToHsl,pickerHex}=load();
const snapshot=(token='a',change={})=>({ok:true,available:true,provider:'ayaneo3-hid',token:token.repeat(64),writeAvailable:true,
  restoreAvailable:false,manualRecoveryRequired:false,readbackKind:'vendor-hid-status',modules:{left:2,right:16},
  state:{leftMode:'solid',leftRgb:'112233',rightMode:'pulse',rightRgb:'445566',vibration:'high'},...change});
const flush=()=>new Promise(resolve=>setImmediate(resolve));
const deferred=()=>{let resolve;const promise=new Promise(done=>resolve=done);return{resolve,promise};};
function fixture(){
  let actual=snapshot(),visible=true,read=async()=>actual,apply=async request=>{
    const next={...actual.state};
    if(request.rgbMode){next.leftMode=next.rightMode=request.rgbMode;next.leftRgb=next.rightRgb=request.rgbHex;}
    if(request.vibration)next.vibration=request.vibration;
    actual=snapshot('b',{state:next,restoreAvailable:true,verified:true});return actual;
  },restore=async()=>actual=snapshot('c');
  const calls=[],updates=[];
  const model=new Model({read:()=>{calls.push(['get']);return read();},apply:r=>{calls.push(['set',r]);return apply(r);},
    restore:r=>{calls.push(['restore',r]);return restore(r);},visible:()=>visible,changed:view=>updates.push(view)});
  return{model,calls,updates,actual:()=>actual,setActual:value=>actual=value,onRead:fn=>read=fn,onApply:fn=>apply=fn,onRestore:fn=>restore=fn,visible:value=>visible=value};
}
test('strict source snapshot rejects foreign provider, weak token, invented values or write+uncertain',()=>{
  assert.equal(validAyaneoSnapshot(snapshot()),true);
  for(const patch of [{provider:'other'},{token:'abc'},{writeAvailable:true,manualRecoveryRequired:true},{state:{...snapshot().state,leftRgb:'#112233'}},
    {modules:{left:3,right:16}},{state:{...snapshot().state,vibration:'max'}},{restoreAvailable:1},{readbackKind:'ack-only'}]){
    assert.equal(validAyaneoSnapshot(snapshot('a',patch)),false);
  }
});
test('real color model round-trips hex and rejects CSS/alpha/out-of-range values',()=>{
  for(const color of ['000000','ffffff','ff0000','00ff00','0000ff','112233','f0aa5b']){
    const[h,s,l]=hexToHsl(color);assert.equal(pickerHex(`hsla(${h}, ${s}%, ${l}%, 1)`),color);
  }
  for(const value of ['red','hsla(361, 50%, 50%, 1)','hsla(50, 101%, 50%, 1)','hsla(50, 50%, 50%, .5)','url(x)'])assert.equal(pickerHex(value),undefined);
});
test('opening/refreshing is read-only; unsupported state exposes no write',async()=>{
  const f=fixture();await f.model.refresh();assert.equal(f.calls.length,1);assert.ok(f.model.view.snapshot);
  f.onRead(async()=>({available:false}));await f.model.refresh();f.model.edit({vibration:'low'});await f.model.run('apply');
  assert.equal(f.calls.filter(x=>x[0]==='set').length,0);assert.equal(f.model.view.snapshot,undefined);
});
test('vibration-only edit preserves asymmetric RGB by omitting RGB fields from actual RPC',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});await f.model.run('apply');
  const request=f.calls.find(x=>x[0]==='set')[1];assert.deepEqual(Object.keys(request).sort(),['token','vibration']);
  assert.equal(f.model.view.snapshot.state.rightRgb,'445566');assert.equal(f.model.view.error,undefined);
});
test('RGB edits send explicit mode+hex; reverting unchanged vibration emits no request',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});f.model.edit({vibration:'high'});
  assert.equal(f.model.view.editing,false);await f.model.run('apply');assert.equal(f.calls.length,1);
  f.model.edit({rgbHex:'ff0000'});await f.model.run('apply');
  const request=f.calls.find(x=>x[0]==='set')[1];assert.equal(request.rgbMode,'solid');assert.equal(request.rgbHex,'ff0000');assert.equal(request.vibration,undefined);
});
test('token replacement in the fresh read rejects before any native write',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});f.onRead(async()=>snapshot('d'));
  await f.model.run('apply');assert.equal(f.calls.filter(x=>x[0]==='set').length,0);assert.equal(f.model.view.error,'changed');assert.equal(f.model.view.editing,false);
});
test('polling during editing preserves draft only while actual token stays current',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});await f.model.refresh();assert.equal(f.model.view.draft.vibration,'low');
  f.onRead(async()=>snapshot('d'));await f.model.refresh();assert.equal(f.model.view.editing,false);assert.equal(f.model.view.draft.vibration,'high');
});
test('double activation serializes one explicit write and requires actual post-readback',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});const pending=deferred();f.onApply(()=>pending.promise);
  const run=f.model.run('apply');await flush();await f.model.run('apply');assert.equal(f.calls.filter(x=>x[0]==='set').length,1);
  pending.resolve({...snapshot('b'),verified:true});await run;
  assert.equal(f.model.view.error,'failed');assert.equal(f.model.view.snapshot.token,'a'.repeat(64));
});
test('ACK/ok without the matching typed status never reports success',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});f.onApply(async()=>({ok:true,acknowledged:true}));
  await f.model.run('apply');assert.equal(f.model.view.error,'failed');
});
test('late unmount before fresh read completion cannot dispatch',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});const pending=deferred();f.onRead(()=>pending.promise);
  const run=f.model.run('apply');f.model.dispose();pending.resolve(snapshot());await run;
  assert.equal(f.calls.filter(x=>x[0]==='set').length,0);
});
test('unmount after dispatch does not issue another RPC or invent rollback',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});const pending=deferred();f.onApply(()=>pending.promise);
  const run=f.model.run('apply');await flush();f.model.dispose();const count=f.calls.length;pending.resolve(snapshot('b',{verified:true}));await run;
  assert.equal(f.calls.length,count);assert.equal(f.calls.filter(x=>x[0]==='restore').length,0);
});
test('visibility loss during preflight blocks dispatch',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});const pending=deferred();f.onRead(()=>pending.promise);
  const run=f.model.run('apply');f.visible(false);pending.resolve(snapshot());await run;assert.equal(f.calls.filter(x=>x[0]==='set').length,0);
});
test('existing async poll settles before action fresh read, with no overlapping reads',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});const pending=deferred();let count=0;
  f.onRead(()=>++count===1?pending.promise:Promise.resolve(f.actual()));const poll=f.model.refresh(),run=f.model.run('apply');
  assert.equal(count,1);pending.resolve(snapshot());await Promise.all([poll,run]);assert.equal(f.calls.filter(x=>x[0]==='set').length,1);
});
test('fresh writeAvailable=false cannot be promoted by an older UI state',async()=>{
  const f=fixture();await f.model.refresh();f.model.edit({vibration:'low'});f.onRead(async()=>snapshot('a',{writeAvailable:false}));
  await f.model.run('apply');assert.equal(f.calls.filter(x=>x[0]==='set').length,0);assert.equal(f.model.view.error,'failed');
});
test('explicit restore can recover owned target even when new apply is blocked',async()=>{
  const f=fixture();f.onRead(async()=>snapshot('a',{writeAvailable:false,manualRecoveryRequired:true,restoreAvailable:true}));await f.model.refresh();
  f.onRestore(async()=>snapshot('c'));let reads=0;f.onRead(async()=>++reads===1?snapshot('a',{writeAvailable:false,manualRecoveryRequired:true,restoreAvailable:true}):snapshot('c'));
  await f.model.run('restore');assert.equal(f.calls.filter(x=>x[0]==='restore').length,1);assert.equal(f.model.view.error,undefined);
});

function componentFixture(language='en'){
  const states=[],refs=[],listeners=new Map(),timers=new Map(),calls=[];let si=0,ri=0,effect;
  const ui={ButtonItem:'ButtonItem',DropdownItem:'DropdownItem',ConfirmModal:'ConfirmModal',SliderField:'SliderField',showModal:()=>({Close(){}})};
  const ex=load({react:{useState(value){const index=si++;if(!(index in states))states[index]=value;return[states[index],v=>states[index]=v];},
    useRef(value){return refs[ri++]??=( {current:value});},useEffect(fn){effect??=fn;}},'@decky/ui':ui,
    '../controlBackend':{call:async(method,request)=>{calls.push([method,request]);return snapshot();}},
    globals:{document:{visibilityState:'visible',addEventListener:(k,v)=>listeners.set(k,v),removeEventListener:k=>listeners.delete(k)},
      window:{setTimeout:fn=>{const key={};timers.set(key,fn);return key;},clearTimeout:key=>timers.delete(key),
        addEventListener:(k,v)=>listeners.set(k,v),removeEventListener:k=>listeners.delete(k)}}});
  function render(){si=ri=0;return ex.Ayaneo3ControllerControls({locale:language,deviceKey:'actual-ayaneo3'});}
  render();const cleanup=effect();return{render,cleanup,calls,listeners,timers,exports:ex,reset(){si=ri=0;}};
}
const nodes=(tree)=>!tree?[]:Array.isArray(tree)?tree.flatMap(nodes):typeof tree==='object'?[tree,...nodes(tree.children)]:[];
test('actual component uses native Decky dropdowns and binds only get on mount',async()=>{
  const f=componentFixture();await flush();const tree=f.render();assert.equal(nodes(tree).filter(x=>x.type==='DropdownItem').length,2);
  assert.deepEqual(f.calls.map(x=>x[0]),['get_ayaneo3_controller']);f.cleanup();assert.equal(f.listeners.size,0);assert.equal(f.timers.size,0);
});
test('actual native RGB modal has exactly three localized HSL sliders and no alpha control',()=>{
  const f=componentFixture('it');f.reset();const tree=f.exports.AyaneoColorPicker({locale:'it',color:'ff0000',closeModal(){},onConfirm(){}});
  assert.equal(tree.type,'ConfirmModal');const sliders=nodes(tree).filter(x=>x.type==='SliderField');
  assert.deepEqual(sliders.map(x=>x.props.label),['Tonalità','Saturazione','Luminosità']);assert.equal(sliders.length,3);f.cleanup();
});
test('all twelve deviceLanguage locales render actual labels and native modal fields',async()=>{
  const titles=new Set();
  for(const language of ['en','it','de','es','fr','pt','ru','uk','ja','ko','zh','hi']){
    const f=componentFixture(language);await flush();titles.add(f.render().children[0].children.join(''));
    f.reset();const picker=f.exports.AyaneoColorPicker({locale:language,color:'112233',closeModal(){},onConfirm(){}});
    assert.equal(nodes(picker).filter(x=>x.type==='SliderField').length,3);f.cleanup();
  }
  assert.equal(titles.size,12);assert.equal(locale.deviceLanguage('koreana'),'ko');assert.equal(locale.deviceLanguage('brazilian'),'pt');
});
