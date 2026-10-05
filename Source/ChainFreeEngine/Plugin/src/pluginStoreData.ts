import {
  CatalogDocument,
  CatalogPlugin,
  GithubRelease,
  InstallArtifact,
  resolveInstallArtifact,
  PLAYHUB_CATALOG_URL,
  isSafeHttpsUrl,
  isSafeRepository,
  parseCatalog,
  parseGithubRelease,
} from "./pluginStoreCatalog";
import bundledCatalogData from "../../../catalog/plugins.json";
import { descriptionToMarkdown } from "./pluginStoreDescription";
import { pluginCoverSources } from "./pluginStoreAssets";

const bundledCatalog = parseCatalog(bundledCatalogData);

const CATALOG_CACHE_KEY = "playhub.plugin-store.catalog.v1";
const RELEASE_CACHE_PREFIX = "playhub.plugin-store.release.v1:";
const README_CACHE_PREFIX = "playhub.plugin-store.readme.v1:";
const RELEASE_CACHE_MS = 15 * 60 * 1000;
const README_CACHE_MS = 60 * 60 * 1000;

interface TimedCache<T> {
  savedAt: number;
  value: T;
}

export interface ReadmeDocument {
  markdown: string;
  branch: string;
}

export interface PluginMedia {
  kind: "image" | "video";
  url: string;
  alt: string;
}

let catalogPromise: Promise<CatalogDocument> | null = null;
const releasePromises = new Map<string, Promise<GithubRelease | null>>();
const readmePromises = new Map<string, Promise<ReadmeDocument | null>>();

function storage(): Storage | null {
  try { return window.localStorage; } catch { return null; }
}

function readTimed<T>(key: string, ttl: number): T | null {
  try {
    const parsed = JSON.parse(storage()?.getItem(key) ?? "null") as TimedCache<T> | null;
    if (!parsed || Date.now() - Number(parsed.savedAt) > ttl) return null;
    return parsed.value;
  } catch {
    return null;
  }
}

function writeTimed<T>(key: string, value: T): void {
  try { storage()?.setItem(key, JSON.stringify({ savedAt: Date.now(), value })); } catch {}
}

async function fetchText(url: string, timeoutMs = 8_000): Promise<string> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(url, { cache: "no-store", signal: controller.signal });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    return await response.text();
  } finally {
    window.clearTimeout(timer);
  }
}

async function fetchJson(url: string, timeoutMs = 8_000): Promise<unknown> {
  return JSON.parse(await fetchText(url, timeoutMs));
}

async function requestCatalog(): Promise<CatalogDocument> {
  try {
    const raw = await fetchText(PLAYHUB_CATALOG_URL, 10_000);
    const catalog = parseCatalog(raw);
    if (catalog.catalogRevision <= bundledCatalog.catalogRevision) return bundledCatalog;
    writeTimed(CATALOG_CACHE_KEY, raw);
    return catalog;
  } catch (error) {
    const cached = readTimed<string>(CATALOG_CACHE_KEY, Number.MAX_SAFE_INTEGER);
    if (cached) {
      try {
        const catalog = parseCatalog(cached);
        if (catalog.catalogRevision > bundledCatalog.catalogRevision) return catalog;
      } catch {}
    }
    return bundledCatalog;
  }
}

export function preloadPluginStoreCatalog(): Promise<CatalogDocument> {
  catalogPromise ??= requestCatalog().catch((error) => {
    catalogPromise = null;
    throw error;
  });
  return catalogPromise;
}

export function refreshPluginStoreCatalog(): Promise<CatalogDocument> {
  catalogPromise = null;
  return preloadPluginStoreCatalog();
}

export function fetchLatestRelease(repository: string): Promise<GithubRelease | null> {
  if (!isSafeRepository(repository)) return Promise.resolve(null);
  const key = RELEASE_CACHE_PREFIX + repository.toLocaleLowerCase("en-US");
  const cached = readTimed<GithubRelease>(key, RELEASE_CACHE_MS);
  if (cached) return Promise.resolve(cached);
  const existing = releasePromises.get(repository);
  if (existing) return existing;

  const request = fetchJson(`https://api.github.com/repos/${repository}/releases/latest`)
    .then(parseGithubRelease)
    .then((release) => {
      if (release) writeTimed(key, release);
      return release;
    })
    .catch(() => null)
    .finally(() => releasePromises.delete(repository));
  releasePromises.set(repository, request);
  return request;
}

