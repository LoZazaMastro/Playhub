import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
const text=fs.readFileSync(new URL('../PlayhubSetup/BundledPlayhubMaintenance.cs',import.meta.url),'utf8');
const script=text.match(/ReloadScript = """([\s\S]*?)""";/)[1];
function fixture(state=false){
 const calls=[];const plugin={name:'Playhub'},loader={plugins:[plugin]};
 const window={DeckyPluginLoader:loader,SteamUIStore:{IsAnyAppRunning:()=>state},DeckyBackend:{call:async(...args)=>{calls.push(args);return args[0]==='utilities/settings/get'?[]:undefined;}}};
 return {window,loader,plugin,calls,run:()=>vm.runInNewContext(script,{window})};
}
test('official reload requests only the existing Playhub plugin',async()=>{
 const f=fixture();assert.equal(await f.run(),'reloaded');assert.deepEqual(JSON.parse(JSON.stringify(f.calls)),[['utilities/settings/get','disabled_plugins',[]],['loader/reload_plugin','Playhub']]);
});
test('busy and unknown Steam state never issue any loader request',async()=>{
 for(const value of[true,null,undefined,0]){const f=fixture(value);if(value===undefined)f.window.SteamUIStore.IsAnyAppRunning=()=>undefined;assert.equal(await f.run(),'busy');assert.equal(f.calls.length,0);}
});
test('disabled or unknown disabled state never reloads',async()=>{
 for(const value of[['Playhub'],'unknown',undefined]){const f=fixture();f.window.DeckyBackend.call=async(...args)=>{f.calls.push(args);return value;};assert.notEqual(await f.run(),'reloaded');assert.equal(f.calls.length,1);}
});
test('game begins while disabled state is awaited: immediate second idle guard prevents reload',async()=>{
 const f=fixture();f.window.DeckyBackend.call=async()=>{f.window.SteamUIStore.IsAnyAppRunning=()=>true;return[];};assert.equal(await f.run(),'busy');
});
test('loader/plugin replacement during await cannot reload a foreign generation',async()=>{
 const f=fixture();f.window.DeckyBackend.call=async()=>{f.loader.plugins=[{name:'Playhub'}];return[];};assert.equal(await f.run(),'unavailable');
});
test('rejected official reload is not an acknowledgement',async()=>{
 const f=fixture();f.window.DeckyBackend.call=async(name)=>name==='utilities/settings/get'?[]:false;assert.equal(await f.run(),'unavailable');
});
