// Real Chromium cascade check. Own headless profile only; never attaches to Steam.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {spawn} from 'node:child_process';
import {loadDigitalSource} from './storeDigitalFixture.mjs';

test('native search and dialog input focus survives late Steam outline resets at both TV resolutions', {timeout:30000}, async()=>{
 const executable='C:/Program Files/Google/Chrome/Application/chrome.exe';
 assert.ok(fs.existsSync(executable),'Chromium is required for the actual CSS cascade regression');
 const directory=fs.mkdtempSync(path.join(os.tmpdir(),'Playhub-Digital-Input-Focus-'));
 const child=spawn(executable,['--headless=new','--disable-gpu','--disable-extensions','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--remote-debugging-address=127.0.0.1','--user-data-dir='+directory,'about:blank'],{windowsHide:true,stdio:'ignore'});
 const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));let socket;const pending=new Map();let serial=0;
 function call(method,params={}){const id=++serial;return new Promise((resolve,reject)=>{const timeout=setTimeout(()=>{pending.delete(id);reject(Error(method+' timed out'));},5000);pending.set(id,{resolve,reject,timeout});socket.send(JSON.stringify({id,method,params}));});}
 try{
  const activePort=path.join(directory,'DevToolsActivePort');for(let i=0;i<100&&!fs.existsSync(activePort);i++)await pause(50);assert.ok(fs.existsSync(activePort),'Own isolated browser started');
  const port=fs.readFileSync(activePort,'utf8').split('\n')[0],targets=await(await fetch('http://127.0.0.1:'+port+'/json/list')).json();
  const target=targets.find(value=>value.type==='page'&&value.url==='about:blank');assert.ok(target,'Own blank target');
  socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
  socket.addEventListener('message',event=>{const message=JSON.parse(event.data),entry=pending.get(message.id);if(!entry)return;pending.delete(message.id);clearTimeout(entry.timeout);message.error?entry.reject(Error(JSON.stringify(message.error))):entry.resolve(message.result);});
  const source=loadDigitalSource({decky:{SP_REACT:{}}})('StoreDigitalStyles').storeDigitalStyle;
  const fix='.ph-sd-root input:is(.gpfocus,.gpfocuswithin,:focus-visible){outline:2px solid var(--sd-accent)!important;outline-offset:4px!important}';
  assert.ok(source.includes(fix));const baseline=source.replace(fix,'');
  await call('Page.enable');await call('Emulation.setFocusEmulationEnabled',{enabled:true});
  const metrics=[];
  for(const [width,height]of [[1920,1080],[1280,720]])for(const surface of ['search','dialog'])for(const focus of ['gpfocus','gpfocuswithin','focus-visible']){
   await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
   for(const [version,css]of [['baseline',baseline],['fixed',source]]){
    const html=`<html><head><style>html,body{margin:0;background:#080809}input{outline:none}</style><style>${css}</style></head><body><div class="ph-sd-root ${surface==='dialog'?'ph-sd-modal':'ph-sd-fullscreen'}"><input id="native-input" class="DialogInput ${focus==='focus-visible'?'':focus}" aria-label="${surface}" style="outline-style:none"></div><style>.DialogInput.gpfocus,.DialogInput.gpfocuswithin,.DialogInput:focus-visible{outline:none;box-shadow:none}</style></body></html>`;
    const tree=await call('Page.getFrameTree');await call('Page.setDocumentContent',{frameId:tree.frameTree.frame.id,html});
    const result=await call('Runtime.evaluate',{expression:`(()=>{const input=document.getElementById('native-input');${focus==='focus-visible'?'input.focus();':''}const s=getComputedStyle(input);return{style:s.outlineStyle,width:s.outlineWidth,color:s.outlineColor,offset:s.outlineOffset};})()`,returnByValue:true});
    const value=result.result.value;
    if(version==='baseline')assert.equal(value.style,'none',`${surface}/${focus} must reproduce the missing focus ring before the fix`);
    else{assert.deepEqual(value,{style:'solid',width:'2px',color:'rgb(255, 204, 0)',offset:'4px'},`${surface}/${focus}`);metrics.push({width,height,surface,focus,...value});}
   }
  }
  const rowFix='.ph-sd-download:is(.gpfocus,.ph-sd-focused,:focus-visible){outline:2px solid var(--sd-accent)!important;outline-offset:4px!important}';assert.ok(source.includes(rowFix));
  for(const [width,height]of [[1920,1080],[1280,720]]){await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});for(const [version,css]of [['baseline',source.replace(rowFix,'')],['fixed',source]]){
   const html=`<html><head><style>${css}</style></head><body><div class="ph-sd-root"><div id="download-row" class="ph-sd-download gpfocus" style="outline-style:none">Static download status</div></div><style>div.gpfocus{outline:none}</style></body></html>`;const tree=await call('Page.getFrameTree');await call('Page.setDocumentContent',{frameId:tree.frameTree.frame.id,html});const result=await call('Runtime.evaluate',{expression:"(()=>{const s=getComputedStyle(document.getElementById('download-row'));return{style:s.outlineStyle,width:s.outlineWidth,color:s.outlineColor,offset:s.outlineOffset};})()",returnByValue:true});
   if(version==='baseline')assert.equal(result.result.value.style,'none');else{assert.deepEqual(result.result.value,{style:'solid',width:'2px',color:'rgb(255, 204, 0)',offset:'4px'});metrics.push({width,height,surface:'download-row',focus:'gpfocus',...result.result.value});}
  }}
  for(const [width,height]of [[1920,1080],[1280,720]]){await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});for(const version of ['baseline','fixed']){
   const html=`<html><head><style>${source}</style></head><body><div class="ph-sd-root"><div class="ph-sd-tabs"><button id="tab" class="DialogButton ${version==='fixed'?'ph-sd-button ':''}ph-sd-tab is-active gpfocus" style="border-radius:3px;outline-style:none">Your games</button></div><button id="refresh" class="DialogButton ph-sd-button gpfocus" style="border-radius:3px;outline-style:none">Refresh</button></div><style>.DialogButton.gpfocus{outline:none}</style></body></html>`;
   const tree=await call('Page.getFrameTree');await call('Page.setDocumentContent',{frameId:tree.frameTree.frame.id,html});const result=await call('Runtime.evaluate',{expression:"(()=>{const measure=id=>{const s=getComputedStyle(document.getElementById(id));return{style:s.outlineStyle,width:s.outlineWidth,color:s.outlineColor,offset:s.outlineOffset,radius:s.borderTopLeftRadius,border:s.borderTopWidth};};return{tab:measure('tab'),refresh:measure('refresh')};})()",returnByValue:true});
   if(version==='baseline')assert.notEqual(result.result.value.tab.radius,result.result.value.refresh.radius,'old native wrapper reproduces different outline corners');else{assert.deepEqual(result.result.value.tab,result.result.value.refresh,'navigation reuses the exact Refresh button skin');assert.equal(result.result.value.tab.radius,'999px');metrics.push({width,height,surface:'tab-pill',...result.result.value.tab});}
  }}
  fs.writeFileSync(path.join(directory,'review.json'),JSON.stringify(metrics,null,2));console.log(JSON.stringify({actualCascadeCases:metrics.length,baselineReproduced:true,completeYellowOutline:true,output:directory}));
 }finally{
  if(socket?.readyState===WebSocket.OPEN)await call('Browser.close').catch(()=>{});
  socket?.close();for(const item of pending.values())clearTimeout(item.timeout);if(child.exitCode===null)child.kill();
 }
});
