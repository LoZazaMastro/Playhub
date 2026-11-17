// SPDX-License-Identifier: MIT. Native row selection remains available even without actions.
import {SP_REACT as React,DFL} from "./decky";
import {FaCheckCircle,FaCloudDownloadAlt,FaPauseCircle,FaExclamationCircle,FaClock,FaBan} from "react-icons/fa";
import {StoreDigitalController} from "./StoreDigitalState";
import {storeDigitalDisplayTitle} from "./storeDigitalPresentation";
import {type StoreDigitalJob,storeBytes} from "./StoreDigitalModels";
import {sameStoreDownload} from "./storeDownloadActivity";
import {type StoreDigitalCopy,digitalStoreName,storeDigitalRuntimeError,storeDigitalError} from "./StoreDigitalLocale";
import {DigitalFocusable as Focusable,DigitalButton as Button,useDigitalTarget,digitalTakeFocus,digitalModalOpen,useDigitalModalGuard,type StoreDigitalNav} from "./StoreDigitalNative";
import {StoreDigitalStyles} from "./StoreDigitalStyles";
import {confirmStoreDigital} from "./StoreDigitalDialogs";

type Target={element:HTMLElement|null;nav:StoreDigitalNav|null};
type Action="pause"|"resume"|"cancel"|"downloadNow"|"remove"|"dismiss";
const {ModalRoot,showModal}=DFL as any;
export const orderStoreDigitalDownloads=(jobs:readonly StoreDigitalJob[])=>[...jobs.filter(job=>job.state==="downloading"),...jobs.filter(job=>job.state!=="downloading")];

export function StoreDigitalDownloadOptions({controller,c,job:opened,onClosed,closeModal}:{controller:StoreDigitalController;c:StoreDigitalCopy;job:StoreDigitalJob;onClosed?:()=>void;closeModal?:()=>void}){
 useDigitalModalGuard();
 const view=React.useSyncExternalStore(controller.subscribe,controller.state),job=view.snapshot?.jobs.find(row=>row.jobId===opened.jobId),valid=!!job&&sameStoreDownload(opened,job);
 const close=useDigitalTarget(),first=useDigitalTarget(),targets=React.useRef(new Map<Action,Target>()),alive=React.useRef(true),working=React.useRef(false);
 const windowsFallback=valid&&job.removalKind==null&&job.key.store==="xbox"&&job.state==="externalHandoff"&&job.reasonCode==="windows_store_managed"&&job.errorCode==="windows_store_install_unconfirmed";
 const dismiss=valid&&job.canRemove===true&&["history","windowsRequest"].includes(job.removalKind??"")||windowsFallback;
 const enabled:Action[]=valid?[...(job.canPause?["pause" as const]:[]),...(job.canResume?["resume" as const]:[]),...(job.canDownloadNow&&job.state!=="downloading"?["downloadNow" as const]:[]),...(["completed","canceled"].includes(job.state)?["remove" as const]:["cancel" as const])]:[];
 React.useLayoutEffect(()=>{if(!first.takeFocus())close.takeFocus();},[]);
 React.useLayoutEffect(()=>{if(view.busy||working.current)return;const focused=close.ref.current?.ownerDocument.querySelector<HTMLElement>(".gpfocus");if(focused?.isConnected&&!focused.matches(":disabled,[aria-disabled=true]"))return;if(!first.takeFocus())close.takeFocus();},[enabled.join(","),view.busy]);
 React.useEffect(()=>{alive.current=true;return()=>{alive.current=false;onClosed?.();};},[]);
 React.useEffect(()=>{if(!job)queueMicrotask(()=>{if(alive.current)closeModal?.();});},[!!job]);
 const finish=()=>{if(!working.current)closeModal?.();};
 const invoke=async(action:Action)=>{
  if(!valid||!job||view.busy||view.unknown||working.current||!enabled.includes(action))return;
  working.current=true;
  try{
   if(action==="cancel"&&!dismiss){
    const approved=await confirmStoreDigital(c.confirmCancel,c.cancelCleanupNote,c.cancelDownload,c);
    if(!alive.current)return;
    if(!approved){const target=targets.current.get(action);if(!digitalTakeFocus(target?.element,target?.nav))close.takeFocus();return;}
   }
   const accepted=action==="remove"?await controller.removeHistory(job.jobId):action==="dismiss"?job.removalKind?await controller.removeRequest(job):await controller.dismissWindowsInstall(job.jobId):action==="cancel"?await controller.cancelAndCleanup(job):await controller.control(job,action);
   if(!alive.current)return;
   if(accepted)closeModal?.();else close.takeFocus();
  }finally{working.current=false;}
 };
 const labels:Record<Action,string>={pause:c.pause,resume:c.resume,downloadNow:c.downloadNow,cancel:job&&["failed","interrupted","canceled"].includes(job.state)?c.removeFromQueue:c.cancelDownload,remove:c.removeFromQueue,dismiss:c.removeFromQueue};
 return <ModalRoot className="ph-sd-modal-frame" closeModal={finish} onCancel={finish} bDisableBackgroundDismiss><Focusable className="ph-sd-root ph-sd-modal" flow-children="column" noFocusRing onCancel={(event:any)=>{event.preventDefault();event.stopPropagation();finish();}}><StoreDigitalStyles/><h2>{storeDigitalDisplayTitle(job??opened)}</h2><Focusable className="ph-sd-modal-body" flow-children="column" noFocusRing>
 {!valid&&job&&<div className="ph-sd-banner" role="alert">{c.errorQueueChanged}</div>}{view.error&&view.errorJobId===opened.jobId&&<div className="ph-sd-banner" role="alert">{storeDigitalError(view.error,c)}</div>}
 {dismiss&&<div className="ph-sd-muted">{job?.removalKind==="history"?c.removeHistoryNote:c.dismissRequestNote}</div>}
 {!enabled.length&&<div className="ph-sd-muted">{view.busy?c.loading:c.downloadNoActions}</div>}
 <Focusable className="ph-sd-actions" flow-children="row" noFocusRing>
 {enabled.map((action,index)=><Button key={action} ref={(node:HTMLElement|null)=>{const previous=targets.current.get(action)??{element:null,nav:null};previous.element=node;targets.current.set(action,previous);if(index===0&&action!=="cancel"&&action!=="remove")first.ref.current=node;}} navRef={(node:StoreDigitalNav|null)=>{const previous=targets.current.get(action)??{element:null,nav:null};previous.nav=node;targets.current.set(action,previous);if(index===0&&action!=="cancel"&&action!=="remove")first.navRef.current=node;}} data-job-action={action} className={`ph-sd-button${action==="cancel"?" ph-sd-danger":""}`} disabled={view.busy||view.unknown} onClick={()=>void invoke(action)}>{labels[action]}</Button>)}
 <Button ref={close.ref} navRef={close.navRef} className="ph-sd-button" disabled={view.busy} onClick={finish}>{c.close}</Button>
 </Focusable>
 </Focusable></Focusable></ModalRoot>;
}

