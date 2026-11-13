import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';

const options={module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React};
const localeExports={};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/deviceInfoLocale.ts',import.meta.url),'utf8'),{compilerOptions:options}).outputText,{exports:localeExports});
const source=fs.readFileSync(new URL('../src/DeviceSettings.tsx',import.meta.url),'utf8');
const ast=ts.createSourceFile('DeviceSettings.tsx',source,ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX);
const component=ast.statements.find(n=>ts.isFunctionDeclaration(n)&&n.name?.text==='DeviceSettings');
const actual=ts.transpileModule(component.getText(ast),{compilerOptions:options}).outputText;
const base={selection:'auto',detected:'pc',effective:'pc',manufacturer:'Fixture',model:'PC',handhelds:[],display:null,powerModeAvailable:false};
const valid={available:true,battery:true,percent:50,source:'battery',discharging:true,remainingSeconds:3661};

function render(power,locale='en'){
  const effects=[],calls=[],exports={};let index=0;
  const states=[{...base,power},undefined,false,0,undefined];
  const React={useState:()=>[states[index++],()=>{}],useRef:v=>({current:v}),useEffect:e=>effects.push(e),
    createElement:(type,props,...children)=>({type,props:props??{},children})};
  const env={exports,React,deviceInfoLocale:localeExports.deviceInfoLocale,style:'',DialogButton:'button',DropdownItem:'dropdown',Focusable:'div',DeviceInfo:'details',DeviceCapabilities:'capabilities',Ayaneo3ControllerControls:'ayaneo',
    TbBattery:'battery',TbChevronDown:'down',TbChevronRight:'right',TbDeviceDesktop:'pc',TbDeviceGamepad2:'handheld',
    call:async(...args)=>{calls.push(args);return {percent:null};}};
  vm.runInNewContext(actual,env);
  return {tree:exports.DeviceSettings({locale}),effects,calls};
}
function strings(value){
  if(typeof value==='string')return [value];
  if(Array.isArray(value))return value.flatMap(strings);
  return value&&typeof value==='object'?strings(value.children??[]):[];
}
const output=rendered=>strings(rendered.tree).join('\n');

test('actual DeviceSettings displays the Windows estimate in minutes with an estimate label',()=>{
  const result=render(valid);
  assert.match(output(result),/Estimated time remaining · 62 min/);
  assert.match(output(result),/50% · On battery/);
  assert.deepEqual(result.calls,[]);
});
test('one real second rounds to one minute and real zero remains zero',()=>{
  assert.match(output(render({...valid,remainingSeconds:1})),/Estimated time remaining · 1 min/);
  assert.match(output(render({...valid,remainingSeconds:0})),/Estimated time remaining · 0 min/);
});
test('unknown sentinel, malformed types and noninteger durations do not render a guessed estimate',()=>{
  for(const value of [null,undefined,0xffffffff,-1,1.5,true,false,'60',NaN,Infinity,0x100000000])
    assert.doesNotMatch(output(render({...valid,remainingSeconds:value})),/Estimated time remaining/);
});
test('availability, battery, source and discharging must be exact positive fields',()=>{
  for(const patch of [{available:false},{available:1},{battery:false},{battery:'true'},{source:'ac'},{source:null},{discharging:false},{discharging:'true'},{discharging:undefined}])
    assert.doesNotMatch(output(render({...valid,...patch})),/Estimated time remaining/);
});
test('old backend replies remain compatible and have no estimate',()=>{
  assert.doesNotMatch(output(render({available:true,battery:true,percent:40,source:'battery'})),/Estimated time remaining/);
});
test('all twelve languages have natural nonempty labels and supported minute formatting',()=>{
  for(const language of ['en','it','de','es','fr','pt','ru','uk','ja','ko','zh','hi']){
    const copy=localeExports.deviceInfoLocale(language);
    assert.equal(copy.language,language);
    assert.equal(typeof copy.batteryRemaining,'string');
    assert.ok(copy.batteryRemaining.length>3);
    assert.ok(output(render(valid,language)).includes(copy.batteryRemaining));
  }
  assert.match(output(render(valid,'italian')),/Autonomia stimata/);
  assert.match(output(render(valid,'unknown-language')),/Estimated time remaining/);
});
test('the display adds no request, polling timer, setting or hardware capability',async()=>{
  const result=render(valid);
  assert.equal(result.effects.length,2);
  const cleanup=result.effects[1]();
  await Promise.resolve();
  assert.deepEqual(result.calls,[['get_battery_health']]);
  cleanup();
  assert.doesNotMatch(source,/setInterval|setTimeout|get_battery_remaining|set_battery_remaining/);
});
