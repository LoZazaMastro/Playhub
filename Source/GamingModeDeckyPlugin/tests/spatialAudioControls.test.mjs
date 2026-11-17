import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const source=fs.readFileSync(new URL('../src/quickSettings/SpatialAudioControls.tsx',import.meta.url),'utf8');
const compile=text=>ts.transpileModule(text,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React}}).outputText;
const flush=()=>new Promise(resolve=>setImmediate(resolve));
const deferred=()=>{let resolve;const promise=new Promise(yes=>resolve=yes);return{promise,resolve}};
const off='{00000000-0000-0000-0000-000000000000}',sonic='{B53D8E06-78B2-498C-8C7C-FD20E61AA03C}';
const nativeA='{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}',nativeB='{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}';
const full=id=>'\\\\?\\SWD#MMDEVAPI#'+id+'#{e6327cad-dcec-4949-ae8a-991e976a79d2}';
const state=(extra={})=>({ok:true,code:'read',endpointId:full(nativeA),selectedFormat:sonic,activeFormat:sonic,
  canSet:true,cleanupComplete:true,options:[{id:off,label:'Off',supported:true,requiresProviderLicense:false},
    {id:sonic,label:'Windows Sonic for Headphones',supported:true,requiresProviderLicense:false}],...extra});
function fixture(initial=state()){
  const values=[],refs=[],effects=[],calls=[],timers=new Map(),listeners=new Map();
  let s=0,r=0,e=0,t=0,props={locale:'en',endpointId:nativeA},read=()=>Promise.resolve(initial),write=()=>Promise.resolve(state({ok:false}));
  const react={useState(value){const i=s++;if(!(i in values))values[i]=value;return[values[i],next=>values[i]=next]},
    useRef(value){return refs[r++]??={current:value}},useEffect(fn,deps){const i=e++,old=effects[i];if(!old||deps.some((v,n)=>v!==old.deps[n]))effects[i]={fn,deps,cleanup:old?.cleanup,pending:true}}};
  const document={visibilityState:'visible',addEventListener:(k,f)=>listeners.set('d'+k,f),removeEventListener:k=>listeners.delete('d'+k)};
  const window={setTimeout(fn,ms){timers.set(++t,{fn,ms});return t},clearTimeout:id=>timers.delete(id),setInterval(fn,ms){timers.set(++t,{fn,ms});return t},clearInterval:id=>timers.delete(id),
    addEventListener:(k,f)=>listeners.set('w'+k,f),removeEventListener:k=>listeners.delete('w'+k)};
  const exports={};vm.runInNewContext(compile(source),{exports,React:{createElement:(type,props,...children)=>({type,props,children})},window,document,
    require:name=>name==='react'?react:name==='@decky/ui'?{DropdownItem:'dropdown',DialogButton:'button'}:name==='../controlCenterLocale'?{controlLocale:()=>({retry:'Retry'})}:
      name==='../controlBackend'?{call(method,arg){calls.push([method,arg]);return method==='get_spatial_audio'?read():write(arg)}}:assert.fail(name)});
  const render=()=>{s=r=e=0;const tree=exports.SpatialAudioControls(props);for(const x of effects)if(x.pending){x.pending=false;x.cleanup?.();x.cleanup=x.fn()}return tree};
  const nodes=(tree,result=[])=>{if(Array.isArray(tree))tree.forEach(x=>nodes(x,result));else if(tree&&typeof tree==='object'){result.push(tree);nodes(tree.children,result)}return result};
  render();return{calls,render,read:fn=>read=fn,write:fn=>write=fn,dropdown:()=>nodes(render()).find(x=>x.type==='dropdown'),nodes:()=>nodes(render()),
    props:next=>{props={...props,...next};return render()},focus:()=>listeners.get('wfocus')?.(),timeout:()=>{for(const[k,v]of[...timers])if(v.ms===12000){timers.delete(k);v.fn()}},
    cleanup:()=>effects.forEach(x=>x.cleanup?.()),listeners:()=>listeners.size};
}
test('opening reads the actual endpoint once without changing its audio format',async()=>{
  const f=fixture();await flush();assert.equal(f.calls.length,1);assert.equal(f.calls[0][0],'get_spatial_audio');
  assert.equal(f.dropdown().props.selectedOption,sonic);f.cleanup();assert.equal(f.listeners(),0);
});
test('a format write carries endpoint and expected selected format and waits for both readbacks',async()=>{
  const f=fixture();await flush();const waiting=deferred();f.write(()=>waiting.promise);
  f.dropdown().props.onChange({data:off});assert.equal(f.dropdown().props.disabled,true);
  assert.deepEqual(JSON.parse(JSON.stringify(f.calls[1])),['set_spatial_audio',{endpointId:full(nativeA),expectedFormat:sonic,format:off}]);
  waiting.resolve(state({code:'applied-confirmed',selectedFormat:off,activeFormat:off}));await flush();
  assert.equal(f.dropdown().props.selectedOption,off);assert.equal(f.dropdown().props.disabled,false);f.cleanup();
});
test('exit-zero or selected-only replies never replace the confirmed active format',async()=>{
  for(const reply of [state({ok:false,code:'not-confirmed'}),state({ok:true,selectedFormat:off}),state({ok:true,activeFormat:off,selectedFormat:off,cleanupComplete:false})]){
    const f=fixture();await flush();f.write(async()=>reply);f.dropdown().props.onChange({data:off});await flush();
    assert.equal(f.dropdown().props.selectedOption,sonic);assert.ok(f.nodes().some(x=>x.props?.role==='status'));f.cleanup();
  }
});
test('a menu captured for an old output cannot write after the device changes',async()=>{
  const f=fixture();await flush();const old=f.dropdown().props.onChange;
  f.read(async()=>state({endpointId:full(nativeB)}));f.props({endpointId:nativeB});await flush();
  old({data:off});assert.equal(f.calls.filter(x=>x[0]==='set_spatial_audio').length,0);f.cleanup();
});
test('late writes from the previous endpoint cannot replace the new output state',async()=>{
  const f=fixture();await flush();const waiting=deferred();f.write(()=>waiting.promise);f.dropdown().props.onChange({data:off});
  f.read(async()=>state({endpointId:full(nativeB)}));f.props({endpointId:nativeB});await flush();
  waiting.resolve(state({selectedFormat:off,activeFormat:off}));await flush();assert.equal(f.dropdown().props.selectedOption,sonic);f.cleanup();
});
test('unsupported choices and repeated menu actions cannot start extra commands',async()=>{
  const f=fixture();await flush();const waiting=deferred();f.write(()=>waiting.promise);const choose=f.dropdown().props.onChange;
  choose({data:'not-a-format'});choose({data:sonic});assert.equal(f.calls.length,1);
  choose({data:off});choose({data:off});assert.equal(f.calls.length,2);waiting.resolve(state({activeFormat:off,selectedFormat:off}));await flush();f.cleanup();
});
test('a stalled read expires and a later focus retries, while the expired result stays inert',async()=>{
  const f=fixture();await flush();const slow=deferred();f.read(()=>slow.promise);f.focus();f.timeout();await flush();
  f.read(async()=>state({activeFormat:off,selectedFormat:off}));f.focus();await flush();assert.equal(f.dropdown().props.selectedOption,off);
  slow.resolve(state());await flush();assert.equal(f.dropdown().props.selectedOption,off);f.cleanup();
});
test('a different output, capture endpoint or malformed device path never creates a spatial control',async()=>{
  for(const id of [nativeB,full(nativeB),nativeA.replace('0.0.0.00000000','0.0.1.00000000'),full(nativeA)+'suffix',full(nativeA).replace('e6327cad','e6327cae')]){
    const f=fixture(state({endpointId:id}));await flush();assert.equal(f.dropdown(),undefined);assert.equal(f.calls.length,1);f.cleanup();
  }
});
test('an output offering only spatial-off has no redundant selector',async()=>{
  const f=fixture(state({selectedFormat:off,activeFormat:off,options:[{id:off,label:'Off',supported:true,requiresProviderLicense:false}]}));
  await flush();assert.equal(f.dropdown(),undefined);f.cleanup();
});
