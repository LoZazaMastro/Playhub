import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import ts from "typescript";
import vm from "node:vm";

const source = readFileSync(new URL("../src/DashboardPage.tsx", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX }
}).outputText;
const exports = {};
vm.runInNewContext(compiled, { exports, require: () => new Proxy({}, { get: () => () => null }),
  window: { requestAnimationFrame: () => 0 }, document: {}, console });

const node = (rect, style, children = 1) => ({
  children: [], childElementCount: children, contains: () => false,
  getBoundingClientRect: () => rect, __style: style,
});
function build(nodes, root) {
  const styles = new Map(nodes.map(n => [n, n.__style]));
  const body = { children: nodes, childElementCount: nodes.length, contains: () => false, getBoundingClientRect: () => ({}) };
  const view = { innerWidth: 1920, innerHeight: 1080, getComputedStyle: n => styles.get(n) ?? {} };
  return { ...root, ownerDocument: { body, defaultView: view } };
}

/** A guessed inset either wastes space above the footer or hides the last row behind it. */
test("the reserved band is Steam's actual footer, measured, not a hardcoded 64px", () => {
  const footer = node({ top: 1002, bottom: 1080, left: 0, right: 1920, width: 1920, height: 78 }, { position: "fixed" });
  const header = node({ top: 0, bottom: 96, left: 0, right: 1920, width: 1920, height: 96 }, { position: "fixed" });
  const root = { contains: () => false };
  assert.equal(exports.measureSteamFooterInset(build([header, footer], root)), 78);

  // A taller footer is followed exactly; that was the row hidden behind it.
  const tall = node({ top: 968, bottom: 1080, left: 0, right: 1920, width: 1920, height: 112 }, { position: "fixed" });
  assert.equal(exports.measureSteamFooterInset(build([tall], root)), 112);

  // Nothing at the bottom, an empty element, or a hidden one: keep the safe default.
  assert.equal(exports.measureSteamFooterInset(build([header], root)), 64);
  const empty = node({ top: 1002, bottom: 1080, left: 0, right: 1920, width: 1920, height: 78 }, { position: "fixed" }, 0);
  assert.equal(exports.measureSteamFooterInset(build([empty], root)), 64);
  const hidden = node({ top: 1002, bottom: 1080, left: 0, right: 1920, width: 1920, height: 78 }, { position: "fixed", visibility: "hidden" });
  assert.equal(exports.measureSteamFooterInset(build([hidden], root)), 64);
  // A narrow bottom-anchored widget is not the footer.
  const widget = node({ top: 1002, bottom: 1080, left: 1600, right: 1920, width: 320, height: 78 }, { position: "fixed" });
  assert.equal(exports.measureSteamFooterInset(build([widget], root)), 64);
});

test("the page reserves the measured band and the picker grid owns the whole scrollport", () => {
  assert.match(source, /\.ph-main \{ height: calc\(100% - 108px\); padding: 10px 54px var\(--ph-footer, 64px\); overflow: hidden; \}/);
  assert.match(source, /padding-bottom: var\(--ph-footer, 64px\)/);
  assert.match(source, /element\.style\.setProperty\("--ph-footer"/);
  assert.match(source, /grid\.style\.height = `\$\{viewport\.clientHeight\}px`/);
  assert.doesNotMatch(source, /padding: 10px 54px 64px/, "no hardcoded footer band remains");
});
