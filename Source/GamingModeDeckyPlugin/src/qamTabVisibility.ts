import { DFL } from "./decky";
import { findOnboardingBrowserViewDocument, invalidateOnboardingBrowserViewOwners } from './onboardingAnchors';

const registryHost=globalThis as any;
const registryKey=Symbol.for('playhub.qam.documents.v1');
const documentRegistry=registryHost[registryKey]??(registryHost[registryKey]={documents:new Map(),listeners:new Set()});
const mountedDocuments:Map<Document,number> = documentRegistry.documents;
const documentListeners:Set<()=>void> = documentRegistry.listeners;
export function registerQamVisibilityDocument(doc:Document) {
  mountedDocuments.set(doc,(mountedDocuments.get(doc)??0)+1);
  documentListeners.forEach(listener=>listener());
  return ()=>{const count=(mountedDocuments.get(doc)??1)-1;if(count)mountedDocuments.set(doc,count);else mountedDocuments.delete(doc);documentListeners.forEach(listener=>listener());};
}

/** Overrides position-based theme visibility only for stable IDs owned by the QAM layout. */
export function createQamTabVisibility(documents?: () => Document[]) {
  let desired = new Map<string, boolean>();
  let stopped = false;
  let lastDiscovery = 0;
  let knownDocuments: Document[] = [];
  const watched = new Map<Document, MutationObserver>();
  const saved = new Map<HTMLElement, {value:string;priority:string;written:string}>();
  const discover = documents ?? (() => {
    const result:Document[]=[...mountedDocuments.keys()];
    const host=typeof window==='undefined'?undefined:window as any;
    if (!host) return result;
    if(host.document)result.push(host.document);
    const store=(DFL as any).Router?.WindowStore;
    for(const win of [store?.GamepadUIMainWindowInstance,...(store?.SteamUIWindows??[]),...(store?.OverlayWindows??[])]){
      const doc=(win?.BrowserWindow??win?.m_BrowserWindow)?.document;
      if(!doc)continue;
      if(!result.includes(doc))result.push(doc);
      // Popup BrowserViews belong to React owners, not SteamUIWindows.
      invalidateOnboardingBrowserViewOwners(doc);
      for(const id of desired.keys()){
        const popup=findOnboardingBrowserViewDocument(doc,Number(id));
        if(popup&&!result.includes(popup))result.push(popup);
        if(popup)break;
      }
    }
    // SharedJSContext owns popup BrowserViews; the main window may be only a shell.
    if(host.document){
      invalidateOnboardingBrowserViewOwners(host.document);
      for(const id of desired.keys()){
        const popup=findOnboardingBrowserViewDocument(host.document,Number(id));
        if(popup&&!result.includes(popup))result.push(popup);
        if(popup)break;
      }
    }
    return result;
  });
  const restore = (node:HTMLElement,entry:{value:string;priority:string;written:string}) => {
    if(node.style.getPropertyValue('display')===entry.written&&node.style.getPropertyPriority('display')==='important'){
      if(entry.value)node.style.setProperty('display',entry.value,entry.priority);else node.style.removeProperty('display');
    }
    saved.delete(node);
  };
  const apply = (doc:Document) => {
    for(const [node,entry] of saved)if(!node.isConnected||!desired.has(node.id.slice('quickaccess_tab_'.length)))restore(node,entry);
    for(const [id,shown] of desired){
      if(!/^\d+$/.test(id)||id==='999')continue;
      const node=doc.getElementById(`quickaccess_tab_${id}`) as HTMLElement|null;
      if(!node?.style)continue;
      const value=shown?'flex':'none';
      let entry=saved.get(node);
      if(!entry){entry={value:node.style.getPropertyValue('display'),priority:node.style.getPropertyPriority('display'),written:value};saved.set(node,entry);}
      else if(node.style.getPropertyValue('display')!==entry.written||node.style.getPropertyPriority('display')!=='important'){
        entry.value=node.style.getPropertyValue('display');entry.priority=node.style.getPropertyPriority('display');
      }
      entry.written=value;
      if(node.style.getPropertyValue('display')!==value||node.style.getPropertyPriority('display')!=='important')node.style.setProperty('display',value,'important');
    }
  };
  const reconcile=()=>{
    if(stopped)return;
    if (!knownDocuments.length || Date.now() - lastDiscovery >= 500) {
      try { knownDocuments = discover(); lastDiscovery = Date.now(); } catch { knownDocuments=[...mountedDocuments.keys()]; }
    }
    const docs = [...new Set([...mountedDocuments.keys(),...knownDocuments])];
    for(const [doc,observer] of watched)if(!docs.includes(doc)){observer.disconnect();watched.delete(doc);}
    for(const doc of docs){
      apply(doc);
      const Observer=doc.defaultView?.MutationObserver;
      if(!watched.has(doc)&&Observer&&doc.documentElement){const observer=new Observer(()=>{if(!stopped)apply(doc);});observer.observe(doc.documentElement,{subtree:true,childList:true,attributes:true,attributeFilter:['id','style']});watched.set(doc,observer);}
    }
  };
  const documentChanged=()=>{lastDiscovery=0;reconcile();};
  documentListeners.add(documentChanged);
  const timer=typeof window==='undefined'?undefined:window.setInterval(reconcile,500);
  return {update(value:ReadonlyMap<string,boolean>){desired=new Map(value);reconcile();},reconcile,
    stop(){stopped=true;documentListeners.delete(documentChanged);if(timer!==undefined)window.clearInterval(timer);for(const observer of watched.values())observer.disconnect();watched.clear();for(const [node,entry] of saved)restore(node,entry);}
  };
}


