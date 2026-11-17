import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createRequire} from 'node:module';
import test from 'node:test';
import vm from 'node:vm';
const require=createRequire(import.meta.url),ts=require('typescript');
const source=readFileSync(new URL('../src/storeLibraryRuntime.ts',import.meta.url),'utf8');
test('Digital Stores owns the achievement runtime for its full lifetime instead of forwarding Xbox to Metadata',()=>{
 let dependencies,closed=0;
 const context=vm.createContext({exports:{},Symbol,Map,Set,TextEncoder,setInterval:()=>1,clearInterval(){},performance:{now:()=>0},
  require:name=>name==='./storeInstallRecents'?{installStoreInstallRecents:()=>()=>{}}:name==='./StoreAchievementsRuntime'?{installStoreAchievementsRuntime:args=>{dependencies=args;return()=>{closed++;};}}:name==='./controlBackend'?{call:async()=>{throw Error('inert background poll');}}:name==='./storeCompletionNotifications'?{createStoreCompletionNotifications:()=>()=>{}}:{},
 });
 vm.runInContext(ts.transpileModule(source+'\nexports.seed=r=>owned=new Map([[r.appId,r]]);',{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,context);
 const stop=context.exports.installStoreLibraryRuntime(),row={appId:101,key:{store:'xbox',id:'9NJRX71M5X9P'}};context.exports.seed(row);
 assert.equal(dependencies.rows().get(101),row);assert.equal(typeof dependencies.request,'function');assert.equal(typeof dependencies.subscribe,'function');
 assert.equal(vm.runInContext("globalThis[Symbol.for('playhub.store.xbox-achievements.v1')]",context),undefined);
 stop();assert.equal(closed,1);stop();assert.equal(closed,1);
});
