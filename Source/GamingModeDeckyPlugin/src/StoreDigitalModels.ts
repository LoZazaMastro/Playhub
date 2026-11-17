// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
// Wire records from Source/Shared/StoreLibraryModels.cs and StoreInstalledGameModels.cs.
import type {StoreKey,StoreJob} from "./storeLibraryRuntime";
export type {StoreKey};
export type StoreKind=StoreKey["store"];
export type StoreInstallProvider="microsoft"|"ea"|"ubisoft"|"battlenet"|"unknown";
export const validStoreInstallProvider=(value:unknown):value is StoreInstallProvider=>typeof value==="string"&&["microsoft","ea","ubisoft","battlenet","unknown"].includes(value);
export const STORE_KINDS:StoreKind[]=["epic","gog","xbox","amazon"];
export type StoreCapabilities={installKind:"direct"|"externalHandoff"|"unavailable";canConnect:boolean;canRefresh:boolean;canPause:boolean;canResume:boolean;canCancel:boolean;unavailableReason?:string|null};
export type StoreAccount={store:StoreKind;state:"disconnected"|"connecting"|"connected"|"expired"|"unavailable";revision:number;displayName:string|null;capabilities:StoreCapabilities;errorCode?:string|null};
export type StoreDigitalGame={installProvider?:StoreInstallProvider|null;libraryKind?:string|null;librarySource?:"gamepass";key:StoreKey;title:string;accountRevision:number;capabilities:StoreCapabilities;isInstalled:boolean;isLaunchable:boolean;installPath?:string|null;artworkKey?:string|null;downloadBytes?:number|null;unavailableReason?:string|null;artworkUrl?:string|null;artworkFallbackUrls?:string[]|null;artworkKind?:string|null};
export type StoreDigitalJob=StoreJob&{updatedAt:string;reasonCode?:string|null;errorCode?:string|null};
export type StoreDigitalSnapshot={revision:number;observedAt:string;accounts:StoreAccount[];games:StoreDigitalGame[];jobs:StoreDigitalJob[]};
export type StoreDigitalCommand={accepted:boolean;errorCode?:string|null;snapshot:StoreDigitalSnapshot;jobId?:string|null};
export type StoreInstallLocation={rootPath:string;displayName:string;freeBytes:number;isReady:boolean};
export type StoreDigitalPreparation={key:StoreKey;accountRevision:number;revision:number;selectionNonce:string|null;expiresAt:string;requiredBytes:number|null;suggestedPath:string|null;confirmedPath:string|null;locations:StoreInstallLocation[];installKind:StoreCapabilities["installKind"];errorCode?:string|null;languages?:string[]|null;language?:string|null};
export type StoreInstalledStatus={key:StoreKey;accountRevision:number;libraryRevision:number;isInstalled:boolean;canLaunch:boolean;canUninstall:boolean;installPath:string|null;errorCode?:string|null;uninstallErrorCode?:string|null};
export type StoreUninstallPreparation={key:StoreKey;accountRevision:number;libraryRevision:number;installPath:string|null;nonce:string|null;expiresAt:string;canUninstall:boolean;errorCode?:string|null};
export type StoreDigitalDirectories={path:string|null;parentPath:string|null;roots:StoreInstallLocation[];directories:{name:string;path:string}[];truncated:boolean};
export type StoreDigitalIntent={requestId:string;key:StoreKey;action:"install"|"manage";expiresAt:string};
export type StoreDigitalRequest=(method:string,args?:Record<string,unknown>)=>Promise<unknown>;
export const sameStoreKey=(left:StoreKey|null|undefined,right:StoreKey|null|undefined)=>!!left&&!!right&&left.store===right.store&&left.id===right.id;
export const storeGameId=(key:StoreKey)=>JSON.stringify([key.store,key.id]);
const object=(value:unknown):value is Record<string,any>=>!!value&&typeof value==="object";
export const storeCount=(value:unknown):value is number=>Number.isSafeInteger(value)&&(value as number)>=0;
const nullableCount=(value:unknown)=>value==null||storeCount(value);
export const validStoreKey=(value:unknown):value is StoreKey=>object(value)&&STORE_KINDS.includes(value.store)&&typeof value.id==="string"&&value.id.length>0&&value.id.length<=512&&value.id.trim()===value.id&&!/[\u0000-\u001f\u007f]/.test(value.id);
const validCaps=(value:unknown):value is StoreCapabilities=>object(value)&&["direct","externalHandoff","unavailable"].includes(value.installKind)&&["canConnect","canRefresh","canPause","canResume","canCancel"].every(key=>typeof value[key]==="boolean");
export const validDigitalGame=(value:unknown):value is StoreDigitalGame=>object(value)&&validStoreKey(value.key)&&typeof value.title==="string"&&value.title.length>0&&value.title.length<=2048&&storeCount(value.accountRevision)&&validCaps(value.capabilities)&&typeof value.isInstalled==="boolean"&&typeof value.isLaunchable==="boolean"&&(!value.isLaunchable||value.isInstalled)&&nullableCount(value.downloadBytes)&&(value.installProvider==null||validStoreInstallProvider(value.installProvider));
export function validDigitalSnapshot(value:unknown):value is StoreDigitalSnapshot{
 if(!object(value)||!storeCount(value.revision)||typeof value.observedAt!=="string"||!Number.isFinite(Date.parse(value.observedAt))||!Array.isArray(value.accounts)||value.accounts.length>4||!Array.isArray(value.games)||value.games.length>100000||!Array.isArray(value.jobs)||value.jobs.length>10000)return false;
 if(value.accounts.some(account=>!object(account)||!STORE_KINDS.includes(account.store)||!["disconnected","connecting","connected","expired","unavailable"].includes(account.state)||!storeCount(account.revision)||!validCaps(account.capabilities))||new Set(value.accounts.map(account=>account.store)).size!==value.accounts.length)return false;
 if(!value.games.every(validDigitalGame)||new Set(value.games.map(game=>storeGameId(game.key))).size!==value.games.length)return false;
 return value.jobs.every(job=>object(job)&&typeof job.jobId==="string"&&validStoreKey(job.key)&&storeCount(job.revision)&&storeCount(job.accountRevision)&&typeof job.installPath==="string"&&typeof job.title==="string"&&storeCount(job.downloadedBytes)&&nullableCount(job.totalBytes)&&["queued","blockedSteam","starting","downloading","pausing","pausedByUser","pausedBySteam","installing","verifying","completed","canceling","canceled","failed","interrupted","externalHandoff"].includes(job.state)&&["canPause","canResume","canCancel"].every(key=>typeof job[key]==="boolean")&&(job.canRemove==null||typeof job.canRemove==="boolean")&&(job.removalKind==null||["cleanup","history","windowsRequest"].includes(job.removalKind))&&(job.bytesPerSecond==null||Number.isFinite(job.bytesPerSecond)&&job.bytesPerSecond>=0)&&(job.progressPercent==null||Number.isFinite(job.progressPercent)&&job.progressPercent>=0&&job.progressPercent<=100))&&new Set(value.jobs.map(job=>job.jobId)).size===value.jobs.length;
}
export const absoluteWindowsPath=(path:unknown):path is string=>typeof path==="string"&&path.length<=32767&&!/[\u0000-\u001f]/.test(path)&&(/^[A-Za-z]:[\\/]/.test(path)||/^\\\\[^\\/]+[\\/][^\\/]+(?:[\\/]|$)/.test(path));
export const sameWindowsPath=(left:string|null|undefined,right:string|null|undefined)=>!!left&&!!right&&left.replace(/\//g,"\\").replace(/\\+$/,"").toLowerCase()===right.replace(/\//g,"\\").replace(/\\+$/,"").toLowerCase();
export const storeJobActive=(job:StoreJob&{reasonCode?:string|null})=>job.state==="externalHandoff"&&["windows_store_managed","windows_store_install_unconfirmed"].includes(job.reasonCode??"")||!["completed","canceled","failed","externalHandoff"].includes(job.state);
export function storeBytes(value:number|null|undefined):string{if(typeof value!=="number"||!Number.isFinite(value)||value<0)return "—";let unit=0;const units=["B","KiB","MiB","GiB","TiB"];while(value>=1024&&unit<units.length-1){value/=1024;unit++;}return `${value.toFixed(unit?1:0)} ${units[unit]}`;}
