// Stable persisted IDs: audio is Video (third); performance is Audio (fourth).
export const CONTROL_TABS = ["home", "store", "audio", "performance", "graphics", "controller", "decky"] as const;
export type ControlTab = typeof CONTROL_TABS[number];
export interface ControlPreferences {
  order: ControlTab[];
  hidden: ControlTab[];
  collapsed: string[];
  active: ControlTab;
  deckyHostEnabled: boolean;
  topbarDateEnabled: boolean;
  topbarDateFormat: string;
  topbarClockLeft: boolean;
}
export function visibleControlTabs(prefs: ControlPreferences, deckyAvailable: boolean): ControlTab[] {
  const visible = prefs.order.filter(id => !prefs.hidden.includes(id) && (id !== "decky" || deckyAvailable));
  return visible.length ? visible : ["home"];
}
export function normalizeControlPreferences(value: any): ControlPreferences {
  const valid = (v: unknown): v is ControlTab => CONTROL_TABS.includes(v as ControlTab);
  const order = [...new Set<ControlTab>((Array.isArray(value?.order) ? value.order : []).filter(valid))];
  for (const tab of CONTROL_TABS) if (!order.includes(tab)) order.push(tab);
  const hidden = [...new Set<ControlTab>((Array.isArray(value?.hidden) ? value.hidden : []).filter(valid))];
  const deckyHostEnabled = value?.deckyHostEnabled === undefined || value.deckyHostEnabled === true;
  const eligible = (id: ControlTab) => valid(id);
  if (order.every(id => !eligible(id) || hidden.includes(id))) hidden.splice(hidden.indexOf("home"), 1);
  const active = valid(value?.active) && eligible(value.active) && !hidden.includes(value.active) ? value.active : order.find(id => eligible(id) && !hidden.includes(id))!;
  const dateFormats = ["auto", "dd_mm_yyyy", "dd_mm_yy", "yyyy_mm_dd", "dd_month_yyyy", "weekday_dd_month", "weekday_short_dd_month", "month_dd_yyyy", "month_short_dd_yyyy", "iso"];
  const topbarDateFormat = dateFormats.includes(value?.topbarDateFormat) ? value.topbarDateFormat : "auto";
  return { order, hidden, active, deckyHostEnabled, topbarDateEnabled: value?.topbarDateEnabled !== false, topbarDateFormat, topbarClockLeft: value?.topbarClockLeft === true, collapsed: [...new Set<string>((Array.isArray(value?.collapsed) ? value.collapsed : []).filter((id: unknown) => typeof id === "string" && id.length < 80))] };
}
export function moveControlTab(prefs: ControlPreferences, id: ControlTab, direction: -1 | 1): ControlPreferences {
  const order = [...prefs.order];
  const from = order.indexOf(id), to = from + direction;
  if (from < 0 || to < 0 || to >= order.length) return prefs;
  [order[from], order[to]] = [order[to], order[from]];
  return normalizeControlPreferences({ ...prefs, order });
}
