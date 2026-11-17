import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const source = name => fs.readFileSync(new URL('../src/'+name, import.meta.url), 'utf8');
const compile = text => ts.transpileModule(text, {compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React}}).outputText;
const load = (text, dependencies={}, globals={}) => {const exports={};vm.runInNewContext(compile(text),{exports,require:name=>{assert.ok(name in dependencies,name);return dependencies[name]},...globals});return exports};
const device = load(source('deviceInfoLocale.ts'));
const words = load(source('quickSettings/nvidiaLocale.ts'),{'../deviceInfoLocale':device});
const common = {retry:'Retry',expand:'Expand',collapse:'Collapse'};
const flush = () => new Promise(resolve=>setImmediate(resolve));
const deferred = () => {let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no});return {promise,resolve,reject}};
const clone = value => JSON.parse(JSON.stringify(value));
const settings = () => [
  {id:'frame_limit',value:60,default:false,supported:true,editable:true,min:20,max:1000,custom:false},
  {id:'vsync',value:0x60925292,default:false,supported:true,editable:true,values:[0x60925292,0x08416747,0x47814940],custom:false},
  {id:'power',value:1,default:true,supported:true,editable:true,values:[1],custom:false},
];
const state = changes => ({available:true,global:true,status:'ready',token:'a'.repeat(64),settings:settings(),...changes});

function fixture(initial=state(),initialCollapsed=false) {
  const values=[],refs=[],effects=[],calls=[],timers=new Map(),windowEvents=new Map(),documentEvents=new Map();
  let stateIndex=0,refIndex=0,effectIndex=0,clock=10000,timerId=0,alive=true;
  let props={locale:'en',collapsed:initialCollapsed,onToggle:id=>calls.push(['toggle',id])};
  let read=async()=>initial,write=async request=>{
    const next=state({status:'persisted',ok:true,token:'b'.repeat(64)});
    const setting=next.settings.find(item=>item.id===request.feature);
    if(request.value==='default')setting.default=true;else {setting.value=request.value;setting.default=false}
    return next;
  };
  const element=(type,properties,...children)=>{
    const all={...properties,children:children.length===1?children[0]:children};
    return typeof type==='function'?type(all):{type,props:all,children};
  };
  const react={useState(value){const i=stateIndex++;if(!(i in values))values[i]=typeof value==='function'?value():value;return[values[i],next=>{values[i]=typeof next==='function'?next(values[i]):next}]},
    useRef(value){return refs[refIndex++]??={current:value}},
    useEffect(fn,deps){const i=effectIndex++,old=effects[i];if(!old||!deps||deps.some((value,n)=>value!==old.deps?.[n]))effects[i]={fn,deps,cleanup:old?.cleanup,pending:true}}};
  const ui={DialogButton:'button',DropdownItem:'dropdown',SliderField:'slider'};
  const section=load(source('ControlSection.tsx'),{'./decky':{SP_REACT:{createElement:element},DFL:{DialogButton:'button',Focusable:'focusable'}},'react-icons/tb':{TbChevronDown:'down',TbChevronRight:'right'},'./controlCenterLocale':{controlLocale:()=>common}});
  const document={visibilityState:'visible',addEventListener:(name,fn)=>documentEvents.set(name,fn),removeEventListener:(name,fn)=>{if(documentEvents.get(name)===fn)documentEvents.delete(name)}};
  const globals={React:{createElement:element},Date:{now:()=>clock},document,
    window:{addEventListener:(name,fn)=>windowEvents.set(name,fn),removeEventListener:(name,fn)=>{if(windowEvents.get(name)===fn)windowEvents.delete(name)},
      setTimeout(fn,ms){timers.set(++timerId,{fn,ms});return timerId},clearTimeout(id){timers.delete(id)}}};
  const module=load(source('quickSettings/NvidiaControls.tsx'),{
    react,'@decky/ui':ui,'react-icons/fa':{FaMicrochip:'gpu'},'../ControlSection':section,'../controlCenterLocale':{controlLocale:()=>common},'./nvidiaLocale':words,
    '../controlBackend':{async call(method,request){calls.push([method,clone(request??null)]);return method==='get_nvidia_settings'?read():write(request)}}
  },globals);
  const render=()=>{stateIndex=refIndex=effectIndex=0;const tree=module.NvidiaControls(props);for(const effect of effects){if(effect?.pending){effect.pending=false;effect.cleanup?.();effect.cleanup=effect.fn()}}return tree};
  const nodes=(tree,result=[])=>{if(Array.isArray(tree)){for(const child of tree)nodes(child,result)}else if(tree&&typeof tree==='object'){result.push(tree);nodes(tree.children??[],result)}return result};
  const all=()=>nodes(render()),byLabel=(type,label)=>all().find(node=>node.type===type&&node.props.label===label);
  render();
  return {module,calls,timers,values,render,all,byLabel,words,
    button:label=>all().find(node=>node.type==='button'&&node.children.flat().includes(label)),
    onRead:fn=>read=fn,onWrite:fn=>write=fn,
    focus(){windowEvents.get('focus')?.()},visible(value){document.visibilityState=value;documentEvents.get('visibilitychange')?.()},
    advance(ms){clock+=ms},timeout(){for(const timer of [...timers.values()])timer.fn()},
    setProps(next){props={...props,...next};return render()},
    cleanup(){if(!alive)return;alive=false;for(const effect of effects)effect.cleanup?.()},
    replay(){for(const effect of effects){effect.cleanup?.();effect.pending=true}return render()},
    listeners:()=>windowEvents.size+documentEvents.size,
  };
}

