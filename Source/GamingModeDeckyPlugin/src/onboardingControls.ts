export type OnboardingControlStep = "home" | "decky" | "store" | "audio" | "performance" | "graphics" | "controller" | "customize";
export function onboardingVisibleSteps(visible: readonly string[]) {
  const allowed = new Set(["home", "store", "audio", "performance", "graphics", "controller", "decky"]);
  return [...new Set(visible.filter(id => allowed.has(id)).map(id => id === "home" ? "playhub" : id)), "customize"];
}
let requested: OnboardingControlStep | null = null;
const listeners = new Set<() => void>();
export const getOnboardingControlStep = () => requested;
export function subscribeOnboardingControlStep(listener: () => void) {
  listeners.add(listener);
  return () => { listeners.delete(listener); };
}
export function requestOnboardingControlStep(step: OnboardingControlStep | null) {
  if (requested === step) return;
  requested = step; listeners.forEach(listener => listener());
}
/** An ephemeral view selection only: never writes the user's tab preferences. */
export function resolveOnboardingControlShowcase<T extends string>(step: OnboardingControlStep | null,
  ready: boolean, visible: readonly T[], active: T, customizing: boolean) {
  const showcasing = ready && step !== null && (step === "customize" || visible.includes(step as T));
  return { active: showcasing && step !== "customize" ? step as T : active,
    customizing: showcasing ? step === "customize" : customizing, showcasing };
}
