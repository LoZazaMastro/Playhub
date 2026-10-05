// Pure helpers for the standalone host: route injection, route patches and the
// Quick Access Menu tab. No Steam, React or DOM access, so they run under
// `node --test` exactly as they run in the renderer.
//
// The technique (wrap the router's output, append a cached keyed route array,
// insert one stable tab object into the QAM tab list) follows ShelvesHub
// d237922 (Jonathan Santos, MIT — vendor/ShelvesHub-LICENSE). The code here is
// Playhub's own and carries no ShelvesHub identity, globals or behaviour.

export const OUR_ROUTES = Symbol.for('playhub.standalone.routes.v1');
export const PATCHED = Symbol.for('playhub.standalone.route-patched.v1');

/** Build our route elements once per (route set, Route type) and keep identities stable. */
export function createRouteBuilder(React) {
  let built = null;
  let signature = '';
  let builtType = null;
  return function buildRoutes(Route, routes, Boundary) {
    const next = [...routes.keys()].sort().join('|');
    if (built && signature === next && builtType === Route) return built;
    const list = [];
    list[OUR_ROUTES] = true;
    for (const [path, entry] of routes) {
      const page = React.createElement(entry.component);
      const body = Boundary ? React.createElement(Boundary, null, page) : page;
      list.push(React.createElement(Route, { key: 'playhub-route:' + path, path, ...(entry.props || {}) }, body));
    }
    built = list;
    signature = next;
    builtType = Route;
    return list;
  };
}

/** The route element type Steam itself uses, taken from the list we are about to extend. */
export function routeTypeFromList(routeList) {
  for (const entry of routeList || []) {
    const candidates = Array.isArray(entry) ? entry : [entry];
    for (const element of candidates) {
      if (element && element.props && element.props.path !== undefined && element.type) return element.type;
    }
  }
  return undefined;
}

/**
 * Append our routes at the END of Steam's main route list, reusing our own slot
 * so repeated renders never duplicate them. Steam's routes keep their position,
 * so React reconciles them in place instead of remounting.
 */
export function injectRoutes(routeList, ourRoutes) {
  if (!Array.isArray(routeList) || !ourRoutes || !ourRoutes.length) return false;
  const last = routeList.length ? routeList[routeList.length - 1] : null;
  if (last && last[OUR_ROUTES]) routeList[routeList.length - 1] = ourRoutes;
  else routeList.push(ourRoutes);
  return true;
}

/** Apply registered route patches once per rendered route. */
export function applyRoutePatches(routeList, patches, onError) {
  if (!Array.isArray(routeList) || !patches || !patches.size) return;
  for (const entry of routeList) {
    const elements = Array.isArray(entry) ? entry : [entry];
    for (const route of elements) {
      const path = route && route.props && route.props.path;
      const set = path === undefined ? undefined : patches.get(String(path));
      if (!set || !set.size) continue;
      if (route.props.children && route.props.children[PATCHED]) continue;
      for (const patch of set) {
        try {
          const result = patch({ ...route.props });
          if (result && result.children !== undefined) route.props.children = result.children;
        } catch (error) { onError?.(path, error); }
      }
      try { if (route.props.children) route.props.children[PATCHED] = true; } catch { /* frozen children */ }
    }
  }
}

/**
 * Insert our tab once, after `afterKey` when that tab exists. Steam rebuilds the
 * list on every render, so the same tab object is reused: a new object per render
 * remounts the panel and loses gamepad focus.
 */
export function insertQamTab(tabs, tab, afterKey, visible) {
  if (!Array.isArray(tabs) || !tab) return 'skipped';
  for (const existing of tabs) {
    if (existing && existing.key === tab.key) {
      if (typeof existing.qAMVisibilitySetter === 'function') existing.qAMVisibilitySetter(visible);
      else existing.initialVisibility = visible;
      return 'present';
    }
  }
  tab.initialVisibility = visible;
  let at = tabs.length;
  if (afterKey !== null && afterKey !== undefined) {
    const index = tabs.findIndex((entry) => entry && entry.key === afterKey);
    if (index >= 0) at = index + 1;
  }
  tabs.splice(at, 0, tab);
  return 'inserted';
}

/** Remove our tab from a list Steam still holds. */
export function removeQamTab(tabs, key) {
  if (!Array.isArray(tabs)) return 0;
  let removed = 0;
  for (let i = tabs.length - 1; i >= 0; i--) if (tabs[i] && tabs[i].key === key) { tabs.splice(i, 1); removed++; }
  return removed;
}

/** Register our numeric key in Steam's QuickAccessTab enum, so the panel gets a real class. */
export function registerTabKey(tabEnum, key, name) {
  if (!tabEnum || typeof tabEnum !== 'object') return false;
  if (tabEnum[key] !== undefined) return tabEnum[key] === name;
  tabEnum[key] = name;
  tabEnum[name] = key;
  return true;
}
