import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,writeFileSync,mkdtempSync,rmSync,existsSync} from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {spawnSync} from 'node:child_process';
import {pathToFileURL} from 'node:url';

const source=readFileSync(new URL('../src/PluginStorePage.tsx',import.meta.url),'utf8');
const css=source.match(/export const STORE_STYLE = `([\s\S]*?)`;/)[1];
const browser=['C:/Program Files/Google/Chrome/Application/chrome.exe','C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
test('actual Store CSS keeps square, portrait, panoramic and failed covers at one row height in Chromium',{skip:!browser},()=>{
 const directory=mkdtempSync(path.join(os.tmpdir(),'Playhub-Store-Card-Geometry-'));
 try {
  const sizes=[[1200,630],[256,256],[200,1600],[1800,180],[1,1]];
  const cards=sizes.map(([w,h],i)=>`<div class="ph-store-row"><div class="ph-store-row-media"><img class="ph-store-image" width="${w}" height="${h}" src="${i===4?'data:image/png,broken':`data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}"><rect width="100%" height="100%" fill="#645"/></svg>`)}`}"></div><div class="ph-store-row-copy"><h3 class="ph-store-row-title">${['Amethyst Deploy','Deckycord','ForzaHorizon-DualSense','Reroll','Museck'][i]}</h3><p class="ph-store-row-description">Original developer description with enough content to wrap over two lines.</p></div><div class="ph-store-row-trailing">Decky</div></div>`).join('');
  const script=`const rows=[...document.querySelectorAll('.ph-store-row')].map(row=>row.getBoundingClientRect().height);const expected=matchMedia('(max-width:700px)').matches?98:112;if(new Set(rows).size!==1||rows[0]!==expected)throw Error(JSON.stringify({rows,expected,innerWidth}));document.querySelector('#result').textContent='PASS '+JSON.stringify({rows,expected,innerWidth});`;
  const file=path.join(directory,'geometry.html');writeFileSync(file,`<style>${css}</style><body style="margin:0;background:#101014;color:white">${cards}<pre id="result">PENDING</pre><script>try{${script}}catch(e){document.querySelector('#result').textContent='FAIL '+e.message}</script></body>`);
  for(const width of [1920,1280,800,650]) {
   const result=spawnSync(browser,['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',`--window-size=${width},900`,`--user-data-dir=${path.join(directory,'profile')}`,'--dump-dom',pathToFileURL(file).href],{encoding:'utf8',timeout:20000,windowsHide:true,maxBuffer:2*1024*1024});
   const proof=result.stdout?.match(/<pre id="result">([^<]*)/)[1];
   assert.equal(result.status,0,result.stderr);assert.match(proof??'',/^PASS /);console.log(proof);
  }
 }finally {
  const absolute=path.resolve(directory);
  assert.equal(path.dirname(absolute),path.resolve(os.tmpdir()));assert.ok(path.basename(absolute).startsWith('Playhub-Store-Card-Geometry-'));
  rmSync(absolute,{recursive:true,force:true});
 }
});
