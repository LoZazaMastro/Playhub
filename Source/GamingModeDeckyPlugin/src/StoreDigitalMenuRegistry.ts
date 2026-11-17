// SPDX-License-Identifier: BSD-3-Clause
// Copyright (c) LoZazaMastro, Necrosiak, Steam Deck Homebrew.
// Shared menu contract v1; full notice: third-party/steam-menu/LICENSE.
import {SP_REACT} from "./decky";
const {createElement,useSyncExternalStore}=SP_REACT;
import { findInReactTree, getReactRoot } from "@decky/ui";

type Entry = { id: string; label: string; route: string; icon: any; order: number; onSecondary?: () => void };
type State = { entries: Map<string, Entry>; dispose?: () => void; timer?: any; revision?: number;
  implementation?: number; current?: () => boolean; sequence?: number; listeners?: Set<() => void>; unobserveEntries?: () => void };
const REVISION = 3; // Registry/cleanup ABI remains compatible with installed Discord v3.
const IMPLEMENTATION = 4;
const KEY = "__necroSteamMenuV1";
const state = (): State => ((window as any)[KEY] ||= { entries: new Map() });
const ownedItem = (item: any) => String(item?.key || "").startsWith("necro-menu:")
  || (item?.key === "steamcord" && item?.props?.route === "/discord");
const isItem = (item: any) => item?.props?.label && (item.props.onFocus || item.props.onGamepadFocus) && item.props.route;

function surface() {
  const root = document.getElementById("root");
  if (!root) return null;
  const stored: any = getReactRoot(root);
  const current = stored?.stateNode?.current || stored;
  const node: any = findInReactTree(current, (item: any) => item?.memoizedProps?.navID === "MainNavMenuContainer");
  const parent = node?.return;
  if (typeof parent?.type !== "function" || typeof parent?.elementType !== "function") return null;
  // A fiber's .type is disposable: React can reconstruct it on the next render.
  // The native memo component is the stable owner used again for future fibers.
  for (let memo = parent.return; memo; memo = memo.return) {
    const owner = memo.elementType;
    if (owner?.$$typeof === Symbol.for("react.memo") && typeof owner.type === "function")
      return { parent, memo, owner };
  }
  return null;
}

// Copy only the changed element/portal branch. No mutation of frozen React props,
// DOM insertion, focus manipulation, or unrelated native menu items.
function replace(tree: any, predicate: (item: any) => boolean, change: (item: any) => any): any {
  if (!tree || typeof tree !== "object") return tree;
  if (predicate(tree)) return change(tree);
  if (Array.isArray(tree)) {
    const values = tree.map(item => replace(item, predicate, change));
    return values.some((item, index) => item !== tree[index]) ? values : tree;
  }
  if (tree.props && "children" in tree.props) {
    const children = replace(tree.props.children, predicate, change);
    return children === tree.props.children ? tree : { ...tree, props: { ...tree.props, children } };
  }
  if ("children" in tree) {
    const children = replace(tree.children, predicate, change);
    return children === tree.children ? tree : { ...tree, children };
  }
  return tree;
}

