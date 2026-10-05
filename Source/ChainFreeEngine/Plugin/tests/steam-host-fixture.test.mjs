import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fixture } from './fixtures/react-model.mjs';
import * as refresh from '../src/react19Refresh.mjs';
import * as core from '../src/steamHostCore.mjs';
const require = createRequire(new URL('../../../GamingModeDeckyPlugin/package.json', import.meta.url));
let ts;
try { ts = require('typescript'); }
catch { ts = require(path.join(execFileSync('npm', ['root', '-g'], { encoding: 'utf8' }).trim(), 'typescript')); }
const js = ts.transpileModule(fs.readFileSync(new URL('../src/steamHost.ts', import.meta.url), 'utf8'), {
  compilerOptions: { target: ts.ScriptTarget.ES2021, module: ts.ModuleKind.CommonJS },
}).outputText;
function mountedHost(nativeKeys = [0, 3, 4, 5, 7, 6]) {
  const f = fixture();
  const elementTag = Symbol('element');
  const React = { version: '19.1.1', Fragment: Symbol('fragment'),
    createElement: (type, props, ...children) => ({ $$typeof: elementTag, type, key: props?.key,
      props: children.length ? { ...props, children: children.length === 1 ? children[0] : children } : { ...props } }),
    isValidElement: value => value?.$$typeof === elementTag,
    cloneElement: (value, patch) => ({ ...value, props: { ...value.props, ...patch } }),
  };
  function QuickAccessMenuBrowserView(props) { return React.createElement('NativeTabs', { tabs: props.nativeTabs }); }
  const memo = { $$typeof: Symbol('memo'), type: QuickAccessMenuBrowserView };
  const nativeTabs = Object.freeze(nativeKeys.map(key => ({ key })));
  f.target.type = QuickAccessMenuBrowserView;
  f.target.elementType = memo;
  f.target.memoizedProps = f.target.pendingProps = { nativeTabs };
  f.target.alternate.type = QuickAccessMenuBrowserView;
  f.target.alternate.elementType = memo;
  f.target.onCommit = out => {
    f.target.child = { tag: 0, type: 'NativeTabs', return: f.target, memoizedProps: out.props, pendingProps: out.props, lanes: 0, childLanes: 0 };
  };
  f.target.onCommit(QuickAccessMenuBrowserView(f.target.pendingProps));
  const element = { '__reactContainer$fixture': f.root };
  const document = { getElementById: () => element, body: { children: [] }, documentElement: {} };
  const tabEnum = { Notifications: 0, Settings: 6, 0: 'Notifications', 6: 'Settings' };
  let foreign = false;
  const host = { React, ReactDOM: f.ReactDOM, targetDocument: document, diagnostics: {},
    moduleExports: [memo, tabEnum], hasForeignRenderer: () => foreign };
  const window = { __PLAYHUB_HOST__: host };
  const timers = new Map(); let sequence = 0;
  const exports = {};
  vm.runInNewContext(js, { exports, module: { exports }, window, document, console,
    setTimeout(fn) { const id = ++sequence; timers.set(id, fn); return id; },
    clearTimeout(id) { timers.delete(id); },
    require(name) {
      if (name === './steamUi') return { DFL: { QuickAccessTab: tabEnum, getReactRoot: () => f.root } };
      if (name === './steamHostCore.mjs') return core;
      if (name === './react19Refresh.mjs') return refresh;
      throw new Error(name);
    },
  });
  return { ...f, window, host, memo, tabEnum, exports, nativeTabs,
    foreign: value => { foreign = value; },
    tick() { const entry = timers.entries().next().value; if (!entry) return; timers.delete(entry[0]); entry[1](); },
    pending: () => timers.size,
    tabs: () => f.target.child.memoizedProps.tabs,
    original: QuickAccessMenuBrowserView,
  };
}
test('steamHost late attach: passa da attached/false a lista committed con una tab', () => {
  const f = mountedHost(); const panel = {};
  const qam = f.exports.installQamTab({ content: panel });
  assert.equal(qam.inserted, false); f.tick();
  assert.equal(qam.attached, true); assert.equal(qam.inserted, true);
  assert.equal(JSON.stringify(f.tabs().map(x => x.key)), '[0,3,4,5,5261387,7,6]');
  assert.equal(f.tabs()[4].panel, panel);
  assert.equal(qam.diagnostics.refresh.reason, 'sync-lane-scheduled');
  assert.equal(f.target.type, f.memo.type); assert.equal(f.target.alternate.type, f.memo.type);
  qam.uninstall();
});
test('un secondo render non duplica la tab e conserva l’identita’ del pannello', () => {
  const f = mountedHost(); const qam = f.exports.installQamTab({ content: {} }); f.tick();
  const tab = f.tabs()[4];
  refresh.refreshFiber(f.target, f.ReactDOM, '19.1.1');
  assert.equal(f.tabs().filter(x => x.key === 5261387).length, 1);
  assert.equal(f.tabs()[4], tab);
  assert.equal(f.nativeTabs.length, 6);
  qam.uninstall();
});
test('uninstall ripristina memo e fiber, rimuove solo la tab propria e cancella il timer', () => {
  const f = mountedHost(); const qam = f.exports.installQamTab({ content: {} }); f.tick();
  qam.uninstall(); qam.uninstall();
  assert.equal(qam.attached, false); assert.equal(qam.inserted, false);
  assert.equal(f.memo.type, f.original); assert.equal(f.target.type, f.original);
  assert.equal(f.tabs().length, 6); assert.equal(f.pending(), 0);
  assert.equal(f.tabEnum[5261387], undefined);
});
test('la stessa chiave di un’altra scheda non autorizza modifiche al suo setter', () => {
  const f = mountedHost([0, 3, 4, 5, 5261387, 7, 6]);
  let calls = 0; const foreign = f.nativeTabs[4]; foreign.qAMVisibilitySetter = () => calls++;
  const qam = f.exports.installQamTab({ content: {} }); f.tick();
  assert.equal(qam.inserted, false); assert.equal(qam.diagnostics.collision, true);
  qam.setVisible(false); qam.uninstall();
  assert.equal(calls, 0); assert.equal(f.tabs()[4], foreign);
});
test('una presa di possesso successiva non rimuove l’enum condiviso o la patch altrui', () => {
  const f = mountedHost(); const qam = f.exports.installQamTab({ content: {} }); f.tick();
  const foreignPatch = () => null; f.memo.type = foreignPatch; f.foreign(true); f.tick();
  assert.equal(qam.attached, false); assert.equal(f.memo.type, foreignPatch);
  assert.equal(f.tabEnum[5261387], 'Playhub'); assert.equal(f.pending(), 0);
});
test('Decky presente all’inizio impedisce la modifica della view e dell’enum', () => {
  const f = mountedHost(); f.foreign(true);
  const qam = f.exports.installQamTab({ content: {} }); f.tick();
  assert.equal(qam.attached, false); assert.equal(f.memo.type, f.original);
  assert.equal(f.tabEnum[5261387], undefined);
});
test('setVisible invoca soltanto il setter della scheda propria fuori dal render', () => {
  const f = mountedHost(); const qam = f.exports.installQamTab({ content: {} }); f.tick();
  let calls = 0; f.tabs()[4].qAMVisibilitySetter = value => { assert.equal(value, false); calls++; };
  qam.setVisible(false);
  refresh.refreshFiber(f.target, f.ReactDOM, '19.1.1');
  assert.equal(calls, 1); qam.uninstall();
});
