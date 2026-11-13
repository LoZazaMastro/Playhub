import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const options = { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React };
const localeExports = {};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL("../src/deviceInfoLocale.ts", import.meta.url), "utf8"), { compilerOptions: options }).outputText, { exports: localeExports });
const { deviceInfoLocale } = localeExports;
const source = fs.readFileSync(new URL("../src/ControlCenter.tsx", import.meta.url), "utf8");
const deviceSource = fs.readFileSync(new URL("../src/DeviceSettings.tsx", import.meta.url), "utf8");
const ast = ts.createSourceFile("DeviceSettings.tsx", deviceSource, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
function render(componentName, props, states) {
  const calls = [], effects = [], exports = {};
  let hook = 0;
  const React = {
    useState: () => [states[hook++], () => {}],
    useEffect: effect => effects.push(effect),
    useRef: value => ({ current: value }),
    createElement: (type, props, ...children) => ({ type, props: props ?? {}, children }),
  };
  const component = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === componentName);
  const icons = Object.fromEntries(["Battery", "Bolt", "ChevronDown", "ChevronRight", "DeviceDesktop", "DeviceGamepad2"].map(name => [`Tb${name}`, name]));
  vm.runInNewContext(ts.transpileModule(`${component.getText(ast).replace(/^export /, "")}\nexport { ${componentName} };`, { compilerOptions: options }).outputText, {
    exports, React, deviceInfoLocale, style: "", DialogButton: "button", DropdownItem: "select", Focusable: "div", DeviceInfo: "details", DeviceCapabilities: "capabilities", Ayaneo3ControllerControls: "ayaneo-controls", ...icons,
    call: async (...args) => { calls.push(args); return { ok: true }; },
  });
  return { tree: exports[componentName](props), calls, effects };
}
const renderDetails = info => render("DeviceInfo", { copy: deviceInfoLocale("en"), model: "Fixture" }, [info, false]);
const info = { device_name: "Desk PC", gpu: ["RX 9070 XT"], storage_total_bytes: 1024 ** 4, windows_edition: "Windows 11 Pro", windows_version: "10.0.26100", windows_update_available: true };
test("device rows show name/GPU/storage and edition before Windows version", () => {
  const { tree } = renderDetails(info);
  const output = JSON.stringify(tree);
  for (const text of ["Desk PC", "RX 9070 XT", "1024.0 GiB", "Windows 11 Pro", "10.0.26100"]) assert.ok(output.includes(text));
  assert.ok(output.indexOf("Windows edition") < output.indexOf("Windows version"));
});
test("unknown capacity remains unavailable instead of an invented zero", () => {
  const { tree } = renderDetails({ ...info, storage_total_bytes: NaN });
  assert.ok(!JSON.stringify(tree).includes("0.0 GiB"));
  assert.ok(JSON.stringify(tree).includes("Unavailable"));
});
test("Device information contains no Windows Update action", () => {
  const { tree, calls } = renderDetails(info);
  assert.ok(!tree.children.some(child => child?.type === "button"));
  assert.deepEqual(calls, []);
  assert.ok(!deviceSource.includes('"open_windows_update"'));
});

test("manual handheld profile displays experimental status without enabling unsupported controls", () => {
  const profile = { selection: "handheld", detected: "pc", effective: "handheld", manufacturer: "Fixture", model: "A test PC", handhelds: [], power: { available: false }, display: null, powerModeAvailable: false };
  const props = { locale: "en", openVideo: () => {}, openPerformance: () => {} };
  const absent = JSON.stringify(render("DeviceSettings", props, [profile]).tree);
  assert.match(absent, /Experimental/);
  assert.doesNotMatch(absent, /Primary display|Video controls|Power controls|Plugged in/);
  const supported = { ...profile, power: { available: true, battery: true, percent: 63, source: "ac" }, display: { width: 1280, height: 800, hz: 90 }, powerModeAvailable: true };
  const present = JSON.stringify(render("DeviceSettings", props, [supported]).tree);
  assert.match(present, /63% · Plugged in/);
  assert.match(present, /Primary display/);
  assert.match(present, /capabilities/);
  const matrix = render("DeviceSettings", props, [supported]).tree.children.find(child => child?.type === "capabilities");
  assert.equal(matrix.props.selection, "handheld");
  assert.equal(matrix.props.detected, "pc");
  assert.equal(matrix.props.openVideo, props.openVideo);
  assert.equal(matrix.props.openPerformance, props.openPerformance);
});

test("battery health loads only for a reported battery and remains distinct from charge level", () => {
  const base = { selection: "auto", detected: "pc", effective: "pc", manufacturer: "", model: "", handhelds: [], display: null, powerModeAvailable: false };
  const props = { locale: "en" };
  const desktop = render("DeviceSettings", props, [{ ...base, power: { available: true, battery: false } }, undefined, false, 0, 91]);
  desktop.effects[1]();
  assert.deepEqual(desktop.calls, []);
  assert.doesNotMatch(JSON.stringify(desktop.tree), /Battery health/);
  const portable = { ...base, power: { available: true, battery: true, source: "battery", percent: 55 } };
  const pending = render("DeviceSettings", props, [portable]);
  const stop = pending.effects[1]();
  assert.deepEqual(pending.calls, [["get_battery_health"]]);
  stop();
  assert.doesNotMatch(JSON.stringify(pending.tree), /Battery health/);
  const read = render("DeviceSettings", props, [portable, undefined, false, 0, 91]);
  const output = JSON.stringify(read.tree);
  assert.match(output, /55% · On battery/);
  assert.match(output, /Battery health · 91%/);
});

test("device details use the persisted parent preference and never reset it on mount", () => {
  const profile = { selection: "auto", detected: "pc", effective: "pc", manufacturer: "Fixture", model: "PC", handhelds: [], power: { available: false }, display: null, powerModeAvailable: false };
  let toggles = 0;
  const nodes = value => !value || typeof value !== "object" ? [] : Array.isArray(value)
    ? value.flatMap(nodes) : [value, ...nodes(value.children)];
  const props = { locale: "en", detailsOpen: true, onDetailsToggle: () => { toggles++; } };
  const first = render("DeviceSettings", props, [profile]);
  assert.equal(nodes(first.tree).find(node => node.props?.className === "ph-device-details").props["aria-expanded"], true);
  assert.ok(nodes(first.tree).some(node => node.type === "details"));
  assert.equal(toggles, 0);
  nodes(first.tree).find(node => node.props?.className === "ph-device-details").props.onClick();
  assert.equal(toggles, 1);
  const reopened = render("DeviceSettings", { ...props, detailsOpen: false }, [profile]);
  assert.equal(nodes(reopened.tree).find(node => node.props?.className === "ph-device-details").props["aria-expanded"], false);
  assert.ok(!nodes(reopened.tree).some(node => node.type === "details"));
  assert.equal(toggles, 1);
});

test("12 locales contain device labels and localized error/loading states", () => {
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const copy = deviceInfoLocale(locale);
    assert.ok(Object.values(copy).every(value => typeof value === "string" && value.length));
    if (locale !== "en") assert.notEqual(copy.loadError, deviceInfoLocale("en").loadError);
    assert.deepEqual(deviceInfoLocale(`${locale}-XX`), copy);
  }
  assert.deepEqual(deviceInfoLocale("italian"), deviceInfoLocale("it"));
  assert.match(source, /<DeviceSettings locale=\{locale\}/);
});
