import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = readFileSync(new URL("../src/steamFocusEvents.ts", import.meta.url), "utf8");
const compiled = ts.transpile(source, { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 });
const { observeSteamOverlayFocus } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);

function store() {
  const value = { m_unFocusedOverlayAppID: 0, m_unFocusedOverlayPID: 0,
    GetFocusedOverlayAppID() { return this.m_unFocusedOverlayAppID; },
    GetFocusedOverlayPID() { return this.m_unFocusedOverlayPID; },
    OnOverlayGameWindowFocusChanged(appId, pid) {
      this.m_unFocusedOverlayAppID = appId; this.m_unFocusedOverlayPID = pid; return "Valve focus result";
    } };
  // Match Valve's MobX-bound callback: the native slot captures it at Init.
  value.OnOverlayGameWindowFocusChanged = value.OnOverlayGameWindowFocusChanged.bind(value);
  return { value, native: value.OnOverlayGameWindowFocusChanged };
}

test("a previously captured bound native callback still updates Valve state and the existing poll observes it", () => {
  const f = store(), changes = [];
  const observer = observeSteamOverlayFocus(() => f.value, (...args) => changes.push(args));
  observer.refresh();
  const original = f.value.OnOverlayGameWindowFocusChanged;
  let propertyCalls = 0;
  f.value.OnOverlayGameWindowFocusChanged = (...args) => { propertyCalls++; return original(...args); };
  assert.equal(f.native(0xa1234567, 1234), "Valve focus result");
  assert.equal(propertyCalls, 0, "after-patching the property cannot observe a callback native already captured");
  assert.deepEqual(changes, []);
  observer.refresh(); observer.refresh();
  assert.deepEqual(changes, [[0xa1234567, 1234]]);
  assert.equal(f.value.GetFocusedOverlayAppID(), 0xa1234567);
  f.native(0, 0); observer.refresh();
  assert.deepEqual(changes.at(-1), [0, 0]);
  observer.dispose();
  assert.equal(f.value.OnOverlayGameWindowFocusChanged(42, 1), "Valve focus result");
});

test("late and replaced stores receive a fresh baseline while stale stores and disposed observers cannot wake", () => {
  const a = store(), b = store(), changes = [];
  let current;
  const observer = observeSteamOverlayFocus(() => current, (...args) => changes.push(args));
  observer.refresh(); current = a.value; observer.refresh(); a.native(42, 1); observer.refresh();
  current = b.value; observer.refresh(); a.native(0, 0); observer.refresh();
  assert.deepEqual(changes, [[42, 1]]);
  b.native(43, 2); observer.refresh(); assert.deepEqual(changes.at(-1), [43, 2]);
  observer.dispose(); b.native(0, 0); observer.refresh();
  assert.equal(changes.length, 2);
});

test("each committed tuple is delivered once even when a wake reenters the existing poll", () => {
  const f = store(), changes = [];
  const observer = observeSteamOverlayFocus(() => f.value, (...args) => { changes.push(args); observer.refresh(); });
  observer.refresh(); f.native(42, 1); observer.refresh();
  f.native(42, 1); observer.refresh(); f.native(42, 2); observer.refresh();
  assert.deepEqual(changes, [[42, 1], [42, 2]]);
});

test("unavailable, malformed and throwing observers do not interrupt Valve or publish a fabricated tuple", () => {
  const f = store(); let unavailable = false, calls = 0;
  const observer = observeSteamOverlayFocus(() => { if (unavailable) throw Error("not ready"); return f.value; }, () => { calls++; throw Error("observer"); });
  observer.refresh();
  for (const pair of [[-1, 1], [42, -1], [42.5, 1], [42, NaN], [0x100000000, 1], [42, 0x100000000], ["42", 1]]) {
    f.native(...pair); observer.refresh();
  }
  assert.equal(calls, 0);
  unavailable = true; observer.refresh(); unavailable = false;
  assert.equal(f.native(42, 1), "Valve focus result");
  assert.doesNotThrow(() => observer.refresh()); observer.refresh(); assert.equal(calls, 1);
});

test("a store replaced during its read cannot commit an event from the previous generation", () => {
  const a = store(), b = store(), changes = [];
  let current = a.value, replace = false;
  const observer = observeSteamOverlayFocus(() => current, (...args) => changes.push(args));
  observer.refresh();
  a.value.GetFocusedOverlayPID = () => { if (replace) current = b.value; return a.value.m_unFocusedOverlayPID; };
  a.native(42, 1); replace = true; observer.refresh(); observer.refresh();
  assert.deepEqual(changes, []);
  b.native(43, 2); observer.refresh(); assert.deepEqual(changes, [[43, 2]]);
});
