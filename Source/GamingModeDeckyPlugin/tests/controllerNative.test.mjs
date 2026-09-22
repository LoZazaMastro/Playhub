import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
function load(file) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }
  }).outputText, { exports });
  return exports;
}
const { readSteamControllers: read, controllerActionReason: reason, openControllerAction: open } = load("controllerNative.ts");
const { controllerSettingsLocale: copy } = load("controllerSettingsLocale.ts");
function fixture(capabilities = 0n) {
  let devices = [{ nControllerIndex: 2, strName: "Reported controller", unVendorID: 0x28de, unProductID: 0x1304, unCapabilities: capabilities }];
  const store = { GetControllers: () => devices };
  const paths = [];
  const router = { Navigate: path => paths.push(path), MainRunningAppID: 42 };
  return { store, router, paths, device: read(store)[0], replace: next => { devices = next; } };
}
test("receiver identity alone never proves gyro or stick support", () => {
  const f = fixture();
  assert.equal(f.device.hardware, "28de:1304");
  assert.equal(reason("gyro", f.device, f.router), "unsupported");
  assert.equal(reason("sticks", f.device, f.router), "unsupported");
  assert.equal(open("gyro", f.device, f.store, f.router), "unsupported");
  assert.deepEqual(f.paths, []);
});
test("native capability bits gate navigation, with no settings writes", () => {
  const f = fixture((1n << 11n) | (1n << 2n));
  for (const action of ["test", "settings", "sticks", "gyro", "layout"]) assert.equal(open(action, f.device, f.store, f.router), "ready");
  assert.deepEqual(f.paths, ["/controller/devicesupport/2", "/settings/controller/controller/2", "/controller/calibration/2/Inputs", "/controller/calibration/2/Gyro", "/app/42/controllerconfigurator/main"]);
});
test("missing capability data is distinct from unsupported hardware", () => {
  const f = fixture(null);
  assert.equal(reason("gyro", f.device, f.router), "unknownCapabilities");
  assert.equal(reason("test", f.device, f.router), "ready");
});
test("disconnect and index reuse are revalidated at click time", () => {
  const f = fixture(1n << 11n);
  f.replace([{ nControllerIndex: 2, strName: "Other", unVendorID: 1, unProductID: 2, unCapabilities: 1n << 11n }]);
  assert.equal(open("gyro", f.device, f.store, f.router), "changed");
  f.replace([]);
  assert.equal(open("test", f.device, f.store, f.router), "changed");
  assert.deepEqual(f.paths, []);
});
test("missing runtime, no game, malformed indices and duplicate reports fail closed", () => {
  const f = fixture();
  assert.equal(reason("test", f.device, {}), "nativeUnavailable");
  assert.equal(reason("test", undefined, f.router), "noController");
  for (const id of [NaN, Infinity, -1, 0, "42", 2 ** 40]) {
    f.router.MainRunningAppID = id;
    assert.equal(open("layout", f.device, f.store, f.router), "noGame");
  }
  f.replace([{ nControllerIndex: NaN }, { nControllerIndex: -1 }, { nControllerIndex: "2" }, { nControllerIndex: 2 }, { nControllerIndex: 2 }]);
  assert.equal(read(f.store).length, 1);
  assert.equal(read({ GetControllers() { throw Error("offline"); } }).length, 0);
  assert.deepEqual(f.paths, []);
});
test("all 12 locales and Steam aliases contain reason and action copy", () => {
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "zh", "ja", "ko", "hi"]) {
    assert.equal(Object.keys(copy(locale)).length, 13);
    assert.ok(Object.values(copy(locale)).every(value => typeof value === "string" && value.length > 0));
  }
  assert.deepEqual(copy("italian"), copy("it"));
  assert.deepEqual(copy("koreana"), copy("ko"));
});
