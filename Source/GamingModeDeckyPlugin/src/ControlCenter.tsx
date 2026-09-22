import { registerQamVisibilityDocument } from './qamTabVisibility';
import { HomeHistorySettings, historyTitle } from "./DailyHistory";
import { HomeNewsSettings } from "./HomeNews";
import { call } from "./controlBackend";
import { DFL, SP_REACT as React, toaster } from "./decky";
import { TbPower, TbDeviceTv, TbVolume, TbDeviceGamepad2, TbPhoto, TbSettings, TbShoppingBag, TbRestore, TbPlug } from "react-icons/tb";
import { controlLocale, controlStatus } from "./controlCenterLocale";
import { normalizeControlPreferences, visibleControlTabs, type ControlPreferences, type ControlTab } from "./controlCenterState";
import { createTabEditor, canToggleTab, tabEditorTransition, type TabEditorAction } from "./controlTabEditorState";
import { tabEditorLocale } from "./controlTabEditorLocale";
import { topbarDateCopy, topbarDateOptions } from "./topbarDateLocale";
import { QuickSettingsContent } from "./quickSettings";
import { ControlSection } from "./ControlSection";
import { DeckyHost, getDeckyHostSnapshot, subscribeDeckyHost, setDeckyHostEnabled, setDeckyNativeHidden } from "./deckyHost";
import { deckyIntegrationLocale } from "./deckyIntegrationLocale";
import { getOnboardingExtraCopy } from "./onboardingLocale";
import { registerOnboardingQamDocument, replayPlayhubOnboarding } from "./onboardingIntegration";
import { useOnboardingControlShowcase } from "./onboardingControlShowcase";
import { reportPlayhubOnboardingAction } from "./onboardingRuntime";
import { deviceInfoLocale } from "./deviceInfoLocale";

import { QamSettings, qamSettingsCopy } from "./QamSettings";

const { Focusable, DialogButton, ToggleField, DropdownItem, ScrollPanel, GamepadButton } = DFL as any;
const icons = { home: TbPower, audio: TbDeviceTv, performance: TbVolume, graphics: TbPhoto, controller: TbDeviceGamepad2, store: TbShoppingBag, decky: TbPlug };
let prefs = normalizeControlPreferences({});
let loaded = false;
let lastSettingsPage: 'general'|'qam' = 'general';
let loading: Promise<void> | undefined;
let write = Promise.resolve();
const listeners = new Set<() => void>();
const subscribe = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener); }; };

function TopbarDateSettings({ state, update, locale }: { state: ControlPreferences; update: (next: ControlPreferences, locale: string) => void; locale: string }) {
  const text = topbarDateCopy(locale);
  return <Focusable flow-children="column" style={{ padding: "8px 0", display: "flex", flexDirection: "column", gap: 10 }}>
    <ToggleField label={text.dateLabel} description={text.dateDescription} bottomSeparator="none"
      checked={state.topbarDateEnabled} onChange={(enabled: boolean) => update({ ...state, topbarDateEnabled: enabled }, locale)} />
    <ToggleField bottomSeparator="none" label={text.leftLabel} description={text.leftDescription} checked={state.topbarClockLeft} onChange={(enabled: boolean) => update({ ...state, topbarClockLeft: enabled }, locale)} />
    <DropdownItem label={text.formatLabel} layout="below" bottomSeparator="none" selectedOption={state.topbarDateFormat}
      disabled={!state.topbarDateEnabled} rgOptions={topbarDateOptions(locale)}
      onChange={(option: { data: string }) => update({ ...state, topbarDateFormat: option.data }, locale)} />
  </Focusable>;
}

