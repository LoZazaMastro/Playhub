import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, "..", "src", "shortcutOrder.ts"), "utf8");
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const exports = {};
vm.runInNewContext(compiled, { exports, Set, Map, JSON });

const entries = (...ids) => ids.map((id) => ({ id, name: id.toUpperCase() }));

test("saved order survives a reload and appends newly discovered shortcuts", () => {
  const values = new Map();
  const storage = {
    getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value),
  };
  assert.equal(exports.writeShortcutOrder(storage, entries("c", "a", "b")), true);
  const restored = exports.applyShortcutOrder(entries("a", "b", "c", "new"), exports.readShortcutOrder(storage));
  assert.deepEqual(restored.map(({ id }) => id), ["c", "a", "b", "new"]);
});

test("moving to a destination works in both directions", () => {
  assert.deepEqual(Array.from(exports.moveShortcutTo(entries("a", "b", "c"), "b", "c"), ({ id }) => id), ["a", "c", "b"]);
  assert.deepEqual(Array.from(exports.moveShortcutTo(entries("a", "b", "c"), "b", "a"), ({ id }) => id), ["b", "a", "c"]);
});

test("cancel restores the snapshot without persisting the temporary order", () => {
  const original = entries("a", "b", "c");
  const moved = exports.moveShortcutTo(original, "a", "c");
  const restored = exports.applyShortcutOrder(moved, original.map(({ id }) => id));
  assert.deepEqual(Array.from(restored, ({ id }) => id), ["a", "b", "c"]);
});

test("malformed persisted values fail closed", () => {
  const storage = { getItem: () => "not-json", setItem: () => {} };
  assert.deepEqual(Array.from(exports.readShortcutOrder(storage)), []);
});
