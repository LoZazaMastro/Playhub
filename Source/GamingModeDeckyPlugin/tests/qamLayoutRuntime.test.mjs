import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
const compile = name => ts.transpileModule(readFileSync(new URL(`../src/${name}`,import.meta.url),"utf8"),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
const iconCode = compile("qamIcons.ts");
function fixture() {
 const api={};
 const icons={};
 vm.runInNewContext(iconCode,{exports:icons,require:()=>({createElement:(type,props)=>({type,props})})});
 const context={exports:api,WeakRef,require:()=>icons};
 vm.runInNewContext(compile('qamLayoutRuntime.ts'),context);
 const hook={tabs:[{id:999,content:{}}]};
 function render(tabs,visible){ if(tabs.length===1){tabs[0].initialVisibility=visible;return;} for(const entry of this.tabs)tabs.push({key:entry.id,decky:true,panel:{header:entry.title,content:entry.content},tab:entry.icon,initialVisibility:visible}); }
 let inventory={plugins:[{name:'Music',title:'Now playing',content:{value:1},icon:'original'}]};
 let refreshes=0;
 const runtime=api.createQamLayoutRuntime(hook,render,100,()=>refreshes++,()=>inventory);
 return {api,runtime,inventory,refreshes:()=>refreshes};
}
const prefs=(extra={})=>({version:3,selected:['Music'],icons:{},order:[],hidden:[],updated_at:12,...extra});
test('native renderer keeps title/header, preferences order/hide and restores native identity',()=>{
 const f=fixture();f.api.setQamLayoutPreferences(prefs({order:['shortcut:Music','steam:1','decky:999'],hidden:['steam:2']}));
 const one={key:1,title:{props:{}},strTitle:'Impostazioni'},two={key:2,title:'Friends'},decky={key:999,decky:true},own={key:100}; const tabs=[one,two,decky,own];
 f.runtime.afterRender(tabs,true);
 assert.equal(f.api.getQamLayoutSnapshot().native[0].label,'Impostazioni');assert.equal(tabs[0].panel.header,'Now playing');assert.equal(tabs[0].panel.content.value,1);
 assert.deepEqual(tabs.slice(1),[one,decky,own]);const shortcut=tabs[0];
 for(let i=0;i<5;i++){f.runtime.beforeRender(tabs);f.runtime.afterRender(tabs,false);}
 assert.equal(tabs.length,4);assert.equal(tabs[0],shortcut);assert.equal(shortcut.initialVisibility,false);
 f.runtime.stop();assert.deepEqual(tabs,[one,two,decky,own]);assert.equal(f.api.getQamLayoutSnapshot().active,false);
});
test('unknown saved plugins/icons survive and unavailable/disabled plugins are not projected',()=>{
 const f=fixture();f.api.setQamLayoutPreferences(prefs({selected:['Music','Missing'],icons:{Music:'unknown',Missing:'photo'},hidden:['decky:999','steam:2']}));
 const tabs=[{key:999}];f.runtime.afterRender(tabs,true);assert.equal(tabs[1].tab,'original');
 assert.deepEqual(Array.from(f.api.getQamLayoutPreferences().selected),['Music','Missing']);
 assert.deepEqual(Array.from(f.api.getQamLayoutPreferences().hidden),['steam:2']);
 f.runtime.beforeRender(tabs);f.inventory.disabledPlugins=['Music'];f.runtime.afterRender(tabs,true);assert.equal(tabs.length,1);f.runtime.stop();
});
test('icon preference renders supported independent icon and settings refresh exactly once',()=>{
 const f=fixture();f.api.setQamLayoutPreferences(prefs({icons:{Music:'music-outline'}}));assert.equal(f.refreshes(),1);
 const tabs=[{key:999}];f.runtime.afterRender(tabs,true);assert.equal(tabs[1].tab.type,'svg');assert.equal(tabs[1].tab.props.fill,'none');
 f.api.setQamLayoutPreferences(prefs({icons:{Music:'music-outline'}}));assert.equal(f.refreshes(),1);f.runtime.stop();
});
test('restoration preserves a new foreign tab and ignores Playhub selected duplication',()=>{
 const f=fixture();f.api.setQamLayoutPreferences(prefs({selected:['Playhub','Music']}));
 const own={key:100},decky={key:999},foreign={key:800};const tabs=[own,decky];
 f.runtime.afterRender(tabs,true);tabs.push(foreign);f.runtime.beforeRender(tabs);assert.deepEqual(tabs,[own,decky,foreign]);f.runtime.stop();
});
test('plugin load/unload refreshes once per inventory change without render polling',()=>{
 const f=fixture();f.runtime.refreshInventory();assert.equal(f.refreshes(),1);
 f.runtime.refreshInventory();assert.equal(f.refreshes(),1);
 f.inventory.plugins.push({name:'News',content:{},icon:'news'});f.runtime.refreshInventory();assert.equal(f.refreshes(),2);
 f.runtime.refreshInventory();assert.equal(f.refreshes(),2);f.runtime.stop();
});

test('actual Decky titleView shape takes precedence and refreshes changed plugin header',()=>{
 const f=fixture();const first={props:{children:'Plugin heading'}};f.inventory.plugins[0].titleView=first;
 f.api.setQamLayoutPreferences(prefs());const tabs=[{key:999}];f.runtime.afterRender(tabs,true);
 assert.equal(tabs[1].panel.header,first);
 f.runtime.beforeRender(tabs);const second={props:{children:'New plugin heading'}};f.inventory.plugins[0].titleView=second;
 f.runtime.afterRender(tabs,true);assert.equal(tabs[1].panel.header,second);f.runtime.stop();
});


test('native icon references are retained without serializing React owner cycles',()=>{
 const f=fixture();const icon={type:'svg',props:{}};icon._owner=icon;
 const tabs=[{key:0,strTitle:'Notifiche',tab:icon},{key:999}];
 assert.doesNotThrow(()=>f.runtime.afterRender(tabs,true));
 assert.equal(f.api.getQamLayoutSnapshot().native[0].icon,icon);
 const stable=f.api.getQamLayoutSnapshot();f.runtime.beforeRender(tabs);f.runtime.afterRender(tabs,true);
 assert.equal(f.api.getQamLayoutSnapshot(),stable);
 f.runtime.beforeRender(tabs);const newIcon={type:'svg',props:{updated:true}};tabs[0].tab=newIcon;f.runtime.afterRender(tabs,true);
 assert.equal(f.api.getQamLayoutSnapshot().native[0].icon,newIcon);f.runtime.stop();
});
