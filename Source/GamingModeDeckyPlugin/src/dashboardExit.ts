type ExitActions = {
  hideOverlay: () => void;
  back: () => void;
  library: () => void;
  closeMenus: () => void;
  isPresent: () => boolean;
  schedule: (callback: () => void, delay: number) => unknown;
};

// Steam may expose a route with no previous entry, or with the same route as
// its previous entry. One back attempt preserves history; the library is the
// bounded fallback when that attempt has left the Dashboard mounted.
export function createDashboardExit(actions: ExitActions) {
  let generation = 0;
  let exiting = false;
  return {
    reset() { generation += 1; exiting = false; },
    leave() {
      if (exiting) return false;
      exiting = true;
      const visit = generation;
      try { actions.hideOverlay(); } catch {}
      try { actions.back(); } catch {}
      actions.schedule(() => {
        if (visit !== generation) return;
        if (actions.isPresent()) {
          try { actions.library(); } catch {}
        }
        try { actions.closeMenus(); } catch {}
      }, 240);
      return true;
    },
  };
}

export function consumeDashboardCancel(event: any, now: number, last: number, held = false): boolean {
  try { event?.preventDefault?.(); event?.stopPropagation?.(); } catch {}
  return !held && !event?.repeat && !event?.nativeEvent?.repeat
    && !event?.detail?.repeat && now - last >= 420;
}
