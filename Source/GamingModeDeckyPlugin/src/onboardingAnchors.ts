export interface AnchorRect { left: number; top: number; width: number; height: number }
/** Native offscreen BrowserViews report hidden and suspend their own animation clock. */
export function onboardingAnimationFrameHost(document: Document): Window | null {
  const native = document.defaultView;
  const shared = typeof window === "undefined" ? null : window;
  return document.visibilityState === "hidden" && shared?.document?.visibilityState === "visible" ? shared : native;
}
const browserViewOwners = new WeakMap<Document, { owner: any; document: Document }[]>();
export function invalidateOnboardingBrowserViewOwners(main: Document) { browserViewOwners.delete(main); }
function onboardingBrowserViewOwners(main: Document): any[] {
  const cached = browserViewOwners.get(main);
  if (cached) {
    const valid = cached.every(({ owner, document }) => {
      try { const view = owner.GetViewWindow(); return view && !view.closed && view.document === document; }
      catch { return false; }
    });
    if (valid) return cached.map(entry => entry.owner);
    browserViewOwners.delete(main);
  }
  const roots = new Set<any>();
  // Steam attaches React to its container; never enumerate the library DOM.
  for (const element of Array.from(main.querySelectorAll("html, body, #root, body > div")).slice(0, 24)) {
    const key = Object.keys(element).find(key => key.startsWith("__reactContainer$") || key.startsWith("__reactFiber$"));
    let fiber = key ? (element as any)[key] : null;
    if (!fiber) continue;
    for (let hops = 0; fiber.return && hops < 200; hops++) fiber = fiber.return;
    roots.add(fiber.stateNode?.current ?? fiber);
  }
  const owners: any[] = [];
  const seen = new Set<any>();
  const pending = [...roots];
  while (pending.length && seen.size < 20000) {
    const fiber = pending.pop();
    if (!fiber || seen.has(fiber)) continue;
    seen.add(fiber); pending.push(fiber.child, fiber.sibling);
    for (let hook = fiber.memoizedState, count = 0; hook && count < 100; hook = hook.next, count++) {
      const owner = hook.memoizedState;
      if (typeof owner?.GetViewWindow !== "function" || typeof owner?.GetBrowserView !== "function") continue;
      owners.push(owner);
    }
  }
  const entries = owners.flatMap(owner => {
    try { const view = owner.GetViewWindow(); return view && !view.closed && view.document ? [{ owner, document: view.document }] : []; }
    catch { return []; }
  });
  browserViewOwners.set(main, entries);
  return entries.map(entry => entry.owner);
}

export function findOnboardingBrowserViewDocument(main: Document, tabId: number): Document | null {
  for (const owner of onboardingBrowserViewOwners(main)) {
    try {
      const window = owner.GetViewWindow();
      if (window && !window.closed && window.document?.getElementById(`quickaccess_tab_${tabId}`)) return window.document;
    } catch { /* Replaced BrowserView. */ }
  }
  return null;
}

/** Steam's BrowserView SetBounds/GetBounds use the parent document's CSS coordinates. */
export function readOnboardingBrowserViewBounds(main: Document, qam: Document): AnchorRect | null {
  for (const owner of onboardingBrowserViewOwners(main)) {
    try {
      const view = owner.GetViewWindow();
      if (!view || view.closed || view.document !== qam) continue;
      const bounds = owner.GetBrowserView()?.GetBounds?.();
      if (bounds && [bounds.x, bounds.y, bounds.width, bounds.height].every(Number.isFinite)
        && bounds.width > 0 && bounds.height > 0)
        return { left: bounds.x, top: bounds.y, width: bounds.width, height: bounds.height };
    } catch { /* A replaced native view is not a usable measurement. */ }
  }
  return null;
}

/**
 * Steam positions the Quick Access BrowserView over an empty placeholder element in the
 * main document (`ViewPlaceholder`: absolute, right-aligned, fixed width, full height).
 * Its client rect is the menu band in main-document CSS pixels, so it is an exact
 * measurement even when the native GetBounds call is unavailable.
 */
