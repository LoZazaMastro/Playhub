import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import test from 'node:test';
import ts from 'typescript';
import vm from 'node:vm';
const api={};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL('../src/qamTabVisibility.ts',import.meta.url),'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{exports:api,require:()=>({})});
function node(id,value='',priority=''){
 let current=value,rank=priority;
 return {id,isConnected:true,style:{getPropertyValue:()=>current,getPropertyPriority:()=>rank,setProperty:(_k,v,p)=>{current=v;rank=p;},removeProperty:()=>{current='';rank='';}}};
}
test('stable native and custom IDs override positional CSS without touching Decky or content',()=>{
 const nodes=new Map(['0','3','4','5','6','7','5261568','999'].map(id=>[`quickaccess_tab_${id}`,node(`quickaccess_tab_${id}`)]));
 const content=node('quickaccess_content_6'),header=node('header');nodes.set(content.id,content);nodes.set(header.id,header);
 const projection=api.createQamTabVisibility(()=>[{getElementById:id=>nodes.get(id)}]);
 const ids=['0','3','4','5','6','7','5261568'];
 for(const hidden of ids){projection.update(new Map(ids.map(id=>[id,id!==hidden])));for(const id of ids){assert.equal(nodes.get(`quickaccess_tab_${id}`).style.getPropertyValue('display'),id===hidden?'none':'flex');assert.equal(nodes.get(`quickaccess_tab_${id}`).style.getPropertyPriority('display'),'important');}}
 assert.equal(nodes.get('quickaccess_tab_999').style.getPropertyValue('display'),'');assert.equal(content.style.getPropertyValue('display'),'');assert.equal(header.style.getPropertyValue('display'),'');
 projection.stop();for(const n of nodes.values())assert.equal(n.style.getPropertyValue('display'),'');
});
test('cleanup restores original inline style and preserves subsequent foreign writes',()=>{
 const one=node('quickaccess_tab_4','grid','important'),two=node('quickaccess_tab_6');const nodes=new Map([[one.id,one],[two.id,two]]);
 const projection=api.createQamTabVisibility(()=>[{getElementById:id=>nodes.get(id)}]);projection.update(new Map([['4',true],['6',true]]));
 two.style.setProperty('display','block','important');projection.stop();assert.equal(one.style.getPropertyValue('display'),'grid');assert.equal(one.style.getPropertyPriority('display'),'important');assert.equal(two.style.getPropertyValue('display'),'block');
});
