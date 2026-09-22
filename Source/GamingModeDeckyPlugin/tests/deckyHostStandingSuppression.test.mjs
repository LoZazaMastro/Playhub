import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const compile = (file) => ts.transpileModule(readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
}).outputText;

class Hook {
  tabs = [{ id: 999, content: {} }];
  render(tabs, visible) {
    if (tabs.filter(tab => tab.decky).length === this.tabs.length) {
      for (const tab of tabs) if (tab.decky) tab.initialVisibility = visible;
      return;
    }
    for (const tab of this.tabs) tabs.push({ key: tab.id, decky: true, panel: tab.content, initialVisibility: visible });
  }
}

function standalone() {
  const api = {};
  vm.runInNewContext(compile("deckyHostStandalone.ts"), { exports: api, WeakRef, setInterval: () => 1, clearInterval() {} });
  return api;
}

/** The reported defect: the preference only took effect once Playhub's own tab mounted. */
test("native Decky is suppressed by preference alone, before any host lease is acquired", () => {
  const api = standalone();
  const hook = new Hook();
  const id = 1401877092;
  hook.tabs.push({ id, content: {} });
  const tabs = [];
  let renders = 0;
  const adapter = api.createStandaloneDeckyHost(hook, Hook.prototype.render, id, () => true, () => {
    renders++; adapter.beforeRender(tabs); hook.render(tabs, true); adapter.afterRender(tabs, true);
  });

  adapter.beforeRender(tabs); hook.render(tabs, true); adapter.afterRender(tabs, true);
  assert.ok(tabs.some(tab => tab.key === 999), "no preference applied yet");

  adapter.capability.setNativeHidden(true);
  assert.ok(renders > 0, "changing the preference re-renders the owner instead of waiting for a mount");
  assert.equal(tabs.some(tab => tab.key === 999), false, "hidden without ever acquiring a lease");
  assert.ok(tabs.some(tab => tab.key === id), "Playhub's own tab still provides access to Decky");

  adapter.capability.setNativeHidden(false);
  assert.equal(tabs.filter(tab => tab.key === 999).length, 1, "restored exactly once");
  adapter.stop();
});

test("standing suppression keeps Decky reachable and is released on stop", () => {
  const api = standalone();
  const hook = new Hook();
  const id = 1401877092;
  hook.tabs.push({ id, content: {} });

  // Playhub's tab is absent from this array: hiding Decky would strand the user.
  const orphan = [];
  let owns = true;
  const adapter = api.createStandaloneDeckyHost(hook, Hook.prototype.render, () => owns ? id : undefined, () => owns, () => {});
  adapter.capability.setNativeHidden(true);
  orphan.push({ key: 999, decky: true, panel: {} });
  adapter.afterRender(orphan, true);
  assert.ok(orphan.some(tab => tab.key === 999), "never hidden without Playhub's own tab in the same array");

  const tabs = [];
  adapter.beforeRender(tabs); hook.render(tabs, true); adapter.afterRender(tabs, true);
  assert.equal(tabs.some(tab => tab.key === 999), false);

  // A frozen array is left untouched rather than throwing inside Steam's render.
  const frozen = Object.freeze([{ key: 999, decky: true }, { key: id, decky: true }]);
  adapter.afterRender(frozen, true);
  assert.equal(frozen.length, 2);

  adapter.stop();
  assert.equal(tabs.filter(tab => tab.key === 999).length, 1, "unloading Playhub restores the native tab");
});

test("a mounted lease still owns suppression and the standing path never double-projects", () => {
  const api = standalone();
  const hook = new Hook();
  const entry = hook.tabs.pop();
  const id = 1401877092;
  hook.tabs.push({ id, content: {} });
  const tabs = [];
  const adapter = api.createStandaloneDeckyHost(hook, Hook.prototype.render, id, () => true, () => {
    adapter.beforeRender(tabs); hook.render(tabs, true); adapter.afterRender(tabs, true);
  });
  hook.tabs.push(entry);
  adapter.capability.setNativeHidden(true);

  let mounted = false, selected = true;
  const attributes = new Map();
  const nativeButton = { getAttribute: name => name === "aria-selected" ? String(selected) : attributes.get(name) ?? null,
    setAttribute: (name, value) => attributes.set(name, value), removeAttribute: name => attributes.delete(name), querySelectorAll: () => [] };
  const lease = adapter.capability.acquire({ element: { isConnected: true,
    closest: () => mounted ? { id: `quickaccess_content_${id}` } : null,
    ownerDocument: { getElementById: key => key === "quickaccess_tab_999" ? nativeButton : mounted ? {} : null } },
    createContent: root => root, isHealthy: () => true, onRelease: () => {}, hideNative: true });
  assert.ok(lease);
  lease.setReady();
  mounted = true; selected = true; lease.renew();
  assert.ok(tabs.some(tab => tab.key === 999), "the selected native tab is never yanked away, even with the preference on");
  selected = false; lease.renew();
  assert.equal(tabs.filter(tab => tab.key === 999).length, 0);
  lease.release();
  assert.equal(tabs.filter(tab => tab.key === 999).length, 1, "releasing the lease restores before the standing path re-applies");
  adapter.stop();
});
