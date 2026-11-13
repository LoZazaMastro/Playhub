import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
const text=fs.readFileSync(new URL('../Playhub/Services/DeckyPluginReload.cs',import.meta.url),'utf8');
const script=text.match(/ReloadScript = """([\s\S]*?)""";/)[1];
const loaded=text.match(/LoadedScript = """([\s\S]*?)""";/)[1];
test('migration age uses latest exact Decky Playhub import, not unrelated resources',()=>{
 const run=entries=>vm.runInNewContext(loaded,{window:{DeckyPluginLoader:{plugins:[{name:'Playhub'}]}},performance:{getEntriesByType:()=>entries.map(name=>({name}))},URL,Date});
 assert.equal(run(['http://127.0.0.1:1337/plugins/Playhub/dist/index.js?t=1790000000000','http://localhost:1337/plugins/Playhub/dist/index.js?t=1790000001000','http://127.0.0.1:1337/plugins/News/dist/index.js?t=1790000009000']),1790000001000);
 assert.equal(run(['https://evil.test/plugins/Playhub/dist/index.js?t=1790000001000','http://127.0.0.1:1337/plugins/Playhub/dist/index.js?t=bad']),0);
});
function fixture(running=false){
 const calls=[];
 const window={SteamUIStore:{IsAnyAppRunning:()=>running},DeckyBackend:{call:async(...args)=>{calls.push(args)}},DeckyPluginLoader:{plugins:[{name:'Playhub'}]}};
 return {window,calls,run:()=>vm.runInNewContext(script,{window})};
}
test('idle Steam reloads only Playhub through its normal loader API',async()=>{
 const f=fixture();assert.equal(await f.run(),'reloaded');assert.deepEqual(f.calls,[['loader/reload_plugin','Playhub']]);
});
test('running or unknown game state never reloads',async()=>{
 for(const state of [true,undefined,null,0]){const f=fixture(state);if(state===undefined)f.window.SteamUIStore.IsAnyAppRunning=()=>undefined;assert.equal(await f.run(),'busy');assert.equal(f.calls.length,0)}
});
test('missing Steam state, Decky bridge or plugin fails closed',async()=>{
 for(const key of ['SteamUIStore','DeckyBackend','DeckyPluginLoader']){const f=fixture();delete f.window[key];assert.equal(await f.run(),'unavailable');assert.equal(f.calls.length,0)}
});
test('game guard runs immediately before the loader call, and rejection is not success',async()=>{
 const f=fixture();let checked=false;f.window.SteamUIStore.IsAnyAppRunning=()=>{checked=true;return false};
 f.window.DeckyBackend.call=async()=>{assert.ok(checked);throw Error('loader unavailable')};
 await assert.rejects(f.run(),/loader unavailable/);
});
