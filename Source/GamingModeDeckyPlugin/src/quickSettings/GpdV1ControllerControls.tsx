import { ButtonItem, DropdownItem } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale, deviceLanguage } from "../deviceInfoLocale";

const words = {
  en: ["Vibration", "Off", "Low", "High", "Apply", "Restore previous settings", "Current", "Device state changed. Review the current settings."],
  it: ["Vibrazione", "Spenta", "Bassa", "Alta", "Applica", "Ripristina le impostazioni precedenti", "Attuale", "Lo stato del dispositivo è cambiato. Controlla le impostazioni attuali."],
  de: ["Vibration", "Aus", "Niedrig", "Hoch", "Anwenden", "Vorherige Einstellungen wiederherstellen", "Aktuell", "Der Gerätestatus hat sich geändert. Prüfe die aktuellen Einstellungen."],
  es: ["Vibración", "Apagada", "Baja", "Alta", "Aplicar", "Restaurar los ajustes anteriores", "Actual", "El estado del dispositivo ha cambiado. Revisa los ajustes actuales."],
  fr: ["Vibrations", "Désactivé", "Faible", "Fort", "Appliquer", "Rétablir les réglages précédents", "Actuel", "L’état de l’appareil a changé. Vérifiez les réglages actuels."],
  pt: ["Vibração", "Desligada", "Baixa", "Alta", "Aplicar", "Restaurar as definições anteriores", "Atual", "O estado do dispositivo mudou. Reveja as definições atuais."],
  ru: ["Вибрация", "Выкл.", "Низкая", "Высокая", "Применить", "Восстановить предыдущие настройки", "Сейчас", "Состояние устройства изменилось. Проверьте текущие настройки."],
  uk: ["Вібрація", "Вимкнено", "Низька", "Висока", "Застосувати", "Відновити попередні налаштування", "Поточне", "Стан пристрою змінився. Перевірте поточні налаштування."],
  ja: ["振動", "オフ", "弱", "強", "適用", "以前の設定に戻す", "現在", "デバイスの状態が変わりました。現在の設定を確認してください。"],
  ko: ["진동", "끄기", "약하게", "강하게", "적용", "이전 설정 복원", "현재", "장치 상태가 변경되었습니다. 현재 설정을 확인하세요."],
  zh: ["振动", "关闭", "低", "高", "应用", "恢复之前的设置", "当前", "设备状态已更改。请检查当前设置。"],
  hi: ["कंपन", "बंद", "कम", "तेज़", "लागू करें", "पिछली सेटिंग बहाल करें", "वर्तमान", "डिवाइस की स्थिति बदल गई है। वर्तमान सेटिंग जाँचें।"],
} as const;
export const rumbleLevels = ["off", "low", "high"] as const;
type Rumble = typeof rumbleLevels[number];
export type GpdSnapshot = {
  ok: true; available: true; provider: "gpd-v1-hid"; profile: "gpd-win-4" | "gpd-win-mini";
  token: string; writeAvailable: boolean; restoreAvailable: boolean; recoveryRequired: boolean;
  readbackKind: "gpd-v1-config128-checksum-double-read"; state: { rumble: Rumble };
  firmware: { x: number[]; k: number[] }; experimental: true; hardwareValidated: false; verified?: boolean;
};
type Reply = GpdSnapshot | { ok: false; reason?: string };
type View = { snapshot?: GpdSnapshot; draft?: Rumble; editing: boolean; busy: boolean; error?: "failed" | "changed" };
type Services = { read(): Promise<Reply>; apply(request: { token: string; rumble: Rumble }): Promise<Reply>;
  restore(request: { token: string }): Promise<Reply>; visible(): boolean; changed(view: View): void };
