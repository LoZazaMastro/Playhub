import { DFL } from "./decky";
import { API_BASE } from "./api";

type HapticAction =
  | "moveUp" | "moveDown" | "moveLeft" | "moveRight"
  | "confirm" | "back" | "options" | "menu" | "dropdown"
  | "tabPrevious" | "tabNext" | "sliderDecrease" | "sliderIncrease"
  | "toggleOn" | "toggleOff" | "letter";

export interface NavigationHapticsConfig {
  enabled: boolean;
  intensity: number;
}

interface SoundPulse {
  delay: number;
  scale: number;
  kind: 1 | 2;
}

interface SoundRequest {
  id: number;
  action: HapticAction;
  controllerIndex: number;
  createdAt: number;
  timer: number;
}

interface HapticStep {
  delay: number;
  scale: number;
  kind: 1 | 2;
  side?: 0 | 1 | 2;
  duration?: number;
}

const MODERN_HAPTIC_CONTROLLERS = new Set([10, 48]);
const pending = new Set<number>();

let config: NavigationHapticsConfig = { enabled: false, intensity: 55 };
let lastControllerIndex = 0;
let nextSoundRequestId = 1;
let nextSoundSerial = 1;
let lastConsumedSoundSerial = 0;
let recentUiSound: { buffer: AudioBuffer; url: string; at: number; serial: number } | null = null;
let recentUiTrigger: { url: string; at: number } | null = null;
let lastActionForCapture: { action: HapticAction; at: number } | null = null;
const soundRequests = new Map<number, SoundRequest>();
const soundEnvelopeCache = new WeakMap<AudioBuffer, SoundPulse[]>();
const soundPlayerPatches: any[] = [];
const uiAudioManagers = new Set<any>();
const patchedManagerPrototypes = new Set<any>();
const patchedPlaybackPrototypes = new Set<any>();
const patchedAudioSourcePrototypes = new Set<any>();
const soundBuffersByUrl = new Map<string, AudioBuffer>();
const soundBuffersByAction = new Map<HapticAction, AudioBuffer>();
let playbackUrls = new WeakMap<object, string>();
let playbackAlreadyMatched = new WeakSet<object>();
const waveformTimers = new Set<number>();
let waveformGeneration = 0;
let lastNativeRequestId = Math.floor(Date.now() * 1000);
const gamepadTimestamps = new Map<number, number>();

function clampIntensity(value: number): number {
  return Math.max(5, Math.min(100, Math.round(value)));
}

function steamRoots(): any[] {
  const roots: any[] = [window];
  try {
    const steam = (DFL as any)?.findSP?.();
    if (steam && !roots.includes(steam)) roots.push(steam);
    if (steam?.window && !roots.includes(steam.window)) roots.push(steam.window);
  } catch {}

  try {
    const trees = (DFL as any)?.getGamepadNavigationTrees?.() ?? [];
    for (const tree of trees) {
      const treeWindow = tree?.Root?.Element?.ownerDocument?.defaultView;
      if (treeWindow && !roots.includes(treeWindow)) roots.push(treeWindow);
    }
  } catch {}

  for (const root of [...roots]) {
    const popups = root?.g_PopupManager?.m_mapPopups;
    if (!popups?.values) continue;
    for (const popup of Array.from(popups.values()) as any[]) {
      const popupWindow = popup?.m_popup ?? popup?.window;
      if (popupWindow && !roots.includes(popupWindow)) roots.push(popupWindow);
    }
  }
  return roots;
}

function steamIsForeground(): boolean {
  for (const root of steamRoots()) {
    const doc = root?.document;
    if (doc?.visibilityState === "visible" && doc?.hasFocus?.()) return true;
    if (root?.m_bFocused) return true;
  }
  return false;
}

function focusedControl(): HTMLElement | null {
  try {
    const controller = (DFL as any)?.getFocusNavController?.();
    const context = controller?.m_ActiveContext ?? controller?.m_LastActiveContext;
    const tree = context?.m_LastActiveFocusNavTree ?? context?.m_LastActiveNavTree;
    const element = tree?.m_lastFocusNode?.Element
      ?? tree?.m_lastFocusNode?.m_element
      ?? tree?.GetLastFocusedNode?.()?.Element
      ?? tree?.GetLastFocusedNode?.()?.m_element;
    if (element?.isConnected) return element as HTMLElement;
  } catch {}

  for (const root of steamRoots()) {
    const doc = root?.document as Document | undefined;
    if (!doc) continue;
    const gamepadFocus = doc.querySelector<HTMLElement>(
      ".gpfocus, [data-gp-focus='true'], [data-gpfocus='true']"
    );
    if (gamepadFocus) return gamepadFocus;
    const active = doc.activeElement as HTMLElement | null;
    if (active && active !== doc.body && (doc.hasFocus?.() || active.matches?.(":focus"))) return active;
  }
  return null;
}

function controlKind(element: HTMLElement | null): "slider" | "dropdown" | null {
  if (!element) return null;
  const selector = '[role="slider"], input[type="range"], [aria-haspopup], [role="combobox"]';
  const control = (element.closest?.(selector) ?? element.querySelector?.(selector)) as HTMLElement | null;
  if (!control) return null;
  if (control.matches('[role="slider"], input[type="range"]')) return "slider";
  const popup = control.getAttribute("aria-haspopup");
  return popup && popup !== "false" ? "dropdown" : control.getAttribute("role") === "combobox" ? "dropdown" : null;
}