export function readOnboardingQamPlaceholderBounds(main: Document): AnchorRect | null {
  const view = main.defaultView;
  const width = view?.innerWidth ?? 0;
  const height = view?.innerHeight ?? 0;
  if (!view || !main.body || !(width > 0 && height > 0)) return null;
  let best: AnchorRect | null = null;
  const queue: Element[] = [main.body];
  for (let index = 0; index < queue.length && index < 1200; index++) {
    const node = queue[index];
    for (const child of Array.from(node.children).slice(0, 32)) queue.push(child);
    // The placeholder holds a native view, never DOM children of its own.
    if (node.childElementCount !== 0) continue;
    const style = view.getComputedStyle(node);
    if (style.position !== "absolute" || style.display === "none" || style.visibility === "hidden") continue;
    const rect = node.getBoundingClientRect();
    if (!(rect.width > 200 && rect.width < width * 0.75 && rect.height > height * 0.5)) continue;
    if (rect.right < width - 2 || rect.left < 0 || rect.top < 0) continue;
    if (!best || rect.height > best.height) best = { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
  }
  return best;
}

/** Last resort when neither the native bounds nor the placeholder can be read. The QAM is a
 *  right-aligned full-height band; this deliberate over-estimate keeps text clear of the menu. */
export function onboardingFallbackQamBounds(viewport: { width: number; height: number }): AnchorRect | null {
  if (!(Number.isFinite(viewport.width) && Number.isFinite(viewport.height)) || viewport.width <= 480 || viewport.height <= 0) return null;
  const width = Math.min(viewport.width - 320, Math.max(360, Math.round(viewport.width * 0.45)));
  if (!(width > 0) || width >= viewport.width) return null;
  return { left: viewport.width - width, top: 0, width, height: viewport.height };
}

export function convertOnboardingAnchor(anchor: AnchorRect, bounds: AnchorRect,
  viewport: { width: number; height: number }): AnchorRect | null {
  if (!(viewport.width > 0 && viewport.height > 0 && bounds.width > 0 && bounds.height > 0)) return null;
  const x = bounds.width / viewport.width, y = bounds.height / viewport.height;
  return { left: bounds.left + anchor.left * x, top: bounds.top + anchor.top * y,
    width: anchor.width * x, height: anchor.height * y };
}

/** Unframed text is centered within the usable space left of the whole QAM. */
export function positionOnboardingShowcase(anchor: AnchorRect | null, qam: AnchorRect | null,
  viewport: { width: number; height: number }, size: { width: number; height: number }, footerInset = 96) {
  if (!qam || !anchor || ![qam.left, anchor.left, anchor.top, anchor.width, anchor.height,
    viewport.width, viewport.height, size.width, size.height].every(Number.isFinite)
    || qam.left >= viewport.width || anchor.left >= viewport.width) return null;
  const gap = 24, margin = 16;
  const width = Math.min(size.width, qam.left - gap - margin);
  const maxHeight = viewport.height - footerInset - margin * 2;
  if (width < 240 || maxHeight < 120) return null;
  const height = Math.min(size.height, maxHeight);
  if (anchor.top < 0 || anchor.top + anchor.height > viewport.height - footerInset) return null;
  const top = margin + (maxHeight - height) / 2;
  const left = margin + (qam.left - gap - margin - width) / 2;
  return { left, top, width, maxHeight };
}

export function createOnboardingBoundsStability(requiredFrames = 4, tolerance = 0.5) {
  let previous: number[] | null = null;
  let frames = 0;
  const coordinates = (bounds: AnchorRect | null, viewport: { width: number; height: number }) => bounds
    ? [bounds.left, bounds.top, bounds.width, bounds.height, viewport.width, viewport.height] : null;
  const matches = (bounds: AnchorRect | null, viewport: { width: number; height: number }) => {
    const next = coordinates(bounds, viewport);
    return !!previous && !!next && next.every((value, index) => Number.isFinite(value) && Math.abs(value - previous![index]) <= tolerance);
  };
  return { matches, ready: () => frames >= requiredFrames,
    reset() { previous = null; frames = 0; },
    sample(bounds: AnchorRect | null, visible: boolean, viewport: { width: number; height: number }) {
      if (!visible || !bounds || bounds.width <= 0 || bounds.height <= 0 || bounds.left < 0 || bounds.left >= viewport.width
        || !coordinates(bounds, viewport)?.every(Number.isFinite)) { previous = null; frames = 0; return false; }
      if (matches(bounds, viewport)) frames++;
      else { frames = 1; previous = coordinates(bounds, viewport); }
      return frames >= requiredFrames;
    }
  };
}
export function onboardingPortalGeometry(document: Document, portal: HTMLElement, anchor: AnchorRect | null, qam = false) {
  const rect = portal.getBoundingClientRect();
  const view = document.defaultView;
  const visual = view?.visualViewport;
  const scaleX = rect.width / portal.clientWidth;
  const scaleY = rect.height / portal.clientHeight;
  if (!(scaleX > 0 && scaleY > 0)) return null;
  let left = Math.max(rect.left, visual?.offsetLeft ?? 0);
  let top = Math.max(rect.top, visual?.offsetTop ?? 0);
  let right = Math.min(rect.left + rect.width, (visual?.offsetLeft ?? 0) + (visual?.width ?? view?.innerWidth ?? 0));
  let bottom = Math.min(rect.top + rect.height, (visual?.offsetTop ?? 0) + (visual?.height ?? view?.innerHeight ?? 0));
  if (qam) {
    const controls = document.querySelector('[data-playhub-onboarding-controls="true"].ph-controls, [data-playhub-onboarding-controls="true"] .ph-controls') as HTMLElement | null;
    if (!controls) return null;
    const bounds = controls.getBoundingClientRect();
    if (bounds.width <= 0 || bounds.height <= 0) return null;
    left = Math.max(left, bounds.left); right = Math.min(right, bounds.right);
    // Steam's transparent BrowserView is wider than its clipped native content pane.
    for (let node = controls.parentElement; node && node !== document.body && node !== document.documentElement; node = node.parentElement) {
      const style = view?.getComputedStyle(node);
      if (style?.display === "none" || style?.visibility === "hidden") return null;
      const bounds = node.getBoundingClientRect();
      // Fixed Steam panes can have zero-area layout ancestors; these are not the pane's clip bounds.
      if (bounds.right <= bounds.left || bounds.bottom <= bounds.top) continue;
      if (/hidden|clip|auto|scroll/.test(style?.overflowX ?? "")) {
        left = Math.max(left, bounds.left); right = Math.min(right, bounds.right);
      }
      if (/hidden|clip|auto|scroll/.test(style?.overflowY ?? "")) {
        top = Math.max(top, bounds.top); bottom = Math.min(bottom, bounds.bottom);
      }
    }
  }
  if (right <= left || bottom <= top) return null;
  return { left: (left - rect.left) / scaleX, top: (top - rect.top) / scaleY,
    width: (right - left) / scaleX, height: (bottom - top) / scaleY,
    anchor: anchor ? { left: (anchor.left - left) / scaleX, top: (anchor.top - top) / scaleY,
      width: anchor.width / scaleX, height: anchor.height / scaleY } : null };
}
export function visibleOnboardingAnchor(document: Document, selector: string): AnchorRect | null {
  const element = document.querySelector(selector) as HTMLElement | null;
  if (!element?.isConnected || element.closest('[hidden], [aria-hidden="true"]')) return null;
  const style = document.defaultView?.getComputedStyle(element);
  if (style?.display === "none" || style?.visibility === "hidden") return null;
  const rect = element.getBoundingClientRect();
  if (rect.width <= 0 || rect.height <= 0 || rect.left < 0 || rect.top < 0
    || rect.bottom > (document.defaultView?.innerHeight ?? 0) || rect.right > (document.defaultView?.innerWidth ?? 0)) return null;
  return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
}
export function visibleOnboardingContent(document: Document, selector: string, fullyOpaque = false): boolean {
  const element = document.querySelector(selector) as HTMLElement | null;
  const view = document.defaultView;
  if (!view || !element?.isConnected || !element.childElementCount || element.closest('[hidden], [aria-hidden="true"]')) return false;
  const rect = element.getBoundingClientRect();
  let left = Math.max(0, rect.left), top = Math.max(0, rect.top);
  let right = Math.min(view.innerWidth, rect.right), bottom = Math.min(view.innerHeight, rect.bottom);
  for (let node: HTMLElement | null = element; node; node = node.parentElement) {
    const style = view.getComputedStyle(node);
    if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity) === 0) return false;
    if (fullyOpaque && Number(style.opacity) < 0.999) return false;
    const bounds = node.getBoundingClientRect();
    if (bounds.width <= 0 || bounds.height <= 0) continue;
    if (/hidden|clip|auto|scroll/.test(style.overflowX)) {
      left = Math.max(left, bounds.left); right = Math.min(right, bounds.right);
    }
    if (/hidden|clip|auto|scroll/.test(style.overflowY)) {
      top = Math.max(top, bounds.top); bottom = Math.min(bottom, bounds.bottom);
    }
  }
  return right > left && bottom > top;
}
const onboardingSignals = '[id^="quickaccess_"], [data-playhub-onboarding-controls], [data-playhub-onboarding-content], [data-playhub-onboarding], [role="dialog"], [aria-modal="true"], video, [class*="Startup"], [class*="GuidedTour"]';
export function isOnboardingMutationRelevant(record: MutationRecord): boolean {
  const target = record.target as Element;
  if (target.closest?.('[data-playhub-onboarding-overlay]')) return false;
  if (record.type === "attributes") {
    return !!target.matches?.(onboardingSignals) || !!target.closest?.('[id^="quickaccess_"], [data-playhub-onboarding-controls]')
      || ((record.attributeName === "style" || record.attributeName === "class") && !!target.querySelector?.(onboardingSignals));
  }
  return [...Array.from(record.addedNodes), ...Array.from(record.removedNodes)].some(node => {
    const element = node as Element;
    if (element.matches?.('[data-playhub-onboarding-overlay]') || element.closest?.('[data-playhub-onboarding-overlay]')) return false;
    return !!element.matches?.(onboardingSignals) || !!element.querySelector?.(onboardingSignals)
      || !!target.closest?.('[id^="quickaccess_"], [data-playhub-onboarding-controls]')
      || element.id === "root";
  });
}
/** Rebound by the runtime when Steam replaces the QAM document. No interval polling. */
export function observeOnboardingAnchors(document: Document, changed: () => void): () => void {
  const view = document.defaultView;
  if (!view) return () => {};
  let frame: number | undefined;
  let frameHost: Window | null = null;
  let stopped = false;
  const schedule = () => {
    if (stopped) return;
    const nextHost = onboardingAnimationFrameHost(document);
    if (frame !== undefined && frameHost !== nextHost) {
      frameHost?.cancelAnimationFrame(frame); frame = undefined;
    }
    if (frame !== undefined) return;
    frameHost = nextHost;
    frame = frameHost?.requestAnimationFrame(() => { frame = undefined; if (!stopped) changed(); });
  };
  const observer = new (view as any).MutationObserver((records: MutationRecord[]) => {
    const relevant = records.filter(isOnboardingMutationRelevant);
    if (relevant.some(record => record.type === "childList")) invalidateOnboardingBrowserViewOwners(document);
    if (relevant.length) schedule();
  });
  observer.observe(document.documentElement, { subtree: true, childList: true, attributes: true,
    attributeFilter: ["id", "class", "style", "hidden", "aria-hidden", "aria-selected", "data-playhub-onboarding",
      "data-onboarding-ready", "data-onboarding-decky-enabled", "data-onboarding-decky-visible", "data-onboarding-customizing",
      "data-onboarding-active-tab", "data-onboarding-content-ready", "data-onboarding-store-visible", "data-onboarding-showcasing", "data-onboarding-visible-tabs"] });
  view.addEventListener("resize", schedule);
  const layoutEvent = (event: Event) => {
    const target = event.target as Element;
    if (target.closest?.('[data-playhub-onboarding-overlay]')) return;
    if (target === document as unknown || target.matches?.(onboardingSignals)
      || target.closest?.('[id^="quickaccess_"], [data-playhub-onboarding-controls]')
      || target.querySelector?.(onboardingSignals)) schedule();
  };
  document.addEventListener("scroll", layoutEvent, true);
  document.addEventListener("transitionend", layoutEvent, true);
  document.addEventListener("animationend", layoutEvent, true);
  document.addEventListener("visibilitychange", schedule);
  view.visualViewport?.addEventListener("resize", schedule);
  return () => {
    if (stopped) return;
    stopped = true; observer.disconnect();
    if (frame !== undefined) frameHost?.cancelAnimationFrame(frame);
    view.removeEventListener("resize", schedule);
    document.removeEventListener("scroll", layoutEvent, true);
    document.removeEventListener("transitionend", layoutEvent, true);
    document.removeEventListener("animationend", layoutEvent, true);
    document.removeEventListener("visibilitychange", schedule);
    view.visualViewport?.removeEventListener("resize", schedule);
  };
}
export function positionOnboardingCoach(anchor: AnchorRect | null, viewport: { width: number; height: number },
  size: { width: number; height: number }, footerInset = 96) {
  const gap = 12;
  const width = Math.min(size.width, Math.max(0, viewport.width - gap * 2));
  const bottom = Math.max(gap, viewport.height - footerInset - gap);
  const height = Math.min(size.height, Math.max(0, bottom - gap));
  const left = Math.max(gap, Math.min(anchor ? anchor.left + anchor.width / 2 - width / 2 : (viewport.width - width) / 2,
    viewport.width - width - gap));
  let top = anchor ? anchor.top + anchor.height + gap : Math.max(gap, (bottom - height) / 2);
  if (top + height > bottom) top = anchor ? anchor.top - height - gap : bottom - height;
  return { left, top: Math.max(gap, Math.min(top, bottom - height)), width, maxHeight: bottom - gap };
}