function install(s: State): boolean {
  const found = surface();
  if (!found) return false;
  const { parent, memo, owner } = found;
  const nativeType = parent.elementType, renderNative = parent.type, original = owner.type;
  const menus = new WeakMap<Function, Function>();
  const subscribe = (listener: () => void) => { s.listeners!.add(listener); return () => { s.listeners!.delete(listener); }; };
  const snapshot = () => s.sequence || 0;
  const menuWrapper = (native: Function) => {
    let wrapped = menus.get(native);
    if (wrapped) return wrapped;
    wrapped = function(this: any, ...args: any[]) {
      useSyncExternalStore(subscribe, snapshot, snapshot);
      const result = native.apply(this, args);
      return replace(result, value => Array.isArray(value) && value.some(isItem), items => {
        const clean = items.filter((item: any) => !ownedItem(item));
        const template = clean.find(isItem);
        if (!template) return items;
        const entries = [...s.entries.values()].sort((a, b) => a.order - b.order || a.id.localeCompare(b.id));
        let position = 3;
        try { position = Number(localStorage.getItem("STEAMCORD_MENU_POSITION") || 3); } catch {}
        if (!Number.isFinite(position) || position < 1 || position > 9) position = 3;
        const indexes = clean.flatMap((item: any, index: number) => isItem(item) ? [index] : []);
        const index = position === 9 ? indexes[indexes.length - 1] + 1 : (indexes[position - 1] ?? indexes[indexes.length - 1] + 1);
        clean.splice(index, 0, ...entries.map(entry => createElement(template.type, {
          key: "necro-menu:" + entry.id, route: entry.route, label: entry.label,
          ...(template.props.onGamepadFocus ? { onGamepadFocus: template.props.onGamepadFocus } : { onFocus: template.props.onFocus }),
          ...(entry.onSecondary ? { onSecondaryActionDescription: "Change Position", onSecondaryButton: entry.onSecondary } : {}),
          ...(template.props.icon ? { icon: entry.icon } : { children: entry.icon }),
        })));
        return clean;
      });
    };
    menus.set(native, wrapped);
    return wrapped;
  };
  const renderMenu = function(this: any, ...args: any[]) {
    const output = renderNative.apply(this, args);
    const element = output?.props?.children?.props?.children?.[0];
    if (typeof element?.type !== "function") return output;
    return replace(output, item => item === element, item => ({ ...item, type: menuWrapper(element.type) }));
  };
  const wrapper = function(this: any, ...args: any[]) {
    const output = original.apply(this, args);
    return replace(output, item => item?.type === nativeType, item => ({ ...item, type: renderMenu }));
  };
  owner.type = wrapper;
  if (memo.type === original) memo.type = wrapper;
  if (memo.alternate?.type === original) memo.alternate.type = wrapper;
  s.current = () => { const active = surface(); return active?.owner === owner && owner.type === wrapper; };
  s.dispose = () => {
    const active = surface();
    if (active && active.owner === owner) {
      if (active.memo.type === wrapper) active.memo.type = original;
      if (active.memo.alternate?.type === wrapper) active.memo.alternate.type = original;
    }
    if (owner.type === wrapper) owner.type = original;
    if (memo.type === wrapper) memo.type = original;
    if (memo.alternate?.type === wrapper) memo.alternate.type = original;
    s.current = undefined;
    if(!s.entries.size)s.unobserveEntries?.();
  };
  return true;
}

function changed(s: State) { s.sequence = (s.sequence || 0) + 1; for (const listener of [...s.listeners!]) listener(); }
function observeEntries(s: State) {
  if(s.unobserveEntries)return;
  const entries=s.entries, set=entries.set, remove=entries.delete;
  const setDescriptor=Object.getOwnPropertyDescriptor(entries,"set"), removeDescriptor=Object.getOwnPropertyDescriptor(entries,"delete");
  // Observe this one shared registry, not Map.prototype. Legacy Discord v3
  // writes the same map and otherwise cannot notify an already mounted menu.
  const write=function(this:Map<string,Entry>,key:string,value:Entry){const previous=this.get(key);const result=set.call(this,key,value);if(this===entries&&previous!==value)changed(s);return result;};
  const erase=function(this:Map<string,Entry>,key:string){const result=remove.call(this,key);if(this===entries&&result){changed(s);if(!entries.size)queueMicrotask(()=>{if(!entries.size&&!s.dispose&&!s.timer)s.unobserveEntries?.();});}return result;};
  Object.defineProperty(entries,"set",{value:write,configurable:true,writable:true});
  Object.defineProperty(entries,"delete",{value:erase,configurable:true,writable:true});
  s.unobserveEntries=()=>{
    if(entries.set===write){if(setDescriptor)Object.defineProperty(entries,"set",setDescriptor);else delete (entries as any).set;}
    if(entries.delete===erase){if(removeDescriptor)Object.defineProperty(entries,"delete",removeDescriptor);else delete (entries as any).delete;}
    s.unobserveEntries=undefined;
  };
}
export function registerSteamMenu(entry: Entry): () => void {
  const s = state();
  s.listeners ||= new Set();
  observeEntries(s);
  if (s.revision !== REVISION || s.implementation !== IMPLEMENTATION || s.current?.() === false) {
    clearInterval(s.timer); s.timer = undefined;
    s.dispose?.(); s.dispose = undefined;
    s.revision = REVISION; s.implementation = IMPLEMENTATION;
  }
  s.entries.set(entry.id, entry);
  if (!s.dispose && !s.timer) {
    let attempts = 0;
    const attempt = () => {
      try { if (install(s)) { clearInterval(s.timer); s.timer = undefined; return; } }
      catch (error) { console.debug("[Steam menu] integration unavailable", error); }
      if (++attempts >= 30) { clearInterval(s.timer); s.timer = undefined; }
    };
    s.timer = setInterval(attempt, 2000); attempt();
  }
  return () => {
    if (s.entries.get(entry.id) !== entry) return;
    s.entries.delete(entry.id);
    if (s.entries.size) return;
    clearInterval(s.timer); s.timer = undefined;
    s.dispose?.(); s.dispose = undefined;
    s.unobserveEntries?.();
  };
}
