import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";
import ts from "typescript";

const source = fs.readFileSync(new URL("../src/gameFrameComparison.ts", import.meta.url), "utf8");
const compiled = ts.transpile(source, { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ES2022 });
const { createGameFrameComparison } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);
const appId = 990080;
const frames = (fps = 60, p95 = 22) => ({ status: "Ready", presentedFps: fps, medianFrameTimeMs: 16, p95FrameTimeMs: p95, samples: 240 });

test("two measurements survive closing Quick Access and stay associated with the game", async () => {
  const state = createGameFrameComparison();
  let resolve;
  let renders = 0;
  const unsubscribe = state.subscribe(() => renders++);
  const pending = state.capture(appId, () => new Promise(done => { resolve = done; }));
  unsubscribe();
  assert.equal(state.read(appId).busy, true);
  await state.capture(appId + 1, () => assert.fail("another request cannot begin"));
  resolve(frames());
  await pending;
  assert.equal(renders, 1);
  assert.equal(state.read(appId).first.presentedFps, 60);
  assert.equal(state.read(appId + 1).first, undefined);
  await state.capture(appId, async id => { assert.equal(id, appId); return frames(61, 18); });
  assert.equal(state.read(appId).first.p95FrameTimeMs, 22);
  assert.equal(state.read(appId).second.p95FrameTimeMs, 18);
  state.reset(appId);
  assert.equal(state.read(appId).first, undefined);
});

test("failure keeps a valid baseline and never produces a made-up second measurement", async () => {
  const state = createGameFrameComparison();
  await state.capture(appId, async () => frames());
  for (const response of [frames(Infinity), frames(60, 10), { ...frames(), samples: 7 }]) {
    await state.capture(appId, async () => response);
    assert.equal(state.read(appId).error, "NoData");
    assert.equal(state.read(appId).second, undefined);
  }
  await state.capture(appId, async () => ({ status: "GameNotForeground" }));
  assert.equal(state.read(appId).error, "GameNotForeground");
  await state.capture(appId, async () => frames(42, 24));
  await state.capture(appId, async () => { throw Error("backend unavailable"); });
  assert.equal(state.read(appId).error, "ProviderUnavailable");
  assert.equal(state.read(appId).first.presentedFps, 60);
  assert.equal(state.read(appId).second.presentedFps, 42);
});
