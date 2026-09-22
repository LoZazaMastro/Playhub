import * as MenuReact from "react";
import { insertPluginSection } from "../pluginMenuSection";
import {
  DialogButton,
  Dropdown,
  Export,
  fakeRenderComponent,
  findInReactTree,
  findInTree,
  findModuleByExport,
  Focusable,
  GamepadButton,
  MenuItem,
  Navigation,
  Patch,
  Router,
  Spinner,
  ToggleField,
  afterPatch,
  useParams,
} from "@decky/ui";
import { routerHook, toaster } from "@decky/api";
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { gameSettingsLabel } from "./gameSettingsLocale";
import { FaArrowLeft, FaBolt, FaCropAlt, FaDesktop, FaImage, FaMagic, FaMicrochip, FaMousePointer } from "react-icons/fa";

import {
  getLosslessProfile,
  reportGameRuntime,
  saveLosslessProfile,
  type AmdProfileSettings,
  type LosslessGameProfile,
  type LosslessProfileSettings,
} from "./backend";
import { t } from "./translations";

const ROUTE = "/quick-settings/:appid";
const LOSSLESS_APP_ID = 993090;
const RUNTIME_POLL_MS = 2500;
const RUNTIME_HEARTBEAT_MS = 8000;
const GAME_PAGE_ACTIVE_CLASS = "qsGamePageActive";
const GAME_PAGE_STYLE_ID = "qs-game-page-chrome-style";
const GAME_PAGE_CHROME_SELECTORS = [
  "#header",
  '[class*="GamepadHeader"]',
  '[class*="HeaderStatus"]',
  '[class*="StatusIcons"]',
  '[class*="TopBar"]',
];

const choice = (items: string[]) => items.map((item) => ({ data: item, label: item }));

function gamePageDocuments(): Document[] {
  const documents: Document[] = [];
  const addDocument = (candidate: Document | null | undefined) => {
    try {
      if (candidate?.documentElement && !documents.includes(candidate)) {
        documents.push(candidate);
      }
    } catch {}
  };
  const addWindowDocument = (candidate: any) => {
    if (!candidate) return;
    try { addDocument(candidate.document); } catch {}
    try { addDocument(candidate.window?.document); } catch {}
    try { addDocument(candidate.m_Window?.document); } catch {}
    try { addDocument(candidate.m_popup?.document); } catch {}
    try { addDocument(candidate.BrowserWindow?.document); } catch {}
    try { addDocument(candidate.GetWindow?.()?.document); } catch {}
  };

  addDocument(document);
  try { addDocument(window.top?.document); } catch {}
  try { addDocument(window.parent?.document); } catch {}
  try { addDocument(window.opener?.document); } catch {}

  const store = (Router as any)?.WindowStore;
  addWindowDocument(store?.GamepadUIMainWindowInstance);
  if (Array.isArray(store?.SteamUIWindows)) {
    store.SteamUIWindows.forEach(addWindowDocument);
  }
  return documents;
}

function markGamePageChrome() {
  gamePageDocuments().forEach((targetDocument) => {
    try {
      targetDocument.documentElement.classList.add(GAME_PAGE_ACTIVE_CLASS);
      targetDocument.body?.classList.add(GAME_PAGE_ACTIVE_CLASS);
      let style = targetDocument.getElementById(GAME_PAGE_STYLE_ID) as HTMLStyleElement | null;
      if (!style) {
        style = targetDocument.createElement("style");
        style.id = GAME_PAGE_STYLE_ID;
        const selectors = GAME_PAGE_CHROME_SELECTORS.flatMap((selector) => [
          `html.${GAME_PAGE_ACTIVE_CLASS} ${selector}`,
          `body.${GAME_PAGE_ACTIVE_CLASS} ${selector}`,
        ]);
        style.textContent = `${selectors.join(",")}{display:none!important;opacity:0!important;visibility:hidden!important;pointer-events:none!important;transition:none!important;animation:none!important}`;
        targetDocument.head?.appendChild(style);
      }
    } catch {}
  });
}

function useGamePageChromeSuppression() {
  useLayoutEffect(() => {
    markGamePageChrome();
    const followUps = [40, 120, 300, 700, 1200, 2000].map((delay) =>
      window.setTimeout(markGamePageChrome, delay)
    );
    const steady = window.setInterval(markGamePageChrome, 1800);
    return () => {
      window.clearInterval(steady);
      followUps.forEach((timer) => window.clearTimeout(timer));
      gamePageDocuments().forEach((targetDocument) => {
        try {
          targetDocument.documentElement.classList.remove(GAME_PAGE_ACTIVE_CLASS);
          targetDocument.body?.classList.remove(GAME_PAGE_ACTIVE_CLASS);
        } catch {}
      });
    };
  }, []);
}

const navigateToQuickSettingsGamePage = (path: string) => {
  markGamePageChrome();
  Navigation.Navigate(path);
};

function directionFromKey(key: string) {
  if (key === "ArrowLeft" || key === "Left") return -1;
  if (key === "ArrowRight" || key === "Right") return 1;
  return 0;
}

function directionFromButton(button: unknown) {
  if (button === GamepadButton.DIR_LEFT) return -1;
  if (button === GamepadButton.DIR_RIGHT) return 1;
  return 0;
}

