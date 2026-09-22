import type { ReactNode } from "react";
import { createLegacyShortcutsDeckyHost } from "./deckyHostStandalone";
import { installDeckyTabProjection } from "./deckyHostProjection";
import { setNativeDeckyHidden } from "./deckyNativeTabPreference";

export interface DeckyHostSnapshot {
  enabled: boolean;
  available: boolean;
  ready: boolean;
  reason: string;
  ownerRevision?: number;
}
export interface DeckyHostLease {
  panel: ReactNode;
  renew(): boolean;
  setActive(active: boolean): boolean;
  setReady(): boolean;
  setNativeHidden?(hidden: boolean): boolean;
  release(reason?: string): void;
}
export interface Capability {
  protocol: number;
  isAvailable(): boolean;
  /** Standing native-tab suppression, independent of any acquired lease. */
  setNativeHidden?(hidden: boolean): void;
  acquire(options: {
    element: HTMLElement;
    createContent(root: ReactNode): ReactNode;
    isHealthy(): boolean;
    onRelease(reason: string): void;
    onVisibility?(visible: boolean): void;
    hideNative?: boolean;
  }): DeckyHostLease | null;
}
const BRIDGE = Symbol.for("shortcuts.qam-bridge.v1");
const INSTANCE = Symbol.for("playhub.decky-host.v1");
const EVENT = "shortcuts:qam-bridge-changed";
const listeners = new Set<() => void>();
let snapshot: DeckyHostSnapshot = { enabled: false, available: false, ready: false, reason: "disabled" };
let capability: Capability | undefined;
let releaseMount: (() => void) | undefined;
let running = false;
let failure: string | undefined;
let projectionFailure: string | undefined;
let nativeHidden = true;
let preferenceRevision = 0;
let mountedLease: DeckyHostLease | undefined;
let batching = false;
let legacyHost: ReturnType<typeof createLegacyShortcutsDeckyHost> = null;
let legacyBridge: unknown;
let legacyHook: unknown;

export const getDeckyHostSnapshot = () => snapshot;
export const subscribeDeckyHost = (listener: () => void) => {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
};
function publish(next: DeckyHostSnapshot) {
  if (JSON.stringify(next) === JSON.stringify(snapshot)) return;
  snapshot = next;
  if (!batching) listeners.forEach(listener => listener());
}
function release() { releaseMount?.(); }
/** The preference alone hides the native tab; mounting Playhub's tab is not a precondition. */
function applyNativeSuppression() {
  const effective = running && snapshot.enabled === true && nativeHidden && !projectionFailure;
  setNativeDeckyHidden(effective);
  try { capability?.setNativeHidden?.(effective); } catch { /* Leave the original Decky tab accessible. */ }
}
export function setDeckyNativeHidden(hidden: boolean) {
  preferenceRevision += 1;
  nativeHidden = hidden === true;
  const applied = mountedLease?.setNativeHidden?.(nativeHidden) ?? !mountedLease;
  applyNativeSuppression();
  return applied;
}
function reconcile() {
  let next: Capability | undefined;
  let available = false;
  try {
    const bridge = (window as any)[BRIDGE];
    const inventory = (window as any).DeckyPluginLoader?.deckyState?.publicState?.();
    const disabled = inventory?.disabledPlugins?.some((item: any) => String(typeof item === "string" ? item : item?.name).toLowerCase() === "shortcuts");
    const useLegacy = !disabled && bridge?.protocol === 1 && !bridge.deckyHost;
    const hook = (window as any).DeckyPluginLoader?.tabsHook;
    if (legacyHost && (!useLegacy || legacyBridge !== bridge || legacyHook !== hook || !legacyHost.ownsHook())) {
      release(); legacyHost.stop(); legacyHost = null;
    }
    if (useLegacy && !legacyHost && hook) {
      legacyHost = createLegacyShortcutsDeckyHost(hook, bridge);
      legacyBridge = bridge; legacyHook = hook;
    }
    const value = !disabled && bridge?.protocol === 1 ? bridge.deckyHost ?? legacyHost?.capability
      : (window as any)[Symbol.for("playhub.decky-host-standalone.v1")];
    if (value?.protocol === 1 && typeof value.acquire === "function" && typeof value.isAvailable === "function") {
      next = value;
      available = value.isAvailable() === true;
    }
  } catch { /* Unknown capability: leave the original Decky tab accessible. */ }
  const changed = next !== capability;
  if (changed && capability) { try { capability.setNativeHidden?.(false); } catch { /* Replaced owner. */ } }
  if (changed || !available) release();
  // A new healthy owner gets its own mount attempt; never retry a failing root
  // continuously on the same capability.
  if (changed && next && available) failure = undefined;
  capability = next;
  const blocked = projectionFailure ?? failure;
  publish({ ...snapshot, ownerRevision: (snapshot.ownerRevision ?? 0) + (changed ? 1 : 0), available: running && available && !blocked,
    ready: snapshot.ready && available && !blocked,
    reason: blocked ?? (!snapshot.enabled ? "disabled" : available ? "waiting_for_mount" : "native_capability_unavailable") });
  applyNativeSuppression();
}
export function setDeckyHostEnabled(enabled: boolean) {
  failure = undefined;
  const before = JSON.stringify(snapshot);
  batching = true;
  try {
    if (!enabled) {
      try { release(); } catch {
        publish({ ...snapshot, reason: "native_restore_failed" });
        return;
      }
    }
    publish({ ...snapshot, enabled: enabled === true, ready: enabled && snapshot.ready, reason: enabled ? "waiting_for_mount" : "disabled" });
    if (running) reconcile(); else applyNativeSuppression();
  } finally {
    batching = false;
    if (JSON.stringify(snapshot) !== before) listeners.forEach(listener => listener());
  }
}
export function failDeckyHost(reason: string) {
  failure = reason;
  release();
  publish({ ...snapshot, available: false, ready: false, reason });
  applyNativeSuppression();
}
export function mountDeckyHost(options: Parameters<Capability["acquire"]>[0]): DeckyHostLease | null {
  if (!running || !snapshot.enabled || !snapshot.available || releaseMount || !capability) return null;
  const lease = capability.acquire({ ...options, hideNative: nativeHidden, onRelease(reason) {
    if (reason === "shortcuts_unloaded") reason = "owner_changed";
    if (reason === "owner_changed") {
      publish({ ...snapshot, available: false, ready: false, ownerRevision: (snapshot.ownerRevision ?? 0) + 1 });
    }
    options.onRelease(reason);
  } });
  if (!lease || typeof lease.release !== "function" || typeof lease.renew !== "function"
    || typeof lease.setActive !== "function" || typeof lease.setReady !== "function" || lease.panel == null) {
    lease?.release?.("invalid_lease");
    return null;
  }
  let alive = true;
  const stop = () => {
    if (!alive) return;
    lease.release("host_unmounted");
    alive = false;
    if (releaseMount === stop) releaseMount = undefined;
    if (mountedLease === lease) mountedLease = undefined;
    publish({ ...snapshot, ready: false });
  };
  releaseMount = stop;
  mountedLease = lease;
  return { ...lease,
    release: stop,
    setReady() {
      // Legacy owners cannot separate hosting from suppression. Keep their native tab.
      const accepted = alive && (typeof lease.setNativeHidden !== "function" || lease.setReady());
      publish({ ...snapshot, ready: accepted });
      return accepted;
    },
  };
}

