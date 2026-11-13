import { TbBattery, TbChevronDown, TbChevronRight, TbDeviceDesktop, TbDeviceGamepad2 } from "react-icons/tb";
import { call } from "./controlBackend";
import { DFL, SP_REACT as React } from "./decky";
import { deviceInfoLocale } from "./deviceInfoLocale";
import { DeviceCapabilities } from "./DeviceCapabilities";
import { Ayaneo3ControllerControls } from "./quickSettings/Ayaneo3ControllerControls";
import { GpdV1ControllerControls } from "./quickSettings/GpdV1ControllerControls";

const { DialogButton, DropdownItem, Focusable } = DFL as any;
type DeviceProfile = {
  selection: string;
  detected: string;
  effective: string;
  manufacturer: string;
  model: string;
  handhelds: { id: string; name: string }[];
  power: { available: boolean; battery?: boolean; percent?: number | null; source?: "ac" | "battery" | null;
    discharging?: boolean; remainingSeconds?: number | null };
  display: { width: number; height: number; hz: number | null } | null;
  powerModeAvailable: boolean;
};
type HardwareDetails = {
  device_name?: string;
  cpu?: string;
  gpu?: string[];
  ram?: number;
  storage_total_bytes?: number;
  windows_edition?: string;
  windows_version?: string;
};
type DeviceCopy = ReturnType<typeof deviceInfoLocale>;

const style = `
.ph-device-settings{display:grid;gap:12px;min-width:0;padding:4px 4px 0;overflow-wrap:anywhere}
.ph-device-identity{display:grid;grid-template-columns:auto minmax(0,1fr);align-items:center;gap:12px;padding:12px;border-radius:9px;background:rgba(255,255,255,.07)}
.ph-device-symbol{display:grid;place-items:center;width:42px;height:42px;border-radius:8px;background:rgba(255,255,255,.09);font-size:22px}
.ph-device-title{display:flex;align-items:baseline;flex-wrap:wrap;gap:4px 8px;font-size:15px;line-height:1.35}
.ph-device-experimental{font-size:10px;font-weight:600;color:#ffe467;background:#2b2819;padding:2px 5px;border-radius:4px}
.ph-device-detail,.ph-device-hint{font-size:12px;line-height:1.45;color:#c5cbd1}
.ph-controls .ph-device-settings .ph-device-hint{margin:0;opacity:1}
.ph-device-status{display:grid;gap:8px;margin:0}
.ph-device-status>div{display:grid;grid-template-columns:auto minmax(0,1fr);align-items:center;gap:8px;padding:0 4px}
.ph-device-battery{display:flex;align-items:center;gap:8px;padding:0 4px;font-size:13px;font-weight:600}
.ph-device-battery>svg{flex:none}
.ph-device-battery-health{color:#c5cbd1;font-size:12px;font-weight:400;line-height:1.45}
.ph-device-status dt{font-size:12px;color:#c5cbd1}.ph-device-status dd{margin:0;font-size:13px;font-weight:600}
.ph-device-actions{display:grid;gap:6px}
.ph-device-settings>.qsCardBody{display:flex;flex-direction:column;gap:10px;min-width:0;max-width:100%;overflow-wrap:anywhere}
.ph-device-settings>.qsCardBody .qsMeta{font-size:12px;line-height:1.45;min-width:0;max-width:100%;overflow-wrap:anywhere}
.ph-controls .ph-device-actions button,.ph-controls button.ph-device-details{display:flex!important;align-items:center;gap:8px;min-width:0;width:100%;white-space:normal!important;text-align:left}
.ph-device-actions svg,.ph-device-details svg{flex:none;width:16px;height:16px}
.ph-device-details span{flex:1;min-width:0}
.ph-device-specs{margin:0 6px;display:grid;gap:9px}.ph-device-specs dt{font-size:12px;color:#c5cbd1}.ph-device-specs dd{margin:1px 0 0;font-size:13px}
`;

