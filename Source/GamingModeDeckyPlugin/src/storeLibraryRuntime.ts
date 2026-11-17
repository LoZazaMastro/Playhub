// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import { call } from "./controlBackend";
import {installStoreInstallRecents} from "./storeInstallRecents";
import {installStoreAchievementsRuntime,prepareStoreImportedAchievements} from "./StoreAchievementsRuntime";
import {createStoreCompletionNotifications} from "./storeCompletionNotifications";
import {reportStoreSteamMigration} from "./storeSteamMigration";
import {createNativeImportIndex,matchesNativeImport,nativeUwpArguments,validNativeIdentity,nativeStoreKey,type NativeStoreIdentity} from "./storeNativeImports";
import { localSteamDownloadActivity, matchingShortcut, shortcutAppId, shortcutDirectory, sameStoreDownload, type ShortcutReadback } from "./storeDownloadActivity";

export type StoreKey = { store: "epic" | "gog" | "xbox" | "amazon"; id: string };
export type StoreJob = { jobId: string; key: StoreKey; accountRevision: number; title: string; state: string; revision: number; downloadedBytes: number;
  totalBytes?: number; bytesPerSecond?: number; progressPercent?: number; canPause: boolean; canResume: boolean; canCancel: boolean; canRemove?: boolean; removalKind?:"cleanup"|"history"|"windowsRequest"|null; libraryKind?:string|null; canDownloadNow?: boolean; installPath: string; reasonCode?: string | null; errorCode?: string | null; updatedAt?:string };
export type StoreOwnedGame = { appId: number; key: StoreKey; executable: string; arguments: string; startDirectory: string; title: string;
  accountCurrent: boolean; nativeApplicationId?:string|null;manifestExecutable?:string|null;game?: { title?:string;libraryKind?:string|null;accountRevision: number; isInstalled: boolean; isLaunchable: boolean }; job?: StoreJob };