function toggleAction(element: HTMLElement | null): HapticAction | null {
  if (!element) return null;
  const selector = '[role="switch"], [role="checkbox"], [aria-checked]';
  const toggle = (element.closest?.(selector) ?? element.querySelector?.(selector)) as HTMLElement | null;
  if (!toggle) return null;
  return toggle.getAttribute("aria-checked") === "true" ? "toggleOff" : "toggleOn";
}

function sliderValue(element: HTMLElement | null): number | null {
  if (!element) return null;
  const selector = '[role="slider"], input[type="range"]';
  const slider = (element.closest?.(selector) ?? element.querySelector?.(selector)) as HTMLElement | null;
  const value = Number(slider?.getAttribute("aria-valuenow") ?? (slider as HTMLInputElement | null)?.value);
  return Number.isFinite(value) ? value : null;
}

function controllerFor(index: number): any {
  const store = (window as any).ControllerStore;
  const controllers = Array.from(store?.GetControllers?.() ?? []) as any[];
  return store?.GetController?.(index)
    ?? controllers.find((controller: any) => Number(controller?.nControllerIndex) === index)
    ?? controllers.find((controller: any) => Number(controller?.nXInputIndex) === index && index !== 0xffffffff)
    ?? (controllers.length === 1 ? controllers[0] : undefined);
}

function usesModernHaptics(controller: any): boolean {
  const vendor = Number(controller?.unVendorID);
  const product = Number(controller?.unProductID);
  const dualSense = vendor === 0x054c && (product === 0x0ce6 || product === 0x0df2);
  return !dualSense && MODERN_HAPTIC_CONTROLLERS.has(Number(controller?.eControllerType));
}

function activeController(preferredIndex: number): any {
  const controller = controllerFor(preferredIndex);
  if (!controller) return undefined;
  const index = Number(controller?.nControllerIndex);
  return Number.isFinite(index) ? controller : undefined;
}

function controllerFromActiveSlot(value: unknown): any {
  const slot = Number((value as any)?.nActiveController ?? value);
  if (!Number.isInteger(slot) || slot < 0) return undefined;
  const store = (window as any).ControllerStore;
  const controllers = Array.from(store?.GetControllers?.() ?? []) as any[];
  const controller = controllers[slot] ?? store?.GetController?.(slot);
  return Number.isFinite(Number(controller?.nControllerIndex)) ? controller : undefined;
}

function gamepadHardwareId(id: string): { vendor: number; product: number } | null {
  const match = id.match(/Vendor:\s*([0-9a-f]{4})\s+Product:\s*([0-9a-f]{4})/i);
  if (!match) return null;
  return { vendor: Number.parseInt(match[1], 16), product: Number.parseInt(match[2], 16) };
}

function controllerForGamepad(gamepad: Gamepad): any {
  const hardware = gamepadHardwareId(gamepad.id);
  const controllers = Array.from((window as any).ControllerStore?.GetControllers?.() ?? []) as any[];
  if (hardware) {
    const exact = controllers.find((controller) =>
      Number(controller?.unVendorID) === hardware.vendor
      && Number(controller?.unProductID) === hardware.product
    );
    if (exact) return exact;
  }
  const name = gamepad.id.toLocaleLowerCase();
  return controllers.find((controller) => {
    const controllerName = String(controller?.strName ?? "").toLocaleLowerCase();
    return controllerName.length >= 5 && (name.includes(controllerName) || controllerName.includes(name));
  });
}

function controllerFromRecentGamepad(): any {
  let latest: { controller: any; timestamp: number } | null = null;
  for (const gamepad of Array.from(navigator.getGamepads?.() ?? []).filter(Boolean) as Gamepad[]) {
    const timestamp = Number(gamepad.timestamp) || 0;
    const previous = gamepadTimestamps.get(gamepad.index);
    gamepadTimestamps.set(gamepad.index, timestamp);
    if (previous === undefined || timestamp <= previous) continue;
    const controller = controllerForGamepad(gamepad);
    if (controller && (!latest || timestamp >= latest.timestamp)) latest = { controller, timestamp };
  }
  return latest?.controller;
}

function primeGamepadTimestamps() {
  gamepadTimestamps.clear();
  for (const gamepad of Array.from(navigator.getGamepads?.() ?? []).filter(Boolean) as Gamepad[]) {
    gamepadTimestamps.set(gamepad.index, Number(gamepad.timestamp) || 0);
  }
}

function later(callback: () => void, delay: number): number {
  const timer = window.setTimeout(() => {
    pending.delete(timer);
    callback();
  }, delay);
  pending.add(timer);
  return timer;
}

function nextNativeRequestId(): number {
  lastNativeRequestId = Math.max(lastNativeRequestId + 1, Math.floor(Date.now() * 1000));
  return lastNativeRequestId;
}

function actionSide(action: HapticAction): 0 | 1 | 2 {
  if (action === "moveLeft" || action === "sliderDecrease" || action === "tabPrevious") return 0;
  if (action === "moveRight" || action === "sliderIncrease" || action === "tabNext") return 1;
  return 2;
}

function discoverUiAudioManagers() {
  if (uiAudioManagers.size) return;
  try {
    const parent = (DFL as any)?.findModuleChild?.((module: any) => {
      if (!module || typeof module !== "object") return undefined;
      for (const key of Object.keys(module)) {
        if (module[key]?.GamepadUIAudio) return module[key];
      }
      return undefined;
    });
    [
      parent?.GamepadUIAudio?.m_AudioPlaybackManager,
      parent?.m_GamepadUIAudioStore?.m_AudioPlaybackManager,
      parent?.m_AudioPlaybackManager,
    ].forEach((manager) => {
      if (manager && (manager.PlayAudioURL || Object.getPrototypeOf(manager)?.PlayAudioURL)) {
        uiAudioManagers.add(manager);
      }
    });
  } catch {}
}

