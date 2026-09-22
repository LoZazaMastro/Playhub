import { DFL, SP_REACT as React } from "./decky";

type Labels = { gaming: string; desktop: string };
type RestartMode = "gaming" | "desktop";

const PATCH_MARKER = Symbol.for("playhub.power-menu-patch.v2");
const RESTART_TOKENS = new Set(["#Quit_Restart", "#RestartDevice", "#Restart"]);

function collectionValues(value: any): any[] {
  if (!value) return [];
  try {
    if (typeof value.values === "function") return Array.from(value.values());
    if (typeof value[Symbol.iterator] === "function") return Array.from(value);
  } catch {}
  try { return Object.values(value); } catch { return []; }
}
function popupWindows(host: any): any[] {
  const manager = host?.g_PopupManager;
  const popups = collectionValues(manager?.m_mapPopups);
  const output: any[] = [];
  for (const entry of popups) {
    const popup = entry?.m_popup ?? entry?.popup ?? entry?.window;
    if (popup) output.push(popup);
  }
  return output;
}

function navigationWindows(host: any): any[] {
  const output: any[] = [];
  try {
    const controller = host?.GamepadNavTree?.m_context?.m_controller
      ?? host?.FocusNavController;
    const contexts = [
      controller?.m_ActiveContext,
      controller?.m_LastActiveContext,
      controller?.m_DefaultContext,
      ...collectionValues(controller?.m_rgAllContexts),
    ];
    for (const context of contexts) {
      for (const tree of collectionValues(context?.m_rgGamepadNavigationTrees)) {
        const treeWindow = tree?.Root?.Element?.ownerDocument?.defaultView;
        if (treeWindow) output.push(treeWindow);
      }
    }
  } catch {}
  return output;
}

function steamWindows(): Window[] {
  const roots = new Set<Window>();
  const pending: any[] = [];
  const add = (candidate: any) => {
    try {
      if (!candidate?.document || candidate.closed || roots.has(candidate)) return;
      roots.add(candidate);
      pending.push(candidate);
    } catch {}
  };

  add(window);
  try {
    const sp = (DFL as any)?.findSP?.();
    add(sp?.window ?? sp);
    for (const tree of collectionValues((DFL as any)?.getGamepadNavigationTrees?.())) {
      add(tree?.Root?.Element?.ownerDocument?.defaultView);
    }
  } catch {}

  // Steam renders Quick Access, the main UI and its modal menus in sibling
  // popups owned by SharedJSContext. Navigation trees can temporarily be empty,
  // so walk the popup registry as well and repeat this discovery throughout the
  // plugin lifetime.
  for (let index = 0; index < pending.length && index < 64; index += 1) {
    const host: any = pending[index];
    try { add(host.opener); } catch {}
    try { if (host.parent !== host) add(host.parent); } catch {}
    try { if (host.top !== host) add(host.top); } catch {}
    for (const popup of popupWindows(host)) add(popup);
    for (const navigationWindow of navigationWindows(host)) add(navigationWindow);
  }

  return Array.from(roots);
}

function fiberOf(node: HTMLElement): any {
  const key = Object.keys(node).find((name) =>
    name.startsWith("__reactFiber$") || name.startsWith("__reactInternalInstance$"));
  return key ? (node as any)[key] : null;
}

function powerMenuInstance(row: HTMLElement): any {
  let fiber = fiberOf(row);
  for (let step = 0; fiber && step < 48; step += 1, fiber = fiber.return) {
    const instance = fiber.stateNode;
    if (
      typeof instance?.forceUpdate === "function"
      && typeof instance.render === "function"
      && (containsRestart(instance.props?.children) || containsRestart(fiber.memoizedProps?.children))
    ) return instance;
  }
  return null;
}

function containsRestart(node: any): boolean {
  if (Array.isArray(node)) return node.some(containsRestart);
  if (!node || typeof node !== "object") return false;
  return RESTART_TOKENS.has(node.props?.strDisplayNameLocToken)
    || containsRestart(node.props?.children);
}