function ProfileSlider({
  label,
  value,
  min,
  max,
  step = 1,
  suffix = "",
  description,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  suffix?: string;
  description?: string;
  onChange: (value: number) => void;
}) {
  const valueRef = useRef(value);
  useEffect(() => { valueRef.current = value; }, [value]);
  const setNext = (next: number) => {
    const precision = step < 1 ? 2 : 0;
    const clamped = Math.max(min, Math.min(max, Number((Math.round(next / step) * step).toFixed(precision))));
    valueRef.current = clamped;
    onChange(clamped);
  };
  const fill = max > min ? ((value - min) / (max - min)) * 100 : 0;
  return (
    <Focusable
      className="qspSlider"
      focusClassName="qspSliderFocused"
      noFocusRing
      onActivate={() => undefined}
      onButtonDown={(event: any) => {
        const direction = directionFromButton(event?.detail?.button);
        if (!direction) return;
        event.preventDefault?.();
        event.stopPropagation?.();
        setNext(valueRef.current + direction * step);
      }}
      onKeyDown={(event: any) => {
        const direction = directionFromKey(event.key);
        if (!direction) return;
        event.preventDefault();
        event.stopPropagation();
        setNext(valueRef.current + direction * step);
      }}
      role="slider"
      tabIndex={0}
    >
      <div className="qspSliderHeader"><span>{label}</span><strong>{`${value}${suffix}`}</strong></div>
      {description ? <div className="qspHint">{description}</div> : null}
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        tabIndex={-1}
        style={{ "--qsp-fill": `${fill}%` } as any}
        onChange={(event) => setNext(Number(event.currentTarget.value))}
      />
    </Focusable>
  );
}

function ProfileDropdown({
  label,
  value,
  options,
  description,
  onChange,
}: {
  label: string;
  value: string;
  options: string[];
  description?: string;
  onChange: (value: string) => void;
}) {
  return (
    <div className="qspDropdownBlock">
      <div className="qspLabel">{label}</div>
      {description ? <div className="qspHint">{description}</div> : null}
      <div className="qspDropdown">
        <Dropdown
          focusable
          menuLabel={label}
          rgOptions={choice(options)}
          selectedOption={value}
          onChange={(option: any) => onChange(String(option?.data ?? option))}
        />
      </div>
    </div>
  );
}

function ProfileCard({ icon, title, description, className = "", children }: { icon: ReactNode; title: string; description?: string; className?: string; children: ReactNode }) {
  return (
    <Focusable flow-children="column" className={`qspCard ${className}`.trim()}>
      <div className="qspCardTitle">{icon}<span>{title}</span></div>
      {description ? <div className="qspCardDescription">{description}</div> : null}
      <Focusable flow-children="column" className="qspCardBody">{children}</Focusable>
    </Focusable>
  );
}

async function getGameTitle(appId: number): Promise<string> {
  const candidates = [
    (window as any)?.appStore?.GetAppOverviewByAppID?.(appId),
    (window as any)?.SteamClient?.Apps?.GetAppOverviewByAppID?.(appId),
  ];
  for (const candidate of candidates) {
    try {
      const overview = await Promise.resolve(candidate);
      const title = overview?.display_name ?? overview?.displayName ?? overview?.name;
      if (title) return String(title);
    } catch {
      // Try the next Steam surface.
    }
  }
  return `App ${appId}`;
}

