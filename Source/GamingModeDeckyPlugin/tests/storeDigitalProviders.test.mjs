import test from 'node:test';import assert from 'node:assert/strict';import {digitalFixture,React,snapshot} from './storeDigitalFixture.mjs';
const providers=['ea','ubisoft','battlenet','microsoft'],names=['EA app','Ubisoft Connect','Battle.net','Xbox'];
const catalog=()=>({revision:7,sourceRevision:'official-fixture',source:'official',market:'IT',language:'it-IT',platform:'pc',plan:'pc',validatedAt:new Date().toISOString(),expiresAt:new Date(Date.now()+3600000).toISOString(),isStale:false,canRequestInstall:true,games:providers.map((installProvider,index)=>({productId:'9AAA0000000'+index,title:'Provider fixture '+index,installProvider,artworkUrl:null,isCurrentMember:true,pcAvailability:'desktop-confirmed',entitlementState:'not-checked',installedState:'not-checked',canOpenOfficialPage:true}))});

test('provider badges use explicit metadata for every Game Pass row while retaining Xbox identity and the Game Pass group',async()=>{
 const metadata=catalog(),f=await digitalFixture({method(name){if(name==='xbox-catalog')return metadata;}});try{
  await f.settle(()=>f.container.querySelector('.ph-sd-filters').click());await f.settle(()=>[...f.modals[0].container.querySelectorAll('button')].find(button=>button.textContent==='Xbox Game Pass').click());await f.settle(()=>f.native.pressCancel());
  const cards=[...f.container.querySelectorAll('.ph-sd-game')];assert.equal(cards.length,4);assert.ok(f.container.querySelector('.ph-sd-summary').textContent.includes('Xbox Game Pass'));
  const identities=f.load('StoreDigitalGameStoreBadge');
  for(const [index,card]of cards.entries()){
   const game=f.controller.gamePassGames()[index],badge=card.querySelector('.ph-sd-game-meta');assert.equal(game.key.store,'xbox');assert.equal(game.librarySource,'gamepass');assert.equal(game.installProvider,providers[index]);assert.equal(badge.textContent,names[index]);assert.equal(badge.querySelector('img').src,identities.digitalGameStoreIdentity(game).logo);assert.ok(badge.querySelector('img').src.startsWith('data:image/png;base64,'));
  }
  await f.settle(()=>cards[2].click());assert.equal(f.container.querySelector('.ph-sd-detail .ph-sd-game-meta').textContent,'Battle.net');assert.equal(f.calls.some(call=>['xbox-gamepass-install','xbox-account-open-owned','enqueue'].includes(call.name)),false);
 }finally{await f.close();}
});

test('Xbox Store rows share provider badges; old cache, unknown provider and titles containing launcher names keep Xbox',async()=>{
 const data=snapshot(1,4);data.games.forEach((game,index)=>{game.key={store:'xbox',id:String(index)};game.installProvider=providers[index];});
 const f=await digitalFixture({data});try{
  assert.deepEqual([...f.container.querySelectorAll('.ph-sd-game-meta')].map(badge=>badge.textContent),names);
  const identities=f.load('StoreDigitalGameStoreBadge'),models=f.load('StoreDigitalModels'),base=data.games[3];
  for(const installProvider of [undefined,null,'unknown','microsoft'])assert.equal(identities.digitalGameStoreIdentity({...base,installProvider,title:'Battle.net EA Ubisoft edition'}).name,'Xbox');
  assert.equal(identities.digitalGameStoreIdentity({...base,key:{store:'gog',id:'foreign'},installProvider:'battlenet'}).name,'GOG');
  assert.equal(models.validDigitalGame({...base,installProvider:'battlenet'}),true);assert.equal(models.validDigitalGame({...base,installProvider:'made-up-provider'}),false);
  const gp=f.load('StoreDigitalGamePass'),old=catalog();delete old.games[0].installProvider;assert.equal(gp.validGamePassSnapshot(old),true);old.games[0].installProvider='unexpected';assert.equal(gp.validGamePassSnapshot(old),false);
 }finally{await f.close();}
});

