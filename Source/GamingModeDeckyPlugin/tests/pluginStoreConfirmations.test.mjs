import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const page = readFileSync(new URL("../src/PluginStorePage.tsx", import.meta.url), "utf8");

test("native Store confirms single installs and does not offer bulk updates", () => {
  assert.match(page, /confirmStoreOperation\(update \? shared.copy.updateQuestion : shared.copy.installQuestion, "", action, shared.copy\)/);
  assert.doesNotMatch(page, /confirmStoreOperation\(action, plugin.name/);
  assert.doesNotMatch(page, /function UpdateAllButton|<UpdateAllButton/);
  assert.match(page, /requestDeckyUninstall\([\s\S]*?shared.copy.uninstallTitle/);
});

test("native Store never requests a Decky restart or shares restart preferences", () => {
  assert.doesNotMatch(page, /restartDecky|showDeckyRestartPrompt|PluginRestartPreference/);
});

test("confirmation controls use localized Store copy", () => {
  assert.match(page, /strCancelButtonText=\{copy.back\}/);
  assert.match(page, /update \? shared.copy.update : shared.copy.install/);
  assert.match(page, /shared.copy.uninstallDescription/);
});
