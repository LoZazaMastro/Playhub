import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

function load(file, scope = {}, suffix = "") {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL(`../src/${file}`, import.meta.url), "utf8") + suffix, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
  }).outputText, { exports, ...scope }, { timeout: 1000 });
  return exports;
}
const api = load("controllerProfiles.ts");
const plain = value => JSON.parse(JSON.stringify(value));
const memory = () => {
  const items = new Map();
  return { items, getItem: key => items.get(key) ?? null, setItem: (key, value) => items.set(key, value) };
};
test("three isolated slots persist and reload validated independent profiles", () => {
  const storage = memory();
  for (let slot = 1; slot <= 3; slot++) {
    const profile = api.defaultControllerProfile();
    profile.leftDeadzone = slot / 10;
    assert.equal(api.saveControllerProfile(storage, slot, profile), "saved");
    assert.deepEqual(plain(api.loadControllerProfile(storage, slot).profile), plain(profile));
  }
  assert.equal(storage.items.size, 3);
  assert.equal(api.saveControllerProfile(storage, 4, api.defaultControllerProfile()), "storageError");
  assert.equal(storage.items.size, 3);
});
test("schema rejects unknown versions, extra fields, malformed mappings and non-finite zones", () => {
  const base = api.defaultControllerProfile();
  for (const value of [null, [], {}, { ...base, version: 2 }, { ...base, activate: true },
    { ...base, mapping: {} }, { ...base, mapping: { ...base.mapping, A: "Gyro" } },
    ...[NaN, Infinity, -1, 0.51, "0.1"].flatMap(zone => [{ ...base, leftDeadzone: zone }, { ...base, rightDeadzone: zone }])]) {
    assert.equal(api.parseControllerProfile(value), null);
    const storage = memory();
    assert.equal(api.saveControllerProfile(storage, 1, value), "invalid");
    assert.equal(storage.items.size, 0);
  }
});
test("corrupt and unavailable storage are reported without overwriting stored data", () => {
  for (const raw of ["{", "null", "x".repeat(4097), '{"version":2}']) {
    const storage = memory();
    storage.setItem("playhub.controller.profile.v1.1", raw);
    assert.equal(api.loadControllerProfile(storage, 1).status, "invalid");
    assert.equal(storage.getItem("playhub.controller.profile.v1.1"), raw);
  }
  const denied = { getItem() { throw Error("denied"); }, setItem() { throw Error("quota"); } };
  assert.equal(api.loadControllerProfile(denied, 1).status, "storageError");
  assert.equal(api.saveControllerProfile(denied, 1, api.defaultControllerProfile()), "storageError");
});
test("many-to-one, disabled buttons and release are deterministic without mutating input", () => {
  const profile = api.defaultControllerProfile();
  profile.mapping.A = "B"; profile.mapping.X = "B"; profile.mapping.Y = null;
  const before = JSON.stringify(profile);
  for (const pressed of [["A", "X"], ["X"], ["A", "Y"]]) assert.deepEqual(plain(api.previewControllerProfile(profile, pressed).buttons), ["B"]);
  assert.deepEqual(plain(api.previewControllerProfile(profile, ["Y"]).buttons), []);
  assert.deepEqual(plain(api.previewControllerProfile(profile, []).buttons), []);
  assert.equal(JSON.stringify(profile), before);
});
test("radial deadzones neutralize center, rescale, preserve direction and bound diagonals", () => {
  const profile = api.defaultControllerProfile();
  profile.leftDeadzone = 0.2;
  assert.deepEqual(plain(api.previewControllerProfile(profile, [], [0.1, 0.1]).left), [0, 0]);
  const mid = api.previewControllerProfile(profile, [], [0.6, 0]).left;
  assert.ok(Math.abs(mid[0] - 0.5) < 1e-12);
  for (const x of [-1, -0.2, 0, 0.2, 1]) for (const y of [-1, -0.2, 0, 0.2, 1]) {
    const out = api.previewControllerProfile(profile, [], [x, y]).left;
    assert.ok(Math.hypot(...out) <= 1 + 1e-12);
    assert.ok(out.every(Number.isFinite));
  }
});
test("invalid synthetic frames fail closed; no support inferred from USB identities", () => {
  const profile = api.defaultControllerProfile();
  for (const axes of [[NaN, 0], [Infinity, 0], [2, 0], ["0", 0], new Array(2), [], null]) assert.equal(api.previewControllerProfile(profile, [], axes), null);
  assert.equal(api.previewControllerProfile(profile, ["Gyro"]), null);
  assert.equal(api.previewControllerProfile(profile, new Array(1)), null);
  for (const key of ["liveMapping", "virtualOutput", "gyro", "trackpads", "exclusiveMode"]) assert.equal(api.controllerProfileCapabilities[key], false);
  assert.equal(Object.isFrozen(api.controllerProfileCapabilities), true);
});

test("production panel does not expose the synthetic profile editor", () => {
  const source = fs.readFileSync(new URL("../src/index.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(source, /ControllerProfileEditor|controllerProfiles|previewControllerProfile|localStorage/);
  assert.match(source, /controller=\{<PanelSection>/);
  assert.doesNotMatch(source, /<ControllerSettings\b/);
});
