export const PLAYHUB_CATALOG_URL =
  "https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/plugins.json";

export type PluginSource = "playhub" | "decky-store" | "outside-store";
export type PluginSort = "name" | "newest" | "updated";

export interface CatalogPlugin {
  active: boolean;
  name: string;
  installFolder: string;
  author: string;
  repository: string;
  repositoryUrl: string;
  version: string;
  releaseAssetName: string;
  catalogReleaseUrl: string;
  releasePublishedAt: string;
  category: string;
  shortDescription: string;
  longDescription: string;
  coverUrl: string;
  iconGlyph: string;
  catalogStatus: string;
  catalogSource: PluginSource;
  catalogPluginId: number;
  compatibility: string;
  keywords: string[];
  aliases: string[];
}

export interface CatalogDocument {
  schemaVersion: number;
  verifiedAt: string;
  catalogRevision: number;
  plugins: CatalogPlugin[];
}

export function pluginCategory(plugin: CatalogPlugin): string {
  if (plugin.catalogSource !== "playhub") return plugin.category;
  const name = normalizePluginIdentity(plugin.name);
  if (["Playhub Artworks", "Launch Curtain", "Now Playing", "ThemeDeck", "TrailerHero", "Playhub Surround"].some((value) => normalizePluginIdentity(value) === name)) return "Personalizzazione e media";
  if (name === normalizePluginIdentity("Playhub Metadata")) return "Libreria e giochi";
  if (name === normalizePluginIdentity("News")) return "Social e community";
  if (name === normalizePluginIdentity("Proton VPN")) return "Sistema e hardware";
  return plugin.category === "Playhub" ? "Strumenti e utilità" : plugin.category;
}

export function pluginInCategory(plugin: CatalogPlugin, category: string): boolean {
  return category === "Playhub" ? plugin.catalogSource === "playhub" : pluginCategory(plugin) === category;
}

export interface InstalledPlugin {
  name: string;
  version: string;
  disabled?: boolean;
}

export interface GithubReleaseAsset {
  name: string;
  browser_download_url: string;
  size: number;
}

export interface GithubRelease {
  tag_name: string;
  name: string;
  body: string;
  published_at: string;
  html_url: string;
  assets: GithubReleaseAsset[];
}

export interface InstallArtifact {
  url: string;
  name: string;
  version: string;
  hash: string;
  release?: {
    name: string;
    body: string;
    publishedAt: string;
    htmlUrl: string;
  };
}

const SOURCE_VALUES = new Set<PluginSource>(["playhub", "decky-store", "outside-store"]);
const SAFE_DOWNLOAD_HOSTS = new Set([
  "github.com",
  "objects.githubusercontent.com",
  "release-assets.githubusercontent.com",
  "cdn.tzatzikiweeb.moe",
]);

