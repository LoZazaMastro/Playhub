import { DFL, SP_REACT as React, toaster } from "./decky";
import {
  CatalogPlugin,
  GithubRelease,
  InstalledPlugin,
  InstallArtifact,
  PluginSort,
  PluginSource,
  compareVersions,
  featuredPlugins,
  filterPlugins,
  findInstalledPlugin,
  githubCoverUrl,
  isIntegratedPlayhubPlugin,
  normalizePluginIdentity,
  pluginHasUpdate,
  pluginCategory,
  pluginInCategory,
  resolveInstallArtifact,
} from "./pluginStoreCatalog";
import {
  PluginMedia,
  fetchLatestRelease,
  fetchPluginVersions,
  loadPluginDetails,
  preloadPluginStoreCatalog,
  refreshPluginStoreCatalog,
  stripMarkdownMedia,
} from "./pluginStoreData";
import {
  PluginOperationProgress,
  readInstalledPlugins,
  requestDeckyInstall,
  requestDeckyUninstall,
  subscribeInstalledPlugins,
  subscribePluginProgress,
} from "./pluginStoreDecky";
import {
  PluginStoreCopy,
  StoreLocale,
  getPluginStoreCopy,
  localizePluginCategory,
  normalizeStoreLocale,
} from "./pluginStoreLocale";
import { bundledPluginCover, pluginCoverSources, SOURCE_BADGES } from "./pluginStoreAssets";
import { openHistoryImage } from "./historyInteractions";
import { STORE_LAYOUT_PROFILES, storeLayoutForWidth } from "./pluginStoreLayout";
import { descriptionToMarkdown } from "./pluginStoreDescription";
import {
  TbArrowLeft,
  TbArrowsSort,
  TbBrandGithub,
  TbChevronLeft,
  TbChevronRight,
  TbDownload,
  TbRefresh,
  TbTrash,
  TbHistory,
} from "react-icons/tb";

const { useCallback, useEffect, useMemo, useRef, useState } = React;
const {
  DialogButton,
  ConfirmModal,
  Focusable,
  GamepadButton,
  ModalRoot,
  Navigation,
  SteamSpinner,
  Tabs,
  TextField,
  showModal,
} = DFL as any;

export const PLUGIN_STORE_ROUTE = "/playhub/plugin-store";
type StoreTab = "discover" | "search" | "manage";
type SourceFilter = PluginSource | "all";

export { STORE_LAYOUT_PROFILES, storeLayoutForWidth };