/** The parent persists the opt-in. Starting/reloading this runtime is always OFF. */
export function initDeckyHost(readPreferences?: () => Promise<{ deckyHostEnabled?: boolean }>): () => void {
  const host = window as any;
  host[INSTANCE]?.();
  running = true;
  failure = undefined;
  projectionFailure = undefined;
  publish({ enabled: false, available: false, ready: false, reason: "disabled" });
  const projection = installDeckyTabProjection(() => {
    projectionFailure = "native_projection_owner_conflict";
    failDeckyHost(projectionFailure);
  });
  let stopped = false;
  let preferencesLoaded = !readPreferences;
  let preferencesLoading = false;
  const initialPreferenceRevision = preferenceRevision;
  const loadPreferences = async () => {
    if (stopped || preferencesLoaded || preferencesLoading || !readPreferences) return;
    preferencesLoading = true;
    try {
      const preferences = await readPreferences();
      if (stopped) return;
      preferencesLoaded = true;
      if (preferenceRevision === initialPreferenceRevision) {
        setDeckyNativeHidden(preferences?.deckyHostEnabled !== false);
        setDeckyHostEnabled(true);
      }
    } catch { /* Keep the native tab accessible; retry when the backend is ready. */ }
    finally { preferencesLoading = false; }
  };
  const timer = window.setInterval(() => { projection.reconcile(); void loadPreferences(); reconcile(); }, 1000);
  const stop = () => {
    if (stopped) return;
    stopped = true;
    running = false;
    release();
    projection.stop();
    legacyHost?.stop(); legacyHost = null; legacyBridge = legacyHook = undefined;
    window.clearInterval(timer);
    window.removeEventListener(EVENT, reconcile);
    window.removeEventListener("pagehide", stop);
    setNativeDeckyHidden(false);
    try { capability?.setNativeHidden?.(false); } catch { /* Replaced owner. */ }
    capability = undefined;
    if (host[INSTANCE] === stop) delete host[INSTANCE];
    publish({ enabled: false, available: false, ready: false, reason: "disabled" });
  };
  (stop as any).getSnapshot = getDeckyHostSnapshot;
  host[INSTANCE] = stop;
  window.addEventListener(EVENT, reconcile);
  window.addEventListener("pagehide", stop);
  reconcile();
  void loadPreferences();
  return stop;
}
