import { ButtonItem, ConfirmModal, DropdownItem, SliderField, showModal } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale, deviceLanguage } from "../deviceInfoLocale";

const words = {
  en: ["Lighting and vibration", "Lighting", "Color", "Vibration", "Solid", "Pulse", "Rainbow", "Off", "Low", "Medium", "High", "Apply", "Restore previous settings", "Current", "Left", "Right", "Device state changed. Review the current settings."],
  it: ["Illuminazione e vibrazione", "Illuminazione", "Colore", "Vibrazione", "Fissa", "Pulsazione", "Arcobaleno", "Spenta", "Bassa", "Media", "Alta", "Applica", "Ripristina le impostazioni precedenti", "Attuale", "Sinistra", "Destra", "Lo stato del dispositivo è cambiato. Controlla le impostazioni attuali."],
  de: ["Beleuchtung und Vibration", "Beleuchtung", "Farbe", "Vibration", "Statisch", "Pulsierend", "Regenbogen", "Aus", "Niedrig", "Mittel", "Hoch", "Anwenden", "Vorherige Einstellungen wiederherstellen", "Aktuell", "Links", "Rechts", "Der Gerätestatus hat sich geändert. Prüfe die aktuellen Einstellungen."],
  es: ["Iluminación y vibración", "Iluminación", "Color", "Vibración", "Fija", "Pulsación", "Arcoíris", "Apagada", "Baja", "Media", "Alta", "Aplicar", "Restaurar los ajustes anteriores", "Actual", "Izquierda", "Derecha", "El estado del dispositivo ha cambiado. Revisa los ajustes actuales."],
  fr: ["Éclairage et vibrations", "Éclairage", "Couleur", "Vibrations", "Fixe", "Pulsation", "Arc-en-ciel", "Désactivé", "Faible", "Moyen", "Fort", "Appliquer", "Rétablir les réglages précédents", "Actuel", "Gauche", "Droite", "L’état de l’appareil a changé. Vérifiez les réglages actuels."],
  pt: ["Iluminação e vibração", "Iluminação", "Cor", "Vibração", "Fixa", "Pulsação", "Arco-íris", "Desligada", "Baixa", "Média", "Alta", "Aplicar", "Restaurar as definições anteriores", "Atual", "Esquerda", "Direita", "O estado do dispositivo mudou. Reveja as definições atuais."],
  ru: ["Подсветка и вибрация", "Подсветка", "Цвет", "Вибрация", "Постоянная", "Пульсация", "Радуга", "Выкл.", "Низкая", "Средняя", "Высокая", "Применить", "Восстановить предыдущие настройки", "Сейчас", "Слева", "Справа", "Состояние устройства изменилось. Проверьте текущие настройки."],
  uk: ["Підсвічування та вібрація", "Підсвічування", "Колір", "Вібрація", "Постійне", "Пульсація", "Веселка", "Вимкнено", "Низька", "Середня", "Висока", "Застосувати", "Відновити попередні налаштування", "Поточне", "Ліворуч", "Праворуч", "Стан пристрою змінився. Перевірте поточні налаштування."],
  ja: ["照明と振動", "照明", "色", "振動", "固定", "点滅", "レインボー", "オフ", "弱", "中", "強", "適用", "以前の設定に戻す", "現在", "左", "右", "デバイスの状態が変わりました。現在の設定を確認してください。"],
  ko: ["조명 및 진동", "조명", "색상", "진동", "고정", "맥동", "무지개", "끄기", "약하게", "보통", "강하게", "적용", "이전 설정 복원", "현재", "왼쪽", "오른쪽", "장치 상태가 변경되었습니다. 현재 설정을 확인하세요."],
  zh: ["灯光与振动", "灯光", "颜色", "振动", "常亮", "呼吸", "彩虹", "关闭", "低", "中", "高", "应用", "恢复之前的设置", "当前", "左侧", "右侧", "设备状态已更改。请检查当前设置。"],
  hi: ["रोशनी और कंपन", "रोशनी", "रंग", "कंपन", "स्थिर", "धड़कती", "इंद्रधनुष", "बंद", "कम", "मध्यम", "तेज़", "लागू करें", "पिछली सेटिंग बहाल करें", "वर्तमान", "बायाँ", "दायाँ", "डिवाइस की स्थिति बदल गई है। वर्तमान सेटिंग जाँचें।"],
} as const;
const pickerWords = {
  en: ["Hue", "Saturation", "Lightness"], it: ["Tonalità", "Saturazione", "Luminosità"],
  de: ["Farbton", "Sättigung", "Helligkeit"], es: ["Tono", "Saturación", "Luminosidad"],
  fr: ["Teinte", "Saturation", "Luminosité"], pt: ["Matiz", "Saturação", "Luminosidade"],
  ru: ["Тон", "Насыщенность", "Светлота"], uk: ["Тон", "Насиченість", "Світлість"],
  ja: ["色相", "彩度", "明度"], ko: ["색조", "채도", "명도"],
  zh: ["色相", "饱和度", "明度"], hi: ["रंगत", "संतृप्ति", "हल्कापन"],
} as const;
type Mode = "solid" | "pulse" | "rainbow" | "off";
type Vibration = "low" | "medium" | "high" | "off";
const modes: Mode[] = ["solid", "pulse", "rainbow", "off"];
const vibrationLevels: Vibration[] = ["low", "medium", "high", "off"];
type NativeState = { leftMode: Mode; leftRgb: string; rightMode: Mode; rightRgb: string; vibration: Vibration };
export type AyaneoSnapshot = {
  ok: true; available: true; provider: "ayaneo3-hid"; token: string; state: NativeState;
  writeAvailable: boolean; restoreAvailable: boolean; manualRecoveryRequired: boolean;
  readbackKind: "vendor-hid-status"; modules: { left: number; right: number };
  verified?: boolean;
};
type Reply = Partial<Omit<AyaneoSnapshot, "ok" | "available">> & { ok?: boolean; available?: boolean; verified?: boolean };
type Draft = { rgbMode: Mode; rgbHex: string; vibration: Vibration };
const hex = (value: unknown): value is string => typeof value === "string" && /^[a-f0-9]{6}$/.test(value);
export function validAyaneoSnapshot(value: unknown): value is AyaneoSnapshot {
  const v = value as Partial<AyaneoSnapshot> | null;
  return !!v && v.ok === true && v.available === true && v.provider === "ayaneo3-hid"
    && typeof v.token === "string" && /^[a-f0-9]{64}$/.test(v.token)
    && typeof v.writeAvailable === "boolean" && typeof v.restoreAvailable === "boolean"
    && typeof v.manualRecoveryRequired === "boolean" && !(v.manualRecoveryRequired && v.writeAvailable)
    && v.readbackKind === "vendor-hid-status" && !!v.state && modes.includes(v.state.leftMode)
    && modes.includes(v.state.rightMode) && hex(v.state.leftRgb) && hex(v.state.rightRgb)
    && vibrationLevels.includes(v.state.vibration) && !!v.modules
    && [2, 4, 6, 8, 66, 68, 70, 72].includes(v.modules.left)
    && [16, 18, 20, 22, 80, 82, 84, 86].includes(v.modules.right);
}
const sameState = (a?: NativeState, b?: NativeState) => !!a && !!b && a.leftMode === b.leftMode && a.rightMode === b.rightMode
  && a.leftRgb === b.leftRgb && a.rightRgb === b.rightRgb && a.vibration === b.vibration;