function objectValue(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function text(value: unknown, max = 12_000): string {
  return typeof value === "string" ? value.trim().slice(0, max) : "";
}

function textList(value: unknown, maxItems = 48): string[] {
  if (!Array.isArray(value)) return [];
  return value.slice(0, maxItems).map((item) => text(item, 160)).filter(Boolean);
}

export function normalizePluginIdentity(value: unknown): string {
  return text(value, 240).normalize("NFKC").toLocaleLowerCase("en-US").replace(/[^\p{L}\p{N}]+/gu, "");
}

export function isIntegratedPlayhubPlugin(plugin: Pick<CatalogPlugin, "name" | "installFolder" | "aliases">): boolean {
  const identities = [plugin.name, plugin.installFolder, ...plugin.aliases].map(normalizePluginIdentity);
  return identities.includes("playhub") || identities.includes("gamingmode") || identities.includes("quicksettings") || identities.includes("shortcuts");
}

export function isSafeRepository(value: unknown): value is string {
  return typeof value === "string" && /^[A-Za-z0-9_.-]{1,100}\/[A-Za-z0-9_.-]{1,100}$/.test(value);
}

export function isSafePluginName(value: unknown): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= 160 &&
    /^[\p{L}\p{N} ._+()'&-]+$/u.test(value);
}

export function isSafeHttpsUrl(value: unknown, hosts?: Set<string>): value is string {
  if (typeof value !== "string" || value.length > 2_048) return false;
  try {
    const url = new URL(value);
    return url.protocol === "https:" && (!hosts || hosts.has(url.hostname.toLocaleLowerCase("en-US")));
  } catch {
    return false;
  }
}

function parsePlugin(value: unknown): CatalogPlugin | null {
  const source = objectValue(value);
  if (!source || source.active === false) return null;

  const name = text(source.name, 160);
  const installFolder = text(source.installFolder, 160) || name;
  const repository = text(source.repository, 220);
  const aliases = textList(source.aliases);
  const catalogSource = text(source.catalogSource, 40) as PluginSource;
  if (!isSafePluginName(name) || !isSafePluginName(installFolder) || !isSafeRepository(repository) ||
      !SOURCE_VALUES.has(catalogSource)) return null;

  const repositoryUrlValue = text(source.repositoryUrl, 2_048);
  const repositoryUrl = isSafeHttpsUrl(repositoryUrlValue)
    ? repositoryUrlValue
    : `https://github.com/${repository}`;
  const coverUrlValue = text(source.coverUrl, 2_048);
  const releaseUrlValue = text(source.catalogReleaseUrl, 2_048);

  const plugin: CatalogPlugin = {
    active: true,
    name,
    installFolder,
    author: text(source.author, 160),
    repository,
    repositoryUrl,
    version: text(source.version, 80),
    releaseAssetName: text(source.releaseAssetName, 260),
    catalogReleaseUrl: isSafeHttpsUrl(releaseUrlValue, SAFE_DOWNLOAD_HOSTS) ? releaseUrlValue : "",
    releasePublishedAt: text(source.releasePublishedAt, 80),
    category: text(source.category, 120) || "Other",
    shortDescription: text(source.shortDescription, 2_000),
    longDescription: text(source.longDescription),
    coverUrl: isSafeHttpsUrl(coverUrlValue) ? coverUrlValue : "",
    iconGlyph: text(source.iconGlyph, 16),
    catalogStatus: text(source.catalogStatus, 40),
    catalogSource,
    catalogPluginId: Number.isFinite(Number(source.catalogPluginId)) ? Number(source.catalogPluginId) : 0,
    compatibility: text(source.compatibility, 2_000),
    keywords: textList(source.keywords),
    aliases,
  };
  return isIntegratedPlayhubPlugin(plugin) ? null : plugin;
}

export function parseCatalog(value: unknown): CatalogDocument {
  const root = typeof value === "string" ? objectValue(JSON.parse(value)) : objectValue(value);
  if (!root || !Array.isArray(root.plugins)) throw new Error("Invalid Playhub plugin catalog.");

  const seen = new Set<string>();
  const plugins: CatalogPlugin[] = [];
  for (const candidate of root.plugins) {
    const plugin = parsePlugin(candidate);
    if (!plugin) continue;
    const key = `${normalizePluginIdentity(plugin.repository)}:${normalizePluginIdentity(plugin.name)}`;
    if (seen.has(key)) continue;
    seen.add(key);
    plugins.push(plugin);
  }
  if (!plugins.length) throw new Error("The Playhub plugin catalog is empty.");

  return {
    schemaVersion: Number(root.schemaVersion) || 0,
    verifiedAt: text(root.verifiedAt, 80),
    catalogRevision: Number(root.catalogRevision) || 0,
    plugins,
  };
}

type VersionQualifier = "stable" | "prerelease" | "revision" | "opaque";

interface ParsedVersion {
  core: number[];
  qualifier: VersionQualifier;
  identifiers: string[];
}

const PRERELEASE_RANK = new Map<string, number>([
  ["dev", 0], ["snapshot", 0], ["nightly", 0], ["canary", 0],
  ["alpha", 1], ["a", 1],
  ["beta", 2], ["b", 2],
  ["pre", 3], ["preview", 3],
  ["rc", 4],
]);

function parseVersion(value: string): ParsedVersion | null {
  const normalized = value.trim().normalize("NFKC");
  const match = normalized.match(/^(?:version\s*)?v?(\d+(?:\.\d+){0,7})(.*)$/i);
  if (!match) return null;

  const core = match[1].split(".").map(Number);
  while (core.length > 1 && core[core.length - 1] === 0) core.pop();

  let suffix = match[2].trim();
  if (!suffix || suffix.startsWith("+") || /^(?:\([^)]*\)|\[[^\]]*\])$/.test(suffix)) {
    return { core, qualifier: "stable", identifiers: [] };
  }
  if (!suffix.startsWith("-")) return null;
  suffix = suffix.slice(1).split("+", 1)[0].trim();
  const identifiers = suffix.split(/[._-]+/).filter(Boolean).map((item) => item.toLocaleLowerCase("en-US"));
  if (!identifiers.length) return { core, qualifier: "stable", identifiers: [] };

  const first = identifiers[0];
  if (PRERELEASE_RANK.has(first)) return { core, qualifier: "prerelease", identifiers };
  if (/^(?:r|rev|post)?\d+$/.test(first)) return { core, qualifier: "revision", identifiers };
  return { core, qualifier: "opaque", identifiers };
}