type Plan = { libraryKind?:string|null;requestId: string; claimId: string; key: StoreKey; accountRevision: number; title: string; executable: string; arguments: string; startDirectory: string; appId?: number; createdAppId?: number };
type Reply<T> = { ok: boolean; data?: T; errorCode?: string };
let owned: ReadonlyMap<number, StoreOwnedGame> = new Map();
let jobs: readonly StoreJob[] = [];
let steamLinks:ReadonlyMap<string,"present"|"absent">=new Map(),steamLinksAt=0;
let repairSteamLink:((key:StoreKey)=>Promise<boolean>)|undefined;
const listeners = new Set<() => void>();
export function subscribeStoreLibrary(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export const readStoreLibrary = () => owned;
export const readStoreDownloads = () => jobs;
export const readStoreSteamLink=(key:StoreKey)=>Date.now()-steamLinksAt<5000?steamLinks.get(nativeStoreKey({key}))??"unknown":"unknown";
export const repairStoreSteamShortcut=(key:StoreKey)=>repairSteamLink?.(key)??Promise.resolve(false);
export async function storeRequest<T>(method: string, args: Record<string, unknown> = {}): Promise<T> {
  const response = await call<[string, Record<string, unknown>], Reply<T>>("store_library_request", method, args);
  if (!response?.ok) throw new Error(response?.errorCode ?? "store_unavailable");
  return response.data as T;
}
export async function storeJobAction(job: StoreJob, action: "pause" | "resume" | "cancel" | "downloadNow") {
  // Progress can advance while the menu or its confirmation remains open.
  // Refresh the revision, but never apply the user's choice to another account,
  // game, destination or replacement job.
  const current = (await storeRequest<StoreJob[]>("downloads")).find(row => row.jobId === job.jobId);
  const capability = action === "downloadNow" ? "canDownloadNow" : action === "pause" ? "canPause" : action === "resume" ? "canResume" : "canRemove";
  if (!current || !sameStoreDownload(job, current) || action !== "cancel" && !current[capability]) throw new Error("store_job_changed");
  const result = await storeRequest<{ accepted: boolean; errorCode?: string; snapshot?: unknown }>(action === "cancel" ? "cancel-cleanup" : "control",
    { jobId: current.jobId, expectedRevision: current.revision, expectedKey:{...current.key}, expectedAccountRevision:current.accountRevision, expectedInstallPath:current.installPath, ...(action === "cancel" ? { deletePartial: job.removalKind === "cleanup" } : { action }), requestId: crypto.randomUUID() });
  if (typeof result?.accepted !== "boolean") throw new Error("store_command_rejected");
  return result;
}
const host = () => globalThis as any;
const appStore = () => host().appStore;
const steam = () => host().SteamClient;
const collectionNames = { epic: "Epic Games", gog: "GOG", xbox: "Xbox Store", amazon: "Amazon Games" } as const;

async function addStoreCollection(key: StoreKey, appId: number, current: () => boolean,libraryKind?:string|null) {
  const collections = host().collectionStore, store = appStore();
  if (!current() || !collections || typeof collections.GetCollectionIDByUserTag !== "function" ||
      typeof collections.GetCollection !== "function" || typeof collections.NewUnsavedCollection !== "function")
    throw new Error("steam_collections_unavailable");
  const overview = store?.GetAppOverviewByAppID(appId);
  if (!overview || Number(overview.appid) !== appId) throw new Error("steam_shortcut_unconfirmed");
  const name = key.store==="xbox"&&libraryKind==="gamePass"?"Xbox Game Pass":collectionNames[key.store], id = collections.GetCollectionIDByUserTag(name);
  let collection = typeof id === "string" ? collections.GetCollection(id) : undefined;
  if (!collection) collection = collections.NewUnsavedCollection(name, undefined, [overview]);
  if (!collection || typeof collection.Save !== "function") throw new Error("steam_collections_unavailable");
  if (!Array.isArray(collection.allApps) || !collection.allApps.some((app: any) => Number(app?.appid) === appId)) {
    const editable = collection.AsDragDropCollection?.();
    if (typeof editable?.AddApps !== "function") throw new Error("steam_collections_unavailable");
    editable.AddApps([overview]);
  }
  // Add only the verified shortcut; preserve the user's existing collection,
  // ordering and other games. Save must succeed before sync is acknowledged.
  await collection.Save();
  if (!current() || host().collectionStore !== collections || appStore() !== store ||
      !Array.isArray(collection.allApps) || !collection.allApps.some((app: any) => Number(app?.appid) === appId))
    throw new Error("steam_collection_unconfirmed");
}

async function reconcileStoreCollection(kind: StoreKey["store"], rows: readonly StoreOwnedGame[], current: () => boolean,libraryKind?:string|null) {
  const collections = host().collectionStore, store = appStore(), apps = steam()?.Apps;
  if (!current() || !collections || typeof collections.GetCollectionIDByUserTag !== "function" || typeof collections.GetCollection !== "function")
    throw new Error("steam_collections_unavailable");
  const relevant = rows.filter(row => row.key.store === kind && ownedShortcutIdentity(row));
  const matchesLibrary=(row:StoreOwnedGame)=>kind!=="xbox"||((row.game?.libraryKind==="gamePass")===(libraryKind==="gamePass"));
  const name = kind==="xbox"&&libraryKind==="gamePass"?"Xbox Game Pass":collectionNames[kind], id = collections.GetCollectionIDByUserTag(name);
  let collection = typeof id === "string" ? collections.GetCollection(id) : undefined;
  if (!collection && relevant.some(row => row.game?.isInstalled&&matchesLibrary(row))) collection = collections.NewUnsavedCollection?.(name, undefined, []);
  if (!collection) return;
  if (!Array.isArray(collection.allApps)) throw new Error("steam_collections_unavailable");
  const members = new Set<number>(collection.allApps.map((app: any) => Number(app?.appid)));
  const add: any[] = [], remove: any[] = [];
  for (const row of relevant) {
    const installed = row.game!.isInstalled&&matchesLibrary(row);
    if (installed === members.has(row.appId)) continue;
    const identity = ownedShortcutIdentity(row), proof = await readShortcut(row.appId);
    if (!current() || host().collectionStore !== collections || appStore() !== store || steam()?.Apps !== apps)
      throw new Error("steam_shortcut_context_changed");
    // Registry membership alone never grants permission to alter an edited or
    // manually replaced shortcut, even when its numeric AppID is unchanged.
    if (!identity || ownedShortcutIdentity(owned.get(row.appId)) !== identity || !matchingShortcut(proof, row, row.appId)) continue;
    const overview = store?.GetAppOverviewByAppID(row.appId);
    if (!overview || Number(overview.appid) !== row.appId) continue;
    (installed ? add : remove).push(overview);
  }
  if (!add.length && !remove.length) return;
  const editable = collection.AsDragDropCollection?.();
  if (typeof collection.Save !== "function" || add.length && typeof editable?.AddApps !== "function" || remove.length && typeof editable?.RemoveApps !== "function")
    throw new Error("steam_collections_unavailable");
  if (add.length) editable.AddApps(add);
  if (remove.length) editable.RemoveApps(remove);
  await collection.Save();
  if (!current() || host().collectionStore !== collections || appStore() !== store || steam()?.Apps !== apps ||
      add.some(app => !collection.allApps.some((other: any) => Number(other?.appid) === app.appid)) ||
      remove.some(app => collection.allApps.some((other: any) => Number(other?.appid) === app.appid)))
    throw new Error("steam_collection_unconfirmed");
}

export function ownedShortcutIdentity(row?: StoreOwnedGame): string | undefined {
  if (!row?.accountCurrent || !row.game || !Number.isInteger(row.appId) || row.appId <= 0 || row.appId > 0xffffffff ||
      !row.key || !["epic", "gog", "xbox", "amazon"].includes(row.key.store) || typeof row.key.id !== "string" || !row.key.id ||
      !Number.isInteger(row.game.accountRevision) || row.game.accountRevision < 0 ||
      ![row.executable, row.arguments, row.startDirectory].every(value => typeof value === "string" && value.length > 0)) return undefined;
  return JSON.stringify([row.appId, row.key.store, row.key.id, row.game.accountRevision, row.executable, row.arguments, row.startDirectory]);
}

// Durable registry membership is not proof that Steam's live shortcut still has those fields.
export function observeOwnedShortcut(row: StoreOwnedGame, onMatch: (matches: boolean) => void): () => void {
  const identity = ownedShortcutIdentity(row), apps = steam()?.Apps;
  let alive = true, registration: { unregister?: () => void } | undefined;
  onMatch(false);
  if (!identity || typeof apps?.RegisterForAppDetails !== "function") return () => { alive = false; };
  try {
    registration = apps.RegisterForAppDetails(row.appId, (details: ShortcutReadback) => {
      if (!alive) return;
      let matches = false;
      try { matches = steam()?.Apps === apps && ownedShortcutIdentity(owned.get(row.appId)) === identity && matchingShortcut(details, row, row.appId); } catch {}
      onMatch(matches);
    });
  } catch { if (alive) onMatch(false); }
  return () => { alive = false; registration?.unregister?.(); };
}

type CreationReceipt = Readonly<{ tuple: string; appId: number; createdAt: number; persisted?: boolean }>;
const creationSymbol = Symbol.for("playhub.store-library.own-created.v1");
function creationReceipts(apps: unknown, store: unknown): Map<string, CreationReceipt> {
  const root = host(), saved = root[creationSymbol];
  if (saved?.apps !== apps || saved?.store !== store || !(saved?.receipts instanceof Map))
    root[creationSymbol] = { apps, store, receipts: new Map<string, CreationReceipt>() };
  const records: Map<string, CreationReceipt> = root[creationSymbol].receipts, now = Date.now();
  for (const [claim, value] of records) if (now < value.createdAt || now - value.createdAt > 20 * 60 * 1000) records.delete(claim);
  return records;
}

async function readShortcut(appId: number, accept?: (proof: ShortcutReadback) => boolean): Promise<ShortcutReadback | null> {
  const apps = steam()?.Apps;
  if (typeof apps?.RegisterForAppDetails !== "function") return null;
  return new Promise(resolve => {
    let registration: { unregister?: () => void } | undefined, timer: ReturnType<typeof setTimeout> | undefined, poll: ReturnType<typeof setTimeout> | undefined, done = false;
    let latest: ShortcutReadback | null=null;
    const finish = (value: ShortcutReadback | null) => {
      if (done) return; done = true;
      if (timer) clearTimeout(timer);
      if (poll) clearTimeout(poll);
      registration?.unregister?.(); resolve(value);
    };
    const check=()=>{
      if(done)return;
      if(latest&&accept?.(latest)){finish(latest);return;}
      poll=setTimeout(check,50);
    };
    try {
      registration = apps.RegisterForAppDetails(appId, (details: ShortcutReadback) => {
        latest=details;
        if(!accept)finish(details);else if(details&&accept(details))finish(details);
      });
      if (done) registration?.unregister?.();
      else {timer = setTimeout(() => finish(null), 4000);if(accept)check();}
    } catch { finish(null); }
  });
}

async function applyPlan(plan: Plan, current: () => boolean) {
  if (!current()) return;
  const apps = steam()?.Apps, store = appStore();
  if (typeof apps?.RegisterForAppDetails !== "function" || typeof store?.GetAppOverviewByAppID !== "function")
    throw new Error("steam_shortcut_configuration_unavailable");
  const records = creationReceipts(apps, store);
  const tuple = JSON.stringify([plan.requestId, plan.claimId, plan.key.store, plan.key.id, plan.accountRevision,
    plan.title, plan.executable, plan.arguments, plan.startDirectory]);
  let receipt = records.get(plan.claimId);
  if (receipt && receipt.tuple !== tuple) throw new Error("steam_shortcut_identity_changed");
  let appId = plan.appId;
  if(plan.createdAppId!==undefined&&plan.createdAppId!==null) {
    if(!Number.isInteger(plan.createdAppId)||plan.createdAppId<=0||plan.createdAppId>0xffffffff
        ||appId&&appId!==plan.createdAppId||receipt&&receipt.appId!==plan.createdAppId)throw new Error("steam_shortcut_identity_changed");
    appId=plan.createdAppId;
    receipt=Object.freeze({tuple,appId:plan.createdAppId,createdAt:Date.now(),persisted:true});
    records.set(plan.claimId,receipt);
  }
  if (receipt) {
    if (appId && appId !== receipt.appId) throw new Error("steam_shortcut_identity_changed");
    appId = receipt.appId;
  }
  const quoted = `"${plan.executable}"`;
  const privateDraftName = `Playhub - ${plan.key.store} - ${plan.key.id}`;
  // A known owned/draft ID can be temporarily absent while Steam loads its
  // overview. Wait for that exact ID; never create another shortcut for it.
  if (!appId) {
    // Windows Steam can initially ignore AddShortcut's name/arguments. Never
    // create a draft before all APIs needed to finish it are available.
    if (["AddShortcut", "SetShortcutName", "SetShortcutExe", "SetShortcutStartDir", "SetShortcutLaunchOptions"]
        .some(method => typeof apps[method] !== "function")) throw new Error("steam_shortcut_configuration_unavailable");
    const collections = host().collectionStore;
    if (["GetCollectionIDByUserTag", "GetCollection", "NewUnsavedCollection"]
        .some(method => typeof collections?.[method] !== "function")) throw new Error("steam_collections_unavailable");
    // Steam's shortcut CRC excludes launch arguments. Check both native quoting
    // forms before adding, so another store/manual shortcut is never overwritten.
    const occupied=(name:string)=>[shortcutAppId(quoted,name),shortcutAppId(plan.executable,name)].some(id=>!!store.GetAppOverviewByAppID(id));
    const blockedIds=[shortcutAppId(quoted,plan.title),shortcutAppId(plan.executable,plan.title)].filter(id=>!!store.GetAppOverviewByAppID(id));
    if(blockedIds.length){
      const existing=await storeRequest<StoreOwnedGame[]>("steam-owned");
      if(!Array.isArray(existing)||blockedIds.some(id=>!existing.some(row=>row.appId===id&&row.accountCurrent&&(row.key.store!==plan.key.store||row.key.id!==plan.key.id))))throw new Error("steam_shortcut_conflict");
    }
    const initialName=blockedIds.length?privateDraftName:plan.title;
    if(occupied(initialName))throw new Error("steam_shortcut_conflict");
    if (!current() || steam()?.Apps !== apps || appStore() !== store) return;
    const created = await apps.AddShortcut(initialName, plan.executable, "", "");
    if (!Number.isInteger(created) || created <= 0 || created > 0xffffffff || blockedIds.includes(created) || appId && created !== appId)
      throw new Error("steam_shortcut_identity_changed");
    if (steam()?.Apps !== apps || appStore() !== store) throw new Error("steam_shortcut_context_changed");
    appId = created;
    receipt = Object.freeze({ tuple, appId: created, createdAt: Date.now() });
    if (records.size >= 256) records.delete(records.keys().next().value!);
    records.set(plan.claimId, receipt);
  }
  if (!current() || steam()?.Apps !== apps || appStore() !== store) return;
  const sameContext=()=>current()&&steam()?.Apps===apps&&appStore()===store;
  let proof = await readShortcut(appId!,value=>typeof value.strShortcutExe==="string"&&!!store.GetAppOverviewByAppID(appId!));
  if(!sameContext())throw new Error("steam_shortcut_context_changed");
  if(receipt) {
    const overview=store.GetAppOverviewByAppID(appId!), name=overview?.display_name;
    const defaultName=plan.executable.replace(/\\/g,"/").split("/").pop()!.replace(/\.exe$/i,"");
    const exactExecutable=(value: unknown)=>typeof value==="string"&&value.replace(/^"|"$/g,"").toLowerCase()===plan.executable.toLowerCase();
    // Only a retained receipt of THIS exact creation can repair Steam's
    // default metadata. Another title/target/argument/directory is never ours.
    if(!proof||proof.unAppID!==appId||!exactExecutable(proof.strShortcutExe)
        ||proof.strShortcutLaunchOptions!==""&&proof.strShortcutLaunchOptions!==plan.arguments
        ||shortcutDirectory(proof.strShortcutStartDir)!==shortcutDirectory(plan.startDirectory)
        ||name!==plan.title&&name!==defaultName&&name!==privateDraftName)throw new Error("steam_shortcut_unconfirmed");
    if(!receipt.persisted) {
      const saved=await storeRequest<{requestId:string;state:string}>("steam-sync-created",{requestId:plan.requestId,claimId:plan.claimId,appId,
        executable:proof.strShortcutExe,arguments:proof.strShortcutLaunchOptions,startDirectory:proof.strShortcutStartDir});
      if(!sameContext()||saved?.requestId!==plan.requestId||saved.state!=="pending")throw new Error("steam_shortcut_draft_unconfirmed");
      receipt=Object.freeze({...receipt,persisted:true});records.set(plan.claimId,receipt);
    }
    if(!matchingShortcut(proof,plan,appId!)||name!==plan.title) {
      const changes=[["SetShortcutName",plan.title],["SetShortcutExe",plan.executable],["SetShortcutStartDir",plan.startDirectory],["SetShortcutLaunchOptions",plan.arguments]] as const;
      if(changes.some(([method])=>typeof apps[method]!=="function"))throw new Error("steam_shortcut_configuration_unavailable");
      for(const [method,value]of changes) {
        if(!sameContext())throw new Error("steam_shortcut_context_changed");
        await Promise.resolve(apps[method](appId,value));
      }
    }
    proof=await readShortcut(appId!,value=>sameContext()&&matchingShortcut(value,plan,appId!)&&store.GetAppOverviewByAppID(appId!)?.display_name===plan.title);
  }
  if (!current() || steam()?.Apps !== apps || appStore() !== store || !matchingShortcut(proof, plan, appId!)) throw new Error("steam_shortcut_unconfirmed");
  await addStoreCollection(plan.key, appId!, current,plan.libraryKind);
  if (!current() || steam()?.Apps !== apps || appStore() !== store) throw new Error("steam_shortcut_context_changed");
  await storeRequest("steam-sync-ack", { requestId: plan.requestId, claimId: plan.claimId, appId,
    executable: proof!.strShortcutExe, arguments: proof!.strShortcutLaunchOptions, startDirectory: proof!.strShortcutStartDir });
  // Native ACK is authoritative; hydrate store achievements before publishing
  // a newly imported game's Home/Your stuff presentation.
  await warmImportedAchievements(async()=>{
    const rows=await storeRequest<StoreOwnedGame[]>("steam-owned");
    const imported=rows.find(row=>row.appId===appId&&row.key.store===plan.key.store&&row.key.id===plan.key.id&&row.accountCurrent&&row.game?.accountRevision===plan.accountRevision);
    if(imported&&current())await prepareStoreImportedAchievements(imported,current);
  });
  if (records.get(plan.claimId) === receipt) records.delete(plan.claimId);
}

async function warmImportedAchievements(work:()=>Promise<void>):Promise<void> {
  let timer:ReturnType<typeof setTimeout>|undefined;
  try{await Promise.race([Promise.resolve().then(work).catch(()=>{}),new Promise<void>(resolve=>{timer=setTimeout(resolve,8000);})]);}
  finally{if(timer!==undefined)clearTimeout(timer);}
}

async function applyPendingPlans(plans: readonly Plan[], current: () => boolean, deferred: Map<string, number>, eligible:(plan:Plan)=>boolean=()=>true) {
  for (const plan of plans.slice(0, 32)) {
    if (!current()) return;
    if(!eligible(plan))continue;
    // Keep creation strictly sequential: a failed or unconfirmed draft must
    // finish before another game can expose Steam's temporary default name.
    if ((deferred.get(plan.claimId) ?? 0) > Date.now()) return;
    try { await applyPlan(plan, current); deferred.delete(plan.claimId); }
    catch { deferred.set(plan.claimId, Date.now() + 30000); return; }
  }
}

async function reconcileNativeImports(index:ReturnType<typeof createNativeImportIndex>, identities:unknown, rows:readonly StoreOwnedGame[], current:()=>boolean):Promise<Set<string>>{
 const allowed=new Set<string>(),apps=steam()?.Apps,store=appStore();
 const sameContext=()=>current()&&steam()?.Apps===apps&&appStore()===store;
 if(!Array.isArray(identities)||identities.length>2000||!identities.every(validNativeIdentity)||!index.ready())return allowed;
 const keys=identities.map(nativeStoreKey);if(new Set(keys).size!==keys.length)return allowed;
 for(const identity of identities as NativeStoreIdentity[]){
  if(!sameContext())return new Set();
  try{
   const old=rows.find(row=>row.key.store===identity.key.store&&row.key.id===identity.key.id);
   // Repair only our exact, proven automatically suffixed name. Steam keeps
   // the AppID, launch tuple, collection membership and existing artwork.
   const title=old?.game?.title;
   if(old?.accountCurrent&&old.game?.isInstalled&&title&&typeof apps?.SetShortcutName==="function"){
    const suffix=old.title.slice(title.length),storeSuffix=` (${identity.key.store})`;
    const generated=old.title.startsWith(title)&&new RegExp(`^${storeSuffix.replace(/[.*+?^${}()|[\]\\]/g,"\\$&")}(?: [A-F0-9]{8})?$`,"i").test(suffix);
    const candidate=index.read(old.appId);
    if(generated&&candidate?.title===old.title&&matchesNativeImport(candidate,identity)&&matchingShortcut(candidate.proof,old,old.appId)){
     await Promise.resolve(apps.SetShortcutName(old.appId,title));
     await readShortcut(old.appId,proof=>sameContext()&&store.GetAppOverviewByAppID(old.appId)?.display_name===title&&matchingShortcut(proof,old,old.appId));
     if(!sameContext())return new Set();
    }
   }
   let originals=index.candidates().filter(candidate=>matchesNativeImport(candidate,identity));
   // The one retained DS wrapper can be converted to the already tested Import
   // Games activation path. Only its exact live old tuple authorizes this edit.
   if(identity.key.store==="xbox"&&originals.length===0&&old?.accountCurrent&&old.game?.accountRevision===identity.accountRevision&&old.game.isInstalled){
    const candidate=index.read(old.appId);
    if(candidate&&old.arguments===`--store-library xbox ${identity.key.id}`&&matchingShortcut(candidate.proof,old,old.appId)&&
        matchingShortcut({...candidate.proof,strShortcutLaunchOptions:""},{executable:identity.runtimeExecutable,arguments:"",startDirectory:identity.startDirectory},old.appId)){
     if(typeof apps?.SetShortcutLaunchOptions!=="function")continue;
     const args=nativeUwpArguments(identity);
     if(!sameContext())return new Set();
     await Promise.resolve(apps.SetShortcutLaunchOptions(old.appId,args));
     const proof=await readShortcut(old.appId,value=>sameContext()&&matchingShortcut(value,{executable:identity.runtimeExecutable,arguments:args,startDirectory:identity.startDirectory},old.appId));
     if(!proof||!sameContext())continue;
     const live=index.read(old.appId);
     if(live&&matchesNativeImport(live,identity))originals=[live];
    }
   }
   if(originals.length>1)continue;
   if(originals.length===1){
    const original=originals[0],proof=original.proof;
    if(!(old?.appId===original.appId&&old.accountCurrent&&old.nativeApplicationId===identity.nativeApplicationId&&
        old.manifestExecutable===identity.manifestExecutable&&old.title===original.title&&matchingShortcut(proof,old,old.appId))){
     const live=index.read(original.appId);if(!live||live.title!==original.title||!matchingShortcut(live.proof,{executable:proof.strShortcutExe!,arguments:proof.strShortcutLaunchOptions!,startDirectory:proof.strShortcutStartDir!},original.appId))continue;
     if(!sameContext())return new Set();
     const reply=await storeRequest<{accepted:boolean;owned:StoreOwnedGame}>("steam-adopt-import",{key:identity.key,expectedAccountRevision:identity.accountRevision,
      appId:original.appId,title:original.title,executable:proof.strShortcutExe,arguments:proof.strShortcutLaunchOptions,startDirectory:proof.strShortcutStartDir,
      ...(old?{expectedOwnedAppId:old.appId}:{})});
     if(!sameContext())return new Set();
     const after=index.read(original.appId);
     if(reply?.accepted!==true||reply.owned?.appId!==original.appId||reply.owned.key?.store!==identity.key.store||reply.owned.key.id!==identity.key.id||
        !after||after.title!==original.title||!matchingShortcut(after.proof,{executable:proof.strShortcutExe!,arguments:proof.strShortcutLaunchOptions!,startDirectory:proof.strShortcutStartDir!},original.appId))continue;
     await addStoreCollection(identity.key,original.appId,sameContext,reply.owned.game?.libraryKind??identity.libraryKind);
     try{await prepareStoreImportedAchievements(reply.owned,sameContext);}catch{/* A provider outage does not undo the verified native adoption. */}
    }
   }else if(old&&index.read(old.appId))continue;
   allowed.add(nativeStoreKey(identity));
  }catch{/* No new shortcut or collection mutation is authorized by an uncertain adoption. */}
 }
 return allowed;
}

function nativeLinkState(index:ReturnType<typeof createNativeImportIndex>,identity:NativeStoreIdentity,rows:readonly StoreOwnedGame[]):"present"|"absent"|"unknown"{
 if(!index.ready())return "unknown";
 const originals=index.candidates().filter(candidate=>matchesNativeImport(candidate,identity));if(originals.length===1)return "present";if(originals.length>1)return "unknown";
 const old=rows.find(row=>row.key.store===identity.key.store&&row.key.id===identity.key.id);
 if(old){const candidate=index.read(old.appId);if(candidate)return old.accountCurrent&&matchingShortcut(candidate.proof,old,old.appId)?"present":"unknown";}
 return "absent";
}

async function removeUninstalledSteamShortcuts(rows:readonly StoreOwnedGame[],current:()=>boolean){
 const apps=steam()?.Apps,store=appStore(),sameContext=()=>current()&&steam()?.Apps===apps&&appStore()===store;
 if(typeof apps?.RemoveShortcut!=="function"||typeof store?.GetAppOverviewByAppID!=="function")return;
 for(const row of rows){
  if(!sameContext())return;
  if(!ownedShortcutIdentity(row)||row.game?.isInstalled!==false||row.job&&!["completed","canceled","failed"].includes(row.job.state))continue;
  const present=store.GetAppOverviewByAppID(row.appId);
  if(present){
   const proof=await readShortcut(row.appId,value=>[value.strShortcutExe,value.strShortcutLaunchOptions,value.strShortcutStartDir].every(field=>typeof field==="string"));
   if(!proof||!sameContext()||!matchingShortcut(proof,row,row.appId))continue;
   // A fresh registry/game snapshot fences account/reinstall races before the
   // only destructive native call. Never remove a manually edited shortcut.
   const latest=(await storeRequest<StoreOwnedGame[]>("steam-owned")).find(value=>value.appId===row.appId&&ownedShortcutIdentity(value)===ownedShortcutIdentity(row));
   if(!sameContext()||!latest||latest.game?.isInstalled!==false||latest.job&&!["completed","canceled","failed"].includes(latest.job.state))continue;
   const again=await readShortcut(row.appId,value=>[value.strShortcutExe,value.strShortcutLaunchOptions,value.strShortcutStartDir].every(field=>typeof field==="string"));if(!again||!sameContext()||!matchingShortcut(again,row,row.appId))continue;
   await Promise.resolve(apps.RemoveShortcut(row.appId));
   for(let attempt=0;attempt<40&&sameContext()&&store.GetAppOverviewByAppID(row.appId);attempt++)await new Promise(resolve=>setTimeout(resolve,50));
  }
  if(!sameContext()||store.GetAppOverviewByAppID(row.appId))continue;
  await storeRequest("steam-forget-uninstalled",{key:row.key,appId:row.appId});
 }
}

export function installStoreLibraryRuntime() {
  const symbol = Symbol.for("playhub.store-library.runtime.v1");
  const root = host(); root[symbol]?.();
  let alive = true, busy = false, repairing=false, ownerId: string | undefined, sequence = 0;
  let heartbeatBusy=false,heartbeatGeneration=0,heartbeatDeadline:ReturnType<typeof setTimeout>|undefined;
  const stopAchievements=installStoreAchievementsRuntime({rows:()=>owned,subscribe:subscribeStoreLibrary,request:storeRequest});
  const stopRecents=installStoreInstallRecents({rows:()=>owned,jobs:()=>jobs,subscribe:subscribeStoreLibrary,identity:ownedShortcutIdentity,observe:observeOwnedShortcut});
  let observation: { at: number; utc: string; active: boolean | null } | undefined;
  let registration: { unregister?: () => void } | undefined;
  let registeredAt = 0, lastOwned = "", lastJobs = "";
  const reconciled = new Map<string, { signature: string; retryAt: number }>();
  const deferred = new Map<string, number>();
  let importApps:any,importStore:any,imports:ReturnType<typeof createNativeImportIndex>|undefined;
  const notifyCompleted=createStoreCompletionNotifications();
  const current = () => alive && root[symbol] === stop;
  const updateLinks=(next:Map<string,"present"|"absent">)=>{const changed=JSON.stringify([...steamLinks])!==JSON.stringify([...next]);steamLinks=next;steamLinksAt=Date.now();if(changed)listeners.forEach(listener=>{try{listener();}catch{}});};
  const repair=async(key:StoreKey)=>{
   const sameRepairContext=()=>current()&&importApps===steam()?.Apps&&importStore===appStore();if(!sameRepairContext()||busy||repairing||!imports)return false;repairing=true;
   try{
    imports.observe();const [native,rows]=await Promise.all([storeRequest<unknown>("steam-native-identities"),storeRequest<StoreOwnedGame[]>("steam-owned")]);
    if(!sameRepairContext()||!Array.isArray(native)||!Array.isArray(rows)||!imports.ready())return false;
    const identity=native.filter(validNativeIdentity).find(value=>value.key.store===key.store&&value.key.id===key.id);
    if(!identity||nativeLinkState(imports,identity,rows)!=="absent")return false;
    const old=rows.find(row=>row.key.store===key.store&&row.key.id===key.id);
    // Last native check immediately before retiring a stale receipt. An
    // original that appeared while metadata was loading must always win.
    imports.observe();if(!sameRepairContext()||!imports.ready()||nativeLinkState(imports,identity,rows)!=="absent"||old&&appStore()?.GetAppOverviewByAppID(old.appId))return false;
    const reply=await storeRequest<{requestId:string;store:string;accountRevision:number;state:string}>("steam-repair-missing",{key,expectedAccountRevision:identity.accountRevision,...(old?{expectedOwnedAppId:old.appId}:{})});
    if(!sameRepairContext()||reply?.store!==key.store||reply.accountRevision!==identity.accountRevision||reply.state!=="pending"||typeof reply.requestId!=="string")return false;
    const next=new Map(steamLinks);next.delete(nativeStoreKey({key}));updateLinks(next);return true;
   }catch{return false;}finally{repairing=false;}
  };
  repairSteamLink=repair;
  function observe() {
    if (registration && performance.now() - registeredAt >= 10000) {
      registration.unregister?.(); registration = undefined;
    }
    if (registration || typeof steam()?.Downloads?.RegisterForDownloadOverview !== "function") return;
    registeredAt = performance.now();
    registration = steam().Downloads.RegisterForDownloadOverview((value: unknown) => {
      if (!current() || (value as any)?.remote_client_id !== "0") return;
      observation = { at: performance.now(), utc: new Date().toISOString(), active: localSteamDownloadActivity(value) };
    });
  }
  async function heartbeat(){
    if(!current()||heartbeatBusy)return;
    heartbeatBusy=true;const generation=++heartbeatGeneration,valid=()=>current()&&heartbeatGeneration===generation;
    // Library reconciliation may wait on many native imports. Steam priority
    // has its own bounded lease loop and never waits behind that work.
    const deadline=setTimeout(()=>{if(valid()){++heartbeatGeneration;ownerId=undefined;heartbeatBusy=false;}},6000);heartbeatDeadline=deadline;
    try{
      observe();
      if(!ownerId){const registered=await storeRequest<{ownerId:string}>("steam-register");if(!valid())return;ownerId=registered.ownerId;sequence=0;}
      if(ownerId&&valid()){
        const fresh=observation&&performance.now()-observation.at<15000?observation:undefined;
        const reply=await storeRequest<{accepted:boolean}>("steam-activity",{ownerId,sequence:++sequence,observedAt:fresh?.utc??new Date().toISOString(),activeLocalDownload:fresh?.active??null});
        if(valid()&&!reply.accepted)ownerId=undefined;
      }
    }catch{if(valid())ownerId=undefined;}
    finally{clearTimeout(deadline);if(heartbeatDeadline===deadline)heartbeatDeadline=undefined;if(valid())heartbeatBusy=false;}
  }
  async function tick() {
    if (!current() || busy || repairing) return;
    busy = true;
    try {
      const currentJobs = await storeRequest<StoreJob[]>("downloads");
      if (!current()) return;
      notifyCompleted(currentJobs);
      const serializedJobs = JSON.stringify(currentJobs);
      if (Array.isArray(currentJobs) && serializedJobs !== lastJobs) {
        lastJobs = serializedJobs; jobs = currentJobs;
        listeners.forEach(listener => { try { listener(); } catch {} });
      }
      if(importApps!==steam()?.Apps||importStore!==appStore()||!imports){imports?.close();importApps=steam()?.Apps;importStore=appStore();imports=createNativeImportIndex(importApps,importStore);}
      imports.observe();
      let allowedNative=new Set<string>();
      try{const [native,before]=await Promise.all([storeRequest<unknown>("steam-native-identities"),storeRequest<StoreOwnedGame[]>("steam-owned")]);
       if(!current())return;if(Array.isArray(before)){allowedNative=await reconcileNativeImports(imports,native,before,current);
        const links=new Map<string,"present"|"absent">();if(Array.isArray(native))for(const identity of native.filter(validNativeIdentity)){const state=nativeLinkState(imports,identity,before);if(state!=="unknown")links.set(nativeStoreKey(identity),state);}updateLinks(links);
       }
      }catch{/* Missing/unknown native identity cannot authorize another Xbox shortcut. */}
      const pending = await storeRequest<{ plans: Plan[] }>("steam-sync-pending");
      await applyPendingPlans(pending.plans, current, deferred,plan=>{if(!allowedNative.has(nativeStoreKey(plan)))return false;const next=new Map(steamLinks);next.delete(nativeStoreKey(plan));updateLinks(next);return true;});
      const rows = await storeRequest<StoreOwnedGame[]>("steam-owned");
      if (!current() || !Array.isArray(rows)) return;
      await removeUninstalledSteamShortcuts(rows,current);
      if(!current())return;
      const serial = JSON.stringify(rows);
      if (serial !== lastOwned) {
        lastOwned = serial; owned = new Map(rows.map(row => [row.appId, row]));
        listeners.forEach(listener => { try { listener(); } catch {} });
      }
      for (const [kind,libraryKind] of ([...Object.keys(collectionNames).map(kind=>[kind,null]),["xbox","gamePass"]] as [StoreKey["store"],string|null][])) {
        if (!current()) return;
        const signature = JSON.stringify(rows.filter(row => row.key.store === kind).map(row => [ownedShortcutIdentity(row), row.game?.isInstalled,row.game?.libraryKind]));
        const collectionKey=kind+"/"+(libraryKind??"store");
        const prior = reconciled.get(collectionKey);
        if (prior?.signature === signature && prior.retryAt > Date.now()) continue;
        try {
          await reconcileStoreCollection(kind, rows, current,libraryKind);
          reconciled.set(collectionKey, { signature, retryAt: Date.now() + 60000 });
        } catch { reconciled.set(collectionKey, { signature, retryAt: Date.now() + 30000 }); }
      }
      if(imports&&current())try{const collections=host().collectionStore,store=appStore(),apps=steam()?.Apps;await reportStoreSteamMigration(storeRequest,imports,collections,store,()=>current()&&host().collectionStore===collections&&appStore()===store&&steam()?.Apps===apps);}catch{/* A partial migration remains pending; never manufacture success. */}
    } catch { /* Library failures do not retire the independent Steam lease. */ }
    finally { busy = false; }
  }
  function stop() {
    if (!alive) return; alive = false;
    stopRecents();stopAchievements();
    clearInterval(timer);clearInterval(heartbeatTimer);clearTimeout(heartbeatDeadline);++heartbeatGeneration;registration?.unregister?.();
    imports?.close();
    if(repairSteamLink===repair)repairSteamLink=undefined;
    if (root[symbol] === stop) {
      delete root[symbol]; owned = new Map(); jobs = [];
      steamLinks=new Map();steamLinksAt=0;
      listeners.forEach(listener => { try { listener(); } catch {} });
    }
  }
  root[symbol] = stop;
  const timer = setInterval(() => void tick(), 2000);
  const heartbeatTimer=setInterval(()=>void heartbeat(),2000);
  void heartbeat();
  void tick();
  return stop;
}
