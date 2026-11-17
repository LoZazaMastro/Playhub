// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import {DFL} from "./decky";
import {type StoreDigitalIntent,type StoreKey,validStoreKey} from "./StoreDigitalModels";
export const STORE_DIGITAL_ROUTE="/playhub/stores";
export function exitStoreDigitalToHome(){const navigation=(DFL as any).Navigation;navigation?.CloseSideMenus?.();navigation?.Navigate?.("/library/home",true);}
export type StoreDigitalSelection={key:StoreKey;action:"install"|"manage";sequence:number};
let pending:StoreDigitalSelection|null=null,sequence=0;
let downloads:number|null=null;
const listeners=new Set<()=>void>();
export const readStoreDigitalSelection=()=>pending;
export const consumeStoreDigitalSelection=(value:StoreDigitalSelection)=>{if(pending===value)pending=null;};
export const subscribeStoreDigitalSelection=(fn:()=>void)=>{listeners.add(fn);return()=>{listeners.delete(fn);};};
export const readStoreDigitalDownloads=()=>downloads;
export const consumeStoreDigitalDownloads=(value:number)=>{if(downloads===value)downloads=null;};
export function openStoreDigitalDownloads(){pending=null;downloads=++sequence;for(const listener of listeners)listener();(DFL as any).Navigation.Navigate(STORE_DIGITAL_ROUTE);}
export function openStoreDigitalGame(key:StoreKey,action:"install"|"manage"="install"){if(!validStoreKey(key)||!["install","manage"].includes(action))return false;downloads=null;pending={key:{...key},action,sequence:++sequence};for(const listener of listeners)listener();(DFL as any).Navigation.Navigate(`${STORE_DIGITAL_ROUTE}?store=${encodeURIComponent(key.store)}&game=${encodeURIComponent(key.id)}&action=${action}`);return true;}
export function validStoreDigitalIntent(value:unknown,now=Date.now()):value is StoreDigitalIntent{const item=value as StoreDigitalIntent;return !!item&&typeof item.requestId==="string"&&/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(item.requestId)&&validStoreKey(item.key)&&["install","manage"].includes(item.action)&&Number.isFinite(Date.parse(item.expiresAt))&&Date.parse(item.expiresAt)>now&&Date.parse(item.expiresAt)<=now+31000;}
export function installStoreDigitalIntentPump(request:(method:string,args?:Record<string,unknown>)=>Promise<unknown>,open= openStoreDigitalGame,clock=()=>Date.now()){
 let alive=true,busy=false;const seen=new Set<string>();const poll=async()=>{if(!alive||busy)return;busy=true;try{const result=await request("ui-install-take",{}) as {intent?:unknown};if(!alive||!validStoreDigitalIntent(result?.intent,clock())||seen.has(result.intent.requestId))return;seen.add(result.intent.requestId);if(seen.size>128)seen.delete(seen.values().next().value!);open(result.intent.key,result.intent.action);}catch{}finally{busy=false;}};void poll();const timer=setInterval(()=>void poll(),2000);return()=>{alive=false;clearInterval(timer);};
}
