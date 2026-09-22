import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import vm from 'node:vm';
const source=readFileSync(new URL('../src/qamIcons.ts',import.meta.url),'utf8');
const api={};
vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports:api,require:()=>({createElement:(type,props)=>({type,props})})});
test('complete icon catalog retains thousands of filled and outline assets and valid categories',()=>{
 assert.ok(Object.keys(api.ICON_LIBRARY).length>6000);
 assert.ok(Object.values(api.ICON_LIBRARY).filter(d=>d.variant==='filled').length>500);
 assert.ok(Object.values(api.ICON_LIBRARY).filter(d=>d.variant==='outline').length>5000);
 assert.ok(api.ICON_CATEGORIES.length>10);
});
test('saved Shortcuts aliases resolve exactly without forcing outlines',()=>{
 for(const [old,current] of Object.entries(api.ICON_ALIASES))assert.equal(api.resolveQamIconId(old),current);
 assert.equal(api.resolveQamIconId('music'),'file-music');
 assert.equal(api.resolveQamIconId('music-outline'),'music-outline');
 assert.equal(api.renderQamIcon('sun').props.fill,'currentColor');
 assert.equal(api.renderQamIcon('sun-outline').props.fill,'none');
 assert.equal(api.renderQamIcon('sun-outline').props.stroke,'currentColor');
});
test('every saved requested icon has its exact original body and correct paint',()=>{
 for(const id of ['device-gamepad-2-outline','music-outline','news-outline','sun','brand-openvpn-outline']){
  const svg=api.renderQamIcon(id);const d=api.ICON_LIBRARY[api.resolveQamIconId(id)];
  assert.equal(svg.props.dangerouslySetInnerHTML.__html,d.body);
  assert.equal(svg.props.fill,d.variant==='outline'?'none':'currentColor');
 }
 const original={native:true};assert.equal(api.renderQamIcon('original',original),original);assert.equal(api.renderQamIcon('unknown',original),original);
 assert.equal(api.iconMatchesQuery('device-gamepad-2-outline',' GAMEPAD '),true);
});
