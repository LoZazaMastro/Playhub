import { DFL, SP_REACT as React } from "./decky";
import { canShareDeckyTabProjection, getDeckyTabProjection } from "./deckyHostStandalone";
import { filterNativeDeckyTabs, isNativeDeckyHidden, subscribeNativeDeckyHidden } from "./deckyNativeTabPreference";
const PROJECTION_COMPONENT = Symbol.for("playhub.decky-tab-projection-component.v1");
const LEGACY_PROJECTION = `function DeckyTabProjection({ element }) {
    const store = _global_SP_REACT.useMemo(() => getDeckyTabProjection(element.props.tabs), [element.props.tabs]);
    const tabs = _global_SP_REACT.useSyncExternalStore(store.subscribe, store.getSnapshot, store.getSnapshot);
    return _global_SP_REACT.cloneElement(element, { tabs });
}`;

// A render fault inside the projection must degrade the tab list, never Steam's QAM.
// Big Picture has no error boundary above this subtree: a throw here takes the overlay
// (and with it Steam) down, which is exactly the crash reported when opening the QAM.
const RENDER_BURST_WINDOW_MS = 1000;
const RENDER_BURST_LIMIT = 60;
const EMPTY_TABS: any[] = [];
const NO_SUBSCRIBE = () => () => {};
const NO_SNAPSHOT = () => EMPTY_TABS;
let burstStarted = 0;
let burstCount = 0;
let projectionDisabled = false;
/** True once the guard has given up: the projection passes tabs through untouched. */
export const isDeckyTabProjectionDisabled = () => projectionDisabled;
export function resetDeckyTabProjectionGuard() { projectionDisabled = false; burstCount = 0; burstStarted = 0; faultReported = false; }
function noteProjectionRender(): boolean {
  if (projectionDisabled) return false;
  const now = Date.now();
  if (now - burstStarted > RENDER_BURST_WINDOW_MS) { burstStarted = now; burstCount = 0; }
  if (++burstCount > RENDER_BURST_LIMIT) { projectionDisabled = true; return false; }
  return true;
}

export function DeckyTabProjection({ element }: { element: React.ReactElement<{ tabs: any[] }> }) {
  const live = noteProjectionRender();
  const store = React.useMemo(() => {
    try { return live ? getDeckyTabProjection(element.props.tabs) : null; } catch { return null; }
  }, [element.props.tabs, live]);
  const published = React.useSyncExternalStore(store ? store.subscribe : NO_SUBSCRIBE,
    store ? store.getSnapshot : NO_SNAPSHOT, store ? store.getSnapshot : NO_SNAPSHOT);
  // The preference alone decides visibility here; hosting Playhub's tab is not a precondition.
  const hidden = React.useSyncExternalStore(subscribeNativeDeckyHidden, isNativeDeckyHidden, isNativeDeckyHidden);
  const source = store ? published : element.props.tabs;
  const tabs = React.useMemo(() => {
    try { return live ? filterNativeDeckyTabs(source) : source; } catch { return source; }
  }, [source, hidden, live]);
  if (tabs === element.props.tabs) return element;
  return React.cloneElement(element, { tabs });
}
Object.defineProperty(DeckyTabProjection, PROJECTION_COMPONENT, { value: true });

export function projectDeckyTabViews(tree: any, component = DeckyTabProjection, onConflict?: () => void): any {
  if (Array.isArray(tree)) return tree.map(node => projectDeckyTabViews(node, component, onConflict));
  if (!React.isValidElement<any>(tree)) return tree;
  const type = tree.type as any;
  if (type?.[PROJECTION_COMPONENT] === true) return tree;
  const legacy = typeof type === "function" && Function.prototype.toString.call(type) === LEGACY_PROJECTION
    && React.isValidElement(tree.props.element) && Array.isArray(tree.props.element.props.tabs);
  if (!legacy && type?.name === "DeckyTabProjection" && Array.isArray(tree.props.element?.props?.tabs)) {
    onConflict?.();
    return tree;
  }
  if (legacy || Array.isArray(tree.props.tabs)) {
    const Component = component;
    return <Component key={`playhub-decky-projection-v1:${tree.key ?? ""}`} element={legacy ? tree.props.element : tree} />;
  }
  if (tree.props.children == null) return tree;
  return React.cloneElement(tree, {}, projectDeckyTabViews(tree.props.children, component, onConflict));
}

