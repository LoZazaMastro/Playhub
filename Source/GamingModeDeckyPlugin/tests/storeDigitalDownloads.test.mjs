import test from 'node:test';import assert from 'node:assert/strict';
import {digitalFixture,GamepadButton,React,snapshot} from './storeDigitalFixture.mjs';
const job=(index,state,flags={})=>({jobId:`10000000-0000-4000-8000-${String(index).padStart(12,'0')}`,key:{store:'epic',id:'1'},accountRevision:1,title:'Job '+index,state,revision:1,downloadedBytes:0,canPause:false,canResume:false,canCancel:false,installPath:'F:\\Games\\Fixture',...flags});
const press=(f,button)=>f.native.focused().Element.dispatchEvent(new window.CustomEvent('vgp_onbuttondown',{detail:{button,source:0,is_repeat:false},bubbles:true,cancelable:true}));
const tab=f=>[...f.container.querySelectorAll('.ph-sd-tab')].find(node=>node.textContent.includes('Downloads'));
const row=(f,id)=>[...f.container.querySelectorAll('[data-store-job]')].find(node=>node.dataset.storeJob===id);
test('the old empty Steam container cannot take focus, while every current download row can, including static states',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'externalHandoff',{reasonCode:'windows_store_install_unconfirmed'}),job(2,'completed'),job(3,'interrupted')];const f=await digitalFixture({data});try{
  await f.render(React.createElement(f.native.Focusable,{className:'baseline-download','flow-children':'column',noFocusRing:true},React.createElement('div',null,'Status only')));assert.equal(f.native.focusElement(f.container.querySelector('.baseline-download')),false,'baseline reproduces the unreachable row');
  await f.render(React.createElement(f.load('StoreDigitalPage').StoreDigitalDownloads,{controller:f.controller,c:f.load('StoreDigitalLocale').storeDigitalCopy('en')}));
  for(const item of data.jobs){const target=row(f,item.jobId);await f.settle(()=>assert.equal(f.native.focusElement(target),true));assert.equal(f.native.focused().Element,target);assert.ok(target.classList.contains('gpfocus'));}
 }finally{await f.close();}
});
test('native four-way rows open one A-options modal with no inline actions and restore the exact row on B',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'downloading',{canPause:true,canCancel:true,canRemove:true,removalKind:"cleanup"}),job(2,'externalHandoff',{reasonCode:'windows_store_install_unconfirmed'}),job(3,'canceled'),job(4,'completed'),job(5,'pausedByUser',{canResume:true,canCancel:true,canRemove:true,removalKind:"cleanup"})];const f=await digitalFixture({data});try{
  await f.settle(()=>tab(f).click());await f.settle(()=>f.native.focusElement(row(f,data.jobs[0].jobId)));
  for(let n=1;n<4;n++){await f.settle(()=>press(f,GamepadButton.DIR_DOWN));assert.equal(f.native.focused().Element,row(f,data.jobs[n].jobId));}
  for(let n=0;n<3;n++)await f.settle(()=>press(f,GamepadButton.DIR_UP));const origin=row(f,data.jobs[0].jobId);assert.equal(f.native.focused().Element,origin);
  for(const direction of [GamepadButton.DIR_LEFT,GamepadButton.DIR_RIGHT]){await f.settle(()=>press(f,direction));assert.equal(f.native.focused().Element,origin);}
  assert.equal(f.container.querySelectorAll('.ph-sd-download button').length,0);
  await f.settle(()=>{press(f,GamepadButton.OK);origin.click();});assert.equal(f.modals.length,1);const menu=f.modals[0].container;
  assert.deepEqual([...menu.querySelectorAll('[data-job-action]')].map(node=>node.dataset.jobAction),['pause','cancel']);assert.equal(f.native.focused().Element.dataset.jobAction,'pause');
  await f.settle(()=>f.native.pressCancel());assert.equal(menu.isConnected,false);assert.equal(f.native.focused().Element,origin);assert.equal(f.calls.some(call=>call.name==='control'),false);
 }finally{await f.close();}
});

