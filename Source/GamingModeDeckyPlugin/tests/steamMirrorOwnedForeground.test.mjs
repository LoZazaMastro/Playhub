import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import ts from 'typescript';
const exports={};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL('../src/steamMirrorOverlay.ts',import.meta.url),'utf8'),{
 compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports,require:()=>({})});
const base={protocol:1,state:'shown',sessionId:'a'.repeat(64),generation:'1',gameId:'12796537303900094464',expiresAt:'2000',observedAt:'1000',
 sourceHwnd:'101',sourceCookie:'18446744073709551614',destinationHwnd:'102',destinationCookie:'18446744073709551615',ownerPid:10,ownerBirth:'134389729972465669',
 gamePid:11,gameBirth:'134389729909443526',preparedCurrent:true,shownCurrent:true,leaseCurrent:true,cleanupComplete:false,actualScope:3,ownedForegroundPhase:1};
const reply=m=>({ok:true,gameId:m.gameId,overlayPid:12,companionPid:10,mirror:m});
test('native confirmed scope3 is current only when fully Shown',()=>{
 assert.equal(exports.currentMirror(reply(base),'shown')?.actualScope,3);
 for(const state of ['preparing','prepared','closed','cleanup-pending'])assert.equal(exports.currentMirror(reply({...base,state})),undefined);
 for(const key of ['shownCurrent','preparedCurrent','leaseCurrent'])assert.equal(exports.currentMirror(reply({...base,[key]:false})),undefined);
});
test('unknown and legacy companion scopes never substitute owned handoff',()=>{
 for(const actualScope of [0,2,4,-1,undefined])assert.equal(exports.currentMirror(reply({...base,actualScope})),undefined);
 assert.equal(exports.currentMirror(reply({...base,actualScope:1}),'shown')?.actualScope,1);
});
test('confirmed owned handoff retains only the existing post-exit cleanup claim',()=>{
 const cfcExports={};
 vm.runInNewContext(ts.transpileModule(readFileSync(new URL('../src/steamConfiguratorReturn.ts',import.meta.url),'utf8'),{
  compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports:cfcExports});
 const app=Number(BigInt(base.gameId)>>32n),input={ForceConfiguratorFocus(){throw new Error('must not dispatch at admission');}};
 const state=cfcExports.createSteamConfiguratorReturn();state.admitted(app,base,input);assert.equal(state.pending(app),true);
 for(const wrong of [{state:'prepared'},{actualScope:2},{shownCurrent:false}]){
  const denied=cfcExports.createSteamConfiguratorReturn();denied.admitted(app,{...base,...wrong},input);assert.equal(denied.pending(app),false);
 }
});
