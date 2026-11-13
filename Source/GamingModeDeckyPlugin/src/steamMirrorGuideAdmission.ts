// GPL-3.0-only. One lifecycle-bound signal path; this never grants mirror ownership.
type Origin = "system" | "home" | "quick";
type Intent = "main" | "quick";
interface Scope { app: number; revision: number; disposed: boolean; enabled: boolean }
interface Dependencies {
  scope(): Scope; now(): number;
  guide(menu: Intent): Promise<unknown> | undefined;
  pending(): boolean; active(): boolean;
  verifiedAbsent(app: number): Promise<boolean>;
  trace(stage: string): void;
}
export function createSteamMirrorGuideAdmission(deps: Dependencies) {
  type Edge = { app: number; revision: number; menu: Intent; at: number; origins: Set<Origin>; pending: boolean; absenceDone?: boolean; fallback?: () => void };
  const recent = new Map<Intent, Edge>();
  let latestDispatched: Edge | undefined;
  const trace = (stage: string) => { try { deps.trace(stage); } catch {} };
  const scope = () => { try { return deps.scope(); } catch { trace("drop-scope-read"); return undefined; } };
  const valid = (e: Edge) => {
    const s = scope(); const reason = !s ? "scope" : s.disposed ? "disposed" : !s.enabled ? "capability"
      : s.app !== e.app ? "app" : s.revision !== e.revision ? "revision" : undefined;
    if (reason) trace(`drop-${reason}`); return !reason;
  };
  const finish = async (edge: Edge) => {
    if (!valid(edge) || !edge.fallback || edge.absenceDone || deps.pending() || deps.active()) return;
    edge.absenceDone = true;
    let absent = false;
    try { absent = await deps.verifiedAbsent(edge.app); }
    catch { trace("absence-unknown"); return; }
    if (!valid(edge)) return;
    if (absent && !deps.pending() && !deps.active()) { trace("verified-absence-original"); edge.fallback(); }
  };
  return {
    signal(origin: Origin, menu: Intent, fallback?: () => void) {
      const s = scope();
      if (!s) { trace("drop-scope-unknown"); return true; }
      if (!Number.isSafeInteger(s.app)) { trace("drop-app-unknown"); return true; }
      if (s.disposed || !s.enabled || s.app < 0x80000000 || s.app > 0xffffffff) { trace("drop-entry"); return false; }
      let now: number;
      try { now = deps.now(); } catch { trace("drop-clock-unknown"); return true; }
      if (!Number.isFinite(now) || now < 0) { trace("drop-clock-unknown"); return true; }
      trace(`received origin=${origin} intent=${menu} at=${Math.round(now)} app=${s.app} rev=${s.revision}`);
      const previous = recent.get(menu);
      // Quick Access selects the pending side. A repeated notification of
      // that SAME opening is idempotent; Main remains a toggle/cancel.
      if (menu === "quick" && previous && previous === latestDispatched && previous.pending
          && previous.app === s.app && previous.revision === s.revision) {
        let opening = false;
        try { opening = deps.pending(); } catch { trace("drop-pending-unknown"); return true; }
        if (opening && valid(previous)) {
          previous.origins.add(origin); previous.fallback ??= fallback;
          trace(`coalesced-pending-quick origin=${origin}`); return true;
        }
      }
      // Two observers of one press may be separated by native/CEF work. During
      // the same open/close transaction, pair each opposite-origin edge once.
      // Outside the pending Quick selection above, same-origin edges remain toggles.
      if (previous && previous.app === s.app && previous.revision === s.revision
          && (menu !== "quick" || previous === latestDispatched) && !previous.origins.has(origin)
          && now >= previous.at && (now - previous.at <= 80 || previous.pending)) {
        previous.origins.add(origin); previous.fallback ??= fallback;
        if (!previous.pending) void finish(previous).catch(() => trace("fallback-rejected"));
        trace(`coalesced origin=${origin} intent=${menu}`); return true;
      }
      const edge: Edge = { app: s.app, revision: s.revision, menu, at: now, origins: new Set([origin]), pending: true, fallback };
      recent.set(menu, edge); latestDispatched = edge;
      if (!valid(edge)) return true;
      let operation: Promise<unknown> | undefined;
      try { trace(`dispatch intent=${menu}`); operation = deps.guide(menu); }
      catch { edge.pending = false; trace("request-threw"); return true; }
      void Promise.resolve(operation).then(async () => {
        edge.pending = false;
        if (!valid(edge)) return;
        trace(`request-settled pending=${deps.pending()} active=${deps.active()}`);
        // Unknown/rejected ownership probes never invoke native Home fallback.
        // Only the existing verifier's explicit resolved absence does so.
        await finish(edge);
      }).catch(() => { edge.pending = false; trace("request-rejected"); });
      return true;
    },
    invalidate() { recent.clear(); latestDispatched = undefined; },
  };
}
