import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = fs.readFileSync(new URL("../src/steamReturnControllerLayout.ts", import.meta.url), "utf8");
const compiled = ts.transpile(source, { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 });
const { activeSteamController, createSteamReturnControllerLayout } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);
const connected = {};

test("Steam's menu layout is scoped to the exited shortcut and returns to automatic when tracking clears", () => {
  const calls = [];
  const cleared = [];
  const app = 2485417537;
  const layout = createSteamReturnControllerLayout(url => calls.push(url), () => 1, id => cleared.push(id), () => connected);
  layout.begin(app, app + 1);
  layout.begin(app, 0);
  assert.deepEqual(calls, []);
  layout.begin(app, app);
  layout.begin(app, app);
  layout.observe(app, true);
  assert.deepEqual(calls, ["steam://forceinputappid/769"]);
  layout.observe(0, true);
  layout.release();
  assert.deepEqual(calls, ["steam://forceinputappid/769", "steam://forceinputappid/0"]);
  assert.deepEqual(cleared, [1]);
});

test("leaving the main Steam window releases the temporary layout", () => {
  const calls = [];
  const app = 2485417537;
  const layout = createSteamReturnControllerLayout(url => calls.push(url), () => 1, () => {}, () => connected);
  layout.begin(app, app);
  layout.observe(app, false);
  layout.begin(app, app);
  layout.observe(app, false);
  assert.deepEqual(calls, ["steam://forceinputappid/769", "steam://forceinputappid/0"]);
  // A new launch releases/reset the previous exit's ownership.
  layout.release();
  layout.begin(app, app);
  assert.equal(calls.at(-1), "steam://forceinputappid/769");
});

test("the scoped layout has a deadline even when Steam keeps reporting the shortcut", () => {
  let deadline, delayMs;
  const calls = [];
  const layout = createSteamReturnControllerLayout(url => calls.push(url), (callback, delay) => {
    deadline = callback;
    delayMs = delay;
    return 2;
  }, () => {}, () => connected);
  layout.begin(2485417537, 2485417537);
  assert.equal(delayMs, 4000);
  deadline();
  assert.deepEqual(calls, ["steam://forceinputappid/769", "steam://forceinputappid/0"]);
});

function store(controllers, type = "controller_steamcontroller_triton") {
  return { GetControllers: () => controllers,
    GetController: index => controllers.find(item => item.nControllerIndex === index),
    GetControllerTypeString: () => type };
}
const triton = () => ({ nControllerIndex: 1, eControllerType: 49, unVendorID: 0x28de,
  unProductID: 0x1302, bRemoteDevice: false, strName: "User-selected name" });

test("native connected Steam type and physical identity jointly gate the compatibility path", () => {
  const controller = triton();
  assert.equal(activeSteamController(store([controller])), controller);
  assert.equal(activeSteamController(store([{ ...controller, unProductID: 0x1304 }])).unProductID, 0x1304);
  const original = { ...controller, eControllerType: 10, unProductID: 0x1102 };
  assert.equal(activeSteamController(store([original], "controller_steamcontroller_gordon")), original);
  for (const item of [
    { ...controller, unVendorID: 0x045e, unProductID: 0x028e, strName: "Steam Controller 2" },
    { ...controller, unVendorID: 0x054c, unProductID: 0x0ce6 },
    { ...controller, unProductID: 0x11ff }, { ...controller, unProductID: 0x1205 },
    { ...controller, bRemoteDevice: true }, { ...controller, bRemoteDevice: undefined },
    { ...controller, eControllerType: "49" }, { ...controller, nControllerIndex: 16 }
  ]) assert.equal(activeSteamController(store([item])), undefined);
  assert.equal(activeSteamController(store([controller], "controller_xbox360")), undefined);
  assert.equal(activeSteamController(store([controller], "controller_steamcontroller_unknown")), undefined);
  assert.equal(activeSteamController(store([controller], "controller_steamcontroller_neptune")), undefined);
  assert.equal(activeSteamController(store([controller, { ...controller }])), undefined);
  assert.equal(activeSteamController({ ...store([controller]), GetController: () => ({ ...controller }) }), undefined);
});

test("missing or broken Steam inventory never guesses support from a device name", () => {
  for (const inventory of [undefined, {}, store([]), { GetControllers: () => [{ strName: "SteamController2" }] },
    { ...store([triton()]), GetControllerTypeString() { throw Error("Steam reloaded"); } }])
    assert.equal(activeSteamController(inventory), undefined);
  const calls = [];
  createSteamReturnControllerLayout(url => calls.push(url), () => 1, () => {}).begin(2485417537, 2485417537);
  assert.deepEqual(calls, [], "the default unknown inventory cannot force 769");
});

test("disconnect and same-model reconnect release only the existing menu lease", () => {
  let current = triton();
  const original = current;
  const calls = [];
  const layout = createSteamReturnControllerLayout(url => calls.push(url), () => 1, () => {}, () => current);
  layout.begin(2485417537, 2485417537);
  current = undefined;
  layout.observe(2485417537, true);
  current = { ...original };
  layout.begin(2485417537, 2485417537);
  assert.deepEqual(calls, ["steam://forceinputappid/769", "steam://forceinputappid/0"]);
  layout.release();
  layout.begin(2485417537, 2485417537);
  current = { ...current };
  layout.observe(2485417537, true);
  assert.deepEqual(calls.slice(-2), ["steam://forceinputappid/769", "steam://forceinputappid/0"],
    "a replaced native controller object cannot inherit an older lease");
});

const steamSourcePath = process.env.PLAYHUB_STEAM_UI_SOURCE ?? "C:/Program Files (x86)/Steam/steamui/sp.js";
test("the installed Steam controller getters expose typed connected objects, not translated display names",
  { skip: !fs.existsSync(steamSourcePath) }, () => {
    const native = fs.readFileSync(steamSourcePath, "utf8");
    const typeGetter = native.match(/GetControllerTypeString\(([\w$]+)\)\{return O\[\1\]\?\?O\[a\.([\w$]+)\]\}/)?.[0];
    const connectedGetter = native.match(/GetControllers\(\)\{return this\.m_controllerList\}/)?.[0];
    const indexGetter = native.match(/GetController\(([\w$]+)\)\{return this\.m_controllerList\.find\(([\w$]+)=>\2\.nControllerIndex==\1\)\}/)?.[0];
    assert.ok(typeGetter && connectedGetter && indexGetter, "the actual native ControllerStore contract must remain available");
    for (const key of ["controller_steamcontroller_gordon", "controller_steamcontroller_triton"])
      assert.ok(native.includes(`]:"${key}"`), "the selector must use Steam's internal enum key");
    // Evaluate only these three stateless getters with inert fixture data, never
    // the Steam bundle, its imports, UI stores or a real controller connection.
    const fallback = typeGetter.match(/O\[a\.([\w$]+)\]/)[1];
    const typed = Function("O", "a", `return ({${typeGetter},${connectedGetter},${indexGetter}})`)(
      { 49: "controller_steamcontroller_triton", 0: "controller_unknown" }, { [fallback]: 0 });
    const controller = triton();
    typed.m_controllerList = [controller];
    assert.equal(activeSteamController(typed), controller);
    typed.m_controllerList = [];
    assert.equal(activeSteamController(typed), undefined, "removal from Steam's real connected-list accessor revokes eligibility");
  });
