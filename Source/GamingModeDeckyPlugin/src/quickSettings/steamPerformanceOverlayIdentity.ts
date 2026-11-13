// Visibility evidence only. This never grants game focus or controller ownership.
import type { OwnedNativeOverlayVisibility } from "../steamOwnedLaunchDispatcher";
export function performanceNativeVisibilityMatches(entry: any, value: OwnedNativeOverlayVisibility | undefined, app: number): boolean {
  const info=entry?.params?.browserInfo;
  return !!value&&value.app===app&&value.entry===entry&&value.pid===info?.m_unPID&&value.browserId===info?.m_nBrowserID
    &&value.nativeApp===info?.m_unAppID&&value.rendererToken===String(info?.m_gameID??"");
}
export function performanceOverlayMatches(entry: any, store: any, app: number, observedGameId?: string | null,
  native?: OwnedNativeOverlayVisibility): boolean {
  if (!Number.isInteger(app) || app <= 0 || app > 0xffffffff) return false;
  const info = entry?.params?.browserInfo;
  const full = app >= 0x80000000 ? ((BigInt(app) << 32n) | 0x02000000n).toString() : String(app);
  if (String(info?.m_gameID ?? "") === full) return true;
  // Steam's injected game host can expose the shortcut's compact ID, while
  // its activation callback carries a separate native AppID. Bind both to
  // the actual mapped browser; low AppID bits alone are never a match.
  return app >= 0x80000000 && (performanceNativeVisibilityMatches(entry,native,app)||String(info?.m_gameID ?? "") === String(app)
      ||typeof observedGameId === "string"&&/^[1-9]\d{0,19}$/.test(observedGameId)&&String(info?.m_gameID ?? "")===observedGameId)
    && Number.isInteger(info?.m_unPID) && info.m_unPID > 0
    && Number.isInteger(info?.m_nBrowserID) && info.m_nBrowserID > 0
    && Number.isInteger(info?.m_unAppID) && info.m_unAppID > 0 && info.m_unAppID <= 0xffffffff
    && store?.m_mapOverlayPopupByPID?.get(info.m_unPID) === entry
    && store?.OverlayWindows?.includes(entry) === true
    && entry.IsGamepadUIOverlayWindow?.() === true;
}

export function performanceActivationMatches(entry: any, activation: any, app: number): boolean {
  const info = entry?.params?.browserInfo;
  return activation?.app === app && activation.active === true
    && activation.entry === entry && activation.browserId === info?.m_nBrowserID
    && info?.m_unPID === activation.pid && info?.m_unAppID === activation.nativeApp;
}
