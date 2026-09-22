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
const ast = ts.createSourceFile("ControlCenter.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const component = ast.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === "DeviceInfo");
function render(info) {
  const calls = [], toasts = [], exports = {};
  let hook = 0;
  const React = {
    useState: () => [[info, false, false][hook++], () => {}],
    useEffect: () => {},
    createElement: (type, props, ...children) => ({ type, props: props ?? {}, children }),
  };
  vm.runInNewContext(ts.transpileModule(`${component.getText(ast)}\nexport { DeviceInfo };`, { compilerOptions: options }).outputText, {
    exports, React, deviceInfoLocale, DialogButton: "button", TbBrandWindowsFilled: "tabler-windows-icon",
    call: async (...args) => { calls.push(args); return { ok: true }; },
    toaster: { toast: value => toasts.push(value) },
  });
  return { tree: exports.DeviceInfo({ locale: "en" }), calls, toasts };
}
const info = { device_name: "Desk PC", gpu: ["RX 9070 XT"], storage_total_bytes: 1024 ** 4, windows_edition: "Windows 11 Pro", windows_version: "10.0.26100", windows_update_available: true };
test("device rows show name/GPU/storage and edition before Windows version", () => {
  const { tree } = render(info);
  const output = JSON.stringify(tree);
  for (const text of ["Desk PC", "RX 9070 XT", "1024.0 GiB", "Windows 11 Pro", "10.0.26100"]) assert.ok(output.includes(text));
  assert.ok(output.indexOf("Windows edition") < output.indexOf("Windows version"));
});
test("unknown capacity remains unavailable instead of an invented zero", () => {
  const { tree } = render({ ...info, storage_total_bytes: NaN });
  assert.ok(!JSON.stringify(tree).includes("0.0 GiB"));
  assert.ok(JSON.stringify(tree).includes("Unavailable"));
});
test("Device information contains no Windows Update action", () => {
  const { tree, calls } = render(info);
  assert.ok(!tree.children.some(child => child?.type === "button"));
  assert.deepEqual(calls, []);
  assert.ok(!source.includes('"open_windows_update"'));
});

test("12 locales contain device labels and localized error/loading states", () => {
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const copy = deviceInfoLocale(locale);
    assert.ok(Object.values(copy).every(value => typeof value === "string" && value.length));
    if (locale !== "en") assert.notEqual(copy.loadError, deviceInfoLocale("en").loadError);
    assert.deepEqual(deviceInfoLocale(`${locale}-XX`), copy);
  }
  assert.deepEqual(deviceInfoLocale("italian"), deviceInfoLocale("it"));
  assert.match(source, /<DeviceInfo locale=\{locale\}/);
});
