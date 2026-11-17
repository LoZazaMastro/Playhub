// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import {DFL,SP_REACT as React} from "./decky";
export type StoreDigitalNav={TakeFocus:(button?:number)=>boolean};
export const DigitalFocusable=(DFL as any).Focusable;
export const DigitalButton=(DFL as any).DialogButton;
const digitalModals=new Set<object>();
export function useDigitalModalGuard(){const owner=React.useRef({});React.useLayoutEffect(()=>{digitalModals.add(owner.current);return()=>{digitalModals.delete(owner.current);};},[]);}
export const digitalModalOpen=()=>digitalModals.size>0;
export function digitalTakeFocus(element:HTMLElement|null|undefined,nav:StoreDigitalNav|null|undefined,button?:number):boolean{if(!element?.isConnected||element.matches(":disabled,[aria-disabled=true]")||!nav)return false;return nav.TakeFocus(button);}
export function useDigitalTarget(){const element=React.useRef<HTMLElement|null>(null),nav=React.useRef<StoreDigitalNav|null>(null);return{ref:element,navRef:nav,takeFocus:()=>digitalTakeFocus(element.current,nav.current)};}
