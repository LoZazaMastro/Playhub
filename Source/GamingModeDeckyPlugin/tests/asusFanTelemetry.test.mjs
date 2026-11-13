import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
const code = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/asusFanTelemetry.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const exports = {};
vm.runInNewContext(code, { exports });
const { asusFanTelemetry, createAsusFanMonitor } = exports;
const flush = () => new Promise(resolve => setImmediate(resolve));
const reading = (time, change = {}) => ({ available: true, ok: true, provider: "asus-ally", token: "a".repeat(64),
  capabilities: { fan_rpm: true, charge_limit: false }, fans: { cpu_rpm: 3500, gpu_rpm: 0 }, sampled_at_ms: time, ...change });
function fixture() {
  let now = 100000, visible = true, get = async () => reading(now), calls = 0;
  let view, updates = 0;
  const timers = new Map();
  const monitor = createAsusFanMonitor({ get: () => { calls++; return get(); }, visible: () => visible, now: () => now,
    schedule(fn, delay) { const key = {}; timers.set(key, { fn, due: now + delay }); return key; },
    cancel(key) { timers.delete(key); }, changed(value) { view = value; updates++; } });
  return { monitor, timers, get view() { return view; }, get calls() { return calls; }, get updates() { return updates; },
    get now() { return now; }, source(fn) { get = fn; }, visible(value) { visible = value; monitor.visibilityChanged(); },
    async advance(ms) {
      now += ms;
      for (const [key, value] of [...timers]) if (value.due <= now && timers.has(key)) { timers.delete(key); value.fn(); }
      await flush();
    } };
}

test("typed real RPM includes stopped fans, rejects malformed, old, future or foreign-provider readings", () => {
  const now = 100000;
  assert.equal(asusFanTelemetry(reading(now), now).fans.gpu_rpm, 0);
  for (const patch of [{ fans: { cpu_rpm: true, gpu_rpm: 0 } }, { fans: { cpu_rpm: -100, gpu_rpm: null } },
    { fans: { cpu_rpm: 3501, gpu_rpm: null } }, { fans: { cpu_rpm: null, gpu_rpm: null } },
    { token: ["a".repeat(64)] }, { provider: "other" }, { sampled_at_ms: now - 6000 },
    { sampled_at_ms: now + 1001 }, { sampled_at_ms: "100000" }, { capabilities: { fan_rpm: 1 } }])
    assert.equal(asusFanTelemetry(reading(now, patch), now), null);
  assert.equal(asusFanTelemetry({ available: false }, now), false);
});

test("sensor polling runs every three seconds only while visible and clears values while hidden", async () => {
  const f = fixture(); f.visible(false); await flush();
  assert.equal(f.calls, 0); assert.equal(f.timers.size, 0);
  f.visible(true); await flush();
  assert.equal(f.calls, 1); assert.equal(f.view.fans.cpu_rpm, 3500);
  await f.advance(3000); assert.equal(f.calls, 2);
  f.visible(false); assert.equal(f.timers.size, 0); assert.equal(f.view.fans, null);
  await f.advance(30000); assert.equal(f.calls, 2);
  f.monitor.dispose();
});

test("hiding during an async read discards it and reopening obtains a new reading without overlap", async () => {
  const f = fixture(); let finish;
  f.source(() => new Promise(resolve => { finish = resolve; }));
  f.visible(true); f.visible(false); f.visible(true);
  assert.equal(f.calls, 1);
  finish(reading(f.now, { fans: { cpu_rpm: 9900, gpu_rpm: null } })); await flush();
  assert.equal(f.view.fans, null);
  f.source(async () => reading(f.now, { token: "b".repeat(64), fans: { cpu_rpm: 2800, gpu_rpm: null } }));
  await f.advance(0);
  assert.equal(f.calls, 2); assert.equal(f.view.fans.cpu_rpm, 2800);
  f.monitor.dispose();
});

test("a stalled refresh expires the previous RPM and an unmounted monitor accepts no late data", async () => {
  const f = fixture(); f.visible(true); await flush();
  let finish; f.source(() => new Promise(resolve => { finish = resolve; }));
  await f.advance(3000); assert.equal(f.view.fans.cpu_rpm, 3500);
  await f.advance(3000); assert.equal(f.view.fans, null); assert.equal(f.view.failed, true);
  const changes = f.updates;
  f.monitor.dispose(); finish(reading(f.now)); await flush();
  assert.equal(f.updates, changes); assert.equal(f.timers.size, 0);
});

test("RPC failure clears numbers without inventing zero; unsupported hardware removes the sensor", async () => {
  const f = fixture(); f.visible(true); await flush();
  f.source(async () => { throw new Error("Unavailable"); }); await f.advance(3000);
  assert.equal(f.view.fans, null); assert.equal(f.view.failed, true);
  f.source(async () => ({ available: false })); await f.advance(3000);
  assert.equal(f.view.available, false); assert.equal(f.view.fans, null);
  f.monitor.dispose();
});

