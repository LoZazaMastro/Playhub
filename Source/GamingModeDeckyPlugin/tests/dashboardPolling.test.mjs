import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

function loadModule(path, globals = {}) {
  const source = readFileSync(new URL(path, import.meta.url), "utf8");
  const exports = {};
  vm.runInNewContext(ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports, AbortController, setTimeout, clearTimeout, ...globals });
  return exports;
}

const { createDashboardPoll, dashboardSystemVisible } = loadModule("../src/dashboardPolling.ts");

async function settle() {
  for (let step = 0; step < 6; step++) await Promise.resolve();
}

function createClock() {
  let current = 0;
  let serial = 0;
  const timers = new Map();
  const clock = {
    now: () => current,
    schedule(callback, delay) {
      timers.set(++serial, { callback, due: current + delay });
      return serial;
    },
    cancel: (id) => timers.delete(id),
    get pending() { return timers.size; },
    async advance(delay) {
      const end = current + delay;
      await settle();
      for (;;) {
        const next = [...timers].sort((left, right) => left[1].due - right[1].due)[0];
        if (!next || next[1].due > end) break;
        const [id, { callback, due }] = next;
        timers.delete(id);
        current = due;
        callback();
        await settle();
      }
      current = end;
    },
  };
  return clock;
}

function deferredReads() {
  const calls = [];
  return {
    calls,
    read: (signal) => new Promise((resolve, reject) => calls.push({ signal, resolve, reject })),
  };
}

test("slow process enumeration never delays usage or overlaps another enumeration", async () => {
  const clock = createClock();
  const usage = [];
  const processes = deferredReads();
  const base = { ...clock, visible: () => true };
  const quick = createDashboardPoll({ ...base, read: async () => clock.now(), publish: (sample) => usage.push(sample), interval: 2500 });
  const slow = createDashboardPoll({ ...base, read: processes.read, publish: () => {}, interval: 8000 });
  quick.setActive(true);
  slow.setActive(true);
  await clock.advance(10000);
  assert.deepEqual(usage, [0, 2500, 5000, 7500, 10000]);
  assert.equal(processes.calls.length, 1);
  processes.calls[0].resolve([]);
  await clock.advance(7999);
  assert.equal(processes.calls.length, 1);
  await clock.advance(1);
  assert.equal(processes.calls.length, 2);
  quick.dispose();
  slow.dispose();
});

test("hiding or disposing cancels pending work and ignores late results", async () => {
  const clock = createClock();
  const source = deferredReads();
  const samples = [];
  let visible = true;
  const poll = createDashboardPoll({ ...clock, read: source.read, publish: (sample) => samples.push(sample), interval: 2500, visible: () => visible });
  poll.setActive(true);
  visible = false;
  poll.setActive(false);
  assert.equal(source.calls[0].signal.aborted, true);
  visible = true;
  poll.setActive(true);
  assert.equal(source.calls.length, 1);
  source.calls[0].resolve("stale");
  await clock.advance(0);
  assert.deepEqual(samples, []);
  assert.equal(source.calls.length, 2);
  poll.dispose();
  assert.equal(source.calls[1].signal.aborted, true);
  source.calls[1].resolve("unmounted");
  await clock.advance(30000);
  assert.deepEqual(samples, []);
  assert.equal(clock.pending, 0);
  assert.equal(source.calls.length, 2);
});

test("a process action replaces a pre-action sample and queues one prompt refresh", async () => {
  const clock = createClock();
  const source = deferredReads();
  const samples = [];
  const poll = createDashboardPoll({ ...clock, read: source.read, publish: (sample) => samples.push(sample), interval: 8000, visible: () => true });
  poll.setActive(true);
  await clock.advance(100);
  poll.refresh(450);
  await clock.advance(100);
  poll.refresh(450);
  source.calls[0].resolve([123]);
  await clock.advance(449);
  assert.equal(source.calls.length, 1);
  assert.deepEqual(samples, []);
  await clock.advance(1);
  assert.equal(source.calls.length, 2);
  source.calls[1].resolve([]);
  await settle();
  assert.deepEqual(samples, [[]]);
  poll.dispose();
});

