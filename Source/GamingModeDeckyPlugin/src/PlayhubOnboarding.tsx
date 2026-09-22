import { createPortal } from "react-dom";
import { SP_REACT as React } from "./decky";
import { OnboardingIllustration } from "./OnboardingIllustration";
import playhubWordmark from "../assets/playhub-wordmark-large.png";
import { getOnboardingCopy, getOnboardingActionCopy, getOnboardingExtraCopy } from "./onboardingLocale";
import { getOnboardingTabCopy } from "./onboardingTabLocale";
import { positionOnboardingCoach, positionOnboardingShowcase, onboardingPortalGeometry } from "./onboardingAnchors";
import { getPlayhubOnboardingSnapshot, subscribePlayhubOnboarding, markPlayhubOnboardingPresented,
  renderPlayhubOnboardingHint, reportPlayhubOnboardingAction } from "./onboardingRuntime";
export { initPlayhubOnboarding, reportPlayhubOnboardingAction, resumePlayhubOnboarding,
  getPlayhubOnboardingSnapshot, subscribePlayhubOnboarding } from "./onboardingRuntime";
export type { PlayhubOnboardingOptions, OnboardingEnvironment, OnboardingAction } from "./onboardingRuntime";

class HintBoundary extends React.Component<React.PropsWithChildren, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  render() { return this.state.failed ? null : this.props.children; }
}