export function LosslessGamePage() {
  useGamePageChromeSuppression();
  const local = t();
  const params = useParams<{ appid?: string }>();
  const appId = Number.parseInt(params?.appid ?? "0", 10) || 0;
  const [profile, setProfile] = useState<LosslessGameProfile | null>(null);
  const [saveState, setSaveState] = useState("");
  const saveTimer = useRef<number | undefined>(undefined);

  useEffect(() => {
    let cancelled = false;
    void getGameTitle(appId).then(async (title) => {
      try {
        const loaded = await getLosslessProfile(appId, title);
        if (!cancelled) setProfile(loaded);
      } catch {
        if (!cancelled) {
          toaster.toast({ title: local.title, body: local.profileLoadError });
          setSaveState(local.profileLoadError);
        }
      }
    });
    return () => {
      cancelled = true;
      window.clearTimeout(saveTimer.current);
    };
  }, [appId]);

  const queueSave = (next: LosslessGameProfile) => {
    setProfile(next);
    setSaveState(local.saving);
    window.clearTimeout(saveTimer.current);
    saveTimer.current = window.setTimeout(() => {
      void saveLosslessProfile(
        next.app_id,
        next.title,
        next.auto_enabled,
        next.settings,
        next.amd_auto_enabled,
        next.amd_settings,
        next.sdl3_native_controller_enabled,
      )
        .then((result: any) => {
          setSaveState(result?.ok === false ? (result?.message || local.profileLoadError) : local.saved);
        })
        .catch(() => setSaveState(local.profileLoadError));
    }, 350);
  };

  const updateSetting = <K extends keyof LosslessProfileSettings>(
    key: K,
    value: LosslessProfileSettings[K],
  ) => {
    if (!profile) return;
    queueSave({ ...profile, settings: { ...profile.settings, [key]: value } });
  };
  const updateAmdSetting = <K extends keyof AmdProfileSettings>(
    key: K,
    value: AmdProfileSettings[K],
  ) => {
    if (!profile) return;
    const next = { ...profile.amd_settings, [key]: value };
    if (key === "sharpening_value") {
      next.sharpening_value = Math.max(0, Math.min(100, Math.round(Number(value) / 10) * 10));
    }
    if (value === true) {
      if (key === "rsr") next.sharpening = false;
      if (key === "sharpening") next.rsr = false;
      if (key === "chill") {
        next.antilag = false;
        next.boost = false;
      }
      if (key === "antilag" || key === "boost") next.chill = false;
    }
    if (next.chill_min > next.chill_max) {
      if (key === "chill_min") next.chill_max = next.chill_min;
      else next.chill_min = next.chill_max;
    }
    queueSave({ ...profile, amd_settings: next });
  };
  if (!profile) {
    return <div className="qspLoading"><Spinner /></div>;
  }

  const s = profile.settings;
  const a = profile.amd_settings;
  const amd = profile.amd_status;
  return (
    <Focusable
      className="qspPage"
      flow-children="vertical"
      onCancel={() => { Navigation.NavigateBack(); return true; }}
      onCancelButton={() => { Navigation.NavigateBack(); return true; }}
      onFocusCapture={(event: any) => {
        event?.target?.scrollIntoView?.({ block: "nearest", inline: "nearest", behavior: "smooth" });
      }}
    >
      <style>{`
        .qspPage{position:fixed;inset:0 0 48px;z-index:10;width:100vw;height:auto;background:#08090a;color:#fff;padding:26px max(34px,calc((100vw - 1450px)/2)) 24px;box-sizing:border-box;overflow-y:auto;overflow-x:hidden;scrollbar-width:none;overscroll-behavior:contain;scroll-padding:24px 0 24px}
        .qspPage::-webkit-scrollbar{display:none;width:0;height:0}
        .qspHeader{display:grid;grid-template-columns:52px minmax(0,1fr) auto;align-items:center;gap:15px;margin:0 0 18px;width:100%}
        .qspBack{width:52px!important;height:52px!important;min-width:52px!important;padding:0!important;border-radius:6px!important}
        .qspBack svg{width:20px;height:20px}
        .qspEyebrow{font-size:13px;font-weight:700;color:rgba(255,255,255,.48)}
        .qspTitle{font-size:30px;line-height:1.12;font-weight:750;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
        .qspSave{font-size:13px;color:rgba(255,255,255,.55);min-width:100px;text-align:right}
        .qspGrid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));align-items:start;gap:14px;width:100%;padding-bottom:24px}
        .qspWide{grid-column:1/-1;min-width:0}
        .qspColumns{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));align-items:start;gap:14px;width:100%}
        .qspColumn{display:flex;flex-direction:column;align-items:stretch;gap:14px;min-width:0;width:100%;height:100%}
        .qspCard{align-self:start;min-width:0;width:100%;max-width:100%;padding:14px;border:1px solid rgba(255,255,255,.12);border-radius:6px;background:rgba(255,255,255,.045);box-sizing:border-box}
        .qspRenderingCard{flex:1}
        .qspCardTitle{display:flex;align-items:center;gap:9px;font-size:17px;font-weight:750;margin-bottom:6px}
        .qspCardTitle svg{width:16px;height:16px;color:#66c0f4}
        .qspCardDescription{font-size:12.5px;line-height:1.35;color:rgba(255,255,255,.48);margin:0 0 10px 25px;max-width:64ch}
        .qspCardBody{display:flex;flex-direction:column;gap:8px}
        .qspCardBody>.DialogToggleField{padding:7px 2px!important;background:transparent!important}
        .qspCardBody>.DialogToggleField:hover,.qspCardBody>.DialogToggleField.gpfocus{background:rgba(102,192,244,.12)!important}
        .qspSlider{display:flex;flex-direction:column;gap:6px;padding:8px 10px;border:1px solid transparent;border-radius:5px;background:rgba(255,255,255,.045)}
        .qspSliderFocused{border-color:rgba(102,192,244,.92)!important;background:rgba(102,192,244,.13)!important;box-shadow:0 0 0 2px rgba(102,192,244,.17)}
        .qspSliderHeader{display:flex;justify-content:space-between;gap:12px;font-size:14px;color:rgba(255,255,255,.78)}
        .qspSliderHeader strong{font-weight:650;color:rgba(255,255,255,.58)}
        .qspSlider input[type="range"]{width:100%;height:4px;margin:2px 0;appearance:none;background:linear-gradient(90deg,#1a9fff var(--qsp-fill),rgba(255,255,255,.16) var(--qsp-fill));border-radius:999px}
        .qspSlider input[type="range"]::-webkit-slider-thumb{appearance:none;width:15px;height:15px;border-radius:50%;background:#fff;box-shadow:0 1px 5px rgba(0,0,0,.45)}
        .qspDropdownBlock{display:flex;flex-direction:column;gap:5px}
        .qspLabel{font-size:13px;font-weight:650;color:rgba(255,255,255,.58)}
        .qspHint{font-size:11.5px;line-height:1.35;color:rgba(255,255,255,.42);max-width:68ch}
        .qspDropdown,.qspDropdown>div,.qspDropdown button,.qspDropdown .DialogDropDown{width:100%!important;max-width:100%!important}
        .qspDropdown button,.qspDropdown .DialogDropDown{min-height:40px!important;color:#fff!important;background:rgba(255,255,255,.09)!important;border-radius:5px!important}
        .qspDropdown button:hover,.qspDropdown button.gpfocus,.qspDropdown .DialogDropDown.gpfocus{color:#fff!important;background:rgba(102,192,244,.16)!important;box-shadow:inset 0 0 0 1px #66c0f4!important}
        .qspDropdown button *,.qspDropdown .DialogDropDown *{color:inherit!important}
        .qspLoading{position:fixed;inset:0 0 48px;z-index:10;display:flex;align-items:center;justify-content:center;background:#08090a}
        @media(max-width:900px){.qspGrid,.qspColumns{grid-template-columns:1fr}.qspPage{padding:20px}.qspTitle{font-size:25px}}
      `}</style>
      <div className="qspHeader">
        <DialogButton className="qspBack" aria-label={local.back} onClick={() => Navigation.NavigateBack()}>
          <FaArrowLeft />
        </DialogButton>
        <div>
          <div className="qspEyebrow">{local.title}</div>
          <div className="qspTitle">{profile.title}</div>
        </div>
        <div className="qspSave">{saveState}</div>
      </div>

      <Focusable flow-children="grid" className="qspGrid">
        <Focusable flow-children="horizontal" className="qspWide qspColumns">
          <Focusable flow-children="vertical" className="qspColumn">
          <ProfileCard icon={<FaBolt />} title={local.automaticScaling} description={local.automaticScalingDesc}>
            <ToggleField
              label={local.autoScaleGame}
              description={local.autoScaleGameDesc}
              checked={profile.auto_enabled}
              onChange={(auto_enabled: boolean) => queueSave({ ...profile, auto_enabled })}
            />
          </ProfileCard>

          <ProfileCard icon={<FaImage />} title={local.scalingAlgorithm} description={local.scalingAlgorithmDesc}>
            <ProfileDropdown label={local.scalingAlgorithm} description={local.scalingAlgorithmDesc} value={s.scaling_type} options={["Off", "LS1", "FSR", "NIS", "SGSR", "BCAS", "Anime4K", "xBR", "SharpBilinear", "Integer", "NearestNeighbor"]} onChange={(v) => updateSetting("scaling_type", v)} />
            <ProfileDropdown label={local.scalingMode} description={local.scalingModeDesc} value={s.scaling_mode} options={["Auto", "Custom"]} onChange={(v) => updateSetting("scaling_mode", v)} />
            <ProfileDropdown label={local.fitMode} value={s.scaling_fit_mode} options={["AspectRatio", "Fullscreen"]} onChange={(v) => updateSetting("scaling_fit_mode", v)} />
            <ProfileSlider label={local.scaleFactor} value={s.scale_factor} min={1} max={5} step={0.1} suffix="x" onChange={(v) => updateSetting("scale_factor", v)} />
            {s.scaling_type === "FSR" && <ProfileDropdown label={local.fsrVariant} value={s.fsr_type} options={["ORIGINAL", "OPTIMIZED"]} onChange={(v) => updateSetting("fsr_type", v)} />}
            {s.scaling_type === "LS1" && <ProfileDropdown label={local.ls1Variant} value={s.ls1_type} options={["BALANCED", "PERFORMANCE"]} onChange={(v) => updateSetting("ls1_type", v)} />}
            {s.scaling_type === "Anime4K" && <ProfileDropdown label={local.anime4kSize} value={s.anime4k_type} options={["S", "M", "L", "VL", "UL"]} onChange={(v) => updateSetting("anime4k_type", v)} />}
            <ProfileSlider label={local.sharpness} value={s.sharpness} min={0} max={10} step={0.1} onChange={(v) => updateSetting("sharpness", v)} />
            <ProfileSlider label={local.ls1Sharpness} value={s.ls1_sharpness} min={0} max={10} step={0.1} onChange={(v) => updateSetting("ls1_sharpness", v)} />
            <ToggleField label={local.resizeBeforeScaling} description={local.resizeBeforeScalingDesc} checked={s.resize_before_scaling} onChange={(v: boolean) => updateSetting("resize_before_scaling", v)} />
            <ToggleField label={local.windowedMode} description={local.windowedModeDesc} checked={s.windowed_mode} onChange={(v: boolean) => updateSetting("windowed_mode", v)} />
            <ToggleField label={local.variableRateShading} description={local.variableRateShadingDesc} checked={s.vrs} onChange={(v: boolean) => updateSetting("vrs", v)} />
          </ProfileCard>

          <ProfileCard icon={<FaCropAlt />} title={local.gpuAndDisplay} description={local.gpuAndDisplayDesc}>
            <ProfileSlider label={local.preferredGpu} description={local.preferredGpuDesc} value={s.preferred_gpu_id} min={0} max={16} onChange={(v) => updateSetting("preferred_gpu_id", v)} />
            <ProfileSlider label={local.outputDisplay} description={local.outputDisplayDesc} value={s.output_display_id} min={0} max={16} onChange={(v) => updateSetting("output_display_id", v)} />
          </ProfileCard>
          </Focusable>

          <Focusable flow-children="vertical" className="qspColumn">
          <ProfileCard icon={<FaMagic />} title={local.losslessFrameGen} description={local.frameGenerationDesc}>
            <ProfileDropdown label={local.frameGenMode} description={local.frameGenModeDesc} value={s.frame_generation} options={["Off", "LSFG1", "LSFG2", "LSFG3"]} onChange={(v) => updateSetting("frame_generation", v)} />
            {s.frame_generation === "LSFG2" && <ProfileDropdown label={local.frameGenMode} value={s.lsfg2_mode} options={["X2", "X3", "X4"]} onChange={(v) => updateSetting("lsfg2_mode", v)} />}
            {s.frame_generation === "LSFG3" && <>
              <ProfileDropdown label={local.frameGenMode} value={s.lsfg3_mode} options={["FIXED", "ADAPTIVE"]} onChange={(v) => updateSetting("lsfg3_mode", v)} />
              <ProfileSlider label={local.losslessMultiplier} value={s.lsfg3_multiplier} min={2} max={4} suffix="x" onChange={(v) => updateSetting("lsfg3_multiplier", v)} />
              <ProfileSlider label={local.targetFps} description={local.targetFpsDesc} value={s.lsfg3_target} min={30} max={360} step={5} onChange={(v) => updateSetting("lsfg3_target", v)} />
            </>}
            <ProfileSlider label={local.flowScale} value={s.lsfg_flow_scale} min={25} max={100} step={5} suffix="%" onChange={(v) => updateSetting("lsfg_flow_scale", v)} />
            <ProfileDropdown label={local.performanceProfile} value={s.lsfg_size} options={["PERFORMANCE", "BALANCED"]} onChange={(v) => updateSetting("lsfg_size", v)} />
          </ProfileCard>

          <ProfileCard className="qspRenderingCard" icon={<FaDesktop />} title={local.rendering} description={local.renderingDesc}>
            <ProfileDropdown label={local.syncMode} description={local.syncModeDesc} value={s.sync_mode} options={["OFF", "DEFAULT", "VSYNC1", "VSYNC2", "VSYNC3", "VSYNC4"]} onChange={(v) => updateSetting("sync_mode", v)} />
            <ProfileSlider label={local.maxFrameLatency} description={local.maxFrameLatencyDesc} value={s.max_frame_latency} min={0} max={4} onChange={(v) => updateSetting("max_frame_latency", v)} />
            <ProfileDropdown label={local.captureApi} description={local.captureApiDesc} value={s.capture_api} options={["DXGI", "WGC", "GDI"]} onChange={(v) => updateSetting("capture_api", v)} />
            <ProfileSlider label={local.queueTarget} value={s.queue_target} min={0} max={4} onChange={(v) => updateSetting("queue_target", v)} />
            <ToggleField label={local.gsyncSupport} checked={s.gsync_support} onChange={(v: boolean) => updateSetting("gsync_support", v)} />
            <ToggleField label={local.hdrPassthrough} description={local.hdrPassthroughDesc} checked={s.hdr_support} onChange={(v: boolean) => updateSetting("hdr_support", v)} />
            <ToggleField label={local.drawFps} description={local.drawFpsDesc} checked={s.draw_fps} onChange={(v: boolean) => updateSetting("draw_fps", v)} />
          </ProfileCard>

          </Focusable>
        </Focusable>

          <div className="qspWide">
          <ProfileCard icon={<FaMousePointer />} title={local.cursor} description={local.cursorDesc}>
            <ToggleField label={local.clipCursor} checked={s.clip_cursor} onChange={(v: boolean) => updateSetting("clip_cursor", v)} />
            <ToggleField label={local.adjustCursorSpeed} checked={s.adjust_cursor_speed} onChange={(v: boolean) => updateSetting("adjust_cursor_speed", v)} />
            <ToggleField label={local.hideCursor} checked={s.hide_cursor} onChange={(v: boolean) => updateSetting("hide_cursor", v)} />
            <ToggleField label={local.scaleCursor} checked={s.scale_cursor} onChange={(v: boolean) => updateSetting("scale_cursor", v)} />
            <ToggleField label={local.multiDisplayMode} description={local.multiDisplayModeDesc} checked={s.multi_display_mode} onChange={(v: boolean) => updateSetting("multi_display_mode", v)} />
            <ToggleField label={local.cropInput} description={local.cropInputDesc} checked={s.crop_input} onChange={(v: boolean) => updateSetting("crop_input", v)} />
            {s.crop_input && <>
              <ProfileSlider label={local.cropLeft} value={s.crop_input_left} min={0} max={4096} step={4} onChange={(v) => updateSetting("crop_input_left", v)} />
              <ProfileSlider label={local.cropTop} value={s.crop_input_top} min={0} max={4096} step={4} onChange={(v) => updateSetting("crop_input_top", v)} />
              <ProfileSlider label={local.cropRight} value={s.crop_input_right} min={0} max={4096} step={4} onChange={(v) => updateSetting("crop_input_right", v)} />
              <ProfileSlider label={local.cropBottom} value={s.crop_input_bottom} min={0} max={4096} step={4} onChange={(v) => updateSetting("crop_input_bottom", v)} />
            </>}
          </ProfileCard>
          </div>

          <div className="qspWide">
            <ProfileCard icon={<FaMicrochip />} title={local.amdGameProfile} description={local.amdGameProfileDesc}>
              {amd?.available ? (
                <ToggleField
                  label={local.amdGameProfileToggle}
                  description={local.amdGameProfileToggleDesc}
                  checked={profile.amd_auto_enabled}
                  onChange={(amd_auto_enabled: boolean) => queueSave({ ...profile, amd_auto_enabled })}
                />
              ) : (
                <div className="qspHint">{amd?.message || local.amdGameProfileUnavailable}</div>
              )}
            </ProfileCard>
          </div>

          {amd?.available && profile.amd_auto_enabled && (
            <ProfileCard icon={<FaMagic />} title={local.amdFrameAndLatency}>
              {amd.afmf?.supported && <ToggleField label={local.amdAfmf} description={local.amdAfmfHint} checked={a.afmf} onChange={(v: boolean) => updateAmdSetting("afmf", v)} />}
              {amd.antilag?.supported && <ToggleField label={local.amdAntilag} description={local.amdAntilagHint} checked={a.antilag} onChange={(v: boolean) => updateAmdSetting("antilag", v)} />}
              {amd.enhanced_sync?.supported && <ToggleField label={local.amdEnhancedSync} description={local.amdEnhancedSyncHint} checked={a.enhanced_sync} onChange={(v: boolean) => updateAmdSetting("enhanced_sync", v)} />}
              {amd.chill?.supported && <ToggleField label={local.amdChill} description={local.amdChillHint} checked={a.chill} onChange={(v: boolean) => updateAmdSetting("chill", v)} />}
              {amd.chill?.supported && a.chill && <>
                <ProfileSlider label={local.amdChillMin} value={a.chill_min} min={amd.chill.fmin ?? 30} max={amd.chill.fmax ?? 300} onChange={(v) => updateAmdSetting("chill_min", Math.min(v, a.chill_max))} />
                <ProfileSlider label={local.amdChillMax} value={a.chill_max} min={amd.chill.fmin ?? 30} max={amd.chill.fmax ?? 300} onChange={(v) => updateAmdSetting("chill_max", Math.max(v, a.chill_min))} />
              </>}
            </ProfileCard>
          )}

          {amd?.available && profile.amd_auto_enabled && (
            <ProfileCard icon={<FaImage />} title={local.amdScalingAndImage}>
              {amd.rsr?.supported && <ToggleField label={local.amdRsr} description={local.amdRsrHint} checked={a.rsr} onChange={(v: boolean) => updateAmdSetting("rsr", v)} />}
              {amd.rsr?.supported && a.rsr && <ProfileSlider label={local.amdRsrSharpness} value={a.rsr_sharpness} min={amd.rsr.smin ?? 0} max={amd.rsr.smax ?? 100} suffix="%" onChange={(v) => updateAmdSetting("rsr_sharpness", v)} />}
              {amd.sharpening?.supported && <ToggleField label={local.amdSharpening} description={local.amdSharpeningHint} checked={a.sharpening} onChange={(v: boolean) => updateAmdSetting("sharpening", v)} />}
              {amd.sharpening?.supported && a.sharpening && <ProfileSlider label={local.amdSharpeningValue} value={a.sharpening_value} min={amd.sharpening.smin ?? 0} max={amd.sharpening.smax ?? 100} step={amd.sharpening.step ?? 10} suffix="%" onChange={(v) => updateAmdSetting("sharpening_value", v)} />}
              {amd.boost?.supported && <ToggleField label={local.amdBoost} description={local.amdBoostHint} checked={a.boost} onChange={(v: boolean) => updateAmdSetting("boost", v)} />}
              {amd.boost?.supported && a.boost && <ProfileSlider label={local.amdBoostResolution} value={a.boost_resolution} min={amd.boost.rmin ?? 50} max={amd.boost.rmax ?? 100} suffix="%" onChange={(v) => updateAmdSetting("boost_resolution", v)} />}
            </ProfileCard>
          )}
      </Focusable>
    </Focusable>
  );
}

