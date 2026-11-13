import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const code = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/AsusPerformanceActions.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
}).outputText;
const configured = (token = "verified-device") => ({ available: true, token, commands: [2, 0, 1], ok: true, acknowledged: false });
const flush = () => new Promise(resolve => setImmediate(resolve));
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { resolve, promise }; };

function fixture(first = configured(), locale = "en") {
  const exports = {}, states = [], refs = [], calls = [], listeners = new Map();
  let stateIndex, refIndex, effect;
  let read = async () => first;
  let write = async request => ({ ...configured(request.token), acknowledged: true });
  const document = { visibilityState: "visible", addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) };
  const dependencies = {
    react: {
      useState(initial) { const slot = stateIndex++; if (!(slot in states)) states[slot] = initial; return [states[slot], value => { states[slot] = value; }]; },
      useRef(initial) { const slot = refIndex++; return refs[slot] ??= { current: initial }; },
      useEffect(fn) { effect ??= fn; },
    },
    "@decky/ui": { DialogButton: "button" },
    "../controlBackend": { async call(method, value) { calls.push([method, value]); return method === "get_asus_performance_commands" ? read() : write(value); } },
    "../controlCenterLocale": { controlLocale: () => ({ saveError: "Setting not confirmed." }) },
    "../deviceInfoLocale": { deviceInfoLocale: () => ({ experimental: "Experimental" }) },
  };
  vm.runInNewContext(code, { exports, require: name => dependencies[name], document,
    React: { createElement: (type, props, ...children) => ({ type, props, children }) },
    window: { addEventListener: (name, fn) => listeners.set(name, fn), removeEventListener: name => listeners.delete(name) },
  });
  const render = () => { stateIndex = refIndex = 0; return exports.AsusPerformanceActions({ locale }); };
  render();
  const cleanup = effect();
  return { calls, render, cleanup, document, listeners,
    apply: index => render().children[2][index].props.onClick(),
    onRead: fn => { read = fn; }, onWrite: fn => { write = fn; } };
}

test("unsupported devices expose no ASUS commands and opening the UI never writes", async () => {
  const absent = fixture({ available: false });
  await flush();
  assert.equal(absent.render(), null);
  assert.deepEqual(absent.calls.map(([method]) => method), ["get_asus_performance_commands"]);
  absent.cleanup();
  const native = fixture();
  await flush();
  assert.deepEqual(Array.from(native.render().children[2], item => item.children[0]), ["Silent", "Balanced", "Turbo"]);
  assert.equal(native.calls.length, 1);
  native.cleanup();
});

test("preset actions serialize one explicit command with no selected firmware-state badge", async () => {
  const f = fixture();
  await flush();
  const pending = deferred();
  f.onWrite(() => pending.promise);
  f.apply(0);
  f.apply(1);
  assert.equal(f.calls.length, 2);
  assert.equal(f.calls[1][0], "apply_asus_performance_command");
  assert.equal(f.calls[1][1].mode, 2);
  assert.equal(f.calls[1][1].token, "verified-device");
  assert.ok(f.render().children[2].every(item => item.props.disabled));
  pending.resolve({ ...configured(), acknowledged: true });
  await flush();
  const buttons = f.render().children[2];
  assert.ok(buttons.every(item => !item.props.disabled));
  assert.ok(buttons.every(item => !("checked" in item.props) && !("selected" in item.props) && !("aria-pressed" in item.props)));
  assert.equal(f.render().children[3], false);
  f.cleanup();
});

test("an older capability refresh cannot undo a new command response", async () => {
  const f = fixture();
  await flush();
  const old = deferred(), pending = deferred();
  f.onRead(() => old.promise);
  f.onWrite(() => pending.promise);
  f.listeners.get("focus")();
  f.apply(1);
  pending.resolve({ ...configured("new-device"), acknowledged: true });
  await flush();
  old.resolve({ available: false });
  await flush();
  assert.ok(f.render());
  f.onWrite(async request => ({ ...configured(request.token), acknowledged: true }));
  f.apply(2);
  await flush();
  assert.equal(f.calls.at(-1)[1].token, "new-device");
  assert.equal(f.calls.at(-1)[1].mode, 1);
  f.cleanup();
});

test("a refused or uncertain acknowledgement shows failure without retry", async () => {
  const f = fixture();
  await flush();
  f.onWrite(async () => ({ ...configured(), ok: false, acknowledged: false, code: "command_refused" }));
  f.apply(0);
  await flush();
  assert.equal(f.calls.length, 2);
  assert.equal(f.render().children[3].props.role, "status");
  f.document.visibilityState = "hidden";
  f.listeners.get("focus")();
  await flush();
  assert.equal(f.calls.length, 2);
  f.cleanup();
  assert.equal(f.listeners.size, 0);
});

test("12 languages provide wrapping command labels with no inferred active selection", async () => {
  const labels = new Set();
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const f = fixture(configured(), locale);
    await flush();
    const tree = f.render();
    labels.add(tree.children[0].children[0]);
    for (const button of tree.children[2]) {
      assert.ok(typeof button.children[0] === "string" && button.children[0].length > 0);
      assert.equal(button.props.style.whiteSpace, "normal");
      assert.equal(button.props.style.height, "auto");
      assert.ok(!("selected" in button.props));
    }
    f.cleanup();
  }
  assert.equal(labels.size, 12);
});
