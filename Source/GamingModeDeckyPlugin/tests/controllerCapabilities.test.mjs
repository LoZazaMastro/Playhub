import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

// Execute actual source declarations/JSX with inert components, never Steam,
// HTTP, an agent, Python, or a native library. Missing mock imports fail closed.
const source = fs.readFileSync(new URL("../src/quickSettings/index.tsx", import.meta.url), "utf8");
const ast = ts.createSourceFile("index.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const react = { createElement: (type, props, ...children) => ({ type, props: props ?? {}, children }) };
function execute(code, scope = {}) {
  const exports = {};
  const compiled = ts.transpileModule(code, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
  }).outputText;
  vm.runInNewContext(compiled, { exports, React: react, ...scope }, { timeout: 1000 });
  return exports;
}
const sliderNode = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "QuickSlider");
assert.ok(sliderNode, "QuickSlider declaration must exist");
const { QuickSlider } = execute(`${sliderNode.getText(ast)}\nexport { QuickSlider };`, { SliderField: "SliderField" });
function slider(overrides = {}) {
  const calls = [];
  const rendered = QuickSlider({ label: "Power", value: 15, min: 4, max: 40, onChange: value => calls.push(value), ...overrides });
  return { rendered, calls };
}

test("disabled controller-navigable slider never invokes its callback", () => {
  const { rendered, calls } = slider({ disabled: true });
  assert.equal(rendered.props.disabled, true);
  for (const value of [4, 20, 40, NaN, Infinity, -Infinity]) rendered.props.onChange(value);
  assert.deepEqual(calls, []);
});

test("finite slider input is clamped to the declared range", () => {
  const { rendered, calls } = slider();
  for (const value of [-100, 4, 20, 40, 100]) rendered.props.onChange(value);
  assert.deepEqual(calls, [4, 4, 20, 40, 40]);
});

for (const value of [NaN, Infinity, -Infinity]) {
  test(`slider rejects non-finite input ${value} without scheduling a write`, () => {
    const { rendered, calls } = slider();
    rendered.props.onChange(value);
    assert.deepEqual(calls, [], "invalid input must not become NaN or an extreme power request");
  });
}

for (const range of [{ min: NaN, max: 40 }, { min: 4, max: Infinity }, { min: 40, max: 4 }]) {
  test(`invalid slider range ${String(range.min)}..${String(range.max)} cannot dispatch`, () => {
    const { rendered, calls } = slider(range);
    rendered.props.onChange(20);
    assert.deepEqual(calls, []);
  });
}

const expressions = [];
function visit(node) {
  if (ts.isJsxExpression(node) && node.expression) {
    const text = node.expression.getText(ast);
    if (text.includes("section(<FaBolt />, local.tdp,")) expressions.push(text);
  }
  ts.forEachChild(node, visit);
}
visit(ast);
test("removed TDP has no control or unavailable card", () => {
  assert.equal(expressions.length, 0);
  assert.ok(!source.includes("getTdpStatus()"));
  assert.ok(!source.includes("setTdp("));
});
const reason = "CPU power control has been removed.";

function backend() {
  const calls = [];
  const exports = execute(fs.readFileSync(new URL("../src/quickSettings/backend.ts", import.meta.url), "utf8"), {
    require: name => {
      assert.equal(name, "../controlBackend", "all dependencies must remain mocked");
      return { call: async (...args) => { calls.push(args); return { ok: false, available: false, message: reason }; } };
    },
  });
  return { exports, calls };
}
test("capability failure reason survives the mocked backend client", async () => {
  const { exports, calls } = backend();
  const result = await exports.getTdpStatus();
  assert.equal(result.available, false);
  assert.equal(result.message, reason);
  assert.equal(calls[0][0], "get_tdp_status");
});
test("TDP client rejects non-finite numbers before any RPC", async () => {
  const { exports, calls } = backend();
  for (const value of [NaN, Infinity, -Infinity]) {
    try { await exports.setTdp(value); } catch { /* Explicit validation rejection is valid. */ }
  }
  assert.equal(calls.length, 0, "non-finite values must not reach even the mocked RPC boundary");
});

test("controller haptic intensity remains finite and bounded for invalid intensity input", () => {
  const { modernHapticIntensity } = execute(fs.readFileSync(new URL("../src/hapticIntensity.ts", import.meta.url), "utf8"));
  for (const value of [NaN, Infinity, -Infinity, -100, 0, 5, 55, 100, 1000]) {
    const result = modernHapticIntensity(value);
    assert.ok(Number.isFinite(result.level) && result.level >= 0 && result.level <= 2);
    assert.ok(Number.isFinite(result.gain) && result.gain >= -12 && result.gain <= 16);
    assert.ok(result.repeatGain === null || Number.isFinite(result.repeatGain));
  }
});
