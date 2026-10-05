export interface ShortcutIdentity {
  id: string;
}

export interface ShortcutOrderStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
}

export const SHORTCUT_ORDER_STORAGE_KEY = "playhub.dashboard.shortcut-order.v1";

function uniqueIds(values: readonly unknown[]): string[] {
  const seen = new Set<string>();
  const output: string[] = [];
  for (const value of values) {
    const id = typeof value === "string" ? value.trim() : "";
    if (!id || seen.has(id)) continue;
    seen.add(id);
    output.push(id);
  }
  return output;
}

export function readShortcutOrder(storage: ShortcutOrderStorage | null | undefined): string[] {
  if (!storage) return [];
  try {
    const parsed = JSON.parse(storage.getItem(SHORTCUT_ORDER_STORAGE_KEY) ?? "[]");
    return Array.isArray(parsed) ? uniqueIds(parsed) : [];
  } catch {
    return [];
  }
}

export function applyShortcutOrder<T extends ShortcutIdentity>(
  shortcuts: readonly T[],
  order: readonly string[],
): T[] {
  const positions = new Map(uniqueIds(order).map((id, index) => [id, index]));
  return shortcuts
    .map((shortcut, index) => ({ shortcut, index, position: positions.get(shortcut.id) }))
    .sort((left, right) => {
      const leftKnown = left.position !== undefined;
      const rightKnown = right.position !== undefined;
      if (leftKnown && rightKnown) return left.position! - right.position!;
      if (leftKnown) return -1;
      if (rightKnown) return 1;
      return left.index - right.index;
    })
    .map(({ shortcut }) => shortcut);
}

export function moveShortcutTo<T extends ShortcutIdentity>(
  shortcuts: readonly T[],
  movingId: string,
  targetId: string,
): T[] {
  const sourceIndex = shortcuts.findIndex((shortcut) => shortcut.id === movingId);
  const targetIndex = shortcuts.findIndex((shortcut) => shortcut.id === targetId);
  if (sourceIndex < 0 || targetIndex < 0 || sourceIndex === targetIndex) return [...shortcuts];

  const next = [...shortcuts];
  const [moving] = next.splice(sourceIndex, 1);
  next.splice(targetIndex, 0, moving);
  return next;
}

export function writeShortcutOrder<T extends ShortcutIdentity>(
  storage: ShortcutOrderStorage | null | undefined,
  shortcuts: readonly T[],
): boolean {
  if (!storage) return false;
  try {
    storage.setItem(SHORTCUT_ORDER_STORAGE_KEY, JSON.stringify(uniqueIds(shortcuts.map(({ id }) => id))));
    return true;
  } catch {
    return false;
  }
}
