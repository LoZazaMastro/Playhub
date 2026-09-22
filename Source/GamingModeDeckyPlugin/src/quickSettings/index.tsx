import { definePlugin, toaster } from "@decky/api";
import {
  DialogButton,
  Dropdown,
  DropdownItem,
  Focusable,
  GamepadButton,
  ModalRoot,
  ScrollPanel,
  showModal,
  staticClasses,
  ToggleField,
  SliderField,
} from "@decky/ui";
import { type ReactNode, useEffect, useRef, useState } from "react";
import { FaBolt, FaDesktop, FaFileAlt, FaMicrochip, FaPlay, FaSlidersH, FaTachometerAlt, FaTools, FaVolumeUp } from "react-icons/fa";

import {
  AudioDevices,
  Capabilities,
  DisplayStatus,
  ensureAgent,
  getAgentStatus,
  getAudioDevices,
  getCapabilities,
  getDisplayStatus,
  getHdrStatus,
  getInitialState,
  getLosslessStatus,

  getQuickSettings,

  HdrStatus,
  LosslessStatus,

  AmdStatus,
  QuickSettingsStatus,
  getAmdStatus,
  generateDiagnostics,
  setAmd,
  resetAgentPromise,
  setAudioInput,
  setAudioOutput,
  setDimmerLevel,
  beginDisplayChange,
  finishDisplayChange,
  getDisplayChange,
  setLosslessSetting,

  setMicrophoneVolumeLevel,

  setVolumeLevel,
  startAgentNow,
  stopAgentAuto,
  stopAgentNow,

  setLosslessScaling,
  setSteamOverlayActive,
} from "./backend";
import { installLosslessGameIntegration } from "./losslessProfiles";
import { Strings, t } from "./translations";
import { ControlSection } from "../ControlSection";
import type { ControlTab } from "../controlCenterState";
import { controlLocale, controlStatus } from "../controlCenterLocale";
import { createSettledWriter } from "../panelIntegration/settledWriter";

const REFRESH_INTERVAL = 15000;
const SLIDER_DEBOUNCE = 260;
const AMD_SLIDER_SETTLE = 220;

interface AudioStartupSnapshot {
  quick?: QuickSettingsStatus;
  devices?: AudioDevices;
  capturedAt: number;
}

let audioStartupSnapshot: AudioStartupSnapshot = { capturedAt: 0 };
let audioStartupPromise: Promise<AudioStartupSnapshot> | undefined;

function preloadAudioControls(): Promise<AudioStartupSnapshot> {
  if (audioStartupPromise) return audioStartupPromise;
  audioStartupPromise = Promise.allSettled([
    getQuickSettings(),
    getAudioDevices(),
  ]).then(([quickResult, devicesResult]) => {
    if (quickResult.status === "fulfilled") {
      audioStartupSnapshot.quick = quickResult.value;
    }
    if (devicesResult.status === "fulfilled") {
      audioStartupSnapshot.devices = devicesResult.value;
    }
    audioStartupSnapshot.capturedAt = Date.now();
    return audioStartupSnapshot;
  }).finally(() => {
    audioStartupPromise = undefined;
  });
  return audioStartupPromise;
}

type AmdWriteValue = string | number;

interface AmdWriteSlot {
  desired: AmdWriteValue;
  sequence: number;
  inFlight: boolean;
  lastInputAt: number;
  timer?: number;
}

const clampPercent = (v: number) => Math.max(0, Math.min(100, Math.round(v)));
const dimmerToBrightness = (d: number) => clampPercent(100 - d);
const brightnessToDimmer = (b: number) => clampPercent(100 - b);
const sleep = (ms: number) => new Promise((r) => window.setTimeout(r, ms));
// MMDevice endpoint ids are case-insensitive; the picker takes them from the
// registry and the readback from IMMDevice::GetId, and the GUID casing of the
// two does not always match.
const sameEndpoint = (a?: string, b?: string) =>
  String(a ?? "").toLowerCase() === String(b ?? "").toLowerCase();

function amdBoolean(value: AmdWriteValue): boolean {
  return value === "on" || value === "true" || value === 1;
}

function patchAmdValue(status: AmdStatus, feature: string, rawValue: AmdWriteValue): AmdStatus {
  const numberValue = Number(rawValue);
  const boolValue = amdBoolean(rawValue);
  switch (feature) {
    case "rsr":
      return status.rsr ? { ...status, rsr: { ...status.rsr, enabled: boolValue } } : status;
    case "rsr_sharpness":
      return status.rsr ? { ...status, rsr: { ...status.rsr, sharpness: numberValue } } : status;
    case "afmf":
      return status.afmf ? { ...status, afmf: { ...status.afmf, enabled: boolValue } } : status;
    case "antilag":
      return status.antilag ? { ...status, antilag: { ...status.antilag, enabled: boolValue } } : status;
    case "chill":
      return status.chill ? { ...status, chill: { ...status.chill, enabled: boolValue } } : status;
    case "chill_min":
      return status.chill ? { ...status, chill: { ...status.chill, min: numberValue } } : status;
    case "chill_max":
      return status.chill ? { ...status, chill: { ...status.chill, max: numberValue } } : status;
    case "sharpening":
      return status.sharpening ? { ...status, sharpening: { ...status.sharpening, enabled: boolValue } } : status;
    case "sharpening_value":
      return status.sharpening ? { ...status, sharpening: { ...status.sharpening, value: numberValue } } : status;
    case "boost":
      return status.boost ? { ...status, boost: { ...status.boost, enabled: boolValue } } : status;
    case "boost_resolution":
      return status.boost ? { ...status, boost: { ...status.boost, resolution: numberValue } } : status;
    case "enhanced_sync":
      return status.enhanced_sync
        ? { ...status, enhanced_sync: { ...status.enhanced_sync, enabled: boolValue } }
        : status;
    case "display_brightness":
      return status.display_color?.brightness
        ? { ...status, display_color: { ...status.display_color, brightness: { ...status.display_color.brightness, value: numberValue } } }
        : status;
    case "display_contrast":
      return status.display_color?.contrast
        ? { ...status, display_color: { ...status.display_color, contrast: { ...status.display_color.contrast, value: numberValue } } }
        : status;
    case "display_saturation":
      return status.display_color?.saturation
        ? { ...status, display_color: { ...status.display_color, saturation: { ...status.display_color.saturation, value: numberValue } } }
        : status;
    case "display_temperature":
      return status.display_color?.temperature
        ? { ...status, display_color: { ...status.display_color, temperature: { ...status.display_color.temperature, value: numberValue } } }
        : status;
    default:
      return status;
  }
}

