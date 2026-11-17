import test from 'node:test';
import assert from 'node:assert/strict';
import {digitalFixture,React,snapshot} from './storeDigitalFixture.mjs';

test('installed games keep an actionable removal button despite stale local status, without passive verification errors',async()=>{
 for(const code of ['installed_files_modified','uninstall_manifest_unavailable','provider_busy','installation_ownership_lost','uninstall_file_busy','uninstall_verification_timeout']){
  const data=snapshot(1,4),game=data.games[0];game.isInstalled=true;game.isLaunchable=true;game.installPath='F:\\Fixture\\Game';
  const f=await digitalFixture({data,method(name){if(name==='installed-game')return{key:game.key,accountRevision:1,libraryRevision:1,isInstalled:true,canLaunch:true,canUninstall:false,installPath:game.installPath,errorCode:null,uninstallErrorCode:code};}});
  try{const locale=f.load('StoreDigitalLocale');for(const language of ['en-US','it-IT']){
   const c=locale.storeDigitalCopy(language);
   await f.render(React.createElement(f.load('StoreDigitalPage').StoreDigitalDetail,{controller:f.controller,c,game,onBack(){},onDownloads(){},onAccounts(){}}));
   await f.settle();
   const removal=[...f.container.querySelectorAll('button')].find(button=>button.textContent===c.uninstall);
   assert.ok(removal&&!removal.disabled,code);
   assert.ok(!f.container.textContent.includes(c.errorRead));assert.ok(!f.container.textContent.includes(c.uninstallUnavailable));
   assert.ok([...f.container.querySelectorAll('button')].some(button=>button.textContent===c.statusRetry&&!button.disabled));
   assert.equal(f.controller.game(game.key).isLaunchable,true);
  }}finally{await f.close();}
 }
});

test('active removal waits for confirmed cancellation before preparation, and a rejected cancellation keeps the action available',async()=>{
 for(const accepted of [true,false]){
  const data=snapshot(1,4),game=data.games[0];game.isInstalled=true;game.installPath='F:\\Fixture\\Game';
  const job={jobId:'active',key:game.key,accountRevision:1,title:game.title,state:'downloading',revision:1,downloadedBytes:1,canPause:true,canResume:false,canCancel:true,installPath:game.installPath};data.jobs=[job];
  let release;const pending=new Promise(resolve=>release=resolve),after=structuredClone(data);after.revision=2;after.jobs=[];
  const receipt={key:game.key,accountRevision:1,libraryRevision:2,installPath:game.installPath,nonce:'reviewed',expiresAt:new Date(Date.now()+60000).toISOString(),canUninstall:true};
  const f=await digitalFixture({data,control:()=>pending,method(name){if(name==='installed-game')throw Error('unavailable local status');if(name==='prepare-uninstall')return receipt;if(name==='uninstall')return{accepted:true,snapshot:after};}});
  try{
   const c=f.load('StoreDigitalLocale').storeDigitalCopy('en');await f.render(React.createElement(f.load('StoreDigitalPage').StoreDigitalDetail,{controller:f.controller,c,game,onBack(){},onDownloads(){},onAccounts(){}}));
   const removal=()=>[...f.container.querySelectorAll('button')].find(button=>button.textContent===c.uninstall);
   await f.settle(()=>removal().click());assert.equal(f.calls.some(call=>call.name==='job-action'),false,'no cancellation before approval');
   await f.settle(()=>f.modals.at(-1).container.querySelector('button').click());
   assert.equal(f.calls.filter(call=>call.name==='job-action').length,1);assert.equal(f.calls.some(call=>call.name==='prepare-uninstall'),false,'must await cancellation/drain acknowledgement');
   await f.settle(()=>release({accepted,errorCode:accepted?undefined:'provider_busy',snapshot:accepted?after:data}));
   assert.equal(f.calls.filter(call=>call.name==='prepare-uninstall').length,accepted?1:0);assert.equal(f.calls.filter(call=>call.name==='uninstall').length,accepted?1:0);
   assert.ok(removal()&&!removal().disabled,'can retry after either outcome');
  }finally{await f.close();}
 }
});

test('canceling active removal performs no operation, and a different server removal path requires its own approval',async()=>{
 for(const {cancelFirst,approveSecond} of [{cancelFirst:true,approveSecond:false},{cancelFirst:false,approveSecond:false},{cancelFirst:false,approveSecond:true}]){
  const data=snapshot(1,4),game=data.games[0];game.isInstalled=true;game.installPath='F:\\Fixture\\Installed';
  data.jobs=[{jobId:'update',key:game.key,accountRevision:1,title:game.title,state:'downloading',revision:1,downloadedBytes:1,canPause:true,canResume:false,canCancel:true,installPath:'E:\\Fixture\\Download'}];
  const after=structuredClone(data);after.revision=2;after.jobs=[];
  const receipt={key:game.key,accountRevision:1,libraryRevision:2,installPath:game.installPath,nonce:'different-path',expiresAt:new Date(Date.now()+60000).toISOString(),canUninstall:true};
  const f=await digitalFixture({data,control:()=>({accepted:true,snapshot:after}),method(name){if(name==='installed-game')throw Error('unavailable local status');if(name==='prepare-uninstall')return receipt;if(name==='uninstall')return{accepted:true,snapshot:after};}});
  try{
   const c=f.load('StoreDigitalLocale').storeDigitalCopy('en');await f.render(React.createElement(f.load('StoreDigitalPage').StoreDigitalDetail,{controller:f.controller,c,game,onBack(){},onDownloads(){},onAccounts(){}}));
   await f.settle(()=>[...f.container.querySelectorAll('button')].find(button=>button.textContent===c.uninstall).click());
   await f.settle(()=>f.modals[0].container.querySelectorAll('button')[cancelFirst?1:0].click());
   if(cancelFirst){assert.equal(f.calls.some(call=>['job-action','prepare-uninstall','uninstall'].includes(call.name)),false);}
   else{assert.equal(f.calls.filter(call=>call.name==='prepare-uninstall').length,1);assert.equal(f.calls.some(call=>call.name==='uninstall'),false,'the approved download path cannot authorize deleting another installation');assert.equal(f.modals.length,2,'review the actual installation path separately');await f.settle(()=>f.modals[1].container.querySelectorAll('button')[approveSecond?0:1].click());assert.equal(f.calls.filter(call=>call.name==='uninstall').length,approveSecond?1:0,'only the second approval authorizes the server path');if(approveSecond)assert.equal(f.calls.find(call=>call.name==='uninstall').args.preparation.installPath,game.installPath);}
   assert.ok([...f.container.querySelectorAll('button')].some(button=>button.textContent===c.uninstall&&!button.disabled));
  }finally{await f.close();}
 }
});
