import test from 'node:test';import assert from 'node:assert/strict';import fs from 'node:fs';import vm from 'node:vm';import ts from 'typescript';
test('actual Decky navigation wrapper and installed Steam Navigate replace the plugin route and focus Steam root once',()=>{
 const steamFile='C:/Program Files (x86)/Steam/steamui/sp.js';const steam=fs.readFileSync(steamFile,'utf8');const method=/Navigate\(C,k=!1,H=!1,q=void 0\)\{[\s\S]*?\}(?=NavigateWithoutChangingFocus)/.exec(steam)?.[0];assert.ok(method,'actual installed WindowInstance.Navigate contract');
 const Navigate=vm.runInNewContext('({'+method+'}).Navigate'),calls=[];const win={BCanNavigate:()=>true,BRouteMatch:()=>false,m_history:{replace:(...args)=>calls.push(['replace',...args]),push:(...args)=>calls.push(['push',...args])},FocusApplicationRoot:()=>calls.push(['native-root-focus']),EnsureVROverlayVisible:()=>calls.push(['vr-visible']),MenuStore:{CloseSideMenus:()=>calls.push(['closeMenus'])},Navigate};
 let router=fs.readFileSync(new URL('../node_modules/@decky/ui/dist/modules/Router.js',import.meta.url),'utf8').replace(/^import[^\n]+\n/gm,'').replace(/export /g,'');router+='\nthis.navigation=Navigation;';const context={window:{SteamUIStore:{GetFocusedWindowInstance:()=>win}},Logger:class{warn(){}error(error){throw Error(error);}},findModuleExport:()=>({WindowStore:{GamepadUIMainWindowInstance:win}})};vm.runInNewContext(router,context);
 const exports={};vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL('../src/StoreDigitalNavigation.ts',import.meta.url),'utf8'),{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.CommonJS}}).outputText,{exports,require:()=>({DFL:{Navigation:context.navigation}})});exports.exitStoreDigitalToHome();
 assert.deepEqual(calls,[['closeMenus'],['replace','/library/home',undefined],['native-root-focus'],['vr-visible']]);assert.equal(calls.some(([type])=>type==='push'||type==='back'),false,'no browser or focus-history unwinding');
});

import {digitalFixture,snapshot} from './storeDigitalFixture.mjs';
test('one physical B or native keyboard Escape leaves every Digital root surface directly for Steam Home',async()=>{
 for(const input of ['pressCancel','pressEscape'])for(const surface of ['tab','game','search','downloads','accounts']){
  const data=snapshot(1,120);data.jobs=[{jobId:'pending',key:data.games[2].key,accountRevision:1,title:'Pending',state:'externalHandoff',revision:1,downloadedBytes:0,canPause:false,canResume:false,canCancel:false,installPath:'Microsoft Store',reasonCode:'windows_store_managed',errorCode:'windows_store_install_unconfirmed'}];
  const f=await digitalFixture({data});try{
   let target;
   if(surface==='downloads'||surface==='accounts'){await f.settle(()=>[...f.container.querySelectorAll('.ph-sd-tab')].find(node=>node.textContent.includes(surface==='downloads'?'Downloads':'Accounts')).click());target=f.container.querySelector(surface==='downloads'?'[data-store-job]':'.ph-sd-account button');}
   else target=f.container.querySelector(surface==='tab'?'.ph-sd-tab':surface==='game'?'.ph-sd-game':'.ph-sd-toolbar input');
   await f.settle(()=>f.native.focusElement(target));await f.settle(()=>f.native[input]());
   assert.deepEqual(f.navigationCalls,[{closeMenus:true},{route:'/library/home',replace:true}],surface+'/'+input);assert.equal(f.routes.includes('back'),false);assert.ok(f.native.proof().modules.includes(71141));
  }finally{await f.close();}
 }
});
test('B and Escape close the current detail and modal before a later root press leaves Steam once',async()=>{
 for(const input of ['pressCancel','pressEscape']){const f=await digitalFixture();try{const origin=f.container.querySelector('.ph-sd-game');await f.settle(()=>f.native.focusElement(origin));await f.settle(()=>origin.click());assert.ok(f.container.querySelector('.ph-sd-detail'));await f.settle(()=>f.native[input]());assert.equal(f.container.querySelector('.ph-sd-detail'),null);assert.equal(f.navigationCalls.length,0);assert.equal(f.native.focused()?.Element?.getAttribute("data-game-key"),origin.getAttribute("data-game-key"));await f.settle(()=>f.native.pressY());assert.equal(f.modals.length,1);await f.settle(()=>f.native[input]());assert.equal(f.modals[0].container.isConnected,false);assert.equal(f.navigationCalls.length,0);await f.settle(()=>f.native.focusElement(f.container.querySelector(".ph-sd-game")));await f.settle(()=>f.native[input]());assert.equal(f.navigationCalls.filter(call=>call.route).length,1);}finally{await f.close();}}
});