function compareNumberLists(left: number[], right: number[]): number {
  for (let index = 0; index < Math.max(left.length, right.length); index += 1) {
    const delta = (left[index] ?? 0) - (right[index] ?? 0);
    if (delta) return delta > 0 ? 1 : -1;
  }
  return 0;
}

function compareIdentifiers(left: string[], right: string[]): number {
  for (let index = 0; index < Math.max(left.length, right.length); index += 1) {
    const a = left[index];
    const b = right[index];
    if (a === undefined) return -1;
    if (b === undefined) return 1;
    if (a === b) continue;
    const aNumber = /^\d+$/.test(a) ? Number(a) : null;
    const bNumber = /^\d+$/.test(b) ? Number(b) : null;
    if (aNumber !== null && bNumber !== null) return aNumber > bNumber ? 1 : -1;
    if (aNumber !== null) return -1;
    if (bNumber !== null) return 1;
    const delta = a.localeCompare(b, "en-US", { sensitivity: "base" });
    if (delta) return delta > 0 ? 1 : -1;
  }
  return 0;
}

function revisionNumber(value: string | undefined): number {
  const match = value?.match(/\d+/);
  return match ? Number(match[0]) : 0;
}

export function compareVersions(left: string, right: string): number {
  const a = parseVersion(left);
  const b = parseVersion(right);
  if (!a || !b) return 0;

  const coreDelta = compareNumberLists(a.core, b.core);
  if (coreDelta) return coreDelta;
  if (a.qualifier === "opaque" || b.qualifier === "opaque") return 0;
  if (a.qualifier === b.qualifier) {
    if (a.qualifier === "stable") return 0;
    if (a.qualifier === "revision") {
      const delta = revisionNumber(a.identifiers[0]) - revisionNumber(b.identifiers[0]);
      return delta ? (delta > 0 ? 1 : -1) : compareIdentifiers(a.identifiers.slice(1), b.identifiers.slice(1));
    }
    const rankDelta = (PRERELEASE_RANK.get(a.identifiers[0]) ?? 0) -
      (PRERELEASE_RANK.get(b.identifiers[0]) ?? 0);
    return rankDelta ? (rankDelta > 0 ? 1 : -1) : compareIdentifiers(a.identifiers.slice(1), b.identifiers.slice(1));
  }
  const rank: Record<Exclude<VersionQualifier, "opaque">, number> = {
    prerelease: 0,
    stable: 1,
    revision: 2,
  };
  return rank[a.qualifier] > rank[b.qualifier] ? 1 : -1;
}

export function findInstalledPlugin(plugin: CatalogPlugin, installed: InstalledPlugin[]): InstalledPlugin | undefined {
  const identities = new Set(
    [plugin.name, plugin.installFolder, ...plugin.aliases].map(normalizePluginIdentity).filter(Boolean),
  );
  return installed.find((candidate) => identities.has(normalizePluginIdentity(candidate.name)));
}

export function pluginHasUpdate(plugin: CatalogPlugin, installed: InstalledPlugin[]): boolean {
  const local = findInstalledPlugin(plugin, installed);
  return Boolean(local?.version && plugin.version && compareVersions(plugin.version, local.version) > 0 && resolveInstallArtifact(plugin));
}

