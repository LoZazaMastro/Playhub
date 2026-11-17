// SPDX-License-Identifier: MIT. A completion receipt follows native readback, never a request alone.
import {validDigitalSnapshot,sameWindowsPath,storeGameId,type StoreDigitalSnapshot} from "./StoreDigitalModels";
import {validNativeIdentity,nativeStoreKey,matchesNativeImport,type NativeStoreIdentity,type createNativeImportIndex} from "./storeNativeImports";
import {matchingShortcut} from "./storeDownloadActivity";
import type {StoreOwnedGame} from "./storeLibraryRuntime";
type Migration={requestId:string;version:string;steamAccountId:string;state:string;confirmed:number;total:number};
type Request=<T>(method:string,args?:Record<string,unknown>)=>Promise<T>;
const collectionNames={epic:"Epic Games",gog:"GOG",xbox:"Xbox Store",amazon:"Amazon Games"};
const valid=(value:any):value is Migration=>!!value&&typeof value.requestId==="string"&&/^[0-9a-f-]{36}$/i.test(value.requestId)&&typeof value.version==="string"&&/^\d+\.\d+\.\d+:store-collections-v\d+$/.test(value.version)&&/^\d{17}$/.test(value.steamAccountId)&&BigInt(value.steamAccountId)>76561197960265728n&&BigInt(value.steamAccountId)<=76561202255233023n&&value.state==="pending";
const snapshotSignature=(snapshot:any)=>JSON.stringify([snapshot.accounts.map((row:any)=>[row.store,row.state,row.revision]).sort(),snapshot.games.filter((game:any)=>game.isInstalled).map((game:any)=>[storeGameId(game.key),game.accountRevision,game.installPath]).sort()]);
const nativeSignature=(rows:NativeStoreIdentity[])=>JSON.stringify(rows.map(row=>[nativeStoreKey(row),row.accountRevision,row.installPath,row.runtimeExecutable,row.startDirectory,row.nativeApplicationId,row.manifestExecutable,row.gameExecutablePath,row.epicNamespace,row.epicCatalogItemId,row.libraryKind]).sort());
const ownedSignature=(rows:StoreOwnedGame[])=>JSON.stringify(rows.filter(row=>row.accountCurrent&&row.game?.isInstalled).map(row=>[row.appId,nativeStoreKey(row),row.game!.accountRevision,row.title,row.executable,row.arguments,row.startDirectory,row.game?.libraryKind]).sort());
const accountSignature=(snapshot:StoreDigitalSnapshot)=>JSON.stringify(snapshot.accounts.map(account=>[account.store,account.state,account.revision,account.capabilities.canRefresh]).sort());
const pendingSignature=(pending:Migration[])=>JSON.stringify(pending.map(item=>[item.requestId,item.version,item.steamAccountId]).sort());
const samePending=(value:unknown,signature:string):value is Migration[]=>Array.isArray(value)&&value.length<=64&&value.every(valid)&&pendingSignature(value)===signature;
type InventoryRead={signature:string;promise:Promise<boolean>};
const migrationInventory=new WeakMap<Request,InventoryRead>();

// A restored snapshot can still say "not installed" for an official launcher
// install. Refresh ownership/inventory once for this migration and these exact
// account revisions before even a zero-game completion can be considered.
async function refreshMigrationInventory(request:Request,pending:Migration[],before:StoreDigitalSnapshot,current:()=>boolean){
 const accounts=accountSignature(before),migrations=pendingSignature(pending),signature=JSON.stringify([migrations,accounts]);
 let read=migrationInventory.get(request);
 if(read?.signature!==signature){
  const promise=Promise.resolve().then(async()=>{
   if(!current())return false;
   // Drain all reads, including failures, before permitting a future retry.
   const replies=await Promise.allSettled(before.accounts.filter(account=>account.state==="connected"&&account.capabilities.canRefresh).map(async account=>
    request<any>("refresh",{store:account.store,expectedAccountRevision:account.revision})));
   if(!current()||replies.some(reply=>reply.status!=="fulfilled"||reply.value?.accepted!==true||!validDigitalSnapshot(reply.value.snapshot)||accountSignature(reply.value.snapshot)!==accounts))return false;
   const [after,fresh]=await Promise.all([request<unknown>("snapshot"),request<unknown>("steam-migration-pending")]);
   return current()&&validDigitalSnapshot(after)&&accountSignature(after)===accounts&&samePending(fresh,migrations);
  }).catch(()=>false);
  const next={signature,promise};read=next;migrationInventory.set(request,next);
  void promise.then(ready=>{if(!ready&&migrationInventory.get(request)===next)migrationInventory.delete(request);});
 }
 // The global observer sends the Steam activity heartbeat from this same tick.
 // Do not hold it behind provider network IO; a later tick consumes the ACK.
 const pendingRead=Symbol(),ready=await Promise.race([read.promise,Promise.resolve(pendingRead)]);
 if(ready===pendingRead)return false;
 if(!ready||!current()){
  if(migrationInventory.get(request)===read)migrationInventory.delete(request);
  return false;
 }
 return true;
}

