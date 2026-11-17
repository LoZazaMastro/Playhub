// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
export type CompletionJob={id:string;identity:string;title:string;completed:boolean};
export type CompletionStorage={getItem(key:string):string|null;setItem(key:string,value:string):void};
type Receipt={identity:string;done:boolean};
export function createCompletionObserver(key:string,notify:(job:CompletionJob)=>void,provided?:CompletionStorage){
 let storage=provided;
 if(!storage)try{storage=globalThis.localStorage;}catch{}
 const receipts=new Map<string,Receipt>();
 try{
  const raw=storage?.getItem(key);
  if(raw&&raw.length<=2*1024*1024){const disk=JSON.parse(raw);if(disk?.version===1&&Array.isArray(disk.rows)&&disk.rows.length<=16384)
   for(const row of disk.rows)if(Array.isArray(row)&&typeof row[0]==="string"&&row[0].length>0&&row[0].length<=128&&typeof row[1]==="string"&&row[1].length<=2048&&typeof row[2]==="boolean")receipts.set(row[0],{identity:row[1],done:row[2]});}
 }catch{}
 let initialized=false,lastRevision=-1;
 return(revision:number,jobs:readonly CompletionJob[])=>{
  if(!Number.isSafeInteger(revision)||revision<0||revision<lastRevision||!Array.isArray(jobs)||jobs.length>10000
    ||jobs.some(job=>!job||typeof job.id!=="string"||!job.id||job.id.length>128||typeof job.identity!=="string"||!job.identity||job.identity.length>2048||typeof job.title!=="string"||!job.title.trim()||job.title.length>2048||typeof job.completed!=="boolean")
    ||new Set(jobs.map(job=>job.id)).size!==jobs.length)return;
  const first=!initialized;initialized=true;lastRevision=revision;
  const ready:CompletionJob[]=[];let changed=false;
  for(const job of jobs){
   const prior=receipts.get(job.id),same=prior?.identity===job.identity;
   if(same&&prior!.done)continue;
   // The first full snapshot is a baseline. Only a previously persisted active
   // job can complete across restart; unknown historical completions stay quiet.
   if(job.completed&&(!first||same)&&(!prior||same))ready.push(job);
   if(!same||job.completed){receipts.set(job.id,{identity:job.identity,done:job.completed});changed=true;}
  }
  if(changed){
   while(receipts.size>16384)receipts.delete(receipts.keys().next().value!);
   // Persist BEFORE native delivery: a reload can never replay the same toast.
   try{storage?.setItem(key,JSON.stringify({version:1,rows:[...receipts].map(([id,row])=>[id,row.identity,row.done])}));}catch{}
  }
  for(const job of ready)try{notify(job);}catch{} // Toast availability cannot break downloads.
 };
}
const wording:Record<string,[string,string]>={en:["Download completed","Installation completed"],it:["Download completato","Installazione completata"],de:["Download abgeschlossen","Installation abgeschlossen"],es:["Descarga completada","Instalación completada"],fr:["Téléchargement terminé","Installation terminée"],pt:["Transferência concluída","Instalação concluída"],uk:["Завантаження завершено","Встановлення завершено"],ru:["Загрузка завершена","Установка завершена"],zh:["下载完成","安装完成"],ja:["ダウンロード完了","インストール完了"],ko:["다운로드 완료","설치 완료"],hi:["डाउनलोड पूरा हुआ","स्थापना पूरी हुई"]};
export const completionTitle=(locale:string,kind:"download"|"install")=>(wording[locale.toLowerCase().split(/[-_]/)[0]]??wording.en)[kind==="install"?1:0];