const draftFrom = (snapshot: AyaneoSnapshot): Draft => ({ rgbMode: snapshot.state.leftMode, rgbHex: snapshot.state.leftRgb, vibration: snapshot.state.vibration });
type ModelView = { snapshot?: AyaneoSnapshot; draft?: Draft; editing: boolean; busy: boolean; error?: "failed" | "changed" };
type Services = { read(): Promise<Reply>; apply(request: Record<string, string>): Promise<Reply>;
  restore(request: { token: string }): Promise<Reply>; visible(): boolean; changed(view: ModelView): void };

/** In-process UI lifecycle, using the real component's RPC dependencies. */
export class AyaneoControlModel {
  view: ModelView = { editing: false, busy: false };
  private stopped = false;
  private revision = 0;
  private reading?: Promise<Reply>;
  private edits: Partial<Draft> = {};
  constructor(private services: Services) {}
  private publish() { if (!this.stopped) this.services.changed({ ...this.view }); }
  private async read() {
    if (!this.reading) {
      const pending = this.services.read();
      this.reading = pending;
      void pending.finally(() => { if (this.reading === pending) this.reading = undefined; }).catch(() => {});
    }
    return this.reading;
  }
  private accept(snapshot: AyaneoSnapshot, reset: boolean) {
    this.view.snapshot = snapshot;
    if (reset) { this.view.draft = draftFrom(snapshot); this.view.editing = false; this.edits = {}; }
  }
  async refresh() {
    if (this.stopped || this.view.busy || !this.services.visible()) return;
    const generation = this.revision;
    try {
      const reply = await this.read();
      if (this.stopped || generation !== this.revision || !this.services.visible()) return;
      if (!validAyaneoSnapshot(reply)) { this.view = { editing: false, busy: false, error: "failed" }; }
      else {
        const changed = !!this.view.snapshot && this.view.snapshot.token !== reply.token;
        const edited = this.view.editing;
        this.accept(reply, !edited || changed);
        this.view.error = changed && edited ? "changed" : undefined;
      }
    } catch { if (!this.stopped && generation === this.revision) this.view = { editing: false, busy: false, error: "failed" }; }
    this.publish();
  }
  edit(patch: Partial<Draft>) {
    if (this.stopped || this.view.busy || !this.view.snapshot?.writeAvailable || this.view.snapshot.manualRecoveryRequired) return;
    const next = { ...this.view.draft!, ...patch };
    if (!modes.includes(next.rgbMode) || !hex(next.rgbHex) || !vibrationLevels.includes(next.vibration)) return;
    const actual = this.view.snapshot.state;
    if ("rgbMode" in patch || "rgbHex" in patch) {
      if (next.rgbMode === actual.leftMode && next.rgbMode === actual.rightMode && next.rgbHex === actual.leftRgb && next.rgbHex === actual.rightRgb) {
        delete this.edits.rgbMode; delete this.edits.rgbHex;
      } else { this.edits.rgbMode = next.rgbMode; this.edits.rgbHex = next.rgbHex; }
    }
    if ("vibration" in patch) {
      if (next.vibration === actual.vibration) delete this.edits.vibration; else this.edits.vibration = next.vibration;
    }
    this.view.draft = next; this.view.editing = Object.keys(this.edits).length > 0; this.view.error = undefined; this.publish();
  }
  async run(action: "apply" | "restore") {
    const original = this.view.snapshot, draft = this.view.draft;
    if (this.stopped || this.view.busy || !original || !draft || !this.services.visible()
      || (action === "apply" ? !original.writeAvailable || original.manualRecoveryRequired || !this.view.editing : !original.restoreAvailable)) return;
    this.view.busy = true; this.view.error = undefined; const generation = ++this.revision; this.publish();
    const edits = { ...this.edits };
    const current = () => !this.stopped && generation === this.revision && this.services.visible();
    try {
      if (this.reading) await this.reading.catch(() => {});
      if (!current()) return;
      const fresh = await this.read();
      if (!current()) return;
      if (!validAyaneoSnapshot(fresh) || fresh.token !== original.token) {
        if (validAyaneoSnapshot(fresh)) this.accept(fresh, true); else { this.view.snapshot = undefined; this.view.draft = undefined; }
        this.view.error = "changed"; return;
      }
      if (action === "apply" ? !fresh.writeAvailable || fresh.manualRecoveryRequired : !fresh.restoreAvailable) {
        this.accept(fresh, true); this.view.error = "failed"; return;
      }
      // No asynchronous gap or untrusted UI value is introduced after this fence.
      const result = action === "apply"
        ? await this.services.apply({ token: fresh.token, ...edits })
        : await this.services.restore({ token: fresh.token });
      if (!current()) return;
      const actual = await this.read();
      if (!current()) return;
      if (!validAyaneoSnapshot(actual)) {
        this.view.snapshot = undefined; this.view.draft = undefined; this.view.editing = false; this.view.error = "failed"; return;
      }
      this.accept(actual, true);
      this.view.error = result.ok === true && validAyaneoSnapshot(result) && result.token === actual.token
        && sameState(result.state, actual.state) && (action !== "apply" || result.verified === true) ? undefined : "failed";
    } catch { if (current()) { this.view.snapshot = undefined; this.view.draft = undefined; this.view.editing = false; this.view.error = "failed"; } }
    finally { this.view.busy = false; this.publish(); }
  }
  dispose() { this.stopped = true; this.revision++; }
}