test('Xbox install review displays the classified provider without issuing an installation command',async()=>{
 const f=await digitalFixture({method(name){if(name==='xbox-install-volumes')return{accountRevision:1,locations:[{volumeName:String.raw`\\?\Volume{11111111-2222-3333-4444-555555555555}`,rootPath:'F:\\',freeBytes:10000,isReady:true}]};}});try{
  const game={...snapshot(1,4).games[2],installProvider:'battlenet',librarySource:'gamepass'};
  await f.render(React.createElement(f.load('StoreDigitalDialogs').StoreDigitalXboxInstallDialog,{controller:f.controller,game,c:f.load('StoreDigitalLocale').storeDigitalCopy('en'),onInstalled(){},closeModal(){}}));
  assert.equal(f.container.querySelector('.ph-sd-game-meta').textContent,'Battle.net');assert.equal(f.calls.some(call=>['xbox-gamepass-install','xbox-account-open-owned','enqueue'].includes(call.name)),false);
 }finally{await f.close();}
});

test('partner install refusals preserve their localized reason in Game Pass and owned Xbox dialogs without reporting success',async()=>{
 for(const provider of ['ea','ubisoft','battlenet','unknown'])for(const gamepass of [true,false]){
  const code=`xbox_partner_${provider}_install_unavailable`,data=snapshot(1,4),metadata=catalog();metadata.games[0].installProvider=provider;
  const game={...data.games[2],key:{store:'xbox',id:metadata.games[0].productId},installProvider:provider,...(gamepass?{librarySource:'gamepass'}:{})};data.games[2]=game;let installed=0;
  const f=await digitalFixture({data,method(name){
   if(name==='xbox-catalog')return metadata;
   if(name==='xbox-install-volumes')return{accountRevision:1,locations:[{volumeName:'\\\\?\\Volume{11111111-2222-3333-4444-555555555555}',rootPath:'F:'+String.fromCharCode(92),freeBytes:10000,isReady:true}]};
   if(name==='xbox-account-owned')return{revision:1,isStale:false,games:[{productId:game.key.id,supportsPc:true}]};
   if(name==='xbox-gamepass-install'||name==='xbox-account-open-owned')return{accepted:false,errorCode:code};
  }});try{
   if(gamepass)await f.settle(()=>f.controller.loadGamePass());
   const locale=f.load('StoreDigitalLocale'),c=locale.storeDigitalCopy('it');
   await f.render(React.createElement(f.load('StoreDigitalDialogs').StoreDigitalXboxInstallDialog,{controller:f.controller,game,c,onInstalled(){installed++;},closeModal(){}}));
   const drive=f.container.querySelector('.ph-sd-folder');assert.ok(drive,'official volume review loads');await f.settle(()=>drive.click());await f.settle(()=>[...f.container.querySelectorAll('button')].find(button=>button.textContent===c.confirmInstall).click());
   assert.equal(f.controller.state().error,code);assert.equal(installed,0);assert.equal(f.controller.game(game.key).isInstalled,false);
   const reason=locale.storeDigitalError(code,c);assert.notEqual(reason,c.errorCommand);assert.ok(reason.includes('non è ancora disponibile'));assert.ok(f.container.querySelector('[role=alert]').textContent.includes(reason));assert.ok(!f.container.textContent.includes(code));assert.equal(f.calls.some(call=>call.name==='enqueue'),false);
   const english=locale.storeDigitalError(code,locale.storeDigitalCopy('en'));assert.ok(english.includes('not available yet'));if(provider==='battlenet'){assert.ok(reason.includes('Battle.net'));assert.ok(english.includes('Battle.net'));}
   assert.equal(locale.storeDigitalError('xbox_partner_future_unknown',c),c.errorCommand,'unknown technical codes never leak into UI');
  }finally{await f.close();}
 }
});
