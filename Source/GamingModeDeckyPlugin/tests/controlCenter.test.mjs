import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";
import ts from "typescript";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
function load(file) {
  const exports = {};
  vm.runInNewContext(ts.transpileModule(fs.readFileSync(new URL(file, import.meta.url), "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText, { exports });
  return exports;
}
const { CONTROL_TABS, normalizeControlPreferences: normalize, moveControlTab: move, visibleControlTabs } = load("../src/controlCenterState.ts");
const { controlLocale } = load("../src/controlCenterLocale.ts");
const { gameSettingsLabel } = load("../src/quickSettings/gameSettingsLocale.ts");
const { deckyIntegrationLocale } = load("../src/deckyIntegrationLocale.ts");

test("Decky integration has dedicated copy in every supported language", () => {
  const english = deckyIntegrationLocale("en");
  for (const language of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const copy = deckyIntegrationLocale(language);
    assert.ok(copy.label.length > 0 && copy.description.length > 0);
    assert.match(copy.description, /Playhub/);
    if (language !== "en") assert.notEqual(copy.description, english.description);
    assert.equal(deckyIntegrationLocale(`${language}-XX`).description, copy.description);
  }
  assert.equal(deckyIntegrationLocale("unknown").description, english.description);
  assert.equal(deckyIntegrationLocale("it").label, "Nascondi Decky dalle tab di Steam");
  assert.equal(deckyIntegrationLocale("it").description, "Decky rimane sempre disponibile nel menu di Playhub.");
});

test("game menu uses the Playhub Game Settings product name in all languages", () => {
  for (const locale of [null, "italian", "en-US", "koreana", "brazilian", "ja", "zh", "hi"])
    assert.equal(gameSettingsLabel(locale), "Playhub Game Settings");
});

test("control center restores valid unique tabs and retains a visible tab", () => {
  for (const value of [null, {}, { order: ["audio", "audio", "unknown"], hidden: [...CONTROL_TABS], active: "missing" }]) {
    const prefs = normalize(value);
    assert.equal(new Set(prefs.order).size, CONTROL_TABS.length);
    assert.ok(prefs.order.includes(prefs.active));
    assert.ok(!prefs.hidden.includes(prefs.active));
  }
});
test("reordering persists independently of hidden tabs and collapsed sections", () => {
  const prefs = normalize({ order: ["home", "audio", "performance", "graphics", "controller", "store"], hidden: ["store"], collapsed: ["radeon"], active: "graphics" });
  const moved = move(prefs, "audio", -1);
  assert.equal(moved.order[0], "audio");
  assert.equal(prefs.order[0], "home");
  assert.equal(JSON.stringify(moved.hidden), JSON.stringify(prefs.hidden));
  assert.equal(JSON.stringify(normalize(JSON.parse(JSON.stringify(moved)))), JSON.stringify(moved));
  assert.equal(move(moved, "audio", -1), moved);
});

test("Store is second by default without rewriting an existing custom order", () => {
  assert.equal(normalize({}).order[1], "store");
  const order = ["controller", "audio", "home", "graphics", "store", "performance"];
  assert.equal(JSON.stringify(normalize({ order }).order), JSON.stringify([...order, "decky"]));
});

test("Decky hosting defaults on, respects explicit disable and retains a usable tab", () => {
  assert.equal(normalize({}).deckyHostEnabled, true);
  assert.equal(normalize({ deckyHostEnabled: false }).deckyHostEnabled, false);
  assert.equal(normalize({ deckyHostEnabled: "true", active: "decky" }).active, "decky");
  assert.equal(normalize({ deckyHostEnabled: true, active: "decky" }).active, "decky");
  const prefs = normalize({ deckyHostEnabled: false, hidden: CONTROL_TABS.filter(id => id !== "decky") });
  assert.equal(prefs.active, "decky");
  assert.ok(prefs.hidden.includes("home"));
});
test("hiding the active tab selects a visible sibling", () => {
  const prefs = normalize({ active: "audio", hidden: ["audio"] });
  assert.equal(prefs.active, "home");
});
test("unavailable Decky host keeps navigation accessible without overwriting hidden preferences", () => {
  const prefs = normalize({ deckyHostEnabled: true, active: "decky", hidden: CONTROL_TABS.filter(id => id !== "decky") });
  const saved = JSON.stringify(prefs);
  assert.equal(JSON.stringify(visibleControlTabs(prefs, false)), '["home"]');
  assert.equal(JSON.stringify(visibleControlTabs(prefs, true)), '["decky"]');
  assert.equal(JSON.stringify(prefs), saved);
});
test("panel copy covers all Playhub languages including Steam aliases", () => {
  for (const language of ["en", "it", "de", "es", "fr", "pt", "ru", "uk", "ja", "ko", "zh", "hi"]) {
    const copy = controlLocale(language);
    assert.ok(Object.values(copy).every(value => typeof value === "string" && value.length > 0));
    if (language !== "en") assert.notEqual(copy.customize, controlLocale("en").customize);
  }
  assert.equal(controlLocale("italian").customize, controlLocale("it").customize);
});
test("helper packaging executes its allowlist without copying caches or arbitrary drivers", { skip: process.platform !== "win32" }, () => {
  const build = fs.readFileSync(new URL("../build-plugin.bat", import.meta.url), "utf8");
  const block = build.split("rem ---------- helper payload allowlist ----------")[1].split("rem ---------- end helper payload allowlist ----------")[0];
  const command = block.split(/\r?\n/).filter(line => /^  "/.test(line)).map(line => line.slice(3, line.indexOf('"', 3))).join(" ");
  const required = [...command.match(/\$required=@\(([^;]+)\);/)[1].matchAll(/'([^']+)'/g)].map(match => match[1]);
  for (const name of required) assert.ok(fs.existsSync(new URL(`../${name.replaceAll("\\", "/")}`, import.meta.url)), `Real runtime helper missing: ${name}`);
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "playhub-packaging-"));
  const target = path.join(root, "payload");
  const put = (name, content = name) => {
    const full = path.join(root, name); fs.mkdirSync(path.dirname(full), { recursive: true }); fs.writeFileSync(full, content);
  };
  try {
    for (const name of required) put(name);
    const extra = ["quick_settings/amd/ADLXDepends/Binding.cs", "quick_settings/licenses/GPL-3.0.txt", "quick_settings/extra.py"];
    for (const name of extra) put(name);
    for (const name of ["quick_settings/__pycache__/main.pyc", "quick_settings/rogue.sys", "quick_settings/bin/unlisted.exe", "quick_settings/bin/libryzenadj.dll"]) put(name);
    put("payload/quick_settings/__pycache__/old.pyc"); put("payload/cpu_power/old.pyo");
    put("payload/quick_settings/unrelated.txt", "preserve");
    const result = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command.replaceAll("%ASSETS%", target)], { cwd: root, encoding: "utf8" });
    assert.equal(result.status, 0, result.stderr + result.stdout);
    for (const name of [...required, ...extra]) assert.deepEqual(fs.readFileSync(path.join(target, name)), fs.readFileSync(path.join(root, name)));
    for (const name of ["quick_settings/__pycache__/main.pyc", "quick_settings/__pycache__/old.pyc", "cpu_power/old.pyo", "quick_settings/rogue.sys", "quick_settings/bin/unlisted.exe", "quick_settings/bin/libryzenadj.dll"]) assert.equal(fs.existsSync(path.join(target, name)), false, name);
    assert.equal(fs.readFileSync(path.join(target, "quick_settings/unrelated.txt"), "utf8"), "preserve");
    fs.unlinkSync(path.join(root, required[0]));
    const missing = spawnSync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command.replaceAll("%ASSETS%", target)], { cwd: root, encoding: "utf8" });
    assert.notEqual(missing.status, 0, "Missing mandatory runtime must fail closed");
  } finally {
    assert.ok(path.resolve(root).startsWith(path.resolve(os.tmpdir()) + path.sep));
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("packaging includes backend, notices and integrated helpers", () => {
  const build = fs.readFileSync(new URL("../build-plugin.bat", import.meta.url), "utf8");
  assert.match(build, /copy \/y "main.py"/);
  assert.match(build, /helper payload allowlist/);
  assert.doesNotMatch(build, /xcopy[^\r\n]*\/e[^\r\n]*"quick_settings"/);
  for (const file of ["../main.py", "../quick_settings/main.py", "../quick_settings/bin/QuickSettingsAgent.exe", "../quick_settings/LICENSE", "../THIRD-PARTY-NOTICES.md"])
    assert.ok(fs.existsSync(new URL(file, import.meta.url)), file);
});