function directionFromKey(key: string) {
  if (key === "ArrowLeft" || key === "Left") return -1;
  if (key === "ArrowRight" || key === "Right") return 1;
  return 0;
}

function directionFromGamepadButton(button: unknown) {
  if (button === GamepadButton.DIR_LEFT) return -1;
  if (button === GamepadButton.DIR_RIGHT) return 1;
  return 0;
}

function QuickSlider({
  label,
  value,
  min,
  max,
  step = 1,
  suffix = "",
  disabled = false,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  suffix?: string;
  disabled?: boolean;
  onChange: (value: number) => void;
}) {
  const validRange = Number.isFinite(min) && Number.isFinite(max) && min <= max;
  const validStep = Number.isFinite(step) && step > 0;
  const blocked = disabled || !validRange || !validStep || !Number.isFinite(value);
  return <SliderField bottomSeparator="none" label={label} value={validRange && Number.isFinite(value) ? Math.max(min, Math.min(max, value)) : 0}
    min={validRange ? min : 0} max={validRange ? max : 1} step={validStep ? step : 1}
    valueSuffix={suffix} showValue disabled={blocked}
    onChange={(next: number) => { if (!blocked && Number.isFinite(next)) onChange(Math.max(min, Math.min(max, next))); }} />;
}

function QuickDropdown({ label, options, value, disabled = false, fullWidth = false, onChange }: {
  label: string;
  options: any[];
  value: any;
  disabled?: boolean;
  fullWidth?: boolean;
  onChange: (value: any) => void;
}) {
  return (
    <div className={fullWidth ? "qsDropdownField qsDropdownFullWidth" : "qsDropdownField"}>
      <DropdownItem bottomSeparator="none" label={label} layout={fullWidth ? "below" : "inline"}
        childrenContainerWidth="max" disabled={disabled} rgOptions={options}
        selectedOption={value} onChange={(option: any) => onChange(option?.data ?? option)} />
    </div>
  );
}

// ---- Steam UI mode (agent runs only in Big Picture) ---- //
async function getUIMode(): Promise<number> {
  try {
    const value = await Promise.resolve(
      (window as any).SteamClient?.UI?.GetUIMode?.() ?? -1,
    );
    return Number.isFinite(Number(value)) ? Number(value) : -1;
  } catch {
    return -1;
  }
}
async function isBigPicture(): Promise<boolean> {
  const m = await getUIMode();
  return m === 4 || m === 7 || m === -1; // 4/7 gamepad; -1 unknown -> assume BPM
}
async function syncAgentToMode(): Promise<void> {
  try {
    if (await isBigPicture()) await ensureAgent();
    else await stopAgentAuto();
  } catch {
    /* ignore */
  }
}


function useDebounced<T, R = unknown>(fn: (value: T) => Promise<R> | R, delay = SLIDER_DEBOUNCE, onError?: (error: unknown) => void, onSettled?: (result: R) => void) {
  const callbacks = useRef({ fn, onError, onSettled });
  callbacks.current = { fn, onError, onSettled };
  const writer = useRef<ReturnType<typeof createSettledWriter<T, R>> | undefined>(undefined);
  useEffect(() => {
    const instance = createSettledWriter<T, R>({
      delayMs: delay,
      write: async (value) => {
        const result = await callbacks.current.fn(value);
        if (result && typeof result === "object" && "ok" in result && result.ok === false) {
          throw new Error("Setting write was not confirmed");
        }
        return result;
      },
      onSettled: (result) => callbacks.current.onSettled?.(result),
      onError: (error) => callbacks.current.onError?.(error),
      scheduler: { schedule(callback, delayMs) {
        const timer = window.setTimeout(callback, delayMs);
        return () => window.clearTimeout(timer);
      } },
    });
    writer.current = instance;
    return () => {
      instance.dispose();
      if (writer.current === instance) writer.current = undefined;
    };
  }, [delay]);
  return Object.assign((value: T) => { writer.current?.enqueue(value); }, {
    cancel: () => writer.current?.cancelPending(),
  });
}

function HdrConfirmModal({
  closeModal,
  onKeep,
  onRevert,
  label,
  expiresAt,
  secondsRemaining,
}: {
  closeModal?: () => void;
  onKeep: () => Promise<boolean>;
  onRevert: () => Promise<boolean>;
  label: Strings;
  expiresAt: number;
  secondsRemaining?: number;
}) {
  const deadline = useRef(performance.now() + Math.max(0, Math.min(30,
    secondsRemaining ?? (expiresAt - Date.now()) / 1000)) * 1000);
  const [seconds, setSeconds] = useState(() => Math.max(0, Math.ceil((deadline.current - performance.now()) / 1000)));
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const finished = useRef(false);
  const inFlight = useRef(false);
  const mounted = useRef(true);
  const finish = async (keep: boolean) => {
    if (finished.current || inFlight.current) return;
    inFlight.current = true;
    setBusy(true);
    setFailed(false);
    try {
      const confirmed = await (keep ? onKeep() : onRevert());
      if (confirmed) {
        finished.current = true;
        if (mounted.current) closeModal?.();
      } else if (mounted.current) setFailed(true);
    } catch {
      if (mounted.current) setFailed(true);
    } finally {
      inFlight.current = false;
      if (mounted.current) setBusy(false);
    }
  };
  const finishKeep = () => void finish(performance.now() < deadline.current);
  const finishRevert = () => void finish(false);
  useEffect(() => {
    mounted.current = true;
    let expired = false;
    const timer = window.setInterval(() => {
      const remaining = Math.max(0, Math.ceil((deadline.current - performance.now()) / 1000));
      setSeconds(remaining);
      if (remaining === 0 && !expired && !inFlight.current) {
        expired = true;
        finishRevert();
      }
    }, 250);
    return () => {
      mounted.current = false;
      window.clearInterval(timer);
      // A mode change can remount Steam windows. Unmount is not user cancellation;
      // the backend watchdog owns rollback and the next mount resumes confirmation.
    };
  }, []);
  return (
    <ModalRoot closeModal={finishRevert}>
      <div style={{ fontWeight: 700, fontSize: "1.1rem", marginBottom: "0.65rem" }}>{label.hdrConfirmTitle}</div>
      <div style={{ fontSize: "0.85rem", lineHeight: "1.25rem", opacity: 0.82, marginBottom: "0.9rem" }}>
        {`${label.hdrConfirmBody} ${seconds}s`}
      </div>
      {failed && <div role="alert">{label.hdrUnavailable}</div>}
      <Focusable flow-children="row" noFocusRing style={{ display: "flex", gap: "0.5rem", justifyContent: "flex-end" }}>
        <DialogButton focusable disabled={busy} onClick={finishRevert} style={{ minWidth: "7rem" }}>
          {label.cancel}
        </DialogButton>
        <DialogButton focusable disabled={busy || seconds === 0} onClick={finishKeep} style={{ minWidth: "7rem" }}>
          {label.ok}
        </DialogButton>
      </Focusable>
    </ModalRoot>
  );
}