test("a failed snapshot keeps the last good sample and the feed retries", async () => {
  const clock = createClock();
  const source = deferredReads();
  const samples = [];
  const poll = createDashboardPoll({ ...clock, read: source.read, publish: (sample) => samples.push(sample), interval: 2500, visible: () => true });
  poll.setActive(true);
  source.calls[0].resolve([1]);
  await clock.advance(2500);
  source.calls[1].resolve(null);
  await clock.advance(2500);
  source.calls[2].reject(new Error("agent unavailable"));
  await clock.advance(2500);
  assert.deepEqual(samples, [[1]]);
  source.calls[3].resolve([]);
  await settle();
  assert.deepEqual(samples, [[1], []]);
  poll.dispose();
});

test("the agent bridge aborts hidden dashboard reads and distinguishes failure from an empty snapshot", async () => {
  const fetches = [];
  let unavailable = 0;
  const { listProcesses, readUsage } = loadModule("../src/api.ts", {
    TypeError,
    require: () => ({ agentAvailability: {
      canRequest: () => true,
      available: () => {},
      unavailable: () => { unavailable++; },
    } }),
    fetch: (url, { signal }) => new Promise((resolve, reject) => {
      fetches.push({ url, signal, resolve });
      signal.addEventListener("abort", () => reject(new TypeError("aborted")), { once: true });
    }),
  });
  const controller = new AbortController();
  const usage = readUsage(controller.signal);
  const processes = listProcesses(controller.signal);
  assert.match(fetches[0].url, /\/dash\/usage$/);
  assert.match(fetches[1].url, /\/dash\/processes$/);
  controller.abort();
  assert.equal(fetches[0].signal.aborted, true);
  assert.equal(fetches[1].signal.aborted, true);
  assert.equal(await usage, null);
  assert.equal(await processes, null);
  assert.equal(unavailable, 0);
  assert.equal(await listProcesses(controller.signal), null);
  assert.equal(fetches.length, 2);

  const failed = listProcesses();
  fetches[2].resolve({ ok: false });
  assert.equal(await failed, null);
  const empty = listProcesses();
  fetches[3].resolve({ ok: true, json: async () => [] });
  assert.deepEqual(await empty, []);
});

test("System surface visibility checks route, rendered document, viewport and CSS", () => {
  let visibility = "visible";
  const doc = { hidden: false, defaultView: { closed: false, getComputedStyle: () => ({ visibility }) } };
  const surface = { isConnected: true, ownerDocument: doc, getClientRects: () => [{ width: 100 }] };
  assert.equal(dashboardSystemVisible(surface, true, true), true);
  assert.equal(dashboardSystemVisible(surface, false, true), false);
  assert.equal(dashboardSystemVisible(surface, true, false), false);
  doc.hidden = true;
  assert.equal(dashboardSystemVisible(surface, true, true), false);
  doc.hidden = false;
  doc.defaultView.closed = true;
  assert.equal(dashboardSystemVisible(surface, true, true), false);
  doc.defaultView.closed = false;
  visibility = "hidden";
  assert.equal(dashboardSystemVisible(surface, true, true), false);
  visibility = "visible";
  doc.defaultView.getComputedStyle = () => { throw new Error("closed portal"); };
  assert.equal(dashboardSystemVisible(surface, true, true), false);
  surface.isConnected = false;
  assert.equal(dashboardSystemVisible(surface, true, true), false);
});

test("System uses the portal document, watches the route and refreshes processes after both actions", () => {
  const page = readFileSync(new URL("../src/DashboardPage.tsx", import.meta.url), "utf8");
  const system = page.slice(page.indexOf("function SystemTab"), page.indexOf("function ProcessOptionsSheet"));
  assert.match(system, /surface\?\.ownerDocument/);
  assert.match(system, /history\?\.listen/);
  assert.match(system, /ownerDocument\.addEventListener\("visibilitychange"/);
  assert.match(system, /styles\.observe\(node, \{ attributes: true, attributeFilter: \["class", "hidden", "style"\] \}\)/);
  assert.match(system, /dashboardOverlayClosedListeners\.add/);
  assert.match(system, /closeProcess\(current.id\)\.then\(\(\) => processPollRef\.current\?\.refresh\(450\)\)/);
  assert.match(system, /killProcess\(current.id\)\.then\(\(\) => processPollRef\.current\?\.refresh\(450\)\)/);
  assert.match(system, /usagePoll\.dispose\(\)/);
  assert.match(system, /processPoll\.dispose\(\)/);
  assert.doesNotMatch(system, /Promise\.all|setInterval/);
});
