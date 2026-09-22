export interface OverlayIdentity {
  gameID: string;
  unPID: number;
  nBrowserID: number;
  appID: number;
}

export function selectOverlay(infos: unknown, runningAppId?: number): OverlayIdentity | null {
  if (!Array.isArray(infos)) return null;
  const candidates = infos.filter((info) => info && Number(info.unPID) > 0 &&
    Number(info.appID) > 0 && Number.isInteger(Number(info.nBrowserID)) &&
    Number(info.nBrowserID) >= 0 && /^\d+$/.test(String(info.gameID ?? "")) &&
    String(info.gameID) !== "0" && Number(info.appID) !== 993090);
  const matching = runningAppId ? candidates.filter((info) => Number(info.appID) === runningAppId) : candidates;
  if (matching.length !== 1) return null;
  const info = matching[0];
  return { gameID: String(info.gameID), unPID: Number(info.unPID),
    nBrowserID: Number(info.nBrowserID), appID: Number(info.appID) };
}

export function findOverlayWindow(windows: unknown, identity: OverlayIdentity | null): any | null {
  if (!identity || !Array.isArray(windows)) return null;
  for (const candidate of windows) {
    try {
      const info = candidate?.params?.browserInfo;
      if (candidate?.IsGamepadUIOverlayWindow?.() !== true ||
          String(info?.m_gameID) !== identity.gameID ||
          Number(info?.m_unPID) !== identity.unPID ||
          Number(info?.m_nBrowserID) !== identity.nBrowserID) continue;
      const browser = candidate.BrowserWindow ?? candidate.m_BrowserWindow;
      if (browser?.document?.body && !browser.closed) return candidate;
    } catch { /* A closing browser can reject property access. */ }
  }
  return null;
}

// Polling is finite and never reopens an overlay after the user closes it.
export async function waitForOverlay<T>(find: () => T | null, cancelled: () => boolean,
  delay: () => Promise<void>): Promise<T | null> {
  for (let attempt = 0; attempt < 40; attempt++) {
    if (cancelled()) return null;
    const target = find();
    if (target) return target;
    await delay();
  }
  return null;
}
