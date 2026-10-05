// Backend client for Quick Settings.
//   * call(...)  -> Decky Python backend (main.py)
//   * fetch(...) -> bundled local agent on 127.0.0.1:47993 (volume + dimmer)

import { call } from "../controlBackend";

export const API_BASE = "http://127.0.0.1:47993";

let ensureAgentPromise: Promise<boolean> | undefined;
export function resetAgentPromise(): void {
  ensureAgentPromise = undefined;
}
// Auto path: respects a manual stop (won't relaunch if the user pressed Stop).
export async function ensureAgent(): Promise<boolean> {
  if (!ensureAgentPromise) {
    ensureAgentPromise = call<[], any>("ensure_agent")
      .then((r: any) => {
        const running = Boolean(r?.running ?? r);
        if (!running) ensureAgentPromise = undefined;
        return running;
      })
      .catch(() => {
        ensureAgentPromise = undefined;
        return false;
      });
  }
  return ensureAgentPromise;
}

// ----------------------------- Agent (HTTP) ---------------------------- //
export interface QuickSettingsStatus {
  volume: { available: boolean; level: number; muted: boolean };
  dimmer: { available: boolean; level: number };
}
async function fetchQuickSettings(timeoutMs = 1200): Promise<QuickSettingsStatus> {
  const controller = new AbortController();
  const timer = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(`${API_BASE}/quick-settings`, { signal: controller.signal });
    if (!response.ok) throw new Error(`${response.status}`);
    return (await response.json()) as QuickSettingsStatus;
  } finally {
    window.clearTimeout(timer);
  }
}
export async function getQuickSettings(): Promise<QuickSettingsStatus> {
  try {
    return await fetchQuickSettings();
  } catch {
    await ensureAgent();
    return await fetchQuickSettings(2200);
  }
}
export async function postAgent(path: string, body: unknown): Promise<any> {
  await ensureAgent();
  const response = await fetch(`${API_BASE}${path}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${response.status}`);
  return await response.json();
}
export const setVolumeLevel = (level: number) => postAgent("/quick-settings/volume", { level });
export const setDimmerLevel = (level: number) => postAgent("/quick-settings/dimmer", { level });

// ----------------------- Agent lifecycle (Python) --------------------- //
export interface AgentStatus { running: boolean }
export const getAgentStatus = () => call<[], AgentStatus>("get_agent_status");
export const ensureAgentNow = () => call<[], any>("ensure_agent");
export const startAgentNow = () => call<[], { ok: boolean; running: boolean }>("start_agent");
export const stopAgentNow = () => call<[], { ok: boolean; running: boolean }>("stop_agent");
export const stopAgentAuto = () => call<[], { ok: boolean; running: boolean }>("stop_agent_auto");

