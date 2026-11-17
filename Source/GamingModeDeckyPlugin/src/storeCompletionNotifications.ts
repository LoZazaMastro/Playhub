// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
import {toaster} from "./decky";
import {createCompletionObserver,completionTitle} from "./completionNotifications";
import {openStoreDigitalDownloads} from "./StoreDigitalNavigation";
import type {StoreJob} from "./storeLibraryRuntime";
const states=new Set(["queued","blockedSteam","starting","downloading","pausing","pausedByUser","pausedBySteam","installing","verifying","completed","canceling","canceled","failed","interrupted","externalHandoff"]);
export function createStoreCompletionNotifications(){
 const observe=createCompletionObserver("playhub.store-completions.v1",job=>toaster?.toast?.({title:completionTitle(navigator.language,"install"),body:job.title,subtext:"Playhub",showToast:true,playSound:true,onClick:()=>openStoreDigitalDownloads()}));
 let revision=0;
 return(jobs:readonly StoreJob[])=>{
  if(!Array.isArray(jobs)||jobs.some(job=>!job?.key||!states.has(job.state)||!["epic","gog","xbox","amazon"].includes(job.key.store)||typeof job.key.id!=="string"))return;
  observe(++revision,jobs.map(job=>({id:job.jobId,identity:JSON.stringify([job.key.store,job.key.id]),title:job.title,completed:job.state==="completed"})));
 };
}
