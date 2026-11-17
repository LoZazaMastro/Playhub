// SPDX-License-Identifier: MIT. Native metadata only; no shortcut mutation or title matching.
import {type ShortcutReadback,shortcutDirectory} from "./storeDownloadActivity";
export type NativeStoreIdentity={libraryKind?:string|null;key:{store:"epic"|"gog"|"xbox"|"amazon";id:string};accountRevision:number;nativeApplicationId?:string|null;manifestExecutable?:string|null;installPath:string;runtimeExecutable:string;startDirectory:string;gameExecutablePath?:string|null;epicNamespace?:string|null;epicCatalogItemId?:string|null};
export type NativeImportCandidate={appId:number;title:string;proof:ShortcutReadback};
const canonical=(value:unknown)=>typeof value==="string"?value.replace(/^"|"$/g,"").replace(/\//g,"\\").toLowerCase():"";
export function windowsArguments(value:string):string[]|null{
 if(typeof value!=="string"||value.length>32768||/[\0\r\n]/.test(value))return null;
 const result:string[]=[];let index=0;
 while(index<value.length){while(/\s/.test(value[index]??"")&&index<value.length)index++;if(index===value.length)break;let token="",quoted=false;
  while(index<value.length&&(quoted||!(/\s/.test(value[index])))){let slashes=0;while(value[index]==="\\"){slashes++;index++;}if(value[index]==='"'){token+="\\".repeat(Math.floor(slashes/2));if(slashes%2)token+='"';else quoted=!quoted;index++;}else{token+="\\".repeat(slashes);if(index<value.length&&(quoted||!(/\s/.test(value[index]))))token+=value[index++];}}
  if(quoted)return null;result.push(token);if(result.length>64)return null;
 }return result;
}
export function nativeUwpArguments(identity:NativeStoreIdentity){const quote=(value:string)=>{let result='"',slashes=0;for(const character of value){if(character==="\\"){slashes++;continue;}result+="\\".repeat(character==='"'?slashes*2+1:slashes)+character;slashes=0;}return result+"\\".repeat(slashes*2)+'"';};return `--uwp ${quote(identity.nativeApplicationId!)} ${quote(identity.manifestExecutable!)} ""`;}
export function validNativeIdentity(value:any):value is NativeStoreIdentity{
 const text=(field:unknown)=>typeof field==="string"&&field.length>0&&field.length<=32768&&!/[\0\r\n]/.test(field);
 return ["epic","gog","xbox","amazon"].includes(value?.key?.store)&&typeof value.key.id==="string"&&/^[A-Za-z0-9._-]{1,250}$/.test(value.key.id)&&Number.isSafeInteger(value.accountRevision)&&value.accountRevision>=0&&
 [value.installPath,value.runtimeExecutable,value.startDirectory].every(text)&&(value.key.store==="xbox"?[value.nativeApplicationId,value.manifestExecutable].every(text):text(value.gameExecutablePath));
}
export const nativeStoreKey=(identity:{key:{store:string;id:string}})=>JSON.stringify([identity.key.store,identity.key.id]);
export function matchesNativeImport(candidate:NativeImportCandidate,identity:NativeStoreIdentity){const proof=candidate.proof,args=windowsArguments(proof.strShortcutLaunchOptions??"");if(proof.unAppID!==candidate.appId||canonical(proof.strShortcutExe)!==canonical(identity.runtimeExecutable)||shortcutDirectory(proof.strShortcutStartDir)!==shortcutDirectory(identity.startDirectory)||!args)return false;
 if(identity.key.store==="xbox")return args.length===4&&args[0]==="--uwp"&&canonical(args[1])===canonical(identity.nativeApplicationId)&&canonical(args[2])===canonical(identity.manifestExecutable);
 if(args[0]==="--game")return args.length>=2&&canonical(args[1])===canonical(identity.gameExecutablePath);
 return identity.key.store==="epic"&&args.length===5&&args[0]==="--epic"&&args[1]===identity.epicNamespace&&args[2]===identity.epicCatalogItemId&&args[3]===identity.key.id&&canonical(args[4])===canonical(identity.gameExecutablePath);
}

export function createNativeImportIndex(apps:any,store:any){
 type Entry={proof:ShortcutReadback|null;registration?:{unregister?:()=>void}};
 const entries=new Map<number,Entry>();let alive=true,inventoryKnown=false;
 const overview=(id:number)=>{try{return store?.GetAppOverviewByAppID?.(id);}catch{return undefined;}};
 const observe=()=>{
  const ids=new Set<number>();inventoryKnown=false;let observed=0;
  const collect=(source:any)=>{if(typeof source?.forEach!=="function")return;inventoryKnown=true;source.forEach((value:any)=>{const id=Number(value?.appid);const current=Number.isInteger(id)&&id>0&&id<=0xffffffff?overview(id):undefined;if(current){observed++;if(current.app_type===1073741824)ids.add(id);}});};
  try{collect(store?.allApps);collect(store?.m_mapAppOverview);}catch{inventoryKnown=false;return;}
  if(observed===0)inventoryKnown=false;
  if(ids.size>2048){inventoryKnown=false;return;}
  for(const[id,entry]of entries)if(!ids.has(id)){entry.registration?.unregister?.();entries.delete(id);}
  for(const id of ids){if(entries.has(id))continue;const entry:Entry={proof:null};entries.set(id,entry);
   try{entry.registration=apps.RegisterForAppDetails(id,(value:ShortcutReadback)=>{if(!alive||entries.get(id)!==entry)return;entry.proof=value?.unAppID===id&&[value.strShortcutExe,value.strShortcutLaunchOptions,value.strShortcutStartDir].every(field=>typeof field==="string")?Object.freeze({...value}):null;});}catch{entries.delete(id);inventoryKnown=false;}
  }
 };
 const candidates=()=>[...entries].flatMap(([appId,entry])=>{const title=overview(appId)?.display_name;return entry.proof&&typeof title==="string"&&title.trim()&&title.length<=2048?[{appId,title,proof:entry.proof}]:[];});
 return{observe,candidates,ready:()=>alive&&inventoryKnown&&[...entries.values()].every(entry=>!!entry.proof),
  read:(appId:number)=>candidates().find(candidate=>candidate.appId===appId),
  close(){alive=false;for(const entry of entries.values())entry.registration?.unregister?.();entries.clear();}};
}