const extractAppId = (...values: any[]): number | null => {
  for (const value of values) {
    const raw = value?.appid ?? value?.app_id ?? value?.appId ?? value?.unAppID ?? value;
    const parsed = Number.parseInt(String(raw ?? ""), 10);
    if (Number.isFinite(parsed) && parsed > 0 && parsed !== LOSSLESS_APP_ID) return parsed;
  }
  return null;
};

const collectRunningAppId = (candidate: any, visited = new Set<any>()): number | null => {
  if (candidate == null) return null;
  const direct = extractAppId(candidate);
  if (direct) return direct;
  if (typeof candidate !== "object" || visited.has(candidate)) return null;
  visited.add(candidate);
  if (Array.isArray(candidate)) {
    for (const item of candidate) {
      const found = collectRunningAppId(item, visited);
      if (found) return found;
    }
    return null;
  }
  if (candidate instanceof Map || candidate instanceof Set) {
    for (const item of candidate.values()) {
      const found = collectRunningAppId(item, visited);
      if (found) return found;
    }
  }
  for (const key of ["runningApps", "running_apps", "apps", "games", "rgRunningApps", "m_mapRunningApps"]) {
    if (key in candidate) {
      const found = collectRunningAppId(candidate[key], visited);
      if (found) return found;
    }
  }
  return null;
};

