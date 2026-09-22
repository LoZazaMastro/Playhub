import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

// Opt-in local contract test: reads installed source without importing its plugin.
// No third-party bundle is copied into this repository or executed at top level.
const bundlePath = process.env.PLAYHUB_SHORTCUTS_BUNDLE;
const hookUrl = process.env.PLAYHUB_STEAM_HOOK_URL;
test("installed Shortcuts renderer preserves ownership, refresh and detach with native Decky identity", {
  skip: !bundlePath || !hookUrl,
}, async () => {
  const source = ts.createSourceFile("shortcuts.js", readFileSync(bundlePath, "utf8"), ts.ScriptTarget.Latest, true);
  const functions = source.statements.filter(ts.isFunctionDeclaration)
    .filter(node => !node.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.ExportKeyword))
    .map(node => node.getText(source)).join("\n");
  const constants = source.statements.filter(ts.isVariableStatement).filter(node =>
    node.declarationList.declarations.every(declaration => /^(OWNER|OWNER_FIELD|PLUGIN_FIELD|DECKY_TAB_ID|PDC_TAB_ID|PDC_OWNER|SHARED_ADAPTER|SHARED_ADAPTER_PROTOCOL|SHORTCUTS_ADAPTER_OWNER|TAB_LAYOUT_ADAPTER|STEAM_TAB_PREFIX|DECKY_TAB_PREFIX|SHORTCUT_TAB_PREFIX)$/.test(declaration.name.getText(source)))
  ).map(node => node.getText(source)).join("\n");
  const response = await fetch(hookUrl);
  assert.equal(response.ok, true);
  const steam = ts.createSourceFile("steam.js", await response.text(), ts.ScriptTarget.Latest, true);
  const hook = steam.statements.find(node => ts.isClassDeclaration(node) && node.name?.text === "TabsHook");
  assert.ok(hook, "actual Steam-served Decky TabsHook must be present");
  const context = vm.createContext({ WeakRef, setInterval: () => 1, clearInterval() {},
    queueMicrotask, window: {}, NATIVE_TAB_LABELS: { en: {} }, Logger$1: class { log() {} debug() {} warn() {} error() {} },
    SP_JSX: { jsx: (type, props) => ({ type, props }) }, ErrorBoundary: "ErrorBoundary",
    QuickAccessVisibleStateProvider: "QuickAccessVisibleStateProvider", exports: {},
  });
  vm.runInContext(`${constants}\n${functions}\n${hook.getText(steam)}\ncurrentLanguage = () => "en";\nthis.api = { TabsHook, installRenderAdapter, installTabLayout, renderAdapterState, tabLayoutState, refreshObservedQam, detachTabLayout, restoreRenderAdapter };`, context);
  const compiled = ts.transpileModule(readFileSync(new URL("../src/deckyHostStandalone.ts", import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  vm.runInContext(compiled, context);
  const api = context.api;
  const nativeHook = new api.TabsHook();
  const root = {};
  nativeHook.tabs = [{ id: 999, content: root }, { id: 1401877092, content: {}, __shortcutsOwner: "shortcuts", __shortcutsPlugin: "Playhub" }];
  const state = api.installRenderAdapter(nativeHook);
  assert.ok(state);
  const layout = api.installTabLayout(state);
  assert.ok(layout);
  const originalWrapper = nativeHook.render;
  const tabs = [{ key: 1 }];
  nativeHook.render(tabs, true);
  assert.equal(state.failure, null);
  const originalDecky = tabs.find(tab => String(tab.key) === "999");
  assert.ok(originalDecky);
  const host = context.exports.createLegacyShortcutsDeckyHost(nativeHook, { protocol: 1, register() {}, getVisible: () => true });
  assert.ok(host);
  const projected = context.exports.getDeckyTabProjection(tabs);
  let mountedKeys = Array.from(projected.getSnapshot(), tab => String(tab.key));
  const unsubscribe = projected.subscribe(() => { mountedKeys = Array.from(projected.getSnapshot(), tab => String(tab.key)); });
  assert.equal(api.renderAdapterState(nativeHook), state);
  assert.equal(api.tabLayoutState(state), layout);
  const lease = host.capability.acquire({ element: { isConnected: true,
    closest: () => ({ id: "quickaccess_content_1401877092" }),
    ownerDocument: { getElementById: () => ({ getAttribute: () => "false" }) },
  }, createContent: native => ({ native }), isHealthy: () => true, onRelease() {} });
  assert.ok(lease);
  assert.equal(lease.setReady(), true, "already-observed arrays must be projected without another native render");
  await Promise.resolve();
  assert.equal(mountedKeys.includes("999"), false, "mounted observer repaints on commit without native input");
  assert.equal(tabs.includes(originalDecky), false);
  for (let i = 0; i < 5; i++) {
    assert.equal(api.refreshObservedQam(state), true);
    assert.equal(state.failure, null);
    assert.equal(tabs.includes(originalDecky), false);
  }
  lease.setNativeHidden(false);
  assert.equal(tabs.find(tab => String(tab.key) === "999"), originalDecky);
  lease.setNativeHidden(true);
  api.detachTabLayout(layout);
  assert.equal(tabs.find(tab => String(tab.key) === "999"), originalDecky, "detach restores before the owner validates its registry");
  assert.equal(api.renderAdapterState(nativeHook), state);
  assert.equal(api.refreshObservedQam(state), true);
  assert.equal(state.failure, null);
  host.stop();
  assert.notEqual(nativeHook.render, originalWrapper, "detached layout must not be resurrected");
  assert.equal(api.restoreRenderAdapter(state), true);
  assert.equal(nativeHook.tabs.find(tab => tab.id === 999).content, root);
  unsubscribe();
});