export function filterPlugins(
  plugins: CatalogPlugin[],
  options: {
    query?: string;
    source?: PluginSource | "all";
    installed?: InstalledPlugin[];
    installedOnly?: boolean;
    category?: string;
    sort?: PluginSort;
  } = {},
): CatalogPlugin[] {
  const query = normalizePluginIdentity(options.query ?? "");
  const installed = options.installed ?? [];
  const result = plugins.filter((plugin) => {
    if (options.source && options.source !== "all" && plugin.catalogSource !== options.source) return false;
    if (options.category && plugin.category !== options.category) return false;
    if (options.installedOnly && !findInstalledPlugin(plugin, installed)) return false;
    if (!query) return true;
    return [plugin.name, plugin.author, plugin.repository, plugin.shortDescription, ...plugin.keywords, ...plugin.aliases]
      .some((value) => normalizePluginIdentity(value).includes(query));
  });

  const sort = options.sort ?? "name";
  return result.sort((left, right) => {
    if (options.installedOnly) {
      const updateDelta = Number(pluginHasUpdate(right, installed)) - Number(pluginHasUpdate(left, installed));
      if (updateDelta) return updateDelta;
    }
    if (sort === "newest") {
      const delta = compareFirstRelease(left, right);
      if (delta) return delta;
    }
    if (sort === "updated") {
      const delta = (timestamp(right.releasePublishedAt) ?? 0) - (timestamp(left.releasePublishedAt) ?? 0);
      if (delta) return delta;
    }
    return left.name.localeCompare(right.name, undefined, { numeric: true, sensitivity: "base" });
  });
}

