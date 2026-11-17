import test from 'node:test';import assert from 'node:assert/strict';
import {digitalFixture,snapshot} from './storeDigitalFixture.mjs';
test('Add to Steam is shown only for installed games with fresh proven absence and rechecks before repair',async()=>{
 for(const state of ['unknown','present','absent']){const data=snapshot(1,4),game=data.games[0];game.isInstalled=true;game.isLaunchable=true;game.installPath='F:\\Games\\Fixture';let link=state,calls=0;
  const f=await digitalFixture({data,runtime:{readStoreSteamLink:()=>link,repairStoreSteamShortcut:async key=>{calls++;assert.equal(key.id,game.key.id);link='unknown';return true;}},method(name){if(name==='installed-game')return{key:game.key,accountRevision:1,libraryRevision:1,isInstalled:true,canLaunch:true,canUninstall:true,installPath:game.installPath};}});
  try{await f.settle(()=>f.container.querySelector('.ph-sd-game').click());const button=[...f.container.querySelectorAll('.ph-sd-detail button')].find(node=>node.textContent==='Add to Steam library');assert.equal(!!button,state==='absent');if(button){link='present';await f.settle(()=>button.click());assert.equal(calls,0,'late existing shortcut wins');link='absent';await f.settle(()=>button.click());assert.equal(calls,1);assert.equal(f.container.querySelector('.ph-sd-detail').textContent.includes('Add to Steam library'),false);}}finally{await f.close();}
 }
});
