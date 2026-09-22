import { DFL } from "./decky";
import { call } from "./controlBackend";
const MODE="Playhub Circles";
function documents():Document[]{
 const docs=[document];
 const add=(value:any)=>{try{if(value?.document&&!docs.includes(value.document))docs.push(value.document);}catch{}};
 const store=(DFL as any).Router?.WindowStore;
 for(const item of [store?.GamepadUIMainWindowInstance,...(store?.SteamUIWindows||[])]){
  add(item);add(item?.window);add(item?.m_Window);add(item?.BrowserWindow);add(item?.m_BrowserWindow);
 }
 return docs;
}
export function installCirclesScreensaver(locale:()=>string){
 const ui=DFL as any;
 const service=ui.findModule((m:any)=>typeof m?.b3?.GetLocalScreensavers==="function")?.b3;
 const settings=ui.findModule((m:any)=>m?.rV?.clientSettings&&typeof m?.qt==="function");
 let disposed=false,busy=false,restore=()=>{};
 async function register(){
  const result=await call<[],{ok:boolean}>("install_circles_screensaver");if(disposed||!result.ok||!service)return;
          try{
              const response=await fetch("https://steamloopback.host/custom_fonts/clientui.uifont?MotivaSans-Medium");
              if(response.ok){const bytes=new Uint8Array(await response.arrayBuffer());let binary="";for(let i=0;i<bytes.length;i+=8192)binary+=String.fromCharCode(...bytes.subarray(i,i+8192));await call.bind(null,"cache_circles_font")(btoa(binary));}
          }catch(error){console.debug("Screensaver font",error);}

  let client:any;ui.findModule((module:any)=>{try{client=Object.values(module||{}).find((v:any)=>typeof v?.getQueryCache==="function"&&typeof v?.setQueryData==="function");}catch{}return Boolean(client);});
  if(!client)return;
  const key=["settings","getscreensavers"];
  const rename=(from:string,to:string)=>{const data=client.getQueryData(key);if(Array.isArray(data)&&data.some((entry:any)=>entry.strID===from))client.setQueryData(key,data.map((entry:any)=>entry.strID===from?{...entry,strID:to}:entry));};
  const decorate=()=>{
   const data=client.getQueryData(key);
   if(Array.isArray(data)&&data.some((entry:any)=>entry.strID==="uioverride-playhub-circles"))rename("uioverride-playhub-circles",MODE);
   else if(!Array.isArray(data)||!data.some((entry:any)=>entry.strID===MODE))client.setQueryData(key,[...(Array.isArray(data)?data:[{strID:"steam-gameslideshow",strURL:"steam-gameslideshow.steamscreensavers.host"},{strID:"steam-bouncinglogo",strURL:"steam-bouncinglogo.steamscreensavers.host"}]),{strID:MODE,strURL:"uioverride-playhub-circles.steamscreensavers.host"}]);
  };
  const unsubscribe=client.getQueryCache().subscribe((event:any)=>{if(JSON.stringify(event?.query?.queryKey)===JSON.stringify(key))decorate();});
  restore=()=>{unsubscribe();rename(MODE,"uioverride-playhub-circles");};
  decorate();void client.invalidateQueries({queryKey:key});
 }
 async function tick(){
  if(disposed||busy||settings?.rV.clientSettings.screensaver_current_id!==MODE)return;
  busy=true;
  try{
   if(!(await service.GetActiveState({})).Body().active())return;
   let weather:any;
   for(const doc of documents()){weather=(doc.defaultView as any)?.__deckyWeatherTopbarState;if(weather)break;}
            let header:any,clockText:string|undefined,dateText:string|undefined;
            for(const doc of documents()){
                const clock=doc.querySelector<HTMLElement>('#header [data-decky-weather-clock], #header [data-playhub-clock-left], #header ._1HhLUvHH6BZLIOyOE80TVh');
                if(!clock)continue;
                const style=doc.defaultView!.getComputedStyle(clock),rect=clock.getBoundingClientRect();
                const copy=(element:Element|null|undefined,keys:string[])=>element?Object.fromEntries(keys.map(key=>[key,doc.defaultView!.getComputedStyle(element)[key as any]])):{};
                const date=doc.getElementById('playhub-topbar-date'),badge=doc.getElementById('decky-weather-topbar-badge');
                header={style:{fontFamily:style.fontFamily,fontSize:style.fontSize,fontWeight:style.fontWeight,lineHeight:style.lineHeight,letterSpacing:style.letterSpacing,padding:'0px',top:'4vh',left:'4vw'},dateStyle:copy(date,['marginLeft','marginRight','fontSize','fontWeight','lineHeight']),weatherStyle:copy(badge,['marginLeft','gap','fontSize','fontWeight','lineHeight']),iconStyle:copy(badge?.querySelector('.decky-weather-topbar-icon'),['fontSize','width','height','transform'])};
                clockText=[...clock.childNodes].filter(node=>node.nodeType===3).map(node=>node.textContent).join('').trim();
                dateText=date?.textContent?.replace(/(^|\s)(\p{L})/gu,(_,space,letter)=>space+letter.toLocaleUpperCase());break;
            }

   await call("sync_circles_screensaver",{locale:locale(),header,clockText,dateText,weatherIcon:weather?.enabled?weather.iconText:"",weatherTemp:weather?.enabled?weather.tempText:""});
  }catch(error){console.debug("Playhub Circles",error);}finally{busy=false;}
 }
 void register().catch(error=>console.warn("Playhub Circles registration",error));
 const timer=window.setInterval(tick,1500);
 return()=>{disposed=true;window.clearInterval(timer);restore();};
}