function OnboardingInputCapture({ active }: { active: boolean }) {
  const root = React.useRef<HTMLDivElement>(null);
  React.useLayoutEffect(() => {
    if (!active || !root.current) return;
    const button = root.current.querySelector("button") as HTMLButtonElement | null;
    const focus = () => button?.focus?.({ preventScroll: true });
    focus();
    const owner = root.current.ownerDocument.defaultView;
    const frame = owner?.requestAnimationFrame(focus);
    return () => { if (frame !== undefined) owner?.cancelAnimationFrame(frame); };
  }, [active]);
  if (!active) return null;
  const consume = (event: any) => { event?.preventDefault?.(); event?.stopPropagation?.(); };
  return <div ref={root} data-playhub-onboarding-input-capture="true" style={{ position: "fixed", inset: 0,
    overflow: "hidden", opacity: 0.001, pointerEvents: "auto", zIndex: 2147483646 }}>
    <DialogButton tabIndex={0} onClick={(event: any) => { consume(event); reportPlayhubOnboardingAction("confirm"); }}
      onCancelButton={consume} style={{ width: "100%", height: "100%", minWidth: "100%", minHeight: "100%", padding: 0 }} />
  </div>;
}
function update(next: ControlPreferences, locale: string) {
  prefs = normalizeControlPreferences(next);
  listeners.forEach(listener => listener());
  const snapshot = prefs;
  write = write.catch(() => {}).then(async () => { await call("save_panel_preferences", snapshot); }).catch(() => {
    toaster.toast({ title: "Playhub", body: controlLocale(locale).saveError });
  });
}
const css = `
.ph-controls{width:100%;min-width:0;color:inherit;letter-spacing:0}
.ph-controls *{box-sizing:border-box;letter-spacing:0}
.ph-control-nav{display:grid;grid-template-columns:minmax(0,1fr);align-items:center;margin:4px 12px 12px;min-width:0;max-width:calc(100% - 24px)}
.ph-control-tabs{display:flex;gap:3px;flex:1 1 0;overflow-x:auto;scrollbar-width:none;padding:3px;min-width:0}
.ph-control-tabs::-webkit-scrollbar{display:none}
.ph-controls .ph-control-icon{display:grid!important;place-items:center;width:32px!important;min-width:32px!important;max-width:32px!important;height:34px!important;min-height:34px!important;max-height:34px!important;padding:0!important;margin:0!important;border-radius:5px!important;border:2px solid transparent!important;background:transparent!important;color:inherit!important;flex:0 0 32px!important;transform:none!important;box-sizing:border-box!important;transition:background .12s,color .12s!important}
.ph-control-icon svg{width:19px;height:19px}
.ph-controls .ph-control-tabs>.ph-control-icon{width:auto!important;max-width:none!important;flex:1 0 32px!important}
.ph-control-nav>.ph-control-tabs{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(0,1fr);overflow:visible;min-width:0}
.ph-controls .ph-control-nav .ph-control-tabs>.ph-control-icon{width:100%!important;min-width:0!important;max-width:100%!important;flex:none!important}
.ph-controls .ph-control-icon[aria-selected=true]{border-bottom-color:#fee505!important}
.ph-controls .ph-control-icon.gpfocus,.ph-controls .ph-control-icon:focus-visible,.ph-controls .ph-control-icon:hover{background:#dcdedf!important;color:#171a21!important;box-shadow:inset 0 0 0 2px rgba(255,255,255,.85)!important;transform:none!important}
.ph-control-heading{font-size:18px;font-weight:700;margin:0 18px 12px;line-height:1.35}
.ph-control-section{width:100%;min-width:0;margin:0 0 12px;border:0!important}
.ph-controls [class*="PanelSection"]::after,.ph-controls [class*="Field"]::after,.ph-controls [class*="Field"]::before{display:none!important}
.ph-controls [class*="PanelSection"],.ph-controls [class*="Field"]{border-top:0!important;border-bottom:0!important}
.ph-controls hr,.ph-controls [role="separator"],.ph-controls [class*="FieldSeparator"],.ph-controls [class*="PanelSectionSeparator"],.ph-controls [class*="Divider"]:empty,.ph-controls [class*="divider"]:empty{display:none!important}
.ph-controls [class*="BottomSeparator"],.ph-controls [class*="WithSeparator"]{border-top:0!important;border-bottom:0!important;box-shadow:none!important}
.ph-controls [class*="BottomSeparator"]::before,.ph-controls [class*="BottomSeparator"]::after,.ph-controls [class*="WithSeparator"]::before,.ph-controls [class*="WithSeparator"]::after{display:none!important}
.ph-controls .ph-control-section-title{width:100%!important;max-width:100%!important;min-width:0!important;margin:0!important;min-height:42px!important;padding:10px 12px!important;display:flex!important;align-items:center;justify-content:space-between;gap:8px;border-radius:5px!important;transform:none!important;font-size:14px!important}
.ph-control-section-title>span{display:flex;gap:9px;align-items:center;text-align:left;min-width:0}
.ph-control-section-title svg{width:17px;height:17px;flex:none}
.ph-control-section-body{padding:6px 0 12px}
.ph-control-settings{padding:12px 16px 20px}
.ph-tab-editor-title{font-size:18px;line-height:1.3;margin:16px 0 8px;font-weight:700}
.ph-control-settings p{font-size:12px;line-height:1.45;opacity:.7;margin:8px 0 18px}
.ph-control-setting-row{padding:4px 0 12px}
.ph-controls .ph-tab-editor{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(0,1fr);width:100%;min-width:0;margin:4px 0 4px;overflow:visible}
.ph-controls .ph-tab-editor>.ph-control-icon{width:100%!important;min-width:0!important;max-width:100%!important;transition:background .12s,color .12s!important}
.ph-tab-editor .ph-control-icon[data-hidden=true]{opacity:.4}
.ph-tab-editor .ph-control-icon[data-moving=true]{outline:1px dashed currentColor;outline-offset:-5px}
.ph-selected-tab{display:flex;align-items:center;justify-content:space-between;gap:12px;font-size:15px;font-weight:700;min-height:28px;line-height:1.4}
.ph-selected-tab>span{min-width:0;overflow-wrap:anywhere}
.ph-selected-tab-status{font-size:12px;font-weight:400;opacity:.75;text-align:right;max-width:48%}
.ph-controls .ph-settings-action{width:100%!important;display:flex!important;align-items:center;gap:10px;white-space:normal!important;text-align:left}
.ph-settings-action svg{flex:none;width:18px;height:18px}
.ph-settings-action span{min-width:0;overflow-wrap:anywhere}
.ph-control-settings .ph-tab-reset-hint{margin:10px 2px 0;opacity:.7}
.ph-editor-following{padding-top:32px}
.ph-controls .qsRedesign{padding:0 16px 18px!important}
.ph-controls .ph-control-settings .qsRedesign{padding:0 0 18px!important}
.ph-controls .qsCardBody{gap:12px;padding:8px 0!important}
`;