// ---------------------------- Types ----------------------------------- //
export interface Capabilities {
  ok: boolean;
  platform: string;
  cpu: string;
  is_amd: boolean;
  performance: boolean;
  power_mode: boolean;
  display: boolean;
  tdp: boolean;
  tdp_message: string;
  lossless: boolean;
  amd_radeon: boolean;
  amd_build_needed: boolean;
  amd_path?: string;
}
export interface PerformanceStatus {
  ok: boolean;
  code?: string;
  detected?: boolean;
  message?: string;
  power_mode: "efficiency" | "balanced" | "better" | "best";
}
export interface HdrStatus {
  available: boolean;
  supported: boolean;
  enabled: boolean;
  shortcut_only: boolean;
  real_state: boolean;
  message: string;
}
export interface AudioDevices {
  ok: boolean;
  /** Set only when a call failed. Empty/absent means the enumeration ran. */
  code?: string;
  message: string;
  outputs: { id: string; name: string }[];
  inputs: { id: string; name: string }[];
  default_output_id: string;
  default_input_id: string;
  input_volume: number;
}
export interface DisplayStatus {
  ok: boolean;
  message: string;
  current: { width?: number; height?: number; hz?: number };
  modes: { width: number; height: number; hz: number }[];
  resolutions: { width: number; height: number }[];
  refresh_rates: number[];
}
export interface TdpStatus {
  ok: boolean;
  available: boolean;
  message: string;
  stapm: number;
  fast: number;
  slow: number;
}
export interface LosslessStatus {
  ok: boolean;
  available: boolean;
  installed: boolean;
  running: boolean;
  path?: string;
  message: string;
  frame_gen?: string;
  multiplier?: number;
  hotkey?: string;
  scaling_active?: boolean;
  settings_found?: boolean;
  settings?: LosslessProfileSettings;
  runtime?: Record<string, any>;
  current_game?: {
    app_id?: number;
    title?: string;
    ui_mode?: number;
    source?: string;
    reported_at?: string;
  };
  can_scale?: boolean;
}
export interface LosslessProfileSettings {
  activation_delay_ms: number;
  scaling_mode: string;
  scaling_fit_mode: string;
  scale_factor: number;
  resize_before_scaling: boolean;
  windowed_mode: boolean;
  scaling_type: string;
  fsr_type: string;
  ls1_type: string;
  anime4k_type: string;
  sharpness: number;
  ls1_sharpness: number;
  vrs: boolean;
  frame_generation: string;
  lsfg2_mode: string;
  lsfg3_mode: string;
  lsfg3_multiplier: number;
  lsfg3_target: number;
  lsfg_flow_scale: number;
  lsfg_size: string;
  clip_cursor: boolean;
  adjust_cursor_speed: boolean;
  hide_cursor: boolean;
  scale_cursor: boolean;
  sync_mode: string;
  max_frame_latency: number;
  gsync_support: boolean;
  hdr_support: boolean;
  draw_fps: boolean;
  capture_api: string;
  queue_target: number;
  preferred_gpu_id: number;
  output_display_id: number;
  multi_display_mode: boolean;
  crop_input: boolean;
  crop_input_left: number;
  crop_input_top: number;
  crop_input_right: number;
  crop_input_bottom: number;
}
export interface LosslessGameProfile {
  ok: boolean;
  app_id: number;
  title: string;
  auto_enabled: boolean;
  settings: LosslessProfileSettings;
  amd_auto_enabled: boolean;
  amd_settings: AmdProfileSettings;
  sdl3_native_controller_enabled: boolean;
  amd_status?: AmdStatus;
}
export interface AmdProfileSettings {
  rsr: boolean;
  rsr_sharpness: number;
  afmf: boolean;
  antilag: boolean;
  chill: boolean;
  chill_min: number;
  chill_max: number;
  sharpening: boolean;
  sharpening_value: number;
  boost: boolean;
  boost_resolution: number;
  enhanced_sync: boolean;
}
export interface InitialState {
  capabilities: Capabilities;
  performance?: PerformanceStatus;
  audio?: AudioDevices;
  hdr?: HdrStatus | { ok?: boolean; hdr?: HdrStatus };
  agent?: AgentStatus;
  display?: DisplayStatus;
  tdp?: TdpStatus;
  lossless?: LosslessStatus;
  amd?: AmdStatus;
  timings_ms?: Record<string, number>;
  total_ms?: number;
}

// HDR
export const getHdrStatus = () => call<[], any>("get_hdr_status");
export type DisplayChangeResult = {
  ok: boolean;
  code?: string;
  terminal?: boolean;
  kept?: boolean;
  expired?: boolean;
  state?: string;
  message?: string;
};
export const beginDisplayChange = (request: { kind: 'hdr' | 'display'; enabled?: boolean; width?: number; height?: number; hz?: number }) =>
  call<[typeof request], DisplayChangeResult & { token?: string; expiresAt?: number; secondsRemaining?: number }>("begin_display_change", request);
export const finishDisplayChange = (token: string, keep: boolean) =>
  call<[{ token: string; keep: boolean }], DisplayChangeResult>("finish_display_change", { token, keep });
export const getDisplayChange = (token?: string) =>
  call<[{ token?: string }], Partial<DisplayChangeResult> & { state: string; token?: string; secondsRemaining?: number }>(
    "get_display_change", { token });
export const setHdrEnabled = (enabled: boolean) => call<[{ enabled: boolean }], any>("set_hdr_enabled", { enabled });

// Audio
export const getAudioDevices = () => call<[], AudioDevices>("get_audio_devices");
export const setAudioOutput = (id: string) => call<[{ id: string }], any>("set_audio_output", { id });
export const setAudioInput = (id: string) => call<[{ id: string }], any>("set_audio_input", { id });
export const setMicrophoneVolumeLevel = (level: number, expected_endpoint?: string) => call<[{ level: number; expected_endpoint?: string }], any>("set_microphone_volume", { level, expected_endpoint });

// Capabilities
export const getCapabilities = () => call<[], Capabilities>("get_capabilities");
export const getInitialState = () => call<[], InitialState>("get_initial_state");
export const getPerformanceStatus = () => call<[], PerformanceStatus>("get_performance_status");
export const setPowerMode = (mode: PerformanceStatus["power_mode"]) =>
  call<[{ mode: PerformanceStatus["power_mode"] }], PerformanceStatus>(
    "set_power_mode",
    { mode },
  );

// Display
export const getDisplayStatus = () => call<[], DisplayStatus>("get_display_status");
export const setDisplayMode = (width: number, height: number, hz: number) =>
  call<[{ width: number; height: number; hz: number }], any>("set_display_mode", { width, height, hz });
