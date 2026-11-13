import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const source=ts.transpileModule(readFileSync(new URL("../src/steamModeCommands.ts",import.meta.url),"utf8"),{
 compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022},
}).outputText;
const exports={};vm.runInNewContext(source,{exports});
const deferred=()=>{let resolve;const promise=new Promise(done=>{resolve=done;});return{promise,resolve};};
const settle=()=>new Promise(done=>setImmediate(done));
const command=(revision,kind="exit")=>({revision,kind,token:revision.toString(16).padStart(32,"0")});
function fixture(host={}){
 let alive=true,now=0,desktop=true,read;
 const calls=[],acks=[];
 const client=exports.installSteamModeCommands(host,{
  alive:()=>alive,exit(){calls.push("exit");desktop=false;},
  readDesktop:()=>read?read():Promise.resolve(desktop),
  ack:(cmd,ok)=>{acks.push([cmd.revision,ok]);return Promise.resolve(true);},
  now:()=>now,delay:async ms=>{now+=ms;},
 });
 return{host,client,calls,acks,setAlive:value=>{alive=value;},setDesktop:value=>{desktop=value;},setRead:fn=>{read=fn;}};
}

test("a delayed consumed exit cannot run after the newer open barrier was acknowledged",async()=>{
 const f=fixture(),old=command(100),barrier=command(101,"barrier");
 await f.client.process(barrier);await f.client.process(old);
 assert.deepEqual(f.calls,[]);assert.deepEqual(f.acks,[[101,true]]);
});

test("explicit Big Picture waits for an exit already submitted before its barrier",async()=>{
 const f=fixture(),desktop=deferred();f.setRead(()=>desktop.promise);
 const exit=f.client.process(command(100));await settle();assert.deepEqual(f.calls,["exit"]);
 const barrier=f.client.process(command(101,"barrier"));await settle();
 assert.deepEqual(f.acks,[],"native open must not receive an early acknowledgement");
 desktop.resolve(true);await exit;await barrier;
 assert.deepEqual(f.acks,[[100,true],[101,true]]);
});

test("duplicate polls share one desktop command and repeat only its acknowledgement",async()=>{
 const f=fixture();f.setRead(()=>Promise.resolve(true));
 await Promise.all([f.client.process(command(100)),f.client.process(command(100))]);
 assert.deepEqual(f.calls,["exit"]);assert.deepEqual(f.acks,[[100,true],[100,true]]);
});

test("the same command revision cannot be reused with a different token or action",async()=>{
 const f=fixture();await f.client.process(command(100,"barrier"));
 await f.client.process({...command(100),token:"f".repeat(32)});await f.client.process(command(100));
 assert.deepEqual(f.calls,[]);assert.deepEqual(f.acks,[[100,true]]);
});

test("a new plugin root shares the old root's pending exit rather than acknowledging ahead of it",async()=>{
 const host={},old=fixture(host),desktop=deferred();old.setRead(()=>desktop.promise);
 const first=old.client.process(command(100));await settle();old.setAlive(false);
 const next=fixture(host);next.setRead(()=>Promise.resolve(true));
 const barrier=next.client.process(command(101,"barrier"));await settle();assert.deepEqual(next.acks,[]);
 desktop.resolve(false);await first;await barrier;
 assert.deepEqual(next.acks,[[101,true]]);assert.deepEqual(next.calls,[]);
});

test("an exit whose readback remains unknown cannot release the native open barrier",async()=>{
 const f=fixture();f.setRead(()=>Promise.resolve(false));
 await f.client.process(command(100));await f.client.process(command(101,"barrier"));
 assert.deepEqual(f.acks,[[100,false],[101,false]]);
 assert.deepEqual(f.calls,["exit"]);
});

test("unmounted roots and malformed or conflicting commands never call the native exit",async()=>{
 const f=fixture();f.setAlive(false);await f.client.process(command(100));
 f.setAlive(true);
 for(const value of [null,{},command(-1),command(1.5),{...command(200),token:"bad"},{...command(200),kind:"other"}])
  await f.client.process(value);
 assert.deepEqual(f.calls,[]);assert.deepEqual(f.acks,[]);
});

test("a queued exit superseded before execution is discarded without closing Big Picture",async()=>{
 const f=fixture();const exit=f.client.process(command(100));const barrier=f.client.process(command(101,"barrier"));
 await Promise.all([exit,barrier]);assert.deepEqual(f.calls,[]);assert.deepEqual(f.acks,[[101,true]]);
});

test("a barrier can recover after an earlier failed readback once Steam confirms desktop mode",async()=>{
 const f=fixture();f.setRead(()=>Promise.resolve(false));await f.client.process(command(100));
 await f.client.process(command(101,"barrier"));f.setRead(()=>Promise.resolve(true));await f.client.process(command(102,"barrier"));
 assert.deepEqual(f.calls,["exit"]);assert.deepEqual(f.acks,[[100,false],[101,false],[102,true]]);
});

test("a pending desktop exit retries native readback without dispatching Exit a second time",async()=>{
 const f=fixture();f.setRead(()=>Promise.resolve(false));
 await f.client.process(command(100));assert.deepEqual(f.acks,[[100,false]]);
 f.setRead(()=>Promise.resolve(true));await f.client.process(command(100));
 assert.deepEqual(f.calls,["exit"]);assert.deepEqual(f.acks,[[100,false],[100,true]]);
});

test("the original queued open barrier recovers from a transient false observation",async()=>{
 const f=fixture();f.setRead(()=>Promise.resolve(false));await f.client.process(command(100));
 const open=command(101,"barrier");await f.client.process(open);
 assert.deepEqual(f.acks,[[100,false],[101,false]]);
 f.setRead(()=>Promise.resolve(true));await f.client.process(open);
 assert.deepEqual(f.calls,["exit"]);assert.deepEqual(f.acks,[[100,false],[101,false],[101,true]]);
});

test("root replacement after an unconfirmed native exit preserves dispatch and retries only observation",async()=>{
 const host={},old=fixture(host);old.setRead(()=>Promise.resolve(false));
 await old.client.process(command(100));old.setAlive(false);
 const next=fixture(host);next.setRead(()=>Promise.resolve(true));
 await next.client.process(command(100));await next.client.process(command(101,"barrier"));
 assert.deepEqual(old.calls,["exit"]);assert.deepEqual(next.calls,[]);
 assert.deepEqual(next.acks,[[100,true],[101,true]]);
});
