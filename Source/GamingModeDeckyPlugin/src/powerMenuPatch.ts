import { DFL, SP_REACT as React } from "./decky";

type Labels = { gaming: string; desktop: string };
type RestartMode = "gaming" | "desktop";

const PATCH_MARKER = "__playhubPowerMenuPatch";
const RESTART_TOKENS = new Set(["#Quit_Restart", "#RestartDevice", "#Restart"]);

function steamWindows(): Window[] {
  const roots: any[] = [window];
  try {
    const steam = (DFL as any)?.findSP?.();
    if (steam && !roots.includes(steam)) roots.push(steam);
    const trees = (DFL as any)?.getGamepadNavigationTrees?.() ?? [];
    for (const tree of trees) {
      const treeWindow = tree?.Root?.Element?.ownerDocument?.defaultView;
      if (treeWindow && !roots.includes(treeWindow)) roots.push(treeWindow);
    }
  } catch {}
  return roots.filter((root) => root?.document);
}

function fiberOf(node: HTMLElement): any {
  const key = Object.keys(node).find((name) => name.startsWith("__reactFiber$"));
  return key ? (node as any)[key] : null;
}

function powerMenuInstance(row: HTMLElement): any {
  let fiber = fiberOf(row);
  for (let step = 0; fiber && step < 24; step += 1, fiber = fiber.return) {
    const instance = fiber.stateNode;
    if (
      typeof instance?.forceUpdate === "function"
      && typeof instance.render === "function"
      && containsRestart(instance.props?.children)
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
  const observers = new Map<Document, MutationObserver>();
  const patches = new Set<any>();
  const refreshedInstances = new WeakSet<object>();
  const pendingRefreshes = new Set<() => void>();
  let disposed = false;

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
      if (!Object.prototype.hasOwnProperty.call(target, PATCH_MARKER)) {
        const patch = (DFL as any).afterPatch(
          target,
          "render",
          function (this: any, _args: any[], result: any) {
            return !disposed && containsRestart(this?.props?.children)
              ? addRestartActions(result, labels(), restart)
              : result;
          },
        );
        Object.defineProperty(target, PATCH_MARKER, { value: patch, configurable: true });
        patches.add(patch);
      }
      // A new instance can mount after prototype discovery but before the first
      // refresh. It still needs its own refresh even when the prototype is patched.
      refreshInstance(instance, row);
    }
  };

  const bind = () => {
    for (const win of steamWindows()) {
      const doc = win.document;
      if (!doc.documentElement) continue;
      patchDocument(doc);
      if (observers.has(doc)) continue;
      const Observer = (win as any).MutationObserver as typeof MutationObserver;
      if (!Observer) continue;
      const observer = new Observer(() => patchDocument(doc));
      observer.observe(doc.documentElement, { childList: true, subtree: true });
      observers.set(doc, observer);
    }
  };

  bind();
  const timer = window.setInterval(bind, 500);
  return () => {
    disposed = true;
    window.clearInterval(timer);
    pendingRefreshes.forEach((cancel) => cancel());
    pendingRefreshes.clear();
    observers.forEach((observer) => observer.disconnect());
    observers.clear();
    patches.forEach((patch) => {
      try {
        delete patch.object?.[PATCH_MARKER];
        patch.unpatch?.();
      } catch {}
    });
    patches.clear();
  };
}
