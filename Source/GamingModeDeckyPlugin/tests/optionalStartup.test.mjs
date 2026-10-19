import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';
const source = readFileSync(new URL('../src/index.tsx', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, {compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React}}).outputText;
function fixture(failing) {
  let factory;
  const calls = [], disposed = [], warnings = [];
  const install = name => () => {
    calls.push(name);
    if (failing === name) throw new Error(`Simulated unavailable Steam hook: ${name}`);
    return () => disposed.push(name);
  };
  const noop = () => {};
  const modules = {
    './decky': {definePlugin: value => {factory=value;return value;},DFL:{},SP_REACT:{createElement:(type,props,...children)=>({type,props,children})}, routerHook:{addRoute:noop,removeRoute:noop}},
    './circlesScreensaver':{installCirclesScreensaver:install('screensaver')},
    './onboardingIntegration':{installPlayhubOnboarding:install('onboarding')},
    './deckyHost':{initDeckyHost:install('decky host')},
    './quickSettings':{installQuickSettings:install('quick settings'),disposeDisplayConfirmation:noop},
    './controlledQamIntegration':{installControlledQamFocus:install('qam focus')},
    './QamSettings':{initializeQamPreferences:()=>Promise.resolve()},
    './HomeNews':{installHomeNews:install('home news')},
    './DashboardPage':{preloadDashboardWindows:()=>Promise.resolve(),clearDashboardChrome:noop},
    './pluginStoreData':{preloadPluginStoreCatalog:()=>Promise.resolve()},
    './navigationHaptics':{installNavigationHaptics:install('haptics'),getNavigationHapticsRevision:()=>0,configureNavigationHaptics:noop},
    './qam':{initPlayhubQam:install('qam')},
    './powerMenuPatch':{installPowerMenuPatch:install('power menu')},
    './deckyIpcRecovery':{installDeckyIpcRecovery:install('ipc recovery')},
    './api':{readSettings:()=>Promise.resolve({}),writeSettings:()=>Promise.resolve()},
  };
  const window = {setInterval:()=>1,clearInterval:noop,setTimeout:()=>2,clearTimeout:noop};
  vm.runInNewContext(compiled,{exports:{},require:name=>modules[name]??{}, window,document:{documentElement:{lang:'en'}},navigator:{language:'en'},console:{warn:(...items)=>warnings.push(items),log:noop,debug:noop},Promise,Set});
  return {factory,calls,disposed,warnings};
}
for (const name of ['ipc recovery','screensaver','onboarding','decky host','quick settings','qam focus','haptics','power menu']) {
  test(`Unavailable ${name} hook does not prevent the real plugin and QAM from loading`, async () => {
    const f = fixture(name);
    const plugin = f.factory();
    await Promise.resolve();
    assert.equal(plugin.name,'Playhub');
    assert.ok(f.calls.includes('qam'));
    assert.equal(f.warnings.length,1);
    plugin.onDismount();
    assert.ok(f.disposed.includes('qam'));
    assert.ok(!f.disposed.includes(name));
  });
}
test('Successful startup keeps all integrations and releases every hook',async()=>{
  const f=fixture();const plugin=f.factory();await Promise.resolve();plugin.onDismount();
  assert.equal(f.warnings.length,0);assert.equal(f.calls.length,10);assert.equal(f.disposed.length,10);
});
