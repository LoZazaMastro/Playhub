import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import test from 'node:test';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
const project=fileURLToPath(new URL('../',import.meta.url));
const context=process.env.GPD_TEST_PROJECT || new URL('../package.json',import.meta.url);
const ts=createRequire(context)('typescript');
const dependencies=process.env.GPD_TEST_PROJECT?path.dirname(process.env.GPD_TEST_PROJECT):project;
const read=relative=>fs.readFileSync(fs.existsSync(path.join(project,relative))?path.join(project,relative):path.join(dependencies,relative),'utf8');
const options={module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React};
const compile=text=>ts.transpileModule(text,{compilerOptions:options}).outputText;
function locales(file){const exports={};vm.runInNewContext(compile(read('src/'+file)),{exports});return exports;}
const device=locales('deviceInfoLocale.ts'),control=locales('controlCenterLocale.ts');
const componentSource=read('src/quickSettings/GpdV1ControllerControls.tsx');
function load(extra={}){
 const exports={},deps={'@decky/ui':{ButtonItem:'button',DropdownItem:'select'},react:{},'../controlBackend':{},'../controlCenterLocale':control,'../deviceInfoLocale':device,...extra.deps};
 vm.runInNewContext(compile(componentSource),{exports,require:x=>deps[x],React:{createElement:(type,props,...children)=>({type,props:props??{},children})},...extra.globals});
 return exports;
}
const {GpdControlModel:Model,validGpdSnapshot}=load();
const snapshot=(letter='a',patch={})=>({ok:true,available:true,provider:'gpd-v1-hid',profile:'gpd-win-4',token:letter.repeat(64),
 writeAvailable:true,restoreAvailable:false,recoveryRequired:false,readbackKind:'gpd-v1-config128-checksum-double-read',
 state:{rumble:'off'},firmware:{x:[1,2],k:[4,7]},experimental:true,hardwareValidated:false,...patch});
