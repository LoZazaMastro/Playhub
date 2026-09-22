import fs from 'node:fs';
import path from 'node:path';
import {createRequire} from 'node:module';
import {fileURLToPath,pathToFileURL} from 'node:url';
const require=createRequire(import.meta.url);
const {chromium}=require('playwright');
const root=path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const catalog=JSON.parse(fs.readFileSync(path.join(root,'quick_settings/history_editorial.json')));
const manifest=JSON.parse(fs.readFileSync(path.join(root,'quick_settings/history_images.json')));
const source=fs.readFileSync(path.join(root,'src/DailyHistory.tsx'),'utf8');
const css=source.match(/const css=`([\s\S]*?)`;/)[1];
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const out=path.join(root,'work/creator-preview');fs.mkdirSync(out,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.PLAYHUB_CHROMIUM_PATH});
try {
 for(const id of (process.argv.slice(2).length ? process.argv.slice(2) : ['kondo','kojima','miyamoto'])) {
  const theme=catalog.themes.find(t=>t.id===id);
  const subject=(theme.subject??theme.topic).replace(/\s+\([^()]*\)\s*$/,'');
  const cards=[{title:theme.title,body:theme.intro,image_file:manifest[id][0].file,subject},...theme.chapters];
  const quoteData={
   mario:['Un salto può insegnare il linguaggio di un mondo.',2],
   zelda:['Una mappa comincia a vivere quando scegliamo una deviazione.',2],
   kondo:['Una melodia può continuare oltre il bordo dello schermo.',2],
   kojima:['Ogni sistema racconta qualcosa quando ci costringe a scegliere.',2],
   miyamoto:['Un mondo diventa memorabile quando impariamo a leggerlo giocando.',2],
  }[id];
  const quote=quoteData?`<aside tabindex="0" class="ph-history-quote"><p>${esc(quoteData[0])}</p></aside>`:'';
  const html=cards.map((c,i)=>{
   const media=manifest[id].find(m=>m.file===c.image_file);
   if(!media)throw Error(`${id} chapter ${i} lacks explicit media`);
   const url=pathToFileURL(path.join(root,'src/assets/history',media.file));
   const kicker=typeof c.kicker==='object'?c.kicker.it:c.kicker??c.subject;
   const card=`<article tabindex="0" class="ph-history-card ${i%2?'reverse':''}"><div class="ph-history-copy"><div class="ph-history-eyebrow">${i?String(i).padStart(2,'0')+' / ':''}${esc(kicker)}</div><h${i?3:2}>${esc(c.title.it)}</h${i?3:2}><p>${esc(c.body.it)}</p></div><div class="ph-history-image"><img class="ph-history-image-fg" src="${url}" alt="${esc(media.subject)}"></div></article>`;
   return card+(quoteData&&i===quoteData[1]?quote:'');
  }).join('');
  const preview=path.join(out,`${id}.html`);
  const anniversary=catalog.anniversaries.find(a=>a.theme_id===id&&['birth','release','foundation'].includes(a.kind));
  if(!anniversary)throw Error(`Missing birthday ${id}`);
  const dateLabel=new Intl.DateTimeFormat('it',{day:'numeric',month:'long'}).format(new Date(`2026-${anniversary.date}T12:00:00`));
  const occasion=anniversary.kind==='release'?'In questo giorno esce':anniversary.kind==='foundation'?'In questo giorno viene fondata':'In questo giorno nasce';
  fs.writeFileSync(preview,`<!doctype html><html lang="it"><meta charset="utf-8"><style>body{background:#141414;margin:0;padding:110px 32px 64px;font-family:Arial,sans-serif}*{box-sizing:border-box}${css}</style><main class="ph-history"><header class="ph-history-head"><div class="ph-history-date">${dateLabel}</div><div class="ph-history-sub"><span>${occasion}</span><strong>${esc(subject)}</strong><span>${anniversary.year}</span></div></header>${html}</main></html>`);
  const page=await browser.newPage({viewport:{width:1280,height:800}});
  await page.goto(pathToFileURL(preview).href);await page.locator('img').evaluateAll(imgs=>Promise.all(imgs.map(i=>i.decode())));
  const result=await page.evaluate(()=>({images:[...document.images].length,broken:[...document.images].filter(i=>!i.naturalWidth).length,overflow:document.documentElement.scrollWidth>innerWidth,cards:document.querySelectorAll('.ph-history-card').length}));
  if(result.broken||result.overflow)throw Error(JSON.stringify(result));
  await page.screenshot({path:path.join(out,`${id}.png`),fullPage:true});
  console.log(id,JSON.stringify(result));await page.close();
 }
} finally {await browser.close();}
