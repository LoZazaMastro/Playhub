import { DFL, SP_REACT as React } from "./decky";
import type { OnboardingAction } from "./onboardingRuntime";

function componentSource(value: any): string {
  for (let depth = 0; depth < 4 && value; depth++) {
    if (typeof value === "function") return Function.prototype.toString.call(value);
    value = value.type ?? value.render;
  }
  return "";
}

/** Native cached chord data only. No config loading, previewing or writes. */
export function readOnboardingChords(store: any, controller: any) {
  if (!controller || typeof store?.CurrentAppConfigInfo !== "function") return [];
  const config = store.ChordConfiguration;
  // 443510 is Steam's guide-button chord configuration app, not a game/app guess.
  const selected = store.CurrentAppConfigInfo(443510, controller.nControllerIndex);
  if (!config?.url || selected?.URL !== config.url || !Array.isArray(config.sets?.[0]?.source_bindings)) return [];
  const result: { source: number; input: number }[] = [];
  for (const source of config.sets[0].source_bindings) {
    for (const input of source.active_group?.inputs ?? []) {
      for (const activator of input.activators ?? []) {
        // Only the regular-press form can be described by a plain chord glyph.
        if (activator.activation !== 1 || Object.keys(activator.settings ?? {}).length > 0 || source.active_group?.mode_shift) continue;
        if (activator.bindings?.length !== 1) continue;
        const binding = activator.bindings[0];
        if (binding.type === 7 && binding.controller_action?.action === 61
          && typeof source.key === "number" && typeof input.key === "number") {
          if (!result.some(item => item.source === source.key && item.input === input.key)) result.push({ source: source.key, input: input.key });
        }
      }
    }
  }
  return result;
}

export function createOnboardingNativeAdapter(ui: any = DFL) {
  const find = (predicate: (value: any) => boolean) => {
    try { return ui.findModuleExport?.(predicate); } catch { return undefined; }
  };
  const autorun = find(value => typeof value === "function" && /"Autorun"/.test(componentSource(value))
    && componentSource(value).includes("getDisposer_") && componentSource(value).includes("requiresObservable"));
  let Glyph = find(value => { const source = componentSource(value); return source.includes("bKnockout")
    && source.includes("MostRecentlyActiveController") && source.includes("bUseReversedLayout"); });
  const router = ui.Router;
  const configStore = find(value => value && typeof value.CurrentAppConfigInfo === "function" && "ChordConfiguration" in value);
  let glyphModule: any;
  try {
    glyphModule = ui.findModule?.((value: any) => typeof value?._H === "function" && typeof value?.yD === "function"
      && componentSource(value?.UT).includes("controllerModeInput"));
  } catch { /* Unsupported native exports leave the hint textual. */ }
  // MobX memo wrappers conceal the original render closure. Match its already-loaded
  // factory instead; do not execute factories or hardcode webpack module IDs.
  if (!Glyph || !glyphModule) {
    let factories: Record<string, Function> | undefined;
    try {
      const chunks = (window as any).webpackChunksteamui;
      if (typeof chunks?.push === "function" && ui.modules?.get) {
        chunks.push([[Symbol("playhub-onboarding-native")], {}, (require: any) => { factories = require.m; }]);
        for (const [id, factory] of Object.entries(factories ?? {})) {
          const exports = ui.modules.get(id);
          if (!exports) continue;
          const source = Function.prototype.toString.call(factory);
          if (!Glyph && exports.W && exports.X && source.includes("bKnockout")
            && source.includes("bUseReversedLayout") && source.includes("/steaminputglyphs/qam_icon.svg")) Glyph = exports.W;
          if (!glyphModule && exports.UT && typeof exports._H === "function" && typeof exports.yD === "function"
            && source.includes("controllerModeInput") && source.includes("controllerSource")) glyphModule = exports;
        }
      }
    } catch { /* Unknown factory layout means no invented glyph or chord. */ }
  }
  const read = () => {
    const controller = router?.MostRecentlyActiveController;
    const chords = readOnboardingChords(configStore, controller);
    return { controller, chords };
  };
  const subscribe = (listener: () => void) => {
    if (typeof autorun !== "function") return () => {};
    return autorun(() => { read(); listener(); });
  };
  const renderHint = (action: OnboardingAction) => {
    if (!Glyph) return null;
    if (action !== "quick-access") return <Glyph button={action === "confirm" ? 0 : 1} style={{ height: 28 }} />;
    const { controller, chords } = read();
    if (!controller) return null;
    // Dedicated QuickMenu devices follow Steam's native guided-tour mapping.
    if (controller.eControllerType === 10 || controller.eControllerType === 49) return <Glyph button={9} style={{ height: 28 }} />;
    if (!glyphModule || !chords.length) return null;
    const InputGlyph = glyphModule.UT;
    return <span style={{ display: "inline-flex", flexWrap: "wrap", alignItems: "center", gap: 8 }}>
      {chords.map((chord, index) => <React.Fragment key={`${chord.source}-${chord.input}`}>
        {index > 0 && <span>/</span>}<Glyph button={8} style={{ height: 28 }} /><span>+</span>
        <InputGlyph controllerType={controller.eControllerType} controllerStyle={controller.eControllerStyle}
          controllerSource={chord.source} controllerModeInput={glyphModule._H(chord.input)} style={{ height: 28 }} />
      </React.Fragment>)}
    </span>;
  };
  return { renderHint, subscribe, autorun, router };
}
