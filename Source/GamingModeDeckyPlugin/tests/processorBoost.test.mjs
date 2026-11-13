import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const code = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/ProcessorBoost.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
}).outputText;
const configured = (mode = 2, source = "ac", plan = "current-plan") => ({ available: true, plan, source, mode, modes: [0, 1, 2, 3, 4, 5, 6] });
const flush = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { resolve, promise }; };

function fixture(first, locale = "en") {
  const exports = {}, states = [], refs = [], calls = [], listeners = new Map();
  let stateIndex, refIndex, effect, refresh;
  let read = async () => first;
  let write = async request => ({ ...configured(request.mode, request.source, request.plan), ok: true });
  const document = { visibilityState: "visible", addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
  const dependencies = {
    react: {
      useState(initial) { const slot = stateIndex++; if (!(slot in states)) states[slot] = initial; return [states[slot], value => { states[slot] = value; }]; },
      useRef(initial) { const slot = refIndex++; return refs[slot] ??= { current: initial }; },
      useEffect(fn) { effect ??= fn; },
    },
    "@decky/ui": { DropdownItem: "select" },
    "../controlBackend": { async call(method, value) { calls.push([method, value]); return method === "get_processor_boost" ? read() : write(value); } },
    "../controlCenterLocale": { controlLocale: () => ({ saveError: "Setting not confirmed." }) },
    "../deviceInfoLocale": { deviceInfoLocale: () => ({ ac: "Plugged in", battery: "On battery" }) },
  };
  vm.runInNewContext(code, { exports, require: name => dependencies[name], document,
    React: { createElement: (type, props, ...children) => ({ type, props, children }) },
    window: { setInterval: fn => { refresh = fn; }, clearInterval: () => { refresh = undefined; },
      addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) },
  });
  const render = () => { stateIndex = refIndex = 0; return exports.ProcessorBoost({ locale }); };
  render();
  const cleanup = effect();
  return { calls, render, cleanup, document, listeners, refresh: () => refresh?.(),
    change: mode => render().children[0].props.onChange({ data: mode }),
    onRead: fn => { read = fn; }, onWrite: fn => { write = fn; } };
}

test("current advanced Windows policy is retained alongside simple supported choices", async () => {
  const f = fixture(configured(6, "battery"));
  await flush();
  const select = f.render().children[0].props;
  assert.equal(select.selectedOption, 6);
  assert.match(select.description, /^On battery/);
  assert.deepEqual(Array.from(select.rgOptions, option => option.data), [0, 1, 2, 3, 4, 6]);
  assert.equal(select.rgOptions.at(-1).label, "Windows preset");
  f.cleanup();
});

test("write retains the confirmed policy, refuses repeated input and ignores a stale read", async () => {
  const f = fixture(configured());
  await flush();
  const previous = deferred(), change = deferred();
  f.onRead(() => previous.promise);
  f.onWrite(() => change.promise);
  f.refresh();
  f.change(0);
  f.change(1);
  assert.equal(f.render().children[0].props.selectedOption, 2);
  assert.equal(f.render().children[0].props.disabled, true);
  const writes = f.calls.filter(([method]) => method === "set_processor_boost");
  assert.equal(writes.length, 1);
  assert.equal(writes[0][1].plan, "current-plan");
  assert.equal(writes[0][1].source, "ac");
  assert.equal(writes[0][1].mode, 0);
  change.resolve({ ...configured(0), ok: true });
  await flush();
  previous.resolve(configured(2));
  await flush();
  assert.equal(f.render().children[0].props.selectedOption, 0);
  f.cleanup();
});

test("changed source or plan replaces the visible policy and reports a refused write", async () => {
  const f = fixture(configured());
  await flush();
  f.onWrite(async () => ({ ...configured(3, "battery", "new-plan"), ok: false, code: "profile_changed" }));
  f.change(0);
  await flush();
  const tree = f.render();
  assert.equal(tree.children[0].props.selectedOption, 3);
  assert.match(tree.children[0].props.description, /^On battery/);
  assert.equal(tree.children[1].props.role, "status");
  f.cleanup();
});

test("hidden control pauses reads and disposing removes timer and event listeners", async () => {
  const f = fixture({ available: false });
  await flush();
  assert.equal(f.render(), null);
  f.document.visibilityState = "hidden";
  f.refresh();
  assert.equal(f.calls.length, 1);
  f.cleanup();
  assert.equal(f.listeners.size, 0);
  f.refresh();
  assert.equal(f.calls.length, 1);
});

test("each supported locale supplies CPU boost choices", async () => {
  const labels = new Set();
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const f = fixture(configured(), locale);
    await flush();
    const select = f.render().children[0].props;
    labels.add(select.label);
    assert.ok(select.rgOptions.every(option => typeof option.label === "string" && option.label.length));
    f.cleanup();
  }
  assert.ok(labels.size >= 11);
});