function isSteamUiSound(url: string): boolean {
  if (!url) return false;
  try {
    const parsed = new URL(url, window.location.href);
    if (parsed.origin !== window.location.origin) return false;
    return parsed.pathname.startsWith("/sounds/") || parsed.pathname.startsWith("/sounds_custom/");
  } catch {
    return url.startsWith("sounds/")
      || url.startsWith("/sounds/")
      || url.startsWith("sounds_custom/")
      || url.startsWith("/sounds_custom/");
  }
}

function soundEnvelope(buffer: AudioBuffer): SoundPulse[] {
  const cached = soundEnvelopeCache.get(buffer);
  if (cached) return cached;
  const sampleRate = Math.max(1, Number(buffer.sampleRate) || 48000);
  const frameCount = Math.min(buffer.length, Math.floor(sampleRate * 8));
  const duration = frameCount / sampleRate;
  const bucketSeconds = Math.max(0.01, duration / 240);
  const bucketFrames = Math.max(1, Math.floor(sampleRate * bucketSeconds));
  const channels = Math.max(1, Math.min(2, buffer.numberOfChannels));
  const data = Array.from({ length: channels }, (_, channel) => buffer.getChannelData(channel));
  const samples: Array<{ energy: number; crossings: number; peak: number }> = [];
  for (let start = 0; start < frameCount; start += bucketFrames) {
    const end = Math.min(frameCount, start + bucketFrames);
    const stride = Math.max(1, Math.floor((end - start) / 96));
    let sum = 0;
    let count = 0;
    let crossings = 0;
    let peak = 0;
    const previous = new Array(channels).fill(0);
    for (let frame = start; frame < end; frame += stride) {
      for (let channel = 0; channel < channels; channel += 1) {
        const value = data[channel][frame] ?? 0;
        sum += value * value;
        peak = Math.max(peak, Math.abs(value));
        if (value * previous[channel] < 0) crossings += 1;
        previous[channel] = value;
        count += 1;
      }
    }
    const rms = count ? Math.sqrt(sum / count) : 0;
    samples.push({ energy: rms * 0.68 + peak * 0.32, crossings, peak });
  }
  const ranked = samples.map((sample) => sample.energy).filter((value) => value > 0.0001).sort((left, right) => left - right);
  const reference = ranked[Math.max(0, Math.ceil(ranked.length * 0.97) - 1)] ?? 0;
  if (reference <= 0.0001) {
    const silent: SoundPulse[] = [{ delay: 0, scale: 0.72, kind: 1 }];
    soundEnvelopeCache.set(buffer, silent);
    return silent;
  }
  const smoothed = samples.map((_sample, index) => {
    const start = Math.max(0, index - 1);
    const end = Math.min(samples.length, index + 2);
    const window = samples.slice(start, end);
    return window.reduce((sum, sample) => sum + sample.energy, 0) / Math.max(1, window.length);
  });
  const threshold = reference * 0.035;
  const onset = Math.max(0, smoothed.findIndex((energy) => energy >= threshold));
  let lastActive = onset;
  for (let index = onset; index < smoothed.length; index += 1) {
    if (smoothed[index] >= threshold) lastActive = index;
  }
  const end = Math.min(samples.length, lastActive + 2);
  const envelope: SoundPulse[] = [];
  const bucketMs = bucketFrames / sampleRate * 1000;
  for (let index = onset; index < end; index += 1) {
    const sample = samples[index];
    const energy = smoothed[index];
    const previous = smoothed[index - 1] ?? 0;
    const normalized = Math.min(1.2, energy / reference);
    if (normalized < 0.045) continue;
    const transient = previous <= threshold || energy > previous * 1.32 || sample.peak > reference * 1.1;
    envelope.push({
      delay: Math.round((index - onset) * bucketMs),
      scale: Math.min(1.22, 0.18 + Math.pow(normalized, 0.72)),
      kind: transient || sample.crossings < channels * 5 ? 2 : 1,
    });
  }
  if (envelope[0]?.delay > 24) {
    const sample = samples[onset];
    envelope.unshift({ delay: 0, scale: 0.55, kind: sample.crossings >= channels * 10 ? 1 : 2 });
  }
  if (!envelope.length) envelope.push({ delay: 0, scale: 1, kind: 1 });
  soundEnvelopeCache.set(buffer, envelope);
  return envelope;
}

function emitSoundPulse(controllerIndex: number, action: HapticAction, pulse: SoundPulse) {
  if (!config.enabled || !steamIsForeground()) return;
  const controller = activeController(controllerIndex);
  if (!controller) return;
  const index = Number(controller.nControllerIndex);
  const side = actionSide(action);
  if (usesModernHaptics(controller)) {
    modernPulse(index, side, pulse.kind, pulse.scale);
  } else {
    rumblePulse(index, side, pulse.scale, pulse.kind === 2 ? 34 : 24);
  }
}

