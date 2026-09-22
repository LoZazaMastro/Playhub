// Independent logical profiles. No device reader, driver, RPC or output sink.
export const profileButtons = ["A", "B", "X", "Y", "LB", "RB", "Back", "Start", "LS", "RS", "Up", "Down", "Left", "Right"] as const;
export type ProfileButton = typeof profileButtons[number];
export interface ControllerProfile {
  version: 1;
  mapping: Record<ProfileButton, ProfileButton | null>;
  leftDeadzone: number;
  rightDeadzone: number;
}
export const controllerProfileCapabilities = Object.freeze({
  localProfiles: true, syntheticPreview: true, liveMapping: false,
  virtualOutput: false, gyro: false, trackpads: false, exclusiveMode: false,
});
export function defaultControllerProfile(): ControllerProfile {
  return { version: 1, mapping: Object.fromEntries(profileButtons.map(button => [button, button])) as ControllerProfile["mapping"], leftDeadzone: 0.1, rightDeadzone: 0.1 };
}
const record = (value: unknown): value is Record<string, unknown> => !!value && typeof value === "object" && !Array.isArray(value);
const button = (value: unknown): value is ProfileButton => profileButtons.includes(value as ProfileButton);
const deadzone = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value) && value >= 0 && value <= 0.5;
export function parseControllerProfile(value: unknown): ControllerProfile | null {
  if (!record(value) || value.version !== 1 || !record(value.mapping) || !deadzone(value.leftDeadzone) || !deadzone(value.rightDeadzone)) return null;
  if (Object.keys(value).some(key => !["version", "mapping", "leftDeadzone", "rightDeadzone"].includes(key))) return null;
  if (Object.keys(value.mapping).length !== profileButtons.length) return null;
  const mapping = {} as ControllerProfile["mapping"];
  for (const key of profileButtons) {
    if (!Object.prototype.hasOwnProperty.call(value.mapping, key)) return null;
    const target = value.mapping[key];
    if (target !== null && !button(target)) return null;
    mapping[key] = target;
  }
  return { version: 1, mapping, leftDeadzone: value.leftDeadzone, rightDeadzone: value.rightDeadzone };
}
export interface ProfileStorage { getItem(key: string): string | null; setItem(key: string, value: string): void; }
function storageKey(slot: number): string {
  if (!Number.isInteger(slot) || slot < 1 || slot > 3) throw Error("Invalid profile slot");
  return `playhub.controller.profile.v1.${slot}`;
}
export function loadControllerProfile(storage: ProfileStorage, slot: number): { profile: ControllerProfile; status: "saved" | "empty" | "invalid" | "storageError" } {
  try {
    const raw = storage.getItem(storageKey(slot));
    if (raw === null) return { profile: defaultControllerProfile(), status: "empty" };
    if (raw.length > 4096) return { profile: defaultControllerProfile(), status: "invalid" };
    let parsed: unknown;
    try { parsed = JSON.parse(raw); } catch { return { profile: defaultControllerProfile(), status: "invalid" }; }
    const profile = parseControllerProfile(parsed);
    return { profile: profile ?? defaultControllerProfile(), status: profile ? "saved" : "invalid" };
  } catch { return { profile: defaultControllerProfile(), status: "storageError" }; }
}
export function saveControllerProfile(storage: ProfileStorage, slot: number, value: unknown): "saved" | "invalid" | "storageError" {
  const profile = parseControllerProfile(value);
  if (!profile) return "invalid";
  try { storage.setItem(storageKey(slot), JSON.stringify(profile)); return "saved"; }
  catch { return "storageError"; }
}
export type Stick = readonly [number, number];
function transformStick(stick: Stick, zone: number): [number, number] {
  const length = Math.hypot(stick[0], stick[1]);
  if (length <= zone) return [0, 0];
  const magnitude = (Math.min(length, 1) - zone) / (1 - zone);
  return [stick[0] / length * magnitude, stick[1] / length * magnitude];
}
export function previewControllerProfile(value: unknown, pressed: readonly ProfileButton[], left: Stick = [0, 0], right: Stick = [0, 0]) {
  const profile = parseControllerProfile(value);
  const validAxis = (axis: unknown) => typeof axis === "number" && Number.isFinite(axis) && Math.abs(axis) <= 1;
  const validStick = (stick: Stick) => Array.isArray(stick) && stick.length === 2 && validAxis(stick[0]) && validAxis(stick[1]);
  if (!profile || !Array.isArray(pressed) || !Array.from(pressed).every(button) || !validStick(left) || !validStick(right)) return null;
  // Recompute from the full pressed set so many-to-one bindings release correctly.
  const buttons = [...new Set(pressed.map((source: ProfileButton) => profile.mapping[source]).filter((target): target is ProfileButton => target !== null))];
  return { buttons, left: transformStick(left, profile.leftDeadzone), right: transformStick(right, profile.rightDeadzone) };
}
