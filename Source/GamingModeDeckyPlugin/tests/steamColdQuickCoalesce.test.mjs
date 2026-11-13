import fs from 'node:fs';import vm from 'node:vm';import assert from 'node:assert/strict';import test from 'node:test';
import ts from 'typescript';
function load(file){const exports={};vm.runInNewContext(ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports});return exports;}
const admission=load(new URL('../src/steamMirrorGuideAdmission.ts',import.meta.url)),native=load(new URL('../src/steamNativeGameOverlay.ts',import.meta.url));
const settle=async()=>{for(let n=0;n<30;n++)await Promise.resolve();};
const defer=()=>{let resolve;const promise=new Promise(r=>resolve=r);return{promise,resolve};};
function joined(){
 const proof=defer(),calls=[],fallback=[],traces=[];let clock=0;
 const nativeRoute=native.createSteamNativeGameOverlay({scope:()=>({app:2979426017,observedApp:2979426017,revision:1,disposed:false}),
  now:()=>clock,proof:()=>{calls.push('proof');return proof.promise;},browserInfo:async()=>[],windowStore:()=>({}),
  input:async()=>{throw Error('No native input expected for packaged');},schedule:()=>0,cancel(){},setOverlayState:async()=>{throw Error('No Set expected');},trace:s=>traces.push(s)});
 const route=admission.createSteamMirrorGuideAdmission({scope:()=>({app:2979426017,revision:1,disposed:false,enabled:true}),now:()=>clock,
  guide:async menu=>{calls.push(menu);const result=await nativeRoute.guide(menu);if(result?.handled===false)fallback.push(menu);},
  pending:()=>nativeRoute.pendingApp===2979426017,active:()=>nativeRoute.active,verifiedAbsent:async()=>false,trace:s=>traces.push(s)});
 return{route,calls,fallback,traces,advance:ms=>clock+=ms,finish:async()=>{proof.resolve({ok:true,kind:'packaged'});await settle();}};
}
test('same system Quick twice during actual native packaged proof produces one Mirror fallback',async()=>{
 const f=joined();f.route.signal('system','quick');await settle();f.advance(394);f.route.signal('system','quick');await f.finish();
 assert.deepEqual(f.fallback,['quick']);assert.equal(f.calls.filter(c=>c==='quick').length,1);
 assert.equal(f.traces.some(t=>t==='open-refused stage=post-proof-intent'),false);
});
test('same-origin Main still cancels actual pending native proof',async()=>{
 const f=joined();f.route.signal('system','main');await settle();f.advance(394);f.route.signal('system','main');await f.finish();
 assert.deepEqual(f.fallback,[]);assert.equal(f.calls.filter(c=>c==='main').length,2);
});
test('Quick after the transaction settles remains a new action',async()=>{
 const f=joined();f.route.signal('system','quick');await settle();await f.finish();f.advance(394);f.route.signal('system','quick');await settle();
 assert.deepEqual(f.fallback,['quick','quick']);
});
test('an intervening intent is not swallowed by an older pending Quick record',()=>{
 const calls=[];const route=admission.createSteamMirrorGuideAdmission({scope:()=>({app:2979426017,revision:1,disposed:false,enabled:true}),now:()=>1000,
  guide:m=>{calls.push(m);return new Promise(()=>{});},pending:()=>true,active:()=>false,verifiedAbsent:async()=>false,trace(){}});
 route.signal('system','quick');route.signal('system','quick');route.signal('system','main');route.signal('system','quick');
 assert.deepEqual(calls,['quick','main','quick']);
});

test('an opposite-origin Quick after Main is not paired with the older Quick',()=>{
 const calls=[];const route=admission.createSteamMirrorGuideAdmission({scope:()=>({app:2979426017,revision:1,disposed:false,enabled:true}),now:()=>1000,
  guide:m=>{calls.push(m);return new Promise(()=>{});},pending:()=>true,active:()=>false,verifiedAbsent:async()=>false,trace(){}});
 route.signal('system','quick');route.signal('system','main');route.signal('quick','quick');
 assert.deepEqual(calls,['quick','main','quick']);
});