function timestamp(value: string): number | null {
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function catalogAddedAt(plugin: CatalogPlugin): number {
  const marker = plugin.keywords.find((value) => /^catalog-added:/i.test(value));
  const markedAt = marker ? timestamp(marker.slice("catalog-added:".length).trim()) : null;
  return markedAt ?? 0;
}

function compareCatalogAdded(left: CatalogPlugin, right: CatalogPlugin): number {
  const dateDelta = catalogAddedAt(right) - catalogAddedAt(left);
  if (dateDelta) return dateDelta;
  const nameDelta = left.name.localeCompare(right.name, "en-US", { numeric: true, sensitivity: "base" });
  return nameDelta || left.repository.localeCompare(right.repository, "en-US", { sensitivity: "base" });
}

function compareFirstRelease(left: CatalogPlugin, right: CatalogPlugin): number {
  const firstReleaseAt = (plugin: CatalogPlugin) => {
    const marker = plugin.keywords.find((value) => /^first-release:/i.test(value));
    return marker ? timestamp(marker.slice("first-release:".length).trim()) ?? 0 : 0;
  };
  const delta = firstReleaseAt(right) - firstReleaseAt(left);
  return delta || left.name.localeCompare(right.name, "en-US", { numeric: true, sensitivity: "base" });
}

export function newestPlugins(plugins: CatalogPlugin[], limit = 12, now = Date.now()): CatalogPlugin[] {
  return [...plugins]
    .filter((plugin) => {
      const marker = plugin.keywords.find((value) => /^first-release:/i.test(value));
      const first = marker ? timestamp(marker.slice("first-release:".length).trim()) : null;
      return first !== null && first <= now && now - first <= 30 * 24 * 60 * 60 * 1000;
    })
    .sort(compareFirstRelease)
    .slice(0, limit);
}

const FEATURED_DECKY_REPOSITORIES = [
  "Tormak9970/TabMaster",
  "DeckThemes/SDH-CssLoader",
  "EMERALD0874/SDH-AudioLoader",
  "bentemple/decky-download-all",
  "jessebofill/DeckWebBrowser",
] as const;

function featuredIdentityKeys(plugin: CatalogPlugin): string[] {
  return [
    `repository:${normalizePluginIdentity(plugin.repository)}`,
    `folder:${normalizePluginIdentity(plugin.installFolder)}`,
    `name:${normalizePluginIdentity(plugin.name)}`,
  ];
}

function isCssLoader(plugin: CatalogPlugin): boolean {
  return [plugin.name, plugin.installFolder, plugin.repository, ...plugin.aliases]
    .some((value) => normalizePluginIdentity(value).includes("cssloader"));
}

export function featuredPlugins(plugins: CatalogPlugin[], limit = 6): CatalogPlugin[] {
  const requestedLimit = Math.max(0, Math.floor(limit));
  if (!requestedLimit) return [];

  const playhub = plugins.filter((plugin) => plugin.catalogSource === "playhub").sort(compareCatalogAdded);
  const required = FEATURED_DECKY_REPOSITORIES.map((repository) => plugins.find((plugin) =>
    plugin.catalogSource === "decky-store" &&
    plugin.repository.toLocaleLowerCase("en-US") === repository.toLocaleLowerCase("en-US"),
  )).filter((plugin): plugin is CatalogPlugin => Boolean(plugin));
  const selected: CatalogPlugin[] = [];
  const used = new Set<string>();
  const push = (plugin: CatalogPlugin | undefined): boolean => {
    if (!plugin || selected.length >= requestedLimit) return false;
    const keys = featuredIdentityKeys(plugin);
    if (keys.some((key) => used.has(key))) return false;
    keys.forEach((key) => used.add(key));
    selected.push(plugin);
    return true;
  };

  let playhubIndex = 0;
  if (requestedLimit > required.length) push(playhub[playhubIndex++]);
  required.forEach((plugin, index) => {
    push(plugin);
    const requiredRemaining = required.length - index - 1;
    if (requestedLimit - selected.length > requiredRemaining) push(playhub[playhubIndex++]);
  });

  const fallback = [
    ...playhub.slice(playhubIndex),
    ...newestPlugins(plugins.filter((plugin) =>
      plugin.catalogSource !== "playhub" &&
      (!isCssLoader(plugin) || (
        plugin.catalogSource === "decky-store" &&
        plugin.repository.toLocaleLowerCase("en-US") === "deckthemes/sdh-cssloader"
      )),
    ), plugins.length),
  ];
  for (const plugin of fallback) {
    push(plugin);
    if (selected.length >= requestedLimit) break;
  }
  return selected;
}

const SOURCE_ARCHIVE_PATTERN = /(^|[-_. (])(source[-_. ]?code|sources?|src|project)(?=$|[-_. )])/i;
const GENERIC_ARCHIVE_TOKENS = new Set([
  "decky", "plugin", "installer", "install", "release", "archive", "bundle",
  "windows", "linux", "steamos", "sdh", "prod", "production", "version",
]);

function releaseTagFromAssetUrl(value: string): string | null {
  try {
    const url = new URL(value);
    if (url.hostname.toLocaleLowerCase("en-US") !== "github.com") return null;
    const parts = url.pathname.split("/").filter(Boolean);
    const releaseIndex = parts.findIndex((part, index) =>
      part.toLocaleLowerCase("en-US") === "releases" &&
      parts[index + 1]?.toLocaleLowerCase("en-US") === "download",
    );
    return releaseIndex >= 0 && parts[releaseIndex + 2]
      ? decodeURIComponent(parts[releaseIndex + 2])
      : null;
  } catch {
    return null;
  }
}

function repositoryNameFromAssetUrl(value: string): string {
  try {
    const url = new URL(value);
    if (url.hostname.toLocaleLowerCase("en-US") !== "github.com") return "";
    const parts = url.pathname.split("/").filter(Boolean);
    return parts[2]?.toLocaleLowerCase("en-US") === "releases" ? parts[1] ?? "" : "";
  } catch {
    return "";
  }
}

function archiveWords(value: string): Set<string> {
  const separated = value
    .replace(/([\p{Ll}\p{N}])([\p{Lu}])/gu, "$1 $2")
    .replace(/\.zip$/i, "")
    .toLocaleLowerCase("en-US");
  return new Set(separated.split(/[^\p{L}\p{N}]+/u).filter((word) =>
    word.length >= 3 &&
    !GENERIC_ARCHIVE_TOKENS.has(word) &&
    !/^v?\d+(?:\.\d+)*$/i.test(word),
  ));
}

function isContentAddressedDeckyAsset(asset: GithubReleaseAsset): boolean {
  if (!/^[a-f0-9]{64}\.zip$/i.test(asset.name)) return false;
  try {
    return new URL(asset.browser_download_url).hostname.toLocaleLowerCase("en-US") === "cdn.tzatzikiweeb.moe";
  } catch {
    return false;
  }
}

function isCurrentReleaseAsset(asset: GithubReleaseAsset, tag: string): boolean {
  if (SOURCE_ARCHIVE_PATTERN.test(asset.name)) return false;
  if (isContentAddressedDeckyAsset(asset)) return true;
  return Boolean(tag) && releaseTagFromAssetUrl(asset.browser_download_url) === tag;
}

function assetMatchesIdentities(asset: GithubReleaseAsset, identities: string[]): boolean {
  if (isContentAddressedDeckyAsset(asset)) return true;
  const archiveIdentity = normalizePluginIdentity(asset.name.replace(/\.zip$/i, ""));
  if (identities.some((identity) => identity.length >= 4 &&
    (archiveIdentity.includes(identity) || archiveIdentity.length >= 4 && identity.includes(archiveIdentity)))) return true;

  const assetTokens = archiveWords(asset.name);
  return identities.some((identity) => {
    const identityTokens = archiveWords(identity);
    return [...identityTokens].some((token) => assetTokens.has(token));
  });
}

function looksCompatibleWithReleaseRepository(asset: GithubReleaseAsset): boolean {
  if (isContentAddressedDeckyAsset(asset)) return true;
  const repositoryName = repositoryNameFromAssetUrl(asset.browser_download_url);
  if (!repositoryName) return false;
  return assetMatchesIdentities(asset, [repositoryName]);
}

export function parseGithubRelease(value: unknown): GithubRelease | null {
  const root = objectValue(value);
  if (!root) return null;
  const tag = text(root.tag_name, 80);
  const assets = Array.isArray(root.assets) ? root.assets.map((candidate) => {
    const asset = objectValue(candidate);
    const name = text(asset?.name, 260);
    const url = text(asset?.browser_download_url, 2_048);
    return asset && name.toLocaleLowerCase("en-US").endsWith(".zip") && isSafeHttpsUrl(url, SAFE_DOWNLOAD_HOSTS)
      ? { name, browser_download_url: url, size: Number(asset.size) || 0 }
      : null;
  }).filter((asset): asset is GithubReleaseAsset => Boolean(asset) &&
    isCurrentReleaseAsset(asset!, tag) && looksCompatibleWithReleaseRepository(asset!)) : [];
  if (!tag || !assets.length) return null;
  return {
    tag_name: tag,
    name: text(root.name, 300),
    body: text(root.body, 60_000),
    published_at: text(root.published_at, 80),
    html_url: isSafeHttpsUrl(root.html_url) ? String(root.html_url) : "",
    assets,
  };
}

export function resolveInstallArtifact(plugin: CatalogPlugin, release?: GithubRelease | null): InstallArtifact | null {
  let url = plugin.catalogReleaseUrl;
  let name = plugin.releaseAssetName;
  let version = plugin.version;
  let releaseMetadata: InstallArtifact["release"];

  if (release) {
    const identities = [
      plugin.name,
      plugin.installFolder,
      String(plugin.repository ?? "").split("/").pop() ?? "",
      ...(plugin.aliases ?? []),
    ].map(normalizePluginIdentity).filter(Boolean);
    const preferredName = name.toLocaleLowerCase("en-US");
    const candidates = release.assets.filter((asset) =>
      isCurrentReleaseAsset(asset, release.tag_name) &&
      (Boolean(preferredName) && asset.name.toLocaleLowerCase("en-US") === preferredName ||
        assetMatchesIdentities(asset, identities)),
    );
    const selected = candidates.sort((left, right) => {
      const score = (asset: GithubReleaseAsset) => {
        const assetName = asset.name.toLocaleLowerCase("en-US");
        return Number(Boolean(preferredName) && assetName === preferredName) * 100 +
          Number(isContentAddressedDeckyAsset(asset)) * 80 +
          Number(assetName.includes("installer")) * 40 +
          Number(assetName.includes("decky")) * 30 +
          Math.min(asset.size || 0, 100_000_000) / 100_000_000;
      };
      return score(right) - score(left) || left.name.localeCompare(right.name, "en-US");
    })[0];
    if (!selected) return null;
    url = selected.browser_download_url;
    name = selected.name;
    version = release.tag_name.replace(/^v/i, "");
    releaseMetadata = {
      name: release.name,
      body: release.body,
      publishedAt: release.published_at,
      htmlUrl: release.html_url,
    };
  }
  if (!isSafeHttpsUrl(url, SAFE_DOWNLOAD_HOSTS) || !name.toLocaleLowerCase("en-US").endsWith(".zip")) return null;
  const hash = name.match(/^([a-f0-9]{64})\.zip$/i)?.[1] ?? "";
  return { url, name, version, hash, ...(releaseMetadata ? { release: releaseMetadata } : {}) };
}

export function githubCoverUrl(plugin: CatalogPlugin): string {
  return plugin.coverUrl || `https://opengraph.githubassets.com/playhub-store/${plugin.repository}`;
}