async function readRunningGame(): Promise<number> {
  const apps = (window as any)?.SteamClient?.Apps;
  const stores = [(window as any)?.appStore, Router as any, (Router as any)?.WindowStore];
  for (const owner of [apps, ...stores]) {
    for (const method of ["GetRunningApps", "GetRunningAppList", "GetCurrentlyRunningApp", "GetRunningGameID", "GetCurrentGameID"]) {
      if (typeof owner?.[method] !== "function") continue;
      try {
        const found = collectRunningAppId(await Promise.resolve(owner[method]()));
        if (found) return found;
      } catch {
        // Keep probing.
      }
    }
  }
  for (const candidate of [
    apps?.m_mapRunningApps,
    apps?.m_runningApps,
    (window as any)?.appStore?.m_mapRunningApps,
    (Router as any)?.MainRunningApp,
    (Router as any)?.RunningApp,
    (Router as any)?.WindowStore?.m_mapRunningApps,
  ]) {
    const found = collectRunningAppId(candidate);
    if (found) return found;
  }
  return 0;
}

let runtimePoll: number | undefined;
let runtimeSubscriptions: Array<() => void> = [];
let lastRuntimeAppId = -1;
let lastRuntimeReportAt = 0;
let runtimeProbeInFlight = false;

const wrapUnsubscribe = (token: any): (() => void) | null => {
  if (typeof token === "function") return token;
  if (typeof token?.unregister === "function") return () => token.unregister();
  if (typeof token?.Unregister === "function") return () => token.Unregister();
  return null;
};

