import test from 'node:test';
import assert from 'node:assert/strict';
import {digitalFixture,React,snapshot} from './storeDigitalFixture.mjs';
const codes=['amazon_setup_runtime_missing','amazon_setup_unsupported','amazon_setup_publisher_unverified','amazon_setup_metadata_invalid','amazon_setup_file_unverified','amazon_setup_file_changed','amazon_setup_cabinet_invalid','amazon_setup_ownership_lost'];
test('Amazon post-install failures render their localized reason inside the actual Downloads component',async()=>{
 const data=snapshot(1,4);data.jobs=codes.map((code,index)=>({jobId:'amazon-'+index,key:data.games[3].key,accountRevision:1,title:'Amazon game '+index,state:'failed',revision:1,downloadedBytes:1000,canPause:false,canResume:false,canCancel:false,installPath:'F:\\Games\\Amazon',...(index%2?{reasonCode:code}:{errorCode:code})}));
 data.jobs.push({...data.jobs[0],jobId:'completed',state:'completed'});
 const f=await digitalFixture({data});try{const locale=f.load('StoreDigitalLocale');
  for(const language of ['en-US','it-IT']){const c=locale.storeDigitalCopy(language);await f.render(React.createElement(f.load('StoreDigitalPage').StoreDigitalDownloads,{controller:f.controller,c}));
   const rows=[...f.container.querySelectorAll('.ph-sd-download')];assert.equal(rows.length,9);
   for(let index=0;index<codes.length;index++){const message=locale.storeDigitalRuntimeError(codes[index],c);assert.ok(message);assert.notEqual(message,c.errorCommand);assert.equal(rows[index].querySelector('[role="alert"]').textContent,message);assert.ok(!rows[index].textContent.includes(codes[index]));assert.equal(rows[index].querySelector('[data-state]').textContent,c.failed);}
   assert.equal(rows[8].querySelector('[role="alert"]'),null,'old error metadata cannot turn confirmed completion into a new failure');
   assert.equal(locale.storeDigitalRuntimeError('amazon_setup_future_unknown',c),null);assert.equal(locale.storeDigitalError('amazon_setup_future_unknown',c),c.errorCommand,'unknown codes never leak raw server text');
  }
 }finally{await f.close();}
});
test('Amazon setup failure ACK preserves the exact mapped reason and never claims installation success',async()=>{
 for(const code of codes){const f=await digitalFixture({data:snapshot(1,4),method(name){if(name==='prepare')return{errorCode:code};}});try{let result;await f.settle(async()=>{result=await f.controller.prepare({store:'amazon',id:'4'});});assert.equal(result,null);assert.equal(f.controller.state().error,code);assert.equal(f.controller.game({store:'amazon',id:'4'}).isInstalled,false);assert.equal(f.calls.some(call=>call.name==='enqueue'),false);}finally{await f.close();}}
});
