export function createSteamTourGate() {
  let historicalSeen: boolean | null = null;
  let visible = false;
  let started = false;
  let finished = false;
  let rendererKnown = false;
  let modalCount: number | null = null;
  return {
    setHistoricalSeen(value: boolean | null) { historicalSeen = value; },
    setVisible(value: boolean) {
      if (value && !visible) { started = true; finished = false; }
      visible = value;
    },
    observeRenderer(known: boolean, active: boolean, count: number | null) {
      if (known && rendererKnown && visible && !active) finished = true;
      if (known && active && !visible) { started = true; finished = false; }
      rendererKnown = known;
      visible = active;
      modalCount = count;
    },
    finish() { finished = true; },
    ready() { return rendererKnown && modalCount === 0 && !visible && (finished || (historicalSeen === true && !started)); },
  };
}

/** Read the current committed fiber tree, never call Steam's independent tour hook. */
export function probeSteamTourRenderer(document: Document): { known: boolean; active: boolean; startupKnown: boolean; startupPlaying: boolean } {
  let root: any;
  let startupKnown = false;
  let startupPlaying = false;
  const candidates: Element[] = [];
  if (document.body && typeof document.createTreeWalker === "function") {
    const walker = document.createTreeWalker(document.body, 1);
    candidates.push(document.body);
    for (let node = walker.nextNode(); node && candidates.length < 160; node = walker.nextNode()) candidates.push(node as Element);
  } else candidates.push(...Array.from(document.querySelectorAll("body, body > div, #root, #root div")).slice(0, 160));
  for (const element of candidates) {
    const key = Object.keys(element).find(key => key.startsWith("__reactFiber$") || key.startsWith("__reactContainer$"));
    if (!key) continue;
    let fiber = (element as any)[key];
    let hops = 0;
    while (fiber?.return && hops++ < 200) fiber = fiber.return;
    if (fiber?.stateNode?.current) { root = fiber.stateNode.current; break; }
  }
  if (!root) return { known: false, active: false, startupKnown, startupPlaying };
  const pending = [root];
  let count = 0;
  while (pending.length && count++ < 20000) {
    const fiber = pending.pop();
    if (typeof fiber?.memoizedProps?.bPlayingStartupMovie === "boolean") {
      startupKnown = true;
      startupPlaying ||= fiber.memoizedProps.bPlayingStartupMovie;
    }
    const type = fiber?.type;
    if (typeof type === "function") {
      const source = Function.prototype.toString.call(type).replace(/\s/g, "");
      if (source.includes("bShowTour") && source.includes("onComplete") && source.includes("active:!0")) {
        return { known: true, active: !!fiber.child, startupKnown, startupPlaying };
      }
    }
    if (fiber?.sibling) pending.push(fiber.sibling);
    if (fiber?.child) pending.push(fiber.child);
  }
  return { known: false, active: false, startupKnown, startupPlaying };
}

/** Steam writes its seen flag at tour mount. A tour seen in this session needs the finish event. */
export function observeSteamTourCompletion(client: any, changed: () => void) {
  const gate = createSteamTourGate();
  let alive = true;
  const cleanups: (() => void)[] = [];
  const retain = (value: any) => {
    if (typeof value === "function") cleanups.push(value);
    else if (typeof value?.unregister === "function") cleanups.push(() => value.unregister());
  };
  const readHistory = async () => {
    try {
      const value = await client?.Storage?.GetString?.("Deck_GuidedTourVersionSeen");
      if (alive) { gate.setHistoricalSeen(value == null ? false : Number.parseInt(value, 10) >= 1); changed(); }
    } catch { if (alive) { gate.setHistoricalSeen(null); changed(); } }
  };
  try {
    const properties = client?.OpenVR?.PathProperties;
    if (typeof properties?.RegisterForPathPropertyChange === "function" && typeof properties?.GetInt32PathProperty === "function") {
      retain(properties.RegisterForPathPropertyChange("/steam/guidedtour", async () => {
        try { const phase = await properties.GetInt32PathProperty("/steam/guidedtour");
          if (alive && phase === 7) { gate.finish(); changed(); }
        } catch { /* Unknown completion never opens the gate. */ }
      }));
    }
    if (typeof client?.User?.RegisterForLoginStateChange === "function") retain(client.User.RegisterForLoginStateChange(readHistory));
  } catch { /* Historical non-first-run users can still be recognized safely. */ }
  void readHistory();
  return { gate, stop() { alive = false; for (const cleanup of cleanups.splice(0)) cleanup(); } };
}