function playSoundEnvelope(controllerIndex: number, action: HapticAction, buffer: AudioBuffer, elapsed = 0) {
  if (!usesModernHaptics(controllerFor(controllerIndex))) {
    playActionPattern(controllerIndex, action);
    return;
  }
  waveformGeneration += 1;
  const generation = waveformGeneration;
  waveformTimers.forEach((timer) => {
    window.clearTimeout(timer);
    pending.delete(timer);
  });
  waveformTimers.clear();
  const envelope = soundEnvelope(buffer);
  const remaining = envelope.filter((pulse) => pulse.delay + 12 >= elapsed);
  const available = remaining.length ? remaining : [envelope[envelope.length - 1]];
  const limit = 240;
  const playable = available.length <= limit
    ? available
    : Array.from({ length: limit }, (_, index) => available[Math.round(index * (available.length - 1) / (limit - 1))]);
  const startedAt = performance.now();
  const playNext = (index: number) => {
    if (generation !== waveformGeneration || index >= playable.length) return;
    const pulse = playable[index];
    const delay = Math.min(8000, Math.max(0, pulse.delay - elapsed - (performance.now() - startedAt)));
    if (delay <= 1) {
      emitSoundPulse(controllerIndex, action, pulse);
      playNext(index + 1);
      return;
    }
    let timer = 0;
    timer = later(() => {
      waveformTimers.delete(timer);
      if (generation !== waveformGeneration) return;
      emitSoundPulse(controllerIndex, action, pulse);
      playNext(index + 1);
    }, delay);
    waveformTimers.add(timer);
  };
  playNext(0);
}

function discardSoundRequest(request: SoundRequest) {
  window.clearTimeout(request.timer);
  pending.delete(request.timer);
  soundRequests.delete(request.id);
}

function useUiSound(
  request: SoundRequest,
  sound: { buffer: AudioBuffer; url: string; at: number; serial: number }
) {
  discardSoundRequest(request);
  lastConsumedSoundSerial = Math.max(lastConsumedSoundSerial, sound.serial);
  soundBuffersByAction.set(request.action, sound.buffer);
  playSoundEnvelope(request.controllerIndex, request.action, sound.buffer, Math.max(0, performance.now() - sound.at));
}

function recordUiSound(buffer: AudioBuffer, url: string, playback?: object) {
  if (!config.enabled) return;
  if (!buffer || !Number.isFinite(buffer.duration) || buffer.duration < 0.01 || buffer.duration > 8) return;
  if (!isSteamUiSound(url)) return;
  soundBuffersByUrl.set(url, buffer);
  if (playback && playbackAlreadyMatched.has(playback)) return;
  const sound = { buffer, url, at: performance.now(), serial: nextSoundSerial++ };
  recentUiSound = sound;
  const request = Array.from(soundRequests.values())
    .filter((candidate) => sound.at - candidate.createdAt <= 220)
    .sort((left, right) => right.createdAt - left.createdAt)[0];
  if (request) {
    if (playback) playbackAlreadyMatched.add(playback);
    useUiSound(request, sound);
  }
}

function recordLiveUiSound(buffer: AudioBuffer, playback?: object) {
  if (!config.enabled || !buffer || !Number.isFinite(buffer.duration) || buffer.duration < 0.01 || buffer.duration > 8) return;
  if (playback && playbackAlreadyMatched.has(playback)) return;
  const at = performance.now();
  if (!recentUiTrigger || at - recentUiTrigger.at > 520) return;
  const request = Array.from(soundRequests.values())
    .filter((candidate) => at - candidate.createdAt <= 420)
    .sort((left, right) => right.createdAt - left.createdAt)[0];
  if (!request) {
    if (lastActionForCapture && at - lastActionForCapture.at <= 420) {
      soundBuffersByAction.set(lastActionForCapture.action, buffer);
    }
    return;
  }
  if (playback) playbackAlreadyMatched.add(playback);
  const sound = { buffer, url: "live://steam-ui", at, serial: nextSoundSerial++ };
  recentUiSound = sound;
  useUiSound(request, sound);
}

function bindLiveAudioPatches() {
  for (const root of steamRoots()) {
    const prototype = root?.AudioBufferSourceNode?.prototype;
    if (!prototype?.start || patchedAudioSourcePrototypes.has(prototype)) continue;
    try {
      const patch = (DFL as any).beforePatch(
        prototype,
        "start",
        function (this: AudioBufferSourceNode) {
          if (this?.buffer) recordLiveUiSound(this.buffer, this);
        }
      );
      patchedAudioSourcePrototypes.add(prototype);
      soundPlayerPatches.push(patch);
    } catch {}
  }
}

function bindPlaybackPrototype(playback: any) {
  const prototype = playback && Object.getPrototypeOf(playback);
  if (!prototype?.PlayBuffer || patchedPlaybackPrototypes.has(prototype)) return;
  try {
    const patch = (DFL as any).beforePatch(
      prototype,
      "PlayBuffer",
      function (this: any, args: [AudioBuffer]) {
        const buffer = args?.[0];
        const url = playbackUrls.get(this) ?? String(this?.url ?? "");
        if (buffer) recordUiSound(buffer, url, this);
      }
    );
    patchedPlaybackPrototypes.add(prototype);
    soundPlayerPatches.push(patch);
  } catch {}
}