export const setRefreshRate = (hz: number) => call<[{ hz: number }], any>("set_refresh_rate", { hz });

// TDP
export const getTdpStatus = () => call<[], TdpStatus>("get_tdp_status");
export const setTdp = (watts: number) => {
  if (!Number.isFinite(watts) || watts < 4 || watts > 40) {
    return Promise.reject(new RangeError("TDP must be a finite value between 4 and 40 W."));
  }
  return call<[{ watts: number }], any>("set_tdp", { watts });
};

// Lossless Scaling
export const getLosslessStatus = () => call<[], LosslessStatus>("get_lossless_status");
export const launchLossless = () => call<[], { ok: boolean; running: boolean; message: string }>("launch_lossless");
export const setLosslessScaling = (enabled: boolean) =>
  call<[{ enabled: boolean }], { ok: boolean; active: boolean; message: string }>("set_lossless_scaling", { enabled });
export const setSteamOverlayActive = (
  active: boolean,
  overlayPid = 0,
  appId = 0,
  userInitiated = false,
) =>
  call<
    [{
      active: boolean;
      overlay_pid: number;
      app_id: number;
      user_initiated: boolean;
    }],
    any
  >("set_steam_overlay_active", {
    active,
    overlay_pid: overlayPid,
    app_id: appId,
    user_initiated: userInitiated,
  });
export const setLosslessSetting = (key: string, value: string | number | boolean) =>
  call<[{ key: string; value: string | number | boolean }], any>(
    "set_lossless_setting",
    { key, value },
  );
export const getLosslessProfile = (appId: number, title: string) =>
  call<[{ app_id: number; title: string }], LosslessGameProfile>(
    "get_lossless_profile",
    { app_id: appId, title },
  );
export const saveLosslessProfile = (
  appId: number,
  title: string,
  autoEnabled: boolean,
  settings: LosslessProfileSettings,
  amdAutoEnabled: boolean,
  amdSettings: AmdProfileSettings,
  sdl3NativeControllerEnabled: boolean,
) =>
  call<
    [{
      app_id: number;
      title: string;
      auto_enabled: boolean;
      settings: LosslessProfileSettings;
      amd_auto_enabled: boolean;
      amd_settings: AmdProfileSettings;
      sdl3_native_controller_enabled: boolean;
    }],
    any
  >("save_lossless_profile", {
    app_id: appId,
    title,
    auto_enabled: autoEnabled,
    settings,
    amd_auto_enabled: amdAutoEnabled,
    amd_settings: amdSettings,
    sdl3_native_controller_enabled: sdl3NativeControllerEnabled,
  });
export const reportGameRuntime = (
  appId: number,
  title: string,
  uiMode: number,
  source: string,
) =>
  call<
    [{ app_id: number; title: string; ui_mode: number; source: string }],
    any
  >("game_runtime_changed", {
    app_id: appId,
    title,
    ui_mode: uiMode,
    source,
  });
export const generateDiagnostics = () =>
  call<[], { ok: boolean; path?: string; duration_ms?: number; message?: string }>(
    "generate_diagnostics",
  );

// AMD Radeon (ADLX helper)
export interface AmdToggle { supported: boolean; enabled?: boolean }
export interface AmdStatus {
  ok: boolean;
  available: boolean;
  built: boolean;
  source: boolean;
  message?: string;
  gpu?: boolean;
  rsr?: { supported: boolean; enabled?: boolean; sharpness?: number; smin?: number; smax?: number };
  afmf?: AmdToggle;
  antilag?: AmdToggle;
  chill?: { supported: boolean; enabled?: boolean; min?: number; max?: number; fmin?: number; fmax?: number };
  boost?: { supported: boolean; enabled?: boolean; resolution?: number; rmin?: number; rmax?: number };
  enhanced_sync?: AmdToggle;
  display_color?: {
    available: boolean;
    brightness?: { supported: boolean; value?: number; min?: number; max?: number };
    contrast?: { supported: boolean; value?: number; min?: number; max?: number };
    saturation?: { supported: boolean; value?: number; min?: number; max?: number };
    temperature?: { supported: boolean; value?: number; min?: number; max?: number };
  };
  sharpening?: {
    supported: boolean;
    enabled?: boolean;
    value?: number;
    smin?: number;
    smax?: number;
    step?: number;
    desktop_supported?: boolean;
    desktop_enabled?: boolean;
  };
}
export const getAmdStatus = () => call<[], AmdStatus>("get_amd_status");
export const setAmd = (feature: string, value: string | number) =>
  call<[{ feature: string; value: string | number }], any>("set_amd", { feature, value });
