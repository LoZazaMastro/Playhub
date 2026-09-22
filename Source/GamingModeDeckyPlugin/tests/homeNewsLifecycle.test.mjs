import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
const React={Component:class{},createElement:(type,props,...children)=>Object.freeze({type,key:null,props:{...props,...(children.length?{children:children.length===1?children[0]:children}:{})}}),isValidElement:node=>!!node?.type,cloneElement:(node,props,...children)=>React.createElement(node.type,{...node.props,...props},...children)};
import test from 'node:test';
const read = name => fs.readFileSync(new URL('../src/'+name,import.meta.url),'utf8');
const compile = text => ts.transpileModule(text,{compilerOptions:{module:ts.ModuleKind.CommonJS,jsx:ts.JsxEmit.React,target:ts.ScriptTarget.ES2022}}).outputText;
test('Home editorial survives uninstall/reinstall without mutating native or another plugin wrapper', async()=>{
 const routes=new Set();const routerHook={addPatch:(_,fn)=>(routes.add(fn),fn),removePatch:(_,fn)=>routes.delete(fn)};
 const decky={SP_REACT:React,DFL:{},routerHook}; const helper={};
 vm.runInNewContext(compile(read('reactOutputPatch.ts')),{exports:helper,require:()=>decky});
 function install(){const exports={};vm.runInNewContext(compile(read('HomeNews.tsx')),{exports,window:{},console,require:path=>path==='./decky'?decky:path==='./reactOutputPatch'?helper:path==='./controlBackend'?{call:async()=>({enabled:true,country:'auto'})}:path==='./DailyHistory'?{HistoryTabs:function HistoryTabs(){},notifyHistoryLanguageChanged(){}}:{}});return exports.installHomeNews(()=> 'it');}
 const Tabs = () => React.createElement('section',{tabs:[{id:'Recommended',title:'Native'}]});
 const Inner = () => React.createElement(Tabs,{strActiveTab:'Recommended',setActiveTab(){}});
 const Outer = () => React.createElement(Inner);
 // Another plugin may wrap the route root; its rendered output must remain patchable.
 const OtherPlugin = (...args)=>Outer(...args);
 const route={children:React.createElement(OtherPlugin)};
 const render = root=>{let node=root;for(let n=0;n<3;n++)node=node.type(node.props);return node;};
 const stop=install();const first=[...routes][0](route);
 assert.equal(route.children.type,OtherPlugin,'native route must not be mutated');
 assert.equal(render(first.children).type.name,'HistoryTabs');
 assert.equal([...routes][0](first).children.type,first.children.type,'stable identity on repeat patch');
 stop();assert.equal(routes.size,0);assert.equal(render(first.children).type,'section','stopped wrapper renders native');
 const stopSecond=install();const second=[...routes][0](route);
 assert.equal(render(second.children).type.name,'HistoryTabs','new installation owns active output');
 stopSecond();await Promise.resolve();
});
