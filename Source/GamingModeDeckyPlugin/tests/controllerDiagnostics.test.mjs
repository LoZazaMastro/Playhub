import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const source = fs.readFileSync(new URL("../src/controllerDiagnostics.ts", import.meta.url), "utf8");
function load(timers = {}) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, setTimeout, clearTimeout, ...timers });
  return exports;
}
const api = load();
const fixture = () => ({ schemaVersion: 1, scope: "sdl_file_only", observedAtMs: Date.now(),
  status: "file_observed", sha256: "a".repeat(64), sizeBytes: 20,
  nativeLoaded: false, deviceProbed: false,
  capabilities: Object.fromEntries(api.blockedFeatures.map(key => [key, false])) });

test("one argument-free direct RPC; no bootstrap imports or reloads", async () => {
  const calls = [];
  const result = await api.requestControllerDiagnostic(async (...args) => { calls.push(args); return fixture(); });
  assert.equal(result.kind, "observed");
  assert.deepEqual(calls, [["get_controller_sdl_diagnostics"]]);
  assert.equal(ts.createSourceFile("helper.ts", source, ts.ScriptTarget.Latest).statements.some(ts.isImportDeclaration), false);
  assert.ok(!Object.values(result.value.capabilities).some(Boolean));
});

test("malformed, stale, loaded or hardware-capable data is rejected", () => {
  const base = fixture();
  for (const raw of [null, [], {}, { ...base, schemaVersion: 2 }, { ...base, scope: "device" },
    { ...base, nativeLoaded: true }, { ...base, deviceProbed: true },
    { ...base, observedAtMs: Date.now() - 61000 }, { ...base, observedAtMs: Date.now() + 6000 },
    { ...base, sha256: "bad" }, { ...base, sizeBytes: Infinity }, { ...base, status: "ready" },
    ...api.blockedFeatures.map(key => ({ ...base, capabilities: { ...base.capabilities, [key]: true } }))]) {
    assert.equal(api.parseControllerDiagnostic(raw), null);
  }
});

test("file status is not controller support, and no backend is unavailable", async () => {
  const result = await api.requestControllerDiagnostic(async () => { throw Error("no such method"); });
  assert.equal(result.kind, "unavailable");
  assert.equal(api.controllerDiagnosticText(result, "en"), "Backend diagnostics unavailable");
  assert.match(api.controllerDiagnosticText({ kind: "observed", value: fixture() }, "it"), /hardware non verificati/);
  const missing = { ...fixture(), status: "file_missing", sha256: null, sizeBytes: null };
  assert.ok(api.parseControllerDiagnostic(missing));
  assert.equal(api.parseControllerDiagnostic({ ...missing, sha256: "a".repeat(64) }), null);
});

test("bounded timeout ignores late RPC success without retry", async () => {
  let expire, resolve, calls = 0;
  const helper = load({ setTimeout: fn => { expire = fn; return 1; }, clearTimeout: () => {} });
  const pending = helper.requestControllerDiagnostic(() => { calls++; return new Promise(r => { resolve = r; }); });
  await Promise.resolve();
  expire();
  assert.equal((await pending).kind, "timeout");
  resolve(fixture());
  assert.equal(calls, 1);
});

test("unknown fields are not propagated and localization has an English fallback", () => {
  const parsed = api.parseControllerDiagnostic({ ...fixture(), activate: true, path: "private" });
  assert.equal(parsed.activate, undefined);
  assert.equal(parsed.path, undefined);
  assert.equal(api.controllerDiagnosticCopy("italian").title, api.controllerDiagnosticCopy("it").title);
  assert.equal(api.controllerDiagnosticCopy("de").title, api.controllerDiagnosticCopy("en").title);
});

function proposedPanel(call) {
  const patch = fs.readFileSync(new URL("../controller_porting/parent-integration.patch", import.meta.url), "utf8");
  const lines = patch.split(/\r?\n/);
  const start = lines.findIndex(line => line.startsWith("+function ControllerRuntimeDiagnostics("));
  assert.ok(start >= 0);
  const body = [];
  for (let i = start; i < lines.length && lines[i].startsWith("+"); i++) body.push(lines[i].slice(1));
  let cleanup;
  const writes = [];
  const React = {
    useState: value => [value, next => writes.push(next)],
    useRef: current => ({ current }),
    useEffect: effect => { cleanup = effect(); },
    createElement: (type, props, ...children) => ({ type, props, children }),
  };
  const exports = {};
  vm.runInNewContext(ts.transpileModule(body.join("\n") + "\nexport { ControllerRuntimeDiagnostics };", {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
  }).outputText, { exports, React, ...api, directDeckyCall: call,
    PanelSection: "section", PanelSectionRow: "row", ButtonItem: "button", TbRotateClockwise: "icon" });
  const tree = exports.ControllerRuntimeDiagnostics({ locale: "en" });
  return { button: tree.children[0].children[0], cleanup, writes };
}

test("proposed actual panel is inert until click and suppresses double dispatch", async () => {
  let calls = 0, resolve;
  const panel = proposedPanel(() => { calls++; return new Promise(r => { resolve = r; }); });
  assert.equal(calls, 0);
  const pending = panel.button.props.onClick();
  await panel.button.props.onClick();
  await Promise.resolve();
  assert.equal(calls, 1);
  resolve(fixture());
  await pending;
  assert.equal(panel.writes.at(-1), false);
});

test("proposed panel ignores a response after unmount", async () => {
  let resolve;
  const panel = proposedPanel(() => new Promise(r => { resolve = r; }));
  const pending = panel.button.props.onClick();
  await Promise.resolve();
  panel.cleanup();
  const before = panel.writes.length;
  resolve(fixture());
  await pending;
  assert.equal(panel.writes.length, before);
});
