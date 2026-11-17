import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import test from 'node:test';
import vm from 'node:vm';
const require = createRequire(new URL('../package.json', import.meta.url));
const ts = require('typescript');
const source = process.env.STORE_DOWNLOAD_MENU_SOURCE ?? new URL('../src/storeDownloadMenu.tsx', import.meta.url);
function fixture() {
  const modals = [], commands = [], alerts = [];
  let jobs = [];
  const React = { createElement: (type,props,...children)=>({type,props:props??{},children}), useState: initial=>[initial,()=>{}], useSyncExternalStore: (_subscribe,read)=>read() };
  const dfl = Object.fromEntries(['PanelSection','PanelSectionRow','DropdownItem','ConfirmModal'].map(k=>[k,k]));
  dfl.showModal = value=>modals.push(value);
  const exports = {};
  const modules = {
    './decky': { DFL:dfl, SP_REACT:React, toaster:{toast:value=>alerts.push(value)} },
    './storeLibraryRuntime': { readStoreDownloads:()=>jobs, subscribeStoreLibrary:()=>()=>{}, storeJobAction:async(job,action)=>{commands.push({job,action});return{accepted:true};} },
    './storeLibraryLocale': { storeLibraryLocale:()=>new Proxy({}, {get:(_o,key)=>String(key)}) },
  };
  vm.runInNewContext(ts.transpileModule(readFileSync(source,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.React}}).outputText,{exports,require:key=>modules[key]});
  return {exports,modals,commands,alerts,setJobs:value=>{jobs=value;}};
}
const job = {jobId:'job-1',key:{store:'epic',id:'game-1'},accountRevision:3,title:'A real game title',state:'downloading',revision:5,installPath:'D:\\Games\\One',canCancel:true,canRemove:true,removalKind:"cleanup",canPause:true,canResume:false,canDownloadNow:false};
test('cancel opens a destructive native Yes/No dialog without issuing a command',async()=>{
  const f=fixture(), menu=f.exports.StoreDownloadMenu({job,locale:'en'});
  menu.props.onChange({data:'cancel'});
  assert.equal(f.commands.length,0); assert.equal(f.modals.length,1);
  const dialog=f.modals[0]; assert.equal(dialog.type,'ConfirmModal'); assert.equal(dialog.props.bDestructiveWarning,true);
  assert.equal(dialog.props.focusButton,'secondary','native Cancel/No receives the initial focus');
  assert.equal(dialog.props.strTitle,'StoreLibrary.ConfirmCancelDownload'); assert.equal(dialog.props.strDescription,job.title);
  assert.equal(dialog.props.strOKButtonText,'StoreLibrary.Yes'); assert.equal(dialog.props.strCancelButtonText,'StoreLibrary.No');
  dialog.props.onCancel(); await Promise.resolve(); assert.equal(f.commands.length,0);
});
test('only explicit Yes requests cancellation of the selected job',async()=>{
  const f=fixture(), menu=f.exports.StoreDownloadMenu({job,locale:'it'});
  menu.props.onChange({data:'cancel'}); f.modals[0].props.onOK(); await Promise.resolve();
  assert.equal(f.commands.length,1); assert.equal(f.commands[0].job,job); assert.equal(f.commands[0].action,'cancel');
});
test('menu rejects actions not offered by current provider capabilities',async()=>{
  const f=fixture(), menu=f.exports.StoreDownloadMenu({job:{...job,canCancel:false,canRemove:false},locale:'en'});
  for(const data of ['cancel','resume','downloadNow','unexpected']) menu.props.onChange({data});
  await Promise.resolve(); assert.equal(f.modals.length,0); assert.equal(f.commands.length,0);
  menu.props.onChange({data:'pause'}); await Promise.resolve(); assert.equal(f.commands[0].action,'pause');
});
test('QAM keeps paused and queued jobs visible and hides an empty queue',()=>{
  const f=fixture(); assert.equal(f.exports.StoreDownloadsQam({locale:'en'}),null);
  f.setJobs([{...job,state:'pausedByUser'},{...job,jobId:'job-2',state:'queued'},{...job,jobId:'job-3',state:'completed'}]);
  const section=f.exports.StoreDownloadsQam({locale:'en'}); assert.equal(section.type,'PanelSection');
  assert.equal(section.children[0].length,2); assert.equal(section.children[0][0].props.key,'job-1');
});

