// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
// null is unknown. A callback for another Steam client never becomes local idle proof.
export function localSteamDownloadActivity(value: unknown): boolean | null {
  if (!value || typeof value !== "object") return null;
  const v = value as Record<string, unknown>;
  if (v.remote_client_id !== "0" || typeof v.paused !== "boolean" || typeof v.update_state !== "string") return null;
  if (v.paused) return false;
  const app = v.update_appid;
  if (typeof app !== "number" || !Number.isInteger(app) || app < 0 || app > 0xffffffff) return null;
  if (app === 0 && v.update_state === "None") return false;
  if (app === 0) return null;
  if (v.update_is_upload === true) return false;
  const rates = [v.update_network_bytes_per_second, v.update_disc_bytes_per_second];
  if (rates.some(rate => typeof rate === "number" && Number.isFinite(rate) && rate > 0)) return true;
  if (["Starting", "Downloading", "Updating", "Preloading", "Preallocating", "Patching", "Unpacking", "Installing", "Verifying", "Validating", "Transferring"].includes(v.update_state)) return true;
  return v.update_state === "None" ? false : null;
}

export function shortcutAppId(executable: string, title: string): number {
  let crc = 0xffffffff;
  for (const byte of new TextEncoder().encode(executable + title)) {
    crc ^= byte;
    for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0);
  }
  return ((~crc) | 0x80000000) >>> 0;
}

export type ShortcutReadback = { unAppID?: number; strShortcutExe?: string; strShortcutLaunchOptions?: string; strShortcutStartDir?: string };
export function shortcutDirectory(value: unknown): string {
  if (typeof value !== "string") return "";
  const path=value.replace(/^"|"$/g, "").replace(/\//g, "\\").toLowerCase();
  return /^[a-z]:\\+$/.test(path) ? path.slice(0,2)+"\\" : path.replace(/\\+$/, "");
}
export function matchingShortcut(proof: ShortcutReadback | null, plan: { executable: string; arguments: string; startDirectory: string }, appId: number): boolean {
  const path = (text: unknown) => typeof text === "string" ? text.replace(/^"|"$/g, "").toLowerCase() : "";
  return !!proof && proof.unAppID === appId && appId > 0 &&
    path(proof.strShortcutExe) === path(plan.executable) &&
    proof.strShortcutLaunchOptions === plan.arguments && shortcutDirectory(proof.strShortcutStartDir) === shortcutDirectory(plan.startDirectory);
}

export function sameStoreDownload(a: { jobId: string; key: { store: string; id: string }; accountRevision: number; installPath: string }, b: typeof a): boolean {
  return !!a?.key && !!b?.key && a.jobId === b.jobId && a.key.store === b.key.store && a.key.id === b.key.id &&
    Number.isSafeInteger(a.accountRevision) && a.accountRevision === b.accountRevision &&
    typeof a.installPath === "string" && a.installPath.toLowerCase() === b.installPath?.toLowerCase();
}
