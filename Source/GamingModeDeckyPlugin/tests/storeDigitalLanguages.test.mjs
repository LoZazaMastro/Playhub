import test from 'node:test';import assert from 'node:assert/strict';import {digitalFixture,snapshot,loadDigitalSource} from './storeDigitalFixture.mjs';
test('language dropdown contains actual game choices and changed language gets a fresh size and confirmation',async()=>{
 const data=snapshot(1,4),key=data.games[0].key,path='F:\\Games\\Game';let installs=[];
 const f=await digitalFixture({data,method(name,args){if(name==='prepare')return{key,accountRevision:1,revision:args.language==='it-IT'?3:2,selectionNonce:args.absoluteDestination?'nonce-'+args.language:null,expiresAt:new Date(Date.now()+60000).toISOString(),requiredBytes:args.language==='it-IT'?2000:1000,suggestedPath:path,confirmedPath:args.absoluteDestination??null,locations:[{rootPath:'F:\\',displayName:'F:',freeBytes:100000,isReady:true}],installKind:'direct',languages:['en-US','it-IT'],language:args.language==='it-IT'?'it-IT':'en-US'};if(name==='enqueue'){installs.push(args);return{accepted:true,snapshot:data};}}});
 try{await f.settle(()=>f.container.querySelector('.ph-sd-game').click());await f.settle(()=>[...f.container.querySelectorAll('.ph-sd-detail button')].find(b=>b.textContent.includes('Install')).click());const modal=f.modals.at(-1).container,select=modal.querySelector('select');assert.ok(select);assert.deepEqual([...select.options].map(o=>o.value),['en-US','it-IT']);await f.settle(()=>{select.value='it-IT';select.dispatchEvent(new f.dom.window.Event('change',{bubbles:true}));});assert.equal(modal.querySelector('select').value,'it-IT');assert.equal(installs.length,0);const call=f.calls.filter(c=>c.name==='prepare').at(-1);assert.equal(call.args.language,'it-IT');assert.equal(call.args.absoluteDestination,path);assert.ok(modal.textContent.includes('2.0 KiB'));await f.settle(()=>[...modal.querySelectorAll('button')].find(b=>b.textContent==='Install now').click());assert.equal(installs.length,1);assert.equal(installs[0].language,'it-IT');assert.equal(installs[0].selectionNonce,'nonce-it-IT');assert.equal(installs[0].expectedPreparationRevision,3);}finally{await f.close();}
});

test('a GOG package with zero or one language has no misleading language selector',async()=>{
 for(const languages of [[],['en-US'],['it-IT']]){
  const data=snapshot(1,4),game=data.games.find(game=>game.key.store==='gog'),path='F:\\Games\\GOG';
  data.games=[game];let installed;
  const f=await digitalFixture({data,method(name,args){
   if(name==='prepare')return {key:game.key,accountRevision:1,revision:2,selectionNonce:args.absoluteDestination?'gog-confirmed':null,expiresAt:new Date(Date.now()+60000).toISOString(),requiredBytes:1000,suggestedPath:path,confirmedPath:args.absoluteDestination??null,locations:[{rootPath:'F:\\',displayName:'F:',freeBytes:100000,isReady:true}],installKind:'direct',languages,language:languages[0]??null};
   if(name==='enqueue'){installed=args;return {accepted:true,snapshot:data};}
  }});
  try{
   await f.settle(()=>f.container.querySelector('.ph-sd-game').click());
   await f.settle(()=>[...f.container.querySelectorAll('.ph-sd-detail button')].find(button=>button.textContent.includes('Install')).click());
   const modal=f.modals.at(-1).container;
   assert.equal(modal.querySelector('select'),null,JSON.stringify(languages));
   assert.equal(modal.textContent.includes('American English'),false);
   await f.settle(()=>[...modal.querySelectorAll('button')].find(button=>button.textContent==='Continue').click());
   assert.equal(modal.querySelector('select'),null);
   await f.settle(()=>[...modal.querySelectorAll('button')].find(button=>button.textContent==='Install now').click());
   assert.ok(installed,'hiding the selector still installs the verified package');
   assert.equal(installed.language,languages[0]??undefined,'keep the actual download language when the provider requires it');
  }finally{await f.close();}
 }
});

test('language labels capitalize names and consistently use US and UK in all interface languages',()=>{
 const {formatStoreInstallLanguage,hasStoreInstallLanguageChoice}=loadDigitalSource()('StoreInstallLanguages');
 for(const locale of ['en','it','de','es','fr','pt','ru','uk','ja','ko','zh','hi']){
  const english=new Intl.DisplayNames([locale,'en'],{type:'language'}).of('en');
  const title=english.charAt(0).toLocaleUpperCase(locale)+english.slice(1);
  assert.equal(formatStoreInstallLanguage('en-US',locale),title+' (US)',locale);
  assert.equal(formatStoreInstallLanguage('en-GB',locale),title+' (UK)',locale);
  assert.equal(formatStoreInstallLanguage('en_UK',locale),title+' (UK)',locale);
  for(const code of ['it','fr','de','es','pt-BR']){
   const label=formatStoreInstallLanguage(code,locale);
   assert.equal(label.charAt(0),label.charAt(0).toLocaleUpperCase(locale),locale+'/'+code);
  }
 }
 assert.equal(formatStoreInstallLanguage('en-US','it'),'Inglese (US)');
 assert.equal(formatStoreInstallLanguage('en-GB','it'),'Inglese (UK)');
 assert.equal(hasStoreInstallLanguageChoice(['en-US','en_us']),false);
 assert.equal(hasStoreInstallLanguageChoice(['en-US','it-IT']),true);
});