export const STORE_STYLE = `
  .ph-store-page { box-sizing:border-box; width:100%; height:100%; min-width:0; color:#f5f5f5; background:rgba(8,8,10,.76); }
  .ph-store-tabs { box-sizing:border-box; height:100%; padding-top:40px; background:rgba(0,0,0,.16); }
  .ph-store-tab-body { box-sizing:border-box; width:100%; min-width:0; padding:22px 4.2vw 72px; }
  .ph-store-flow { min-width:0; }
  .ph-store-page-header { display:grid; grid-template-columns:minmax(0,1fr) auto; align-items:end; gap:18px; min-width:0; margin:0 0 16px; }
  .ph-store-page-title { min-width:0; margin:0; font-size:30px; line-height:1.15; font-weight:700; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .ph-store-sort-label { display:inline-flex; align-items:center; justify-content:flex-end; gap:8px; max-width:260px; min-width:0; color:rgba(255,255,255,.72); font-size:15px; white-space:nowrap; }
  .ph-store-sort-label span { overflow:hidden; text-overflow:ellipsis; }
  .ph-store-toolbar { display:flex; align-items:center; justify-content:flex-start; gap:12px; min-width:0; margin-bottom:18px; }
  .ph-store-filter, .ph-store-actions { display:flex; align-items:center; gap:8px; min-width:0; }
  .ph-store-filter { flex-wrap:wrap; }
  .ph-store-filter button { width:auto !important; min-width:46px; min-height:42px; padding:7px 13px !important; }
  .ph-store-filter button.ph-selected { background:rgba(255,255,255,.92) !important; color:#202124 !important; }
  .ph-store-filter button:hover, .ph-store-filter button.gpfocus { background:#fff !important; color:#202124 !important; outline:3px solid #fff; outline-offset:3px; box-shadow:0 0 0 2px #171717 !important; }
  .ph-store-filter button.ph-selected { box-shadow:inset 0 -4px 0 #657783; }
  .ph-store-bulk-action { display:grid !important; width:46px !important; min-width:46px !important; height:42px; min-height:42px; place-items:center; padding:0 !important; }
  .ph-store-bulk-action svg { width:22px; height:22px; }
  .ph-store-section { min-width:0; margin:0 0 30px; }
  .ph-store-section-link { display:inline-flex; width:auto; margin:0 0 16px; padding:8px 12px; border:0; border-radius:6px; color:inherit; background:transparent; font:inherit; transition:background-color 160ms ease,box-shadow 160ms ease,color 160ms ease; }
  .ph-store-section-link h2 { margin:0; font-size:24px; line-height:1.2; }
  .ph-store-section-link.gpfocus, .ph-store-section-link:hover { color:#fff; background:rgba(255,255,255,.12); box-shadow:inset 0 0 0 2px rgba(255,255,255,.75); }
  .ph-store-list { display:grid; grid-template-columns:minmax(0,1fr); gap:9px; min-width:0; }
  .ph-store-row { position:relative; display:grid; grid-template-columns:190px minmax(0,1fr) auto; min-height:112px; min-width:0; overflow:hidden; border-radius:6px; background:rgba(255,255,255,.08); box-shadow:inset 0 0 0 1px rgba(255,255,255,.065); transition:background-color 130ms ease, box-shadow 130ms ease; }
  .ph-store-row.ph-store-row-openable.gpfocus, .ph-store-row.ph-store-row-openable:hover { background:rgba(255,255,255,.15); box-shadow:inset 0 0 0 3px rgba(255,255,255,.88); }
  .ph-store-row-media { position:relative; min-width:0; overflow:hidden; background:rgba(0,0,0,.28); }
  .ph-store-image { display:block; width:100%; height:100%; object-fit:cover; object-position:center; }
  .ph-store-image-placeholder { min-height:100%; background:rgba(255,255,255,.035); }
  .ph-store-row-copy { display:flex; flex-direction:column; justify-content:center; min-width:0; padding:13px 16px; }
  .ph-store-row-title { margin:0 0 5px; font-size:19px; line-height:1.2; font-weight:700; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .ph-store-row-description { display:-webkit-box; min-height:2.7em; margin:0; overflow:hidden; color:rgba(255,255,255,.67); font-size:14px; line-height:1.34; -webkit-line-clamp:2; -webkit-box-orient:vertical; }
  .ph-store-row-trailing { display:flex; align-items:center; justify-content:flex-end; gap:9px; min-width:54px; padding:12px 14px 12px 6px; }
  .ph-store-source-badge { display:inline-flex; height:30px; align-items:center; gap:7px; max-width:142px; padding:4px 8px; border-radius:5px; color:rgba(255,255,255,.92); background:rgba(255,255,255,.12); font-size:12px; font-weight:600; line-height:1; white-space:nowrap; }
  .ph-store-source-badge img, .ph-store-source-badge svg { width:20px; height:20px; flex:0 0 20px; object-fit:contain; }
  .ph-store-source-badge[data-source="playhub"] { background:rgba(255,203,0,.15); }
  .ph-store-source-badge[data-source="decky-store"] { background:rgba(71,205,211,.14); }
  .ph-store-source-badge[data-source="outside-store"] { background:rgba(151,92,220,.18); }
  .ph-store-update-marker { display:grid; width:27px; height:27px; place-items:center; border-radius:50%; color:#161616; background:#fff; }
  .ph-store-update-marker svg { width:16px; height:16px; }
  .ph-store-action { position:relative; display:grid !important; width:46px !important; min-width:46px !important; height:42px; min-height:42px; place-items:center; overflow:hidden; padding:0 !important; }
  .ph-store-action svg { width:22px; height:22px; }
  .ph-store-action-spinner { width:22px; height:22px; overflow:hidden; }
  .ph-store-action-spinner > * { transform:scale(.54); transform-origin:top left; }
  .ph-store-progress { position:absolute; right:5px; bottom:3px; left:5px; display:block; height:3px; overflow:hidden; border-radius:2px; background:rgba(255,255,255,.17); }
  .ph-store-progress span { display:block; height:100%; background:#fff; transition:width 120ms linear; }
  .ph-store-empty { display:grid; min-height:210px; place-items:center; color:rgba(255,255,255,.68); text-align:center; }
  .ph-store-featured-shell { position:relative; min-width:0; }
  .ph-store-featured-slide { position:relative; display:grid; grid-template-columns:minmax(0,1.62fr) minmax(300px,.82fr); width:100%; height:clamp(240px,40vh,360px); min-height:0; overflow:hidden; border-radius:7px; background:rgba(255,255,255,.085); box-shadow:inset 0 0 0 1px rgba(255,255,255,.08); transition:background-color 140ms ease, box-shadow 140ms ease; }
  .ph-store-featured-slide.gpfocus, .ph-store-featured-slide:hover { background:rgba(255,255,255,.14); box-shadow:inset 0 0 0 3px rgba(255,255,255,.9); }
  .ph-store-featured-media { position:relative; min-width:0; min-height:0; height:100%; overflow:hidden; background:rgba(0,0,0,.32); }
  .ph-store-featured-media .ph-store-image { position:absolute; inset:0; }
  .ph-store-featured-copy { display:grid; grid-template-rows:38px 66px minmax(0,1fr) 22px; gap:10px; min-width:0; min-height:0; padding:24px 28px; background:linear-gradient(130deg,rgba(35,35,38,.96),rgba(23,23,26,.88)); }
  .ph-store-featured-copy h2 { display:-webkit-box; margin:0; font-size:29px; line-height:1.12; overflow:hidden; -webkit-line-clamp:2; -webkit-box-orient:vertical; }
  .ph-store-featured-copy p { display:-webkit-box; margin:0; overflow:hidden; color:rgba(255,255,255,.74); font-size:16px; line-height:1.42; -webkit-line-clamp:4; -webkit-box-orient:vertical; }
  .ph-store-featured-meta { display:flex; align-items:center; gap:12px; margin:0; font-variant-numeric:tabular-nums; }
  .ph-store-featured-count { color:rgba(255,255,255,.55); font-size:13px; font-variant-numeric:tabular-nums; }
  .ph-store-featured-arrow { position:absolute; z-index:3; top:50%; display:grid; width:42px; height:52px; margin-top:-26px; place-items:center; border-radius:5px; color:#fff; background:rgba(0,0,0,.48); pointer-events:none; }
  .ph-store-featured-arrow svg { width:25px; height:25px; }
  .ph-store-featured-arrow-left { left:12px; }
  .ph-store-featured-arrow-right { right:12px; }
  .ph-store-featured-dots { display:flex; justify-content:center; gap:7px; margin-top:10px; }
  .ph-store-featured-dot { width:7px; height:7px; border-radius:50%; background:rgba(255,255,255,.28); }
  .ph-store-featured-dot.ph-active { background:#fff; }
  .ph-store-search-shell { margin:0 0 16px; }
  .ph-store-search-shell input { min-height:40px !important; height:40px !important; padding-top:5px !important; padding-bottom:5px !important; }
  .ph-store-search-shell [class*="TextField"] { min-height:40px !important; }
  .ph-store-detail { min-width:0; }
  .ph-store-detail-heading { display:grid; grid-template-columns:minmax(0,1.62fr) minmax(300px,.82fr); gap:26px; margin-bottom:20px; }
  .ph-store-detail-summary { display:flex; flex-direction:column; justify-content:center; min-width:0; gap:14px; }
  .ph-store-back { display:grid !important; width:46px !important; min-width:46px !important; height:44px; place-items:center; padding:0 !important; }
  .ph-store-back svg { width:23px; height:23px; }
  .ph-store-detail-title { min-width:0; margin:0; font-size:30px; line-height:1.16; overflow-wrap:anywhere; }
  .ph-store-detail-status { display:flex; flex-direction:column; gap:6px; color:rgba(255,255,255,.68); font-size:14px; }
  .ph-store-detail-hero { display:flex; width:100%; height:clamp(240px,40vh,360px); min-height:0; align-items:center; justify-content:center; overflow:hidden; border-radius:6px; background:rgba(0,0,0,.3); }
  .ph-store-detail-hero .ph-store-image { object-fit:cover; object-position:center; }
  .ph-store-detail-hero:hover, .ph-store-detail-hero.gpfocus { outline:3px solid #fff; outline-offset:3px; }
  .ph-store-actions { margin:11px 0 13px; }
  .ph-store-media-section h2 { margin:17px 0 9px; font-size:21px; }
  .ph-store-media-strip { display:grid; grid-auto-flow:column; grid-auto-columns:minmax(250px,34%); gap:10px; overflow-x:auto; overscroll-behavior-x:contain; padding:0 0 10px; }
  .ph-store-media-item { display:flex; height:145px; align-items:center; justify-content:center; overflow:hidden; border-radius:5px; background:rgba(0,0,0,.28); }
  .ph-store-media-item img, .ph-store-media-item video { width:100%; height:100%; object-fit:contain; object-position:center; }
  .ph-store-media-item { padding:0; border:0; width:100%; transition:box-shadow 140ms ease; }
  .ph-store-media-item.gpfocus, .ph-store-media-item:hover { box-shadow:inset 0 0 0 3px #fff; outline:2px solid rgba(255,255,255,.8); outline-offset:2px; }
  .ph-store-media-modal { width:85vw; height:80vh; display:grid; grid-template-rows:auto minmax(0,1fr); gap:12px; }
  .ph-store-media-modal img, .ph-store-media-modal video { width:100%; height:100%; min-height:0; object-fit:contain; }
  .ph-store-filter .ph-store-source-badge { height:26px; padding:0; background:transparent; color:inherit; }
  .ph-store-reader { max-height:38vh; overflow-y:auto; overscroll-behavior:contain; margin-top:14px; padding:16px 18px; border-radius:6px; background:rgba(255,255,255,.07); box-shadow:inset 0 0 0 1px rgba(255,255,255,.08); transition:background-color 140ms ease, box-shadow 140ms ease; }
  .ph-store-reader.gpfocus, .ph-store-reader:hover { background:rgba(255,255,255,.105); box-shadow:inset 0 0 0 3px rgba(255,255,255,.62); }
  .ph-store-reader.ph-reading { background:rgba(255,255,255,.12); box-shadow:inset 0 0 0 3px rgba(255,255,255,.96); }
  .ph-store-reader h2, .ph-store-reader h3, .ph-store-reader h4 { margin:18px 0 8px; }
  .ph-store-reader h2:first-child, .ph-store-reader h3:first-child { margin-top:0; }
  .ph-store-reader p, .ph-store-reader li, .ph-store-reader blockquote { color:rgba(255,255,255,.79); font-size:16px; line-height:1.48; }
  .ph-store-reader p { margin:0 0 11px; }
  .ph-store-reader ul, .ph-store-reader ol { margin:0 0 13px; padding-left:24px; }
  .ph-store-reader blockquote { margin:10px 0; padding:8px 12px; border-left:3px solid rgba(255,255,255,.5); background:rgba(255,255,255,.05); }
  .ph-store-reader a { color:#fff; text-decoration:underline; }
  .ph-store-sort-modal { min-width:min(520px,80vw); padding:8px 4px 4px; }
  .ph-store-sort-modal h2 { margin:0 0 16px; }
  .ph-store-sort-options { display:grid; gap:8px; }
  .ph-store-sort-options button { min-height:48px; }
  .ph-store-sort-options button.ph-selected { background:rgba(255,255,255,.92) !important; color:#202124 !important; }
  @media (max-width:700px) {
    .ph-store-tab-body { padding:16px 18px 62px; }
    .ph-store-page-header { align-items:center; gap:10px; }
    .ph-store-page-title, .ph-store-detail-title { font-size:24px; }
    .ph-store-sort-label { max-width:170px; font-size:13px; }
    .ph-store-filter { gap:6px; }
    .ph-store-filter button { min-height:39px; padding:6px 9px !important; font-size:12px !important; }
    .ph-store-row { grid-template-columns:130px minmax(0,1fr) auto; min-height:98px; }
    .ph-store-row-copy { padding:10px 11px; }
    .ph-store-row-title { font-size:16px; }
    .ph-store-row-description { font-size:12px; }
    .ph-store-row-trailing { gap:6px; padding-right:9px; }
    .ph-store-source-badge { max-width:42px; padding:4px 6px; }
    .ph-store-source-badge span { display:none; }
    .ph-store-featured-slide { grid-template-columns:minmax(0,1.35fr) minmax(215px,.8fr); }
    .ph-store-featured-copy { grid-template-rows:34px 50px minmax(0,1fr) 20px; gap:8px; padding:18px 20px; }
    .ph-store-featured-copy h2 { font-size:22px; }
    .ph-store-featured-copy p { font-size:13px; -webkit-line-clamp:3; }
    .ph-store-detail-heading { grid-template-columns:minmax(0,1.35fr) minmax(215px,.8fr); gap:16px; }
    .ph-store-media-strip { grid-auto-columns:minmax(210px,76%); }
    .ph-store-reader { max-height:42vh; padding:13px; }
  }
`;

