import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

// Exercise the controller tab actually mounted by Playhub. The old standalone
// ControllerSettings component is no longer reachable: the requested UI is feedback only.
const source = fs.readFileSync(new URL("../src/index.tsx", import.meta.url), "utf8");
const ast = ts.createSourceFile("index.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
let expression;
function visit(node) {
  if (ts.isJsxAttribute(node) && node.name.getText(ast) === "controller") expression = node.initializer.expression.getText(ast);
  ts.forEachChild(node, visit);
}
visit(ast);
assert.ok(expression, "the production controller tab must exist");
function fixture(enabled = false, intensity = 55) {
  const changes = [], saves = [];
  const jsx = (type, props) => ({ type, props });
  const code = ts.transpileModule(`const panel = ${expression};`, { compilerOptions: { target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX, module: ts.ModuleKind.CommonJS } }).outputText;
  const context = { exports: {}, require: () => ({ jsx, jsxs: jsx }), PanelSection: "PanelSection", PanelSectionRow: "PanelSectionRow", ToggleField: "ToggleField", SliderField: "SliderField", local: { haptics: "Feedback", hapticsDescription: "Tactile response", hapticsIntensity: "Intensity" }, hapticsEnabled: enabled, hapticsIntensity: intensity, configureNavigationHaptics: value => changes.push(value), saveHapticSettings: value => saves.push(value) };
  const panel = vm.runInNewContext(code + "\npanel", context);
  const nodes = [];
  function walk(node) { if (Array.isArray(node)) return node.forEach(walk); if (!node || typeof node !== "object") return; nodes.push(node); walk(node.props?.children); }
  walk(panel);
  return { nodes, changes, saves };
}
test("production controller tab contains only feedback and no native calibration or SDL controls", () => {
  const f = fixture();
  assert.deepEqual(f.nodes.filter(n => !["PanelSection", "PanelSectionRow"].includes(n.type)).map(n => n.type), ["ToggleField"]);
  assert.doesNotMatch(expression, /ControllerSettings|SDL|DropdownItem|DialogButton|Test e calibrazione/);
});
test("enabling feedback updates runtime and persistent preferences", () => {
  const f = fixture(); f.nodes.find(n => n.type === "ToggleField").props.onChange(true);
  assert.equal(f.changes[0].enabled, true); assert.equal(f.saves[0].navigationHapticsEnabled, true);
});
test("intensity appears only while feedback is enabled", () => {
  assert.equal(fixture(false).nodes.some(n => n.type === "SliderField"), false);
  const slider = fixture(true, 75).nodes.find(n => n.type === "SliderField");
  assert.equal(slider.props.value, 75); assert.equal(slider.props.valueSuffix, "%");
});
test("intensity is rounded and clamped before runtime and persistence changes", () => {
  const f = fixture(true); const slider = f.nodes.find(n => n.type === "SliderField");
  for (const [input, expected] of [[-1, 5], [250, 100], [42.6, 43]]) {
    slider.props.onChange(input); assert.equal(f.changes.at(-1).intensity, expected); assert.equal(f.saves.at(-1).navigationHapticsIntensity, expected);
  }
});
test("feedback fields use localized labels and disable native separators", () => {
  const f = fixture(true);
  for (const node of f.nodes.filter(n => ["ToggleField", "SliderField"].includes(n.type))) {
    assert.equal(node.props.bottomSeparator, "none"); assert.ok(node.props.label);
  }
  assert.match(expression, /local.hapticsDescription/);
});
