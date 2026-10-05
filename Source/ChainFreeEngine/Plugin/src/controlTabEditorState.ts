import { CONTROL_TABS, normalizeControlPreferences, type ControlPreferences, type ControlTab } from "./controlCenterState";

export interface TabEditorState { selected: ControlTab; draftOrder: ControlTab[] | null }
export type TabEditorAction = { type: "select"; id: ControlTab } | { type: "move"; direction: -1 | 1 } | { type: "secondary" | "confirm" | "cancel" | "reset" };
export const createTabEditor = (selected: ControlTab = "home"): TabEditorState => ({ selected, draftOrder: null });
export function canToggleTab(prefs: ControlPreferences, id: ControlTab, deckyAvailable: boolean) {
  return prefs.hidden.includes(id) || id === "decky" && !deckyAvailable ||
    prefs.order.some(other => other !== id && !prefs.hidden.includes(other) && (other !== "decky" || deckyAvailable));
}
export function tabEditorTransition(prefs: ControlPreferences, editor: TabEditorState, action: TabEditorAction, deckyAvailable: boolean) {
  let preferences = prefs;
  let next = editor;
  if (action.type === "select") {
    if (!editor.draftOrder) next = { ...editor, selected: action.id };
  } else if (action.type === "cancel") next = { ...editor, draftOrder: null };
  else if (action.type === "reset") {
    preferences = normalizeControlPreferences({ ...prefs, order: [...CONTROL_TABS], hidden: [] });
    next = { ...editor, draftOrder: null };
  } else if (action.type === "move" && editor.draftOrder) {
    const order = [...editor.draftOrder];
    const eligible = order;
    const target = eligible[eligible.indexOf(editor.selected) + action.direction];
    if (target) {
      const from = order.indexOf(editor.selected), to = order.indexOf(target);
      [order[from], order[to]] = [order[to], order[from]];
      next = { ...editor, draftOrder: order };
    }
  } else if (action.type === "secondary" || action.type === "confirm") {
    if (editor.draftOrder) {
      preferences = normalizeControlPreferences({ ...prefs, order: editor.draftOrder });
      next = { ...editor, draftOrder: null };
    } else if (action.type === "secondary") next = { ...editor, draftOrder: [...prefs.order] };
    else if (canToggleTab(prefs, editor.selected, deckyAvailable)) {
      const hidden = prefs.hidden.includes(editor.selected) ? prefs.hidden.filter(id => id !== editor.selected) : [...prefs.hidden, editor.selected];
      preferences = normalizeControlPreferences({ ...prefs, hidden });
    }
  }
  return { editor: next, preferences };
}