function stopEvent(event?: any): void {
  try { event?.preventDefault?.(); } catch {}
  try { event?.stopPropagation?.(); } catch {}
  try { event?.stopImmediatePropagation?.(); } catch {}
}

let lastStoreCancelAt = -Infinity;
const tabBackHandlers = new Map<StoreTab, () => void>();

function useTabBack(tab: StoreTab, action: () => void) {
  useEffect(() => {
    tabBackHandlers.set(tab, action);
    return () => { if (tabBackHandlers.get(tab) === action) tabBackHandlers.delete(tab); };
  }, [tab, action]);
}

function useGuardedCancel(action: () => void) {
  return useCallback((event?: any) => {
    stopEvent(event);
    const now = performance.now();
    if (now - lastStoreCancelAt < 420) return true;
    lastStoreCancelAt = now;
    action();
    return true;
  }, [action]);
}

function sourceLabel(source: PluginSource): string {
  if (source === "playhub") return "Playhub";
  if (source === "decky-store") return "Decky Store";
  return "GitHub";
}

function SourceBadge({ source }: { source: PluginSource }) {
  const image = source === "playhub"
    ? SOURCE_BADGES.playhub
    : source === "decky-store"
      ? SOURCE_BADGES.deckyStore
      : "";
  return (
    <span className="ph-store-source-badge" data-source={source}>
      {image ? <img src={image} alt="" /> : <TbBrandGithub />}
      <span>{sourceLabel(source)}</span>
    </span>
  );
}

function useStoreLocale(): StoreLocale {
  const [locale, setLocale] = useState<StoreLocale>(() => normalizeStoreLocale(
    document.documentElement.lang || navigator.language,
  ));
  useEffect(() => {
    let alive = true;
    void (window as any).SteamClient?.Settings?.GetCurrentLanguage?.().then((value: unknown) => {
      if (alive) setLocale(normalizeStoreLocale(value));
    }).catch(() => {});
    return () => { alive = false; };
  }, []);
  return locale;
}

function InlineMarkdown({ text }: { text: string }) {
  const parts: any[] = [];
  const pattern = /(\*\*[^*]+\*\*|__[^_]+__|`[^`]+`|\[[^\]]+\]\(https:\/\/[^)]+\))/g;
  let cursor = 0;
  let match: RegExpExecArray | null;
  while ((match = pattern.exec(text))) {
    if (match.index > cursor) parts.push(text.slice(cursor, match.index));
    const token = match[0];
    if (token.startsWith("**") || token.startsWith("__")) {
      parts.push(<strong key={match.index}>{token.slice(2, -2)}</strong>);
    } else if (token.startsWith("`")) {
      parts.push(<code key={match.index}>{token.slice(1, -1)}</code>);
    } else {
      const link = token.match(/^\[([^\]]+)\]\((https:\/\/[^)]+)\)$/);
      parts.push(link ? <span key={match.index}>{link[1]}</span> : token);
    }
    cursor = match.index + token.length;
  }
  if (cursor < text.length) parts.push(text.slice(cursor));
  return <>{parts}</>;
}

