import assert from 'node:assert/strict';
import test from 'node:test';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
const read = name => fs.readFileSync(new URL('../src/'+name, import.meta.url),'utf8');
function module(name) { const exports={}; vm.runInNewContext(ts.transpileModule(read(name),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports}); return exports; }
test('third and fourth default tabs are Video and Audio without rewriting saved tab IDs',()=>{
 const {CONTROL_TABS,normalizeControlPreferences}=module('controlCenterState.ts');
 const {controlLocale}=module('controlCenterLocale.ts'); const names=CONTROL_TABS.map(id=>controlLocale('it')[id]);
 assert.equal(names[2],'Video'); assert.equal(names[3],'Audio');
 const previous={order:['controller','audio','performance','store'],hidden:['performance'],active:'audio'};
 const current=normalizeControlPreferences(previous);
 assert.equal(current.active,'audio'); assert.equal(current.hidden[0],'performance'); assert.equal(current.order[0],'controller');
});
test('audio and video route separately; energy profile and RTSS are absent from controls',()=>{
 const source=read('quickSettings/index.tsx');
 assert.match(source,/\[local.audio\]: \["performance", "audio"\]/);
 assert.match(source,/\[local.display\]: \["audio", "display"\]/);
 for(const removed of ['FrameLimitControl','setPowerMode(', 'onPowerMode','getPerformanceStatus()']) assert.ok(!source.includes(removed));
 assert.ok(!source.includes('section(<FaFileAlt />, local.diagnostics'));
 const start=source.indexOf('section(<FaTools />, local.advanced');
 assert.ok(source.slice(start).includes('onClick={onDiagnostics}'));
});
test('settings hide bumper hints and restore/replay use the same native button style',()=>{
 const source=read('ControlCenter.tsx');
 assert.match(source,/actionDescriptionMap=\{customizing \? \{\} :/);
 assert.equal((source.match(/className="ph-settings-action"/g)||[]).length,2);
 assert.ok(!source.includes('open_windows_update'));
 assert.ok(!source.includes('<PowerStatus'));
 assert.ok(!source.includes('font-size:13px!important'));
 assert.ok(read('onboardingLocale.ts').includes('Ripeti introduzione'));
});
test('introduction contains no retired power or FPS pitch',()=>{
 const copy=read('onboardingTabLocale.ts');
 assert.doesNotMatch(copy,/\bTDP\b|power mode|modalità energetica|RTSS/);
 assert.ok(copy.includes('Scegli uscita audio e microfono'));
 assert.ok(copy.includes('Regola luminosità, risoluzione'));
});