function addRestartActions(node: any, labels: Labels, restart: (mode: RestartMode) => void): any {
  if (Array.isArray(node)) {
    if (node.some((child) => child?.key === "playhub-restart-gaming")) return node;
    const output: any[] = [];
    for (const child of node) {
      output.push(addRestartActions(child, labels, restart));
      if (!RESTART_TOKENS.has(child?.props?.strDisplayNameLocToken)) continue;
      const MenuItem = (DFL as any).MenuItem;
      const MenuSeparator = (DFL as any).MenuSeparator;
      if (MenuSeparator) output.push(React.createElement(MenuSeparator, { key: "playhub-restart-separator" }));
      output.push(
        React.createElement(MenuItem, {
          key: "playhub-restart-gaming",
          tone: "destructive",
          onSelected: () => restart("gaming"),
          children: labels.gaming,
        }),
        React.createElement(MenuItem, {
          key: "playhub-restart-desktop",
          tone: "destructive",
          onSelected: () => restart("desktop"),
          children: labels.desktop,
        }),
      );
    }
    return output;
  }
  if (!React.isValidElement(node)) return node;
  const element = node as React.ReactElement<any>;
  if (!element.props?.children) return node;
  const children = element.props.children;
  return React.cloneElement(element, undefined, addRestartActions(
    RESTART_TOKENS.has(children?.props?.strDisplayNameLocToken) ? [children] : children,
    labels,
    restart,
  ));
}

