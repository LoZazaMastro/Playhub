import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../screensaver/main.js',import.meta.url),'utf8');
test('Circles renders its moving background, scales the centered row and releases its timers',()=>{
 const events={},arcs=[],elements={};let frame,timer,cancelled=false,cleared=false;
 const ctx={setTransform(){},clearRect(){arcs.length=0},beginPath(){},arc(...args){arcs.push(args)},stroke(){}};
 const canvas={getContext:()=>ctx};const row={style:{},offsetWidth:400};
 const doc={getElementById(id){return id==='circles'?canvas:(elements[id]??={style:{}})},querySelector:()=>row};
 vm.runInNewContext(source,{document:doc,window:{addEventListener:(name,fn)=>events[name]=fn},navigator:{language:'it-IT'},innerWidth:1280,innerHeight:720,devicePixelRatio:3,performance:{now:()=>0},matchMedia:()=>({matches:false}),requestAnimationFrame:fn=>{frame=fn;return 1},cancelAnimationFrame:()=>cancelled=true,setInterval:fn=>{timer=fn;return 2},clearInterval:()=>cleared=true,Date,Intl,Math});
 assert.equal(canvas.width,2560);assert.equal(canvas.height,1440);frame(1000);assert.ok(arcs.length>1000);assert.ok(arcs.every(a=>a[2]>0&&Number.isFinite(a[2])));const first=JSON.stringify(arcs);frame(3000);assert.notEqual(JSON.stringify(arcs),first);assert.equal(row.style.transform,'scale(2.25)');
 timer();assert.match(elements.date.textContent,/^[A-ZÀ-Ü]/);events.pagehide();assert.equal(cancelled,true);assert.equal(cleared,true);
});
