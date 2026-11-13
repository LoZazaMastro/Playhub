import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const code = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/HandheldPowerMode.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
}).outputText;
const deviceLocale = {};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL("../src/deviceInfoLocale.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports: deviceLocale });
const configured = (mode = 2, token = "verified-device") => ({ available: true, token, mode, modes: [1, 2, 3], editable: mode !== 255 });
const flush = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { resolve, promise }; };

function fixture(first, locale = "en") {
  const exports = {}, states = [], refs = [], calls = [], listeners = new Map();
  let stateIndex, refIndex, effect;
  let read = async () => first;
  let write = async request => ({ ...configured(request.mode, request.token), ok: true });
  const document = { visibilityState: "visible", addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
  const dependencies = {
    react: {
      useState(initial) { const slot = stateIndex++; if (!(slot in states)) states[slot] = initial; return [states[slot], value => { states[slot] = value; }]; },
      useRef(initial) { const slot = refIndex++; return refs[slot] ??= { current: initial }; },
      useEffect(fn) { effect ??= fn; },
    },
    "@decky/ui": { DropdownItem: "select" },
    "../controlBackend": { async call(method, value) { calls.push([method, value]); return method === "get_handheld_power_mode" ? read() : write(value); } },
    "../controlCenterLocale": { controlLocale: () => ({ saveError: "Setting not confirmed." }) },
    "../deviceInfoLocale": deviceLocale,
  };
  vm.runInNewContext(code, { exports, require: name => dependencies[name], document,
    React: { createElement: (type, props, ...children) => ({ type, props, children }) },
    window: { addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) },
  });
  const render = () => { stateIndex = refIndex = 0; return exports.HandheldPowerMode({ locale }); };
  render();
  const cleanup = effect();
  return { calls, render, cleanup, document, listeners,
    change: mode => render().children[0].props.onChange({ data: mode }),
    onRead: fn => { read = fn; }, onWrite: fn => { write = fn; } };
}

test("only verified firmware capability creates the device performance control", async () => {
  const absent = fixture({ available: false });
  await flush();
  assert.equal(absent.render(), null);
  assert.equal(absent.calls.length, 1);
  absent.cleanup();
  const native = fixture(configured());
  await flush();
  const select = native.render().children[0].props;
  assert.equal(select.label, "Device performance");
  assert.equal(select.description, "Lenovo firmware · Experimental");
  assert.deepEqual(Array.from(select.rgOptions, option => option.data), [1, 2, 3]);
  native.cleanup();
});

test("custom firmware profile stays visible and cannot trigger a preset write", async () => {
  const f = fixture(configured(255));
  await flush();
  const tree = f.render();
  assert.equal(tree.children[0].props.selectedOption, 255);
  assert.equal(tree.children[0].props.disabled, true);
  assert.equal(tree.children[0].props.rgOptions.at(-1).label, "Custom");
  f.change(1);
  await flush();
  assert.equal(f.calls.length, 1);
  f.cleanup();
});

test("firmware write is serialized, carries expected mode and ignores an older refresh", async () => {
  const f = fixture(configured());
  await flush();
  const previous = deferred(), change = deferred();
  f.onRead(() => previous.promise);
  f.onWrite(() => change.promise);
  f.listeners.get("focus")();
  f.change(1);
  f.change(3);
  assert.equal(f.render().children[0].props.selectedOption, 2);
  const writes = f.calls.filter(([method]) => method === "set_handheld_power_mode");
  assert.equal(writes.length, 1);
  assert.equal(writes[0][1].expected, 2);
  assert.equal(writes[0][1].token, "verified-device");
  change.resolve({ ...configured(1), ok: true });
  await flush();
  previous.resolve(configured(2));
  await flush();
  assert.equal(f.render().children[0].props.selectedOption, 1);
  f.cleanup();
});

test("refused stale request updates to the current firmware mode and shows failure", async () => {
  const f = fixture(configured());
  await flush();
  f.onWrite(async () => ({ ...configured(3, "new-device"), ok: false, code: "profile_changed" }));
  f.change(1);
  await flush();
  const tree = f.render();
  assert.equal(tree.children[0].props.selectedOption, 3);
  assert.equal(tree.children[2].props.role, "status");
  f.document.visibilityState = "hidden";
  f.listeners.get("focus")();
  await flush();
  assert.equal(f.calls.length, 2);
  f.cleanup();
  assert.equal(f.listeners.size, 0);
});

test("12 languages contain native firmware labels and preserve preset values", async () => {
  const labels = new Set();
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const f = fixture(configured(), locale);
    await flush();
    const select = f.render().children[0].props;
    labels.add(select.label);
    assert.deepEqual(Array.from(select.rgOptions, option => option.data), [1, 2, 3]);
    assert.ok(select.rgOptions.every(option => typeof option.label === "string" && option.label.length));
    f.cleanup();
  }
  assert.equal(labels.size, 12);
});
