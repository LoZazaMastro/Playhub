import { DropdownItem } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale } from "../deviceInfoLocale";
import { t } from "./translations";

const options = ["default", "efficiency", "performance"] as const;
type Mode = typeof options[number];
type Profile = { ok: boolean; available: boolean; ac: Mode; battery: Mode; batteryAvailable: boolean; status: string };
type Slot = "ac" | "battery";

const copyByLanguage: Record<string, readonly [string, string, string, string]> = {
  en: ["Game CPU profile", "Windows default", "Profile applied", "Managed by another app"],
  it: ["Profilo CPU del gioco", "Predefinito di Windows", "Profilo applicato", "Gestito da un’altra app"],
  de: ["CPU-Profil des Spiels", "Windows-Standard", "Profil angewendet", "Von einer anderen App verwaltet"],
  es: ["Perfil de CPU del juego", "Predeterminado de Windows", "Perfil aplicado", "Gestionado por otra aplicación"],
  fr: ["Profil processeur du jeu", "Par défaut sous Windows", "Profil appliqué", "Géré par une autre application"],
  pt: ["Perfil de CPU do jogo", "Padrão do Windows", "Perfil aplicado", "Gerenciado por outro aplicativo"],
  ru: ["Профиль ЦП игры", "По умолчанию в Windows", "Профиль применён", "Управляется другим приложением"],
  uk: ["Профіль ЦП гри", "Типовий у Windows", "Профіль застосовано", "Керується іншою програмою"],
  ja: ["ゲームの CPU プロファイル", "Windows の既定値", "プロファイルを適用済み", "別のアプリで管理中"],
  ko: ["게임 CPU 프로필", "Windows 기본값", "프로필 적용됨", "다른 앱에서 관리 중"],
  zh: ["游戏 CPU 配置", "Windows 默认", "配置已应用", "由其他应用管理"],
  hi: ["गेम की CPU प्रोफ़ाइल", "Windows का डिफ़ॉल्ट", "प्रोफ़ाइल लागू की गई", "दूसरा ऐप प्रबंधित कर रहा है"],
};

export function GameExecutionProfile({ appId, locale }: { appId: number; locale: string }) {
  const [profile, setProfile] = useState<Profile>();
  const [saving, setSaving] = useState(false);
  const [failed, setFailed] = useState(false);
  const active = useRef(false);
  const writing = useRef(false);
  const revision = useRef(0);
  const polling = useRef(false);
  const [title, defaultLabel, appliedLabel, externalLabel] = copyByLanguage[locale.toLowerCase().split(/[-_]/)[0]] ?? copyByLanguage.en;
  const device = deviceInfoLocale(locale);
  const control = controlLocale(locale);
  const local = t(locale);
  const labels: Record<Mode, string> = { default: defaultLabel, efficiency: local.powerEfficiency, performance: local.powerBest };

  useEffect(() => {
    active.current = true;
    let disposed = false;
    const refresh = async () => {
      if (disposed || writing.current || polling.current || document.visibilityState === "hidden") return;
      const requested = revision.current;
      polling.current = true;
      try {
        const result = await call<[number], Profile>("get_game_execution_profile", appId);
        if (!disposed && requested === revision.current && result.ok) setProfile(result);
      } catch { /* Keep the last saved profile during a transient backend outage. */ }
      finally { polling.current = false; }
    };
    void refresh();
    const timer = window.setInterval(() => void refresh(), 7000);
    const onVisible = () => void refresh();
    document.addEventListener("visibilitychange", onVisible);
    return () => { disposed = true; active.current = false; clearInterval(timer); document.removeEventListener("visibilitychange", onVisible); };
  }, [appId]);

  async function choose(slot: Slot, value: Mode, current: Profile) {
    if (!options.includes(value) || current[slot] === value || writing.current) return;
    writing.current = true;
    revision.current++;
    setSaving(true);
    setFailed(false);
    try {
      const updated = { ...current, [slot]: value };
      const response = await call<[number, Mode, Mode], Profile>("set_game_execution_profile", appId, updated.ac, updated.battery);
      if (!active.current) return;
      if (response.ok) setProfile(response);
      else setFailed(true);
    } catch { if (active.current) setFailed(true); }
    finally {
      writing.current = false;
      if (active.current) setSaving(false);
    }
  }

  if (!profile?.available) return null;
  const modes = options.map(data => ({ data, label: labels[data] }));
  const feedback = failed ? control.saveError : profile.status === "Applied" ? appliedLabel
    : profile.status === "ExternalControl" ? externalLabel
    : ["Unavailable", "RestoreUnavailable"].includes(profile.status) ? control.unavailable : undefined;
  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={saving}>
    <DropdownItem bottomSeparator="none" label={title} description={profile.batteryAvailable ? device.ac : undefined}
      layout="below" rgOptions={modes} selectedOption={profile.ac} disabled={saving}
      onChange={item => void choose("ac", item.data as Mode, profile)} />
    {profile.batteryAvailable && <DropdownItem bottomSeparator="none" label={device.battery} layout="below" rgOptions={modes}
      selectedOption={profile.battery} disabled={saving} onChange={item => void choose("battery", item.data as Mode, profile)} />}
    {feedback && <div className="qsMeta" role="status">{feedback}</div>}
  </div>;
}
