import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = fs.readFileSync(new URL("../src/quickSettings/downloadPowerController.ts", import.meta.url), "utf8");
const compiled = ts.transpile(source, { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 });
const { activeLocalDownload, createSteamDownloadPower } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);
const appId = 990080;
const lease = "b".repeat(32);
const transfer = {
  remote_client_id: "0", paused: false, update_appid: appId, update_state: "Downloading", update_is_upload: false,
  update_is_shader: false, update_is_workshop: false, update_is_prefetch_estimate: false,
  update_network_bytes_per_second: 1, update_disc_bytes_per_second: 0, progress: [{ bytes_in_progress: 1, bytes_total: 99 }],
};

test("only verified active local game transfer and disk work are eligible", () => {
  assert.equal(activeLocalDownload(transfer), appId);
  assert.equal(activeLocalDownload({ ...transfer, update_state: "Installing", update_network_bytes_per_second: 0, update_disc_bytes_per_second: 1 }), appId);
  for (const changes of [
    { remote_client_id: "remote" }, { remote_client_id: 0 }, { paused: true }, { update_appid: "990080" },
    { update_state: "None" }, { update_state: "Unknown" }, { update_state: "Hosting" }, { update_is_upload: true },
    { update_is_shader: true }, { update_is_workshop: true }, { update_is_prefetch_estimate: true },
    { update_network_bytes_per_second: 0 }, { update_disc_bytes_per_second: Infinity },
    { progress: null }, { progress: [{ bytes_in_progress: 99, bytes_total: 99 }] },
  ]) assert.equal(activeLocalDownload({ ...transfer, ...changes }), 0, JSON.stringify(changes));
});

test("an opt-in lease cannot renew another download and stops immediately on state loss", async () => {
  let currentApp = appId;
  const requests = [];
  const mode = createSteamDownloadPower(() => currentApp, async request => { requests.push(request); return { ok: true, lease }; });
  try {
    await mode.start();
    await mode.tick();
    assert.equal(mode.read().active, true);
    currentApp++;
    await mode.tick();
    assert.equal(mode.read().active, false);
    assert.deepEqual(requests, [{ action: "start", appId }, { action: "renew", appId, lease }, { action: "stop", appId, lease }]);
  } finally { await mode.stop(); }
});

test("closing the browser during arm releases a delayed response without renewing it", async () => {
  const calls = [];
  let complete;
  const mode = createSteamDownloadPower(() => appId, request => {
    calls.push(request);
    return request.action === "start" ? new Promise(resolve => { complete = resolve; }) : Promise.resolve({ ok: true });
  });
  const started = mode.start();
  await mode.stop();
  complete({ ok: true, lease });
  await started;
  assert.equal(mode.read().active, false);
  assert.deepEqual(calls, [{ action: "start", appId }, { action: "stop", appId, lease }]);
});

test("native safety denial or heartbeat failure stops the UI lease", async () => {
  let renewalAllowed = false;
  const mode = createSteamDownloadPower(() => appId, async request => {
    if (request.action === "start") return { ok: true, lease };
    if (request.action === "renew" && !renewalAllowed) throw new Error("Backend unavailable");
    return { ok: true };
  });
  try {
    await mode.start();
    await mode.tick();
    assert.equal(mode.read().active, false);
    renewalAllowed = true;
    await mode.start();
    await mode.tick();
    assert.equal(mode.read().active, true);
  } finally { await mode.stop(); }
});
