# Design Acceptance Review

Date: 2026-09-08. Independent QA; parent is the sole coordinator and live deploy owner.
The later explicit Store-gap assignment authorizes only PluginStoreIntro.tsx and
tests/pluginStoreLayout.test.mjs implementation changes. No other component was edited.
Artwork and its icon WIP backup were not touched.

## Actionable Findings

### P2: Mode button names differ from the explicit user requirement

Observed in live QAM text: `Modalita\nGaming` and `Modalita\nDesktop` (with the
Italian accent in the actual DOM). The user explicitly requested `Gaming Mode`
and `Desktop Mode`, each on two lines. Source: Source/GamingModeDeckyPlugin/src/index.tsx:100-101;
ModeButtonLabel at :26-31 splits the localized string at its first space.
The two-line structure is present, but the requested product names are not.
Action for parent: retain the two-row layout and use the explicitly requested
names on these two buttons. Do not change unrelated localization strings.

### P2: Editor text paints under the fixed Playhub logo in a scrolled state

Observed screenshot: C:/Users/Andrea/AppData/Local/Temp/playhub-design-qa-20260908-home.png.
Despite the filename, the user switched to the editor before capture. The sentence
`Scegli quali tab mostrare e in quale ordine.` is visibly behind the wordmark.
The following DOM sample measured the wordmark at x80/y25.99/w144/h36 and
.ph-controls at x48/y-23.77/w300/h795.66 in a 855x682 QAM document.
This is an observed occlusion, not proof of which ancestor caused the scroll.
Source inspection entry: Source/GamingModeDeckyPlugin/src/ControlCenter.tsx:215-217
(editor ScrollPanel and heading), :58-60 (editor spacing); index.tsx:755 (wordmark wrapper).
Action for parent: reproduce that scroll/focus state and constrain the actual
editor scrollport below the header. Do not compensate by moving the logo or
adding an arbitrary negative top margin. Acceptance: text cannot paint inside
the wordmark rectangle after entering, focusing reset, or scrolling the editor.

### P2: Store clipping gap was measured and corrected in source

Live pre-fix DOM: actual button top180.276/height42.143/bottom222.420;
launcher bottom232.416. Animation outer top201.348 plus clip-path inset31.0685
placed the effective clipping edge at232.416: a 9.997px gap below the button.
Screenshot: C:/Users/Andrea/AppData/Local/Temp/playhub-design-qa-20260908-store.png.
The current-source variant initially had the same edge at the launcher bottom,
although it used an inner viewport instead of the live build's clip-path.

Authorized fix in Source/GamingModeDeckyPlugin/src/PluginStoreIntro.tsx:47-49,63-64:
measure wrapperGap=launcher.bottom-button.bottom, recover only that gap with the
outer margin, and keep the inner origin at the button midpoint. The outer uses
overflow:hidden and starts at actual button.bottom, so no animation paints over
the button. Width, scale, shelf, keyframes and description spacing are unchanged.

Independent Chromium DOM geometry: 27 combinations of width174/268/480,
buttonHeight32/42/80 and wrapperPadding0/10/24 passed. The real component's CSS,
refs, measurement callback and DOM layout ran in an inert hook host; Steam and
native input did not run. Example width268/button42: button.bottom=92 and
clip.top=92 for all padding values; inner.top=71 (midpoint), clip.bottom=201,
copy.top=207. Screenshots in C:/Users/Andrea/AppData/Local/Temp/playhub-store-edge-qa/.
Kuhn separately reported 48 mocked combinations, including padding8 and height400.
Status: source ready for parent deploy; post-deploy live geometry not certified.
The parent retains responsibility for prior implementation/delivery and new deployment.

## Acceptance Matrix