const versionRequests = new Map<string, Promise<InstallArtifact[]>>();
export function fetchPluginVersions(plugin: CatalogPlugin): Promise<InstallArtifact[]> {
  const key = `${plugin.catalogSource}:${plugin.repository}`;
  const cached = readTimed<InstallArtifact[]>(`playhub.versions:${key}`, RELEASE_CACHE_MS);
  if (cached?.length) return Promise.resolve(cached);
  const existing = versionRequests.get(key);
  if (existing) return existing;
  const request = requestPluginVersions(plugin).then((items) => {
    const unique = [...new Map(items.map((item) => [item.version, item])).values()];
    if (unique.length) writeTimed(`playhub.versions:${key}`, unique);
    return unique;
  }).finally(() => versionRequests.delete(key));
  versionRequests.set(key, request);
  return request;
}

async function publicGithubVersions(plugin: CatalogPlugin): Promise<InstallArtifact[]> {
  const base = `https://github.com/${plugin.repository}`;
  const feed = new DOMParser().parseFromString(await fetchText(`${base}/releases.atom`), "application/xml");
  const artifacts: InstallArtifact[] = [];
  for (const entry of Array.from(feed.querySelectorAll("entry"))) {
    const href = entry.querySelector("link")?.getAttribute("href") || "";
    const tag = decodeURIComponent(href.split("/releases/tag/")[1] || "");
    if (!tag) continue;
    const html = new DOMParser().parseFromString(await fetchText(`${base}/releases/expanded_assets/${encodeURIComponent(tag)}`), "text/html");
    const assets = Array.from(html.querySelectorAll("a[href]")).flatMap((link) => {
      const url = new URL(link.getAttribute("href")!, base).href;
      if (!url.startsWith(`${base}/releases/download/`) || !url.toLowerCase().endsWith(".zip")) return [];
      return [{ name: decodeURIComponent(url.split("/").pop()!), browser_download_url: url, size: 0 }];
    });
    const release = parseGithubRelease({ tag_name: tag, name: entry.querySelector("title")?.textContent,
      published_at: entry.querySelector("updated")?.textContent, html_url: href, assets });
    const artifact = release && resolveInstallArtifact(plugin, release);
    if (artifact) artifacts.push(artifact);
  }
  return artifacts;
}

async function requestPluginVersions(plugin: CatalogPlugin): Promise<InstallArtifact[]> {
  if (plugin.catalogSource === "decky-store") {
    const entries = await fetchJson("https://plugins.deckbrew.xyz/plugins");
    if (!Array.isArray(entries)) throw new Error("Invalid Decky catalog.");
    const entry = entries.find((item: any) => plugin.catalogPluginId > 0
      ? item.id === plugin.catalogPluginId
      : String(item.name).toLowerCase() === plugin.name.toLowerCase());
    return (Array.isArray(entry?.versions) ? entry.versions : []).flatMap((version: any) => {
      if (!/^[a-f0-9]{64}$/i.test(String(version.hash)) || typeof version.name !== "string") return [];
      const artifact = resolveInstallArtifact({ ...plugin, version: version.name,
        releaseAssetName: `${version.hash}.zip`,
        catalogReleaseUrl: `https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/${version.hash}.zip` });
      return artifact ? [artifact] : [];
    });
  }
  if (!isSafeRepository(plugin.repository)) return [];
  const artifacts: InstallArtifact[] = [];
  try {
  for (let page = 1; page <= 10; page++) {
    const releases = await fetchJson(`https://api.github.com/repos/${plugin.repository}/releases?per_page=100&page=${page}`);
    if (!Array.isArray(releases)) throw new Error("Invalid release list.");
    for (const raw of releases) {
      if (raw.draft) continue;
      const release = parseGithubRelease(raw);
      const artifact = release && resolveInstallArtifact(plugin, release);
      if (artifact) artifacts.push(artifact);
    }
    if (releases.length < 100) break;
  }
  } catch {
    return publicGithubVersions(plugin);
  }
  return artifacts;
}

export function fetchPluginReadme(repository: string): Promise<ReadmeDocument | null> {
  if (!isSafeRepository(repository)) return Promise.resolve(null);
  const key = README_CACHE_PREFIX + repository.toLocaleLowerCase("en-US");
  const cached = readTimed<ReadmeDocument>(key, README_CACHE_MS);
  if (cached) return Promise.resolve(cached);
  const existing = readmePromises.get(repository);
  if (existing) return existing;

  const request = (async () => {
    for (const branch of ["main", "master"]) {
      try {
        const markdown = await fetchText(`https://raw.githubusercontent.com/${repository}/${branch}/README.md`);
        const result = { markdown, branch };
        writeTimed(key, result);
        return result;
      } catch {}
    }
    return null;
  })().finally(() => readmePromises.delete(repository));
  readmePromises.set(repository, request);
  return request;
}

