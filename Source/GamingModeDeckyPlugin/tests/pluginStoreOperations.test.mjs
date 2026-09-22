import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const here = dirname(fileURLToPath(import.meta.url));

function compile(path, context = {}) {
  const source = readFileSync(path, "utf8");
  const output = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const exports = {};
  vm.runInNewContext(output, { exports, URL, Set, Map, Date, Number, JSON, EventTarget, ...context });
  return exports;
}

const catalog = compile(join(here, "..", "src", "pluginStoreCatalog.ts"));

function fixture({
  installedPlugins = [{ name: "Example", version: "1.0.0" }],
  disabledPlugins = [],
  withBackend = true,
  withUninstall = true,
  backendCall,
} = {}) {
  const calls = [];
  const uninstalls = [];
  const listeners = new Map();
  const eventBus = new EventTarget();
  const loader = {
    deckyState: {
      publicState: () => ({ installedPlugins, disabledPlugins }),
      eventBus,
    },
    checkPluginUpdates: async () => {},
  };
  if (withUninstall) {
    loader.uninstallPlugin = function (...args) {
      assert.equal(this, loader, "Decky's uninstall method must retain its loader receiver");
      assert.ok(this.deckyState.publicState(), "the native modal must be able to read deckyState");
      uninstalls.push(args);
    };
  }
  const window = {
    setTimeout: (callback) => { callback(); return 1; },
    DeckyBackend: withBackend ? {
      call: async (...args) => {
        calls.push(args);
        await backendCall?.(...args);
      },
      addEventListener: (event, listener) => {
        const group = listeners.get(event) ?? new Set();
        group.add(listener);
        listeners.set(event, group);
      },
      removeEventListener: (event, listener) => listeners.get(event)?.delete(listener),
    } : undefined,
    DeckyPluginLoader: loader,
  };
  const operations = compile(join(here, "..", "src", "pluginStoreDecky.ts"), {
    window,
    require: (id) => {
      if (id === "./pluginStoreCatalog") return catalog;
      throw new Error(`unexpected import ${id}`);
    },
  });
  const emit = (event, ...args) => {
    for (const listener of listeners.get(event) ?? []) listener(...args);
  };
  return { calls, emit, loader, uninstalls, operations };
}

const plugin = {
  active: true, name: "Example", installFolder: "Example", author: "A", repository: "owner/repo",
  repositoryUrl: "https://github.com/owner/repo", version: "1.1.0", releaseAssetName: "plugin.zip",
  catalogReleaseUrl: "https://github.com/owner/repo/releases/download/v1/plugin.zip", releasePublishedAt: "",
  category: "Tools", shortDescription: "", longDescription: "", coverUrl: "", iconGlyph: "",
  catalogStatus: "github", catalogSource: "outside-store", catalogPluginId: 0, compatibility: "", keywords: [], aliases: [],
};

test("install/update requests use Decky's real utility route and action type", async () => {
  const { calls, operations } = fixture();
  await operations.requestDeckyInstall(plugin, {
    url: plugin.catalogReleaseUrl, name: "plugin.zip", version: "1.1.0", hash: "",
  }, [{ name: "Example", version: "1.0.0" }]);
  assert.deepEqual(Array.from(calls[0]), [
    "utilities/install_plugin", plugin.catalogReleaseUrl, "Example", "1.1.0", "", 2,
  ]);
});

test("operation type follows Decky's install semantics", () => {
  const { operations } = fixture();
  assert.equal(operations.operationType(plugin, []), 0, "missing plugin must install");
  assert.equal(operations.operationType(plugin, [{ name: "Example", version: "1.1.0" }]), 1, "same version must reinstall");
  assert.equal(operations.operationType(plugin, [{ name: "Example", version: "1.0.9" }]), 2, "newer catalog version must update");
  assert.equal(operations.operationType(plugin, [{ name: "Example", version: "2.0.0" }]), 3, "older catalog version must downgrade");
});

test("operation type matches an installed plugin through its aliases", () => {
  const { operations } = fixture();
  const aliased = { ...plugin, name: "Renamed Example", installFolder: "Renamed Example", aliases: ["Example"] };
  assert.equal(operations.operationType(aliased, [{ name: "Example", version: "1.0.0" }]), 2);
});

