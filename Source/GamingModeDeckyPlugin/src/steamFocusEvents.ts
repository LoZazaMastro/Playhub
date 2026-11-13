/** Sample Valve's committed state from the existing return watcher, never its native callback. */
export function observeSteamOverlayFocus(
  readStore: () => any,
  changed: (appId: number, pid: number) => void,
) {
  let disposed = false;
  let observedStore: any;
  let previous: { appId: number; pid: number } | undefined;
  const refresh = () => {
    if (disposed) return;
    try {
      const store = readStore();
      if (store !== observedStore) { observedStore = store; previous = undefined; }
      if (!store) return;
      const appId = store.GetFocusedOverlayAppID?.() ?? store.m_unFocusedOverlayAppID;
      const pid = store.GetFocusedOverlayPID?.() ?? store.m_unFocusedOverlayPID;
      if (!Number.isInteger(appId) || appId < 0 || appId > 0xffffffff
          || !Number.isInteger(pid) || pid < 0 || pid > 0xffffffff
          || readStore() !== store) return;
      const prior = previous;
      previous = { appId, pid };
      if (prior && (prior.appId !== appId || prior.pid !== pid)) changed(appId, pid);
    } catch { /* An observer cannot interrupt Steam's window or the return watcher. */ }
  };
  return { refresh, dispose() { disposed = true; observedStore = undefined; previous = undefined; } };
}