function DeviceInfo({ locale }: { locale: string }) {
  const [info, setInfo] = React.useState<any>();
  const [failed, setFailed] = React.useState(false);
  const copy = deviceInfoLocale(locale);
  React.useEffect(() => { let alive = true; void call("get_device_info").then(value => { if (alive) setInfo(value); }).catch(() => { if (alive) setFailed(true); }); return () => { alive = false; }; }, []);
  if (!info) return <div role="status" style={{ padding: "8px 10px", fontSize: 12 }}>{failed ? copy.loadError : copy.loading}</div>;
  const text = (value: unknown) => typeof value === "string" && value.trim() ? value : copy.unavailable;
  const storage = Number.isFinite(info.storage_total_bytes) && info.storage_total_bytes > 0 ? `${(info.storage_total_bytes / (1024 ** 3)).toFixed(1)} GiB` : copy.unavailable;
  const rows = [
    [copy.name, text(info.device_name)],
    [copy.model, text([info.manufacturer, info.model].filter(Boolean).join(" "))],
    [copy.cpu, text(info.cpu)],
    [copy.gpu, text(Array.isArray(info.gpu) ? info.gpu.filter((value: unknown) => typeof value === "string").join(", ") : info.gpu)],
    [copy.ram, Number.isFinite(info.ram) && info.ram > 0 ? `${info.ram} GiB` : copy.unavailable],
    [copy.storage, storage],
    [copy.edition, text(info.windows_edition)],
    [copy.version, text(info.windows_version)],
  ];
  return <div style={{ fontSize: 12, lineHeight: 1.5, overflowWrap: "anywhere", padding: "4px 10px" }}>
    <dl style={{ margin: "0 0 18px" }}>{rows.map(([label, value]) => <div key={label} style={{ padding: "7px 0" }}>
      <dt style={{ opacity: .65, marginBottom: 3 }}>{label}</dt><dd style={{ margin: 0, fontSize: 13 }}>{value}</dd>
    </div>)}</dl>
  </div>;
}

