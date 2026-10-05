import fs from 'node:fs';
import http from 'node:http';
import {createRequire} from 'node:module';
import ts from 'typescript';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYHUB_PLAYWRIGHT_PATH || 'playwright');
const root=new URL('../',import.meta.url);
const manifest=JSON.parse(fs.readFileSync(new URL('../../assets/on-this-day/v1/manifest.json',root)));
const config=JSON.parse(fs.readFileSync(new URL('quick_settings/history_streaming.json',root)));
const file=Object.keys(manifest.files).find(name=>name.endsWith('.jpg'));
const url=`https://raw.githubusercontent.com/LoZazaMastro/Playhub/${config.commit}/assets/on-this-day/v1/${file}`;
const source=ts.transpileModule(fs.readFileSync(new URL('src/historyPhotoCache.ts',root),'utf8'),{
 compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}
}).outputText;
const server=http.createServer((req,res)=>{res.writeHead(200,{'content-type':'text/html'});res.end('<!doctype html><title>Playhub photo cache verification</title>');});
await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
const browser=await chromium.launch({headless:true,executablePath:process.env.PLAYHUB_CHROMIUM_PATH});
const results={url,browser:browser.version(),checks:[]};
try{
 const context=await browser.newContext();const page=await context.newPage();
 const local=`http://127.0.0.1:${server.address().port}`;
 const load=async()=>{await page.addScriptTag({content:'var exports={};'+source});return page.evaluate(async({url,expected})=>{
  const blob=await exports.loadHistoryPhoto(url,expected);if(!blob)return null;
  const objectUrl=URL.createObjectURL(blob);const image=new Image();image.src=objectUrl;await image.decode();URL.revokeObjectURL(objectUrl);
  return {bytes:blob.size,width:image.naturalWidth,height:image.naturalHeight};
 },{url,expected:manifest.files[file]});};
 await page.goto(local);const online=await load();
 if(!online?.width)throw Error('Live photo failed to load/decode');
 results.checks.push({name:'live GitHub verified photo decodes',result:online});
 await page.waitForFunction(async url=>Boolean(await(await caches.open('playhub-history-photos-v1')).match(url)),url);
 await context.route('https://raw.githubusercontent.com/**',route=>route.abort('internetdisconnected'));
 await page.reload();const offline=await load();
 if(offline?.width!==online.width)throw Error('Persisted cache not used after runtime reload offline');
 results.checks.push({name:'new runtime offline uses persistent verified cache',result:offline});
 await context.close();
}finally{await browser.close();server.close();}
const output=new URL('../../../docs/validation/history-streaming-browser-20260928.json',import.meta.url);
fs.mkdirSync(new URL('./',output),{recursive:true});fs.writeFileSync(output,JSON.stringify(results,null,2));
console.log(JSON.stringify(results,null,2));
