import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

function load(name, dependencies = {}) {
  const exports = {};
  const source = fs.readFileSync(new URL(`../src/${name}.ts`, import.meta.url), "utf8");
  vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, {
    exports, require: name => { assert.ok(dependencies[name], `Unexpected import ${name}`); return dependencies[name]; },
  });
  return exports;
}
const state = load("controlCenterState");
const { createTabEditor, canToggleTab, tabEditorTransition: transition } = load("controlTabEditorState", { "./controlCenterState": state });
const { tabEditorLocale } = load("controlTabEditorLocale");
const json = value => JSON.parse(JSON.stringify(value));
const initial = () => state.normalizeControlPreferences({ collapsed: ["device", "radeon"], active: "audio", deckyHostEnabled: false });

test("move preview keeps selection and does not write preferences; B cancels", () => {
  const prefs = initial();
  let result = transition(prefs, createTabEditor("audio"), { type: "secondary" }, false);
  result = transition(prefs, result.editor, { type: "move", direction: -1 }, false);
  assert.equal(result.preferences, prefs);
  assert.equal(result.editor.selected, "audio");
  assert.notDeepEqual(json(result.editor.draftOrder), json(prefs.order));
  result = transition(prefs, result.editor, { type: "select", id: "store" }, false);
  assert.equal(result.editor.selected, "audio", "focus events cannot steal the moving tab");
  result = transition(prefs, result.editor, { type: "cancel" }, false);
  assert.equal(result.preferences, prefs);
  assert.equal(result.editor.draftOrder, null);
  assert.equal(result.editor.selected, "audio");
});
for (const type of ["confirm", "secondary"]) {
  test(`${type} commits movement without toggling visibility or unrelated settings`, () => {
    const prefs = initial();
    let result = transition(prefs, createTabEditor("audio"), { type: "secondary" }, false);
    result = transition(prefs, result.editor, { type: "move", direction: 1 }, false);
    const expected = json(result.editor.draftOrder);
    result = transition(prefs, result.editor, { type }, false);
    assert.deepEqual(json(result.preferences.order), expected);
    assert.deepEqual(json(result.preferences.hidden), json(prefs.hidden));
    assert.deepEqual(json(result.preferences.collapsed), json(prefs.collapsed));
    assert.equal(result.preferences.deckyHostEnabled, false);
    assert.equal(result.preferences.active, "audio");
    assert.equal(result.editor.draftOrder, null);
    assert.equal(result.editor.selected, "audio");
  });
}
test("A toggles visibility and keeps the selected editor tab", () => {
  const prefs = initial();
  let result = transition(prefs, createTabEditor("audio"), { type: "confirm" }, false);
  assert.ok(result.preferences.hidden.includes("audio"));
  assert.equal(result.editor.selected, "audio");
  result = transition(result.preferences, result.editor, { type: "confirm" }, false);
  assert.ok(!result.preferences.hidden.includes("audio"));
});
test("the final usable tab cannot be hidden even when Decky is opted in but unavailable", () => {
  const prefs = state.normalizeControlPreferences({ deckyHostEnabled: true, hidden: state.CONTROL_TABS.filter(id => !["home", "decky"].includes(id)) });
  assert.equal(canToggleTab(prefs, "home", false), false);
  const result = transition(prefs, createTabEditor("home"), { type: "confirm" }, false);
  assert.equal(result.preferences, prefs);
  assert.equal(canToggleTab(prefs, "home", true), true);
  assert.equal(canToggleTab(prefs, "audio", false), true);
});
test("movement includes Decky even when native Steam hiding is disabled", () => {
  const prefs = initial();
  let result = transition(prefs, createTabEditor("controller"), { type: "secondary" }, false);
  const order = json(result.editor.draftOrder);
  result = transition(prefs, result.editor, { type: "move", direction: 1 }, false);
  assert.notDeepEqual(json(result.editor.draftOrder), order);
  assert.equal(result.editor.draftOrder.at(-1), "controller");
  result = transition(prefs, createTabEditor("home"), { type: "secondary" }, false);
  result = transition(prefs, result.editor, { type: "move", direction: -1 }, false);
  assert.deepEqual(json(result.editor.draftOrder), json(prefs.order));
});
for (const enabled of [true, false]) {
  test(`reset changes only tab order/visibility and preserves explicit Decky ${enabled}`, () => {
    const prefs = state.normalizeControlPreferences({ order: [...state.CONTROL_TABS].reverse(), hidden: ["store"], collapsed: ["device", "radeon"], active: "audio", deckyHostEnabled: enabled });
    const result = transition(prefs, createTabEditor("graphics"), { type: "reset" }, enabled);
    assert.deepEqual(json(result.preferences.order), json(state.CONTROL_TABS));
    assert.deepEqual(json(result.preferences.hidden), []);
    assert.deepEqual(json(result.preferences.collapsed), ["device", "radeon"]);
    assert.equal(result.preferences.active, "audio");
    assert.equal(result.preferences.deckyHostEnabled, enabled);
    assert.equal(result.editor.selected, "graphics");
  });
}
test("reset preserves the parent's default Decky opt-in", () => {
  const prefs = state.normalizeControlPreferences({});
  assert.equal(prefs.deckyHostEnabled, true);
  assert.equal(transition(prefs, createTabEditor(), { type: "reset" }, false).preferences.deckyHostEnabled, true);
});
test("all 12 locales cover footer, state and a scoped reset explanation", () => {
  const en = tabEditorLocale("en");
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const copy = tabEditorLocale(locale);
    assert.ok(Object.values(copy).every(text => typeof text === "string" && text.trim().length));
    if (locale !== "en") assert.notEqual(copy.resetHint, en.resetHint);
    assert.deepEqual(copy, tabEditorLocale(`${locale}-XX`));
  }
  assert.deepEqual(tabEditorLocale("italian"), tabEditorLocale("it"));
  assert.deepEqual(tabEditorLocale("koreana"), tabEditorLocale("ko"));
  assert.deepEqual(tabEditorLocale("unknown"), en);
});
test("editor uses native actions, stable keyed nodes and no legacy visibility/order controls", () => {
  const source = fs.readFileSync(new URL("../src/ControlCenter.tsx", import.meta.url), "utf8");
  assert.match(source, /onSecondaryButton=/);
  assert.match(source, /onGamepadDirection=\{editor\.draftOrder \? editorDirection : undefined\}/);
  assert.match(source, /key=\{id\}/);
  assert.match(source, /style=\{\{ order: \(editor\.draftOrder \?\? state\.order\)\.indexOf\(id\) \}\}/);
  assert.doesNotMatch(source, /ToggleField label=\{copy.show\}|TbArrowLeft|TbArrowRight|<h3>\{copy.settings\}/);
  assert.doesNotMatch(source, /<TbX|ph-control-settings-header/);
  assert.match(source, /active !== "decky" && \(active !== "home" \|\| homeTitle\)/);
});