let faultReported = false;
/** Never publish host state from inside Steam's render pass. */
function reportProjectionFault(onConflict?: () => void) {
  projectionDisabled = true;
  if (faultReported || !onConflict) return;
  faultReported = true;
  void Promise.resolve().then(() => { try { onConflict(); } catch { /* Host already torn down. */ } });
}

function sharesDeckyAncestor(type: any, known: Set<any> | undefined): boolean {
  const seen = new Set<any>();
  for (let current = type; typeof current === "function" && !seen.has(current) && seen.size < 64; current = current.__deckyOrig) {
    if (known?.has(current)) return true;
    seen.add(current);
  }
  return false;
}

/** Same QAM render boundary as Decky's TabsHook, but only tab consumers subscribe. */
export function installDeckyTabProjection(onConflict?: () => void) {
  resetDeckyTabProjectionGuard();
  if (!canShareDeckyTabProjection()) {
    onConflict?.();
    return { reconcile() {}, stop() {} };
  }
  const patches = new Map<any, { unpatch(): void }>();
  const previousTypes = new Map<any, Set<any>>();
  const pendingAttachments = new Set<any>();
  const handlers = new Map<any, any>();
  const attached = new Map<any, { previous: any; installed: any }>();
  let stopped = false;
  let component = DeckyTabProjection;
  const reconcile = () => {
    if (stopped || (patches.size > 0 && pendingAttachments.size === 0)) return;
    try {
      const module = DFL.findModuleByExport((value: any) => value?.type?.toString?.()?.includes("QuickAccessMenuBrowserView"));
      if (!module) return;
      const rootElement = document.getElementById("root");
      const root = rootElement && DFL.getReactRoot(rootElement);
      const retained = root && DFL.findInReactTree(root, (node: any) => node?.type?.[PROJECTION_COMPONENT] === true);
      if (retained) component = retained.type;
      for (const renderer of Object.values(module) as any[]) {
        if (!/QuickAccessMenuBrowserView|QuickAccessMenuEmbedded/.test(renderer?.type?.toString?.() ?? "")) continue;
        if (patches.has(renderer)) continue;
        const handler = DFL.createReactTreePatcher([
          tree => DFL.findInReactTree(tree, (node: any) => node?.props?.onFocusNavDeactivated),
        ], (_args, result) => {
          if (stopped || projectionDisabled) return result;
          try { return projectDeckyTabViews(result, component, () => reportProjectionFault(onConflict)); }
          catch { reportProjectionFault(onConflict); return result; }
        }, "PlayhubDeckyTabProjection");
        const known = new Set<any>();
        for (let type = renderer.type; typeof type === "function" && !known.has(type); type = type.__deckyOrig) known.add(type);
        previousTypes.set(renderer, known);
        handlers.set(renderer, handler);
        patches.set(renderer, DFL.afterPatch(renderer, "type", handler));
        pendingAttachments.add(renderer);
      }
      for (const renderer of pendingAttachments) {
        // React memo fibers retain their resolved type across QAM close/reopen.
        // Match TabsHook's existing-QAM attachment, including the alternate.
        const mounted = root && DFL.findInReactTree(root, (node: any) => node?.elementType === renderer);
        if (!mounted) continue;
        if ([mounted, mounted.alternate].some(fiber => fiber && fiber.type !== renderer.type
          && !previousTypes.get(renderer)?.has(fiber.type)
          && !sharesDeckyAncestor(fiber.type, previousTypes.get(renderer)))) {
          pendingAttachments.delete(renderer);
          onConflict?.();
          continue;
        }
        for (const fiber of [mounted, mounted?.alternate]) {
          if (!fiber || fiber.type === renderer.type) continue;
          const previous = fiber.type;
          if (!previousTypes.get(renderer)?.has(previous)) {
            // Decky can retain a sibling wrapper over the same original after reload.
            // Wrap that live function in place: never discard another owner's wrapper.
            DFL.afterPatch(fiber, "type", handlers.get(renderer));
          } else {
            fiber.type = renderer.type;
          }
          attached.set(fiber, { previous, installed: fiber.type });
        }
        pendingAttachments.delete(renderer);
      }
    } catch { /* Retry when Steam's QAM module is available. */ }
  };
  reconcile();
  return { reconcile, stop() {
    stopped = true;
    patches.forEach(patch => { try { patch.unpatch(); } catch { /* A later owner may have replaced the export. */ } });
    attached.forEach(({ previous, installed }, fiber) => {
      if (fiber.type === installed) fiber.type = previous;
    });
    attached.clear();
    pendingAttachments.clear();
    previousTypes.clear();
    handlers.clear();
    patches.clear();
  } };
}