async function reconcileRuntime(source: string, confirmedAppId = 0) {
  if (runtimeProbeInFlight) return;
  runtimeProbeInFlight = true;
  try {
    const appId = (await readRunningGame()) || extractAppId(confirmedAppId) || 0;
    const now = Date.now();
    if (appId === lastRuntimeAppId && now - lastRuntimeReportAt < RUNTIME_HEARTBEAT_MS) return;
    const title = appId ? await getGameTitle(appId) : "";
    lastRuntimeAppId = appId;
    lastRuntimeReportAt = now;
    const uiModeValue = await Promise.resolve(
      (window as any)?.SteamClient?.UI?.GetUIMode?.() ?? -1,
    );
    const uiMode = Number.isFinite(Number(uiModeValue)) ? Number(uiModeValue) : -1;
    await reportGameRuntime(appId, title, uiMode, source);
  } finally {
    runtimeProbeInFlight = false;
  }
}

function startRuntimeWatcher() {
  const apps = (window as any)?.SteamClient?.Apps;
  const register = (method: string, useConfirmedAppId = false, clearsRuntime = false) => {
    if (typeof apps?.[method] !== "function") return;
    try {
      const callback = (...args: any[]) => {
        const eventAppId = collectRunningAppId(args) ?? 0;
        const booleanState = args.find((value) => typeof value === "boolean");
        const stopped = clearsRuntime || booleanState === false;
        const confirmed = useConfirmedAppId && !stopped ? eventAppId : 0;
        window.setTimeout(
          () => void reconcileRuntime(method, confirmed),
          stopped ? 300 : 150,
        );
      };
      const clean = wrapUnsubscribe(apps[method](callback));
      if (clean) runtimeSubscriptions.push(clean);
    } catch {
      // The bounded poll remains the safety path for unsupported Steam builds.
    }
  };

  for (const method of [
    "RegisterForRunningAppsChanged",
    "RegisterForRunningAppChanges",
    "RegisterForGameActionStart",
    "RegisterForGameActionEnd",
  ]) {
    register(method);
  }
  register("RegisterForAppRunningStateChanged", true);
  register("RegisterForGameLaunched", true);
  register("RegisterForGameExited", false, true);
  runtimePoll = window.setInterval(() => void reconcileRuntime("poll"), RUNTIME_POLL_MS);
  void reconcileRuntime("startup");
}

