import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const code = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/MsiPowerControls.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
}).outputText;
const deviceLocale = {};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL("../src/deviceInfoLocale.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports: deviceLocale });
const configured = (extra = {}) => ({ available: true, token: "verified-device", mode: 6, tdpAvailable: true, pl1: 17, pl2: 19, ...extra });
const flush = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { resolve, promise }; };

function fixture(first = configured(), locale = "en") {
  const exports = {}, states = [], refs = [], calls = [], listeners = new Map();
  let stateIndex, refIndex, effect;
  let read = async () => first;
  let write = async request => ({ ...configured(request), ok: true });
  const document = { visibilityState: "visible", addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
  const dependencies = {
    react: {
      useState(initial) { const slot = stateIndex++; if (!(slot in states)) states[slot] = initial; return [states[slot], value => { states[slot] = typeof value === "function" ? value(states[slot]) : value; }]; },
      useRef(initial) { const slot = refIndex++; return refs[slot] ??= { current: initial }; },
      useEffect(fn) { effect ??= fn; },
    },
    "@decky/ui": { DropdownItem: "select", SliderField: "slider", ButtonItem: "button" },
    "../controlBackend": { async call(method, value) { calls.push([method, value]); return method === "get_msi_claw_firmware" ? read() : write(value); } },
    "../controlCenterLocale": { controlLocale: () => ({ saveError: "Setting not confirmed." }) },
    "../deviceInfoLocale": deviceLocale,
  };
  vm.runInNewContext(code, { exports, require: name => dependencies[name], document,
    React: { Fragment: "fragment", createElement: (type, props, ...children) => ({ type, props, children }) },
    window: { addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) },
  });
  const render = () => { stateIndex = refIndex = 0; return exports.MsiPowerControls({ locale }); };
  render();
  const cleanup = effect();
  return { calls, render, cleanup, document, listeners,
    mode: value => render().children[0].props.onChange({ data: value }),
    limit: (index, value) => render().children[1].children[index].props.onChange(value),
    apply: () => render().children[1].children[2].props.onClick(),
    onRead: fn => { read = fn; }, onWrite: fn => { write = fn; } };
}

test("unsupported devices have no firmware controls", async () => {
  const f = fixture({ available: false });
  await flush();
  assert.equal(f.render(), null);
  assert.equal(f.calls.length, 1);
  f.cleanup();
});

test("automatic firmware mode exposes the deliberate manual choice before power controls", async () => {
  const f = fixture(configured({ mode: 1 }));
  await flush();
  const tree = f.render();
  assert.equal(tree.children[0].props.selectedOption, 1);
  assert.equal(tree.children[1].type, "div");
  assert.match(tree.children[1].children[0], /Manual/);
  assert.deepEqual(Array.from(tree.children[0].props.rgOptions, item => item.data), [1, 2, 6]);
  f.mode(6);
  await flush();
  assert.equal(f.calls[1][0], "set_msi_claw_power_mode");
  assert.equal(f.calls[1][1].expectedMode, 1);
  assert.equal(f.render().children[1].type, "fragment");
  f.cleanup();
});

test("power sliders keep independent limits valid and apply once with native expected values", async () => {
  const f = fixture();
  await flush();
  f.limit(0, 35);
  let controls = f.render().children[1].children;
  assert.equal(controls[0].props.value, 35);
  assert.equal(controls[1].props.value, 37);
  f.limit(1, 45);
  f.limit(0, 20);
  controls = f.render().children[1].children;
  assert.equal(controls[0].props.value, 20);
  assert.equal(controls[1].props.value, 45);
  assert.equal(f.calls.length, 1);
  const pending = deferred();
  f.onWrite(() => pending.promise);
  f.apply();
  f.apply();
  assert.equal(f.calls.length, 2);
  const [method, request] = f.calls[1];
  assert.equal(method, "set_msi_claw_power_limits");
  assert.equal(request.expectedMode, 6);
  assert.equal(request.expectedPl1, 17);
  assert.equal(request.expectedPl2, 19);
  assert.equal(request.pl1, 20);
  assert.equal(request.pl2, 45);
  pending.resolve({ ...configured({ pl1: 20, pl2: 45 }), ok: true });
  await flush();
  assert.equal(f.render().children[1].children[2], false);
  f.cleanup();
});

test("lowering boost power adjusts sustained power while preserving user edits across refresh", async () => {
  const f = fixture();
  await flush();
  const older = deferred();
  f.onRead(() => older.promise);
  f.listeners.get("focus")();
  f.limit(1, 10);
  older.resolve(configured());
  await flush();
  const controls = f.render().children[1].children;
  assert.equal(controls[0].props.value, 8);
  assert.equal(controls[1].props.value, 10);
  assert.equal(f.calls.length, 2);
  f.listeners.get("focus")();
  await flush();
  assert.equal(f.calls.length, 2);
  f.cleanup();
});

test("competing automatic mode replaces draft controls and reports the failed write", async () => {
  const f = fixture();
  await flush();
  f.limit(0, 25);
  f.onWrite(async () => ({ ...configured({ mode: 2 }), ok: false, code: "profile_changed" }));
  f.apply();
  await flush();
  const tree = f.render();
  assert.equal(tree.children[0].props.selectedOption, 2);
  assert.equal(tree.children[1].type, "div");
  assert.equal(tree.children[2].props.role, "status");
  f.cleanup();
});

test("12 languages preserve power units, bounds and measured mode values", async () => {
  const labels = new Set();
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const f = fixture(configured(), locale);
    await flush();
    const tree = f.render();
    labels.add(tree.children[0].props.label);
    const [continuous, boost] = tree.children[1].children;
    assert.equal(continuous.props.min, 8);
    assert.equal(continuous.props.max, 35);
    assert.equal(boost.props.min, 10);
    assert.equal(boost.props.max, 45);
    assert.equal(continuous.props.valueSuffix, " W");
    assert.equal(boost.props.valueSuffix, " W");
    f.cleanup();
  }
  assert.equal(labels.size, 12);
});
