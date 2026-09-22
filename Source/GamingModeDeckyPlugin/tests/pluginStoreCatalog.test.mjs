import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import vm from "node:vm";
import ts from "typescript";

const here = dirname(fileURLToPath(import.meta.url));
const source = readFileSync(join(here, "..", "src", "pluginStoreCatalog.ts"), "utf8");
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const exports = {};
vm.runInNewContext(compiled, { exports, URL, Set, Map, Date, Number, JSON, Intl });

test("canonical catalog parses without duplicating content and hides the integrated Playhub plugin", () => {
  const raw = readFileSync(join(here, "..", "..", "..", "catalog", "plugins.json"), "utf8");
  const original = JSON.parse(raw).plugins.filter((plugin) => plugin.active !== false);
  const parsed = exports.parseCatalog(raw);
  assert.ok(parsed.plugins.length >= 160, `expected the canonical catalog, got ${parsed.plugins.length} entries`);
  assert.ok(parsed.plugins.length <= original.length);
  assert.equal(parsed.plugins.some((plugin) => exports.isIntegratedPlayhubPlugin(plugin)), false);
  assert.equal(parsed.plugins.some((plugin) => plugin.name === "Shortcuts"), false);

  const featured = exports.featuredPlugins(parsed.plugins, 6);
  const featuredRepositories = Array.from(featured, (plugin) => plugin.repository.toLowerCase());
  assert.equal(featured[0].catalogSource, "playhub");
  for (const repository of [
    "tormak9970/tabmaster",
    "deckthemes/sdh-cssloader",
    "emerald0874/sdh-audioloader",
    "bentemple/decky-download-all",
    "jessebofill/deckwebbrowser",
  ]) assert.equal(featuredRepositories.includes(repository), true, `missing featured plugin ${repository}`);
  assert.equal(featured.filter((plugin) => plugin.name === "CSS Loader").length, 1);
  assert.equal(featured.find((plugin) => plugin.name === "CSS Loader").catalogSource, "decky-store");

  const cheevoDeck = parsed.plugins.find((plugin) => plugin.repository === "FAILINATOR5000/decky-cheevodeck");
  assert.equal(cheevoDeck?.name, "CheevoDeck");
  assert.equal(exports.resolveInstallArtifact(cheevoDeck)?.name, "CheevoDeck-0.8.8.zip");
});

test("unsafe catalog entries and download URLs fail closed", () => {
  const raw = {
    plugins: [
      { active: true, name: "Safe Plugin", installFolder: "Safe Plugin", repository: "owner/repo", catalogSource: "outside-store" },
      { active: true, name: "Bad/Plugin", installFolder: "bad", repository: "owner/repo", catalogSource: "outside-store" },
    ],
  };
  const parsed = exports.parseCatalog(raw);
  assert.equal(parsed.plugins.length, 1);
  assert.equal(exports.resolveInstallArtifact({ ...parsed.plugins[0], catalogReleaseUrl: "https://evil.example/plugin.zip", releaseAssetName: "plugin.zip" }), null);
});

test("search, source filters and installed updates are deterministic", () => {
  const make = (name, source, version) => ({
    active: true, name, installFolder: name, author: "A", repository: `owner/${name}`,
    repositoryUrl: `https://github.com/owner/${name}`, version, releaseAssetName: `${name}.zip`, catalogReleaseUrl: `https://github.com/owner/${name}/releases/download/${version}/${name}.zip`,
    releasePublishedAt: "", category: "Tools", shortDescription: `${name} utility`, longDescription: "",
    coverUrl: "", iconGlyph: "", catalogStatus: "", catalogSource: source, catalogPluginId: 0,
    compatibility: "", keywords: [], aliases: [],
  });
  const plugins = [make("Beta", "decky-store", "2.0.0"), make("Alpha", "playhub", "1.0.0")];
  const installed = [{ name: "Beta", version: "1.0.0" }, { name: "Alpha", version: "1.0.0" }];
  assert.equal(exports.pluginHasUpdate(plugins[0], installed), true);
  assert.equal(exports.pluginHasUpdate({ ...plugins[0], catalogReleaseUrl: "" }, installed), false);
  assert.deepEqual(Array.from(exports.filterPlugins(plugins, { source: "playhub" }), (plugin) => plugin.name), ["Alpha"]);
  assert.deepEqual(Array.from(exports.filterPlugins(plugins, { query: "utility" }), (plugin) => plugin.name), ["Alpha", "Beta"]);
  assert.deepEqual(Array.from(exports.filterPlugins(plugins, { installed, installedOnly: true }), (plugin) => plugin.name), ["Beta", "Alpha"]);
});

