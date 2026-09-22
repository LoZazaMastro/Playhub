import { SP_REACT as React, DFL, routerHook, toaster } from "./decky";
import { createOutputPatch } from "./reactOutputPatch";
import { call } from "./controlBackend";
import { HistoryTabs, notifyHistoryLanguageChanged } from "./DailyHistory";
import { newsCopy, NEWS_COUNTRIES } from "./homeNewsLocale";

const { Focusable, DialogButton, DropdownItem, ToggleField, Navigation } = DFL as any;
type Config = { enabled: boolean; country: string };
type Article = { title: string; url: string; source: string; image: string; logo: string; published: number };
type Feed = { items: Article[]; stale?: boolean; updated?: number };
let config: Config = { enabled: true, country: "auto" };
let ready = false;
const listeners = new Set<() => void>();
const subscribe = (f: () => void) => { listeners.add(f); return () => { listeners.delete(f); }; };
const notify = () => listeners.forEach(f => f());
export const notifyHomeNewsLanguageChanged = () => { config = { ...config }; notify(); notifyHistoryLanguageChanged(); };
const readConfig = async () => { config = await call<[], Config>("get_home_news_settings"); ready = true; notify(); };

export function HomeNewsSettings({ locale }: { locale: string }) {
  const value = React.useSyncExternalStore(subscribe, () => config);
  const [busy, setBusy] = React.useState(false);
  const [loadFailed, setLoadFailed] = React.useState(false);
  const pending = React.useRef(false);
  const copy = newsCopy(locale);
  React.useEffect(() => { if (!ready) void readConfig().catch(() => setLoadFailed(true)); }, []);
  const save = async (next: Config) => {
    if (pending.current) return;
    pending.current = true; setBusy(true);
    try { config = await call<[boolean, string], Config>("set_home_news_settings", next.enabled, next.country); notify(); }
    catch { toaster.toast({ title: "Playhub", body: copy[5] }); }
    finally { pending.current = false; setBusy(false); }
  };
  const names = new Intl.DisplayNames([locale], { type: "region" });
  return <Focusable flow-children="column" style={{ padding: "8px 0", display: "flex", flexDirection: "column", gap: 12 }}>
    {loadFailed && <DialogButton onClick={() => void readConfig().then(() => setLoadFailed(false)).catch(() => {})}>{copy[4]}</DialogButton>}
    <ToggleField label={copy[1]} bottomSeparator="none" checked={value.enabled} disabled={busy || !ready} onChange={(enabled: boolean) => void save({ ...value, enabled })} />
    <DropdownItem label={copy[2]} layout="below" bottomSeparator="none" selectedOption={value.country} disabled={busy || !ready}
      rgOptions={[{ data: "auto", label: copy[3] }, ...NEWS_COUNTRIES.map(data => ({ data, label: names.of(data) ?? data }))]}
      onChange={(option: { data: string }) => void save({ ...value, country: option.data })} />
  </Focusable>;
}