test("uninstall opens Decky's native confirmation with the loader as receiver", () => {
  const { calls, uninstalls, operations } = fixture();
  operations.requestDeckyUninstall("  Example  ", "Remove?", "Remove", "Description");
  assert.deepEqual(Array.from(uninstalls[0]), ["Example", "Remove?", "Remove", "Description"]);
  assert.equal(uninstalls.length, 1);
  assert.equal(calls.length, 0, "uninstall must wait for Decky's confirmation instead of calling the backend directly");
});

test("uninstall rejects unsafe or empty plugin names before opening a dialog", () => {
  const { uninstalls, operations } = fixture();
  assert.throws(() => operations.requestDeckyUninstall("   ", "Remove?", "Remove", "Description"), /Invalid plugin name/);
  assert.throws(() => operations.requestDeckyUninstall("../Example", "Remove?", "Remove", "Description"), /Invalid plugin name/);
  assert.equal(uninstalls.length, 0);
});

test("uninstall reports when Decky's native confirmation API is unavailable", () => {
  const { operations } = fixture({ withUninstall: false });
  assert.throws(
    () => operations.requestDeckyUninstall("Example", "Remove?", "Remove", "Description"),
    /uninstall dialog is not available/,
  );
});

test("install fails closed when Decky's backend is unavailable", async () => {
  const { operations } = fixture({ withBackend: false });
  await assert.rejects(
    operations.requestDeckyInstall(plugin, {
      url: plugin.catalogReleaseUrl, name: "plugin.zip", version: "1.1.0", hash: "",
    }, []),
    /Decky backend is not available/,
  );
});

test("installed state is read from Decky's public state", () => {
  const { operations } = fixture({
    installedPlugins: [
      { name: "Example", version: "1.0.0" },
      { name: "Disabled Example", version: "2.0.0" },
      { name: "../Unsafe", version: "9.9.9" },
    ],
    disabledPlugins: [{ name: "Disabled Example", version: "2.0.0" }],
  });
  assert.deepEqual(Array.from(operations.readInstalledPlugins(), (item) => ({ ...item })), [
    { name: "Example", version: "1.0.0", disabled: false },
    { name: "Disabled Example", version: "2.0.0", disabled: true },
  ]);
});

test("install and update requests are serialized while uninstall remains independent", async () => {
  const releases = [];
  const { calls, operations, uninstalls } = fixture({
    backendCall: () => new Promise((resolve) => releases.push(resolve)),
  });
  const artifact = { url: plugin.catalogReleaseUrl, name: "plugin.zip", version: "1.1.0", hash: "" };
  const first = operations.requestDeckyInstall(plugin, artifact, [{ name: "Example", version: "1.0.0" }]);
  const secondPlugin = { ...plugin, name: "Second", installFolder: "Second" };
  const second = operations.requestDeckyInstall(secondPlugin, artifact, []);

  await Promise.resolve();
  await Promise.resolve();
  assert.equal(calls.length, 1, "the second backend operation must wait for the first one");

  operations.requestDeckyUninstall("Example", "Remove?", "Remove", "Description");
  assert.equal(uninstalls.length, 1, "uninstall must not wait behind the install queue");

  releases.shift()();
  await first;
  await Promise.resolve();
  assert.equal(calls.length, 2, "the next install starts only after the previous backend call settles");
  releases.shift()();
  await second;
});

test("global progress is fail-closed while events from multiple plugins overlap", () => {
  const { emit, operations } = fixture();
  const progress = [];
  const unsubscribe = operations.subscribePluginProgress((event) => progress.push({ ...event }));

  emit("loader/plugin_download_start", "Example");
  emit("loader/plugin_download_info", 10, "Example download");
  emit("loader/plugin_download_start", "Other");
  emit("loader/plugin_download_info", 20, "Ambiguous download");
  emit("loader/plugin_download_finish", "Unknown");
  emit("loader/plugin_download_finish", "Other");
  emit("loader/plugin_download_info", 70, "Example resumes");
  emit("loader/plugin_download_finish", "Example");
  unsubscribe();

  assert.deepEqual(progress.map(({ plugin, percentage, active }) => [plugin, percentage, active]), [
    ["Example", 0, true],
    ["Example", 10, true],
    ["Other", 0, true],
    ["Other", 100, false],
    ["Example", 70, true],
    ["Example", 100, false],
  ]);
  assert.equal(progress.some((event) => event.message === "Ambiguous download"), false);
});