const deferred=()=>{let resolve;const promise=new Promise(done=>resolve=done);return{resolve,promise};};
const flush=()=>new Promise(done=>setImmediate(done));
function fixture(){
 let actual=snapshot(),visible=true,read=async()=>actual,apply=async req=>actual=snapshot('b',{state:{rumble:req.rumble},restoreAvailable:true,verified:true}),restore=async()=>actual=snapshot('c',{verified:true});
 const calls=[],changes=[];
 const model=new Model({read:()=>{calls.push(['get']);return read();},apply:req=>{calls.push(['set',req]);return apply(req);},
 restore:req=>{calls.push(['restore',req]);return restore(req);},visible:()=>visible,changed:view=>changes.push(view)},'gpd-win-4');
 return{model,calls,changes,setActual:x=>actual=x,onRead:fn=>read=fn,onApply:fn=>apply=fn,onRestore:fn=>restore=fn,visible:x=>visible=x};
}
test('strict native report rejects guessed firmware, missing proof and non-typed flags',()=>{
 assert.equal(validGpdSnapshot(snapshot()),true);
 for(const patch of[{provider:'other'},{token:'x'},{firmware:{x:[1,2],k:[4,8]}},{state:{rumble:'medium'}},{writeAvailable:1},
 {recoveryRequired:true},{hardwareValidated:true},{firmware:{x:[true,2],k:[4,7]}}])assert.equal(validGpdSnapshot(snapshot('a',patch)),false,JSON.stringify(patch));
 assert.equal(validGpdSnapshot(snapshot(),'gpd-win-mini'),false);
});
test('editing draft never dispatches; explicit Apply performs fresh get and verifies post-read',async()=>{
 const f=fixture();await f.model.refresh();f.model.edit('high');assert.equal(f.calls.filter(x=>x[0]==='set').length,0);
 await f.model.run('apply');assert.deepEqual(f.calls.map(x=>x[0]),['get','get','set','get']);assert.deepEqual({...f.calls[2][1]},{token:'a'.repeat(64),rumble:'high'});
 assert.equal(f.model.view.snapshot.state.rumble,'high');assert.equal(f.model.view.error,undefined);
});
test('foreign token in last fresh get prevents apply and clears old draft',async()=>{
 const f=fixture();await f.model.refresh();f.model.edit('high');f.setActual(snapshot('b',{state:{rumble:'low'}}));await f.model.run('apply');
 assert.equal(f.calls.some(x=>x[0]==='set'),false);assert.equal(f.model.view.draft,'low');assert.equal(f.model.view.error,'changed');
});
test('lost device before action never calls setter',async()=>{
 const f=fixture();await f.model.refresh();f.model.edit('high');f.setActual({ok:false,reason:'removed'});await f.model.run('apply');
 assert.equal(f.calls.some(x=>x[0]==='set'),false);assert.equal(f.model.view.snapshot,undefined);
});
test('receipt without independent matching post-read stays failed',async()=>{
 const f=fixture();await f.model.refresh();f.model.edit('high');f.onApply(async()=>snapshot('b',{state:{rumble:'high'},verified:true}));await f.model.run('apply');
 assert.equal(f.model.view.error,'failed');assert.equal(f.model.view.snapshot.state.rumble,'off');
});
test('failed native setter is never converted into success by a later get',async()=>{
 const f=fixture();await f.model.refresh();f.model.edit('high');f.onApply(async()=>({ok:false,reason:'unknown'}));await f.model.run('apply');assert.equal(f.model.view.error,'failed');
});
test('restore accepts only latest server token, no client baseline',async()=>{
 const f=fixture();f.setActual(snapshot('a',{state:{rumble:'high'},restoreAvailable:true}));await f.model.refresh();await f.model.run('restore');
 const sent=f.calls.find(x=>x[0]==='restore')[1];assert.deepEqual({...sent},{token:'a'.repeat(64)});assert.equal(f.model.view.snapshot.state.rumble,'off');
});
test('uncertain state remains read-only and cannot restore',async()=>{
 const f=fixture();f.setActual(snapshot('a',{writeAvailable:false,recoveryRequired:true}));await f.model.refresh();f.model.edit('high');await f.model.run('restore');await f.model.run('apply');
 assert.equal(f.calls.some(x=>x[0]!=='get'),false);assert.equal(f.model.view.editing,false);
});
test('unmount during fresh-read await prevents late mutation or publication',async()=>{
 const f=fixture();await f.model.refresh();f.model.edit('high');const d=deferred();f.onRead(()=>d.promise);const p=f.model.run('apply');await flush();f.model.dispose();const n=f.changes.length;d.resolve(snapshot());await p;
 assert.equal(f.calls.some(x=>x[0]==='set'),false);assert.equal(f.changes.length,n);
});
test('visibility loss during fresh read prevents late setter',async()=>{
 const f=fixture();await f.model.refresh();f.model.edit('high');const d=deferred();f.onRead(()=>d.promise);const p=f.model.run('apply');await flush();f.visible(false);d.resolve(snapshot());await p;
 assert.equal(f.calls.some(x=>x[0]==='set'),false);
});
test('singleflight action suppresses double apply',async()=>{
 const f=fixture();await f.model.refresh();f.model.edit('high');const d=deferred();f.onRead(()=>d.promise);const p=f.model.run('apply');const q=f.model.run('apply');await flush();d.resolve(snapshot());await Promise.all([p,q]);assert.equal(f.calls.filter(x=>x[0]==='set').length,1);
});
test('hidden component never reads or edits',async()=>{
 const f=fixture();f.visible(false);await f.model.refresh();f.model.edit('high');assert.deepEqual(f.calls,[]);assert.equal(f.model.view.editing,false);
});
test('foreign profile is refused despite a correct token',async()=>{
 const f=fixture();f.setActual(snapshot('a',{profile:'gpd-win-mini',firmware:{x:[1,2],k:[5,4]}}));await f.model.refresh();assert.equal(f.model.view.snapshot,undefined);
});
test('all twelve locales render native controls without issuing RPC on render',()=>{
 for(const locale of['en','it','de','es','fr','pt','ru','uk','ja','ko','zh','hi']){
  const effects=[],calls=[];let index=0;
  const hooks={useState:()=>[index++===0?{editing:false,busy:false,snapshot:snapshot(),draft:'off'}:undefined,()=>{}],useRef:x=>({current:x}),useEffect:fn=>effects.push(fn)};
  const {GpdV1ControllerControls:Component}=load({deps:{react:hooks,'../controlBackend':{call:(...args)=>calls.push(args)}}});
  const tree=Component({locale,deviceKey:'fixture',profile:'gpd-win-4'});assert.equal(calls.length,0);assert.equal(effects.length,1);
  const text=JSON.stringify(tree);assert.ok(!text.includes('undefined'));assert.ok(text.includes('select'));assert.ok(text.includes('GPD'));
 }
});
function mount(profile){
 const ast=ts.createSourceFile('DeviceSettings.tsx',read('src/DeviceSettings.tsx'),ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX);
 const fn=ast.statements.find(x=>ts.isFunctionDeclaration(x)&&x.name?.text==='DeviceSettings');const exports={};let index=0;const calls=[];
 const React={useState:()=>[[profile,undefined,false,0,undefined][index++],()=>{}],useEffect:()=>{},useRef:x=>({current:x}),createElement:(type,props,...children)=>({type,props:props??{},children})};
 vm.runInNewContext(compile(fn.getText(ast)+'\nexport {DeviceSettings};'),{exports,React,deviceInfoLocale:device.deviceInfoLocale,style:'',DialogButton:'button',DropdownItem:'select',Focusable:'div',DeviceInfo:'details',DeviceCapabilities:'capabilities',Ayaneo3ControllerControls:'aya',GpdV1ControllerControls:'gpd-controls',TbBattery:'battery',TbChevronDown:'down',TbChevronRight:'right',TbDeviceDesktop:'desktop',TbDeviceGamepad2:'gamepad',call:async(...x)=>calls.push(x)});
 return{tree:exports.DeviceSettings({locale:'en'}),calls};
}
const base={selection:'auto',detected:'gpd-win-4',effective:'gpd-win-4',manufacturer:'GPD',model:'G1618-04',handhelds:[],power:{available:false},display:null,powerModeAvailable:false};
test('actual DeviceSettings mounts GPD only for detected matching profile',()=>{
 const good=mount(base);assert.match(JSON.stringify(good.tree),/gpd-controls/);assert.deepEqual(good.calls,[]);
 for(const patch of[{detected:'pc',selection:'gpd-win-4'},{effective:'pc'},{detected:'gpd-win-5',effective:'gpd-win-5'}])assert.doesNotMatch(JSON.stringify(mount({...base,...patch}).tree),/gpd-controls/);
});