test("all twelve languages render confirmed RPM readouts and release their listeners and timers", async () => {
  const componentCode = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/AsusFanStatus.tsx", import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
  }).outputText;
  for (const locale of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi", "pt_BR",
    "italian", "german", "english", "koreana", "schinese", "not_a_valid_locale_!", ""]) {
    const slots = [], listeners = new Map(), timers = new Map(), methods = [];
    let effect, cursor = 0;
    const React = { createElement: (type, props, ...children) => ({ type, props: props ?? {}, children }),
      useState(value) { const index = cursor++; slots[index] ??= value; return [slots[index], next => { slots[index] = next; }]; },
      useEffect(fn) { effect ??= fn; } };
    const component = {};
    const dependencies = {
      react: React, "../controlBackend": { call: async name => { methods.push(name); return reading(100000, {
        capabilities: { fan_rpm: true, charge_limit: false, temperature_c: true }, temperatures: { cpu_c: 51, gpu_c: 39 },
      }); } },
      "../controlCenterLocale": { controlLocale: () => ({ unavailable: "Unavailable" }) },
      "../deviceInfoLocale": { deviceInfoLocale: () => ({ experimental: "Experimental" }) },
      "./asusFanTelemetry": exports,
    };
    vm.runInNewContext(componentCode, { exports: component, React, Date: { now: () => 100000 },
      require: name => { assert.ok(name in dependencies, name); return dependencies[name]; },
      document: { visibilityState: "visible", addEventListener: (key, fn) => listeners.set("doc:" + key, fn), removeEventListener: key => listeners.delete("doc:" + key) },
      window: { addEventListener: (key, fn) => listeners.set("win:" + key, fn), removeEventListener: key => listeners.delete("win:" + key),
        setTimeout(fn, delay) { const key = {}; timers.set(key, { fn, delay }); return key; }, clearTimeout(key) { timers.delete(key); } },
    });
    const render = () => { cursor = 0; return component.AsusFanStatus({ locale }); };
    assert.equal(render(), null);
    const dispose = effect(); await flush();
    const tree = render(), text = [];
    function visit(value) {
      if (Array.isArray(value)) return value.forEach(visit);
      if (typeof value === "string" || typeof value === "number") text.push(String(value));
      else value?.children?.forEach(visit);
    }
    visit(tree);
    const expectedAlias = { italian: "Ventole", german: "Lüfter", english: "Fans", koreana: "팬", schinese: "风扇",
      "not_a_valid_locale_!": "Fans", "": "Fans" };
    if (locale in expectedAlias) assert.equal(text[0], expectedAlias[locale]);
    assert.equal(text.filter(value => value === " RPM").length, 2, locale);
    assert.equal(text.filter(value => value === " °C").length, 2, locale);
    assert.ok(text.includes("51") && text.includes("39"), locale);
    const temperatureLabels = { en: "Temperatures", it: "Temperature", de: "Temperaturen", es: "Temperaturas",
      fr: "Températures", pt: "Temperaturas", ru: "Температуры", uk: "Температури", ja: "温度", ko: "온도", zh: "温度", hi: "तापमान" };
    if (locale in temperatureLabels) assert.ok(text.includes(temperatureLabels[locale]), locale);
    assert.ok(text.includes("0"), locale);
    assert.deepEqual(methods, ["get_asus_device_controls"]);
    assert.equal(timers.size, 2);
    dispose(); assert.equal(timers.size, 0); assert.equal(listeners.size, 0);
  }
});

test("temperature units, nulls and capability are strict without inventing stopped fans", () => {
  const now = 100000;
  const value = patch => reading(now, { capabilities: { fan_rpm: false, charge_limit: false, temperature_c: true },
    fans: { cpu_rpm: null, gpu_rpm: null }, temperatures: { cpu_c: 51, gpu_c: null }, ...patch });
  const valid = asusFanTelemetry(value({}), now);
  assert.equal(valid.fans, null); assert.equal(valid.temperatures.cpu_c, 51); assert.equal(valid.temperatures.gpu_c, null);
  for (const temperatures of [{ cpu_c: 0, gpu_c: null }, { cpu_c: 126, gpu_c: null }, { cpu_c: true, gpu_c: 39 },
    { cpu_c: 51.5, gpu_c: 39 }, { cpu_c: "51", gpu_c: 39 }, { cpu_c: null, gpu_c: null }, { cpu_c: 51 },
    { cpu_c: 51, gpu_c: 39, unit: "F" }]) assert.equal(asusFanTelemetry(value({ temperatures }), now), null);
  assert.equal(asusFanTelemetry(value({ capabilities: { fan_rpm: false, temperature_c: 1 } }), now), null);
  assert.equal(asusFanTelemetry(value({ capabilities: { fan_rpm: true, temperature_c: true } }), now), null);
  assert.equal(asusFanTelemetry(value({ sampled_at_ms: now - 6000 }), now), null);
  assert.equal(asusFanTelemetry(reading(now), now).temperatures, null, "legacy fans need no temperature keys");
});

test("temperature expiry, hide, read error and unmount clear every last sensor value", async () => {
  const f = fixture();
  const report = () => reading(f.now, { capabilities: { fan_rpm: true, temperature_c: true }, temperatures: { cpu_c: 51, gpu_c: 39 } });
  f.source(async () => report()); f.visible(true); await flush();
  assert.equal(f.view.temperatures.cpu_c, 51);
  f.visible(false); assert.equal(f.view.temperatures, null); assert.equal(f.view.fans, null);
  f.visible(true); await flush(); assert.equal(f.view.temperatures.gpu_c, 39);
  let finish; f.source(() => new Promise(resolve => { finish = resolve; }));
  await f.advance(3000); await f.advance(3000);
  assert.equal(f.view.temperatures, null); assert.equal(f.view.fans, null);
  const updates = f.updates; f.monitor.dispose(); finish(report()); await flush();
  assert.equal(f.updates, updates); assert.equal(f.timers.size, 0);
});
