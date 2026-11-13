const menuLayout = "steam://forceinputappid/769";
const automaticLayout = "steam://forceinputappid/0";
const returnBudgetMs = 1200;

export interface SteamOverlayLayoutContext { window: object; menu: object }
export interface SteamOverlayLayoutProof {
  ok: boolean; gameId?: string; sessionId?: string; companionPid?: number;
  overlayPid?: number; foregroundScope?: number; overlayOpen?: boolean;
}
export interface SteamOverlayLayoutSnapshot extends SteamOverlayLayoutProof { kind?: string }
export interface SteamOverlayLayoutExitProof {
  ok: boolean; sessionId?: string; gameId?: string;
  sourceExited?: boolean; steamForeground?: boolean; otherGameRunning?: boolean;
}
export interface SteamOverlayGameInputProof {
  ok: boolean; sessionId: string; sourceId: string; epoch: number; sequence: number;
  sampleAgeMs: number; ready: boolean; route: "bridge" | "steam-native";
  foregroundScope?: number; gameAlive?: boolean;
}
interface LayoutDependencies {
  controller(): object | undefined;
  currentApp(): number;
  contextCurrent(context: SteamOverlayLayoutContext): boolean;
  menuClosed(context: SteamOverlayLayoutContext): boolean;
  readSnapshot(app: number, overlayPid: number): Promise<SteamOverlayLayoutSnapshot>;
  // Every Playhub dispatcher must participate in one authority. This is
  // command ownership, not a fabricated getter for Steam's global override.
  claim(owner: object): boolean;
  owns(owner: object): boolean;
  forget(owner: object): void;
  // Return true only when submitted. The real dispatcher must check lastGuard
  // immediately before sending; this boolean does not prove a game consumed input.
  execute(url: string, lastGuard: () => boolean): boolean;
  schedule(callback: () => void, ms: number): unknown;
  cancel(timer: unknown): void;
  now(): number;
  inputBaseline?(sessionId: string): SteamOverlayGameInputProof | undefined;
  readGameInput?(app: number, sessionId: string): Promise<SteamOverlayGameInputProof | undefined>;
  trace?(stage: string): void;
}
interface Lease {
  app: number; gameId: string; sessionId: string; companionPid: number; overlayPid: number;
  controller: object; context: SteamOverlayLayoutContext; owner: object; submitted: boolean;
  input?: SteamOverlayGameInputProof;
}
const sameContext = (a: SteamOverlayLayoutContext, b: SteamOverlayLayoutContext) => a.window === b.window && a.menu === b.menu;
function identity(app: number, proof: SteamOverlayLayoutProof): boolean {
  if (!Number.isSafeInteger(app) || app < 0x80000000 || app > 0xffffffff || proof?.ok !== true
      || typeof proof.sessionId !== "string" || !/^[0-9a-f]{64}$/i.test(proof.sessionId)
      || typeof proof.gameId !== "string" || !/^[1-9][0-9]{0,19}$/.test(proof.gameId)
      || !Number.isSafeInteger(proof.companionPid) || Number(proof.companionPid) <= 0 || Number(proof.companionPid) > 0xffffffff
      || !Number.isSafeInteger(proof.overlayPid) || Number(proof.overlayPid) <= 0 || Number(proof.overlayPid) > 0xffffffff) return false;
  try { const id = BigInt(proof.gameId); return id <= 0xffffffffffffffffn && (id >> 32n) === BigInt(app) && (id & 0xffffffffn) === 0x02000000n; }
  catch { return false; }
}
function sameSession(lease: Lease, proof: SteamOverlayLayoutProof): boolean {
  return identity(lease.app, proof) && proof.sessionId!.toLowerCase() === lease.sessionId
    && proof.gameId === lease.gameId && proof.companionPid === lease.companionPid && proof.overlayPid === lease.overlayPid;
}