function stopRuntimeWatcher() {
  window.clearInterval(runtimePoll);
  runtimePoll = undefined;
  runtimeSubscriptions.splice(0).forEach((clean) => {
    try { clean(); } catch { /* ignore */ }
  });
  lastRuntimeAppId = -1;
}

const coerceChildren = (children: any): any[] | null => {
  if (!children) return null;
  if (Array.isArray(children)) return children;
  if (Array.isArray(children?.props?.children)) return children.props.children;
  if (Array.isArray(children?.children)) return children.children;
  return null;
};

const extractAppIdFromTree = (node: any): number | null => {
  if (!node) return null;
  const direct = extractAppId(
    node?.appid,
    node?.overview?.appid,
    node?.app?.appid,
    node?.app_id,
    node?._owner?.pendingProps?.overview?.appid,
    node?.props?.overview?.appid,
    node?.props?.app?.appid,
    node?.props?.appid,
    node?.props?.app_id,
  );
  if (direct) return direct;
  const children = node?.children ?? node?.props?.children;
  if (Array.isArray(children)) {
    for (const child of children) {
      const found = extractAppIdFromTree(child);
      if (found) return found;
    }
    return null;
  }
  return extractAppIdFromTree(children);
};

const isGameContextMenu = (items: any[]): boolean => {
  if (!items.length) return false;
  return Boolean(findInReactTree(items, (node: any) => {
    const handlers = [
      node?.props?.onSelected,
      node?.props?.onClick,
      node?.onSelected,
      node?.onClick,
    ]
      .filter((handler) => typeof handler === "function")
      .map((handler) => handler.toString())
      .join("\n");
    return (
      handlers.includes("launchSource") ||
      handlers.includes("PlayGame") ||
      handlers.includes("Launch") ||
      handlers.includes("AppProperties") ||
      handlers.includes("ShowAppProperties") ||
      handlers.includes("InstallApp") ||
      handlers.includes("Download")
    );
  }));
};

const deriveAppIdFromMenu = (items: any[], fallbackAppId: number | null): number | null => {
  const ownerEntry = items.find((entry) => entry?._owner?.pendingProps?.overview?.appid);
  const ownerAppId = extractAppId(ownerEntry?._owner?.pendingProps?.overview?.appid);
  if (ownerAppId) return ownerAppId;
  const appNode = findInTree(
    items,
    (node: any) =>
      node?.overview?.appid ??
      node?.props?.overview?.appid ??
      node?.app?.appid ??
      node?.props?.app?.appid ??
      node?.appid ??
      node?.props?.appid ??
      node?.app_id ??
      node?.props?.app_id,
    { walkable: ["props", "children", "_owner", "pendingProps"] },
  );
  return extractAppId(
    appNode?.overview?.appid,
    appNode?.props?.overview?.appid,
    appNode?.app?.appid,
    appNode?.props?.app?.appid,
    appNode?.appid,
    appNode?.props?.appid,
    appNode?.app_id,
    appNode?.props?.app_id,
    fallbackAppId,
  );
};

