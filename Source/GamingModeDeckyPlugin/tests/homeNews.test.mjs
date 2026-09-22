import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
import test from 'node:test';
const source=fs.readFileSync(new URL('../src/HomeNews.tsx',import.meta.url),'utf8');
const out={};vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,jsx:ts.JsxEmit.React,target:ts.ScriptTarget.ES2022}}).outputText,{exports:out,require:()=>({DFL:{},SP_REACT:{Component:class {}}})});
test('news keeps native row objects and reorders without cloning their contents',()=>{
 const activity={props:{children:{type:()=> 'BasicHomeUpdates eventsToShow'}}};
 const updated={props:{children:{type:()=> 'RecentlyCompletedCarousel'}}};
 const rows=out.nativeNewsRows({props:{children:[{},activity,updated,{}]}});
 assert.equal(rows[0],updated);assert.equal(rows[1],activity);
 assert.deepEqual(Array.from(out.nativeNewsRows({props:{children:[activity]}})),[activity]);
 assert.equal(out.nativeNewsRows({props:{children:[]}}).length,0);
 assert.equal(out.nativeNewsRows({props:{children:[activity,activity,updated]}}),null);
});
test('Audio and Video render directly even if previous categories were collapsed',()=>{
 const quick=fs.readFileSync(new URL('../src/quickSettings/index.tsx',import.meta.url),'utf8');
 assert.match(quick,/if \(tab === "audio" \|\| tab === "performance"\) return <div/);
 const center=fs.readFileSync(new URL('../src/ControlCenter.tsx',import.meta.url),'utf8');
 assert.match(center,/active !== "decky" && \(active !== "home" \|\| homeTitle\) && <h2 className="ph-control-heading">\{active === "home" \? homeTitle : copy\[active\]\}/);
});

test("news survives a Steam shelf disappearing during refresh",()=>{
 const updated={props:{children:{type:()=> "RecentlyCompletedCarousel"}}};
 assert.equal(out.nativeNewsRows({props:{children:[null,updated,false]}})[0],updated);
 assert.equal(out.nativeNewsRows({props:{children:[null,false]}}).length,0);
});
