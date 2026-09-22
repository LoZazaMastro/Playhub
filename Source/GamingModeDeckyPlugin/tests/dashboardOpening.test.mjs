import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const source = readFileSync(new URL("../src/index.tsx", import.meta.url), "utf8");
const ast = ts.createSourceFile("index.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
const opening = ast.statements.find((node) => ts.isFunctionDeclaration(node) && node.name?.text === "openDashboard");
assert.ok(opening);
const compiled = ts.transpileModule(opening.getText(ast), {
  compilerOptions: { target: ts.ScriptTarget.ES2022 },
}).outputText;

async function run(router, overlayReady) {
  const calls = [];
  const context = {
    Router: router, logToAgent: () => {}, readEnvironment: async () => ({ enabled: true }),
    preloadDashboardWindows: async () => {}, captureDashboardSourceFocus: () => {},
    prepareDashboardOverlay: async () => overlayReady,
    requestDashboardSteamFocus: async () => calls.push("os-focus"),
    restoreDashboardSourceFocus: () => calls.push("os-restore"),
    markDashboardChrome: () => {}, clearDashboardChrome: () => {},
    focusDashboardSurface: () => {}, DASHBOARD_ROUTE: "/playhub-dashboard",
    EWindowBringToFront_AndForceOS: 1,
    Navigation: { CloseSideMenus: () => calls.push("close-menus"), Navigate: () => calls.push("navigate") },
    window: { setTimeout: () => 1, SteamClient: { Window: { BringToFront: () => calls.push("bring-to-front") } } },
  };
  vm.createContext(context);
  vm.runInContext(compiled, context);
  await context.openDashboard();
  return calls;
}

test("failed native overlay never forces desktop focus for a running game", async () => {
  assert.deepEqual(await run({ MainRunningAppID: 620 }, false), []);
  assert.deepEqual(await run({ MainRunningApp: { appid: 2147483648 } }, false), []);
});
test("native overlay establishes content before closing QAM to avoid a hide activation", async () => {
  assert.deepEqual(await run({ MainRunningAppID: 620 }, true), ["navigate", "close-menus"]);
});
test("desktop-only opening keeps its separate focus behavior", async () => {
  assert.deepEqual(await run({}, false), ["os-focus", "bring-to-front", "close-menus"]);
});
test("obsolete global focus rescue is removed but desktop constant remains", () => {
  assert.doesNotMatch(source, /installFocusRescue|overlayHookedInGame/);
  assert.match(source, /const EWindowBringToFront_AndForceOS = 1;/);
});