function DeviceInfo({ copy, model }: { copy: DeviceCopy; model: string }) {
  const [info, setInfo] = React.useState<HardwareDetails>();
  const [failed, setFailed] = React.useState(false);
  React.useEffect(() => {
    let active = true;
    void call<[], HardwareDetails>("get_device_info").then(value => {
      if (active) setInfo(value);
    }).catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, []);
  if (!info) return <div className="ph-device-detail" role="status">{failed ? copy.loadError : copy.loading}</div>;
  const size = (value: number | undefined, divisor = 1) => value !== undefined && Number.isFinite(value) && value > 0 ? `${(value / divisor).toFixed(1)} GiB` : undefined;
  const rows = [
    [copy.name, info.device_name], [copy.model, model], [copy.cpu, info.cpu], [copy.gpu, info.gpu?.join(", ")],
    [copy.ram, size(info.ram)], [copy.storage, size(info.storage_total_bytes, 1024 ** 3)],
    [copy.edition, info.windows_edition], [copy.version, info.windows_version],
  ];
  return <dl className="ph-device-specs">{rows.map(([label, value]) => <div key={label}>
    <dt>{label}</dt><dd>{value || copy.unavailable}</dd>
  </div>)}</dl>;
}

export function DeviceSettings({ locale, openVideo, openPerformance, detailsOpen = false, onDetailsToggle }: {
  locale: string;
  openVideo?: () => void;
  openPerformance?: () => void;
  detailsOpen?: boolean;
  onDetailsToggle?: () => void;
}) {
  const copy = deviceInfoLocale(locale);
  const [profile, setProfile] = React.useState<DeviceProfile>();
  const [error, setError] = React.useState<"load" | "save">();
  const [saving, setSaving] = React.useState(false);
  const [loadAttempt, setLoadAttempt] = React.useState(0);
  const [batteryHealth, setBatteryHealth] = React.useState<number>();
  const hasBattery = profile?.power.available === true && profile.power.battery === true;
  const active = React.useRef(false);
  const writeInProgress = React.useRef(false);
  React.useEffect(() => {
    active.current = true;
    let latest = true;
    void call<[], DeviceProfile>("get_device_profile").then(value => {
      if (latest) { setProfile(value); setError(undefined); }
    }).catch(() => { if (latest) setError("load"); });
    return () => { latest = false; active.current = false; };
  }, [loadAttempt]);
  React.useEffect(() => {
    if (!hasBattery) { setBatteryHealth(undefined); return; }
    let latest = true;
    void call<[], { percent: number | null }>("get_battery_health").then(({ percent }) => {
      if (latest && typeof percent === "number" && Number.isInteger(percent) && percent >= 0 && percent <= 100) {
        setBatteryHealth(percent);
      }
    }).catch(() => {});
    return () => { latest = false; };
  }, [hasBattery]);
  const select = async ({ data }: { data: string }) => {
    if (writeInProgress.current || data === profile?.selection) return;
    writeInProgress.current = true;
    setSaving(true);
    setError(undefined);
    try {
      const value = await call<[string], DeviceProfile>("set_device_profile", data);
      if (active.current) setProfile(value);
    } catch {
      if (active.current) setError("save");
    } finally {
      writeInProgress.current = false;
      if (active.current) setSaving(false);
    }
  };

  if (!profile) return <Focusable className="ph-device-settings" flow-children="column">
    <style>{style}</style>
    <div className="ph-device-detail" role="status">{error === "load" ? copy.loadError : copy.loading}</div>
    {error === "load" && <DialogButton onClick={() => { setError(undefined); setLoadAttempt(value => value + 1); }}>{copy.retry}</DialogButton>}
  </Focusable>;

  const profileNames = [{ id: "pc", name: copy.pc }, { id: "handheld", name: copy.handheld }, ...profile.handhelds];
  const names = new Map(profileNames.map(({ id, name }) => [id, name]));
  const handheld = profile.effective !== "pc";
  const actualModel = [profile.manufacturer, profile.model].filter(Boolean).join(" ");
  const modelLabel = profile.detected !== "pc" && profile.detected !== profile.effective ? names.get(profile.detected) ?? actualModel : actualModel;
  const display = profile.display;
  const power = profile.power;
  const batteryLabel = power.source === "ac" ? copy.ac : power.source === "battery" ? copy.battery : undefined;
  const battery = hasBattery ? [power.percent == null ? undefined : `${power.percent}%`, batteryLabel].filter(Boolean).join(" · ") : "";
  const health = hasBattery && batteryHealth !== undefined ? `${copy.batteryHealth} · ${batteryHealth}%` : undefined;
  const seconds = power.remainingSeconds;
  const remaining = hasBattery && power.source === "battery" && power.discharging === true
    && typeof seconds === "number" && Number.isInteger(seconds) && seconds >= 0 && seconds < 0xffffffff
    ? `${copy.batteryRemaining} · ${new Intl.NumberFormat(copy.language, {
      style: "unit", unit: "minute", unitDisplay: "short", maximumFractionDigits: 0,
    }).format(Math.ceil(seconds / 60))}` : undefined;

  return <Focusable className="ph-device-settings" flow-children="column">
    <style>{style}</style>
    <div className="ph-device-identity">
      <div className="ph-device-symbol" aria-hidden="true">{handheld ? <TbDeviceGamepad2 /> : <TbDeviceDesktop />}</div>
      <div>
        <div className="ph-device-title"><strong>{names.get(profile.effective) ?? copy.pc}</strong>
          {handheld && <span className="ph-device-experimental">{copy.experimental}</span>}
        </div>
        {modelLabel && <div className="ph-device-detail">{copy.model} · {modelLabel}</div>}
      </div>
    </div>
    <DropdownItem label={copy.profile} layout="below" bottomSeparator="none" selectedOption={profile.selection} disabled={saving}
      rgOptions={[{ label: copy.auto, data: "auto" }, ...profileNames.map(({ id, name }) => ({ label: name, data: id }))]}
      onChange={select} />
    <p className="ph-device-hint">{copy.profileHint}</p>
    {error === "save" && <div className="ph-device-detail" role="alert">{copy.saveError}</div>}
    {(battery || health || remaining) && <div className="ph-device-battery"><TbBattery aria-hidden="true" /><div>
      {battery && <div>{battery}</div>}
      {remaining && <div className="ph-device-battery-health">{remaining}</div>}
      {health && <div className="ph-device-battery-health">{health}</div>}
    </div></div>}
    {display && <dl className="ph-device-status"><div><dt>{copy.primaryDisplay}</dt>
      <dd>{display.width} × {display.height}{display.hz ? ` · ${display.hz} Hz` : ""}</dd></div></dl>}
    <DeviceCapabilities selection={profile.selection} effective={profile.effective} detected={profile.detected} locale={locale} openVideo={openVideo} openPerformance={openPerformance}/>
    {profile.effective === "ayaneo-3" && profile.detected === "ayaneo-3" && <Ayaneo3ControllerControls
      key={JSON.stringify([profile.selection, profile.manufacturer, profile.model])}
      deviceKey={JSON.stringify([profile.selection, profile.manufacturer, profile.model])} locale={locale} active={!saving} />}
    {profile.effective === profile.detected && ["gpd-win-4", "gpd-win-mini"].includes(profile.detected) && <GpdV1ControllerControls
      key={JSON.stringify([profile.selection, profile.manufacturer, profile.model])}
      deviceKey={JSON.stringify([profile.selection, profile.manufacturer, profile.model])} profile={profile.detected} locale={locale} active={!saving} />}
    <DialogButton className="ph-device-details" aria-expanded={detailsOpen} onClick={onDetailsToggle}>
      <span>{copy.details}</span>{detailsOpen ? <TbChevronDown aria-hidden="true" /> : <TbChevronRight aria-hidden="true" />}
    </DialogButton>
    {detailsOpen && <DeviceInfo copy={copy} model={actualModel} />}
  </Focusable>;
}
