import test from 'node:test';import assert from 'node:assert/strict';
import {digitalFixture,GamepadButton,React,snapshot} from './storeDigitalFixture.mjs';
const active=f=>[...f.container.querySelectorAll('.ph-sd-tab')].find(node=>node.classList.contains('is-active')).textContent.replace(/\d+$/,'');
test('installed Steam shoulder mapping changes exactly one tab with both directions, wrapping and native legends',async()=>{
 const f=await digitalFixture();try{
  const first=f.container.querySelector('.ph-sd-game');await f.settle(()=>f.native.focusElement(first));
  const descriptions=f.native.nodeFor(first).GetActiveActionDescriptions();assert.equal(descriptions[GamepadButton.BUMPER_LEFT],'Previous screen');assert.equal(descriptions[GamepadButton.BUMPER_RIGHT],'Next screen');
  await f.settle(()=>f.native.pressBumper('right'));assert.equal(active(f),'Downloads');
  await f.settle(()=>f.native.pressBumper('right'));assert.equal(active(f),'Accounts');
  await f.settle(()=>f.native.pressBumper('right'));assert.equal(active(f),'Your games');
  await f.settle(()=>f.native.pressBumper('left'));assert.equal(active(f),'Accounts');
  await f.settle(()=>f.native.pressBumper('left'));assert.equal(active(f),'Downloads');
 }finally{await f.close();}
});
test('native bubbling consumes a shoulder once, and repeated or already-consumed events do not change the page',async()=>{
 const f=await digitalFixture();try{
  const card=f.container.querySelector('.ph-sd-game');await f.settle(()=>f.native.focusElement(card));
  const event=new window.CustomEvent('vgp_onbuttondown',{detail:{button:GamepadButton.BUMPER_RIGHT,source:0,is_repeat:false},bubbles:true,cancelable:true});
  await f.settle(()=>card.dispatchEvent(event));assert.equal(active(f),'Downloads');assert.equal(event.defaultPrevented,true);
  await f.settle(()=>f.native.focused().Element.dispatchEvent(event));assert.equal(active(f),'Downloads');
  await f.settle(()=>f.native.focused().Element.dispatchEvent(new window.CustomEvent('vgp_onbuttondown',{detail:{button:GamepadButton.BUMPER_RIGHT,source:0,is_repeat:true},bubbles:true,cancelable:true})));assert.equal(active(f),'Downloads');
 }finally{await f.close();}
});
test('filter modal gates shoulders, with immediate cleanup when the modal closes',async()=>{
 const f=await digitalFixture();try{
  const card=f.container.querySelector('.ph-sd-game');await f.settle(()=>f.native.focusElement(card));await f.settle(()=>f.native.pressY());assert.equal(f.modals.length,1);
  await f.settle(()=>f.native.pressBumper('right'));assert.equal(active(f),'Your games');
  const direct=new window.CustomEvent('vgp_onbuttondown',{detail:{button:GamepadButton.BUMPER_RIGHT,source:0},bubbles:true,cancelable:true});await f.settle(()=>card.dispatchEvent(direct));assert.equal(active(f),'Your games');
  await f.settle(()=>f.modals[0].Close());await f.settle(()=>f.native.focusElement(card));await f.settle(()=>f.native.pressBumper('right'));assert.equal(active(f),'Downloads');
 }finally{await f.close();}
});
test('installation, folder selection and uninstall confirmation block shoulder navigation without starting any operation',async()=>{
 const key={store:'epic',id:'1'},f=await digitalFixture({method(name){if(name==='prepare')return{key,accountRevision:1,revision:1,selectionNonce:null,expiresAt:new Date(Date.now()+60000).toISOString(),requiredBytes:1000,suggestedPath:'F:\\Games\\Game',confirmedPath:null,locations:[],installKind:'direct'};if(name==='directories')return{path:null,parentPath:null,roots:[],directories:[],truncated:false};}});
 try{
  const dialogs=f.load('StoreDigitalDialogs'),c=f.load('StoreDigitalLocale').storeDigitalCopy('en');const tab=f.container.querySelector('.ph-sd-tab');
  const assertBlocked=async()=>{await f.settle(()=>tab.dispatchEvent(new window.CustomEvent('vgp_onbuttondown',{detail:{button:GamepadButton.BUMPER_RIGHT,source:0},bubbles:true,cancelable:true})));assert.equal(active(f),'Your games');};
  await f.settle(()=>dialogs.openStoreDigitalInstall(f.controller,key,c,()=>assert.fail('no installation expected')));await assertBlocked();
  await f.settle(()=>dialogs.openStoreDigitalDirectory(f.controller,c,undefined,()=>assert.fail('no directory selection expected')));await assertBlocked();
  await f.settle(()=>f.modals.at(-1).Close());await assertBlocked();await f.settle(()=>f.modals[0].Close());
  let confirmation;await f.settle(()=>{confirmation=dialogs.confirmStoreDigital(c.confirmUninstall,'Fixture game',c.uninstall,c);});await assertBlocked();assert.equal(f.calls.some(call=>call.name==='enqueue'||call.name==='uninstall'),false);
  await f.settle(()=>f.modals.at(-1).Close());await f.settle(()=>f.native.focusElement(tab));await f.settle(()=>f.native.pressBumper('right'));assert.equal(active(f),'Downloads');void confirmation;
 }finally{await f.close();}
});
test('an open native store sign-in leaves shoulder events unhandled on the current page',async()=>{
 const data=snapshot(1,4);data.accounts[0].state='disconnected';const f=await digitalFixture({data});try{
  await f.settle(()=>f.controller.signIn('epic'));assert.equal(f.controller.state().signingIn,'epic');const tab=f.container.querySelector('.ph-sd-tab');
  const event=new window.CustomEvent('vgp_onbuttondown',{detail:{button:GamepadButton.BUMPER_RIGHT,source:0},bubbles:true,cancelable:true});await f.settle(()=>tab.dispatchEvent(event));assert.equal(active(f),'Your games');assert.equal(event.defaultPrevented,false);
 }finally{await f.close();}
});
test('shoulder tabs preserve selected game, query and filters and retain existing B behavior',async()=>{
 const f=await digitalFixture();try{
  const input=f.container.querySelector('input');await f.settle(()=>{input.value='A game 00';input.dispatchEvent(new window.Event('input',{bubbles:true}));});
  let card=f.container.querySelector('.ph-sd-game');await f.settle(()=>f.native.focusElement(card));await f.settle(()=>f.native.pressY());
  const gog=[...f.modals[0].container.querySelectorAll('button')].find(node=>node.textContent==='GOG');await f.settle(()=>gog.click());await f.settle(()=>f.modals[0].Close());
  const count=f.container.querySelectorAll('.ph-sd-game').length;assert.equal(count,2);card=f.container.querySelector('.ph-sd-game');const key=card.dataset.gameKey;
  await f.settle(()=>card.click());const title=f.container.querySelector('.ph-sd-detail h2').textContent;
  await f.settle(()=>f.native.pressBumper('right'));assert.equal(active(f),'Downloads');assert.equal(f.container.querySelector('.ph-sd-detail'),null);
  await f.settle(()=>f.native.pressBumper('left'));assert.equal(active(f),'Your games');assert.equal(f.container.querySelector('.ph-sd-detail h2').textContent,title);
  await f.settle(()=>f.native.pressCancel());assert.equal(f.container.querySelector('.ph-sd-detail'),null);assert.equal(f.native.focused().Element.dataset.gameKey,key);assert.equal(f.container.querySelector('input').value,'A game 00');assert.equal(f.container.querySelectorAll('.ph-sd-game').length,count);
  await f.settle(()=>f.native.pressBumper('right'));await f.settle(()=>f.native.pressCancel());assert.deepEqual(f.routes,['/library/home']);assert.deepEqual(f.navigationCalls.at(-1),{route:'/library/home',replace:true});
 }finally{await f.close();}
});
test('a progressive library resumes the same hundredth game and scroll position after a shoulder tab round trip',async()=>{
 const f=await digitalFixture({data:snapshot(1,200)});try{
  await f.settle(()=>f.native.focusElement(f.container.querySelectorAll('.ph-sd-game')[36]));
  await f.settle(()=>f.native.focusElement(f.container.querySelectorAll('.ph-sd-game')[84]));
  const card=f.container.querySelectorAll('.ph-sd-game')[99],id=card.dataset.gameKey;assert.ok(card);
  await f.settle(()=>f.native.focusElement(card));f.container.querySelector('.ph-sd-content').scrollTop=3600;
  await f.settle(()=>f.native.pressBumper('right'));assert.equal(active(f),'Downloads');
  await f.settle(()=>f.native.pressBumper('left'));assert.equal(active(f),'Your games');
  assert.equal(f.native.focused().Element.dataset.gameKey,id);assert.equal(f.container.querySelector('.ph-sd-content').scrollTop,3600);assert.equal(f.container.querySelectorAll('.ph-sd-game').length,144);
 }finally{await f.close();}
});