export function StoreDigitalDownloads({controller,c,onEmptyFocus}:{controller:StoreDigitalController;c:StoreDigitalCopy;onEmptyFocus?:()=>void}){
 const view=React.useSyncExternalStore(controller.subscribe,controller.state),jobs=orderStoreDigitalDownloads(view.snapshot?.jobs??[]),list=React.useRef<HTMLElement|null>(null),clear=useDigitalTarget();
 const rows=React.useRef(new Map<string,Target>()),memory=React.useRef<{jobId:string|null;index:number}|null>(null),modal=React.useRef<any>(null),opening=React.useRef(false),alive=React.useRef(true),latest=React.useRef(jobs);latest.current=jobs;
 const target=(id:string)=>{let value=rows.current.get(id);if(!value){value={element:null,nav:null};rows.current.set(id,value);}return value;};
 const takeRow=(id:string)=>{const position=latest.current.findIndex(job=>job.jobId===id);if(position<0)return false;memory.current={jobId:id,index:position};const value=rows.current.get(id);return digitalTakeFocus(value?.element,value?.nav);};
 const restore=()=>{if(!alive.current||digitalModalOpen())return;const previous=memory.current;if(previous?.jobId&&takeRow(previous.jobId))return;const next=latest.current[Math.min(Math.max(previous?.index??0,0),latest.current.length-1)];if(next&&takeRow(next.jobId))return;if(clear.ref.current&&!clear.ref.current.matches(":disabled,[aria-disabled=true]")&&clear.takeFocus())return;onEmptyFocus?.();};
 React.useEffect(()=>{alive.current=true;return()=>{alive.current=false;const held=modal.current;queueMicrotask(()=>held?.Close?.());};},[]);
 React.useLayoutEffect(()=>{if(!memory.current||digitalModalOpen())return;const document=list.current?.ownerDocument??clear.ref.current?.ownerDocument,focused=document?.querySelector<HTMLElement>(".gpfocus");if(focused?.isConnected&&!focused.matches(":disabled,[aria-disabled=true]"))return;restore();},[view.snapshot?.jobs,view.busy,view.unknown]);
 const open=(job:StoreDigitalJob)=>{if(!alive.current||opening.current||digitalModalOpen())return;opening.current=true;memory.current={jobId:job.jobId,index:latest.current.findIndex(row=>row.jobId===job.jobId)};modal.current=showModal(<StoreDigitalDownloadOptions controller={controller} c={c} job={job} closeModal={()=>modal.current?.Close?.()} onClosed={()=>{modal.current=null;opening.current=false;queueMicrotask(restore);}}/>,undefined,{bNeverPopOut:true});};
 const navigate=(event:any)=>{const buttons=(DFL as any).GamepadButton;if(!buttons||event.defaultPrevented||digitalModalOpen())return;const row=(event.target as HTMLElement|null)?.closest<HTMLElement>("[data-store-job]"),id=row?.getAttribute("data-store-job");if(!id||!list.current?.contains(row!))return;const index=jobs.findIndex(job=>job.jobId===id),button=event.detail?.button;if(index<0||![buttons.DIR_UP,buttons.DIR_DOWN,buttons.DIR_LEFT,buttons.DIR_RIGHT].includes(button))return;event.preventDefault();event.stopPropagation();if(button===buttons.DIR_UP||button===buttons.DIR_DOWN){const next=jobs[index+(button===buttons.DIR_UP?-1:1)];if(next)takeRow(next.jobId);else if(button===buttons.DIR_UP){if(!clear.takeFocus())onEmptyFocus?.();}}};
 return <><Focusable className="ph-sd-toolbar" flow-children="row" noFocusRing><div className="ph-sd-muted">{c.downloads} · {jobs.length}</div><Button ref={clear.ref} navRef={clear.navRef} className="ph-sd-button" onFocusWithin={(focused:boolean)=>{if(focused)memory.current={jobId:null,index:0};}} disabled={view.busy||view.unknown||!jobs.some(job=>job.state==="completed"||job.state==="canceled")} onClick={()=>void controller.clearCompleted()}>{c.clearCompleted}</Button></Focusable>
 {!jobs.length?<div className="ph-sd-empty"><h2>{c.emptyDownloads}</h2><p>{c.emptyDownloadsHelp}</p></div>:<Focusable ref={list} className="ph-sd-download-list" flow-children="column" noFocusRing onButtonDown={navigate}>{jobs.map((job,index)=>{
  const paused=["pausedByUser","pausedBySteam","pausing"].includes(job.state),failed=["failed","interrupted"].includes(job.state),complete=job.state==="completed",queued=["queued","blockedSteam"].includes(job.state),canceled=job.state==="canceled",unconfirmed=job.reasonCode==="windows_store_install_unconfirmed"||job.errorCode==="windows_store_install_unconfirmed",Icon=complete?FaCheckCircle:canceled?FaBan:paused?FaPauseCircle:failed?FaExclamationCircle:queued||unconfirmed?FaClock:FaCloudDownloadAlt;
  const state=unconfirmed?c.xboxInstallPending:complete?c.completed:canceled?c.canceled:paused?c.paused:job.state==="failed"?c.failed:job.state==="interrupted"?c.interrupted:queued?c.waiting:job.state==="installing"?c.installing:job.state==="verifying"?c.verifying:job.state==="canceling"?c.canceling:c.active,progress=complete?100:job.progressPercent??(job.totalBytes?job.downloadedBytes/job.totalBytes*100:undefined),runtimeError=!complete&&!canceled?(storeDigitalRuntimeError(job.errorCode,c)??storeDigitalRuntimeError(job.reasonCode,c)):null;
  return <Focusable key={job.jobId} ref={(node:HTMLElement|null)=>{const value=target(job.jobId);value.element=node;if(!node&&!value.nav)rows.current.delete(job.jobId);}} navRef={(node:StoreDigitalNav|null)=>{const value=target(job.jobId);value.nav=node;if(!node&&!value.element)rows.current.delete(job.jobId);}} data-store-job={job.jobId} className="ph-sd-download" flow-children="column" focusable noFocusRing role="button" aria-label={`${storeDigitalDisplayTitle(job)} · ${state}`} onFocusWithin={(focused:boolean)=>{if(focused)memory.current={jobId:job.jobId,index};}} onActivate={()=>open(job)} onClick={()=>open(job)}>
   <div className="ph-sd-download-head"><div><h3>{storeDigitalDisplayTitle(job)}</h3><div className="ph-sd-muted">{digitalStoreName(job.key.store)}</div></div><div className="ph-sd-download-state" data-state={job.state}><Icon/><span>{state}</span></div></div><progress className="ph-sd-progress" max={100} value={progress==null?undefined:Math.max(0,Math.min(100,progress))}/><div className="ph-sd-muted">{`${storeBytes(job.downloadedBytes)} / ${storeBytes(job.totalBytes)}${!complete&&!canceled&&!queued&&job.bytesPerSecond!=null?` · ${storeBytes(job.bytesPerSecond)}/s`:""}`}</div>{runtimeError&&<div className="ph-sd-muted" role={failed?"alert":"status"}>{runtimeError}</div>}
  </Focusable>;
 })}</Focusable>}</>;
}