export function nativeNewsRows(tree: any): any[] | null {
  const children = tree?.props?.children;
  if (!Array.isArray(children)) return null;
  // Recognize Steam's two independent shelves by their render contracts, never index/hash.
  const source = (node: any) => String(node?.props?.children?.type ?? "");
  const activity = children.filter((node: any) => source(node).includes("BasicHomeUpdates") && source(node).includes("eventsToShow"));
  const updated = children.filter((node: any) => source(node).includes("RecentlyCompletedCarousel"));
  return activity.length <= 1 && updated.length <= 1 ? [...updated, ...activity] : null;
}
// Resolve Steam's own shelf primitives and CSS rather than approximate their scrolling.
let nativeSkin: any;
function shelfSkin() {
  if (!nativeSkin) {
    const classes = (DFL as any).findModule((value: any) => value?.BasicHomeUpdates && value?.EventPreviewContainer);
    const Bleed = (DFL as any).findModuleExport((value: any) => value?.Unbleed);
    if (!classes || !Bleed || !(DFL as any).Carousel) throw new Error("native_news_shelf_unavailable");
    nativeSkin = { classes, Bleed, Carousel: (DFL as any).Carousel };
  }
  return nativeSkin;
}
const css = `
.ph-home-news{padding:10px 0 22px;min-width:0;color:inherit}
.ph-home-news h2{font-size:20px;line-height:1.3;font-weight:600;margin:0 0 14px}
.ph-news-card{box-sizing:border-box;flex-shrink:0;overflow:hidden}
.ph-news-card,.ph-news-content{border-radius:var(--round-radius-size,0px)!important}
.ph-news-content{height:calc(100% - 22px);box-sizing:border-box;overflow:hidden;display:flex;flex-direction:column}
.ph-news-card .ph-news-info{box-sizing:border-box;flex:1;min-height:0;padding:12px 10px 14px;display:flex;flex-direction:column;gap:6px}
.ph-news-cover{box-sizing:border-box;overflow:hidden;border-radius:var(--round-radius-size,0px) var(--round-radius-size,0px) 0 0!important}
.ph-news-info{border-radius:0 0 var(--round-radius-size,0px) var(--round-radius-size,0px)!important}
.ph-news-card .ph-news-title{display:-webkit-box;-webkit-line-clamp:4;-webkit-box-orient:vertical;overflow:hidden;text-overflow:ellipsis;font-size:16px;line-height:20px;max-height:80px;flex-shrink:0;margin:0}
.ph-news-card .ph-news-date{font-size:12px;line-height:1.4;opacity:.65;min-height:17px}
.ph-news-card .ph-news-image{width:100%;height:100%;object-fit:cover}
.ph-home-news .ph-news-card .ph-news-cover,.ph-home-news .ph-news-card .ph-news-image{filter:none!important;backdrop-filter:none!important;opacity:1!important}
.ph-news-card .ph-news-cover[data-logo=true]{display:flex;align-items:center;justify-content:center;background:linear-gradient(135deg,#263449,#101823)}
.ph-news-card .ph-news-cover[data-logo=true] .ph-news-image{width:64px;height:64px;object-fit:contain}
.ph-news-card .ph-news-publisher-logo{object-fit:contain}
.ph-news-status{padding:12px 0;font-size:14px;opacity:.72}
`;
function NewsCard({ item, width, height, locale, skin }: { item: Article; width: number; height: number; locale: string; skin: any }) {
  const [fallback, setFallback] = React.useState(!item.image);
  const [missing, setMissing] = React.useState(false);
  
  const c = skin.classes;
  const date = item.published > 0 ? new Intl.DateTimeFormat(locale, { weekday: "short", day: "numeric", month: "long" }).format(new Date(item.published * 1000)) : "";
  return <div className={`${c.OuterWrapper} ph-news-card`} style={{ width, height }} data-news-url={item.url}>
    <div className={c.EventType}>{item.source}</div>
    <Focusable className={`${c.EventPreviewContainer} ph-news-content`} focusable={true} aria-label={`${item.title} Â· ${item.source}`} onActivate={() => {
      if (/^https?:\/\//i.test(item.url)) Navigation.NavigateToExternalWeb(item.url);
    }}>
      <div className={`${c.EventImageWrapper} ph-news-cover`} style={{ height: Math.round(width * 9 / 16), minHeight: Math.round(width * 9 / 16), flexShrink: 0 }} data-logo={fallback}>
        {!missing ? <img className={`${c.EventImage} ph-news-image`} loading="lazy" alt="" src={fallback ? item.logo : item.image} onError={() => fallback ? setMissing(true) : setFallback(true)} /> : <span>{item.source}</span>}
      </div>
      <div className={`${c.EventInfo} ph-news-info`}>
        <div className="ph-news-date">{date}</div>
        <div className={`${c.Title} ph-news-title`}>{item.title}</div>
      </div>
    </Focusable>
  </div>;
}
function NewsRow({ locale, country }: { locale: string; country: string }) {
  const [feed, setFeed] = React.useState<Feed>();
  const copy = newsCopy(locale);
  React.useEffect(() => {
    let alive = true, running = false;
    setFeed(undefined);
    const refresh = async () => {
      if (running) return;
      running = true;
      try { const next = await call<[string], Feed>("get_home_news", locale); if (alive) setFeed(next); }
      catch { if (alive) setFeed(previous => previous ? { ...previous, stale: true } : { items: [] }); }
      finally { running = false; }
    };
    void refresh(); const timer = window.setInterval(() => void refresh(), 30 * 60 * 1000);
    return () => { alive = false; window.clearInterval(timer); };
  }, [locale, country]);
  const skin = shelfSkin();
  const { classes: c, Bleed, Carousel } = skin;
  const items = feed?.items ?? [];
  const width = Number.parseInt(c.ItemPreviewWidth) || 300;
  // Keep every card the same height while giving four-line headlines a little
  // more breathing room below the image and above the shelf edge.
  const height = (Number.parseInt(c.ItemPreviewHeight) || 335) + 24;
  const margin = Number.parseInt(c.ItemMarginRight) || 12;
  const columnWidth = React.useCallback(() => width, [width]);
  return <section className="ph-home-news" aria-label={copy[0]}><style>{css}</style><h2>{copy[0]}</h2>
    {feed?.stale && <div className="ph-news-status">{copy[6]}</div>}
    <Bleed><Carousel key={`${locale}:${country}`} name="PlayhubEditorialNews" className={`${c.EventCarousel} ph-news-row`}
      nNumItems={feed ? items.length : 4} nHeight={height} nItemHeight={height} nItemMarginX={margin}
      fnGetColumnWidth={columnWidth} fnGetId={(index: number) => items[index]?.url ?? `loading-${index}`}
      fnItemRenderer={(index: number, itemWidth: number, itemHeight: number) => items[index]
        ? <NewsCard key={items[index].url} item={items[index]} width={itemWidth} height={itemHeight} locale={locale} skin={skin} />
        : <div aria-hidden="true" style={{ width: itemWidth, height: itemHeight, background: "#202a37" }} />}
      scrollToAlignment="center" aria-label={copy[0]} aria-busy={!feed} /></Bleed>
    {feed && !items.length && <div className="ph-news-status" role="status">{copy[4]}</div>}
  </section>;
}

const newsDiagnostics = { renders: 0, nativeRows: 0, reason: "initializing", error: "" };
class NewsBoundary extends React.Component<{ fallback: any; children?: React.ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: unknown) { newsDiagnostics.reason = "render_error"; newsDiagnostics.error = String(error); console.warn("[Playhub News] native Home restored", error); }
  render() { return this.state.failed ? this.props.fallback : this.props.children; }
}
function Composition({ native, getLocale, active }: { native: any; getLocale: () => string; active: () => boolean }) {
  const value = React.useSyncExternalStore(subscribe, () => config);
  const rows = nativeNewsRows(native);
  newsDiagnostics.renders++;
  newsDiagnostics.nativeRows = rows?.length ?? -1;
  newsDiagnostics.reason = !active() ? "unloaded" : !value.enabled ? "disabled" : !rows ? "unrecognized_tree" : "rendering";
  if (!active() || !value.enabled || !rows) return native;
  return <NewsBoundary fallback={native}>{React.cloneElement(native, {}, <NewsRow key="playhub-news" locale={getLocale()} country={value.country} />, ...rows)}</NewsBoundary>;
}
export function installHomeNews(getLocale: () => string) {
  let alive = true;
  (window as any)[Symbol.for("playhub.home-news.diagnostics")] = newsDiagnostics;
  void readConfig().catch(error => console.warn("[Playhub News] settings unavailable", error));
  const onError = (error: unknown) => console.warn("[Playhub News] native Home preserved", error);
  const news = createOutputPatch([() => true],
    (native: any) => alive ? <Composition native={native} getLocale={getLocale} active={() => alive} /> : native,
    onError);
  const descend = createOutputPatch([
    () => true,
    () => true,
    (node: any) => node?.props && "strActiveTab" in node.props && "setActiveTab" in node.props,
  ], (tree: any) => {
    if (!alive || !Array.isArray(tree?.props?.tabs)) return tree;
    const tabs = tree.props.tabs.map((tab: any) => tab.id === "WhatsNew"
      ? { ...tab, content: news.apply(tab.content) } : tab);
    const native = React.cloneElement(tree, { tabs });
    return <HistoryTabs native={native} getLocale={getLocale} />;
  }, onError);
  const patch = routerHook.addPatch("/library/home", (props: any) => {
    const children = alive ? descend.apply(props.children) : props.children;
    return children === props.children ? props : { ...props, children };
  });
  return () => {
    alive = false;
    descend.stop();
    news.stop();
    routerHook.removePatch("/library/home", patch);
    notifyHomeNewsLanguageChanged();
  };
}
