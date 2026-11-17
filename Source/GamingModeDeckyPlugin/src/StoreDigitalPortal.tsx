// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import {SP_REACT as React,routerHook} from "./decky";
import {StoreDigitalPage,StoreDigitalStoreIcon} from "./StoreDigitalPage";
import {StoreDigitalController,storeDigitalApi} from "./StoreDigitalState";
import {registerSteamMenu} from "./StoreDigitalMenuRegistry";
import {storeDigitalCopy} from "./StoreDigitalLocale";
import {STORE_DIGITAL_ROUTE,installStoreDigitalIntentPump} from "./StoreDigitalNavigation";
import {getSteamLocale,subscribeSteamLocale,startSteamLocaleObservation} from "./StoreDigitalSteamLocale";
export function StoreDigitalMenuIcon(){return <span style={{display:"flex",width:"1em",height:"1em"}}><StoreDigitalStoreIcon/></span>;}
export function installStoreDigitalPortal(locale:()=>string=()=>navigator.language){
 const controller=new StoreDigitalController(),stopLocale=startSteamLocaleObservation();let label=storeDigitalCopy(getSteamLocale()).title;
 const register=()=>registerSteamMenu({id:"store-digital",label,route:STORE_DIGITAL_ROUTE,icon:<StoreDigitalMenuIcon/>,order:15});
 let removeMenu=register();
 const Route=()=>{const language=React.useSyncExternalStore(subscribeSteamLocale,getSteamLocale);return <StoreDigitalPage controller={controller} locale={language}/>;};
 routerHook.addRoute(STORE_DIGITAL_ROUTE,Route,{exact:true});
 const stopIntent=installStoreDigitalIntentPump(storeDigitalApi.request);
 const timer=setInterval(()=>{const next=storeDigitalCopy(getSteamLocale()).title;if(next===label)return;label=next;const previous=removeMenu;removeMenu=register();previous();},5000);
 return()=>{clearInterval(timer);stopLocale();stopIntent();removeMenu();routerHook.removeRoute(STORE_DIGITAL_ROUTE);controller.dispose();};
}