test("newest uses the first release even when an older plugin gets an update or joins the catalog", () => {
  const make = (name, version, releasePublishedAt, keywords = []) => ({
    active: true, name, installFolder: name, author: "A", repository: `owner/${name}`,
    repositoryUrl: `https://github.com/owner/${name}`, version, releaseAssetName: "", catalogReleaseUrl: "",
    releasePublishedAt, category: "Tools", shortDescription: "", longDescription: "",
    coverUrl: "", iconGlyph: "", catalogStatus: "", catalogSource: "outside-store", catalogPluginId: 0,
    compatibility: "", keywords, aliases: [],
  });
  const plugins = [
    make("Alpha", "3.0.0", "2026-09-07T00:00:00Z", ["catalog-added:2026-09-07", "first-release:2024-01-01"]),
    make("Beta", "1.0.0", "2026-09-01T00:00:00Z", ["catalog-added:2026-09-04", "first-release:2026-09-01"]),
    make("Gamma", "2.0.0", "2026-09-05T00:00:00Z", ["catalog-added:2026-09-07"]),
  ];

  assert.deepEqual(Array.from(exports.newestPlugins(plugins, 12, Date.parse("2026-09-07T00:00:00Z")), (plugin) => plugin.name), ["Beta"]);
  assert.deepEqual(Array.from(exports.newestPlugins(plugins, 12, Date.parse("2026-11-07T00:00:00Z"))), []);
  assert.deepEqual(Array.from(exports.filterPlugins(plugins, { sort: "newest" }), (plugin) => plugin.name), ["Beta", "Alpha", "Gamma"]);
  assert.deepEqual(Array.from(exports.filterPlugins(plugins, { sort: "name" }), (plugin) => plugin.name), ["Alpha", "Beta", "Gamma"]);
  assert.deepEqual(Array.from(exports.filterPlugins(plugins, { sort: "updated" }), (plugin) => plugin.name), ["Alpha", "Gamma", "Beta"]);
});

test("featured selection is deterministic, mixes Playhub with the requested Decky plugins and excludes duplicate CSS Loader entries", () => {
  const make = (name, repository, source, added = "2026-01-01") => ({
    active: true, name, installFolder: name, author: "A", repository,
    repositoryUrl: `https://github.com/${repository}`, version: "1.0.0", releaseAssetName: "", catalogReleaseUrl: "",
    releasePublishedAt: added, category: source === "playhub" ? "Playhub" : "Tools", shortDescription: "", longDescription: "",
    coverUrl: "", iconGlyph: "", catalogStatus: "", catalogSource: source, catalogPluginId: 0,
    compatibility: "", keywords: [`catalog-added:${added.slice(0, 10)}`], aliases: [],
  });
  const plugins = [
    make("Older Playhub", "playhub/older", "playhub", "2026-08-01"),
    make("Newest Playhub", "playhub/newest", "playhub", "2026-09-01"),
    make("CSS Loader", "somebody/css-loader", "outside-store", "2026-09-07"),
    make("Web Browser", "jessebofill/DeckWebBrowser", "decky-store"),
    make("Download All", "bentemple/decky-download-all", "decky-store"),
    make("Audio Loader", "EMERALD0874/SDH-AudioLoader", "decky-store"),
    make("CSS Loader", "DeckThemes/SDH-CssLoader", "decky-store"),
    make("TabMaster", "Tormak9970/TabMaster", "decky-store"),
  ];

  const first = exports.featuredPlugins(plugins, 6);
  const second = exports.featuredPlugins([...plugins].reverse(), 6);
  const repositories = Array.from(first, (plugin) => plugin.repository);
  assert.deepEqual(repositories, [
    "playhub/newest",
    "Tormak9970/TabMaster",
    "DeckThemes/SDH-CssLoader",
    "EMERALD0874/SDH-AudioLoader",
    "bentemple/decky-download-all",
    "jessebofill/DeckWebBrowser",
  ]);
  assert.deepEqual(Array.from(second, (plugin) => plugin.repository), repositories);
  assert.equal(first.filter((plugin) => plugin.name === "CSS Loader").length, 1);
  assert.equal(first.find((plugin) => plugin.name === "CSS Loader").catalogSource, "decky-store");
});

