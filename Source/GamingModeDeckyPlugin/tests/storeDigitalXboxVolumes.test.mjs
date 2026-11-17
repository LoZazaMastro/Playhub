import test from 'node:test';import assert from 'node:assert/strict';import {digitalFixture,snapshot} from './storeDigitalFixture.mjs';
const volume=String.raw`\\?\Volume{11111111-2222-3333-4444-555555555555}`;
const locations=[{volumeName:volume,rootPath:'F:\\',freeBytes:100000,isReady:true}];
async function open(f){await f.settle(()=>f.container.querySelectorAll('.ph-sd-game')[2].click());await f.settle(()=>[...f.container.querySelectorAll('.ph-sd-detail button')].find(b=>b.textContent.includes('Install')).click());return f.modals.at(-1).container;}
test('Xbox Store requires explicit registered disk selection and uses the same volume contract as Game Pass',async()=>{
 const f=await digitalFixture({data:snapshot(1,4),method(name,args){if(name==='xbox-install-volumes')return{accountRevision:1,locations};if(name==='xbox-account-owned')return{revision:4,isStale:false,games:[{productId:'3',supportsPc:true}]};if(name==='xbox-account-open-owned')return{accepted:true,state:'official-install-requested',productId:args.productId,requestId:args.requestId};}});
 try{const modal=await open(f),confirm=()=>[...modal.querySelectorAll('button')].find(b=>b.textContent==='Install now');assert.equal(confirm().disabled,true);assert.equal(f.calls.some(c=>c.name==='xbox-account-open-owned'),false);assert.equal(f.native.focused()?.Element,modal.querySelector('.ph-sd-folder'));await f.settle(()=>modal.querySelector('.ph-sd-folder').click());assert.equal(confirm().disabled,false);await f.settle(()=>confirm().click());const call=f.calls.find(c=>c.name==='xbox-account-open-owned');assert.equal(call.args.volumeName,volume);assert.equal(call.args.expectedRevision,4);assert.equal(f.container.querySelector('.ph-sd-tab.is-active').textContent,'Downloads');}finally{await f.close();}
});
test('unavailable or malformed volumes never start an Xbox installation; Back restores a live native target',async()=>{
 for(const invalid of [[],[{...locations[0],rootPath:'F:\\foreign'}],[{...locations[0],isReady:false}],[{...locations[0],volumeName:'arbitrary-folder'}]]){
  const f=await digitalFixture({data:snapshot(1,4),method(name){if(name==='xbox-install-volumes')return{accountRevision:1,locations:invalid};}});try{const modal=await open(f);assert.ok(modal.querySelector('[role=alert]'));assert.equal(modal.querySelectorAll('.ph-sd-folder').length,0);await f.settle(()=>f.native.pressCancel());assert.equal(modal.isConnected,false);assert.equal(f.native.focused()?.Element?.isConnected,true);assert.equal(f.calls.some(c=>c.name==='xbox-account-open-owned'),false);}finally{await f.close();}
 }
});
test('late volume discovery after closing the dialog cannot install or reopen it',async()=>{
 let resolve;const pending=new Promise(done=>resolve=done);const f=await digitalFixture({data:snapshot(1,4),method(name){if(name==='xbox-install-volumes')return pending;}});
 try{const modal=await open(f);await f.settle(()=>[...modal.querySelectorAll('button')].find(b=>b.textContent==='Cancel').click());await f.settle(()=>resolve({accountRevision:1,locations}));assert.equal(modal.isConnected,false);assert.equal(f.calls.some(c=>c.name==='xbox-account-open-owned'),false);assert.equal(f.container.querySelector('.ph-sd-tab.is-active').textContent,'Your games');}finally{await f.close();}
});