function MarkdownBlocks({ markdown }: { markdown: string }) {
  const blocks: any[] = [];
  const lines = markdown.replace(/\r/g, "").split("\n");
  let paragraph: string[] = [];
  let list: Array<{ ordered: boolean; text: string }> = [];
  const flushParagraph = () => {
    if (!paragraph.length) return;
    const content = paragraph.join(" ").trim();
    if (content) blocks.push(<p key={`p-${blocks.length}`}><InlineMarkdown text={content} /></p>);
    paragraph = [];
  };
  const flushList = () => {
    if (!list.length) return;
    const ordered = list[0].ordered;
    const children = list.map((item, index) => <li key={index}><InlineMarkdown text={item.text} /></li>);
    blocks.push(ordered
      ? <ol key={`ol-${blocks.length}`}>{children}</ol>
      : <ul key={`ul-${blocks.length}`}>{children}</ul>);
    list = [];
  };
  for (const raw of lines) {
    const line = raw.trim();
    if (!line) { flushParagraph(); flushList(); continue; }
    const heading = line.match(/^(#{1,4})\s+(.+)$/);
    const bullet = line.match(/^[-*]\s+(.+)$/);
    const numbered = line.match(/^\d+[.)]\s+(.+)$/);
    const quote = line.match(/^>\s?(.*)$/);
    if (heading) {
      flushParagraph(); flushList();
      const level = Math.min(4, heading[1].length + 1);
      const Tag = `h${level}` as any;
      blocks.push(<Tag key={`h-${blocks.length}`}><InlineMarkdown text={heading[2]} /></Tag>);
    } else if (bullet || numbered) {
      flushParagraph();
      list.push({ ordered: Boolean(numbered), text: (bullet ?? numbered)![1] });
    } else if (quote) {
      flushParagraph(); flushList();
      blocks.push(<blockquote key={`q-${blocks.length}`}><InlineMarkdown text={quote[1]} /></blockquote>);
    } else {
      flushList();
      paragraph.push(line);
    }
  }
  flushParagraph();
  flushList();
  return <>{blocks}</>;
}

function ScrollReader({ children, copy, onBack }: { children: any; copy: PluginStoreCopy; onBack: () => void }) {
  const ref = useRef<HTMLDivElement | null>(null);
  const [reading, setReading] = useState(false);
  const cancelAction = useCallback(() => {
    if (reading) { setReading(false); return; }
    onBack();
  }, [reading, onBack]);
  const cancel = useGuardedCancel(cancelAction);
  return (
    <Focusable
      ref={ref}
      className={`ph-store-reader${reading ? " ph-reading" : ""}`}
      noFocusRing
      flow-children="none"
      onActivate={(event: any) => { stopEvent(event); setReading(true); }}
      onOKButton={(event: any) => { stopEvent(event); setReading(true); }}
      onCancel={cancel}
      onCancelButton={cancel}
      onOKActionDescription={reading ? undefined : copy.scroll}
      onCancelActionDescription={reading ? copy.stopReading : copy.back}
      actionDescriptionMap={reading ? {
        [GamepadButton.DIR_UP]: copy.scroll,
        [GamepadButton.DIR_DOWN]: copy.scroll,
      } : undefined}
      onButtonDown={(event: any) => {
        if (!reading) return;
        const button = Number(event?.detail?.button);
        if (button !== GamepadButton.DIR_UP && button !== GamepadButton.DIR_DOWN) return;
        stopEvent(event);
        ref.current?.scrollBy({ top: button === GamepadButton.DIR_UP ? -104 : 104, behavior: "smooth" });
      }}
    >
      {children}
    </Focusable>
  );
}

function MediaViewer({ item, copy, closeModal }: { item: PluginMedia; copy: PluginStoreCopy; closeModal?: () => void }) {
  const video = useRef<HTMLVideoElement | null>(null);
  const close = useGuardedCancel(() => closeModal?.());
  return (
    <ModalRoot closeModal={closeModal} onCancel={() => closeModal?.()} style={{ width: "90vw", maxWidth: "1200px", boxSizing: "border-box" }}>
      <Focusable className="ph-store-media-modal" style={{ width: "100%", maxWidth: "100%", height: "78vh", minWidth: 0, overflow: "hidden", display: "grid", gridTemplateRows: "minmax(0,1fr)" }} flow-children="column" onCancel={close} onCancelButton={close} onMenuButton={stopEvent} onMenuActionDescription="">
        <Focusable style={{ width: "100%", height: "100%", minWidth: 0, minHeight: 0, overflow: "hidden", display: "flex", alignItems: "center", justifyContent: "center" }} flow-children="none" onOKActionDescription={copy.select} onOKButton={(event: any) => {
          stopEvent(event);
          if (video.current?.paused) void video.current.play().catch(() => {});
          else video.current?.pause();
        }}>
          {item.kind === "video"
            ? <video style={{ width: "100%", height: "100%", maxWidth: "100%", maxHeight: "100%", objectFit: "contain" }} ref={video} src={item.url} controls autoPlay preload="metadata" />
            : <img style={{ width: "100%", height: "100%", maxWidth: "100%", maxHeight: "100%", objectFit: "contain" }} src={item.url} alt={item.alt || copy.media} />}
        </Focusable>
      </Focusable>
    </ModalRoot>
  );
}

function StoreMediaControls() {
  return <Focusable autoFocus flow-children="none" noFocusRing
    onMenuButton={(event: any) => { stopEvent(event); return true; }} onMenuActionDescription="">
    <style>{`
      [class*="ArtworkModal"]:has(.ph-store-native-media-controls) [class*="ModalArtCloseButton"],
      [class*="ArtworkModal"]:has(.ph-store-native-media-controls) [class*="ScrollForMore"] { display:none !important; }
    `}</style>
    <span className="ph-store-native-media-controls" />
  </Focusable>;
}

function openStoreMedia(item: PluginMedia, copy: PluginStoreCopy): void {
  if (item.kind === "image") {
    openHistoryImage(item.url, item.alt || copy.media, document.documentElement.lang || navigator.language, window);
    return;
  }
  showModal(<MediaViewer item={item} copy={copy} />, window, { bNeverPopOut: true });
}

function SafeMedia({ item, copy }: { item: PluginMedia; copy: PluginStoreCopy }) {
  const [visible, setVisible] = useState(true);
  if (!visible) return null;
  return (
    <Focusable className="ph-store-media-item" flow-children="none" onOKActionDescription={copy.select}
      onActivate={(event: any) => {
        stopEvent(event);
        openStoreMedia(item, copy);
      }}>
      {item.kind === "video"
        ? <video src={item.url} muted preload="metadata" onError={() => setVisible(false)} />
        : <img src={item.url} alt={item.alt} loading="lazy" decoding="async" onError={() => setVisible(false)} />}
    </Focusable>
  );
}

function PluginImage({ plugin, className = "ph-store-image", fallbackSources, onImageLoaded }: { plugin: CatalogPlugin; className?: string; fallbackSources?: string[]; onImageLoaded?: (url: string) => void }) {
  const sources = useMemo(() => plugin.catalogStatus === "installed-only" ? [] :
    pluginCoverSources(plugin.repository, plugin.coverUrl, fallbackSources),
    [plugin.repository, plugin.coverUrl, plugin.catalogStatus, fallbackSources]);
  const [index, setIndex] = useState(0);
  useEffect(() => setIndex(0), [plugin.repository, plugin.coverUrl]);
  if (!sources[index]) return <div className={className + " ph-store-image-placeholder"} aria-hidden="true" />;
  return (
    <img
      className={className}
      src={sources[index]}
      alt=""
      loading="lazy"
      decoding="async"
      onLoad={() => onImageLoaded?.(sources[index])}
      onError={() => setIndex((value: number) => value + 1)}
    />
  );
}

interface SharedStoreProps {
  plugins: CatalogPlugin[];
  installed: InstalledPlugin[];
  copy: PluginStoreCopy;
  locale: StoreLocale;
  progress: PluginOperationProgress | null;
}

function confirmStoreOperation(title: string, description: string, action: string, copy: PluginStoreCopy): Promise<boolean> {
  return new Promise((resolve) => {
    showModal(
      <ConfirmModal
        strTitle={title}
        strDescription={description}
        strOKButtonText={action}
        strCancelButtonText={copy.back}
        onOK={() => resolve(true)}
        onCancel={() => resolve(false)}
        onEscKeypress={() => resolve(false)}
      />,
      window,
      { bNeverPopOut: true },
    );
  });
}

async function installOrUpdatePlugin(
  plugin: CatalogPlugin,
  shared: SharedStoreProps,
): Promise<void> {
  const release = plugin.catalogSource === "decky-store" ? null : await fetchLatestRelease(plugin.repository);
  const artifact = resolveInstallArtifact(plugin, release);
  if (!artifact) throw new Error(shared.copy.releaseUnavailable);
  await requestDeckyInstall(plugin, artifact, shared.installed);
}

function pluginOperationActive(
  plugin: CatalogPlugin,
  installed: InstalledPlugin | undefined,
  progress: PluginOperationProgress | null,
): boolean {
  if (!progress?.active) return false;
  const active = progress.plugin.toLocaleLowerCase("en-US");
  return [plugin.name, plugin.installFolder, installed?.name]
    .filter(Boolean)
    .some((name) => String(name).toLocaleLowerCase("en-US") === active);
}

function PluginActionButton({ plugin, shared }: { plugin: CatalogPlugin; shared: SharedStoreProps }) {
  const current = findInstalledPlugin(plugin, shared.installed);
  const update = pluginHasUpdate(plugin, shared.installed);
  const [preparing, setPreparing] = useState(false);
  if (current && !update) return null;
  const running = pluginOperationActive(plugin, current, shared.progress);
  const label = preparing || running ? shared.copy.preparing : update ? shared.copy.update : shared.copy.install;
  return (
    <DialogButton
      className="ph-store-action"
      aria-label={label}
      title={label}
      onOKActionDescription={label}
      disabled={preparing || running}
      onClick={async (event: any) => {
        stopEvent(event);
        setPreparing(true);
        try {
          const action = update ? shared.copy.update : shared.copy.install;
          if (await confirmStoreOperation(update ? shared.copy.updateQuestion : shared.copy.installQuestion, "", action, shared.copy)) {
            await installOrUpdatePlugin(plugin, shared);
          }
        } catch (error) {
          toaster?.toast?.({
            title: shared.copy.store,
            body: error instanceof Error ? error.message : shared.copy.releaseUnavailable,
          });
        } finally {
          setPreparing(false);
        }
      }}
    >
      {preparing || running
        ? <span className="ph-store-action-spinner"><SteamSpinner /></span>
        : update ? <TbRefresh /> : <TbDownload />}
      {running ? (
        <span className="ph-store-progress">
          <span style={{ width: `${shared.progress?.percentage ?? 0}%` }} />
        </span>
      ) : null}
    </DialogButton>
  );
}

function VersionPicker({ plugin, shared, versions, closeModal }: {
  plugin: CatalogPlugin; shared: SharedStoreProps; versions: InstallArtifact[]; closeModal?: () => void;
}) {
  const [busy, setBusy] = useState(false);
  return <ModalRoot closeModal={closeModal} onCancel={closeModal}>
    <h2>{shared.copy.chooseVersion}</h2>
    <Focusable flow-children="column" style={{ maxHeight: "60vh", overflowY: "auto", display: "flex", flexDirection: "column", gap: 12, padding: "8px 4px" }}>
      {!versions.length ? <p>{shared.copy.releaseUnavailable}</p> :
        versions.map((artifact) => <DialogButton key={artifact.url} disabled={busy}
          onClick={async () => {
            if (!await confirmStoreOperation(shared.copy.installQuestion, "", shared.copy.install, shared.copy)) return;
            setBusy(true);
            try {
              await requestDeckyInstall(plugin, artifact, shared.installed);
              closeModal?.();
            } catch (error) {
              toaster?.toast?.({ title: shared.copy.store, body: error instanceof Error ? error.message : shared.copy.releaseUnavailable });
            } finally { setBusy(false); }
          }}>{artifact.version}</DialogButton>)}
    </Focusable>
  </ModalRoot>;
}


function UninstallButton({ plugin, shared }: { plugin: CatalogPlugin; shared: SharedStoreProps }) {
  const installed = findInstalledPlugin(plugin, shared.installed);
  if (!installed) return null;
  return (
    <DialogButton
      className="ph-store-action"
      aria-label={shared.copy.uninstall}
      title={shared.copy.uninstall}
      onOKActionDescription={shared.copy.uninstall}
      onClick={(event: any) => {
        stopEvent(event);
        try {
          requestDeckyUninstall(
            installed.name,
            shared.copy.uninstallTitle,
            shared.copy.uninstall,
            shared.copy.uninstallDescription,
          );
        } catch (error) {
          toaster?.toast?.({
            title: shared.copy.store,
            body: error instanceof Error ? error.message : shared.copy.uninstallDescription,
          });
        }
      }}
    >
      <TbTrash />
    </DialogButton>
  );
}

function PluginRow({ plugin, shared, onOpen, manage = false }: {
  plugin: CatalogPlugin;
  shared: SharedStoreProps;
  onOpen?: (plugin: CatalogPlugin) => void;
  manage?: boolean;
}) {
  const update = pluginHasUpdate(plugin, shared.installed);
  const body = (
    <>
      <div className="ph-store-row-media"><PluginImage plugin={plugin} /></div>
      <div className="ph-store-row-copy">
        <h3 className="ph-store-row-title">{plugin.name}</h3>
        <p className="ph-store-row-description">{plugin.shortDescription}</p>
      </div>
      <div className="ph-store-row-trailing">
        {update ? <span className="ph-store-update-marker" aria-label={shared.copy.update}><TbRefresh /></span> : null}
        {plugin.catalogStatus === "installed-only" ? null : <SourceBadge source={plugin.catalogSource} />}
        {manage ? (
          <Focusable className="ph-store-actions" flow-children="row" noFocusRing>
            <PluginActionButton plugin={plugin} shared={shared} />
            <UninstallButton plugin={plugin} shared={shared} />
          </Focusable>
        ) : null}
      </div>
    </>
  );
  if (manage || !onOpen) return <div className="ph-store-row ph-store-row-manage">{body}</div>;
  return (
    <Focusable
      className="ph-store-row ph-store-row-openable"
      onActivate={(event: any) => { stopEvent(event); onOpen(plugin); }}
      onOKButton={(event: any) => { stopEvent(event); onOpen(plugin); }}
      onOKActionDescription={shared.copy.select}
    >
      {body}
    </Focusable>
  );
}

function PluginRows({ plugins, shared, onOpen, manage = false }: {
  plugins: CatalogPlugin[];
  shared: SharedStoreProps;
  onOpen?: (plugin: CatalogPlugin) => void;
  manage?: boolean;
}) {
  if (!plugins.length) return <div className="ph-store-empty">{shared.copy.noPlugins}</div>;
  return (
    <div className="ph-store-list">
      {plugins.map((plugin) => (
        <PluginRow
          key={`${plugin.catalogSource}:${plugin.repository}:${plugin.name}`}
          plugin={plugin}
          shared={shared}
          onOpen={onOpen}
          manage={manage}
        />
      ))}
    </div>
  );
}

function SourceFilters({ value, onChange, copy }: {
  value: SourceFilter;
  onChange: (value: SourceFilter) => void;
  copy: PluginStoreCopy;
}) {
  const options: Array<{ id: SourceFilter; label: string }> = [
    { id: "all", label: copy.allSources },
    { id: "playhub", label: "Playhub" },
    { id: "decky-store", label: "Decky Store" },
    { id: "outside-store", label: "GitHub" },
  ];
  return (
    <Focusable className="ph-store-filter" flow-children="row" noFocusRing>
      {options.map((option) => (
        <DialogButton
          key={option.id}
          className={value === option.id ? "ph-selected" : ""}
          onClick={() => onChange(option.id)}
        >
          {option.id === "all" ? option.label : <SourceBadge source={option.id} />}
        </DialogButton>
      ))}
    </Focusable>
  );
}

function sortLabel(sort: PluginSort, copy: PluginStoreCopy): string {
  if (sort === "newest") return copy.sortNewest;
  if (sort === "updated") return copy.sortUpdated;
  return copy.sortName;
}

function SortPickerModal({ value, copy, onChange, closeModal }: {
  value: PluginSort;
  copy: PluginStoreCopy;
  onChange: (value: PluginSort) => void;
  closeModal: () => void;
}) {
  const choose = (next: PluginSort) => {
    onChange(next);
    closeModal();
  };
  return (
    <ModalRoot className="ph-store-sort-modal" closeModal={closeModal} onCancel={closeModal}>
      <h2>{copy.sortBy}</h2>
      <Focusable className="ph-store-sort-options" flow-children="column" noFocusRing>
        {(["name", "newest", "updated"] as PluginSort[]).map((option) => (
          <DialogButton
            key={option}
            className={value === option ? "ph-selected" : ""}
            onClick={() => choose(option)}
          >
            {sortLabel(option, copy)}
          </DialogButton>
        ))}
      </Focusable>
    </ModalRoot>
  );
}

function openSortPicker(value: PluginSort, copy: PluginStoreCopy, onChange: (value: PluginSort) => void): void {
  let modal: any;
  const close = () => modal?.Close?.();
  modal = showModal(
    <SortPickerModal value={value} copy={copy} onChange={onChange} closeModal={close} />,
    window,
    { bNeverPopOut: true },
  );
}

function ListHeader({ title, sort, copy }: { title: string; sort: PluginSort; copy: PluginStoreCopy }) {
  return (
    <div className="ph-store-page-header" data-layout="title-left-sort-right">
      <h1 className="ph-store-page-title">{title}</h1>
      <div className="ph-store-sort-label"><TbArrowsSort /><span>{sortLabel(sort, copy)}</span></div>
    </div>
  );
}

function PluginCollection({ title, items, collectionKey, shared, onOpen, onBack, initialSort = "name", manage = false, toolbarStart }: {
  title: string;
  items: CatalogPlugin[];
  collectionKey: string;
  shared: SharedStoreProps;
  onOpen?: (plugin: CatalogPlugin) => void;
  onBack: () => void;
  initialSort?: PluginSort;
  manage?: boolean;
  toolbarStart?: any;
}) {
  const [sort, setSort] = useState<PluginSort>(initialSort);
  const [source, setSource] = useState<SourceFilter>("all");
  useEffect(() => {
    setSource("all");
    setSort(initialSort);
  }, [collectionKey, initialSort]);
  const filtered = useMemo(() => filterPlugins(
    source === "all" ? items : items.filter((plugin) => plugin.catalogStatus !== "installed-only"), {
    source,
    sort,
    installed: shared.installed,
    installedOnly: manage,
  }), [items, source, sort, manage, shared.installed]);
  const showSort = useCallback((event?: any) => {
    stopEvent(event);
    openSortPicker(sort, shared.copy, setSort);
  }, [sort, shared.copy]);
  const guardedBack = useGuardedCancel(onBack);
  return (
    <Focusable
      className="ph-store-tab-body ph-store-flow"
      flow-children="column"
      noFocusRing
      onCancel={guardedBack}
      onCancelButton={guardedBack}
      onCancelActionDescription={shared.copy.back}
      onOptionsButton={showSort}
      onOptionsActionDescription={shared.copy.sortBy}
    >
      <ListHeader title={title} sort={sort} copy={shared.copy} />
      <div className="ph-store-toolbar">
        {toolbarStart}
        <SourceFilters value={source} onChange={setSource} copy={shared.copy} />
      </div>
      <PluginRows plugins={filtered} shared={shared} onOpen={onOpen} manage={manage} />
    </Focusable>
  );
}

function PluginDetail({ plugin, shared, onBack }: {
  plugin: CatalogPlugin;
  shared: SharedStoreProps;
  onBack: () => void;
}) {
  const [release, setRelease] = useState<GithubRelease | null>(null);
  const [versions, setVersions] = useState<InstallArtifact[]>([]);
  useEffect(() => {
    let alive = true;
    setVersions([]);
    void fetchPluginVersions(plugin).then((items) => { if (alive) setVersions(items); }).catch(() => {});
    return () => { alive = false; };
  }, [plugin.repository, plugin.catalogSource]);
  const [media, setMedia] = useState<PluginMedia[]>([]);
  const [readme, setReadme] = useState("");
  const [coverSources, setCoverSources] = useState<string[]>([]);
  const [activeCover, setActiveCover] = useState("");
  const [loading, setLoading] = useState(true);
  const installed = findInstalledPlugin(plugin, shared.installed);
  const guardedBack = useGuardedCancel(onBack);
  useEffect(() => {
    let alive = true;
    setLoading(true);
    setRelease(null);
    setMedia([]);
    setReadme("");
    setActiveCover("");
    void loadPluginDetails(plugin).then((details) => {
      if (!alive) return;
      setRelease(details.release);
      setMedia(details.media);
      setReadme(details.descriptionMarkdown);
      setCoverSources(details.coverSources);
    }).finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [plugin.repository]);
  const releaseArtifact = release ? resolveInstallArtifact(plugin, release) : null;
  const latestVersion = releaseArtifact?.version || plugin.version;
  const currentPlugin = releaseArtifact && compareVersions(releaseArtifact.version, plugin.version) >= 0
    ? {
        ...plugin,
        version: releaseArtifact.version,
        catalogReleaseUrl: releaseArtifact.url,
        releaseAssetName: releaseArtifact.name,
        releasePublishedAt: releaseArtifact.release?.publishedAt || plugin.releasePublishedAt,
      }
    : plugin;
  const releaseNotes = useMemo(() => descriptionToMarkdown(
    stripMarkdownMedia(releaseArtifact?.release?.body ?? ""), plugin.repositoryUrl,
  ), [releaseArtifact?.release?.body, plugin.repositoryUrl]);
  const description = readme || plugin.longDescription || plugin.shortDescription;
  const galleryMedia = media.filter((item) => item.url !== activeCover
    && !(activeCover === bundledPluginCover(plugin.repository) && item.url === plugin.coverUrl));
  return (
    <Focusable
      className="ph-store-tab-body ph-store-detail ph-store-flow"
      flow-children="column"
      noFocusRing
      onCancel={guardedBack}
      onCancelButton={guardedBack}
      onCancelActionDescription={shared.copy.back}
    >
      <Focusable className="ph-store-detail-heading" flow-children="row" noFocusRing>
        <Focusable className="ph-store-detail-hero" flow-children="none" onOKActionDescription={shared.copy.select}
          onActivate={(event: any) => {
            stopEvent(event);
            if (activeCover) openStoreMedia({ kind: "image", url: activeCover, alt: plugin.name }, shared.copy);
          }}>
          <PluginImage plugin={plugin} fallbackSources={coverSources} onImageLoaded={setActiveCover} />
        </Focusable>
        <div className="ph-store-detail-summary">
          <SourceBadge source={plugin.catalogSource} />
          <h1 className="ph-store-detail-title">{plugin.name}</h1>
          <div className="ph-store-detail-status">
            {installed ? <span>{shared.copy.installedVersion}: {installed.version || "?"}</span> : null}
            <span>{shared.copy.latestVersion}: {latestVersion || "?"}</span>
          </div>
          <Focusable className="ph-store-actions" flow-children="row" noFocusRing>
            <PluginActionButton plugin={currentPlugin} shared={shared} />
            {versions.some((item) => compareVersions(item.version, latestVersion) < 0) && <DialogButton className="ph-store-action" aria-label={shared.copy.chooseVersion}
              title={shared.copy.chooseVersion} onOKActionDescription={shared.copy.chooseVersion}
              onClick={() => showModal(<VersionPicker plugin={plugin} shared={shared} versions={versions} />, window, { bNeverPopOut: true })}>
              <TbHistory />
            </DialogButton>}
            <UninstallButton plugin={plugin} shared={shared} />
          </Focusable>
        </div>
      </Focusable>
      {galleryMedia.length ? (
        <section className="ph-store-media-section">
          <h2>{shared.copy.media}</h2>
          <Focusable className="ph-store-media-strip" flow-children="row" noFocusRing>
            {galleryMedia.map((item) => <SafeMedia key={item.url} item={item} copy={shared.copy} />)}
          </Focusable>
        </section>
      ) : null}
      {loading ? (
        <div className="ph-store-empty"><SteamSpinner /></div>
      ) : (
        <ScrollReader copy={shared.copy} onBack={onBack}>
          <MarkdownBlocks markdown={description} />
          <h2>{shared.copy.releaseNotes}</h2>
          {releaseNotes
            ? <MarkdownBlocks markdown={releaseNotes} />
            : <p>{shared.copy.releaseNotesUnavailable}</p>}
        </ScrollReader>
      )}
    </Focusable>
  );
}

function CategoryLink({ title, onOpen, copy }: { title: string; onOpen: () => void; copy: PluginStoreCopy }) {
  return (
    <Focusable
      className="ph-store-section-link"
      onActivate={(event: any) => { stopEvent(event); onOpen(); }}
      onOKButton={(event: any) => { stopEvent(event); onOpen(); }}
      onOKActionDescription={copy.openCategory}
    >
      <h2>{title}</h2>
    </Focusable>
  );
}

function FeaturedSlider({ plugins, shared, onOpen }: {
  plugins: CatalogPlugin[];
  shared: SharedStoreProps;
  onOpen: (plugin: CatalogPlugin) => void;
}) {
  const [index, setIndex] = useState(0);
  const identity = plugins.map((plugin) => plugin.repository).join("|");
  useEffect(() => setIndex(0), [identity]);
  if (!plugins.length) return null;
  const active = plugins[index % plugins.length];
  const move = (delta: number) => {
    setIndex((value: number) => (value + delta + plugins.length) % plugins.length);
  };
  return (
    <div className="ph-store-featured-shell">
      <Focusable
        className="ph-store-featured-slide"
        flow-children="none"
        onActivate={(event: any) => { stopEvent(event); onOpen(active); }}
        onOKButton={(event: any) => { stopEvent(event); onOpen(active); }}
        onOKActionDescription={shared.copy.select}
        actionDescriptionMap={{
          [GamepadButton.DIR_LEFT]: shared.copy.previous,
          [GamepadButton.DIR_RIGHT]: shared.copy.next,
        }}
        onButtonDown={(event: any) => {
          const button = Number(event?.detail?.button);
          if (button === GamepadButton.DIR_LEFT) { stopEvent(event); move(-1); }
          if (button === GamepadButton.DIR_RIGHT) { stopEvent(event); move(1); }
        }}
      >
        <div className="ph-store-featured-media"><PluginImage plugin={active} /></div>
        <div className="ph-store-featured-copy">
          <SourceBadge source={active.catalogSource} />
          <h2>{active.name}</h2>
          <p>{active.shortDescription}</p>
          <div className="ph-store-featured-meta">
            <span>{active.version ? `v${active.version.replace(/^v/i, "")}` : ""}</span>
          </div>
        </div>
      </Focusable>
      <div className="ph-store-featured-dots">
        {plugins.map((plugin, dot) => (
          <span
            key={`${plugin.repository}:${dot}`}
            className={`ph-store-featured-dot${dot === index ? " ph-active" : ""}`}
          />
        ))}
      </div>
    </div>
  );
}

function DiscoverTab(shared: SharedStoreProps) {
  const [view, setView] = useState<{
    kind: "home" | "category" | "detail";
    category?: string;
    plugin?: CatalogPlugin;
  }>({ kind: "home" });
  const back = useCallback(() => {
    if (view.kind === "detail") {
      setView(view.category ? { kind: "category", category: view.category } : { kind: "home" });
      return;
    }
    if (view.kind === "category") { setView({ kind: "home" }); return; }
    Navigation?.NavigateBack?.();
  }, [view]);
  useTabBack("discover", back);
  const guardedBack = useGuardedCancel(back);
  if (view.kind === "detail" && view.plugin) {
    return <PluginDetail plugin={view.plugin} shared={shared} onBack={back} />;
  }
  if (view.kind === "category" && view.category) {
    const items = shared.plugins.filter((plugin) => pluginInCategory(plugin, view.category!));
    return (
      <PluginCollection
        title={localizePluginCategory(view.category, shared.locale)}
        items={items}
        collectionKey={`category:${view.category}`}
        shared={shared}
        initialSort="name"
        onBack={back}
        onOpen={(plugin) => setView({ kind: "detail", category: view.category, plugin })}
      />
    );
  }

  const featured = featuredPlugins(shared.plugins, 10);
  const featuredRepositories = new Set(featured.map((plugin) => plugin.repository.toLocaleLowerCase("en-US")));
  const categories = [
    "Playhub",
    ...Array.from(new Set(shared.plugins.map(pluginCategory)))
      .filter((category) => category !== "Playhub" && category !== "Novità")
      .sort((left, right) => left.localeCompare(right)),
  ];
  return (
    <Focusable
      className="ph-store-tab-body ph-store-flow"
      flow-children="column"
      noFocusRing
      onCancel={guardedBack}
      onCancelButton={guardedBack}
      onCancelActionDescription={shared.copy.back}
    >
      <section className="ph-store-section">
        <FeaturedSlider
          plugins={featured}
          shared={shared}
          onOpen={(plugin) => setView({ kind: "detail", plugin })}
        />
      </section>
      {categories.map((category) => {
        let items = shared.plugins.filter((plugin) => pluginInCategory(plugin, category));
        if (category === "Playhub") {
          items = items.filter((plugin) => !featuredRepositories.has(plugin.repository.toLocaleLowerCase("en-US")));
        }
        items = items.slice(0, 4);
        if (!items.length) return null;
        return (
          <section className="ph-store-section" key={category}>
            <CategoryLink
              title={localizePluginCategory(category, shared.locale)}
              copy={shared.copy}
              onOpen={() => setView({ kind: "category", category })}
            />
            <PluginRows
              plugins={items}
              shared={shared}
              onOpen={(plugin) => setView({ kind: "detail", category, plugin })}
            />
          </section>
        );
      })}
    </Focusable>
  );
}

function pluginsForManage(
  catalog: CatalogPlugin[],
  installed: InstalledPlugin[],
  copy: PluginStoreCopy,
): CatalogPlugin[] {
  const result = [...catalog];
  for (const current of installed) {
    if (isIntegratedPlayhubPlugin({ name: current.name, installFolder: current.name, aliases: [] })) continue;
    if (catalog.some((plugin) => Boolean(findInstalledPlugin(plugin, [current])))) continue;
    const identity = normalizePluginIdentity(current.name) || "plugin";
    result.push({
      active: true,
      name: current.name,
      installFolder: current.name,
      author: "",
      repository: `installed-local/${identity}`,
      repositoryUrl: "",
      version: current.version,
      releaseAssetName: "",
      catalogReleaseUrl: "",
      releasePublishedAt: "",
      category: "Other",
      shortDescription: `${copy.installedVersion}: ${current.version || "?"}`,
      longDescription: "",
      coverUrl: "",
      iconGlyph: "",
      catalogStatus: "installed-only",
      catalogSource: "outside-store",
      catalogPluginId: 0,
      compatibility: "",
      keywords: [current.name],
      aliases: [current.name],
    });
  }
  return result;
}

function ManageTab({ active, ...shared }: SharedStoreProps & { active: boolean }) {
  const [sourceReset, setSourceReset] = useState(0);
  useEffect(() => { if (active) setSourceReset((value: number) => value + 1); }, [active]);
  const back = useCallback(() => Navigation?.NavigateBack?.(), []);
  useTabBack("manage", back);
  const items = useMemo(
    () => pluginsForManage(shared.plugins, shared.installed, shared.copy),
    [shared.plugins, shared.installed, shared.copy],
  );
  return (
    <PluginCollection
      title={shared.copy.installedPlugins}
      items={items}
      collectionKey={`manage:${sourceReset}`}
      shared={shared}
      manage
      onBack={back}
    />
  );
}

function SearchTab({ active, ...shared }: SharedStoreProps & { active: boolean }) {
  const [detail, setDetail] = useState<CatalogPlugin | null>(null);
  const [query, setQuery] = useState("");
  const [source, setSource] = useState<SourceFilter>("all");
  const [sort, setSort] = useState<PluginSort>("name");
  useEffect(() => { if (active) setSource("all"); }, [active]);
  useEffect(() => { if (!detail) setSource("all"); }, [detail]);
  const back = useCallback(() => {
    if (detail) { setDetail(null); return; }
    Navigation?.NavigateBack?.();
  }, [detail]);
  useTabBack("search", back);
  const guardedBack = useGuardedCancel(back);
  if (detail) return <PluginDetail plugin={detail} shared={shared} onBack={back} />;
  const results = filterPlugins(shared.plugins, {
    query,
    source,
    sort,
    installed: shared.installed,
  });
  const showSort = (event?: any) => {
    stopEvent(event);
    openSortPicker(sort, shared.copy, setSort);
  };
  return (
    <Focusable
      className="ph-store-tab-body ph-store-flow"
      flow-children="column"
      noFocusRing
      onCancel={guardedBack}
      onCancelButton={guardedBack}
      onCancelActionDescription={shared.copy.back}
      onOptionsButton={showSort}
      onOptionsActionDescription={shared.copy.sortBy}
    >
      <ListHeader title={shared.copy.search} sort={sort} copy={shared.copy} />
      <div className="ph-store-search-shell">
        <TextField
          label={shared.copy.searchPlaceholder}
          value={query}
          onChange={(event: any) => setQuery(event.target.value)}
        />
      </div>
      <div className="ph-store-toolbar">
        <SourceFilters value={source} onChange={setSource} copy={shared.copy} />
      </div>
      <PluginRows plugins={results} shared={shared} onOpen={setDetail} />
    </Focusable>
  );
}

export function PluginStorePage() {
  const locale = useStoreLocale();
  const copy = useMemo(() => getPluginStoreCopy(locale), [locale]);
  const [tab, setTab] = useState<StoreTab>("discover");
  const back = useGuardedCancel(() => {
    const handler = tabBackHandlers.get(tab);
    if (handler) handler();
    else Navigation?.NavigateBack?.();
  });
  const [plugins, setPlugins] = useState<CatalogPlugin[] | null>(null);
  const [installed, setInstalled] = useState<InstalledPlugin[]>(() => readInstalledPlugins());
  const [error, setError] = useState(false);
  const [progress, setProgress] = useState<PluginOperationProgress | null>(null);
  const probedReleases = useRef(new Set<string>());
  const load = useCallback((refresh = false) => {
    setError(false);
    setPlugins(null);
    void (refresh ? refreshPluginStoreCatalog() : preloadPluginStoreCatalog())
      .then((catalog) => setPlugins(catalog.plugins))
      .catch(() => setError(true));
  }, []);
  useEffect(() => { load(false); }, [load]);
  useEffect(() => subscribeInstalledPlugins(() => setInstalled(readInstalledPlugins())), []);
  useEffect(() => subscribePluginProgress(setProgress), []);
  useEffect(() => {
    if (!plugins?.length || !installed.length) return;
    const targets = plugins.filter((plugin) => {
      if (plugin.catalogSource === "decky-store" || !findInstalledPlugin(plugin, installed)) return false;
      const key = `${plugin.repository.toLocaleLowerCase("en-US")}:${plugin.version}`;
      if (probedReleases.current.has(key)) return false;
      probedReleases.current.add(key);
      return true;
    });
    if (!targets.length) return;
    let alive = true;
    void Promise.all(targets.map(async (plugin) => {
      const release = await fetchLatestRelease(plugin.repository);
      return { plugin, artifact: resolveInstallArtifact(plugin, release) };
    })).then((results) => {
      if (!alive) return;
      const updates = new Map(results.flatMap(({ plugin, artifact }) =>
        artifact?.release && compareVersions(artifact.version, plugin.version) >= 0
          ? [[plugin.repository, {
              version: artifact.version,
              catalogReleaseUrl: artifact.url,
              releaseAssetName: artifact.name,
              releasePublishedAt: artifact.release.publishedAt || plugin.releasePublishedAt,
            }] as const]
          : [],
      ));
      if (!updates.size) return;
      setPlugins((current) => current?.map((plugin) => {
        const update = updates.get(plugin.repository);
        return update && compareVersions(update.version, plugin.version) >= 0 &&
            (update.version !== plugin.version || update.catalogReleaseUrl !== plugin.catalogReleaseUrl || update.releaseAssetName !== plugin.releaseAssetName)
          ? { ...plugin, ...update }
          : plugin;
      }) ?? null);
    });
    return () => { alive = false; };
  }, [plugins, installed]);

  if (error) {
    return (
      <div className="ph-store-page">
        <style>{STORE_STYLE}</style>
        <div className="ph-store-empty">
          <div>
            <p>{copy.catalogUnavailable}</p>
            <DialogButton onClick={() => load(true)}>{copy.retry}</DialogButton>
          </div>
        </div>
      </div>
    );
  }
  if (!plugins) {
    return (
      <div className="ph-store-page">
        <style>{STORE_STYLE}</style>
        <div className="ph-store-empty"><SteamSpinner /><span>{copy.loading}</span></div>
      </div>
    );
  }
  const shared: SharedStoreProps = { plugins, installed, copy, locale, progress };
  const footer = {
    actionDescriptionMap: {
      [GamepadButton.BUMPER_LEFT]: copy.switchTabs,
      [GamepadButton.BUMPER_RIGHT]: copy.switchTabs,
    },
    onCancelActionDescription: copy.back,
    onCancelButton: back,
  };
  return (
    <div className="ph-store-page">
      <style>{STORE_STYLE}</style>
      <div className="ph-store-tabs">
        <Tabs
          activeTab={tab}
          onShowTab={(next: string) => setTab(next as StoreTab)}
          autoFocusContents
          tabs={[
            { id: "discover", title: copy.discover, content: <DiscoverTab {...shared} />, footer },
            { id: "search", title: copy.search, content: <SearchTab {...shared} active={tab === "search"} />, footer },
            { id: "manage", title: copy.manage, content: <ManageTab {...shared} active={tab === "manage"} />, footer },
          ]}
        />
      </div>
    </div>
  );
}