/** Own a menu override, release only that override, then separately observe input. */
export function createSteamOverlayControllerLayout(deps: LayoutDependencies) {
  let lease: Lease | undefined, disposed = false, revision = 0, phase = "off";
  let wait: { timer: unknown; finish(): void } | undefined;
  const safe = (read: () => boolean) => { try { return read(); } catch { return false; } };
  const owned = (value: Lease) => lease === value && safe(() => deps.owns(value.owner));
  const trace = (stage: string) => { try { deps.trace?.(stage); } catch {} };
  function cancelWait() {
    const pending = wait; wait = undefined;
    if (pending) { try { deps.cancel(pending.timer); } catch {} pending.finish(); }
  }
  function retire(value: Lease, stage: string) {
    if (lease !== value) return;
    lease = undefined; revision++; cancelWait();
    try { deps.forget(value.owner); } catch {} trace(stage);
  }
  function bounded<T>(read: (() => Promise<T>) | undefined, ms: number): Promise<{ ok: true; value: T } | { ok: false }> {
    return new Promise(resolve => {
      let finished = false;
      const pending = { timer: undefined as unknown, finish: () => finish({ ok: false }) };
      function finish(result: { ok: true; value: T } | { ok: false }) {
        if (finished) return;
        finished = true;
        if (wait === pending) wait = undefined;
        try { deps.cancel(pending.timer); } catch {}
        resolve(result);
      }
      wait = pending;
      try {
        pending.timer = deps.schedule(() => finish({ ok: false }), Math.max(0, ms));
        if (read) void Promise.resolve().then(async () => {
          if (finished) return;
          try { finish({ ok: true, value: await read() }); }
          catch { finish({ ok: false }); }
        });
      } catch { finish({ ok: false }); }
    });
  }
  const pause = (ms: number) => bounded<never>(undefined, ms);
  const observing = (value: Lease, version: number, deadline: number) => revision === version && !disposed
    && deps.now() <= deadline && safe(() => deps.currentApp() === value.app && deps.controller() === value.controller);
  function inputIdentity(proof: SteamOverlayGameInputProof | undefined, sessionId: string): proof is SteamOverlayGameInputProof {
    return proof?.ok === true && typeof proof.sessionId === "string" && proof.sessionId.toLowerCase() === sessionId
      && typeof proof.sourceId === "string" && /^[0-9a-f]{64}$/i.test(proof.sourceId)
      && Number.isSafeInteger(proof.epoch) && proof.epoch >= 0 && Number.isSafeInteger(proof.sequence) && proof.sequence >= 0
      && Number.isFinite(proof.sampleAgeMs) && proof.sampleAgeMs >= 0 && proof.sampleAgeMs <= 500
      && (proof.route === "bridge" || proof.route === "steam-native");
  }
  function observationEnded(version: number) {
    if (revision === version && !disposed) { phase = "input-unverified"; trace("game-return-observation-ended"); }
  }
  async function observeGameInput(value: Lease, version: number, deadline: number) {
    const baseline = value.input;
    if (!baseline || !deps.readGameInput) { phase = "input-unverified"; trace("game-input-readback-unavailable"); return; }
    let first: SteamOverlayGameInputProof | undefined;
    for (const delay of [0, 80, 160]) {
      if (delay) await pause(delay);
      if (!observing(value, version, deadline)) { observationEnded(version); return; }
      const result = await bounded(() => deps.readGameInput!(value.app, value.sessionId), deadline - deps.now());
      if (!observing(value, version, deadline)) { observationEnded(version); return; }
      if (!result.ok) break;
      const proof = result.value;
      if (!inputIdentity(proof, value.sessionId) || proof.sourceId.toLowerCase() !== baseline.sourceId.toLowerCase()
          || proof.route !== baseline.route || proof.epoch <= baseline.epoch || proof.ready !== true
          || proof.foregroundScope !== 1 || proof.gameAlive !== true) continue;
      if (first && proof.epoch === first.epoch && proof.sequence > first.sequence) {
        phase = "input-observed"; trace("game-input-new-epoch-observed"); return;
      }
      first = proof;
    }
    phase = "input-unverified"; trace("game-input-readback-unconfirmed");
  }
  async function observeReturn(value: Lease, version: number) {
    const deadline = deps.now() + returnBudgetMs;
    for (const delay of [0, 40, 120, 240]) {
      if (delay) await pause(delay);
      if (!observing(value, version, deadline)) { observationEnded(version); return; }
      const result = await bounded(() => deps.readSnapshot(value.app, value.overlayPid), deadline - deps.now());
      if (!observing(value, version, deadline)) { observationEnded(version); return; }
      if (!result.ok) break;
      const proof = result.value;
      if (proof?.kind !== "owned-session" || !sameSession(value, proof) || (proof.foregroundScope !== 1 && proof.foregroundScope !== 2)) break;
      if (proof.foregroundScope === 2) continue;
      await observeGameInput(value, version, deadline); return;
    }
    phase = "input-unverified"; trace("game-return-not-observed");
  }
  function cleanup(readInput = true): boolean {
    const value = lease;
    if (!value) return false;
    if (!owned(value)) { retire(value, "layout-authority-changed"); phase = "external"; return false; }
    // Automatic0 releases OUR override; it does not select a foreign profile.
    // Alt+Tab, disconnect and teardown therefore do not require game scope1.
    try {
      if (!deps.execute(automaticLayout, () => owned(value))) { if (lease === value) phase = "cleanup-pending"; return false; }
    } catch { phase = "cleanup-pending"; trace("automatic-layout-request-uncertain"); return false; }
    if (lease !== value) return false;
    retire(value, "automatic-layout-requested");
    phase = readInput ? "awaiting-game-input" : "released";
    const version = revision;
    if (readInput && !disposed) void observeReturn(value, version).catch(() => {
      if (revision === version) { phase = "input-unverified"; trace("game-input-readback-unavailable"); }
    });
    return true;
  }
  return {
    acquire(app: number, proof: SteamOverlayLayoutProof, context: SteamOverlayLayoutContext): boolean {
      if (disposed || !identity(app, proof) || proof.foregroundScope !== 2 || proof.overlayOpen !== true
          || !context?.window || typeof context.window !== "object" || !context.menu || typeof context.menu !== "object"
          || !safe(() => deps.currentApp() === app && deps.contextCurrent(context))) return false;
      let controller: object | undefined; try { controller = deps.controller(); } catch { return false; }
      if (!controller || typeof controller !== "object") return false;
      if (lease && owned(lease)) {
        if (!sameSession(lease, proof)) return false;
        revision++; cancelWait(); lease.context = { ...context }; lease.controller = controller;
        return lease.submitted;
      }
      if (lease) retire(lease, "layout-authority-changed");
      const owner = {};
      if (!safe(() => deps.claim(owner))) return false;
      revision++; cancelWait();
      const value: Lease = { app, gameId: proof.gameId!, sessionId: proof.sessionId!.toLowerCase(), companionPid: proof.companionPid!, overlayPid: proof.overlayPid!, controller, context: { ...context }, owner, submitted: false };
      try { const input = deps.inputBaseline?.(value.sessionId); if (inputIdentity(input, value.sessionId)) value.input = { ...input }; } catch {}
      lease = value;
      const current = () => !disposed && owned(value) && safe(() => deps.currentApp() === app && deps.contextCurrent(value.context) && deps.controller() === controller);
      if (!current()) { retire(value, "layout-acquire-canceled"); return false; }
      try {
        if (!deps.execute(menuLayout, current)) { retire(value, "menu-layout-not-dispatched"); return false; }
        value.submitted = true; phase = "menu"; trace("menu-layout-requested"); return true;
      } catch { phase = "cleanup-pending"; trace("menu-layout-request-uncertain"); return false; }
    },
    closed(context: SteamOverlayLayoutContext) {
      if (lease && context && sameContext(lease.context, context) && safe(() => deps.menuClosed(context))) cleanup();
    },
    controllerChanged() {
      if (lease && !safe(() => deps.controller() === lease!.controller)) cleanup(false);
    },
    leftContext() { cleanup(); },
    authorityChanged() {
      revision++; cancelWait();
      if (lease && !owned(lease)) retire(lease, "layout-authority-changed");
      phase = "external";
    },
    retryCleanup() { return cleanup(!disposed); },
    exited(proof: SteamOverlayLayoutExitProof): boolean {
      const value = lease;
      return Boolean(value && proof?.ok === true && typeof proof.sessionId === "string" && proof.sessionId.toLowerCase() === value.sessionId
        && proof.gameId === value.gameId && proof.sourceExited === true && proof.steamForeground === true && proof.otherGameRunning === false && cleanup(false));
    },
    dispose() { disposed = true; revision++; cancelWait(); if (!lease) phase = "released"; cleanup(false); },
    get cleanupPending() { return lease !== undefined; },
    get phase() { return phase; },
  };
}
