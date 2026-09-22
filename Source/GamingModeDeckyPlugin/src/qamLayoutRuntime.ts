import type { ReactNode } from "react";
import { renderQamIcon } from "./qamIcons";

export interface QamLayoutPreferences {
  version: 3; selected: string[]; icons: Record<string, string>; order: string[]; hidden: string[]; updated_at: number;
}
export interface QamLayoutSnapshot { active: boolean; native: { key: string; label: string; icon?: ReactNode }[]; plugins: { name: string; icon?: ReactNode }[] }
type Tab = { key?: number | string; id?: number | string; [key: string]: any };
type Hook = { tabs: Tab[]; render(tabs: Tab[], visible: boolean): unknown };
let preferences: QamLayoutPreferences = { version: 3, selected: ["Playhub"], icons: {}, order: [], hidden: [], updated_at: 0 };
let snapshot: QamLayoutSnapshot = { active: false, native: [], plugins: [] };
const listeners = new Set<() => void>();
const refreshers = new Set<() => void>();
let pending = false;
function notify() { if (pending) return; pending = true; void Promise.resolve().then(() => { pending = false; listeners.forEach(fn => fn()); }); }
export const getQamLayoutPreferences = () => preferences;
export const getQamLayoutSnapshot = () => snapshot;
export function subscribeQamLayout(fn: () => void) { listeners.add(fn); return () => { listeners.delete(fn); }; }
export function setQamLayoutPreferences(value: QamLayoutPreferences) {
  const strings = (items: unknown) => Array.isArray(items) ? [...new Set(items.filter((v): v is string => typeof v === "string" && !!v))] : [];
  const next: QamLayoutPreferences = { version: 3, selected: strings(value?.selected), order: strings(value?.order),
    hidden: strings(value?.hidden).filter(key => key.startsWith("steam:")),
    icons: Object.fromEntries(Object.entries(value?.icons ?? {}).filter(([,v]) => typeof v === "string")),
    updated_at: Number.isFinite(value?.updated_at) ? value.updated_at : 0 };
  if (JSON.stringify(next) === JSON.stringify(preferences)) return;
  preferences = next; refreshers.forEach(fn => fn()); notify();
}
function icon(name: string, original: ReactNode) {
  return renderQamIcon(preferences.icons[name], original);
}
/** Projects preferences through the existing owner. Does not wrap or change Decky's registry. */
export function createQamLayoutRuntime(hook: Hook, nativeRender: Hook["render"], ownId: number, refresh: () => void,
  inventory: () => any = () => (window as any).DeckyPluginLoader?.deckyState?.publicState?.()) {
  const records = new Set<{ array: { deref(): Tab[] | undefined }; original: Tab[]; projected: Tab[] }>();
  const entries = new Map<string, { id: number; content: any; title: any; icon: any; tab?: Tab }>();
  let nextId = 0x504900, stopped = false;
  let managedVisibility = new Map<string, boolean>();
  let inventorySignature: any[] = [];
  const refreshInventory = () => {
    const state = inventory();
    const next = [...(state?.plugins ?? []).flatMap((p: any) => [p.name, p.titleView, p.title, p.content, p.icon]), ...(state?.disabledPlugins ?? []).map((p: any) => typeof p === "string" ? p : p.name)];
    if (next.length === inventorySignature.length && next.every((value, index) => value === inventorySignature[index])) return;
    inventorySignature = next; if (!stopped) refresh();
  };
  const keyOf = (tab: Tab) => String(tab.key) === String(ownId) ? "shortcut:Playhub"
    : [...entries].find(([,entry]) => String(entry.id) === String(tab.key))?.[0]
      ? `shortcut:${[...entries].find(([,entry]) => String(entry.id) === String(tab.key))![0]}`
      : String(tab.key) === "999" ? "decky:999" : `steam:${tab.key}`;
  const restore = (only?: Tab[]) => {
    for (const record of records) {
      const tabs = record.array.deref();
      if (!tabs) { records.delete(record); continue; }
      if (only && tabs !== only) continue;
      // Restore our order and hidden entries while preserving new native entries.
      const added = tabs.filter(tab => !record.projected.includes(tab) && !record.original.includes(tab));
      tabs.splice(0, tabs.length, ...record.original, ...added);
      records.delete(record);
    }
  };
  const changed = () => { if (!stopped) refresh(); };
  refreshers.add(changed);
  return { beforeRender: restore, refreshInventory, getManagedVisibility: () => managedVisibility,
    afterRender(tabs: Tab[], visible: boolean) {
      if (stopped) return;
      const state = inventory();
      const disabled = new Set((state?.disabledPlugins ?? []).map((p: any) => typeof p === "string" ? p : p.name));
      const plugins: any[] = (state?.plugins ?? []).filter((p: any) => p?.name && p.content != null && !disabled.has(p.name) && p.name.toLowerCase() !== "shortcuts");
      const native = tabs.filter(tab => String(tab.key) !== String(ownId)).map(tab => ({key:keyOf(tab), icon:tab.tab, label: typeof tab.strTitle === "string" && tab.strTitle ? tab.strTitle : typeof tab.title === "string" ? tab.title : String(tab.key) === "999" ? "Decky" : `Steam ${tab.key}`}));
      const available = plugins.map(p => ({ name: p.name, icon: p.icon }));
      if (!snapshot.active || native.length !== snapshot.native.length || native.some((tab, index) => tab.key !== snapshot.native[index]?.key || tab.label !== snapshot.native[index]?.label || tab.icon !== snapshot.native[index]?.icon) || available.length !== snapshot.plugins.length
        || available.some((p, i) => p.name !== snapshot.plugins[i]?.name || p.icon !== snapshot.plugins[i]?.icon)) {
        snapshot = {active:true,native,plugins:available}; notify();
      }
      const original = [...tabs];
      for (const name of preferences.selected) {
        if (name === "Playhub") continue;
        const plugin = plugins.find(p => p.name === name); if (!plugin) continue;
        let entry = entries.get(name);
        const chosenIcon = preferences.icons[name] || "";
        if (!entry) { while (tabs.some(tab => Number(tab.key) === nextId) || hook.tabs.some(tab => Number(tab.id) === nextId)) nextId++;
          entry = {id:nextId++,content:plugin.content,title:plugin.titleView ?? plugin.title ?? name,icon:chosenIcon}; entries.set(name,entry); }
        if (entry.content !== plugin.content || entry.title !== (plugin.titleView ?? plugin.title ?? name) || entry.icon !== chosenIcon) {entry.content=plugin.content;entry.title=plugin.titleView ?? plugin.title ?? name;entry.icon=chosenIcon;entry.tab=undefined;}
        const probe = Object.create(hook);
        Object.defineProperty(probe,"tabs",{value:[{id:entry.id,content:plugin.content,title:plugin.titleView ?? plugin.title ?? name,icon:icon(name,plugin.icon)}]});
        const created = entry.tab ? [entry.tab] : [];
        nativeRender.call(probe,created,visible);
        if (created.length === 1 && String(created[0].key) === String(entry.id) && created[0].decky === true) {entry.tab=created[0];tabs.push(created[0]);}
      }
      for (let i=tabs.length-1;i>=0;i--) if(preferences.hidden.includes(keyOf(tabs[i]))) tabs.splice(i,1);
      const rank = new Map(preferences.order.map((key,index)=>[key,index]));
      tabs.sort((a,b)=>(rank.get(keyOf(a)) ?? Number.MAX_SAFE_INTEGER)-(rank.get(keyOf(b)) ?? Number.MAX_SAFE_INTEGER));
      managedVisibility = new Map(original.filter(tab => String(tab.key) !== "999").map(tab => [String(tab.key), !preferences.hidden.includes(keyOf(tab))]));
      managedVisibility.set(String(ownId), tabs.some(tab => String(tab.key) === String(ownId)));
      for (const [name, entry] of entries) managedVisibility.set(String(entry.id), preferences.selected.includes(name) && tabs.some(tab => String(tab.key) === String(entry.id)));
      records.add({array:new (globalThis as any).WeakRef(tabs),original,projected:[...tabs]});
    },
    stop() {stopped=true;refreshers.delete(changed);restore();snapshot={...snapshot,active:false};notify();}
  };
}






