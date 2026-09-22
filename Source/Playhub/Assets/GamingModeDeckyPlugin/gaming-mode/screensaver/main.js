"use strict";
(() => {
 const canvas=document.getElementById('circles'),ctx=canvas.getContext('2d'),row=document.querySelector('.clock-row');
 let state={locale:navigator.language,color:'#FFCB0F',opacity:100},width=0,height=0,frame=0,stopped=false,last=0;
 const start=performance.now(),reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;
 function resize(){width=innerWidth;height=innerHeight;const scale=Math.min(devicePixelRatio||1,2);canvas.width=Math.round(width*scale);canvas.height=Math.round(height*scale);ctx.setTransform(scale,0,0,scale,0,0);}
 // Exact motion, falloff, radius and alpha equations from ReactiveCircleBackground.cs.
 function render(now){
  if(stopped)return;frame=requestAnimationFrame(render);if(now-last<1000/60)return;last=now;
  const seconds=reduced?0:(now-start)/1000*2,angle=seconds*Math.PI*2/20,breath=.5-.5*Math.cos(seconds*Math.PI*2/7);
  const cx=width*(.5+.27*Math.cos(angle)),cy=height*(.5+.28*Math.sin(angle)),spread=.9+.22*breath,pitch=24;
  ctx.clearRect(0,0,width,height);ctx.strokeStyle=state.color;ctx.lineWidth=1.1;
  for(let y=pitch/2;y<height;y+=pitch)for(let x=pitch/2;x<width;x+=pitch){
   const dx=(x-cx)/(width*.36*spread),dy=(y-cy)/(height*.55*spread),strength=Math.exp(-3*(dx*dx+dy*dy))*(.72+.28*breath),radius=.65+strength*(pitch*.43-.65);
   ctx.globalAlpha=(8+Math.floor(strength*127)*1.75)/255*(state.opacity/100);ctx.beginPath();ctx.arc(x,y,radius,0,Math.PI*2);ctx.stroke();
  }
  ctx.globalAlpha=1;
 }
 function tick(){
  const now=new Date(),locale=state.locale||navigator.language;
  document.getElementById('clock').textContent=state.clockText||now.toLocaleTimeString(locale,{hour:'2-digit',minute:'2-digit'});
  document.getElementById('date').textContent=state.dateText||new Intl.DateTimeFormat(locale,{weekday:'long',day:'numeric',month:'long'}).formatToParts(now).map(p=>['weekday','month'].includes(p.type)?p.value.charAt(0).toLocaleUpperCase(locale)+p.value.slice(1):p.value).join('');
  document.getElementById('weather-icon').textContent=state.weatherIcon||'';document.getElementById('weather-temp').textContent=state.weatherTemp||'';
  const h=state.header;
  if(h){for(const key of ['fontFamily','fontSize','fontWeight','lineHeight','letterSpacing'])if(h.style[key])row.style[key]=h.style[key];Object.assign(document.getElementById('date').style,h.dateStyle);Object.assign(document.getElementById('weather').style,h.weatherStyle);Object.assign(document.getElementById('weather-icon').style,h.iconStyle);}
  row.style.transform='scale('+Math.min(2.25,(innerWidth*.84)/Math.max(1,row.offsetWidth))+')';
 }
 window.__playhubCircles={update(value){state={...state,...value};tick();}};
 resize();tick();frame=requestAnimationFrame(render);const timer=setInterval(tick,1000);window.addEventListener('resize',resize);window.addEventListener('pagehide',()=>{stopped=true;cancelAnimationFrame(frame);clearInterval(timer);});
})();
