import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createRequire} from 'node:module';
import vm from 'node:vm';
const require=createRequire(new URL('../package.json',import.meta.url)),ts=require('typescript');
const compile=source=>ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
function load(path,modules={}){const exports={};vm.runInNewContext(compile(readFileSync(path,'utf8')),{exports,require:name=>modules[name],navigator:{language:'it-IT'},console});return exports;}
const pure=load(new URL('../src/completionNotifications.ts',import.meta.url));
const job=(id='job',completed=false)=>({id,identity:'epic:game-'+id,title:'Gioco '+id,completed});
function memory(){let value=null;return{getItem:()=>value,setItem:(_,next)=>{value=next;},get value(){return value;}};}

test('first historical completions stay quiet; subsequent verified completions notify once across reload',()=>{
 const disk=memory(),sent=[];let observe=pure.createCompletionObserver('test',row=>sent.push(row),disk);
 observe(1,[job('history',true),job()]);assert.equal(sent.length,0);
 observe(2,[job('history',true),job('job',true)]);observe(2,[job('job',true)]);assert.equal(sent.length,1);
 observe=pure.createCompletionObserver('test',row=>sent.push(row),disk);observe(3,[job('history',true),job('job',true)]);assert.equal(sent.length,1);
});
test('a persisted active job can complete while Steam restarts without replaying unknown old history',()=>{
 const disk=memory(),sent=[];pure.createCompletionObserver('test',row=>sent.push(row),disk)(5,[job()]);
 const restarted=pure.createCompletionObserver('test',row=>sent.push(row),disk);restarted(6,[job('job',true),job('unknown-old',true)]);
 assert.deepEqual(sent.map(row=>row.id),['job']);
});
test('invalid, stale and replaced identities never report a successful completion',()=>{
 const sent=[],observe=pure.createCompletionObserver('test',row=>sent.push(row),memory());observe(5,[job()]);
 observe(4,[job('job',true)]);observe(6,[{...job('job',true),identity:'gog:foreign'}]);observe(7,[{...job('other',true),title:''}]);
 assert.equal(sent.length,0);
});
test('missing storage or unavailable native toast cannot fail the download poll or replay attempts',()=>{
 const disk={getItem(){throw Error('unavailable');},setItem(){throw Error('unavailable');}};let attempts=0;
 const observe=pure.createCompletionObserver('test',()=>{attempts++;throw Error('native toast unavailable');},disk);
 assert.doesNotThrow(()=>{observe(1,[job()]);observe(2,[job('job',true)]);observe(3,[job('job',true)]);});assert.equal(attempts,1);
});
test('store native toast uses verified host Completed and opens Downloads only on activation',()=>{
 const sent=[];let opened=0;
 const adapter=load(new URL('../src/storeCompletionNotifications.ts',import.meta.url),{'./completionNotifications':pure,'./decky':{toaster:{toast:data=>sent.push(data)}},'./StoreDigitalNavigation':{openStoreDigitalDownloads:()=>opened++}});
 const observe=adapter.createStoreCompletionNotifications(),row={jobId:'store-test',key:{store:'gog',id:'123'},title:'Tyrian 2000',state:'downloading'};
 observe([row]);observe([{...row,state:'externalHandoff'}]);assert.equal(sent.length,0);
 observe([{...row,state:'completed'}]);assert.equal(sent.length,1);assert.equal(sent[0].title,'Installazione completata');assert.equal(sent[0].body,'Tyrian 2000');assert.equal(opened,0);
 sent[0].onClick();assert.equal(opened,1);
});