// Modal lifetime belongs to the display transaction, not the QAM component.
// Steam hides/unmounts the QAM when a global modal opens.
const sharedDisplayModal: { current?: { token: string; modal: any } } = {};
export function disposeDisplayConfirmation() {
  const current = sharedDisplayModal.current;
  sharedDisplayModal.current = undefined;
  current?.modal?.Close?.();
}

export function QuickSettingsContent({ page, collapsed, onToggle, locale }: {
  page: ControlTab | "tools"; collapsed: string[]; onToggle: (id: string) => void; locale: string;
}) {
  const local = t(locale);
  const [loadFailed, setLoadFailed] = useState(false);
  const [loadAttempt, setLoadAttempt] = useState(0);

  const [volume, setVolume] = useState(
    audioStartupSnapshot.quick?.volume
      ?? { available: false, level: 0, muted: false },
  );
  const [dimmer, setDimmer] = useState(
    audioStartupSnapshot.quick?.dimmer
      ?? { available: false, level: 0 },
  );
  const [hdr, setHdr] = useState<HdrStatus>({
    available: true, supported: true, enabled: false, shortcut_only: true, real_state: false, message: "",
  });
  const [audio, setAudio] = useState<AudioDevices>(
    audioStartupSnapshot.devices ?? {
      ok: false, message: "", outputs: [], inputs: [], default_output_id: "", default_input_id: "", input_volume: 0,
    },
  );
  const [caps, setCaps] = useState<Capabilities | null>(null);
  const [display, setDisplay] = useState<DisplayStatus | null>(null);


  const [agentRunning, setAgentRunning] = useState(false);
  const [lossless, setLossless] = useState<LosslessStatus | null>(null);
  const [amd, setAmdState] = useState<AmdStatus | null>(null);
  const [diagnosticsBusy, setDiagnosticsBusy] = useState(false);
  const diagnosticsInFlight = useRef(false);

  const notify = (body: string) => toaster.toast({ title: local.title, body });
  const amdWriteSlots = useRef<Record<string, AmdWriteSlot>>({});
  const amdPendingValues = useRef<Record<string, AmdWriteValue>>({});
  const amdMounted = useRef(true);

  const applyPendingAmdValues = (status: AmdStatus): AmdStatus => {
    let next = status;
    for (const [feature, value] of Object.entries(amdPendingValues.current)) {
      next = patchAmdValue(next, feature, value);
    }
    return next;
  };

  const acceptAmdStatus = (status: AmdStatus) => {
    if (!amdMounted.current) return;
    setAmdState(applyPendingAmdValues(status));
  };

  const amdWritesBusy = () => Object.values(amdWriteSlots.current).some(
    (slot) => slot.inFlight || slot.timer !== undefined,
  );

  const flushAmdWrite = (feature: string) => {
    const slot = amdWriteSlots.current[feature];
    if (!slot || slot.inFlight) return;
    window.clearTimeout(slot.timer);
    slot.timer = undefined;
    const sentSequence = slot.sequence;
    const sentValue = slot.desired;
    slot.inFlight = true;

    void setAmd(feature, sentValue)
      .then((result: any) => {
        if (!amdMounted.current || slot.sequence !== sentSequence) return;
        delete amdPendingValues.current[feature];
        if (result?.gpu) {
          acceptAmdStatus(result);
        } else {
          void getAmdStatus().then(acceptAmdStatus).catch(() => {});
        }
        if (result?.ok === false) notify(local.amdDriverRejected);
      })
      .catch(() => {
        if (!amdMounted.current || slot.sequence !== sentSequence) return;
        delete amdPendingValues.current[feature];
        void getAmdStatus().then(acceptAmdStatus).catch(() => {});
        notify(local.amdDriverRejected);
      })
      .finally(() => {
        slot.inFlight = false;
        if (!amdMounted.current || slot.sequence === sentSequence) return;
        const remaining = Math.max(0, AMD_SLIDER_SETTLE - (Date.now() - slot.lastInputAt));
        slot.timer = window.setTimeout(() => flushAmdWrite(feature), remaining);
      });
  };

  const queueAmdWrite = (feature: string, value: AmdWriteValue, delay: number) => {
    const slot = amdWriteSlots.current[feature] ?? {
      desired: value,
      sequence: 0,
      inFlight: false,
      lastInputAt: 0,
    };
    slot.desired = value;
    slot.sequence += 1;
    slot.lastInputAt = Date.now();
    amdWriteSlots.current[feature] = slot;
    amdPendingValues.current[feature] = value;
    setAmdState((current) => current ? patchAmdValue(current, feature, value) : current);

    window.clearTimeout(slot.timer);
    slot.timer = undefined;
    if (!slot.inFlight) {
      slot.timer = window.setTimeout(() => flushAmdWrite(feature), delay);
    }
  };

  useEffect(() => {
    amdMounted.current = true;
    return () => {
      amdMounted.current = false;
      for (const slot of Object.values(amdWriteSlots.current)) {
        window.clearTimeout(slot.timer);
      }
    };
  }, []);

  // ----------------------------- loaders ----------------------------- //
  const loadAgentStatus = async () => {
    try {
      const qs = await getQuickSettings();
      audioStartupSnapshot = {
        ...audioStartupSnapshot,
        quick: qs,
        capturedAt: Date.now(),
      };
      if (qs.volume) setVolume(qs.volume);
      if (qs.dimmer) setDimmer(qs.dimmer);
      setAgentRunning(true);
    } catch {
      resetAgentPromise();
      void ensureAgent().then(async (running) => {
        setAgentRunning(running);
        if (!running) return;
        await sleep(120);
        try {
          const qs = await getQuickSettings();
          audioStartupSnapshot = {
            ...audioStartupSnapshot,
            quick: qs,
            capturedAt: Date.now(),
          };
          if (qs.volume) setVolume(qs.volume);
          if (qs.dimmer) setDimmer(qs.dimmer);
        } catch { /* keep the immediately rendered controls */ }
      });
    }
  };
  const loadHdr = async () => {
    try {
      const r: any = await getHdrStatus();
      setHdr(r?.hdr ?? r);
    } catch { /* keep */ }
  };
  const audioGeneration = useRef(0);
  const audioPending = useRef(0);
  const audioQueue = useRef<Promise<void>>(Promise.resolve());
  const audioConfirmed = useRef(audio);
  const micVolumePending = useRef(false);
  const micEndpointEpoch = useRef(0);
  const acceptAudio = (r: AudioDevices) => {
    audioConfirmed.current = r;
    audioStartupSnapshot = { ...audioStartupSnapshot, devices: r, capturedAt: Date.now() };
    setAudio((current) => micVolumePending.current ? { ...r, input_volume: current.input_volume } : r);
  };
  const loadAudio = async () => {
    if (audioPending.current) return;
    const generation = audioGeneration.current;
    try {
      const r = await getAudioDevices();
      if (r?.ok && generation === audioGeneration.current && !audioPending.current) acceptAudio(r);
    } catch { /* keep */ }
  };
  const loadDisplay = async () => {
    try {
      const r = await getDisplayStatus();
      if (r?.ok) setDisplay(r);
    } catch { /* ignore */ }
  };
  const loadLossless = async () => {
    try {
      const r = await getLosslessStatus();
      if (r) setLossless(r);
    } catch { /* ignore */ }
  };
  const loadAmd = async () => {
    try {
      const r = await getAmdStatus();
      if (r) acceptAmdStatus(r);
    } catch { /* ignore */ }
  };
  const refreshAgentRunning = async () => {
    try {
      const r = await getAgentStatus();
      setAgentRunning(Boolean(r?.running));
    } catch { /* ignore */ }
  };

  useEffect(() => {
    let cancelled = false;
    let timer: number | undefined;
    (async () => {
      const initialAudioGeneration = audioGeneration.current;
      void loadAgentStatus();
      void loadAudio();
      let c: Capabilities | null = null;
      try {
        const initial = await getInitialState();
        if (cancelled) return;
        c = initial.capabilities;
        setCaps(c);
        if (initial.audio?.ok && initialAudioGeneration === audioGeneration.current && !audioPending.current) acceptAudio(initial.audio);
        if (initial.hdr) setHdr((initial.hdr as any)?.hdr ?? initial.hdr as HdrStatus);
        if (initial.display?.ok) setDisplay(initial.display);


        if (initial.lossless) setLossless(initial.lossless);
        if (initial.amd) acceptAmdStatus(initial.amd);
      } catch { setLoadFailed(true); }
      if (cancelled) return;
      timer = window.setInterval(() => {
        void loadAgentStatus();
        void loadAudio();
        void loadHdr();

        void refreshAgentRunning();
        if (c?.lossless) void loadLossless();
        if (c?.amd_radeon && !amdWritesBusy()) void loadAmd();
      }, REFRESH_INTERVAL);
    })();
    return () => {
      cancelled = true;
      window.clearInterval(timer);
    };
  }, [loadAttempt]);

  // ---------------------------- handlers ----------------------------- //
  const commitVolume = useDebounced<number>((level) => setVolumeLevel(level), 35, () => notify(local.notConnected));
  const commitBrightness = useDebounced<number>((level) => setDimmerLevel(level), 35, () => notify(local.notConnected));
  const commitMicVolume = useDebounced<{ level: number; epoch: number }, number>(({ level, epoch }) => {
    const operation = audioQueue.current.then(async () => {
    if (epoch !== micEndpointEpoch.current) throw new Error("Microphone endpoint changed");
    const expectedEndpoint = audioConfirmed.current.default_input_id;
    try {
      if (!expectedEndpoint) throw new Error("Microphone endpoint is unknown");
      const result = await setMicrophoneVolumeLevel(level, expectedEndpoint);
      const observed = result?.input_volume;
      if (result?.ok !== true || result.endpoint_id !== expectedEndpoint || typeof observed !== "number" || !Number.isFinite(observed)
        || observed < 0 || observed > 100 || Math.abs(observed - level) > 1) {
        throw new Error("Microphone volume readback mismatch");
      }
      return observed;
    } catch {
      // Reconciliation stays inside the serialized operation and its stale-result guard.
      let readback: number | undefined;
      try {
        const snapshot = await getAudioDevices();
        if (snapshot?.ok && snapshot.default_input_id === expectedEndpoint && typeof snapshot.input_volume === "number" && Number.isFinite(snapshot.input_volume)
          && snapshot.input_volume >= 0 && snapshot.input_volume <= 100) readback = snapshot.input_volume;
      } catch { /* fall back to the last confirmed value */ }
      throw Object.assign(new Error("Microphone volume was not confirmed"), { readback });
    }
    });
    audioQueue.current = operation.then(() => {}, () => {});
    return operation;
  }, 180, (error) => {
    micVolumePending.current = false;
    const readback = (error as { readback?: number }).readback;
    const input_volume = readback ?? audioConfirmed.current.input_volume;
    audioConfirmed.current = { ...audioConfirmed.current, input_volume };
    setAudio((current) => ({ ...current, input_volume }));
    notify(local.audioDevicesUnavailable);
  }, (input_volume) => {
    micVolumePending.current = false;
    audioConfirmed.current = { ...audioConfirmed.current, input_volume };
    setAudio((current) => ({ ...current, input_volume }));
  });
  const onVolume = (level: number) => {
    const n = clampPercent(level);
    setVolume((c) => ({ ...c, level: n }));
    commitVolume(n);
  };
  const onBrightness = (b: number) => {
    const level = brightnessToDimmer(clampPercent(b));
    setDimmer((c) => ({ ...c, level }));
    commitBrightness(level);
  };
  const onMicVolume = (level: number) => {
    const n = clampPercent(level);
    micVolumePending.current = true;
    setAudio((c) => ({ ...c, input_volume: n }));
    commitMicVolume({ level: n, epoch: micEndpointEpoch.current });
  };
  const changeAudioDevice = (kind: "output" | "input", id: string) => {
    if (!id) return;
    if (kind === "input") {
      micEndpointEpoch.current++;
      commitMicVolume.cancel();
      micVolumePending.current = false;
    }
    const field = kind === "output" ? "default_output_id" : "default_input_id";
    const generation = ++audioGeneration.current;
    audioPending.current++;
    setAudio((c) => ({ ...c, [field]: id }));
    // Endpoint writes return a full snapshot: serialize input and output together.
    const pending = audioQueue.current.then(async () => {
      let result: AudioDevices | undefined;
      try {
        result = await (kind === "output" ? setAudioOutput(id) : setAudioInput(id));
        if (!result?.ok || !sameEndpoint(result[field], id)) {
          // Report what actually went wrong. "Audio devices unavailable" is
          // reserved for a machine that really has no endpoint.
          throw Object.assign(new Error("Audio readback mismatch"),
            { detail: result?.message });
        }
      } catch (error) {
        notify((error as { detail?: string })?.detail || local.audioChangeFailed);
        try { result = await getAudioDevices(); } catch { result = undefined; }
      }
      if (result?.ok) audioConfirmed.current = result;
      if (generation === audioGeneration.current) acceptAudio(audioConfirmed.current);
    }).finally(() => { audioPending.current--; });
    audioQueue.current = pending;
    return pending;
  };
  const onOutput = (id: string) => changeAudioDevice("output", id);
  const onInput = (id: string) => changeAudioDevice("input", id);
  const displayChanging = useRef(true);
  const displayMounted = useRef(true);
  const displayOperation = useRef(false);
  const displayGeneration = useRef(0);
  const displayToken = useRef<string | undefined>(undefined);
  const displayModal = sharedDisplayModal;
  const displayReconcile = useRef<Promise<any> | undefined>(undefined);
  const refreshDisplay = async () => { await Promise.all([loadHdr(), loadDisplay()]); };
  const closeDisplayModal = () => {
    const previous = displayModal.current;
    displayModal.current = undefined;
    previous?.modal?.Close?.();
  };
  const openDisplayModal = (token: string, secondsRemaining: number) => {
    if (!displayMounted.current || displayModal.current?.token === token) return;
    closeDisplayModal();
    const finish = async (keep: boolean) => {
      displayOperation.current = true;
      displayGeneration.current++;
      try {
        const response = await finishDisplayChange(token, keep);
        if (response.ok && response.terminal === true) {
          if (displayToken.current === token) {
            displayToken.current = undefined;
            displayChanging.current = false;
          }
          if (keep && !response.kept) notify(local.hdrUnavailable);
          return true;
        }
        notify(local.hdrUnavailable);
        return false;
      } catch {
        // A committed response can be lost. Query its token, never reverse a keep.
        try {
          const status = await getDisplayChange(token);
          if (status.ok && status.terminal === true) {
            if (displayToken.current === token) {
              displayToken.current = undefined;
              displayChanging.current = false;
            }
            return true;
          }
        } catch { /* The backend watchdog still owns the transaction. */ }
        notify(local.notConnected);
        return false;
      } finally {
        displayOperation.current = false;
        if (displayMounted.current) void refreshDisplay().catch(() => {});
      }
    };
    const modal: any = showModal(
      <HdrConfirmModal label={local} expiresAt={Date.now() + secondsRemaining * 1000} secondsRemaining={secondsRemaining}
        closeModal={() => {
          if (displayModal.current?.token === token) displayModal.current = undefined;
          modal?.Close?.();
        }} onKeep={() => finish(true)} onRevert={() => finish(false)} />
    );
    displayModal.current = { token, modal };
  };
  const reconcileDisplay = () => {
    if (displayReconcile.current) return displayReconcile.current;
    if (displayOperation.current) return Promise.resolve(undefined);
    const generation = displayGeneration.current;
    const pending = (async () => {
      try {
        const status = await getDisplayChange(displayToken.current);
        if (!displayMounted.current || generation !== displayGeneration.current) return status;
        if (status.state === "idle" || (status.ok && status.terminal === true)) {
          const hadPreview = Boolean(displayToken.current);
          displayToken.current = undefined;
          displayChanging.current = false;
          closeDisplayModal();
          if (hadPreview) await refreshDisplay();
        } else {
          displayChanging.current = true;
          displayToken.current = status.token;
          if (status.state === "previewing" && status.token) {
            openDisplayModal(status.token, Math.max(0, status.secondsRemaining ?? 0));
          }
        }
        return status;
      } catch { return undefined; }
    })().finally(() => {
      if (displayReconcile.current === pending) displayReconcile.current = undefined;
    });
    displayReconcile.current = pending;
    return pending;
  };
  useEffect(() => {
    displayMounted.current = true;
    void reconcileDisplay();
    const timer = window.setInterval(() => void reconcileDisplay(), 1000);
    return () => {
      displayMounted.current = false;
      displayGeneration.current++;
      window.clearInterval(timer);
      // Keep the global confirmation visible while Steam recreates the QAM.
    };
  }, []);
  const changeDisplay = async (request: Parameters<typeof beginDisplayChange>[0]) => {
    const status = await reconcileDisplay();
    if (!status) { notify(local.notConnected); return; }
    if (displayChanging.current) return;
    displayChanging.current = true;
    displayOperation.current = true;
    displayGeneration.current++;
    try {
      const result = await beginDisplayChange(request);
      if (!result.ok || !result.token || !result.expiresAt) {
        // A specific backend reason (mode unsupported, HDR rejected, busy) beats
        // the generic toggle failure.
        notify(result?.message
          || (request.kind === "hdr" ? local.hdrUnavailable : local.displayChangeFailed));
        return;
      }
      displayToken.current = result.token;
      openDisplayModal(result.token, result.secondsRemaining ?? Math.max(0, (result.expiresAt - Date.now()) / 1000));
    } catch {
      notify(local.notConnected);
    } finally {
      displayOperation.current = false;
      if (displayMounted.current) {
        await reconcileDisplay();
        await refreshDisplay();
      }
    }
  };
  const onHdr = (enabled: boolean) => void changeDisplay({ kind: "hdr", enabled });
  const onResolution = (value: string) => {
    const [width, height] = value.split("x").map(Number);
    void changeDisplay({ kind: "display", width, height });
  };
  const onRefresh = (value: string) => void changeDisplay({ kind: "display", hz: Number(value) });
  const onStartAgent = () => void startAgentNow().then((r: any) => setAgentRunning(Boolean(r?.running))).catch(() => {});
  const onStopAgent = () => void stopAgentNow().then((r: any) => setAgentRunning(Boolean(r?.running))).catch(() => {});

  // Lossless Scaling
  const onScalingToggle = (enabled: boolean) => {
    if (enabled && !lossless?.can_scale) {
      notify(local.losslessGameOnlyHint);
      return;
    }
    setLossless((c) => (c ? { ...c, scaling_active: enabled } : c));
    void setLosslessScaling(enabled).then((r: any) => {
      if (r) {
        setLossless((c) => (c ? {
          ...c,
          running: Boolean(r.running),
          scaling_active: Boolean(r.active),
          runtime: r.runtime ?? c.runtime,
        } : c));
      }
      if (r && r.ok === false && r.message) notify(r.message);
    }).catch(() => {}).finally(() => window.setTimeout(() => void loadLossless(), 250));
  };
  const onFrameGen = (value: string) => {
    setLossless((c) => (c ? { ...c, frame_gen: value } : c));
    void setLosslessSetting("frame_gen", value).then((r: any) => { if (r && r.ok === false && r.message) notify(r.message); }).finally(() => window.setTimeout(() => void loadLossless(), 1500));
  };
  const commitMultiplier = useDebounced<number>((v) => void setLosslessSetting("multiplier", v).finally(() => window.setTimeout(() => void loadLossless(), 1500)));
  const onMultiplier = (value: number) => {
    const n = Math.max(2, Math.min(4, Math.round(value)));
    setLossless((c) => (c ? { ...c, multiplier: n } : c));
    commitMultiplier(n);
  };
  const onDrawFps = (enabled: boolean) => {
    setLossless((current) => current ? {
      ...current,
      settings: current.settings
        ? { ...current.settings, draw_fps: enabled }
        : current.settings,
    } : current);
    void setLosslessSetting("draw_fps", enabled)
      .then((result: any) => {
        if (result?.settings) {
          setLossless((current) => current ? {
            ...current,
            settings: result.settings,
            runtime: result.runtime ?? current.runtime,
          } : current);
        }
        if (result?.ok === false && result?.message) notify(result.message);
      })
      .catch(() => notify(local.notConnected))
      .finally(() => window.setTimeout(() => void loadLossless(), 250));
  };

  // ADLX writes are comparatively expensive. Keep the control fully optimistic,
  // coalesce repeated notches and allow at most one write plus the final value.
  const onAmdToggle = (feature: string, enabled: boolean) => {
    queueAmdWrite(feature, enabled ? "on" : "off", 0);
  };
  const onDiagnostics = () => {
    if (diagnosticsInFlight.current) return;
    diagnosticsInFlight.current = true;
    setDiagnosticsBusy(true);
    void generateDiagnostics()
      .then((result) => {
        if (result?.ok && result.path) notify(`${local.diagnosticsReady} ${result.path}`);
        else notify(result?.message || local.notConnected);
      })
      .catch(() => notify(local.notConnected))
      .finally(() => { diagnosticsInFlight.current = false; setDiagnosticsBusy(false); });
  };

  // ----------------------------- render ------------------------------ //
  // A call that failed reports its own reason; only a successful enumeration
  // that came back with nothing means "no audio devices".
  const audioProblem = audio.ok === false && (audio.code || audio.message)
    ? (audio.message || local.audioChangeFailed) : undefined;
  const emptyAudioLabel = audioProblem ?? local.audioDevicesUnavailable;
  const outputOptions = audio.outputs.length > 0
    ? audio.outputs.map((d) => ({ data: d.id, label: d.name || local.audioOutput }))
    : [{ data: "", label: emptyAudioLabel }];
  const inputOptions = audio.inputs.length > 0
    ? audio.inputs.map((d) => ({ data: d.id, label: d.name || local.microphoneInput }))
    : [{ data: "", label: emptyAudioLabel }];

  const resolutionOptions = (display?.resolutions ?? []).map((r) => ({ data: `${r.width}x${r.height}`, label: `${r.width} × ${r.height}` }));
  const refreshOptions = (display?.refresh_rates ?? []).map((hz) => ({ data: `${hz}`, label: `${hz} Hz` }));
  const currentResolution = display?.current?.width ? `${display.current.width}x${display.current.height}` : resolutionOptions[0]?.data ?? "";
  const currentRefresh = display?.current?.hz ? `${display.current.hz}` : refreshOptions[0]?.data ?? "";


  const frameGenOptions = [
    { data: "Off", label: local.off },
    { data: "LSFG3", label: "LSFG 3.1" },
    { data: "LSFG2", label: "LSFG 2.3" },
    { data: "LSFG1", label: "LSFG 1.1" },
  ];

  const section = (icon: ReactNode, title: string, children: ReactNode) => {
    const sections: Record<string, [ControlTab | "tools", string]> = {
      [local.audio]: ["performance", "audio"], [local.display]: ["audio", "display"],
      [local.lossless]: ["graphics", "lossless"], [local.amd]: ["graphics", "radeon"],
      [local.advanced]: ["tools", "agent"],
    };
    const [tab, id] = sections[title];
    if (tab !== page) return null;
    if (tab === "audio" || tab === "performance") return <div key={id} className="qsCardBody">{children}</div>;
    return <ControlSection key={id} id={id} title={title} icon={icon} locale={locale}
      collapsed={collapsed.includes(id)} onToggle={onToggle}>
      <div className="qsCardBody">{children}</div>
    </ControlSection>;
  };

  return (
    <div>
      <Focusable flow-children="column" className="qsRedesign">
        <style>{`
          .qsRedesign,.qsRedesign *{box-sizing:border-box;min-width:0;letter-spacing:0}
          .qsRedesign{width:100%;padding:0 16px 18px;overflow-x:hidden;color:inherit}
          .qsCardBody{display:flex;flex-direction:column;gap:12px;width:100%;padding:8px 0}
          .qsCardBody>div{width:100%;max-width:100%;min-width:0}
          .qsCardBody [class*="PanelSectionRow"]{padding-left:0!important;padding-right:0!important;margin:0!important}
          .qsRedesign .qsDropdownFullWidth>div,.qsRedesign .qsDropdownFullWidth>div>div{width:100%;max-width:100%;min-width:0}
          .qsRedesign .qsDropdownFullWidth [role="combobox"]{width:100%;max-width:100%;min-width:0}
          .qsRedesign [class*="WithBottomSeparator"],.qsRedesign [class*="Field"]{border-top:0!important;border-bottom:0!important}
          .qsRedesign [class*="WithBottomSeparator"]::before,.qsRedesign [class*="WithBottomSeparator"]::after,.qsRedesign [class*="Field"]::before,.qsRedesign [class*="Field"]::after{display:none!important}
          .qsRedesign [role="separator"],.qsRedesign [class*="FieldSeparator"],.qsRedesign hr{display:none!important}
          .qsButtonInner{display:flex;align-items:center;gap:9px;text-align:left}
          .qsButtonInner svg{width:17px;height:17px;flex:none}
          .qsMeta{font-size:12px;line-height:1.45;opacity:.7;overflow-wrap:anywhere}
          .qsStatus{font-size:13px;line-height:1.4;padding:4px 0}
        `}</style>

        {!caps && <div className="qsMeta">{loadFailed ? local.notConnected : controlLocale(locale).loading}
          {loadFailed && <DialogButton onClick={() => { setLoadFailed(false); setLoadAttempt(value => value + 1); }}>{controlLocale(locale).retry}</DialogButton>}
        </div>}
        {caps && (page === "graphics" && !caps.lossless && !caps.amd_radeon) &&
          <div className="qsMeta">{controlStatus(locale)[2]}</div>}

        {section(<FaVolumeUp />, local.audio, <>
          <QuickSlider label={local.volume} value={volume.level} min={0} max={100} suffix="%" disabled={!volume.available} onChange={onVolume} />
          <QuickSlider label={local.microphoneVolume} value={clampPercent(audio.input_volume)} min={0} max={100} suffix="%" disabled={!audio.ok} onChange={onMicVolume} />
          <QuickDropdown fullWidth label={local.audioOutput} options={outputOptions} value={audio.default_output_id || ""} disabled={!audio.ok || audio.outputs.length === 0} onChange={onOutput} />
          <QuickDropdown fullWidth label={local.microphoneInput} options={inputOptions} value={audio.default_input_id || ""} disabled={!audio.ok || audio.inputs.length === 0} onChange={onInput} />
        </>)}

        {section(<FaDesktop />, local.display, <>
          <QuickSlider label={local.brightness} value={dimmerToBrightness(dimmer.level)} min={0} max={100} suffix="%" onChange={onBrightness} />
          {caps?.display && resolutionOptions.length > 0 && <QuickDropdown fullWidth key={`res-${currentResolution}`} label={local.resolution} options={resolutionOptions} value={currentResolution} onChange={onResolution} />}
          {caps?.display && refreshOptions.length > 0 && <QuickDropdown fullWidth key={`hz-${currentRefresh}`} label={local.refreshRate} options={refreshOptions} value={currentRefresh} onChange={onRefresh} />}
          <ToggleField bottomSeparator="none" label={local.hdr} checked={Boolean(hdr.enabled)} onChange={onHdr} />
        </>)}

        {caps?.lossless && section(<FaPlay />, local.lossless, <>
          <ToggleField
            bottomSeparator="none"
            label={local.losslessScaling}
            disabled={!lossless?.installed || !lossless?.can_scale}
            checked={Boolean(lossless?.scaling_active)}
            onChange={onScalingToggle}
          />
          <QuickDropdown key={`fg-${lossless?.frame_gen ?? "Off"}`} label={local.losslessFrameGen} options={frameGenOptions} value={lossless?.frame_gen ?? "Off"} onChange={onFrameGen} />
          <QuickSlider label={local.losslessMultiplier} value={Math.max(2, Math.min(4, lossless?.multiplier ?? 2))} min={2} max={4} suffix="x" onChange={onMultiplier} />
          <ToggleField
            bottomSeparator="none"
            label={local.drawFps}
            description={local.drawFpsDesc}
            checked={Boolean(lossless?.settings?.draw_fps)}
            onChange={onDrawFps}
          />
          <div className="qsMeta">
            {lossless?.can_scale ? local.losslessManagedHint : local.losslessGameOnlyHint}
          </div>
        </>)}

        {caps?.amd_radeon && amd?.available && section(<FaMicrochip />, local.amd, <>
          <div className="qsMeta">{local.amdGlobalHint}</div>
          {amd.rsr?.supported && <ToggleField bottomSeparator="none" label={local.amdRsr} description={local.amdRsrHint} checked={Boolean(amd.rsr.enabled)} onChange={(v: boolean) => onAmdToggle("rsr", v)} />}
          {amd.rsr?.supported && amd.rsr.enabled && <QuickSlider label={local.amdRsrSharpness} value={amd.rsr.sharpness ?? 0} min={amd.rsr.smin ?? 0} max={amd.rsr.smax ?? 100} onChange={(v: number) => queueAmdWrite("rsr_sharpness", v, AMD_SLIDER_SETTLE)} />}
          {amd.afmf?.supported && <ToggleField bottomSeparator="none" label={local.amdAfmf} description={local.amdAfmfHint} checked={Boolean(amd.afmf.enabled)} onChange={(v: boolean) => onAmdToggle("afmf", v)} />}
          {amd.antilag?.supported && <ToggleField bottomSeparator="none" label={local.amdAntilag} description={local.amdAntilagHint} checked={Boolean(amd.antilag.enabled)} onChange={(v: boolean) => onAmdToggle("antilag", v)} />}
          {amd.boost?.supported && <ToggleField bottomSeparator="none" label={local.amdBoost} description={local.amdBoostHint} checked={Boolean(amd.boost.enabled)} onChange={(v: boolean) => onAmdToggle("boost", v)} />}
          {amd.boost?.supported && amd.boost.enabled && <QuickSlider label={local.amdBoostResolution} value={amd.boost.resolution ?? 50} min={amd.boost.rmin ?? 50} max={amd.boost.rmax ?? 100} suffix="%" onChange={(v: number) => queueAmdWrite("boost_resolution", v, AMD_SLIDER_SETTLE)} />}
          {amd.enhanced_sync?.supported && <ToggleField bottomSeparator="none" label={local.amdEnhancedSync} description={local.amdEnhancedSyncHint} checked={Boolean(amd.enhanced_sync.enabled)} onChange={(v: boolean) => onAmdToggle("enhanced_sync", v)} />}
          {amd.chill?.supported && <ToggleField bottomSeparator="none" label={local.amdChill} description={local.amdChillHint} checked={Boolean(amd.chill.enabled)} onChange={(v: boolean) => onAmdToggle("chill", v)} />}
          {amd.chill?.supported && amd.chill.enabled && <QuickSlider label={local.amdChillMin} value={amd.chill.min ?? 0} min={amd.chill.fmin ?? 0} max={amd.chill.fmax ?? 240} onChange={(v: number) => queueAmdWrite("chill_min", v, AMD_SLIDER_SETTLE)} />}
          {amd.chill?.supported && amd.chill.enabled && <QuickSlider label={local.amdChillMax} value={amd.chill.max ?? 0} min={amd.chill.fmin ?? 0} max={amd.chill.fmax ?? 240} onChange={(v: number) => queueAmdWrite("chill_max", v, AMD_SLIDER_SETTLE)} />}
          {amd.sharpening?.supported && <ToggleField bottomSeparator="none" label={local.amdSharpening} description={local.amdSharpeningHint} checked={Boolean(amd.sharpening.enabled)} onChange={(v: boolean) => onAmdToggle("sharpening", v)} />}
          {amd.sharpening?.supported && amd.sharpening.enabled && <QuickSlider label={local.amdSharpeningValue} value={amd.sharpening.value ?? 0} min={amd.sharpening.smin ?? 0} max={amd.sharpening.smax ?? 100} step={amd.sharpening.step ?? 10} onChange={(v: number) => queueAmdWrite("sharpening_value", v, AMD_SLIDER_SETTLE)} />}
          {amd.display_color?.available && <div className="qsMeta">{local.amdDisplayColor}</div>}
          {amd.display_color?.brightness?.supported && <QuickSlider label={local.amdDisplayBrightness} value={amd.display_color.brightness.value ?? 0} min={amd.display_color.brightness.min ?? -100} max={amd.display_color.brightness.max ?? 100} onChange={(v: number) => queueAmdWrite("display_brightness", v, AMD_SLIDER_SETTLE)} />}
          {amd.display_color?.contrast?.supported && <QuickSlider label={local.amdDisplayContrast} value={amd.display_color.contrast.value ?? 100} min={amd.display_color.contrast.min ?? 0} max={amd.display_color.contrast.max ?? 200} onChange={(v: number) => queueAmdWrite("display_contrast", v, AMD_SLIDER_SETTLE)} />}
          {amd.display_color?.saturation?.supported && <QuickSlider label={local.amdDisplaySaturation} value={amd.display_color.saturation.value ?? 100} min={amd.display_color.saturation.min ?? 0} max={amd.display_color.saturation.max ?? 200} onChange={(v: number) => queueAmdWrite("display_saturation", v, AMD_SLIDER_SETTLE)} />}
          {amd.display_color?.temperature?.supported && <QuickSlider label={local.amdDisplayTemperature} value={amd.display_color.temperature.value ?? 6500} min={amd.display_color.temperature.min ?? 4000} max={amd.display_color.temperature.max ?? 10000} step={100} suffix=" K" onChange={(v: number) => queueAmdWrite("display_temperature", v, AMD_SLIDER_SETTLE)} />}
        </>)}

        {section(<FaTools />, local.advanced, <>
          <div className="qsStatus">{`${local.agentLabel}: ${agentRunning ? local.agentRunning : local.agentStopped}`}</div>
          <DialogButton disabled={agentRunning} onClick={onStartAgent}><span className="qsButtonInner"><FaPlay /><span>{local.startAgent}</span></span></DialogButton>
          <DialogButton disabled={!agentRunning} onClick={onStopAgent}><span className="qsButtonInner"><FaTools /><span>{local.stopAgent}</span></span></DialogButton>
          <div className="qsMeta">{local.agentHint}</div>
          <DialogButton disabled={diagnosticsBusy} onClick={onDiagnostics}><span className="qsButtonInner"><FaFileAlt /><span>{local.generateDiagnostics}</span></span></DialogButton>
        </>)}
      </Focusable>
    </div>
  );
}

export function installQuickSettings() {
  void (async () => {
    await syncAgentToMode();
    await preloadAudioControls();
  })();
  let cleanupGameIntegration: (() => void) | undefined;
  try {
    cleanupGameIntegration = installLosslessGameIntegration();
  } catch (error) {
    console.error("[Quick Settings] game integration failed", error);
  }
  let registration: any;
  try {
    registration = (window as any).SteamClient?.UI?.RegisterForUIModeChanged?.(() => void syncAgentToMode());
  } catch {
    /* ignore */
  }
  let overlayRegistration: any;
  try {
    overlayRegistration = (window as any).SteamClient?.Overlay?.RegisterForOverlayActivated?.(
      (
        overlayPid: number,
        appId: number,
        active: boolean,
        userInitiated: boolean,
      ) => {
        void setSteamOverlayActive(
          Boolean(active),
          Number(overlayPid || 0),
          Number(appId || 0),
          Boolean(userInitiated),
        );
      },
    );
  } catch {
    /* ignore */
  }
  return () => {
      cleanupGameIntegration?.();
      try {
        registration?.unregister?.();
      } catch {
        /* ignore */
      }
      try {
        overlayRegistration?.unregister?.();
      } catch {
        /* ignore */
      }
  };
}