test('only the cancel option receives the native option class and scoped red background/white text',()=>{
  const f=fixture(),menu=f.exports.StoreDownloadMenu({job,locale:'en'}),options=menu.props.rgOptions;
  const cancel=options.find(option=>option.data==='cancel');
  assert.equal(cancel.strOptionClass,'ph-store-download-cancel');
  assert.equal(options.filter(option=>option.data!=='cancel').some(option=>option.strOptionClass),false);
  const css=cancel.label.children[0].children[0];
  assert.match(css,/\.ph-store-download-cancel:is\(:hover,:focus-visible,:focus-within,\.gpfocus,\.gpfocuswithin,\[aria-selected=true\]\)/);
  assert.match(css,/background:#c42b1c!important;color:#fff!important/);
  assert.doesNotMatch(css,/:has\(|\[role=option\]|\[role=menuitem\]/);
});

const descendants = tree => !tree || typeof tree !== 'object' ? [] : [tree, ...(tree.children ?? []).flat(Infinity).flatMap(descendants)];
test('native Xbox active options stay accessible and explicit confirmation cancels the exact selected job',async()=>{
  const f=fixture(), native={...job,key:{store:'xbox',id:'9NATIVE'},canPause:false,canResume:false,canDownloadNow:false,canCancel:true,canRemove:true,removalKind:'history',reasonCode:'windows_store_managed'};
  const menu=f.exports.StoreDownloadMenu({job:native,locale:'it'});
  assert.equal(menu.props.disabled,false);
  assert.deepEqual(Array.from(menu.props.rgOptions,option=>option.data),['cancel']);
  menu.props.onChange({data:'cancel'});assert.equal(f.commands.length,0);
  f.modals[0].props.onOK();await Promise.resolve();assert.equal(f.commands[0].job,native);
  assert.equal(f.commands[0].action,'cancel');
});
test('Xbox status-only rows retain removal options and their progress in QAM',()=>{
  const f=fixture(), native={...job,state:'externalHandoff',reasonCode:'windows_store_managed',canPause:false,canCancel:false,canRemove:true,removalKind:'windowsRequest'};
  assert.equal(f.exports.StoreDownloadMenu({job:native,locale:'en'}).props.disabled,false);
  f.setJobs([native]);const row=f.exports.StoreDownloadsQam({locale:'en'}).children[0][0];
  const nodes=descendants(row);
  assert.ok(nodes.some(node=>node.type===f.exports.StoreDownloadMenu));
  assert.ok(nodes.some(node=>node.type===f.exports.StoreDownloadProgress));
});
test('real Windows progress renders its measured fraction and native bytes/speed',()=>{
  const f=fixture(), native={...job,key:{store:'xbox',id:'9NATIVE'},progressPercent:37.25,downloadedBytes:3*1024**3,totalBytes:8*1024**3,bytesPerSecond:12*1024**2,reasonCode:'windows_store_managed'};
  const nodes=descendants(f.exports.StoreDownloadProgress({job:native,locale:'en'}));
  const bar=nodes.find(node=>node.props.role==='progressbar');assert.equal(bar.props['aria-valuenow'],37.25);
  assert.equal(nodes.find(node=>node.props.className==='ph-store-download-progress-fill').props.style.width,'37.25%');
  const values=nodes.flatMap(node=>node.children).filter(value=>typeof value==='string');
  assert.ok(values.includes('3 GB / 8 GB'));assert.ok(values.includes('12 MB/s'));
});
test('byte ratio is used when no native percentage is supplied; queued and unknown rows show an indeterminate track',()=>{
  const f=fixture();
  const fraction=descendants(f.exports.StoreDownloadProgress({job:{...job,downloadedBytes:2,totalBytes:8},locale:'en'})).find(node=>node.props.role==='progressbar');
  assert.equal(fraction.props['aria-valuenow'],25);
  for(const next of [{...job,state:'queued',downloadedBytes:0,totalBytes:8,progressPercent:0},{...job,downloadedBytes:0}]){
    const nodes=descendants(f.exports.StoreDownloadProgress({job:next,locale:'en'}));
    assert.equal(nodes.find(node=>node.props.role==='progressbar').props['aria-valuenow'],undefined);
    assert.ok(nodes.some(node=>node.props.className==='ph-store-download-progress-pending'));
  }
});
test('progress remains bounded and invalid telemetry never renders a fictitious percentage or speed',()=>{
  const f=fixture();
  const clipped=descendants(f.exports.StoreDownloadProgress({job:{...job,progressPercent:120},locale:'en'}));
  assert.equal(clipped.find(node=>node.props.role==='progressbar').props['aria-valuenow'],100);
  const invalid=descendants(f.exports.StoreDownloadProgress({job:{...job,progressPercent:NaN,downloadedBytes:NaN,totalBytes:Infinity,bytesPerSecond:-1},locale:'en'}));
  assert.equal(invalid.find(node=>node.props.role==='progressbar').props['aria-valuenow'],undefined);
  assert.ok(invalid.some(node=>node.props.className==='ph-store-download-progress-pending'));
  assert.ok(!invalid.filter(node=>node.type!=='style').flatMap(node=>node.children).some(value=>typeof value==='string'&&/NaN|Infinity|-1/.test(value)));
});
