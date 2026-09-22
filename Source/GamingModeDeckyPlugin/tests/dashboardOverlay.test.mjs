import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const source = readFileSync(new URL("../src/dashboardOverlay.ts", import.meta.url), "utf8");
const exports = {};
vm.runInNewContext(ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });
const info = { gameID: "18446744071562067968", appID: 2147483648, unPID: 10, nBrowserID: 3 };
const candidate = (overrides = {}) => ({
  IsGamepadUIOverlayWindow: () => true,
  params: { browserInfo: { m_gameID: info.gameID, m_unPID: 10, m_nBrowserID: 3 } },
  BrowserWindow: { document: { body: {} } }, ...overrides,
});

test("Steam shortcut/emulator game IDs retain 64-bit precision", () => {
  assert.equal(exports.selectOverlay([info]).gameID, info.gameID);
});
test("ambiguous games fail closed, selected running app resolves identity", () => {
  const other = { ...info, appID: 620, unPID: 11 };
  assert.equal(exports.selectOverlay([info, other]), null);
  assert.equal(exports.selectOverlay([info, other], 620).unPID, 11);
  assert.equal(exports.selectOverlay([info], 620), null);
});
test("missing browser identity and Lossless Scaling are not game targets", () => {
  assert.equal(exports.selectOverlay([{ ...info, nBrowserID: undefined }]), null);
  assert.equal(exports.selectOverlay([{ ...info, appID: 993090 }]), null);
  assert.equal(exports.selectOverlay(null), null);
});
test("only exact native gamepad overlay identity can receive the portal", () => {
  const correct = candidate();
  const desktop = candidate({ IsGamepadUIOverlayWindow: () => false });
  const stale = candidate({ params: { browserInfo: { m_gameID: info.gameID, m_unPID: 10, m_nBrowserID: 2 } } });
  assert.equal(exports.findOverlayWindow([desktop, stale, correct], info), correct);
  assert.equal(exports.findOverlayWindow([desktop, stale], info), null);
});
test("closed and inaccessible browsers are ignored", () => {
  const inaccessible = candidate();
  Object.defineProperty(inaccessible, "BrowserWindow", { get() { throw Error("closed"); } });
  assert.equal(exports.findOverlayWindow([inaccessible, candidate({ BrowserWindow: { closed: true, document: { body: {} } } })], info), null);
});
test("browser readiness retries are bounded", async () => {
  let waits = 0;
  assert.equal(await exports.waitForOverlay(() => null, () => false, async () => { waits++; }), null);
  assert.equal(waits, 40);
});
test("late browser readiness succeeds and cancellation stops stale open", async () => {
  let waits = 0;
  const target = {};
  assert.equal(await exports.waitForOverlay(() => waits === 3 ? target : null, () => false, async () => { waits++; }), target);
  waits = 0;
  assert.equal(await exports.waitForOverlay(() => null, () => waits === 2, async () => { waits++; }), null);
  assert.equal(waits, 2);
});
test("portal target changes do not close the native overlay", () => {
  const page = readFileSync(new URL("../src/DashboardPage.tsx", import.meta.url), "utf8");
  assert.match(page, /if \(inOverlay\) closeDashboardOverlay\(\);\s*\}, \[inOverlay\]\)/);
  assert.match(page, /portalTarget\?\.remove\(\);[^]*?\}, \[portalTarget\]\)/);
  const component = page.slice(page.indexOf("export function DashboardPage()"));
  assert.doesNotMatch(component, /SetOverlayState|restoreDashboardSourceFocus/);
});