test('native Down enters status-only Downloads and A opens a navigable Close option',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'externalHandoff',{reasonCode:'windows_store_install_unconfirmed'})];const f=await digitalFixture({data});try{await f.settle(()=>tab(f).click());await f.settle(()=>f.native.focusElement(tab(f)));await f.settle(()=>press(f,GamepadButton.DIR_DOWN));assert.equal(f.native.focused().Element,row(f,data.jobs[0].jobId));await f.settle(()=>press(f,GamepadButton.OK));assert.ok(f.modals[0].container.querySelector('[data-job-action=cancel]'));assert.equal(f.native.focused().Element.textContent,'Close');}finally{await f.close();}
});

test('cancel confirmation explains partial-file deletion and declining restores the exact menu action',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'downloading',{canPause:true,canCancel:true,canRemove:true,removalKind:"cleanup"})];const f=await digitalFixture({data});try{await f.settle(()=>tab(f).click());await f.settle(()=>row(f,data.jobs[0].jobId).click());const cancel=f.modals[0].container.querySelector('[data-job-action=cancel]');await f.settle(()=>f.native.focusElement(cancel));await f.settle(()=>cancel.click());assert.equal(f.modals.length,2);const reject=[...f.modals[1].container.querySelectorAll('button')].find(button=>button.textContent==='Cancel');await f.settle(()=>reject.click());assert.equal(f.native.focused().Element,cancel);assert.equal(f.calls.some(call=>call.name==='control'||call.name==='cancel-cleanup'),false);}finally{await f.close();}
});

test('single completed or canceled history removal sends the exact id and restores neighbor then tab',async()=>{
 const data=snapshot(1,4);data.games[0].isInstalled=true;data.games[0].isLaunchable=true;data.jobs=[job(1,'canceled'),job(2,'completed')];const f=await digitalFixture({data,method(name,args){if(name==='snapshot')return{...data,jobs:[...data.jobs]};if(name==='remove-history'){assert.ok(data.jobs.some(job=>job.jobId===args.jobId));assert.equal(args.expectedRevision,data.revision);data.jobs=data.jobs.filter(job=>job.jobId!==args.jobId);data.revision++;return{accepted:true,snapshot:{...data,jobs:[...data.jobs]},jobId:args.jobId};}}});try{
  await f.settle(()=>tab(f).click());const ids=data.jobs.map(job=>job.jobId);for(const id of ids){await f.settle(()=>f.native.focusElement(row(f,id)));await f.settle(()=>press(f,GamepadButton.OK));const menu=f.modals.at(-1);assert.equal(menu.container.querySelector('[data-job-action=remove]').textContent,'Remove from queue');await f.settle(()=>menu.container.querySelector('[data-job-action=remove]').click());assert.equal(row(f,id),undefined);assert.ok(f.native.focused().Element.isConnected);}
  assert.equal(f.native.focused().Element,tab(f));const calls=f.calls.filter(call=>call.name==='remove-history');assert.equal(calls.length,2);assert.deepEqual(calls.map(call=>call.args.jobId),ids);for(const call of calls)assert.deepEqual(Object.keys(call.args).sort(),['expectedAccountRevision','expectedJobRevision','expectedKey','expectedRevision','jobId','requestId']);assert.equal(f.controller.game({store:'epic',id:'1'}).isInstalled,true);
 }finally{await f.close();}
});

test('failed history ACK retains both row and modal while nonterminal jobs cannot request removal',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'completed'),job(2,'canceling'),job(3,'interrupted'),job(4,'externalHandoff',{reasonCode:'windows_store_install_unconfirmed'})];const f=await digitalFixture({data,method(name){if(name==='remove-history')return{accepted:false,errorCode:'provider_busy',snapshot:data};}});try{
  for(const item of data.jobs.slice(1)){let accepted;await f.settle(async()=>{accepted=await f.controller.removeHistory(item.jobId);});assert.equal(accepted,false);}assert.equal(f.calls.some(call=>call.name==='remove-history'),false);
  await f.settle(()=>tab(f).click());await f.settle(()=>row(f,data.jobs[0].jobId).click());await f.settle(()=>f.modals[0].container.querySelector('[data-job-action=remove]').click());assert.ok(row(f,data.jobs[0].jobId));assert.equal(f.controller.state().error,'providerBusy');assert.ok(f.modals[0].container.isConnected);assert.equal(f.native.focused().Element.textContent,'Close');
 }finally{await f.close();}
});

