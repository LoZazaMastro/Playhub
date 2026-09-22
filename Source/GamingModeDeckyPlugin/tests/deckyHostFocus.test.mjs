import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

const exports = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/deckyHostStandalone.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports });
function node(attributes = {}, children = []) {
  const values = new Map(Object.entries(attributes));
  return { values, getAttribute: name => values.get(name) ?? null,
    setAttribute: (name, value) => values.set(name, value), removeAttribute: name => values.delete(name),
    querySelectorAll: () => children };
}
test("native retained tab and content become inert; rollback restores exact identity and attributes", () => {
  const child = node({ tabindex: "3" }), tab = node({ tabindex: "0", "aria-hidden": "false" });
  const content = node({ inert: "existing" }, [child]);
  const doc = { getElementById: id => id === "quickaccess_tab_999" ? tab : content };
  const guard = exports.createNativeDeckyFocusSuppression();
  guard.hide(doc); guard.hide(doc);
  assert.equal(tab.getAttribute("tabindex"), "-1");
  assert.equal(content.getAttribute("inert"), "");
  assert.equal(child.getAttribute("tabindex"), "-1");
  guard.restore(); guard.restore();
  assert.deepEqual(Object.fromEntries(tab.values), { tabindex: "0", "aria-hidden": "false" });
  assert.deepEqual(Object.fromEntries(content.values), { inert: "existing" });
  assert.deepEqual(Object.fromEntries(child.values), { tabindex: "3" });
  assert.equal(doc.getElementById("quickaccess_tab_999"), tab);
});
test("focus rollback does not overwrite later foreign attribute ownership", () => {
  const tab = node(); const guard = exports.createNativeDeckyFocusSuppression();
  guard.hide({ getElementById: id => id === "quickaccess_tab_999" ? tab : null });
  tab.setAttribute("tabindex", "2"); guard.restore();
  assert.deepEqual(Object.fromEntries(tab.values), { tabindex: "2" });
});
const steamPath = "C:/Program Files (x86)/Steam/steamui/library.js";
test("Steam FocusRing navigation consumes bottom escape only after internal geometry is exhausted", { skip: !existsSync(steamPath) }, (t) => {
  const steam = readFileSync(steamPath, "utf8");
  if (!["OnNavigationEvent(e){", "InternalFocusDescendant(", "GetFocusable(){", "BTakeFocus("].every(marker => steam.includes(marker))) {
    t.skip("Installed Steam minified FocusRing no longer matches the captured adapter; integration fixture needs recapture"); return;
  }
  const method = (start, end) => {
    const begin = steam.indexOf(start), finish = steam.indexOf(end, begin);
    assert.ok(begin >= 0 && finish > begin); return steam.slice(begin, finish);
  };
  const context = vm.createContext({ h: { pR: { DIR_UP: 1, DIR_RIGHT: 2, DIR_DOWN: 3, DIR_LEFT: 4 } } });
  vm.runInContext(`this.Native = class { ${method("OnNavigationEvent(e){", "InternalFocusDescendant(")} ${method("GetFocusable(){", "BTakeFocus(")} };`, context);
  const source = readFileSync(new URL("../src/ControlCenter.tsx", import.meta.url), "utf8");
  const ast = ts.createSourceFile("ControlCenter.tsx", source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  let boundary;
  const visit = n => {
    if (ts.isJsxAttribute(n) && n.name.getText(ast) === "onMoveDown") boundary = n.initializer.expression.getText(ast);
    ts.forEachChild(n, visit);
  }; visit(ast); assert.ok(boundary);
  const native = new context.Native();
  native.m_Properties = { onMoveDown: vm.runInContext(`(${boundary})`, context) };
  // Geometry fixture: an eligible lower control wins, then the container boundary.
  const rows = [{ top: 0, bottom: 40 }, { top: 52, bottom: 92 }]; let active = 0, internalMoves = 0;
  native.BTryInternalNavigation = direction => {
    const next = rows.findIndex(row => row.top >= rows[active].bottom);
    if (direction !== 3 || next < 0) return false;
    active = next; internalMoves++; return true;
  };
  assert.equal(native.OnNavigationEvent({ detail: { button: 3 } }), true);
  assert.equal(active, 1);
  assert.equal(native.OnNavigationEvent({ detail: { button: 3 } }), true, "event cannot bubble into native Decky");
  assert.equal(internalMoves, 1);
  assert.equal(native.OnNavigationEvent({ detail: { button: 1 } }), false, "up remains available");
  native.m_bMounted = true; native.m_rgChildren = [{}];
  native.m_Properties = { focusable: false, childFocusDisabled: true };
  assert.equal(native.GetFocusable(), "none");
  native.m_Properties.childFocusDisabled = false;
  assert.equal(native.GetFocusable(), "children");
});