test('opening is one read, omits the general introduction and performs no driver writes',async()=>{
  const f=fixture();await flush();assert.deepEqual(f.calls,[['get_nvidia_settings',null]]);
  assert.equal(f.all().some(node=>node.children.includes(words.nvidiaLocale('en').scope)),false);
  assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x60925292);
  assert.equal(f.byLabel('dropdown','Power mode').props.selectedOption,'default');f.cleanup();
});
test('unavailable or other-vendor initial replies produce no empty NVIDIA section',async()=>{
  for(const initial of [{available:false,status:'unsupported'},null,{available:true,global:false},state({settings:[]})]){
    const f=fixture(initial);await flush();assert.equal(f.render(),null);assert.equal(f.calls.length,1);f.cleanup();
  }
});
test('actual ControlSection hides controls when collapsed and keeps persistent id nvidia',async()=>{
  const f=fixture(state(),true);await flush();assert.equal(f.all().some(node=>node.type==='dropdown'),false);
  const heading=f.all().find(node=>node.props['aria-expanded']===false);assert.ok(heading);heading.props.onClick();assert.deepEqual(f.calls.at(-1),['toggle','nvidia']);
  f.setProps({collapsed:false});assert.ok(f.byLabel('dropdown','Vertical sync'));f.cleanup();
});
test('FPS dragging stages only; explicit Apply sends one exact token/feature/value',async()=>{
  const f=fixture();await flush();for(const value of [61,62,63,144])f.byLabel('slider','Frame limit').props.onChange(value);
  assert.equal(f.calls.length,1);assert.equal(f.byLabel('slider','Frame limit').props.value,144);
  f.button('Apply').props.onClick();await flush();assert.deepEqual(f.calls[1],['set_nvidia_setting',{token:'a'.repeat(64),feature:'frame_limit',value:144}]);assert.equal(f.button('Apply'),undefined);f.cleanup();
});
test('FPS off and single-setting driver-default are explicit draft operations',async()=>{
  for(const [mode,wanted] of [['off',0],['default','default']]){
    const f=fixture();await flush();f.byLabel('dropdown','Frame limit').props.onChange({data:mode});assert.equal(f.calls.length,1);
    f.button('Apply').props.onClick();await flush();assert.equal(f.calls[1][1].feature,'frame_limit');assert.equal(f.calls[1][1].value,wanted);f.cleanup();
  }
});
test('FPS rejects forged/out-of-range/noninteger input without writes',async()=>{
  const f=fixture();await flush();for(const value of [19,1001,NaN,Infinity,24.5,-1])f.byLabel('slider','Frame limit').props.onChange(value);
  assert.equal(f.byLabel('slider','Frame limit').props.value,60);assert.equal(f.button('Apply'),undefined);assert.equal(f.calls.length,1);f.cleanup();
});
test('VSync options use exactly driver-default and three genuine raw values',async()=>{
  const f=fixture();await flush();const field=f.byLabel('dropdown','Vertical sync');assert.deepEqual(clone(field.props.rgOptions.map(option=>option.data)),['default',0x60925292,0x08416747,0x47814940]);
  field.props.onChange({data:0});field.props.onChange({data:2});assert.equal(f.calls.length,1);field.props.onChange({data:0x47814940});await flush();assert.equal(f.calls.at(-1)[1].value,0x47814940);f.cleanup();
});
test('dropdown selected state stays confirmed while single-flight write is outstanding',async()=>{
  const f=fixture();await flush();const pending=deferred();f.onWrite(()=>pending.promise);
  const field=f.byLabel('dropdown','Vertical sync');field.props.onChange({data:0x47814940});field.props.onChange({data:0x08416747});f.focus();
  assert.equal(f.calls.length,2);assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x60925292);assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,true);
  const next=state({status:'persisted',ok:true,token:'b'.repeat(64)});next.settings[1].value=0x47814940;pending.resolve(next);await flush();assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x47814940);f.cleanup();
});
test('unknown/custom power is displayed read-only without fabricated numeric options',async()=>{
  const current=settings();current[2]={...current[2],value:77,default:false,editable:false,custom:true};
  const f=fixture(state({settings:current}));await flush();const field=f.byLabel('dropdown','Power mode');
  assert.equal(field.props.renderButtonValue(),'Current · 0x0000004D');assert.deepEqual(clone(field.props.rgOptions.map(option=>option.data)),['default']);
  field.props.onChange({data:1});assert.equal(f.calls.length,1);field.props.onChange({data:'default'});await flush();assert.equal(f.calls[1][1].value,'default');f.cleanup();
});
test('known power modes are readable by name without becoming extra writable options',async()=>{
  for(const value of [0,2,3,4,5]){
    const current=settings();current[2]={...current[2],value,default:false};const f=fixture(state({settings:current}));await flush();const field=f.byLabel('dropdown','Power mode');
    assert.equal(field.props.renderButtonValue(),words.nvidiaLocale('en').powerCurrent[value]);assert.deepEqual(clone(field.props.rgOptions.map(option=>option.data)),['default',1]);
    field.props.onChange({data:value});assert.equal(f.calls.length,1);field.props.onChange({data:1});await flush();assert.equal(f.calls[1][1].value,1);f.cleanup();
  }
});
test('unconfirmed write shows actual readback and locks editing until a successful Retry',async()=>{
  const f=fixture();await flush();const changed=state({ok:false,status:'changed',token:'b'.repeat(64)});changed.settings[1].value=0x08416747;
  f.onWrite(async()=>changed);f.byLabel('dropdown','Vertical sync').props.onChange({data:0x47814940});await flush();
  assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x08416747);assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,true);assert.ok(f.button('Retry'));
  f.onRead(async()=>state());f.button('Retry').props.onClick();await flush();assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,false);f.cleanup();
});
test('backend success without matching actual value is never optimistic success',async()=>{
  const f=fixture();await flush();f.onWrite(async()=>state({status:'persisted',ok:true}));f.byLabel('dropdown','Power mode').props.onChange({data:1});await flush();
  assert.equal(f.byLabel('dropdown','Power mode').props.selectedOption,'default');assert.equal(f.byLabel('dropdown','Power mode').props.disabled,true);assert.ok(f.button('Retry'));f.cleanup();
});
test('transient refresh error preserves last known controls as read-only with retry',async()=>{
  const f=fixture();await flush();f.advance(5001);f.onRead(async()=>{throw Error('offline')});f.focus();await flush();
  assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x60925292);assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,true);assert.ok(f.button('Retry'));f.cleanup();
});
test('a queued old dropdown handler cannot write after a newer connection failure',async()=>{
  const f=fixture();await flush();const old=f.byLabel('dropdown','Vertical sync');
  f.advance(5001);f.onRead(async()=>{throw Error('offline')});f.focus();await flush();
  const before=f.calls.length;old.props.onChange({data:0x47814940});assert.equal(f.calls.length,before);f.cleanup();
});
test('focus refresh is throttled, serialized, and skips hidden/collapsed/draft states',async()=>{
  const f=fixture();await flush();f.focus();assert.equal(f.calls.length,1);f.advance(5001);const pending=deferred();f.onRead(()=>pending.promise);f.focus();f.focus();f.focus();assert.equal(f.calls.length,2);pending.resolve(state());await flush();
  f.advance(5001);f.visible('hidden');f.focus();assert.equal(f.calls.length,2);f.setProps({collapsed:true});f.visible('visible');f.focus();assert.equal(f.calls.length,2);
  f.setProps({collapsed:false});await flush();const count=f.calls.length;f.byLabel('slider','Frame limit').props.onChange(120);f.advance(5001);f.focus();assert.equal(f.calls.length,count);f.cleanup();
});
test('never-returning transport permits read-only Retry and ignores the retired reply',async()=>{
  const f=fixture();await flush();f.advance(5001);const pending=deferred();f.onRead(()=>pending.promise);f.focus();f.timeout();
  assert.ok(f.button('Retry'));assert.equal(f.button('Retry').props.disabled,false);assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,true);
  const recovered=state({token:'c'.repeat(64)});recovered.settings[1].value=0x47814940;f.onRead(async()=>recovered);f.button('Retry').props.onClick();await flush();
  assert.equal(f.calls.length,3);assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x47814940);assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,false);
  const late=state();late.settings[1].value=0x08416747;pending.resolve(late);await flush();assert.equal(f.calls.length,3);assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x47814940);f.cleanup();
});
test('timed-out write stays unconfirmed until fresh read and cannot overwrite recovery',async()=>{
  const f=fixture();await flush();const pending=deferred();f.onWrite(()=>pending.promise);f.byLabel('dropdown','Vertical sync').props.onChange({data:0x47814940});f.timeout();
  assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x60925292);assert.equal(f.byLabel('dropdown','Power mode').props.disabled,true);
  const fresh=deferred();f.onRead(()=>fresh.promise);f.button('Retry').props.onClick();assert.equal(f.calls.length,3);
  const old=state({ok:true,status:'persisted',token:'b'.repeat(64)});old.settings[1].value=0x47814940;pending.resolve(old);await flush();
  assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x60925292);assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,true);
  fresh.resolve(state({token:'c'.repeat(64)}));await flush();assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,false);assert.equal(f.byLabel('dropdown','Vertical sync').props.selectedOption,0x60925292);f.cleanup();
});
test('unmount retires replies and removes focus listeners',async()=>{
  const pending=deferred();const f=fixture();await flush();f.advance(5001);f.onRead(()=>pending.promise);f.focus();f.cleanup();const before=clone(f.values);pending.resolve(state({token:'c'.repeat(64)}));await flush();assert.deepEqual(clone(f.values),before);assert.equal(f.listeners(),0);
});
test('effect replay retires old read then performs exactly one new read',async()=>{
  const first=deferred();const f=fixture();await flush();f.advance(5001);f.onRead(()=>first.promise);f.focus();f.replay();assert.equal(f.calls.length,2);
  f.onRead(async()=>state({token:'c'.repeat(64)}));first.resolve(state({token:'b'.repeat(64)}));await flush();await flush();assert.equal(f.calls.length,3);assert.equal(f.listeners(),2);f.cleanup();
});
test('strict snapshot parser rejects malformed identity and capability payloads',async()=>{
  const f=fixture();await flush();assert.equal(f.module.validNvidiaState(state()),true);
  const invalid=[state({global:false}),state({token:'short'}),state({settings:[...settings(),settings()[0]]}),state({settings:[{...settings()[0],editable:true,custom:true}]})];
  for(const field of [{id:'other'}, {value:NaN},{value:true},{value:null},{min:0},{max:1001},{values:[false]}])invalid.push(state({settings:[{...settings()[0],...field}]}));
  for(const value of invalid)assert.equal(f.module.validNvidiaState(value),false);f.cleanup();
});
test('actual C# DTO null optional fields remain valid without opening unsupported controls',async()=>{
  const current=settings();current[0].values=null;current[1]={...current[1],min:null,max:null};current[2]={...current[2],min:null,max:null};
  const f=fixture(state({settings:current,profile:{name:'Base Profile',identity:'c'.repeat(64)},driver:{version:62000,branch:'r620'},effectiveInRunningGame:'unknown',restartGameRecommended:true}));await flush();
  assert.equal(f.module.validNvidiaState(state({settings:current})),true);assert.ok(f.byLabel('slider','Frame limit'));assert.ok(f.byLabel('dropdown','Power mode'));f.cleanup();
});
test('all12 locales and Steam aliases have complete localized copy',()=>{
  for(const language of ['en','it','de','es','fr','pt','ru','uk','ja','ko','zh','hi']){
    const copy=words.nvidiaLocale(language);assert.equal(Object.keys(copy).length,15);assert.ok(Object.entries(copy).filter(([key])=>key!=='powerCurrent').every(([,value])=>typeof value==='string'&&value.length>0));assert.ok(Object.values(copy.powerCurrent).every(value=>typeof value==='string'&&value.length>0));if(language!=='en')assert.notEqual(copy.scope,words.nvidiaLocale('en').scope);
  }
  assert.equal(words.nvidiaLocale('italian').scope,words.nvidiaLocale('it').scope);assert.equal(words.nvidiaLocale('brazilian').apply,words.nvidiaLocale('pt').apply);
});