function bindSoundPlayerPatches() {
  discoverUiAudioManagers();
  for (const manager of uiAudioManagers) {
    const prototype = Object.getPrototypeOf(manager);
    if (!prototype?.PlayAudioURLWithRepeats || patchedManagerPrototypes.has(prototype)) continue;
    try {
      const patch = (DFL as any).afterPatch(
        prototype,
        "PlayAudioURLWithRepeats",
        function (args: [string], playback: any) {
          const url = String(playback?.url ?? args?.[0] ?? "");
          if (!isSteamUiSound(url)) return playback;
          recentUiTrigger = { url, at: performance.now() };
          if (!playback) return playback;
          playbackUrls.set(playback, url);
          bindPlaybackPrototype(playback);
          const buffer = soundBuffersByUrl.get(url);
          if (buffer) recordUiSound(buffer, url, playback);
          return playback;
        }
      );
      patchedManagerPrototypes.add(prototype);
      soundPlayerPatches.push(patch);
    } catch {}
  }
}

function playFallback(controllerIndex: number, action: HapticAction) {
  const controller = activeController(controllerIndex);
  if (!controller) return;
  const index = Number(controller.nControllerIndex);
  const side = actionSide(action);
  if (usesModernHaptics(controller)) {
    modernPulse(index, side, 2, 1);
  } else {
    rumblePulse(index, side, 0.82, 28);
  }
}

function actionPattern(action: HapticAction): HapticStep[] {
  const side = actionSide(action);
  switch (action) {
    case "moveLeft":
    case "moveRight":
    case "moveUp":
    case "moveDown":
      return [{ delay: 0, scale: 1.18, kind: 2, side, duration: 19 }];
    case "tabPrevious":
    case "tabNext":
      return [
        { delay: 0, scale: 1.2, kind: 2, side, duration: 22 },
        { delay: 38, scale: 0.68, kind: 1, side, duration: 17 },
      ];
    case "sliderDecrease":
    case "sliderIncrease":
      return [
        { delay: 0, scale: 0.95, kind: 1, side, duration: 16 },
        { delay: 18, scale: 0.52, kind: 2, side, duration: 14 },
      ];
    case "toggleOn":
      return [
        { delay: 0, scale: 0.58, kind: 1, side: 0, duration: 18 },
        { delay: 34, scale: 1.25, kind: 2, side: 1, duration: 26 },
      ];
    case "toggleOff":
      return [
        { delay: 0, scale: 0.58, kind: 1, side: 1, duration: 18 },
        { delay: 34, scale: 1.08, kind: 2, side: 0, duration: 24 },
      ];
    case "confirm":
      return [
        { delay: 0, scale: 1.25, kind: 2, side: 2, duration: 25 },
        { delay: 46, scale: 0.62, kind: 1, side: 2, duration: 18 },
      ];
    case "back":
      return [
        { delay: 0, scale: 1.08, kind: 2, side: 0, duration: 22 },
        { delay: 32, scale: 0.48, kind: 1, side: 0, duration: 16 },
      ];
    case "dropdown":
      return [
        { delay: 0, scale: 0.68, kind: 1, side: 2, duration: 18 },
        { delay: 28, scale: 1.12, kind: 2, side: 2, duration: 24 },
      ];
    case "options":
    case "menu":
      return [
        { delay: 0, scale: 0.78, kind: 1, side: 2, duration: 18 },
        { delay: 30, scale: 1.16, kind: 2, side: 2, duration: 23 },
        { delay: 65, scale: 0.52, kind: 1, side: 2, duration: 16 },
      ];
    case "letter":
      return [{ delay: 0, scale: 1.28, kind: 2, side: 2, duration: 27 }];
  }
}

function playActionPattern(controllerIndex: number, action: HapticAction) {
  waveformGeneration += 1;
  const generation = waveformGeneration;
  waveformTimers.forEach((timer) => {
    window.clearTimeout(timer);
    pending.delete(timer);
  });
  waveformTimers.clear();
  const controller = activeController(controllerIndex);
  if (!controller) return;
  const index = Number(controller.nControllerIndex);
  const modern = usesModernHaptics(controller);
  if (!modern) {
    const vendor = Number(controller.unVendorID);
    if (vendor === 0x045e) {
      playLegacyPattern(index, action, generation);
      return;
    }
    void nativePattern(index, action).then((handled) => {
      if (!handled && generation === waveformGeneration && config.enabled && steamIsForeground()) {
        playLegacyPattern(index, action, generation);
      }
    });
    return;
  }

  for (const step of actionPattern(action)) {
    const emit = () => {
      if (generation !== waveformGeneration || !config.enabled || !steamIsForeground()) return;
      const side = step.side ?? actionSide(action);
      modernPulse(index, side, step.kind, step.scale);
    };
    if (step.delay === 0) emit();
    else {
      let timer = 0;
      timer = later(() => {
        waveformTimers.delete(timer);
        emit();
      }, step.delay);
      waveformTimers.add(timer);
    }
  }
}

function playLegacyPattern(index: number, action: HapticAction, generation: number) {
  for (const step of actionPattern(action)) {
    const emit = () => {
      if (generation !== waveformGeneration || !config.enabled || !steamIsForeground()) return;
      rumblePulse(index, step.side ?? actionSide(action), step.scale, step.duration ?? 20);
    };
    if (step.delay === 0) emit();
    else {
      let timer = 0;
      timer = later(() => {
        waveformTimers.delete(timer);
        emit();
      }, step.delay);
      waveformTimers.add(timer);
    }
  }
}

function modernPulse(index: number, side: number, kind: 1 | 2, scale: number) {
  const input = (window as any).SteamClient?.Input;
  const normalizedShape = Math.min(1, Math.max(0.03, scale));
  const soundShape = 0.18 + normalizedShape * 0.82;
  const requestedAmplitude = Math.max(0.04, (config.intensity / 12.5) * soundShape);
  const gain = Math.max(-12, Math.min(16, Math.round(1 + 20 * Math.log10(requestedAmplitude))));
  input?.TriggerSimpleHapticEvent?.(index, side, kind, 2, gain);
}