export async function reportStoreSteamMigration(request:Request,index:ReturnType<typeof createNativeImportIndex>,collections:any,store:any,current:()=>boolean){
 const pending=await request<unknown>("steam-migration-pending");
 if(!current()||!Array.isArray(pending)||pending.length>64||!pending.every(valid)||!pending.length)return;
 index.observe();if(!index.ready())return;
 const before=await request<unknown>("snapshot");
 if(!current()||!validDigitalSnapshot(before)||!await refreshMigrationInventory(request,pending,before,current))return;
 const [snapshot,native,owned]=await Promise.all([request<unknown>("snapshot"),request<unknown>("steam-native-identities"),request<StoreOwnedGame[]>("steam-owned")]);
 if(!current()||!validDigitalSnapshot(snapshot)||accountSignature(snapshot)!==accountSignature(before)||!Array.isArray(native)||native.length>2000||!native.every(validNativeIdentity)||!Array.isArray(owned)||!index.ready())return;
 if(snapshot.accounts.some(account=>account.state!=="connected"&&owned.some(row=>row.key?.store===account.store)))return;
 const installed=snapshot.games.filter(game=>game.isInstalled),keys=new Set(installed.map(game=>storeGameId(game.key)));
 if(native.length!==installed.length||new Set(native.map(nativeStoreKey)).size!==native.length||native.some(identity=>!keys.has(nativeStoreKey(identity))))return;
 let confirmed=0;
 for(const identity of native as NativeStoreIdentity[]){
  const game=installed.find(game=>storeGameId(game.key)===nativeStoreKey(identity)),account=snapshot.accounts.find(account=>account.store===identity.key.store);
  if(!game||account?.state!=="connected"||account.revision!==identity.accountRevision||game.accountRevision!==identity.accountRevision||!sameWindowsPath(game.installPath,identity.installPath))continue;
  const matches=index.candidates().filter(candidate=>matchesNativeImport(candidate,identity));
  const rows=owned.filter(row=>row.key?.store===identity.key.store&&row.key.id===identity.key.id&&row.accountCurrent&&row.game?.isInstalled&&row.game.accountRevision===identity.accountRevision);
  // Non-import store-library wrappers still need their exact acknowledged tuple.
  const row=rows.length===1?rows[0]:undefined,candidate=row?index.read(row.appId):undefined;
  if(!row||!candidate||matches.length>1||matches.length===1&&matches[0].appId!==row.appId||!matchingShortcut(candidate.proof,row,row.appId))continue;
  const collectionId=collections?.GetCollectionIDByUserTag?.(identity.key.store==="xbox"&&row.game?.libraryKind==="gamePass"?"Xbox Game Pass":collectionNames[identity.key.store]);
  const collection=typeof collectionId==="string"?collections?.GetCollection?.(collectionId):undefined;
  if(!collection||typeof collection.Save!=="function"||!Array.isArray(collection.allApps)||!collection.allApps.some((app:any)=>Number(app?.appid)===row.appId))continue;
  // Save and re-fetch the collection, so a transient in-memory edit is never
  // reported as a completed migration.
  await collection.Save();if(!current()||!index.ready()||store?.GetAppOverviewByAppID?.(row.appId)?.appid!==row.appId) return;
  const readback=collections.GetCollection(collectionId);if(!Array.isArray(readback?.allApps)||!readback.allApps.some((app:any)=>Number(app?.appid)===row.appId))continue;
  const after=index.read(row.appId);if(!after||!matchingShortcut(after.proof,row,row.appId))continue;
  confirmed++;
 }
 if(!current())return;
 // The server validates active Steam identity too. Re-read it immediately
 // before reporting; switching accounts during native work invalidates proof.
 const [fresh,freshSnapshot,freshNative,freshOwned]=await Promise.all([request<unknown>("steam-migration-pending"),request<unknown>("snapshot"),request<unknown>("steam-native-identities"),request<StoreOwnedGame[]>("steam-owned")]);
 if(!current()||!Array.isArray(fresh)||!validDigitalSnapshot(freshSnapshot)||accountSignature(freshSnapshot)!==accountSignature(before)||!Array.isArray(freshNative)||!freshNative.every(validNativeIdentity)||!Array.isArray(freshOwned)||snapshotSignature(snapshot)!==snapshotSignature(freshSnapshot)||nativeSignature(native)!==nativeSignature(freshNative)||ownedSignature(owned)!==ownedSignature(freshOwned))return;
 for(const item of pending){if(!fresh.some(value=>valid(value)&&value.requestId===item.requestId&&value.version===item.version&&value.steamAccountId===item.steamAccountId))continue;
  await request("steam-migration-report",{requestId:item.requestId,steamAccountId:item.steamAccountId,confirmed,total:installed.length,complete:confirmed===installed.length});
  if(!current())return;
 }
}