test('initial transient NVIDIA failure remains visible only after real provider enumeration',async()=>{
  const f=fixture({available:false,global:true,detected:true,status:'unavailable',settings:[]},true);await flush();
  assert.ok(f.render());assert.ok(f.all().find(n=>n.props['aria-expanded']===false));
  assert.equal(f.all().some(n=>n.type==='dropdown'),false);
  f.setProps({collapsed:false});assert.ok(f.button('Retry'));assert.equal(f.button('Retry').props.disabled,false);
  f.onRead(async()=>state());f.button('Retry').props.onClick();await flush();
  assert.equal(f.byLabel('dropdown','Vertical sync').props.disabled,false);assert.equal(f.calls.length,2);f.cleanup();
});
test('unproven initial error can rediscover a recovered provider while default collapsed',async()=>{
  const f=fixture({available:false,global:true,status:'unavailable',settings:[]},true);await flush();assert.equal(f.render(),null);
  f.onRead(async()=>state());f.focus();assert.equal(f.calls.length,1);f.advance(5001);f.focus();f.focus();await flush();
  assert.equal(f.calls.length,2);assert.ok(f.render());assert.equal(f.all().some(n=>n.type==='dropdown'),false);
  f.setProps({collapsed:false});assert.ok(f.byLabel('dropdown','Vertical sync'));f.cleanup();
});
test('filename-like or other-vendor replies never mount NVIDIA controls',async()=>{
  for(const initial of [{available:false,status:'unavailable',driver:'nvapi64.dll'},
    {available:false,global:false,detected:true,status:'unavailable'},
    {available:false,global:true,detected:'true',status:'unavailable'},
    {available:false,global:true,detected:true,status:'invented'}]) {
    const f=fixture(initial);await flush();assert.equal(f.render(),null);assert.equal(f.calls.length,1);f.cleanup();
  }
});
