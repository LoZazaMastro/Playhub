import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {createRequire} from 'node:module';
const require=createRequire(import.meta.url);
const source=fs.readFileSync(new URL('../src/HomeNews.tsx',import.meta.url),'utf8');
const css=source.match(/const css = `([\s\S]*?)`;/)[1];
test('news titles keep four whole lines and equal cards with broken artwork', {skip:process.env.PLAYHUB_BROWSER_QA!=='1'},async()=>{
 const {chromium}=require('playwright');
 const browser=await chromium.launch({executablePath:process.env.PLAYHUB_CHROMIUM_PATH,headless:true});
 try{
  const page=await browser.newPage();
  await page.setContent(`<style>.native-title{max-height:52px;overflow:hidden}body{font-family:Arial}.ph-news-card{width:300px;height:335px;display:inline-block;vertical-align:top}.label{height:22px}${css}</style>`+[false,true].map(logo=>`<div class="ph-news-card"><div class="label">Publisher</div><div class="ph-news-content" tabindex="0"><div class="ph-news-cover" data-logo="${logo}" style="height:169px;min-height:169px;flex-shrink:0"><img class="ph-news-image" src="data:," /></div><div class="ph-news-info"><div class="ph-news-date">9 settembre</div><div class="native-title ph-news-title">First complete line<br>Second complete line<br>Third complete line<br>Fourth complete line</div></div></div></div>`).join(''));
  await page.locator('.ph-news-content').first().focus();
  const boxes=await page.locator('.ph-news-card').evaluateAll(cards=>cards.map(card=>{const title=card.querySelector('.ph-news-title');return {card:card.getBoundingClientRect().height,title:title.getBoundingClientRect().height,line:parseFloat(getComputedStyle(title).lineHeight),fits:title.getBoundingClientRect().bottom<=card.getBoundingClientRect().bottom};}));
  assert.deepEqual(boxes.map(b=>b.card),[335,335]);
  for(const box of boxes){assert.equal(box.title,4*box.line);assert.ok(box.fits);}
 }finally{await browser.close();}
});
