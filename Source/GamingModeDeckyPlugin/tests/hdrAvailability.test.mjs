import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

// Render the actual HDR control; keep its props and transaction callback.
// Only React/Decky element construction is inert. No display API is called.
const source = readFileSync(process.env.PLAYHUB_HDR_UI_SOURCE
  ?? new URL("../src/quickSettings/index.tsx", import.meta.url), "utf8");
const ast = ts.createSourceFile("index.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const controls = [];
function visit(node) {
  if (ts.isJsxSelfClosingElement(node) && node.tagName.getText(ast) === "ToggleField"
      && node.attributes.properties.some(prop => ts.isJsxAttribute(prop) && prop.name.text === "label"
        && prop.initializer?.expression?.getText(ast) === "local.hdr")) controls.push(node);
  ts.forEachChild(node, visit);
}
visit(ast);
assert.equal(controls.length, 1, "exactly one real HDR control must be tested");
const exports = {};
vm.runInNewContext(ts.transpileModule(
  `export function renderHdr(hdr, onHdr) { return (${controls[0].getText(ast)}); }`,
  { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React } },
).outputText, {
  exports, ToggleField: "ToggleField", local: { hdr: "HDR" },
  React: { createElement: (type, props) => ({ type, props }) },
});

for (const [name, state] of [
  ["unsupported display", { available: false, supported: false }],
  ["unavailable backend", { available: false, supported: true }],
  ["available but unsupported display", { available: true, supported: false }],
  ["support not yet read", { available: true }],
  ["availability not yet read", { supported: true }],
  ["initial unknown state", {}],
  ["malformed availability", { available: 1, supported: true }],
  ["malformed support", { available: true, supported: "true" }],
]) test(`HDR is disabled for ${name}`, () => {
  const node = exports.renderHdr(state, () => assert.fail("render must not write display settings"));
  assert.equal(node.type, "ToggleField");
  assert.equal(node.props.disabled, true);
});

for (const enabled of [false, true]) test(`supported HDR remains usable with current state ${enabled}`, () => {
  const calls = [], onHdr = value => calls.push(value);
  const node = exports.renderHdr({ available: true, supported: true, enabled }, onHdr);
  assert.notEqual(node.props.disabled, true);
  assert.equal(node.props.checked, enabled);
  assert.equal(node.props.onChange, onHdr, "keep the existing guarded display transaction");
  node.props.onChange(!enabled);
  assert.deepEqual(calls, [!enabled]);
  assert.equal(node.props.label, "HDR");
  assert.equal(node.props.bottomSeparator, "none");
});