test("version comparison handles prefixes, prereleases, build metadata and Decky revisions without false updates", () => {
  assert.equal(exports.compareVersions("v1.2.0", "1.2"), 0);
  assert.equal(exports.compareVersions("1.10.0", "1.9.9"), 1);
  assert.equal(exports.compareVersions("1.2.0", "1.2.0-rc.2"), 1);
  assert.equal(exports.compareVersions("1.2.0-rc.10", "1.2.0-rc.2"), 1);
  assert.equal(exports.compareVersions("1.2.0+catalog.4", "1.2.0+installed.9"), 0);
  assert.equal(exports.compareVersions("1.2.0", "1.2.0-1"), -1);
  assert.equal(exports.compareVersions("not-a-version", "1.0.0"), 0);

  const plugin = {
    active: true, name: "Plugin", installFolder: "Plugin", author: "A", repository: "owner/plugin",
    repositoryUrl: "https://github.com/owner/plugin", version: "2.1.2", releaseAssetName: "plugin.zip", catalogReleaseUrl: "https://github.com/owner/plugin/releases/download/v2.1.2/plugin.zip",
    releasePublishedAt: "", category: "Tools", shortDescription: "", longDescription: "", coverUrl: "",
    iconGlyph: "", catalogStatus: "", catalogSource: "decky-store", catalogPluginId: 0,
    compatibility: "", keywords: [], aliases: [],
  };
  assert.equal(exports.pluginHasUpdate(plugin, [{ name: "Plugin", version: "2.1.2-1" }]), false);
  assert.equal(exports.pluginHasUpdate(plugin, [{ name: "Plugin", version: "dev" }]), false);
  assert.equal(exports.pluginHasUpdate({ ...plugin, version: "2.1.3" }, [{ name: "Plugin", version: "2.1.2-1" }]), true);
});

test("release resolver prefers the named real ZIP and carries Decky hashes", () => {
  const plugin = {
    name: "Plugin", version: "1.0.0", releaseAssetName: "", catalogReleaseUrl: "",
  };
  const release = exports.parseGithubRelease({
    tag_name: "v1.1.0", assets: [
      { name: "source-code.zip", browser_download_url: "https://github.com/owner/repo/releases/download/v1/source-code.zip" },
      { name: `${"a".repeat(64)}.zip`, browser_download_url: `https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/${"a".repeat(64)}.zip` },
    ],
  });
  const artifact = exports.resolveInstallArtifact(plugin, release);
  assert.equal(artifact.version, "1.1.0");
  assert.equal(artifact.hash, "a".repeat(64));
});

test("release resolver prefers plugin installers over unrelated and source archives", () => {
  const plugin = {
    name: "Shortcuts", repository: "LoZazaMastro/Shortcuts", version: "1.0.0",
    releaseAssetName: "", catalogReleaseUrl: "",
  };
  const release = exports.parseGithubRelease({
    tag_name: "v1.1.0", assets: [
      { name: "Source.zip", browser_download_url: "https://github.com/LoZazaMastro/Shortcuts/releases/download/v1.1.0/Source.zip", size: 9_000_000 },
      { name: "unrelated.zip", browser_download_url: "https://github.com/LoZazaMastro/Shortcuts/releases/download/v1.1.0/unrelated.zip", size: 4_000_000 },
      { name: "Shortcuts-decky_Installer-1.1.0.zip", browser_download_url: "https://github.com/LoZazaMastro/Shortcuts/releases/download/v1.1.0/Shortcuts-decky_Installer-1.1.0.zip", size: 500_000 },
    ],
  });
  assert.equal(exports.resolveInstallArtifact(plugin, release).name, "Shortcuts-decky_Installer-1.1.0.zip");
});