test('the real transfer sorts first without replacing keyed rows or the job held by an open menu',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'queued',{canDownloadNow:true}),job(2,'pausedByUser',{canResume:true}),job(3,'downloading',{canPause:true})];const f=await digitalFixture({data,method(name){if(name==='snapshot')return{...data,jobs:[...data.jobs]};if(name==='clear-completed'){data.jobs=data.jobs.filter(job=>!['completed','canceled'].includes(job.state));data.revision++;return{accepted:true,snapshot:{...data,jobs:[...data.jobs]}};}}});try{
  await f.settle(()=>tab(f).click());assert.equal(f.container.querySelector('[data-store-job]').dataset.storeJob,data.jobs[2].jobId);const held=row(f,data.jobs[1].jobId);await f.settle(()=>f.native.focusElement(held));await f.settle(()=>press(f,GamepadButton.OK));
  data.jobs=[{...data.jobs[0],state:'downloading',canDownloadNow:false,canPause:true},{...data.jobs[1]},{...data.jobs[2],state:'completed',canPause:false}];data.revision++;await f.settle(()=>f.controller.read());assert.equal(f.container.querySelector('[data-store-job]').dataset.storeJob,data.jobs[0].jobId);assert.equal(row(f,data.jobs[1].jobId),held);assert.ok(f.modals[0].container.textContent.includes('Job 2'));assert.deepEqual([...f.modals[0].container.querySelectorAll('[data-job-action]')].map(node=>node.dataset.jobAction),['resume','cancel']);await f.settle(()=>f.native.pressCancel());assert.equal(f.native.focused().Element,held);
  data.jobs=data.jobs.map(job=>({...job,state:'canceled',canPause:false,canResume:false}));data.revision++;await f.settle(()=>f.controller.read());const clear=[...f.container.querySelectorAll('button')].find(button=>button.textContent==='Clear completed');assert.equal(clear.disabled,false);await f.settle(()=>f.native.focusElement(clear));await f.settle(()=>clear.click());assert.equal(f.native.focused().Element,tab(f));assert.ok(f.container.textContent.includes('No downloads yet'));
 }finally{await f.close();}
});

test('the Filters Y badge is absent while physical Y and positive-only Download badges remain',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'completed'),job(2,'canceled')];const f=await digitalFixture({data,method(name){if(name==='snapshot')return{...data,jobs:[...data.jobs]};}});try{assert.equal(f.container.querySelector('.ph-sd-filters .ph-sd-key'),null);assert.equal(f.container.querySelector('.ph-sd-count'),null);const filter=f.container.querySelector('.ph-sd-filters');await f.settle(()=>f.native.focusElement(filter));await f.settle(()=>f.native.pressY());assert.equal(f.modals.length,1);await f.settle(()=>f.modals[0].Close());data.jobs.push(job(3,'downloading',{canPause:true}));data.revision++;await f.settle(()=>f.controller.read());assert.equal(f.container.querySelector('.ph-sd-count').textContent,'1');
 }finally{await f.close();}
});

test('old cancel capability cannot promise cleanup, while new failed cleanup capability enables exact removal',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'downloading',{canCancel:true}),job(2,'failed',{canRemove:true,removalKind:"cleanup"}),{...job(3,'externalHandoff'),key:{store:'xbox',id:'3'},reasonCode:'windows_store_managed',errorCode:'windows_store_install_unconfirmed'}];const f=await digitalFixture({data,method(name,args){if(name==='cancel-cleanup'){assert.equal(args.jobId,data.jobs[2].jobId);assert.equal(args.expectedRevision,data.revision);return{accepted:false,errorCode:'queue_changed',snapshot:data};}}});try{await f.settle(()=>tab(f).click());await f.settle(()=>row(f,data.jobs[0].jobId).click());assert.ok(f.modals.at(-1).container.querySelector('[data-job-action=cancel]'));await f.settle(()=>f.native.pressCancel());await f.settle(()=>row(f,data.jobs[1].jobId).click());assert.equal(f.modals.at(-1).container.querySelector('[data-job-action=cancel]').textContent,'Remove from queue');await f.settle(()=>f.native.pressCancel());await f.settle(()=>row(f,data.jobs[2].jobId).click());const menu=f.modals.at(-1);assert.ok(menu.container.textContent.includes('remains managed by Microsoft Store'));await f.settle(()=>menu.container.querySelector('[data-job-action=cancel]').click());assert.ok(row(f,data.jobs[2].jobId));assert.equal(f.calls.filter(call=>call.name==='job-action').length,1);}finally{await f.close();}
});

