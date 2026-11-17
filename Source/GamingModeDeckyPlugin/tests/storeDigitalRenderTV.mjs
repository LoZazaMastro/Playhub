// Isolated headless visual test; never attaches to Steam or a user browser.
import fs from 'node:fs';import path from 'node:path';import {spawn} from 'node:child_process';
const out='C:/Users/Andrea/AppData/Local/Temp/Playhub-DigitalStores-TV-20261115';
const chrome=spawn('C:/Program Files/Google/Chrome/Application/chrome.exe',['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--user-data-dir=C:/Users/Andrea/AppData/Local/Temp/DigitalStores-CDP-Headless-TV-20261115','--remote-debugging-pipe','--allow-file-access-from-files'],{stdio:['ignore','ignore','ignore','pipe','pipe'],windowsHide:true});
let serial=0,buffer='';const pending=new Map();
chrome.stdio[4].on('data',data=>{buffer+=data.toString();for(;;){const index=buffer.indexOf('\0');if(index<0)break;const packet=buffer.slice(0,index);buffer=buffer.slice(index+1);if(!packet)continue;const message=JSON.parse(packet);const held=pending.get(message.id);if(held){pending.delete(message.id);clearTimeout(held.timer);message.error?held.reject(Error(JSON.stringify(message.error))):held.resolve(message.result);}}});
function call(method,params={},sessionId){const id=++serial;return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{pending.delete(id);reject(Error('CDP timeout '+method));},10000);pending.set(id,{resolve,reject,timer});chrome.stdio[3].write(JSON.stringify({id,method,params,...(sessionId?{sessionId}:{})})+'\0');});}
const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));const results=[];
try{
 const {targetId}=await call('Target.createTarget',{url:'about:blank'}),{sessionId}=await call('Target.attachToTarget',{targetId,flatten:true});
 await call('Page.enable',{},sessionId);
 for(const width of [1280,1920]){
  const height=width===1280?720:1080;
  await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false},sessionId);
  for(const mode of ['library','library-focus','accounts','downloads','detail','filters','gamepass']){
   const fixture=mode.replace('-focus','');await call('Page.navigate',{url:'file:///'+out+'/'+fixture+'.html'},sessionId);await pause(200);
   const result=await call('Runtime.evaluate',{expression:`(()=>{
    const focused=${JSON.stringify(mode)}.endsWith('-focus')?document.querySelector('.ph-sd-game'):null;
    if(focused){focused.classList.add('ph-sd-focused');focused.scrollIntoView({block:'center',inline:'nearest'});}
    const select=s=>[...document.querySelectorAll(s)],rect=e=>{const r=e.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height,right:r.right,bottom:r.bottom};};
    const overflow=select('.ph-sd-page,.ph-sd-header,.ph-sd-toolbar,.ph-sd-account,.ph-sd-modal,.ph-sd-actions').filter(e=>e.scrollWidth>e.clientWidth+1).map(e=>e.className);
    const root=document.querySelector('.ph-sd-root'),input=document.querySelector('input:not([type=checkbox])');
    return{mode:${JSON.stringify(mode)},viewport:{width:innerWidth,height:innerHeight},overflow,root:rect(root),buttons:select('button').map(rect),focus:focused?rect(focused):null,input:input?{color:getComputedStyle(input).color,fill:getComputedStyle(input).webkitTextFillColor}:null,scrollbarWidth:getComputedStyle(root).scrollbarWidth};
   })()`,returnByValue:true},sessionId);
   const data=result.result.value;results.push(data);if(data.viewport.width!==width||data.viewport.height!==height||data.overflow.length)throw Error('bad viewport/overflow '+JSON.stringify(data));
   const screen=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false,fromSurface:true},sessionId);fs.writeFileSync(path.join(out,mode+'-'+width+'.png'),Buffer.from(screen.data,'base64'));
  }
 }
 fs.writeFileSync(path.join(out,'layout-measurements.json'),JSON.stringify(results,null,2));console.log(JSON.stringify({screens:results.length,viewports:[{width:1280,height:720},{width:1920,height:1080}],horizontalOverflow:results.filter(result=>result.overflow.length).length,minButtonHeight:Math.min(...results.flatMap(result=>result.buttons.map(button=>button.height))),neutralInputs:results.filter(result=>result.input).every(result=>result.input.color==='rgb(245, 245, 245)'),output:out}));
}finally{try{await call('Browser.close');}catch{}chrome.kill();}
