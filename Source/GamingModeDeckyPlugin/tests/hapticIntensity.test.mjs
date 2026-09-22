import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";

const exports = {};
vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL("../src/hapticIntensity.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS },
}).outputText, { exports });
const curve = exports.modernHapticIntensity;

test("native Steam levels and gain increase across the whole slider", () => {
  let previous = curve(5);
  for (let value = 10; value <= 100; value += 5) {
    const current = curve(value);
    assert.ok(current.level >= previous.level);
    assert.ok(current.gain >= previous.gain);
    assert.notEqual(JSON.stringify(current), JSON.stringify(previous));
    previous = current;
  }
  assert.equal(curve(5).level, 0);
  assert.equal(curve(50).level, 1);
  assert.equal(curve(100).level, 2);
  assert.equal(curve(100).repeatGain, curve(100).gain);
  assert.equal(curve(5).repeatGain, null);
});

test("gain is bounded and sound shape remains reflected in the output", () => {
  for (const value of [NaN, -100, 5, 55, 100, 200]) {
    for (const scale of [0, .2, .6, 1, 2]) {
      const output = curve(value, scale);
      assert.ok(output.gain >= -12 && output.gain <= 16);
      assert.ok(Number.isFinite(output.gain));
    }
  }
  assert.ok(curve(100, .2).gain < curve(100, 1).gain);
});

test("Steam Controller commands follow live settings and maximum emits two full pulses", () => {
  const calls = [];
  const timers = [];
  const runtime = {};
  const window = {
    document: { visibilityState: "visible", hasFocus: () => true },
    SteamClient: { Input: { TriggerSimpleHapticEvent: (...args) => calls.push(args) } },
    setTimeout: (callback) => { timers.push(callback); return timers.length; },
  };
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL("../src/navigationHaptics.ts", import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText + "\nexports.pulse = modernPulse;", {
    exports: runtime, window, require: () => ({ DFL: {}, modernHapticIntensity: curve }),
  });
  runtime.configureNavigationHaptics({ enabled: true, intensity: 5 });
  runtime.pulse(0, 2, 2, 1);
  assert.deepEqual(calls[0], [0, 2, 2, 0, -12]);
  assert.equal(timers.length, 0);
  runtime.configureNavigationHaptics({ intensity: 100 });
  runtime.pulse(0, 2, 2, 1);
  timers.forEach(callback => callback());
  assert.deepEqual(calls.slice(1), [[0, 2, 2, 2, 16], [0, 2, 2, 2, 16]]);
  assert.equal(runtime.getNavigationHapticsConfig().intensity, 100);
});
