import { DropdownItem } from "@decky/ui";
import { useEffect, useRef, useState } from "react";
import { call } from "../controlBackend";
import { controlLocale } from "../controlCenterLocale";
import { deviceInfoLocale } from "../deviceInfoLocale";
import { t } from "./translations";

const modes = ["efficiency", "balanced", "performance"] as const;
type PowerMode = typeof modes[number];
type PowerSource = "ac" | "battery";
type PowerProfileState = {
  available: true;
  source: PowerSource;
  mode: PowerMode;
};
type PowerResponse = (PowerProfileState | { available?: false }) & { ok?: boolean };

export function PowerProfile({ locale }: { locale: string }) {
  const [state, setState] = useState<PowerProfileState>();
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const mounted = useRef(false);
  const writing = useRef(false);
  const revision = useRef(0);

  useEffect(() => {
    mounted.current = true;
    let stopped = false;
    let reading = false;
    let available = false;
    const refresh = async () => {
      if (stopped || reading || writing.current) return;
      const observedRevision = revision.current;
      reading = true;
      try {
        const response = await call<[], PowerResponse>("get_windows_power_profile");
        if (!stopped && revision.current === observedRevision) {
          available = response.available === true;
          setState(response.available ? response : undefined);
        }
      } catch { /* Startup and transient refresh failures retain the last confirmed value. */ }
      finally { reading = false; }
    };
    void refresh();
    const interval = window.setInterval(() => { if (available) void refresh(); }, 15_000);
    const onFocus = () => void refresh();
    window.addEventListener("focus", onFocus);
    return () => {
      stopped = true;
      mounted.current = false;
      window.clearInterval(interval);
      window.removeEventListener("focus", onFocus);
    };
  }, []);

  if (!state) return null;

  const local = t(locale);
  const device = deviceInfoLocale(locale);
  const labels = { efficiency: local.powerEfficiency, balanced: local.powerBalanced, performance: local.powerBest };
  const supply = state.source === "ac" ? device.ac : device.battery;

  async function choose(mode: PowerMode, current: PowerProfileState) {
    if (writing.current || !modes.includes(mode) || mode === current.mode) return;
    revision.current++;
    writing.current = true;
    setBusy(true);
    setFailed(false);
    try {
      const result = await call<[{ source: PowerSource; mode: PowerMode }], PowerResponse>("set_windows_power_profile", { source: current.source, mode });
      if (!mounted.current) return;
      if (result.available) setState(result);
      if (result.ok !== true) setFailed(true);
    } catch { if (mounted.current) setFailed(true); }
    finally {
      writing.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  return <div className="qsCardBody qsDropdownFullWidth" aria-busy={busy}>
    <DropdownItem bottomSeparator="none" layout="below" label={local.powerMode} description={supply}
      rgOptions={modes.map(data => ({ data, label: labels[data] }))} selectedOption={state.mode}
      disabled={busy} onChange={option => void choose(option.data as PowerMode, state)} />
    {failed && <div className="qsMeta" role="status">{controlLocale(locale).saveError}</div>}
  </div>;
}