function cleanMarkdownTarget(value: string): string {
  const trimmed = value.trim();
  if (trimmed.startsWith("<") && trimmed.endsWith(">")) return trimmed.slice(1, -1).trim();
  const titleStart = trimmed.search(/\s+["']/);
  return titleStart > 0 ? trimmed.slice(0, titleStart).trim() : trimmed;
}

export function resolveRepositoryMediaUrl(repository: string, branch: string, value: string): string {
  const target = cleanMarkdownTarget(value);
  if (!isSafeRepository(repository) || repository.split("/").some((part) => part === "." || part === "..") ||
      !/^[a-zA-Z0-9._-]+$/.test(branch) || branch === "." || branch === "..") return "";
  if (!target || target.startsWith("data:") || target.startsWith("javascript:")) return "";
  if (/^https:\/\//i.test(target)) {
    try {
      const parsed = new URL(target);
      const blobMatch = parsed.hostname === "github.com" && parsed.pathname.match(/^\/([^/]+)\/([^/]+)\/blob\/([^/]+)\/(.+)$/);
      if (blobMatch) return `https://raw.githubusercontent.com/${blobMatch[1]}/${blobMatch[2]}/${blobMatch[3]}/${blobMatch[4]}`;
    } catch {}
    return isSafeHttpsUrl(target) ? target : "";
  }
  if (/^[a-z][a-z\d+.-]*:/i.test(target) || target.startsWith("//") || target.includes("\\")) return "";
  const path = target.replace(/^\.\//, "").replace(/^\//, "");
  if (!path || path.includes("..")) return "";
  return `https://raw.githubusercontent.com/${repository}/${branch}/${path}`;
}

function probableMediaKind(url: string): "image" | "video" | null {
  let pathname = "";
  let host = "";
  try {
    const parsed = new URL(url);
    pathname = parsed.pathname.toLocaleLowerCase("en-US");
    host = parsed.hostname.toLocaleLowerCase("en-US");
  } catch { return null; }
  if (/\.(mp4|webm|mov)$/.test(pathname)) return "video";
  if (/\.(png|jpe?g|webp|gif|avif|bmp)$/.test(pathname) ||
      host === "user-images.githubusercontent.com" ||
      (host === "github.com" && pathname.includes("/user-attachments/assets/"))) return "image";
  return null;
}

export function extractMarkdownMedia(markdown: string, repository: string, branch: string, limit = 8): PluginMedia[] {
  const candidates: Array<{ alt: string; target: string }> = [];
  const imagePattern = /!\[([^\]]*)\]\(([^)]+)\)/g;
  const htmlPattern = /<(?:img|video)[^>]+(?:src|poster)=["']([^"']+)["'][^>]*>/gi;
  let match: RegExpExecArray | null;
  while ((match = imagePattern.exec(markdown))) candidates.push({ alt: match[1], target: match[2] });
  while ((match = htmlPattern.exec(markdown))) candidates.push({ alt: "", target: match[1] });
  const directPattern = /https:\/\/[^\s)<>"']+\.(?:mp4|webm|mov)(?:\?[^\s)<>"']*)?/gi;
  while ((match = directPattern.exec(markdown))) candidates.push({ alt: "", target: match[0] });

  const seen = new Set<string>();
  const media: PluginMedia[] = [];
  for (const candidate of candidates) {
    const url = resolveRepositoryMediaUrl(repository, branch, candidate.target);
    const kind = probableMediaKind(url);
    if (!url || !kind || seen.has(url)) continue;
    seen.add(url);
    media.push({ kind, url, alt: candidate.alt.slice(0, 200) });
    if (media.length >= limit) break;
  }
  return media;
}

export function stripMarkdownMedia(markdown: string): string {
  return markdown
    .replace(/!\[[^\]]*\]\([^)]+\)/g, "")
    .replace(/<(?:img|video)[^>]*>.*?<\/(?:video)>/gis, "")
    .replace(/<(?:img|video)[^>]*\/?\s*>/gi, "")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}

export async function loadPluginDetails(plugin: CatalogPlugin): Promise<{
  release: GithubRelease | null;
  readme: ReadmeDocument | null;
  media: PluginMedia[];
  descriptionMarkdown: string;
  coverSources: string[];
}> {
  const [release, readme] = await Promise.all([
    fetchLatestRelease(plugin.repository),
    fetchPluginReadme(plugin.repository),
  ]);
  const media = readme ? extractMarkdownMedia(readme.markdown, plugin.repository, readme.branch) : [];
  return {
    release,
    readme,
    media,
    descriptionMarkdown: descriptionToMarkdown(
      stripMarkdownMedia(readme?.markdown || plugin.longDescription || plugin.shortDescription),
      `${plugin.repositoryUrl}/blob/${readme?.branch ?? "main"}/`,
    ),
    coverSources: pluginCoverSources(plugin.repository, plugin.coverUrl, media.filter((item) => item.kind === "image").map((item) => item.url)),
  };
}
