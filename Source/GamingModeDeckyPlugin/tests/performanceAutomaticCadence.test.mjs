import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const code = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/PerformanceSettings.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
}).outputText;
function render(locale, selected = "live", liveEnabled = true) {
  const state = { enabled: liveEnabled, detail: 4, corner: 2, transparent: false, refreshMs: 20, status: "NoData", appId: 42 };
  let effects = 0, writes = 0;
  const runtime = { read: () => ({ state, loading: false, busy: false, failed: false }), change: () => { writes++; } };
  const exports = {};
  const React = { createElement: (type, props, ...children) => ({ type, props: props ?? {}, children }), Fragment: "Fragment",
    useState: value => [typeof value === "function" ? value() : value, () => {}], useRef: value => ({current:value}), useEffect: () => { effects++; } };
  const imports = {
    "@decky/ui": { DialogButton: "Button", DropdownItem: "Dropdown", ToggleField: "Toggle" }, react: React,
    "../controlCenterLocale": { controlLocale: () => ({ loading: "Loading", unavailable: "Unavailable", saveError: "Error", retry: "Retry" }) },
    "./livePerformanceRuntime": { livePerformanceRuntime: () => runtime, PERFORMANCE_SOURCE: "fixture",
      performanceMonitorHost: () => ({ localStorage: { getItem: () => selected } }) },
    "./SteamPerformanceSettings": { SteamPerformanceSettings: "SteamCounter" },
    "./steamPerformanceMonitor": { steamPerformanceMonitor: () => null },
  };
  vm.runInNewContext(code, { exports, React, require: name => { assert.ok(name in imports, name); return imports[name]; } });
  const tree = exports.PerformanceSettings({ locale, appId: 42 });
  const all = [];
  function visit(value) {
    if (Array.isArray(value)) return value.forEach(visit);
    if (value && typeof value === "object") { all.push(value); value.children?.forEach(visit); }
  }
  visit(tree);
  return { all, state, get writes() { return writes; }, effects };
}

test("all twelve locales expose source, four bright text colors, detail, position and background without a refresh selector", () => {
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi", "italian", "german", "koreana", "schinese"]) {
    const f = render(locale), dropdowns = f.all.filter(node => node.type === "Dropdown");
    assert.equal(dropdowns.length, 4, locale);
    assert.deepEqual(dropdowns.map(node => node.props.selectedOption), ["live", "white", 4, 2]);
    assert.deepEqual(Array.from(dropdowns[1].props.rgOptions, option => option.data), ["white", "yellow", "red", "green"]);
    assert.ok(dropdowns[1].props.rgOptions.every(option => option.label && option.label.length > 0));
    assert.equal(f.all.filter(node => node.type === "Toggle").length, 2);
    assert.ok(dropdowns.every(node => node.props.rgOptions.every(option => !/\b(?:20|50|100)\s*ms\b/.test(option.label))));
    assert.equal(f.writes, 0);
    assert.equal(f.state.refreshMs, 20);
    assert.ok(f.effects > 0);
  }
});

// The default reflects the product choice rather than the saved enabled bit.
for (const saved of [null, "", "unknown"]) test(`missing/unknown choice ${saved} defaults to Playhub while disabled`, () => {
 const f=render("en",saved,false);const select=f.all.find(n=>n.type==="Dropdown");assert.equal(select.props.selectedOption,"live");assert.equal(f.all.some(n=>n.type==="SteamCounter"),false);assert.equal(f.writes,0);
});
test("saved Steam choice remains selected",()=>{const f=render("en","steam",false);assert.equal(f.all.find(n=>n.type==="Dropdown").props.selectedOption,"steam");assert.equal(f.all.some(n=>n.type==="SteamCounter"),true);assert.equal(f.writes,0)});
