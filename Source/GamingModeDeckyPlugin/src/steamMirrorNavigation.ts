import type { OverlayIdentity } from "./dashboardOverlay";
import type { MirrorPresentation } from "./steamMirrorOverlay";

export interface MirrorNavigationProof {
  app: number;
  browser: OverlayIdentity;
  overlay: any;
  menu: any;
  mirror: MirrorPresentation;
  intent: "main" | "quick";
}
export interface MirrorNavigationState {
  attempted: boolean;
  contextExact: boolean;
  treeExact: boolean;
  nodeExact: boolean;
  confirmed: boolean;
}
export interface MirrorNavigation {
  activate(): MirrorNavigationState;
  read(proof: MirrorPresentation): MirrorNavigationState;
}

const keys = ["sessionId", "generation", "gameId", "sourceHwnd", "sourceCookie", "destinationHwnd", "destinationCookie",
  "ownerPid", "ownerBirth", "gamePid", "gameBirth"] as const;
const u64 = (v: unknown): v is string => typeof v === "string" && /^[1-9]\d{0,19}$/.test(v) && BigInt(v) <= 0xffffffffffffffffn;
const shown = (m: MirrorPresentation) => /^[a-f0-9]{64}$/i.test(m.sessionId)
  && keys.filter(k => k !== "sessionId" && k !== "ownerPid" && k !== "gamePid").every(k => u64(m[k]))
  && [m.ownerPid, m.gamePid].every(p => Number.isInteger(p) && p > 0 && p <= 0xffffffff)
  && u64(m.expiresAt) && u64(m.observedAt) && BigInt(m.expiresAt) > BigInt(m.observedAt)
  && m.protocol === 1 && m.state === "shown" && (m.actualScope === 1 || m.actualScope === 3 && m.ownedForegroundPhase === 1)
  && m.preparedCurrent === true && m.shownCurrent === true && m.leaseCurrent === true && m.cleanupComplete === false;

// These are original Steam navigation objects. This capability never grants
// native input authority and never changes OS foreground or application root.
export function steamMirrorNavigation(proof: MirrorNavigationProof, navigation: () => any,
  windows: () => unknown, current: () => boolean, now: () => number): MirrorNavigation | undefined {
  try {
    const { app, browser, overlay, menu, mirror, intent } = proof;
    if (!shown(mirror) || mirror.gameId !== browser.gameID || app !== Number(BigInt(mirror.gameId) >> 32n)) return;
    const deadline = now() + 250;
    const nav = navigation(), parent = overlay.BrowserWindow ?? overlay.m_BrowserWindow, parentDoc = parent?.document;
    const context = overlay.GetFocusNavContext?.();
    if (!nav || !parentDoc || !context || context.RootWindow !== parent || context.m_controller !== nav) return;
    const id = intent === "quick" ? "QuickAccess-NA" : "MainNavMenuContainer", side = intent === "quick" ? 2 : 1;
    const matches = Array.from(context.m_rgGamepadNavigationTrees ?? []).filter((t: any) => t?.id === id
      && t.WindowContext === context && t.Window?.closed !== true && t.Root?.Element?.isConnected === true
      && t.Root.Element.ownerDocument === t.Window.document && t.BIsEnabled?.() === true
      && t.Root.Element.getClientRects().length > 0);
    if (matches.length !== 1) return;
    const tree: any = matches[0], win = tree.Window, doc = win.document, root = tree.Root, element = root.Element;
    let attempted = false;
    const valid = () => {
      const list = windows(), info = overlay.params?.browserInfo;
      return current() && navigation() === nav && Array.isArray(list) && list.includes(overlay)
        && overlay.IsGamepadUIOverlayWindow?.() === true && String(info?.m_gameID) === browser.gameID
        && Number(info?.m_unPID) === browser.unPID && Number(info?.m_nBrowserID) === browser.nBrowserID
        && (overlay.BrowserWindow ?? overlay.m_BrowserWindow) === parent && parent.closed !== true
        && parent.document === parentDoc && overlay.MenuStore === menu && menu.GetOpenSideMenu?.() === side
        && overlay.GetFocusNavContext?.() === context && context.RootWindow === parent && context.m_controller === nav
        && context.m_rgGamepadNavigationTrees?.includes(tree)
        && context.m_rgGamepadNavigationTrees.filter((t: any) => t?.id === id && t.WindowContext === context).length === 1
        && context.BIsGamepadInputSuppressed?.() !== true
        && tree.id === id && tree.WindowContext === context && tree.Window === win && win.closed !== true && win.document === doc
        && tree.Root === root && root.Element === element && element.isConnected === true && element.ownerDocument === doc
        && tree.BIsEnabled?.() === true && element.getClientRects().length > 0 && current();
    };
    const read = (next: MirrorPresentation): MirrorNavigationState => {
      const empty = { attempted, contextExact: false, treeExact: false, nodeExact: false, confirmed: false };
      try {
        if (!shown(next) || !keys.every(key => next[key] === mirror[key]) || !valid()) return empty;
        const activeContext = nav.GetActiveContext?.(), activeTree = nav.GetActiveNavTree?.();
        let within = false;
        for (let t = activeTree, depth = 0; t && depth < 16; t = t.Parent, depth++) if (t === tree) { within = true; break; }
        const node = activeTree?.GetLastFocusedNode?.(), el = node?.Element;
        const contextExact = activeContext === context && context.BIsActive?.() === true && context.ActiveWindow === win;
        const treeExact = Boolean(activeTree && within && activeTree.Window?.document === doc);
        const nodeExact = Boolean(node && node.BHasFocus?.() === true && el?.isConnected === true
          && el.ownerDocument === doc && element.contains(el));
        if (!valid()) return empty;
        return { attempted, contextExact, treeExact, nodeExact, confirmed: contextExact && treeExact && nodeExact };
      } catch { return empty; }
    };
    return {
      read,
      activate() {
        try {
          const ready = () => valid() && Number.isFinite(now()) && now() <= deadline && current();
          if (!ready() || typeof context.OnActivate !== "function" || typeof tree.Activate !== "function"
            || (intent === "quick" && typeof menu.SetQuickAccessContentFocusRequested !== "function")) return read(mirror);
          attempted = true;
          context.OnActivate(win);
          if (!ready()) return read(mirror);
          tree.Activate(true);
          if (!ready()) return read(mirror);
          if (intent === "quick") menu.SetQuickAccessContentFocusRequested(true);
        } catch { /* A replaced tree grants no retry or foreign focus restore. */ }
        return read(mirror);
      },
    };
  } catch { return; }
}
