import test from 'node:test';import assert from 'node:assert/strict';
import {digitalFixture,React,snapshot} from './storeDigitalFixture.mjs';
const history={jobId:'history',key:{store:'xbox',id:'BLINX'},accountRevision:9,title:'BLiNX',state:'completed',revision:18,downloadedBytes:100,totalBytes:100,canPause:false,canResume:false,canCancel:false,canRemove:true,removalKind:'history',installPath:'D:\\XboxGames\\BLiNX'};
const current={...history,jobId:'active',key:{store:'xbox',id:'CRIMSON'},title:'Crimson',state:'downloading',canPause:true,canCancel:true,removalKind:'cleanup'};
test('all twelve languages and English fallback distinguish queue conflicts from an actual account change in a mounted download modal',async()=>{
 const data=snapshot(12,4);data.jobs=[history,current];data.accounts[2].revision=9;data.games[2].accountRevision=9;
 const f=await digitalFixture({data});try{
  const locale=f.load('StoreDigitalLocale'),Options=f.load('StoreDigitalDownloads').StoreDigitalDownloadOptions;
  for(const language of ['en','it','de','es','fr','pt','ru','uk','zh','ja','ko','hi','unsupported']){
   const c=locale.storeDigitalCopy(language);
   for(const code of ['queue_changed','job_changed','library_changed']){
    await f.settle(()=>f.controller.update({error:code,errorJobId:history.jobId}));
    await f.render(React.createElement(Options,{controller:f.controller,c,job:history,closeModal(){}}));
    assert.equal(f.container.querySelector('[role=alert]').textContent,c.errorQueueChanged,language+'/'+code);
    assert.notEqual(c.errorQueueChanged,c.errorChanged);assert.ok(!f.container.textContent.includes(code));assert.ok(!f.container.textContent.includes(c.errorChanged));
    assert.equal(locale.storeDigitalUninstallError(code,c),c.errorQueueChanged);
   }
   assert.equal(locale.storeDigitalError('changed',c),c.errorChanged,'actual account-change errors retain their own explanation');
  }
  assert.equal(f.calls.some(call=>['job-action','remove-history','cancel-cleanup'].includes(call.name)),false);
 }finally{await f.close();}
});
test('a download modal shows only errors scoped to its job, and a replaced job has an honest queue-change notice',async()=>{
 const data=snapshot(12,4);data.jobs=[history,current];const f=await digitalFixture({data});try{
  const c=f.load('StoreDigitalLocale').storeDigitalCopy('it'),Options=f.load('StoreDigitalDownloads').StoreDigitalDownloadOptions;
  await f.render(React.createElement(Options,{controller:f.controller,c,job:history,closeModal(){}}));
  for(const errorJobId of [current.jobId,null]){await f.settle(()=>f.controller.update({error:'changed',errorJobId}));assert.equal(f.container.querySelector('[role=alert]'),null,'other operation errors do not leak into this job menu');}
  await f.settle(()=>f.controller.update({error:'queue_changed',errorJobId:history.jobId}));assert.equal(f.container.querySelector('[role=alert]').textContent,c.errorQueueChanged);
  await f.settle(()=>f.controller.update({error:'changed',errorJobId:history.jobId}));assert.equal(f.container.querySelector('[role=alert]').textContent,c.errorChanged,'a genuine scoped account change remains visible');
  await f.settle(()=>f.controller.update({error:null,errorJobId:null,snapshot:{...data,revision:13,jobs:[{...history,installPath:'E:\\Moved'},current]}}));
  assert.equal(f.container.querySelector('[role=alert]').textContent,c.errorQueueChanged);assert.equal(f.container.querySelector('[data-job-action]'),null,'changed job identity cannot receive an operation from an old modal');
 }finally{await f.close();}
});