function browserRumble(index: number, side: number, scale: number, duration = 38): boolean {
  const pads = Array.from(navigator.getGamepads?.() ?? []) as Array<any | null>;
  const controller = controllerFor(index);
  const vendor = Number(controller?.unVendorID);
  const product = Number(controller?.unProductID);
  const connectedPads = pads.filter(Boolean) as any[];
  const hardwareMatches = connectedPads.filter((candidate) => {
    const hardware = gamepadHardwareId(String(candidate.id ?? ""));
    return hardware && hardware.vendor === vendor && hardware.product === product
      && (candidate.vibrationActuator ?? candidate.hapticActuators?.[0]);
  });
  const pad = (hardwareMatches.length === 1 ? hardwareMatches[0] : undefined)
    ?? (connectedPads.length === 1 ? connectedPads[0] : undefined);
  const actuator = pad?.vibrationActuator ?? pad?.hapticActuators?.[0];
  if (!actuator?.playEffect) return false;
  const strength = Math.pow(config.intensity / 100, 1.28);
  const normalized = Math.min(1, Math.max(0.04, strength * (0.18 + Math.min(1, scale) * 0.82) * 2));
  const both = side === 2;
  void actuator.playEffect("dual-rumble", {
    duration: Math.round(duration * (0.72 + config.intensity / 72)),
    startDelay: 0,
    strongMagnitude: both || side === 0 ? normalized : normalized * 0.1,
    weakMagnitude: both || side === 1 ? normalized : normalized * 0.1,
  }).catch(() => undefined);
  return true;
}

async function nativePattern(index: number, action: HapticAction): Promise<boolean> {
  const controller = controllerFor(index);
  const vendorId = Number(controller?.unVendorID);
  const productId = Number(controller?.unProductID);
  if (!Number.isInteger(vendorId) || vendorId <= 0 || !Number.isInteger(productId) || productId <= 0) return false;
  const magnitude = Math.min(1, Math.max(0.05, Math.pow(config.intensity / 100, 1.35) * 0.95));
  const controllerSignal = new AbortController();
  const timer = window.setTimeout(() => controllerSignal.abort(), 240);
  try {
    const response = await fetch(`${API_BASE}/dash/haptic`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        controllerIndex: index,
        vendorId,
        productId,
        side: actionSide(action),
        magnitude,
        pattern: action,
        requestId: nextNativeRequestId(),
      }),
      signal: controllerSignal.signal,
    });
    if (!response.ok) return false;
    const result = await response.json() as { handled?: boolean };
    return result.handled === true;
  } catch {
    return false;
  } finally {
    window.clearTimeout(timer);
  }
}

function rumblePulse(index: number, side: number, scale: number, duration = 38) {
  if (browserRumble(index, side, scale, duration)) return;
  const input = (window as any).SteamClient?.Input;
  const requestedAmplitude = Math.max(0.03, (config.intensity / 12.5) * scale);
  const gain = Math.max(-12, Math.min(16, Math.round(1 + 20 * Math.log10(requestedAmplitude))));
  if (input?.ForceSimpleHapticEvent) input.ForceSimpleHapticEvent(index, side, 2, 2, gain);
  else if (input?.TriggerSimpleHapticEvent) input.TriggerSimpleHapticEvent(index, side, 2, 2, gain);
  else input?.TriggerHapticPulse?.(index, side, Math.round(duration * 10), 0);
}

function stopBrowserRumble() {
  for (const pad of Array.from(navigator.getGamepads?.() ?? []).filter(Boolean) as any[]) {
    const actuator = pad.vibrationActuator ?? pad.hapticActuators?.[0];
    try {
      if (actuator?.reset) void Promise.resolve(actuator.reset()).catch(() => undefined);
      else if (actuator?.playEffect) {
        void actuator.playEffect("dual-rumble", {
          duration: 1,
          startDelay: 0,
          strongMagnitude: 0,
          weakMagnitude: 0,
        }).catch(() => undefined);
      }
    } catch {}
  }
}

function stopNativeHaptics() {
  void fetch(`${API_BASE}/dash/haptic`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ stop: true, requestId: nextNativeRequestId() }),
    keepalive: true,
  }).catch(() => undefined);
}

function stopActiveHaptics() {
  waveformGeneration += 1;
  waveformTimers.forEach((timer) => {
    window.clearTimeout(timer);
    pending.delete(timer);
  });
  waveformTimers.clear();
  stopBrowserRumble();
  stopNativeHaptics();
}

export function playNavigationHaptic(action: HapticAction, controllerIndex?: number) {
  if (!config.enabled || !steamIsForeground()) return;
  const controller = activeController(controllerIndex ?? lastControllerIndex);
  if (!controller) return;
  const index = Number(controller.nControllerIndex);
  lastControllerIndex = index;
  playActionPattern(index, action);
}

export function configureNavigationHaptics(next: Partial<NavigationHapticsConfig>) {
  config = {
    enabled: next.enabled ?? config.enabled,
    intensity: clampIntensity(next.intensity ?? config.intensity),
  };
  if (!config.enabled) {
    soundRequests.forEach(discardSoundRequest);
    stopActiveHaptics();
    recentUiSound = null;
    recentUiTrigger = null;
    lastActionForCapture = null;
  }
}

