import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const code = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/MsiChargeLimit.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
}).outputText;
const configured = (chargeLimit = 80, token = "verified-device") => ({ available: true, token, chargeLimit });
const flush = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { resolve, promise }; };

function fixture(first, locale = "en") {
  const exports = {}, states = [], refs = [], calls = [], listeners = new Map();
  let stateIndex, refIndex, effect;
  let read = async () => first;
  let write = async request => ({ ...configured(request.percent, request.token), ok: true });
  const document = { visibilityState: "visible", addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
  const dependencies = {
    react: {
      useState(initial) { const slot = stateIndex++; if (!(slot in states)) states[slot] = initial; return [states[slot], value => { states[slot] = value; }]; },
      useRef(initial) { const slot = refIndex++; return refs[slot] ??= { current: initial }; },
      useEffect(fn) { effect ??= fn; },
    },
    "@decky/ui": { DropdownItem: "select" },
    "../controlBackend": { async call(method, value) { calls.push([method, value]); return method === "get_msi_claw_firmware" ? read() : write(value); } },
    "../controlCenterLocale": { controlLocale: () => ({ saveError: "Setting not confirmed." }) },
    "../deviceInfoLocale": { deviceInfoLocale: () => ({ experimental: "Experimental" }) },
  };
  vm.runInNewContext(code, { exports, require: name => dependencies[name], document,
    React: { createElement: (type, props, ...children) => ({ type, props, children }) },
    window: { addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) },
  });
  const render = () => { stateIndex = refIndex = 0; return exports.MsiChargeLimit({ locale }); };
  render();
  const cleanup = effect();
  return { calls, render, cleanup, document, listeners,
    change: percent => render().children[0].props.onChange({ data: percent }),
    onRead: fn => { read = fn; }, onWrite: fn => { write = fn; } };
}

test("charge limit appears only after verified device firmware capability", async () => {
  const absent = fixture({ available: false });
  await flush();
  assert.equal(absent.render(), null);
  assert.equal(absent.calls.length, 1);
  absent.cleanup();
  const native = fixture(configured());
  await flush();
  const select = native.render().children[0].props;
  assert.equal(select.label, "Charge limit");
  assert.equal(select.description, "MSI firmware · Experimental");
  assert.deepEqual(Array.from(select.rgOptions, option => option.data), [60, 80, 100]);
  native.cleanup();
});

test("an OEM percentage remains selected without changing it to a UI preset", async () => {
  const f = fixture(configured(75));
  await flush();
  const select = f.render().children[0].props;
  assert.equal(select.selectedOption, 75);
  assert.deepEqual(Array.from(select.rgOptions, option => option.data), [60, 75, 80, 100]);
  f.change(75);
  f.change(20);
  await flush();
  assert.equal(f.calls.length, 1);
  f.cleanup();
});

test("charge changes serialize with expected state and discard an older refresh", async () => {
  const f = fixture(configured());
  await flush();
  const previous = deferred(), change = deferred();
  f.onRead(() => previous.promise);
  f.onWrite(() => change.promise);
  f.listeners.get("focus")();
  f.change(60);
  f.change(100);
  assert.equal(f.render().children[0].props.selectedOption, 80);
  const writes = f.calls.filter(([method]) => method === "set_msi_claw_charge_limit");
  assert.equal(writes.length, 1);
  assert.equal(writes[0][1].expected, 80);
  assert.equal(writes[0][1].token, "verified-device");
  change.resolve({ ...configured(60), ok: true });
  await flush();
  previous.resolve(configured(80));
  await flush();
  assert.equal(f.render().children[0].props.selectedOption, 60);
  f.cleanup();
});

test("a competing firmware change replaces stale UI state and reports refusal", async () => {
  const f = fixture(configured());
  await flush();
  f.onWrite(async () => ({ ...configured(75, "new-device"), ok: false, code: "profile_changed" }));
  f.change(60);
  await flush();
  const tree = f.render();
  assert.equal(tree.children[0].props.selectedOption, 75);
  assert.equal(tree.children[1].props.role, "status");
  f.document.visibilityState = "hidden";
  f.listeners.get("focus")();
  await flush();
  assert.equal(f.calls.length, 2);
  f.cleanup();
  assert.equal(f.listeners.size, 0);
});

test("12 languages retain firmware percentages and localized labels", async () => {
  const labels = new Set();
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const f = fixture(configured(), locale);
    await flush();
    const select = f.render().children[0].props;
    labels.add(select.label);
    assert.deepEqual(Array.from(select.rgOptions, option => option.data), [60, 80, 100]);
    assert.ok(select.rgOptions.every(option => option.label === `${option.data}%`));
    f.cleanup();
  }
  assert.equal(labels.size, 12);
});