export function ControlCenter({ locale, origin, session, store, controller, settings, homeTitle }: {
  locale: string; origin: "qam" | "decky"; session: React.ReactNode; store: React.ReactNode; controller: React.ReactNode; settings: React.ReactNode; homeTitle?: string;
}) {
  const state = React.useSyncExternalStore(subscribe, () => prefs);
  const onboardingRoot = React.useRef<HTMLDivElement>(null);
  React.useLayoutEffect(() => {
    if (origin === "qam" && onboardingRoot.current) {
      const doc=onboardingRoot.current.ownerDocument;
      const stopOnboarding=registerOnboardingQamDocument(doc);
      const stopVisibility=registerQamVisibilityDocument(doc);
      return ()=>{stopVisibility();stopOnboarding();};
    }
  }, [origin]);
  const [manualCustomizing, setCustomizing] = React.useState(false);
  const [settingsPage, updateSettingsPage] = React.useState<'general'|'qam'>(lastSettingsPage);
  const setSettingsPage = (page:'general'|'qam') => {lastSettingsPage=page;updateSettingsPage(page);};
  const [editor, setEditor] = React.useState(createTabEditor);
  const editorRef = React.useRef(editor);
  const [ready, setReady] = React.useState(loaded);
  const copy = controlLocale(locale);
  const editorCopy = tabEditorLocale(locale);
  const integrationCopy = deckyIntegrationLocale(locale);
  const deckyHost = React.useSyncExternalStore(subscribeDeckyHost, getDeckyHostSnapshot);
  React.useEffect(() => {
    if (origin === "qam" && ready) {
      setDeckyNativeHidden(state.deckyHostEnabled);
      setDeckyHostEnabled(true);
    }
  }, [origin, ready, state.deckyHostEnabled]);
  React.useEffect(() => {
    let alive = true;
    if (!loading) loading = call<[], unknown>("get_panel_preferences").then(value => {
      prefs = normalizeControlPreferences(value); loaded = true; listeners.forEach(listener => listener());
    }).catch(() => { loading = undefined; }).finally(() => { if (alive) setReady(true); });
    else void loading.finally(() => { if (alive) setReady(true); });
    return () => { alive = false; };
  }, []);
  const deckyAvailable = origin === "qam" && deckyHost.available;
  const editorTabs = state.order;
  const visible = visibleControlTabs(state, deckyAvailable);
  const manualActive = visible.includes(state.active) ? state.active : visible[0] ?? "home";
  const { active, customizing, showcasing } = useOnboardingControlShowcase(origin === "qam", ready, visible, manualActive, manualCustomizing);
  const hostActive = !customizing && active === "decky" && deckyAvailable;
  const editing = editor.selected;
  const edit = (action: TabEditorAction) => {
    const result = tabEditorTransition(prefs, editorRef.current, action, deckyAvailable);
    editorRef.current = result.editor;
    setEditor(result.editor);
    if (result.preferences !== prefs) update(result.preferences, locale);
  };
  React.useEffect(() => {
    if (!editorTabs.includes(editing)) {
      const next = createTabEditor("home"); editorRef.current = next; setEditor(next);
    }
  }, [editing, state.order]);
  const cancelEditor = (event?: any) => {
    event?.stopPropagation?.(); event?.preventDefault?.();
    if (editorRef.current.draftOrder) edit({ type: "cancel" });
    else setCustomizing(false);
  };
  const editorDirection = (event: any) => {
    if (!editorRef.current.draftOrder) return;
    const button = event?.detail?.button;
    if (![GamepadButton.DIR_LEFT, GamepadButton.DIR_RIGHT, GamepadButton.DIR_UP, GamepadButton.DIR_DOWN].includes(button)) return;
    event.preventDefault?.(); event.stopPropagation?.();
    if (button === GamepadButton.DIR_LEFT || button === GamepadButton.DIR_RIGHT) edit({ type: "move", direction: button === GamepadButton.DIR_LEFT ? -1 : 1 });
  };
  const editorAction = (id: ControlTab) => {
    if (editor.draftOrder) return editorCopy.confirm;
    return !canToggleTab(state, id, deckyAvailable) ? editorCopy.lastTab : state.hidden.includes(id) ? editorCopy.show : editorCopy.hide;
  };
  const choose = (id: ControlTab) => update({ ...state, active: id }, locale);
  const toggleSection = (id: string) => update({ ...state, collapsed: state.collapsed.includes(id) ? state.collapsed.filter(x => x !== id) : [...state.collapsed, id] }, locale);
  const step = (direction: number) => {
    const index = visible.indexOf(active);
    choose(visible[(index + direction + visible.length) % visible.length]);
  };
  return <Focusable className="ph-control-shell" flow-children="column"
    onMoveDown={() => true}
    data-playhub-onboarding-controls={origin === "qam" ? "true" : undefined}
    data-onboarding-ready={ready} data-onboarding-decky-enabled={deckyHost.available}
    data-onboarding-decky-visible={visible.includes("decky")} data-onboarding-customizing={customizing}
    data-onboarding-active-tab={customizing ? "customize" : active}
    data-onboarding-store-visible={visible.includes("store")} data-onboarding-showcasing={showcasing}
    data-onboarding-visible-tabs={JSON.stringify(visible)}
    data-onboarding-content-ready={ready && (active !== "decky" || customizing || deckyHost.ready)}
    actionDescriptionMap={customizing ? {} : { [GamepadButton.BUMPER_LEFT]: copy.switchTab, [GamepadButton.BUMPER_RIGHT]: copy.switchTab }}
    onButtonDown={(event: any) => {
      if (customizing) return;
      const button = event?.detail?.button;
      if (button !== GamepadButton.BUMPER_LEFT && button !== GamepadButton.BUMPER_RIGHT) return;
      event.preventDefault?.(); event.stopPropagation?.(); step(button === GamepadButton.BUMPER_LEFT ? -1 : 1);
    }} onCancelButton={customizing ? cancelEditor : undefined} onCancelActionDescription={customizing ? editorCopy.cancel : undefined}>
    <OnboardingInputCapture active={origin === "qam" && showcasing} />
    <div className="ph-controls" ref={onboardingRoot}><style>{css}</style>
    {customizing ? <ScrollPanel><Focusable className="ph-control-settings" flow-children="column" data-playhub-onboarding-content={origin === "qam" ? "customize" : undefined}>
      <style>{`.ph-controls .ph-settings-pages{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:6px;padding:5px;border-radius:10px;background:rgba(0,0,0,.22);margin:0 0 20px;width:100%;min-width:0;max-width:100%}.ph-controls .ph-settings-pages>[role=tab]{display:block!important;width:100%!important;min-width:0!important;max-width:100%!important;height:40px!important;min-height:40px!important;padding:8px 6px!important;margin:0!important;border:0!important;border-radius:7px!important;background:transparent!important;box-shadow:none!important;font-size:14px!important;line-height:24px!important;text-align:center!important;white-space:nowrap!important;overflow:hidden;text-overflow:ellipsis}.ph-controls .ph-settings-pages>[role=tab][aria-selected=true]{background:rgba(255,255,255,.16)!important}.ph-controls .ph-settings-pages>[role=tab],.ph-controls .ph-settings-pages>[role=tab]:hover,.ph-controls .ph-settings-pages>[role=tab]:focus,.ph-controls .ph-settings-pages>[role=tab].gpfocus,.ph-controls .ph-settings-pages>[role=tab] *{color:#fff!important}.ph-controls .ph-settings-pages>[role=tab].gpfocus,.ph-controls .ph-settings-pages>[role=tab]:focus-visible{outline:2px solid #fff!important;outline-offset:-2px!important}`}</style>
      {!showcasing&&<Focusable className="ph-settings-pages" flow-children="row" role="tablist">
        <DialogButton role="tab" onOKActionDescription={copy.switchTab} aria-selected={settingsPage==='general'} onClick={()=>setSettingsPage('general')}>{qamSettingsCopy(locale)[0]}</DialogButton>
        <DialogButton role="tab" onOKActionDescription={copy.switchTab} aria-selected={settingsPage==='qam'} onClick={()=>setSettingsPage('qam')}>Shortcuts</DialogButton>
      </Focusable>}
      {settingsPage==='qam'&&!showcasing?<QamSettings locale={locale}>{settings}
        <ToggleField label={integrationCopy.label} description={integrationCopy.description} bottomSeparator="none" checked={state.deckyHostEnabled}
          onChange={(enabled: boolean) => update({ ...state, deckyHostEnabled: enabled }, locale)} />
      </QamSettings>:<>
      <h2 className="ph-tab-editor-title" data-playhub-onboarding={origin === "qam" ? "customize" : undefined}>{editorCopy.title}</h2>
      <p>{editorCopy.introduction}</p>
      <Focusable className="ph-control-tabs ph-tab-editor" flow-children="row" role="tablist" aria-label={copy.customize}
        onKeyDown={(event: any) => {
          if (event.key === "x" || event.key === "X") { event.preventDefault(); event.stopPropagation(); edit({ type: "secondary" }); }
          else if (event.key === "Escape") cancelEditor(event);
          else if (editorRef.current.draftOrder && ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) {
            event.preventDefault(); event.stopPropagation();
            if (event.key === "ArrowLeft" || event.key === "ArrowRight") edit({ type: "move", direction: event.key === "ArrowLeft" ? -1 : 1 });
          }
        }}>
        {editorTabs.map(id => { const Icon = icons[id]; return <DialogButton key={id} className="ph-control-icon" role="tab" aria-label={`${copy[id]}: ${state.hidden.includes(id) ? editorCopy.hidden : editorCopy.visible}`} title={copy[id]}
          onOKActionDescription={editorAction(id)} onSecondaryActionDescription={editor.draftOrder ? editorCopy.confirm : editorCopy.move} onCancelActionDescription={editorCopy.cancel}
          actionDescriptionMap={{ [GamepadButton.OK]: editorAction(id), [GamepadButton.SECONDARY]: editor.draftOrder ? editorCopy.confirm : editorCopy.move, [GamepadButton.CANCEL]: editorCopy.cancel }}
          aria-selected={editing === id} data-hidden={state.hidden.includes(id)} data-moving={editing === id && !!editor.draftOrder} style={{ order: (editor.draftOrder ?? state.order).indexOf(id) }}
          onFocus={() => edit({ type: "select", id })} onGamepadFocus={() => edit({ type: "select", id })}
          onGamepadDirection={editor.draftOrder ? editorDirection : undefined}
          onSecondaryButton={(event: any) => { event.preventDefault?.(); event.stopPropagation?.(); edit({ type: "select", id }); edit({ type: "secondary" }); }}
          onCancelButton={cancelEditor}
          onClick={() => { edit({ type: "select", id }); edit({ type: "confirm" }); }}><Icon/></DialogButton>; })}
      </Focusable>
      <div className="ph-control-setting-row" aria-live="polite">
        <div className="ph-selected-tab"><span>{copy[editing]}</span><span className="ph-selected-tab-status">{state.hidden.includes(editing) ? editorCopy.hidden : editorCopy.visible}{!canToggleTab(state, editing, deckyAvailable) && <><br/>{editorCopy.lastTab}</>}</span></div>
      </div>
      <DialogButton className="ph-settings-action" onOKActionDescription={editorCopy.reset} onClick={() => edit({ type: "reset" })}><TbRestore /><span>{editorCopy.reset}</span></DialogButton>
      <p className="ph-tab-reset-hint">{editorCopy.resetHint}</p>
      <div className="ph-editor-following">
      <div style={{ paddingTop: 16 }}>
        <ControlSection id="topbar-date" title={topbarDateCopy(locale).title} locale={locale} icon={<TbSettings/>} collapsed={state.collapsed.includes('topbar-date')} onToggle={toggleSection}><TopbarDateSettings state={state} update={update} locale={locale} /></ControlSection>
        <ControlSection id="device" title={copy.device} locale={locale} icon={<TbSettings/>} collapsed={state.collapsed.includes('device')} onToggle={toggleSection}><DeviceInfo locale={locale}/></ControlSection>
      </div>
      <ControlSection id="news" title="News" locale={locale} icon={<TbPhoto />} collapsed={state.collapsed.includes("news")} onToggle={toggleSection}><HomeNewsSettings locale={locale} /></ControlSection>
      <ControlSection id="daily-history" title={historyTitle(locale)} locale={locale} icon={<TbPhoto />} collapsed={state.collapsed.includes("daily-history")} onToggle={toggleSection}><HomeHistorySettings locale={locale} /></ControlSection>
      <QuickSettingsContent page="tools" locale={locale} collapsed={state.collapsed} onToggle={toggleSection} />
      <DialogButton className="ph-settings-action" onClick={() => { setCustomizing(false); replayPlayhubOnboarding(); }}>
        <TbRestore /> {getOnboardingExtraCopy(locale).replay}
      </DialogButton>
      </div>
      </>}
    </Focusable></ScrollPanel> : <>
      <Focusable className="ph-control-nav" flow-children="row">
        <Focusable className="ph-control-tabs" flow-children="row" role="tablist" aria-label="Playhub">
          {visible.map(id => { const Icon = icons[id]; return <DialogButton key={id} className="ph-control-icon" role="tab" aria-selected={active === id} aria-label={copy[id]} onOKActionDescription={copy[id]} disabled={!ready}
            data-playhub-onboarding={origin === "qam" ? id : undefined}
            onClick={() => choose(id)}><Icon /></DialogButton>; })}
        <DialogButton className="ph-control-icon" data-playhub-onboarding={origin === "qam" ? "customize" : undefined} aria-label={copy.settings} onOKActionDescription={copy.settings} disabled={!ready} onClick={() => setCustomizing(true)}><TbSettings /></DialogButton>
        </Focusable>
      </Focusable>
      {active !== "decky" && (active !== "home" || homeTitle) && <h2 className="ph-control-heading">{active === "home" ? homeTitle : copy[active]}</h2>}
      {!hostActive && <ScrollPanel><div data-playhub-onboarding-content={origin === "qam" ? active : undefined}>{active === "home" ? <div data-playhub-onboarding-content={origin === "qam" ? "home" : undefined}>{session}</div> : active === "store" ? <div data-playhub-onboarding-content={origin === "qam" ? "store" : undefined}>{store}</div> : active === "controller" ? <>{controller}</> :
        <QuickSettingsContent page={active === "decky" ? "home" : active} locale={locale} collapsed={state.collapsed} onToggle={toggleSection} />}</div></ScrollPanel>}
    </>}
    </div>
    {origin === "qam" && <DeckyHost active={hostActive} />}
  </Focusable>;
}
