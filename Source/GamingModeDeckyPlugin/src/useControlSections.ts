import { SP_REACT as React } from "./decky";
import { toggleControlSection } from "./controlCenterState";

// The panel preference store owns expansion across QAM openings and tab changes.
export function useControlSections(expanded: string[], setExpanded: (change: (current: string[]) => string[]) => void) {
  const toggle = React.useCallback((id: string) => setExpanded(current => toggleControlSection(current, id)), [setExpanded]);
  return { expanded, toggle };
}
