export interface ScopedDeckyState {
  state: any;
  setVisible(visible: boolean): void;
  dispose(): void;
}

/** Keep native PluginView and its provider, but isolate navigation from the QAM. */
export function createScopedDeckyState(real: any): ScopedDeckyState {
  const delegates = ["setVersionInfo", "setIsLoaderUpdating", "setPluginOrder", "setDisabledPlugins"];
  if (typeof real?.publicState !== "function" || !real.eventBus?.addEventListener
    || !real.eventBus?.removeEventListener || delegates.some(key => typeof real[key] !== "function")) {
    throw new Error("unsupported_decky_state");
  }
  const eventBus = new EventTarget();
  let selected: string | null = null;
  let disposed = false;
  let visible = true;
  const notify = () => { if (!disposed) eventBus.dispatchEvent(new Event("update")); };
  const state: any = {
    eventBus,
    publicState() {
      const snapshot = real.publicState();
      if (!Array.isArray(snapshot?.plugins)) throw new Error("unsupported_plugin_inventory");
      const plugins = snapshot.plugins.filter((plugin: any) => plugin.name !== "Playhub");
      return { ...snapshot, plugins, activePlugin: visible ? plugins.find((plugin: any) => plugin.name === selected) ?? null : null };
    },
    setActivePlugin(name: string) { selected = name === "Playhub" ? null : name; notify(); },
    closeActivePlugin() { selected = null; notify(); },
  };
  for (const key of delegates) state[key] = real[key].bind(real);
  state.publicState();
  real.eventBus.addEventListener("update", notify);
  return { state, setVisible(next: boolean) {
    if (next === visible) return;
    visible = next;
    notify();
  }, dispose() {
    if (disposed) return;
    disposed = true;
    real.eventBus.removeEventListener("update", notify);
  } };
}