/** No modal, focus scope, raw input subscription or pointer-blocking backdrop. */
export function PlayhubOnboarding({ locale }: { locale: unknown }) {
  const snapshot = React.useSyncExternalStore(subscribePlayhubOnboarding, getPlayhubOnboardingSnapshot);
  const box = React.useRef<HTMLDivElement>(null);
  const portal = React.useRef<HTMLDivElement>(null);
  const [geometry, setGeometry] = React.useState<ReturnType<typeof onboardingPortalGeometry>>(null);
  const [qamGeometry, setQamGeometry] = React.useState<ReturnType<typeof onboardingPortalGeometry>>(null);
  const pointerStarted = React.useRef<string | null>(null);
  const [height, setHeight] = React.useState(220);
  const copy = getOnboardingCopy(locale);
  const extra = getOnboardingExtraCopy(locale);
  const { view } = snapshot;
  const tabCopy = view === "audio" || view === "performance" || view === "graphics" || view === "controller" || view === "customize"
    ? getOnboardingTabCopy(locale, view) : null;
  const clickAction = (event: React.MouseEvent, action: "confirm" | "cancel") => {
    event.preventDefault(); event.stopPropagation();
    if (action === "confirm" && (event.target as Element)?.closest?.('[data-playhub-onboarding-action="cancel"]')) return;
    const freshPointer = pointerStarted.current === view;
    pointerStarted.current = null;
    if (action !== "cancel" && event.detail > 0 && !freshPointer) return;
    reportPlayhubOnboardingAction(action);
  };
  React.useLayoutEffect(() => {
    if (!view || !box.current) return;
    pointerStarted.current = null;
    const element = box.current;
    const measure = () => {
      setHeight(element.offsetHeight);
      const next = portal.current ? onboardingPortalGeometry(snapshot.document!, portal.current, snapshot.anchor) : null;
      setGeometry(previous => JSON.stringify(previous) === JSON.stringify(next) ? previous : next);
      const qam = portal.current ? onboardingPortalGeometry(snapshot.document!, portal.current, snapshot.qamBounds ?? null) : null;
      setQamGeometry(previous => JSON.stringify(previous) === JSON.stringify(qam) ? previous : qam);
    };
    measure();
    const Observer = (element.ownerDocument.defaultView as any)?.ResizeObserver;
    const observer = Observer ? new Observer(measure) : null;
    observer?.observe(element);
    if (portal.current) observer?.observe(portal.current);
    const viewport = snapshot.document?.defaultView?.visualViewport;
    viewport?.addEventListener("resize", measure);
    viewport?.addEventListener("scroll", measure);
    return () => { observer?.disconnect(); viewport?.removeEventListener("resize", measure);
      viewport?.removeEventListener("scroll", measure); markPlayhubOnboardingPresented(null); };
  }, [view, locale, snapshot.document, snapshot.anchor?.left, snapshot.anchor?.top, snapshot.anchor?.width,
    snapshot.anchor?.height, snapshot.qamBounds?.left, snapshot.qamBounds?.top, snapshot.qamBounds?.width,
    snapshot.qamBounds?.height, snapshot.width, snapshot.height]);
  const fullscreen = view === "intro" || view === "outro";
  // Sized for the space beside the menu, not for a tooltip.
  const blockWidth = fullscreen ? 532 : 576;
  const showcase = geometry && !fullscreen ? positionOnboardingShowcase(geometry.anchor, qamGeometry?.anchor ?? null,
    geometry, { width: blockWidth, height }, snapshot.footerInset) : null;
  // Consecutive steps share the same band, so the next slide is drawn as soon as it exists
  // instead of leaving the menu captured with nothing on screen while its geometry settles.
  const held = React.useRef<ReturnType<typeof positionOnboardingShowcase>>(null);
  React.useLayoutEffect(() => { if (showcase) held.current = showcase; },
    [showcase?.left, showcase?.top, showcase?.width, showcase?.maxHeight]);
  React.useLayoutEffect(() => { if (!snapshot.qamOpen) held.current = null; }, [snapshot.qamOpen]);
  const settled = !fullscreen && !!showcase && snapshot.layoutStable !== false;
  const anticipated = !settled && !fullscreen && snapshot.qamOpen === true ? held.current : null;
  const placement = showcase ?? anticipated;
  React.useLayoutEffect(() => {
    // Only a settled slide may be confirmed; an anticipated one is visual only.
    markPlayhubOnboardingPresented(fullscreen || settled ? view : null);
    return () => markPlayhubOnboardingPresented(null);
  }, [geometry, showcase?.left, showcase?.top, view, locale, snapshot.document, snapshot.layoutStable, settled, fullscreen]);
  if (!view || !snapshot.document?.body) return null;
  const intro = view === "intro";
  const final = view === "outro";
  const actionCopy = getOnboardingActionCopy(locale, final);
  const confirmGlyph = renderPlayhubOnboardingHint("confirm");
  const position = { ...(placement ?? positionOnboardingCoach(null, geometry ?? snapshot,
    { width: blockWidth, height }, snapshot.footerInset)) };
  position.left += geometry?.left ?? 0;
  position.top += geometry?.top ?? 0;
  const visible = fullscreen || settled || !!anticipated;
  // The outgoing slide fades out the moment the step is confirmed.
  const leaving = snapshot.advancing === true;
  // The closing slide is the wordmark itself: no motif competes with it.
  const art = final ? { width: 392, height: 133, scale: 1 }
    : fullscreen ? { width: 392, height: 175, scale: 1.33 } : { width: 470, height: 208, scale: 1.6 };
  const buttonStyle: React.CSSProperties = { pointerEvents: "auto", display: "inline-flex", alignItems: "center", gap: 10,
    minWidth: 0, maxWidth: "100%", whiteSpace: "normal", overflowWrap: "anywhere",
    background: "transparent", border: 0, padding: "10px 0", color: "inherit", font: "inherit", cursor: "pointer", textAlign: "start" };
  return createPortal(<div ref={portal} data-playhub-onboarding-overlay="true" style={{ position: "fixed", inset: 0,
    zIndex: 10000, pointerEvents: fullscreen && !snapshot.introExiting ? "auto" : "none", color: "#fff", fontFamily: "inherit", letterSpacing: 0,
    ...(fullscreen ? { display: "flex", alignItems: "center", justifyContent: "center", boxSizing: "border-box", padding: "32px 24px 96px",
      background: "rgba(0,0,0,0.62)", backdropFilter: "blur(12px)", WebkitBackdropFilter: "blur(12px)",
      opacity: snapshot.introExiting ? 0 : 1, transition: "opacity 200ms ease-out" } : {}) }}
    onPointerDown={fullscreen ? () => { pointerStarted.current = view; } : undefined}
    onClick={fullscreen ? (event) => clickAction(event, "confirm") : undefined}>
    <style>{'[data-playhub-onboarding-overlay="true"] img{height:44px;max-width:64px;object-fit:contain;vertical-align:middle}[data-playhub-onboarding-overlay="true"] .ph-onboarding-wordmark{display:block;width:100%;max-width:min(100%,308px);height:100%;max-height:100%;margin:0 auto;object-fit:contain;animation:phOnboardingMark 420ms ease-out both}@keyframes phOnboardingMark{from{opacity:0;transform:scale(.96)}to{opacity:1;transform:scale(1)}}[data-playhub-onboarding-overlay="true"] button:focus-visible{outline:2px solid rgba(255,255,255,.8);outline-offset:3px}@keyframes phOnboardingTextIn{from{opacity:0;transform:translateY(9px)}to{opacity:1;transform:translateY(0)}}[data-playhub-onboarding-text]{animation:phOnboardingTextIn 190ms ease-out both}@media(prefers-reduced-motion:reduce){[data-playhub-onboarding-text]{animation:none}}'}</style>
    <div ref={box} role="status" aria-live="polite"
      onPointerDown={() => { pointerStarted.current = view; }}
      onClick={(event) => clickAction(event, "confirm")}
      style={{ left: position.left, top: position.top, width: position.width, maxHeight: position.maxHeight,
      visibility: visible ? "visible" : "hidden",
      pointerEvents: fullscreen && snapshot.introExiting ? "none" : "auto", position: "absolute", boxSizing: "border-box",
      opacity: leaving || !visible ? 0 : 1, transition: "opacity 160ms ease-out",
      padding: 0, borderRadius: 0, border: 0, background: "transparent",
      boxShadow: "none", textAlign: "center", textShadow: "0 1px 4px rgba(0,0,0,0.65)", overflowY: "auto",
      overflowWrap: "anywhere", fontSize: 26, lineHeight: 1.45,
      ...(fullscreen ? { position: "relative", left: "auto", top: "auto", width: "100%", maxWidth: 717, maxHeight: "100%",
        padding: 0, border: 0, borderRadius: 0, background: "transparent", backdropFilter: "none", WebkitBackdropFilter: "none",
        boxShadow: "none", textAlign: "center", fontSize: 22 } : {}) }}>
      <div key={view} data-playhub-onboarding-text="true"><div style={{ width: "100%", maxWidth: art.width, height: art.height, margin: fullscreen ? "0 auto 15px" : "0 auto 22px", pointerEvents: "none" }}>
        {final ? <img className="ph-onboarding-wordmark" src={playhubWordmark} alt="" />
          : visible && <OnboardingIllustration view={view} scale={art.scale} height={art.height} />}
      </div><h2 style={{ fontSize: fullscreen ? 32 : 32, fontWeight: 600, margin: fullscreen ? "0 0 11px" : "0 0 16px", lineHeight: 1.2, letterSpacing: 0 }}>
        {tabCopy ? tabCopy.title : intro ? copy.title : view === "outro" ? copy.outroTitle
          : view === "playhub" ? copy.playhubTitle : view === "store" ? copy.storeTitle : copy.deckyTitle}
      </h2>
      <p style={{ margin: 0, whiteSpace: "pre-line" }}>{tabCopy ? tabCopy.body : intro ? copy.open : view === "outro" ? copy.outro
        : view === "playhub" ? copy.playhub : view === "store" ? extra.store : view === "decky" ? copy.decky : copy.deckyOff}</p></div>
      <div style={{ display: "flex", flexWrap: "wrap", justifyContent: "center", gap: "6px 32px", marginTop: fullscreen ? 28 : 22 }}>
        {<button type="button" aria-label={final ? copy.finish : copy.next} style={{ ...buttonStyle, flexWrap: "wrap" }} onClick={(event) => clickAction(event, "confirm")}>
          {confirmGlyph ? <>{actionCopy.before}<HintBoundary key={`confirm-${snapshot.revision}`}>{confirmGlyph}</HintBoundary>{actionCopy.after}</> : final ? copy.finish : copy.next}
        </button>}
      </div>
    </div>
  </div>, snapshot.document.body);
}
