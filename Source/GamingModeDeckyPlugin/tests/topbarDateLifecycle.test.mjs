import {test} from 'node:test';
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import vm from 'node:vm';
const py=`import ast,json\np='main.py'\nt=ast.parse(open(p,encoding='utf-8-sig').read())\nf=next(n for c in t.body if isinstance(c,ast.ClassDef) for n in c.body if isinstance(n,ast.FunctionDef) and n.name=='_topbar_date_script')\nm=ast.Module(body=[f],type_ignores=[])\ng={'json':json,'_TOPBAR_DATE_BADGE_ID':'playhub-topbar-date','_TOPBAR_DATE_STYLE_ID':'playhub-topbar-date-style','_TOPBAR_CLOCK_SELECTORS':['clock']}\nexec(compile(m,p,'exec'),g)\nprint(g['_topbar_date_script'](None,True,'iso',False))`;
const script=execFileSync('python',['-c',py],{cwd:new URL('..',import.meta.url),encoding:'utf8'});
function fixture(title='Steam Big Picture Mode'){
 let clock;const nodes=[];const timers=new Map();let serial=0;const listeners=new Map();const observers=[];
 class Element{
  constructor(){this.id='';this.childNodes=[];this.parentNode=null;this.attrs=new Set();this._text='';nodes.push(this)}
  get textContent(){return this._text}set textContent(v){this._text=v;this.childNodes.forEach(c=>c.parentNode=null);this.childNodes=[]}
  get nextSibling(){const c=this.parentNode?.childNodes||[];return c[c.indexOf(this)+1]||null}
  appendChild(n){this.insertBefore(n,null)}insertBefore(n,b){n.remove();n.parentNode=this;const i=this.childNodes.indexOf(b);this.childNodes.splice(i<0?this.childNodes.length:i,0,n)}
  remove(){if(this.parentNode)this.parentNode.childNodes=this.parentNode.childNodes.filter(n=>n!==this);this.parentNode=null}
  setAttribute(k){this.attrs.add(k)}removeAttribute(k){this.attrs.delete(k)}hasAttribute(k){return this.attrs.has(k)}toggleAttribute(k,v){v?this.attrs.add(k):this.attrs.delete(k)}
  querySelector(s){return this.childNodes.find(n=>'#'+n.id===s)||null}querySelectorAll(){return this.childNodes}
 }
 const html=new Element(),head=new Element();html.appendChild(head);clock=new Element();html.appendChild(clock);
 const connected=n=>n===html||n.parentNode&&connected(n.parentNode);
 const document={title,documentElement:html,head,createElement:()=>new Element(),getElementById:id=>nodes.find(n=>n.id===id&&connected(n))||null,querySelector:s=>s==='clock'?clock:null,addEventListener:(k,f)=>listeners.set(k,f),removeEventListener:k=>listeners.delete(k)};
 const window={setTimeout:f=>{timers.set(++serial,f);return serial},clearTimeout:id=>timers.delete(id),setInterval:f=>{timers.set(++serial,f);return serial},clearInterval:id=>timers.delete(id),addEventListener:(k,f)=>listeners.set(k,f),removeEventListener:k=>listeners.delete(k)};
 class Observer{constructor(f){this.f=f;this.active=true;observers.push(this)}observe(){}disconnect(){this.active=false}}
 const context=vm.createContext({window,document,MutationObserver:Observer,navigator:{language:'en'},Intl,Date});
 const run=()=>vm.runInContext(script,context);
 const flush=()=>{observers.filter(o=>o.active).forEach(o=>o.f());for(const [id,f]of [...timers]){if(id>1)timers.delete(id);f()}};
 return{run,window,document,timers,listeners,observers,flush,get clock(){return clock},navigate(){clock.remove();clock=new Element();html.appendChild(clock)},rerender(){clock.textContent='12:34'}};
}
test('reinjection reuses one badge observer and minute timer',()=>{const f=fixture();f.run();const badge=f.document.getElementById('playhub-topbar-date');f.run();assert.equal(f.document.getElementById(badge.id),badge);assert.equal(f.observers.length,1);assert.equal(f.timers.size,1)});
test('React clock rerender reattaches same badge',()=>{const f=fixture();f.run();const badge=f.document.getElementById('playhub-topbar-date');f.rerender();f.flush();assert.equal(f.document.getElementById(badge.id),badge);assert.equal(badge.parentNode,f.clock)});
test('navigation moves same badge to new clock',()=>{const f=fixture();f.run();const badge=f.document.getElementById('playhub-topbar-date');f.navigate();f.flush();assert.equal(badge.parentNode,f.clock)});
test('disable cleans observer timers listeners and badge',()=>{const f=fixture();f.run();f.window.__playhubTopbarDateRuntime.update({enabled:false,moveLeft:false,badge:'playhub-topbar-date',style:'playhub-topbar-date-style'});assert.equal(f.document.getElementById('playhub-topbar-date'),null);assert.equal(f.observers[0].active,false);assert.equal(f.timers.size,0);assert.equal(f.listeners.size,0)});

test('in-game QuickAccess header keeps clock and weather but never inserts the date',()=>{
 const f=fixture('QuickAccess_uid21'); const weather=f.document.createElement('span');weather.id='decky-weather-topbar-badge';f.clock.appendChild(weather);
 f.run();f.flush();f.run();
 assert.equal(f.document.getElementById('playhub-topbar-date'),null);
 assert.equal(weather.parentNode,f.clock);assert.equal(f.clock.childNodes.length,1);
 assert.equal(f.observers.length,1);assert.equal(f.timers.size,1);
});
test('actual Steam overlay and main header retain date and unchanged reinjection ownership',()=>{
 for(const title of ['SPOverlay', 'Steam Big Picture Mode','Modalità Big Picture di Steam','QuickAccessSettings']){
  const f=fixture(title);f.run();const badge=f.document.getElementById('playhub-topbar-date');assert.ok(badge,title);
  f.run();assert.equal(f.document.getElementById(badge.id),badge);
 }
});
test('positive QAM context removes only date and returning to overlay reuses its badge',()=>{
 const f=fixture('SPOverlay');f.run();const badge=f.document.getElementById('playhub-topbar-date');
 f.document.title='QuickAccess_uid22';f.flush();assert.equal(f.document.getElementById(badge.id),null);
 f.document.title='SPOverlay';f.flush();assert.equal(f.document.getElementById(badge.id),badge);
});