function actionForButton(button: number): HapticAction | null {
  const kind = controlKind(focusedControl());
  if (button === 30) return "tabPrevious";
  if (button === 31) return "tabNext";
  if (button === 0) return toggleAction(focusedControl()) ?? (kind === "dropdown" ? "dropdown" : "confirm");
  if (button === 1) return "back";
  if (button === 2 || button === 3) return "options";
  if (button === 8 || button === 9 || button === 34 || button === 35 || button === 36) return "menu";
  if (button === 4 || button === 10 || button === 20) return "moveUp";
  if (button === 6 || button === 11 || button === 21) return "moveDown";
  if (button === 7 || button === 12 || button === 22) return kind === "slider" ? null : "moveLeft";
  if (button === 5 || button === 13 || button === 23) return kind === "slider" ? null : "moveRight";
  return null;
}

export function installNavigationHaptics(): () => void {
  let registration: any;
  let activeControllerRegistration: any;
  let lastElement: HTMLElement | null = null;
  let lastIdentity = "";
  let lastBox: DOMRect | null = null;
  let lastSliderValue: number | null = null;
  let lastToggleState: string | null = null;
  let lastButtonAction: HapticAction | null = null;
  let lastButtonAt = 0;
  let lastFocusAction: HapticAction | null = null;
  let lastFocusActionAt = 0;
  const identities = new WeakMap<HTMLElement, number>();
  let nextIdentity = 1;
  let lastLetter = "";
  let lastLetterAt = 0;
  const letterObservers = new Map<Document, MutationObserver>();
  const focusDocuments = new Map<Document, { observer: MutationObserver; onValue: EventListener }>();
  let focusFrame: number | undefined;
  let focusContext: any;
  let focusRegistration: any;

  const playFocusAction = (action: HapticAction) => {
    const now = performance.now();
    if (now - lastButtonAt < 180) return;
    if (lastFocusAction === action && now - lastFocusActionAt < 140) return;
    lastFocusAction = action;
    lastFocusActionAt = now;
    playNavigationHaptic(action);
  };

  const playLetter = (value: string) => {
    const now = performance.now();
    if (value === lastLetter && now - lastLetterAt < 80) return;
    lastLetter = value;
    lastLetterAt = now;
    playNavigationHaptic("letter");
  };

  const inspectLetter = (node: Node) => {
    let candidate = (node.nodeType === Node.TEXT_NODE ? node.parentElement : node) as HTMLElement | null;
    for (let depth = 0; candidate && depth < 5; depth += 1) {
      if (candidate.childElementCount > 1) return;
      const value = candidate.textContent?.trim() ?? "";
      if (!/^[A-Z#]$/i.test(value)) return;
      if (candidate.childElementCount === 0) {
        const view = candidate.ownerDocument.defaultView;
        const box = candidate.getBoundingClientRect();
        const fontSize = Number.parseFloat(view?.getComputedStyle(candidate).fontSize ?? "0");
        if (box.width > 0 && box.height > 0 && fontSize >= 24) playLetter(value.toUpperCase());
        return;
      }
      candidate = candidate.firstElementChild as HTMLElement | null;
    }
  };

  const bindLetterObservers = () => {
    for (const root of steamRoots()) {
      const doc = root?.document as Document | undefined;
      if (!doc?.documentElement || letterObservers.has(doc)) continue;
      const Observer = root.MutationObserver as typeof MutationObserver | undefined;
      if (!Observer) continue;
      const observer = new Observer((mutations) => {
        if (!config.enabled || !steamIsForeground()) return;
        for (const mutation of mutations) {
          inspectLetter(mutation.target);
          mutation.addedNodes.forEach(inspectLetter);
        }
      });
      observer.observe(doc.documentElement, { childList: true, subtree: true, characterData: true });
      letterObservers.set(doc, observer);
    }
  };
  primeGamepadTimestamps();
  bindLetterObservers();
  const inspectFocus = () => {
    if (!config.enabled || !steamIsForeground()) return;
    const element = focusedControl();
    if (!element) return;
    let identity = identities.get(element);
    if (!identity) {
      identity = nextIdentity++;
      identities.set(element, identity);
    }
    const activeDescendant = element.getAttribute("aria-activedescendant") ?? "";
    const label = element.getAttribute("aria-label") ?? element.getAttribute("title") ?? element.textContent?.trim() ?? "";
    const selected = element.getAttribute("aria-selected") ?? "";
    const checked = element.getAttribute("aria-checked");
    const value = sliderValue(element);
    const signature = `${identity}:${activeDescendant}:${selected}:${checked ?? ""}:${value ?? ""}:${label.slice(0, 80)}`;
    if (signature === lastIdentity) return;

    const box = element.getBoundingClientRect();
    const previous = lastBox;
    const wasElement = lastElement;
    const previousSliderValue = lastElement === element ? lastSliderValue : null;
    const previousToggleState = lastElement === element ? lastToggleState : null;
    lastElement = element;
    lastIdentity = signature;
    lastBox = box;
    lastSliderValue = value;
    lastToggleState = checked;
    if (!wasElement) return;

    if (value !== null && previousSliderValue !== null && value !== previousSliderValue) {
      playFocusAction(value > previousSliderValue ? "sliderIncrease" : "sliderDecrease");
      return;
    }
    if (checked !== null && previousToggleState !== null && checked !== previousToggleState) {
      playFocusAction(checked === "true" ? "toggleOn" : "toggleOff");
      return;
    }

    if (/^[A-ZÀ-ÖØ-Þ0-9]$/i.test(label)) {
      playLetter(label.toUpperCase());
      return;
    }
    if (element.closest('[role="tab"]')) {
      const next = previous ? box.left >= previous.left : true;
      playFocusAction(next ? "tabNext" : "tabPrevious");
      return;
    }
    if (wasElement === element) return;
    const dx = previous ? box.left + box.width / 2 - (previous.left + previous.width / 2) : 0;
    const dy = previous ? box.top + box.height / 2 - (previous.top + previous.height / 2) : 0;
    if (Math.abs(dx) >= Math.abs(dy)) playFocusAction(dx < 0 ? "moveLeft" : "moveRight");
    else playFocusAction(dy < 0 ? "moveUp" : "moveDown");
  };

  const scheduleFocusInspection = () => {
    if (focusFrame !== undefined) return;
    focusFrame = window.requestAnimationFrame(() => {
      focusFrame = undefined;
      inspectFocus();
    });
  };

  const bindFocusContext = () => {
    const controller = (DFL as any)?.getFocusNavController?.();
    const context = controller?.m_ActiveContext ?? controller?.m_LastActiveContext;
    if (!context || context === focusContext) return;
    try { focusRegistration?.Unregister?.(); } catch {}
    focusContext = context;
    focusRegistration = context.m_FocusChangedCallbacks?.Register?.(scheduleFocusInspection);
    scheduleFocusInspection();
  };

  const bindFocusDocuments = () => {
    for (const root of steamRoots()) {
      const doc = root?.document as Document | undefined;
      const Observer = root?.MutationObserver as typeof MutationObserver | undefined;
      if (!doc?.documentElement || !Observer || focusDocuments.has(doc)) continue;
      const onValue = scheduleFocusInspection as EventListener;
      doc.addEventListener("focusin", onValue, true);
      doc.addEventListener("input", onValue, true);
      doc.addEventListener("change", onValue, true);
      const observer = new Observer(scheduleFocusInspection);
      observer.observe(doc.documentElement, {
        attributes: true,
        subtree: true,
        attributeFilter: ["aria-valuenow", "aria-checked", "aria-selected", "aria-activedescendant"],
      });
      focusDocuments.set(doc, { observer, onValue });
    }
  };

  bindFocusContext();
  bindFocusDocuments();
  const observerTimer = window.setInterval(() => {
    bindLetterObservers();
    bindFocusContext();
    bindFocusDocuments();
  }, 500);

  try {
    activeControllerRegistration = (window as any).SteamClient?.Input?.RegisterForActiveControllerChanges?.(
      (message: any) => {
        const controller = controllerFromActiveSlot(message);
        if (controller) lastControllerIndex = Number(controller.nControllerIndex);
      }
    );
    registration = (window as any).SteamClient?.Input?.RegisterForControllerInputMessages?.(
      (...args: any[]) => {
        const message = args[0] && typeof args[0] === "object" ? args[0] : undefined;
        const slotController = controllerFromActiveSlot(message);
        const controllerIndex = Number(
          slotController?.nControllerIndex
          ?? message?.nControllerIndex
          ?? message?.unControllerIndex
          ?? args[0]
        );
        const button = Number(message?.eButton ?? message?.nButton ?? message?.button ?? args[1]);
        const pressed = Boolean(message?.bPressed ?? message?.bDown ?? message?.pressed ?? args[2]);
        if (!pressed) return;
        if (Number.isInteger(controllerIndex) && activeController(controllerIndex)) lastControllerIndex = controllerIndex;
        const action = actionForButton(button);
        if (action) {
          lastButtonAction = action;
          lastButtonAt = performance.now();
          playNavigationHaptic(action, controllerIndex);
        }
      }
    );
  } catch {}

  return () => {
    stopActiveHaptics();
    window.clearInterval(observerTimer);
    if (focusFrame !== undefined) window.cancelAnimationFrame(focusFrame);
    focusFrame = undefined;
    try { focusRegistration?.Unregister?.(); } catch {}
    focusRegistration = undefined;
    focusContext = undefined;
    focusDocuments.forEach(({ observer, onValue }, doc) => {
      observer.disconnect();
      doc.removeEventListener("focusin", onValue, true);
      doc.removeEventListener("input", onValue, true);
      doc.removeEventListener("change", onValue, true);
    });
    focusDocuments.clear();
    letterObservers.forEach((observer) => observer.disconnect());
    letterObservers.clear();
    try { (registration?.Unregister ?? registration?.unregister)?.call(registration); } catch {}
    try { (activeControllerRegistration?.Unregister ?? activeControllerRegistration?.unregister)?.call(activeControllerRegistration); } catch {}
    pending.forEach((timer) => window.clearTimeout(timer));
    pending.clear();
    waveformTimers.clear();
    soundRequests.clear();
    soundPlayerPatches.splice(0).forEach((patch) => {
      try { patch?.unpatch?.(); } catch {}
    });
    uiAudioManagers.clear();
    patchedManagerPrototypes.clear();
    patchedPlaybackPrototypes.clear();
    patchedAudioSourcePrototypes.clear();
    soundBuffersByUrl.clear();
    soundBuffersByAction.clear();
    playbackUrls = new WeakMap<object, string>();
    playbackAlreadyMatched = new WeakSet<object>();
    gamepadTimestamps.clear();
    recentUiSound = null;
    recentUiTrigger = null;
    lastActionForCapture = null;
  };
}