export function hexToHsl(value: string): [number, number, number] {
  const [r, g, b] = [0, 2, 4].map(offset => parseInt(value.slice(offset, offset + 2), 16) / 255);
  const high = Math.max(r, g, b), low = Math.min(r, g, b), delta = high - low, light = (high + low) / 2;
  const saturation = delta === 0 ? 0 : delta / (1 - Math.abs(2 * light - 1));
  const hue = delta === 0 ? 0 : (high === r ? ((g - b) / delta) % 6 : high === g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60;
  return [(hue + 360) % 360, saturation * 100, light * 100];
}
export function pickerHex(value: string): string | undefined {
  const match = /^hsla\(\s*(\d+(?:\.\d+)?),\s*(\d+(?:\.\d+)?)%,\s*(\d+(?:\.\d+)?)%,\s*1\s*\)$/.exec(value);
  if (!match) return;
  const [h, s, l] = match.slice(1).map(Number);
  if (h > 360 || s > 100 || l > 100) return;
  const a = s / 100 * Math.min(l / 100, 1 - l / 100);
  return [0, 8, 4].map(n => {
    const k = (n + h / 30) % 12;
    return Math.round(255 * (l / 100 - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)))).toString(16).padStart(2, "0");
  }).join("");
}

export function AyaneoColorPicker({ locale, color, closeModal, onConfirm }: {
  locale: string; color: string; closeModal(): void; onConfirm(hex: string): void;
}) {
  const copy = words[deviceLanguage(locale)], labels = pickerWords[deviceLanguage(locale)], common = controlLocale(locale);
  const initial = hexToHsl(color);
  const [hue, setHue] = useState(initial[0]), [saturation, setSaturation] = useState(initial[1]), [lightness, setLightness] = useState(initial[2]);
  const selected = pickerHex(`hsla(${hue}, ${saturation}%, ${lightness}%, 1)`);
  return <ConfirmModal strTitle={copy[2]} strOKButtonText={copy[11]} strCancelButtonText={common.back}
    bAllowFullSize bOKDisabled={!selected} onCancel={closeModal} closeModal={closeModal}
    onOK={() => { if (selected) { onConfirm(selected); closeModal(); } }}>
    <div className="qsCardBody" style={{ display: "grid", gap: 12, minWidth: 0 }}>
      <div aria-label={`#${(selected ?? color).toUpperCase()}`} style={{ height: 44, borderRadius: 6, border: "1px solid rgba(255,255,255,.4)", background: `#${selected ?? color}`, marginBottom: 12 }} />
      <SliderField label={labels[0]} min={0} max={360} step={1} value={hue} showValue editableValue bottomSeparator="none" onChange={setHue} />
      <SliderField label={labels[1]} min={0} max={100} step={1} value={saturation} showValue editableValue valueSuffix="%" bottomSeparator="none" onChange={setSaturation} />
      <SliderField label={labels[2]} min={0} max={100} step={1} value={lightness} showValue editableValue valueSuffix="%" bottomSeparator="none" onChange={setLightness} />
    </div>
  </ConfirmModal>;
}

