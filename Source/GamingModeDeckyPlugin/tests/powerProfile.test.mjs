import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const code = ts.transpileModule(fs.readFileSync(new URL("../src/quickSettings/PowerProfile.tsx", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.React },
}).outputText;
const configured = (mode = "balanced", source = "ac") => ({ available: true, source, mode });
const deferred = () => {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
};
const flush = () => new Promise(resolve => setImmediate(resolve));

function fixture(first) {
  const exports = {}, states = [], refs = [], calls = [];
  let stateIndex, refIndex, effect, cleanup, refresh;
  let read = async () => first;
  let write = async request => ({ ...configured(request.mode, request.source), ok: true });
  const hooks = {
    useState(initial) {
      const slot = stateIndex++;
      if (!(slot in states)) states[slot] = initial;
      return [states[slot], value => { states[slot] = value; }];
    },
    useRef(initial) {
      const slot = refIndex++;
      return refs[slot] ??= { current: initial };
    },
    useEffect(fn) { effect ??= fn; },
  };
  const dependencies = {
    react: hooks, "@decky/ui": { DropdownItem: "select" },
    "../controlBackend": { async call(method, value) {
      calls.push([method, value]);
      return method === "get_windows_power_profile" ? read() : write(value);
    } },
    "../controlCenterLocale": { controlLocale: () => ({ saveError: "Power mode not confirmed." }) },
    "../deviceInfoLocale": { deviceInfoLocale: () => ({ ac: "Plugged in", battery: "On battery" }) },
    "./translations": { t: () => ({ powerMode: "Power mode", powerEfficiency: "Efficiency", powerBalanced: "Balanced", powerBest: "Performance" }) },
  };
  vm.runInNewContext(code, {
    exports, require: name => dependencies[name], React: { createElement: (type, props, ...children) => ({ type, props, children }) },
    window: { setInterval: tick => { refresh = tick; }, clearInterval: () => { refresh = undefined; }, addEventListener() {}, removeEventListener() {} },
  });
  const render = () => {
    stateIndex = refIndex = 0;
    return exports.PowerProfile({ locale: "en" });
  };
  render();
  cleanup = effect();
  return {
    calls, render, refresh: () => refresh?.(), cleanup,
    change: mode => render().children[0].props.onChange({ data: mode }),
    onRead: fn => { read = fn; },
    onWrite: fn => { write = fn; },
  };
}

test("supported profiles expose the setting for the current power source", async () => {
  const f = fixture(configured("performance", "battery"));
  await flush();
  const select = f.render().children[0].props;
  assert.equal(select.selectedOption, "performance");
  assert.equal(select.description, "On battery");
  assert.deepEqual(Array.from(select.rgOptions, entry => entry.data), ["efficiency", "balanced", "performance"]);
  assert.equal(select.layout, "below");
  f.cleanup();
});

test("unsupported Windows capability produces no control or background RPC", async () => {
  const f = fixture({ available: false });
  await flush();
  assert.equal(f.render(), null);
  f.refresh();
  assert.equal(f.calls.length, 1);
  f.cleanup();
});

test("selection is confirmed by Windows, including concurrent refresh and repeated input", async () => {
  const f = fixture(configured());
  await flush();
  const prior = deferred(), write = deferred();
  f.onRead(() => prior.promise);
  f.onWrite(() => write.promise);
  f.refresh();
  f.change("performance");
  f.change("efficiency");
  assert.equal(f.render().children[0].props.selectedOption, "balanced");
  assert.equal(f.render().children[0].props.disabled, true);
  const requests = f.calls.filter(([method]) => method === "set_windows_power_profile");
  assert.equal(requests.length, 1);
  assert.equal(requests[0][1].source, "ac");
  assert.equal(requests[0][1].mode, "performance");
  write.resolve({ ...configured("performance"), ok: true });
  await flush();
  prior.resolve(configured("balanced"));
  await flush();
  assert.equal(f.render().children[0].props.selectedOption, "performance");
  f.cleanup();
});

test("switching from AC during the operation updates the selector to the reported battery state", async () => {
  const f = fixture(configured());
  await flush();
  f.onWrite(async () => ({ ...configured("efficiency", "battery"), ok: false }));
  f.change("performance");
  await flush();
  const render = f.render();
  assert.equal(render.children[0].props.selectedOption, "efficiency");
  assert.equal(render.children[0].props.description, "On battery");
  assert.equal(render.children[1].props.role, "status");
  f.cleanup();
});