export function validGpdSnapshot(v: any, profile?: string): v is GpdSnapshot {
  const k = v?.profile === "gpd-win-4" ? [4, 7] : v?.profile === "gpd-win-mini" ? [5, 4] : undefined;
  return !!v && v.ok === true && v.available === true && v.provider === "gpd-v1-hid"
    && !!k && (!profile || v.profile === profile) && typeof v.token === "string" && /^[a-f0-9]{64}$/.test(v.token)
    && v.readbackKind === "gpd-v1-config128-checksum-double-read" && v.experimental === true && v.hardwareValidated === false
    && typeof v.writeAvailable === "boolean" && typeof v.restoreAvailable === "boolean" && typeof v.recoveryRequired === "boolean"
    && !(v.recoveryRequired && (v.writeAvailable || v.restoreAvailable)) && !!v.state && rumbleLevels.includes(v.state.rumble)
    && Array.isArray(v.firmware?.k) && v.firmware.k.length === 2 && v.firmware.k.every((x: any, i: number) => Number.isInteger(x) && x === k[i])
    && Array.isArray(v.firmware?.x) && v.firmware.x.length === 2 && v.firmware.x.every((x: any) => Number.isInteger(x) && x >= 0 && x <= 255);
}
export class GpdControlModel {
  view: View = { editing: false, busy: false };
  private stopped = false;
  private revision = 0;
  private reading?: Promise<Reply>;
  constructor(private services: Services, private profile: string) {}
  private publish() { if (!this.stopped) this.services.changed({ ...this.view }); }
  private async read() {
    if (!this.reading) {
      const pending = this.services.read(); this.reading = pending;
      void pending.finally(() => { if (this.reading === pending) this.reading = undefined; }).catch(() => {});
    }
    return this.reading;
  }
  private accept(snapshot: GpdSnapshot) { this.view.snapshot = snapshot; this.view.draft = snapshot.state.rumble; this.view.editing = false; }
  async refresh() {
    if (this.stopped || this.view.busy || !this.services.visible()) return;
    const revision = this.revision;
    try {
      const reply = await this.read();
      if (this.stopped || revision !== this.revision || !this.services.visible()) return;
      if (!validGpdSnapshot(reply, this.profile)) this.view = { editing: false, busy: false, error: "failed" };
      else if (!this.view.editing || this.view.snapshot?.token !== reply.token) {
        const changed = this.view.editing; this.accept(reply); this.view.error = changed ? "changed" : undefined;
      } else this.view.snapshot = reply;
    } catch { if (!this.stopped && revision === this.revision) this.view = { editing: false, busy: false, error: "failed" }; }
    this.publish();
  }
  edit(value: Rumble) {
    if (this.stopped || this.view.busy || !this.services.visible() || !this.view.snapshot?.writeAvailable || this.view.snapshot.recoveryRequired || !rumbleLevels.includes(value)) return;
    this.view.draft = value; this.view.editing = value !== this.view.snapshot.state.rumble; this.view.error = undefined; this.publish();
  }
  async run(action: "apply" | "restore") {
    const initial = this.view.snapshot, draft = this.view.draft;
    if (this.stopped || this.view.busy || !initial || !draft || !this.services.visible() || initial.recoveryRequired
      || (action === "apply" ? !initial.writeAvailable || !this.view.editing : !initial.restoreAvailable)) return;
    this.view.busy = true; this.view.error = undefined; const revision = ++this.revision; this.publish();
    const current = () => !this.stopped && revision === this.revision && this.services.visible();
    try {
      if (this.reading) await this.reading.catch(() => {});
      if (!current()) return;
      const fresh = await this.read();
      if (!current()) return;
      if (!validGpdSnapshot(fresh, this.profile) || fresh.token !== initial.token) {
        if (validGpdSnapshot(fresh, this.profile)) this.accept(fresh); else { this.view.snapshot = undefined; this.view.draft = undefined; this.view.editing = false; }
        this.view.error = "changed"; return;
      }
      if (fresh.recoveryRequired || (action === "apply" ? !fresh.writeAvailable : !fresh.restoreAvailable)) { this.accept(fresh); this.view.error = "failed"; return; }
      const result = action === "apply" ? await this.services.apply({ token: fresh.token, rumble: draft }) : await this.services.restore({ token: fresh.token });
      if (!current()) return;
      const actual = await this.read();
      if (!current()) return;
      if (!validGpdSnapshot(actual, this.profile)) { this.view = { editing: false, busy: false, error: "failed" }; return; }
      this.accept(actual);
      this.view.error = validGpdSnapshot(result, this.profile) && result.verified === true && result.token === actual.token && result.state.rumble === actual.state.rumble ? undefined : "failed";
    } catch { if (current()) this.view = { editing: false, busy: false, error: "failed" }; }
    finally { this.view.busy = false; this.publish(); }
  }
  dispose() { this.stopped = true; this.revision++; }
}
export function GpdV1ControllerControls({ locale, deviceKey, profile, active = true }: { locale: string; deviceKey: string; profile: string; active?: boolean }) {
  const copy = words[deviceLanguage(locale)], common = controlLocale(locale), device = deviceInfoLocale(locale);
  const [view, setView] = useState<View>({ editing: false, busy: false });
  const model = useRef<GpdControlModel | undefined>(undefined);
  useEffect(() => {
    let stopped = false, timer: number | undefined;
    setView({ editing: false, busy: false });
    const owner = new GpdControlModel({ read: () => call<[], Reply>("get_gpd_v1_controller"),
      apply: request => call<[{ token: string; rumble: Rumble }], Reply>("set_gpd_v1_controller", request),
      restore: request => call<[{ token: string }], Reply>("restore_gpd_v1_controller", request),
      visible: () => active && document.visibilityState !== "hidden", changed: next => { if (!stopped) setView(next); } }, profile);
    model.current = owner;
    const refresh = async () => { window.clearTimeout(timer); await owner.refresh(); window.clearTimeout(timer); if (!stopped) timer = window.setTimeout(() => void refresh(), 1500); };
    const changed = () => { window.clearTimeout(timer); void refresh(); };
    void refresh(); window.addEventListener("focus", changed); document.addEventListener("visibilitychange", changed);
    return () => { stopped = true; owner.dispose(); if (model.current === owner) model.current = undefined; window.clearTimeout(timer);
      window.removeEventListener("focus", changed); document.removeEventListener("visibilitychange", changed); };
  }, [deviceKey, profile, active]);
  const snapshot = view.snapshot, disabled = view.busy || !snapshot?.writeAvailable || snapshot.recoveryRequired;
  const label = (level: Rumble) => copy[1 + rumbleLevels.indexOf(level)];
  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={view.busy}>
    <div className="qsMeta">GPD · {copy[0]} · {device.experimental}</div>
    {snapshot && view.draft ? <>
      <DropdownItem label={copy[0]} layout="below" bottomSeparator="none" selectedOption={view.draft} disabled={disabled}
        rgOptions={rumbleLevels.map(level => ({ data: level, label: label(level) }))} onChange={({ data }) => { if (rumbleLevels.includes(data)) model.current?.edit(data); }} />
      <div className="qsMeta">{copy[6]}: {label(snapshot.state.rumble)}</div>
      {view.editing && <ButtonItem layout="below" bottomSeparator="none" disabled={disabled} onClick={() => void model.current?.run("apply")}>{copy[4]}</ButtonItem>}
      {snapshot.restoreAvailable && <ButtonItem layout="below" bottomSeparator="none" disabled={view.busy || snapshot.recoveryRequired} onClick={() => void model.current?.run("restore")}>{copy[5]}</ButtonItem>}
    </> : <div className="qsMeta" role="status">{common.unavailable}</div>}
    {(view.error || snapshot?.recoveryRequired) && <div className="qsMeta" role="status">{view.error === "changed" ? copy[7] : common.saveError}</div>}
    {(!snapshot || view.error || snapshot.recoveryRequired) && <ButtonItem layout="below" bottomSeparator="none" disabled={view.busy} onClick={() => void model.current?.refresh()}>{common.retry}</ButtonItem>}
  </div>;
}