function GameSettingsMenuLabel() {
  const [language, setLanguage] = useState(() => (window as any).g_strLanguage || document.documentElement.lang || navigator.language);
  useEffect(() => {
    let active = true;
    Promise.resolve().then(() => (window as any).SteamClient?.Settings?.GetCurrentLanguage?.())
      .then(value => { if (active && typeof value === "string" && value) setLanguage(value); })
      .catch(() => {});
    return () => { active = false; };
  }, []);
  return <>{gameSettingsLabel(language)}</>;
}

function patchMenuItems(children: any, fallbackAppId: number | null): number | null {
  const items = coerceChildren(children);
  if (!items?.length || !isGameContextMenu(items)) return null;
  const appId = deriveAppIdFromMenu(items, fallbackAppId);
  if (!appId) return null;
  const existing = items.findIndex((item) => item?.key === "quick-settings-game-profile");
  if (existing >= 0) items.splice(existing, 1);
  const propertiesIndex = items.findIndex((item) =>
    Boolean(findInReactTree(item, (node: any) => {
      const handler = node?.onSelected ?? node?.props?.onSelected;
      return typeof handler === "function" && (
        handler.toString().includes("AppProperties") ||
        handler.toString().includes("ShowAppProperties")
      );
    })),
  );
  const openProfile = () => {
    try { Navigation.CloseSideMenus?.(); } catch { /* ignore */ }
    window.setTimeout(() => {
      try { Navigation.CloseSideMenus?.(); } catch { /* ignore */ }
      navigateToQuickSettingsGamePage(`/quick-settings/${appId}`);
      window.setTimeout(() => { try { Navigation.CloseSideMenus?.(); } catch {} }, 0);
      window.setTimeout(() => { try { Navigation.CloseSideMenus?.(); } catch {} }, 120);
      window.setTimeout(() => { try { Navigation.CloseSideMenus?.(); } catch {} }, 300);
    }, 40);
  };
  const menu = (
    <MenuItem
      key="quick-settings-game-profile"
      onSelected={openProfile}
    >
      <GameSettingsMenuLabel />
    </MenuItem>
  );
  const grouped = insertPluginSection(MenuReact, items, menu);
  if (grouped !== items) items.splice(0, items.length, ...grouped);
  return appId;
}

function patchGameContextMenu(): (() => void) | null {
  try {
    const module = findModuleByExport(
      (exported: Export) => exported?.toString && exported.toString().includes("().LibraryContextMenu"),
    );
    const candidate = Object.values(module ?? {}).find((value: any) => value?.toString?.().includes("navigator:"));
    const component = fakeRenderComponent(candidate as any);
    const MenuComponent: any = component?.type ?? candidate;
    if (!MenuComponent?.prototype?.render) return null;
    const state: { appId: number | null } = { appId: null };
    const patches: { outer?: Patch; inner?: Patch; render?: Patch; update?: Patch } = {};
    const outerPatch = afterPatch(
      MenuComponent.prototype,
      "render",
      function (this: any, _args: any[], rendered: any) {
        const fallback = extractAppId(
          rendered?._owner?.pendingProps?.overview?.appid,
          extractAppIdFromTree(rendered?.props?.children),
        );
        if (fallback) state.appId = fallback;
        if (!patches.inner && rendered?.type) {
          patches.inner = afterPatch(rendered, "type", (_typeArgs: any[], inner: any) => {
            const prototype = inner?.type?.prototype;
            if (prototype?.render && !patches.render) {
              patches.render = afterPatch(prototype, "render", (_renderArgs: any[], tree: any) => {
                const currentAppId = extractAppIdFromTree(tree) ?? state.appId;
                const patchedAppId = patchMenuItems(
                  tree?.props?.children?.[0] ?? tree?.props?.children,
                  currentAppId,
                );
                if (patchedAppId) state.appId = patchedAppId;
                return tree;
              });
            }
            if (prototype?.shouldComponentUpdate && !patches.update) {
              patches.update = afterPatch(
                prototype,
                "shouldComponentUpdate",
                ([nextProps]: any[], shouldUpdate: any) => {
                  if (shouldUpdate === true) {
                    const currentAppId = extractAppIdFromTree(nextProps?.children) ?? state.appId;
                    const patchedAppId = patchMenuItems(nextProps?.children, currentAppId);
                    if (patchedAppId) state.appId = patchedAppId;
                  }
                  return shouldUpdate;
                },
              );
            }
            return inner;
          });
        } else {
          const patchedAppId = patchMenuItems(rendered?.props?.children, fallback ?? state.appId);
          if (patchedAppId) state.appId = patchedAppId;
        }
        return rendered;
      },
    );
    patches.outer = outerPatch;
    return () => {
      // Other context-menu plugins can remove a shared patch target first.
      // Release every remaining handle even when Decky has already retired one.
      for (const key of ["update", "render", "inner", "outer"] as const) {
        const patch = patches[key];
        delete patches[key];
        try { patch?.unpatch(); } catch (error) {
          console.debug("[Playhub] Context menu patch already removed", key, error);
        }
      }
    };
  } catch (error) {
    console.error("[Quick Settings] context menu patch failed", error);
    return null;
  }
}

export function installLosslessGameIntegration(): () => void {
  routerHook.addRoute(ROUTE, () => <LosslessGamePage />, { exact: true });
  startRuntimeWatcher();
  let unpatch: (() => void) | null = null;
  const patchTimer = window.setTimeout(() => {
    unpatch = patchGameContextMenu();
  }, 12000);
  return () => {
    window.clearTimeout(patchTimer);
    stopRuntimeWatcher();
    unpatch?.();
    try { routerHook.removeRoute(ROUTE); } catch { /* ignore */ }
  };
}