test('history and Windows request removals preserve honest notes and never open a file-deletion confirmation',async()=>{
 const data=snapshot(1,4);data.jobs=[job(1,'failed',{canRemove:true,removalKind:'history'}),{...job(2,'failed',{canRemove:true,removalKind:'windowsRequest'}),key:{store:'xbox',id:'3'},reasonCode:'windows_store_managed',errorCode:'windows_store_install_failed'},job(3,'failed',{canRemove:true})];
 const f=await digitalFixture({data,control(held,action){assert.equal(action,'cancel');assert.ok(['history','windowsRequest'].includes(held.removalKind));data.jobs=data.jobs.filter(row=>row.jobId!==held.jobId);return{accepted:true,snapshot:{...data,revision:++data.revision,jobs:[...data.jobs]}};}});
 try{await f.settle(()=>tab(f).click());for(const [id,note]of [[data.jobs[0].jobId,'Game files are kept'],[data.jobs[1].jobId,'remains managed by Microsoft Store']]){const before=f.modals.length;await f.settle(()=>row(f,id).click());const menu=f.modals.at(-1).container;assert.deepEqual([...menu.querySelectorAll('[data-job-action]')].map(node=>node.dataset.jobAction),['cancel']);assert.ok(menu.textContent.includes(note));assert.equal(menu.textContent.includes('partial files will be deleted'),false);await f.settle(()=>menu.querySelector('[data-job-action=cancel]').click());assert.equal(f.modals.length,before+1,'no destructive confirmation');assert.equal(row(f,id),undefined);}await f.settle(()=>row(f,data.jobs[0].jobId).click());assert.ok(f.modals.at(-1).container.querySelector('[data-job-action=cancel]'),'unknown cleanup authority cannot remove the dismissal command');assert.equal(f.calls.filter(call=>call.name==='job-action').length,2);}finally{await f.close();}
});


test('download options navigates the visible horizontal action row with native pad and keyboard while snapshots keep the selected action',async()=>{
 const data=snapshot(1,4);data.jobs=[job(101,'downloading',{canPause:true,canCancel:true,canRemove:true,removalKind:'cleanup'})];const f=await digitalFixture({data,method(name){if(name==='snapshot')return{...data,jobs:[...data.jobs]};}});try{
  await f.settle(()=>tab(f).click());const origin=row(f,data.jobs[0].jobId);await f.settle(()=>origin.click());const modal=f.modals.at(-1),pause=modal.container.querySelector('[data-job-action=pause]'),cancel=modal.container.querySelector('[data-job-action=cancel]'),close=[...modal.container.querySelectorAll('button')].find(node=>node.textContent==='Close');
  assert.equal(f.native.focused().Element,pause);await f.settle(()=>press(f,GamepadButton.DIR_RIGHT));assert.equal(f.native.focused().Element,cancel);await f.settle(()=>press(f,GamepadButton.DIR_RIGHT));assert.equal(f.native.focused().Element,close);await f.settle(()=>press(f,GamepadButton.DIR_LEFT));assert.equal(f.native.focused().Element,cancel);
  const held=f.native.focused();data.jobs=[{...data.jobs[0],downloadedBytes:10,revision:2}];data.revision++;await f.settle(()=>f.controller.read());assert.equal(f.native.focused(),held,'snapshot updates do not move action focus');
  const key=async(value)=>f.settle(()=>{for(const type of ['keydown','keyup'])f.native.focused().Element.dispatchEvent(new window.KeyboardEvent(type,{key:value,code:value,bubbles:true,cancelable:true}));});await key('ArrowLeft');assert.equal(f.native.focused().Element,pause);await key('ArrowRight');assert.equal(f.native.focused().Element,cancel);
  assert.equal(f.calls.some(call=>['control','cancel-cleanup','job-action'].includes(call.name)),false,'movement never invokes a transfer command');await f.settle(()=>f.native.pressCancel());assert.equal(modal.container.isConnected,false);assert.equal(f.native.focused().Element,origin,'B restores the held download row once');
 }finally{await f.close();}
});
