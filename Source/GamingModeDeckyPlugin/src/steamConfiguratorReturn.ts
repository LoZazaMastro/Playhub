import type { MirrorPresentation } from "./steamMirrorOverlay";
import type { NativeGameProof, NativeBrowser } from "./steamNativeGameOverlay";
import type { SteamNavigationRecoveryContext } from "./steamNavigationReturn";

type NativeInput = { ForceConfiguratorFocus?: (focused: boolean) => void };
type Claim = { app: number; session: string; gamePid: number; gameBirth: string; input: NativeInput; attempted: boolean };

// Steam pairs overlay activation with ForceConfiguratorFocus(active). Its
// deactivation handler can lose the overlay instance before sending false.
// This receipt authorizes only one cleanup after the retained game has exited;
// it never represents a getter for Steam's unobservable native focus flag.
export function createSteamConfiguratorReturn() {
  let claim: Claim | undefined;
  return {
    admitted(app: number, mirror: MirrorPresentation, input: NativeInput | undefined) {
      if (!Number.isInteger(app) || app < 0x80000000 || app > 0xffffffff || !input
          || typeof input.ForceConfiguratorFocus !== "function" || mirror.protocol !== 1
          || (mirror.state === "preparing" ? mirror.actualScope !== 0 && mirror.actualScope !== 1
            : mirror.actualScope !== 1 && !(mirror.actualScope === 3 && mirror.ownedForegroundPhase === 1 && mirror.state === "shown"
              && mirror.preparedCurrent === true && mirror.shownCurrent === true && mirror.leaseCurrent === true))
          || !["preparing", "prepared", "shown"].includes(mirror.state)
          || !/^[0-9a-f]{64}$/i.test(mirror.sessionId) || !Number.isInteger(mirror.gamePid) || mirror.gamePid <= 0
          || mirror.gamePid > 0xffffffff || !/^[1-9]\d{0,19}$/.test(mirror.gameBirth)
          || BigInt(mirror.gameBirth) > 0xffffffffffffffffn
          || mirror.gameId !== ((BigInt(app) << 32n) | 0x02000000n).toString()) return;
      if (claim?.app === app && claim.session === mirror.sessionId && claim.gamePid === mirror.gamePid
          && claim.gameBirth === mirror.gameBirth && claim.input === input) return;
      claim = { app, session: mirror.sessionId, gamePid: mirror.gamePid, gameBirth: mirror.gameBirth, input, attempted: false };
    },
    nativeAdmitted(app: number, native: NativeGameProof, browser: NativeBrowser, input: NativeInput | undefined) {
      // An original-game-host admission is cleanup-only. It is never a MirrorPresentation.
      if (!Number.isInteger(app) || app < 0x80000000 || app > 0xffffffff || !input
          || typeof input.ForceConfiguratorFocus !== "function" || native.protocol !== 1 || native.routingOnly !== true
          || native.canonicalAppId !== app || native.fullGameId !== ((BigInt(app) << 32n) | 0x02000000n).toString()
          || typeof native.sessionId !== "string" || !/^[0-9a-f]{64}$/i.test(native.sessionId)
          || typeof native.observedGameId !== "string" || !/^[1-9]\d{0,19}$/.test(native.observedGameId)
          || BigInt(native.observedGameId) > 0xffffffffffffffffn || !Number.isInteger(native.gamePid)
          || native.gamePid <= 0 || native.gamePid > 0xffffffff || typeof native.gameBirth !== "string" || !/^[1-9]\d{0,19}$/.test(native.gameBirth)
          || BigInt(native.gameBirth) > 0xffffffffffffffffn || !Number.isInteger(native.overlayPid) || native.overlayPid <= 0
          || native.overlayPid > 0xffffffff || native.overlayPid === native.companionPid || native.overlayPid === native.gamePid
          || browser.overlayPid !== native.overlayPid || !Number.isInteger(browser.browserId) || browser.browserId <= 0
          || browser.browserId > 0xffffffff || !Number.isInteger(browser.appId) || browser.appId < 0 || browser.appId > 0xffffffff) return;
      if (claim?.app === app && claim.session === native.sessionId && claim.gamePid === native.gamePid
          && claim.gameBirth === native.gameBirth && claim.input === input) return;
      claim = { app, session: native.sessionId, gamePid: native.gamePid, gameBirth: native.gameBirth, input, attempted: false };
    },
    forget() { claim = undefined; },
    pending(app: number) { return claim?.app === app && claim.attempted === false; },
    async release(app: number, context: SteamNavigationRecoveryContext, current: () => boolean,
      input: () => NativeInput | undefined): Promise<boolean> {
      const held = claim;
      const valid = () => {
        const currentNow = current() && context.isCurrent();
        return currentNow && claim === held && held != null && !held.attempted && held.app === app
          && context.exitedGame?.pid === held.gamePid && context.exitedGame.birth === held.gameBirth
          && !context.signal.aborted;
      };
      if (!held || !valid() || input() !== held.input) return false;
      try {
        // The native service revalidates the retained process death, canonical
        // app, Steam window PID/birth and actual foreground after this await.
        if (!await context.confirmForeground() || !valid()) return false;
        const receiver = input(), call = receiver?.ForceConfiguratorFocus;
        if (receiver !== held.input || typeof call !== "function" || !valid()) return false;
        if (input() !== receiver || !valid()) return false;
        // Reserve before dispatch: a synchronous callback or an ambiguous throw
        // cannot issue a second global-lane release for this owned session.
        held.attempted = true;
        call.call(receiver, false);
        return true; // Dispatch only. A real native controller event is proof.
      } catch { return false; }
    },
  };
}

export type SteamConfiguratorReturn = ReturnType<typeof createSteamConfiguratorReturn>;