| Requirement | Evidence | Status / Remaining Check |
| --- | --- | --- |
| Stable main tab dimensions, no icon micromovement | ControlCenter.tsx:37-46 uses fixed-height buttons, grid tracks, constant borders, transform:none. Two idle live Store samples had identical rectangles: six tabs about36.5x34, settings32x34. | Stable at idle only. No A/focus/hover/bumper transition was triggered; active-transition acceptance remains parent-owned. |
| Horizontal editor; A visibility, X reorder | ControlCenter.tsx:218-236 and controlTabEditorState.ts implement native action descriptions, confirm visibility and secondary move; 11 existing tests pass. | Source/mocked semantics pass. Native footer glyph and controller dispatch not exercised. |
| Concise explanation and reset padding | controlTabEditorLocale.ts has a one-sentence introduction and scoped reset explanation. ControlCenter.tsx:58-73 gives reset12px padding/min-height52 and32px before subsequent settings. Editor screenshot shows reset text inside the button. | Reset containment observed; header occlusion remains open above. |
| Full-width dropdowns | quickSettings/index.tsx:218 uses below layout when fullWidth; audio output/microphone/resolution/refresh explicitly set it at :967-974. | Source evidence only; those controls were not open in the captured live state. Other dropdowns default inline, so a requirement for every dropdown would need a broader explicit scope. |
| No stray dividers | ControlCenter.tsx:49-53 suppresses native separators; 4 focused AST tests pass. Store/editor screenshots show no stray internal rule. | Captured views pass; audio/graphics not visually certified. |
| Two mode buttons, two lines | index.tsx:666-677 sets two equal columns,96px height and two label rows; live text contains line breaks. | Requested names fail as above; layout geometry of these buttons was not captured. |
| Mode icon glyphs render | index.tsx:668,673,677 uses PUA E7FC/E765/E962 with Segoe Fluent Icons/Segoe MDL2 Assets. | Risk only: PUA characters in DOM do not establish missing glyphs. No mode-button screenshot was obtained. Parent should inspect pixels and actual font availability before declaring a failure or pass. |
| Logo alignment | Live wordmark x80; content button x64, so16px additional inset exists. index.tsx:755 explicitly applies16px. | Measurements confirmed, no invented alternative alignment target. Separate editor overlap finding remains. |
| Store animation below button, truly clipped | Native effective clip edge and button edge measured; corrected source and Chromium matrix above. | Ready in source, parent live recheck required. |
| Dashboard five complete processes | DashboardPage.tsx:659-661 fixes viewport to five row heights plus gaps/padding; existing browser test passed at1280x720,1920x1080,1024x600, initially and after focus scrolling. | Offline DOM pass; no live Dashboard mounted. Intermediate heights not tested and not certified. |
| App picker complete rows and footer clearance | DashboardPage.tsx:571-574,1250-1282; existing Chromium test passed first/last-row focus and footer64px mock at the same3 viewports. | Offline pass; real Steam footer geometry remains unverified. |
| Onboarding fullscreen intro then glass tooltips left of QAM | PlayhubOnboarding.tsx:78-105; onboardingAnchors.ts:67-86 places outside QAM and fails closed without space. Current delivery status supersedes older tracker descriptions. | Source only. Main live document had zero onboarding overlays; no replay, input or lifecycle changes were made. |
| Decky tab access | Latest captured main navigation had no Decky tab; editor screenshot had its plug icon. | Known parent/Pascal work, not independently diagnosed or modified here. |

## Verification And Provenance

- Fresh http://127.0.0.1:8080/json/list used, not stale target assumptions.
- QAM target F0C1514DEB3F8CAD4B99B169061D73D6: actual document855x682.
- Main target493928751A441069AD35DCF7A345F940: actual document1353x761;
  zero Dashboard/onboarding instances during inspection.
- Shared target4F4BE14829EFC9C432766F48FF4B81BB still existed in current inventory.
- Read-only Runtime.evaluate and Page.captureScreenshot only; user changed views
  independently between captures. No native input, focus changes, reload or deploy.
- Store live CSS contained negative outer margin/clip-path while current source
  contained inner .ph-store-download-viewport. Source and live are not treated
  as the same build.
- Source checks: controlTabEditor + qamFieldSeparators15 tests passed.
- Store suite15/15 passed, including optional Chromium27-case geometry test.
- Dashboard browser suite3/3 passed with its existing3-viewport matrix.
- TypeScript --noEmit passed. No build/installer/commit/push was performed.
- Browser QA used bundled Playwright via NODE_PATH and existing
  C:/Program Files/Google/Chrome/Application/chrome.exe in headless isolated sessions.
  The first attempt without explicit executable failed at browser launch because
  the Playwright headless-shell binary was absent; this was infrastructure, not
  a measured product failure. Nothing was installed.

Parent retest priority: Store actual button/clip edges after deploy; editor header
clipping; exact mode names and rendered glyphs; native A/X footer/focus; actual
Dashboard footer/process geometry. Do not promote offline passes to live acceptance.
