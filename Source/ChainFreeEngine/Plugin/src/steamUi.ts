// Verified Steam UI adapter.
//
// Steam's UI components are resolved by @decky/ui (LGPL-2.1, see
// vendor/DeckyUI-LICENSE), the same resolver Decky itself uses, instead of the
// heuristic source matching of the first trial: that matching picked the wrong
// native dropdown (the country field showed "Steam Beta Update").
// The adapter only reads Steam's own webpack chunk. It never starts Decky, and
// it never touches loader globals or another plugin's state.
import * as DeckyUi from '@decky/ui';
import { REQUIRED_STEAM_UI } from './steamUiContract';

export { REQUIRED_STEAM_UI };

export function missingSteamUi(ui: Record<string, unknown>): string[] {
  return REQUIRED_STEAM_UI.filter((name) => {
    const value = ui?.[name];
    return value === undefined || value === null;
  });
}

export function verifySteamUi(ui: Record<string, unknown>): Record<string, unknown> {
  const missing = missingSteamUi(ui);
  if (missing.length) throw new Error('Steam UI components unavailable: ' + missing.join(', '));
  return ui;
}

export const DFL = verifySteamUi(DeckyUi as unknown as Record<string, unknown>);

const host = (window as any).__PLAYHUB_HOST__;
if (host) {
  host.DFL = DFL;
  host.qamAvailable = true;
  host.diagnostics = { ...(host.diagnostics ?? {}), uiResolver: '@decky/ui', components: REQUIRED_STEAM_UI.length };
}
