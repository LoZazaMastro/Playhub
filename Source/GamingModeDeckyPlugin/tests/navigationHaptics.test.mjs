import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const source = ts.transpileModule(fs.readFileSync(new URL("../src/navigationHaptics.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;

function fixture({ native = false, observer = true } = {}) {
  const pulses = [], requests = [], registrations = [], observers = [], timers = new Map(), frames = new Map();
  let now = 1000, nextId = 1, active;
  const keys = new Map(), events = new Map();
  const controller = { nControllerIndex: 7, nXInputIndex: 0, eControllerType: native ? 1 : 48,
    ...(native ? { unVendorID: 0x054c, unProductID: 0x0ce6 } : {}) };
  const document = {
    documentElement: {},
    visibilityState: "visible",
    hasFocus: () => true,
    querySelector: () => active,
    addEventListener: (type, handler) => events.set(type, handler),
    removeEventListener: type => events.delete(type),
  };
  const window = {
    document,
    addEventListener: (type, handler) => keys.set(type, handler),
    removeEventListener: type => keys.delete(type),
    ControllerStore: { GetControllers: () => [controller] },
    SteamClient: { Input: {
      TriggerSimpleHapticEvent: (...args) => pulses.push(args),
      RegisterForControllerInputMessages: handler => {
        registrations.push(handler);
        return { unregister: () => registrations.pop() };
      },
    } },
    MutationObserver: class {
      constructor(callback) { this.callback = callback; }
      observe(_, options) { observers.push({ callback: this.callback, options }); }
      disconnect() {}
    },
    requestAnimationFrame: handler => { frames.set(nextId, handler); return nextId++; },
    cancelAnimationFrame: id => frames.delete(id),
    setTimeout: handler => { timers.set(nextId, handler); return nextId++; },
    clearTimeout: id => timers.delete(id),
    setInterval: () => nextId++,
    clearInterval: () => {},
  };
  document.defaultView = window;
  const popupKeys = new Map();
  const popup = { ...window, document: { ...document },
    addEventListener: (type, handler) => popupKeys.set(type, handler),
    removeEventListener: type => popupKeys.delete(type) };
  popup.document.defaultView = popup;
  if (!observer) { delete window.MutationObserver; delete popup.MutationObserver; }
  const runtime = {};
  vm.runInNewContext(source, {
    exports: runtime, window, navigator: { getGamepads: () => [] }, performance: { now: () => now },
    Node: { TEXT_NODE: 3 }, AbortController,
    require: () => ({ DFL: { findSP: () => popup }, API_BASE: "http://localhost", modernHapticIntensity: () => ({ level: 2, gain: 10, repeatGain: 10 }) }),
    fetch: async (_, request) => { requests.push(JSON.parse(request.body)); return { ok: true }; },
  });
  runtime.configureNavigationHaptics({ enabled: true, intensity: 100 });
  const cleanup = runtime.installNavigationHaptics();
  return {
    pulses, requests, keys, popupKeys, registrations, cleanup,
    advance: value => { now += value; },
    input: (...values) => registrations[0](...values),
    keyboard: (type = "keydown", target = keys, code = "ArrowDown") => target.get(type)?.({ type, code, isTrusted: true }),
    focus(label, x) {
      active = {
        closest: () => null, querySelector: () => null, getAttribute: name => name === "aria-label" ? label : null,
        getBoundingClientRect: () => ({ left: x, top: 0, width: 40, height: 20 }),
      };
      events.get("focusin")?.();
      const work = [...frames.values()];
      frames.clear();
      work.forEach(fn => fn());
    },
    letter(label) {
      window.getComputedStyle = () => ({ fontSize: "30" });
      const observer = observers.find(({ options }) => options.characterData);
      observer.callback([{ target: {
        nodeType: 1, textContent: label, childElementCount: 0, ownerDocument: document,
        getBoundingClientRect: () => ({ width: 40, height: 20 }),
      }, addedNodes: [] }]);
    },
    flush() { while (timers.size) { const [id, run] = timers.entries().next().value; timers.delete(id); run(); } },
  };
}

test("keyboard focus, tab and letter navigation stay silent with a controller connected", () => {
  const f = fixture();
  f.focus("First item", 0);
  f.keyboard();
  f.focus("Second item", 100);
  f.keyboard("keyup");
  f.focus("Z", 150);
  f.letter("Z");
  f.flush();
  assert.deepEqual(f.pulses, []);
  assert.deepEqual(f.requests, []);
  f.cleanup();
  assert.equal(f.keys.size, 0);
  assert.equal(f.registrations.length, 0);
});

test("invalid IDs, XInput slots and Steam active-slot hints cannot select a controller", () => {
  const f = fixture();
  for (const index of [undefined, null, -1, 0, 0xffffffff, "7"]) f.input(index, 0, true);
  f.input({ nActiveController: 0, eButton: 0, bPressed: true });
  f.input(7, 0, false);
  f.flush();
  assert.deepEqual(f.pulses, []);
  f.input(7, 0, true);
  f.flush();
  assert.ok(f.pulses.length > 0 && f.pulses.every(pulse => pulse[0] === 7));
  f.cleanup();
});

test("switching to keyboard cancels delayed haptics and only new physical input re-arms them", () => {
  const f = fixture();
  f.focus("First item", 0);
  f.input(7, 0, true);
  const initial = f.pulses.length;
  assert.ok(initial > 0);
  f.advance(20);
  f.keyboard();
  f.input(7, 4, true);
  f.focus("Next item", 100);
  f.letter("B");
  f.flush();
  assert.equal(f.pulses.length, initial);
  assert.equal(f.requests.filter(request => request.stop).length, 1);
  f.advance(120);
  f.keyboard("keyup");
  f.advance(120);
  f.input({ nControllerIndex: 7, eButton: 4, bPressed: true });
  assert.ok(f.pulses.length > initial);
  const count = f.pulses.length;
  f.letter("C");
  assert.ok(f.pulses.length > count);
  f.flush();
  const afterController = f.pulses.length;
  f.advance(300);
  f.letter("D");
  assert.equal(f.pulses.length, afterController);
  f.cleanup();
});

test("held keyboard input stays silent beyond collision timeout, including Steam popup callbacks", () => {
  const f = fixture();
  f.input(7, 0, true);
  const before = f.pulses.length;
  f.keyboard("keydown", f.popupKeys);
  f.popupKeys.get("blur")({ target: {}, currentTarget: {} }); // A control loses focus, not its window.
  for (let i = 0; i < 4; i++) {
    f.advance(150);
    f.input(7, 4, true);
    f.focus(`Keyboard row ${i}`, i * 50);
    f.flush();
  }
  assert.equal(f.pulses.length, before);
  f.keyboard("keyup", f.popupKeys);
  f.advance(100);
  f.input(7, 4, true);
  assert.ok(f.pulses.length > before);
  f.cleanup();
  assert.equal(f.popupKeys.size, 0);
});

test("keyboard cancels an old native request before its asynchronous fallback can vibrate", async () => {
  const f = fixture({ native: true });
  f.input(7, 0, true);
  f.advance(400); // Longer than the former 260 ms cancellation limit.
  f.keyboard();
  await new Promise(resolve => setImmediate(resolve));
  f.flush();
  assert.deepEqual(f.pulses, []);
  assert.equal(f.requests.filter(request => request.stop).length, 1);
  f.cleanup();
});

test("keyboard listeners remain active when a Steam surface has no MutationObserver", () => {
  const f = fixture({ observer: false });
  f.input(7, 0, true);
  const before = f.pulses.length;
  f.keyboard();
  f.advance(400);
  f.input(7, 0, true);
  f.flush();
  assert.equal(f.pulses.length, before);
  f.cleanup();
});

test("releasing one keyboard key does not re-arm feedback while another key remains held", () => {
  const f = fixture();
  f.keyboard("keydown", f.keys, "ShiftLeft");
  f.keyboard("keydown", f.popupKeys, "ArrowDown");
  f.keyboard("keyup", f.popupKeys, "ArrowDown");
  f.advance(500);
  f.input(7, 4, true);
  f.flush();
  assert.deepEqual(f.pulses, []);
  f.keyboard("keyup", f.keys, "ShiftLeft");
  f.advance(100);
  f.input(7, 4, true);
  assert.ok(f.pulses.length > 0);
  f.cleanup();
});

test("pointer navigation also cancels feedback queued by the previous controller action", () => {
  const f = fixture();
  f.input(7, 0, true);
  const before = f.pulses.length;
  f.popupKeys.get("pointerdown")({ isTrusted: true });
  f.flush();
  assert.equal(f.pulses.length, before);
  assert.equal(f.requests.filter(request => request.stop).length, 1);
  f.cleanup();
});