test("a GitHub tag is ignored until its current release has a compatible installer", () => {
  const plugin = {
    name: "Shortcuts", installFolder: "Shortcuts", aliases: [], repository: "LoZazaMastro/Shortcuts",
    version: "1.0.0", releaseAssetName: "Shortcuts-decky_Installer-1.0.0.zip",
    catalogReleaseUrl: "https://github.com/LoZazaMastro/Shortcuts/releases/download/v1.0.0/Shortcuts-decky_Installer-1.0.0.zip",
  };
  const sourceOnly = exports.parseGithubRelease({
    tag_name: "v1.2.0", name: "Uninstallable", body: "Must not be shown", published_at: "2026-09-07T00:00:00Z",
    assets: [{
      name: "source-code.zip",
      browser_download_url: "https://github.com/LoZazaMastro/Shortcuts/releases/download/v1.2.0/source-code.zip",
    }],
  });
  assert.equal(sourceOnly, null, "source archives must not promote release metadata or notes");

  const oldArchive = exports.parseGithubRelease({
    tag_name: "v1.2.0", name: "Wrong binary", body: "Must not be shown",
    assets: [{
      name: "Shortcuts-decky_Installer-1.0.0.zip",
      browser_download_url: "https://github.com/LoZazaMastro/Shortcuts/releases/download/v1.0.0/Shortcuts-decky_Installer-1.0.0.zip",
    }],
  });
  assert.equal(oldArchive, null, "an archive attached to an older tag must not promote the latest tag");

  const unresolved = {
    tag_name: "v1.2.0", name: "Unrelated", body: "Must not be shown", published_at: "2026-09-07T00:00:00Z",
    html_url: "https://github.com/LoZazaMastro/Shortcuts/releases/tag/v1.2.0",
    assets: [{
      name: "DifferentPlugin-decky_Installer-1.2.0.zip", size: 500_000,
      browser_download_url: "https://github.com/LoZazaMastro/Shortcuts/releases/download/v1.2.0/DifferentPlugin-decky_Installer-1.2.0.zip",
    }],
  };
  assert.equal(exports.parseGithubRelease(unresolved), null,
    "an unrelated installer must not expose the tag or notes to the store");
  assert.equal(exports.resolveInstallArtifact(plugin, unresolved), null,
    "an incompatible latest release must not fall back to the old catalog ZIP");
});

test("only the resolved installable release carries its version and notes", () => {
  const plugin = {
    name: "Shortcuts", installFolder: "Shortcuts", aliases: [], repository: "LoZazaMastro/Shortcuts",
    version: "1.0.0", releaseAssetName: "", catalogReleaseUrl: "",
  };
  const release = exports.parseGithubRelease({
    tag_name: "v1.2.0", name: "Shortcuts 1.2.0", body: "Real release notes",
    published_at: "2026-09-07T00:00:00Z",
    html_url: "https://github.com/LoZazaMastro/Shortcuts/releases/tag/v1.2.0",
    assets: [{
      name: "Shortcuts-decky_Installer-1.2.0.zip", size: 500_000,
      browser_download_url: "https://github.com/LoZazaMastro/Shortcuts/releases/download/v1.2.0/Shortcuts-decky_Installer-1.2.0.zip",
    }],
  });
  const artifact = exports.resolveInstallArtifact(plugin, release);
  assert.equal(artifact.version, "1.2.0");
  assert.equal(artifact.release.body, "Real release notes");
  assert.equal(artifact.release.publishedAt, "2026-09-07T00:00:00Z");
});

