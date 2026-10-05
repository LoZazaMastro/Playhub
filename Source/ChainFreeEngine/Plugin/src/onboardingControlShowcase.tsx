import { SP_REACT as React } from "./decky";
import { getOnboardingControlStep, subscribeOnboardingControlStep, resolveOnboardingControlShowcase } from "./onboardingControls";

export function useOnboardingControlShowcase<T extends string>(enabled: boolean, ready: boolean,
  visible: readonly T[], active: T, customizing: boolean) {
  const step = React.useSyncExternalStore(subscribeOnboardingControlStep, getOnboardingControlStep);
  return resolveOnboardingControlShowcase(enabled ? step : null, ready, visible, active, customizing);
}