export function Ayaneo3ControllerControls({ locale, deviceKey, active = true }: { locale: string; deviceKey: string; active?: boolean }) {
  const copy = words[deviceLanguage(locale)], common = controlLocale(locale), device = deviceInfoLocale(locale);
  const [view, setView] = useState<ModelView>({ editing: false, busy: false });
  const model = useRef<AyaneoControlModel | undefined>(undefined);
  const modal = useRef<{ Close(): void } | undefined>(undefined);
  useEffect(() => {
    let stopped = false, timer: number | undefined;
    setView({ editing: false, busy: false });
    const visible = () => active && document.visibilityState !== "hidden";
    const owner = new AyaneoControlModel({
      read: () => call<[], Reply>("get_ayaneo3_controller"),
      apply: request => call<[Record<string, string>], Reply>("set_ayaneo3_controller", request),
      restore: request => call<[{ token: string }], Reply>("restore_ayaneo3_controller", request),
      visible, changed: next => { if (!stopped) setView(next); },
    });
    model.current = owner;
    const refresh = async () => {
      window.clearTimeout(timer);
      await owner.refresh();
      window.clearTimeout(timer);
      if (!stopped) timer = window.setTimeout(() => void refresh(), 750);
    };
    const changed = () => { window.clearTimeout(timer); void refresh(); };
    void refresh();
    window.addEventListener("focus", changed); document.addEventListener("visibilitychange", changed);
    return () => {
      stopped = true; owner.dispose(); if (model.current === owner) model.current = undefined;
      window.clearTimeout(timer); modal.current?.Close(); modal.current = undefined;
      window.removeEventListener("focus", changed); document.removeEventListener("visibilitychange", changed);
    };
  }, [deviceKey, active]);
  const selectColor = () => {
    const owner = model.current, initial = owner?.view.snapshot, draft = owner?.view.draft;
    if (!owner || owner.view.busy || !initial?.writeAvailable || !draft) return;
    let popup: ReturnType<typeof showModal>;
    popup = showModal(<AyaneoColorPicker locale={locale} color={draft.rgbHex}
      closeModal={() => { popup?.Close(); if (modal.current === popup) modal.current = undefined; }}
      onConfirm={value => {
        if (hex(value) && model.current === owner && owner.view.snapshot?.token === initial.token && !owner.view.busy) owner.edit({ rgbHex: value });
      }} />, window, { bNeverPopOut: true });
    modal.current = popup;
  };
  const snapshot = view.snapshot, draft = view.draft;
  const disabled = view.busy || !snapshot?.writeAvailable || snapshot.manualRecoveryRequired;
  const modeName = (mode: Mode) => copy[4 + modes.indexOf(mode)];
  const vibeName = (vibration: Vibration) => copy[vibration === "off" ? 7 : 8 + vibrationLevels.indexOf(vibration)];
  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={view.busy}>
    <div className="qsMeta">AYANEO 3 · {copy[0]} · {device.experimental}</div>
    {snapshot && draft ? <>
      <DropdownItem label={copy[1]} layout="below" bottomSeparator="none" selectedOption={draft.rgbMode} disabled={disabled}
        rgOptions={modes.map(mode => ({ data: mode, label: modeName(mode) }))}
        onChange={({ data }) => { if (modes.includes(data)) model.current?.edit({ rgbMode: data }); }} />
      <ButtonItem label={copy[2]} layout="below" bottomSeparator="none" disabled={disabled || draft.rgbMode === "off" || draft.rgbMode === "rainbow"}
        onClick={selectColor}><span style={{ display: "inline-flex", alignItems: "center", gap: 8 }}>
          <span aria-hidden="true" style={{ width: 18, height: 18, borderRadius: 4, border: "1px solid currentColor", background: `#${draft.rgbHex}` }} />#{draft.rgbHex.toUpperCase()}</span></ButtonItem>
      <DropdownItem label={copy[3]} layout="below" bottomSeparator="none" selectedOption={draft.vibration} disabled={disabled}
        rgOptions={vibrationLevels.map(level => ({ data: level, label: vibeName(level) }))}
        onChange={({ data }) => { if (vibrationLevels.includes(data)) model.current?.edit({ vibration: data }); }} />
      <div className="qsMeta">{copy[13]}: {copy[14]} {modeName(snapshot.state.leftMode)} #{snapshot.state.leftRgb.toUpperCase()}
        {" · "}{copy[15]} {modeName(snapshot.state.rightMode)} #{snapshot.state.rightRgb.toUpperCase()}
        {" · "}{copy[3]} {vibeName(snapshot.state.vibration)}</div>
      {view.editing && <ButtonItem layout="below" bottomSeparator="none" disabled={disabled} onClick={() => void model.current?.run("apply")}>{copy[11]}</ButtonItem>}
      {snapshot.restoreAvailable && <ButtonItem layout="below" bottomSeparator="none" disabled={view.busy} onClick={() => void model.current?.run("restore")}>{copy[12]}</ButtonItem>}
    </> : <div className="qsMeta" role="status">{common.unavailable}</div>}
    {(view.error || snapshot?.manualRecoveryRequired) && <div className="qsMeta" role="status">{view.error === "changed" ? copy[16] : common.saveError}</div>}
    {(!snapshot || view.error || snapshot.manualRecoveryRequired) && <ButtonItem layout="below" bottomSeparator="none" disabled={view.busy} onClick={() => void model.current?.refresh()}>{common.retry}</ButtonItem>}
  </div>;
}
