export const ONBOARDING_STORAGE_KEY = "playhub.onboarding.2.0.0.v1";
export type OnboardingStep = "intro" | "playhub" | "decky" | "store" | "audio" | "performance" | "graphics" | "controller" | "customize" | "outro" | "done";
export interface OnboardingState { step: OnboardingStep; paused: boolean; completed: boolean; enterRequested?: boolean; visited?: OnboardingStep[]; awaitingTabs?: boolean }
export interface OnboardingFacts {
  steps?: readonly OnboardingStep[];
  activeReady?: boolean;
  qamOpen: boolean;
  playhubSelected: boolean;
  playhubAnchor: boolean;
  deckyEnabled: boolean;
  deckyVisible: boolean;
  deckyAnchor: boolean;
  storeAnchor?: boolean;
  customizeAnchor?: boolean;
  controlShowcase?: boolean;
  storeVisible?: boolean;
  customizeAvailable?: boolean;
}
export type OnboardingEvent = { type: "observe"; facts: OnboardingFacts }
  | { type: "confirm"; facts: OnboardingFacts } | { type: "pause" | "resume" };
export function readOnboardingState(raw: string | null): OnboardingState {
  try {
    const value = JSON.parse(raw ?? "null");
    if (value?.completed === true && value.step === "done") return { step: "done", paused: false, completed: true };
    const valid = ["intro", "playhub", "decky", "store", "audio", "performance", "graphics", "controller", "customize", "outro"];
    if (valid.includes(value?.step)) return { step: value.step, paused: value.paused === true, completed: false,
      ...(value.awaitingTabs === true ? { awaitingTabs: true } : {}),
      ...(Array.isArray(value.visited) ? { visited: value.visited.filter((step: unknown) => valid.includes(step as string)) } : {}) };
  } catch { /* Invalid or older storage starts an unfinished guide. */ }
  return { step: "intro", paused: false, completed: false };
}
export function reduceOnboarding(state: OnboardingState, event: OnboardingEvent): OnboardingState {
  if (state.completed) return state;
  if (event.type === "pause") return { ...state, paused: true };
  if (event.type === "resume") return { ...state, paused: false };
  if (state.paused || !("facts" in event)) return state;
  const f = event.facts;
  // The closing slide lives outside the menu: no tab list exists there, so it must never
  // fall into a branch that depends on one.
  if (state.step === "outro") return event.type === "confirm" ? { step: "done", paused: false, completed: true } : state;
  if (f.steps) {
    if (!f.steps.length) return state.step === "intro" && f.qamOpen ? { ...state, awaitingTabs: true } : state;
    if (event.type === "observe") {
      if (state.step === "intro" && f.qamOpen) return { ...state, step: f.steps[0] ?? "customize", awaitingTabs: false };
      if (state.step !== "intro" && !f.steps.includes(state.step) && f.qamOpen && f.playhubSelected)
        return { ...state, visited: [...(state.visited ?? []), state.step],
          step: f.steps.find(step => !state.visited?.includes(step)) ?? "customize" };
      return state;
    }
    if (f.qamOpen && f.playhubSelected && f.activeReady && state.step !== "intro") {
      const visited = [...new Set([...(state.visited ?? []), state.step])];
      const next = f.steps.find(step => !visited.includes(step));
      return next ? { ...state, step: next, visited } : { ...state, step: "outro", visited };
    }
    return state;
  }
  if (event.type === "observe") {
    if (state.step === "playhub" && state.enterRequested && f.qamOpen && f.playhubSelected) return { ...state, step: "decky", enterRequested: false };
    // Opening QAM dismisses the intro even when its native tab has not mounted yet.
    if (state.step === "intro" && f.qamOpen) return { ...state, step: "playhub" };
    if (f.controlShowcase && f.qamOpen && f.playhubSelected) {
      if (state.step === "decky" && (!f.deckyEnabled || !f.deckyVisible))
        return f.storeVisible ? { ...state, step: "store" } : f.customizeAvailable ? { ...state, step: "customize" } : state;
      if (state.step === "store" && f.storeVisible === false && f.customizeAvailable) return { ...state, step: "customize" };
    }
    return state;
  }
  if (state.step === "playhub" && f.qamOpen && f.playhubAnchor) return { ...state,
    step: f.playhubSelected ? "decky" : "playhub", enterRequested: !f.playhubSelected };
  // Completion requires a visible final explanation, never just opening the QAM.
  if (state.step === "decky" && f.qamOpen && f.playhubSelected
    && (f.deckyAnchor || !f.deckyEnabled || !f.deckyVisible)) {
    return (f.storeVisible ?? f.storeAnchor) ? { ...state, step: "store" } : (f.customizeAvailable ?? f.customizeAnchor) ? { ...state, step: "customize" }
      : { ...state, step: "outro" };
  }
  if (state.step === "store" && f.qamOpen && f.playhubSelected && f.storeAnchor) return (f.customizeAvailable ?? f.customizeAnchor)
    ? { ...state, step: "customize" } : { ...state, step: "outro" };
  if (state.step === "customize" && f.qamOpen && f.playhubSelected && f.customizeAnchor) return { ...state, step: "outro" };
  return state;
}