export function installPowerMenuPatch(
  labels: () => Labels,
  restart: (mode: RestartMode) => void,
): () => void {
  type Subscriber = { labels: () => Labels; restart: (mode: RestartMode) => void };
  type SharedPatch = { patch: any; subscribers: Map<symbol, Subscriber> };

  const owner = Symbol("playhub-power-menu-owner");
  const observers = new Map<Document, MutationObserver>();
  const sharedPatches = new Set<SharedPatch>();
  const refreshedInstances = new WeakSet<object>();
  const pendingRefreshes = new Set<() => void>();
  let disposed = false;
  const managerPatches = new Map<any, any>();
  let menuRegistry: any;

  const bindNativeMenuManager = (win: Window) => {
    try {
      menuRegistry ??= (DFL as any).findModuleExport?.((value: any) =>
        typeof value?.GetContextMenuManager === "function"
        && typeof value?.GetContextMenuManagerFromWindow === "function");
      const manager = menuRegistry?.GetContextMenuManager(win);
      if (!manager || managerPatches.has(manager)) return;
      // Current Steam creates the power menu from a MobX functional component.
      // Intercept its root before mounting, while the localized item tokens are
      // still present in the returned React tree, not in the rendered DOM.
      const patch = (DFL as any).beforePatch(manager, "CreateContextMenuInstance", (args: any[]) => {
        const element = args[0];
        if (!React.isValidElement(element)) return;
        const type: any = element.type;
        const component = typeof type === "function" ? type : type?.type;
        if (typeof component !== "function" || component.prototype?.isReactComponent) return;
        const PowerMenuRoot = (props: any) => {
          const result = component(props);
          return !disposed && containsRestart(result)
            ? addRestartActions(result, labels(), restart) : result;
        };
        args[0] = React.createElement(PowerMenuRoot, { ...(element.props as any), key: element.key });
      });
      managerPatches.set(manager, patch);
    } catch {}
  };

  const attachPatch = (target: any): SharedPatch | null => {
    let shared = target?.[PATCH_MARKER] as SharedPatch | undefined;
    if (shared?.patch?.hasUnpatched || !(shared?.subscribers instanceof Map)) {
      try { delete target[PATCH_MARKER]; } catch {}
      shared = undefined;
    }
    if (!shared) {
      const created: SharedPatch = { patch: null, subscribers: new Map() };
      try {
        created.patch = (DFL as any).afterPatch(
          target,
          "render",
          function (this: any, _args: any[], result: any) {
            const subscribers = Array.from(created.subscribers.values());
            const subscriber = subscribers[subscribers.length - 1];
            if (!subscriber || !containsRestart(this?.props?.children)) return result;
            try {
              return addRestartActions(result, subscriber.labels(), subscriber.restart);
            } catch {
              return result;
            }
          },
        );
        Object.defineProperty(target, PATCH_MARKER, { value: created, configurable: true });
        shared = created;
      } catch {
        try { created.patch?.unpatch?.(); } catch {}
        return null;
      }
    }
    shared.subscribers.delete(owner);
    shared.subscribers.set(owner, { labels, restart });
    sharedPatches.add(shared);
    return shared;
  };

  const refreshInstance = (instance: any, row: HTMLElement) => {
    if (refreshedInstances.has(instance)) return;
    refreshedInstances.add(instance);
    const view = row.ownerDocument.defaultView ?? window;
    let frame: number | undefined;
    const refresh = () => {
      if (disposed) return;
      const mounted = row.isConnected || Array.from(
        row.ownerDocument.querySelectorAll<HTMLElement>("[role='menuitem']"),
      ).some((candidate) => powerMenuInstance(candidate) === instance);
      if (mounted) {
        try { instance.forceUpdate(); } catch {}
      }
    };
    const cancel = () => {
      view.clearTimeout(timeout);
      if (frame !== undefined) view.cancelAnimationFrame(frame);
    };
    const timeout = view.setTimeout(() => {
      refresh();
      frame = view.requestAnimationFrame(() => {
        refresh();
        pendingRefreshes.delete(cancel);
      });
    }, 0);
    pendingRefreshes.add(cancel);
  };

  const patchDocument = (doc: Document) => {
    for (const row of Array.from(doc.querySelectorAll<HTMLElement>("[role='menuitem']"))) {
      const instance = powerMenuInstance(row);
      if (!instance) continue;
      // Some Steam components bind render on the instance. Patching only their
      // prototype cannot update the menu that is already mounted.
      const target = Object.prototype.hasOwnProperty.call(instance, "render")
        ? instance : Object.getPrototypeOf(instance);
      if (!target) continue;
      if (!attachPatch(target)) continue;
      // A new instance can mount after prototype discovery but before the first
      // refresh. It still needs its own refresh even when the prototype is patched.
      refreshInstance(instance, row);
    }
  };

  const bind = () => {
    const activeDocuments = new Set<Document>();
    for (const win of steamWindows()) {
      bindNativeMenuManager(win);
      let doc: Document;
      try { doc = win.document; } catch { continue; }
      if (!doc.documentElement) continue;
      activeDocuments.add(doc);
      patchDocument(doc);
      if (observers.has(doc)) continue;
      const Observer = (win as any).MutationObserver as typeof MutationObserver;
      if (!Observer) continue;
      const observer = new Observer(() => patchDocument(doc));
      observer.observe(doc.documentElement, { childList: true, subtree: true });
      observers.set(doc, observer);
    }
    for (const [doc, observer] of observers) {
      if (activeDocuments.has(doc)) continue;
      observer.disconnect();
      observers.delete(doc);
    }
  };

  bind();
  const timer = window.setInterval(bind, 500);
  return () => {
    disposed = true;
    window.clearInterval(timer);
    managerPatches.forEach((patch) => { try { patch.unpatch(); } catch {} });
    managerPatches.clear();
    pendingRefreshes.forEach((cancel) => cancel());
    pendingRefreshes.clear();
    observers.forEach((observer) => observer.disconnect());
    observers.clear();
    sharedPatches.forEach((shared) => {
      shared.subscribers.delete(owner);
      if (shared.subscribers.size > 0) return;
      const target = shared.patch?.object;
      try { if (target?.[PATCH_MARKER] === shared) delete target[PATCH_MARKER]; } catch {}
      try { if (!shared.patch?.hasUnpatched) shared.patch?.unpatch?.(); } catch {}
    });
    sharedPatches.clear();
  };
}
