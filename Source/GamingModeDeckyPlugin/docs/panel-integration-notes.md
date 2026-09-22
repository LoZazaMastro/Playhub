# Panel integration sidecar

## Provenance and scope

Reviewed 2026-09-08, read-only: `C:\Users\Andrea\Desktop\Panel.de.Control.zip`.
SHA256: `35E60E5C24E14EBA5385C4A67DF8B2A00DD4B4BD2C2F23BD561E8B23546B907F`.
Archive `Panel de Control/package.json` identifies Hooandee, version 0.42.0,
and GPL-3.0-only; `LICENSE` contains GPL version 3. The frontend available in
this archive is `dist/index.js`, not the original TSX source or frontend tests.
No upstream code, assets, translations, stylesheet or runtime was copied into
this sidecar. The helper is an original implementation against Playhub's needs;
this is not a clean-room claim, since the upstream bundle was inspected.

Do not treat upstream as MIT because Playhub's package declares MIT. Before any
future code adaptation/distribution, review GPL sections 4-6: preserve notices
and license, identify modifications/dates, address licensing of the combined
work and provide the required Corresponding Source (including build material).
The release bundle alone is not evidence that these source obligations are met.
Obtain the preferred source and resolve distribution compatibility with the owner
before a literal port; attribution alone is not a substitute. No upstream files
are being redistributed by these three new files.

## Source observations

Line references below refer to the archived `Panel de Control/dist/index.js`:

- `TabBar`, line 4322: active-tab/strip references and reconciliation for an
  overflowing tab strip. Playhub already has controller-native tab selection and
  a transactional tab editor in `ControlCenter.tsx` / `controlTabEditorState.ts`.
- `ContainedSlider`, lines 7295-7300: native Decky SliderField with containment
  and uniform scaling for intrinsic width. Playhub already uses native
  SliderField in `quickSettings/index.tsx:181`; do not introduce a DOM range
  input, custom gamepad polling, or copy the upstream scaled wrapper.
- `Collapsible`, lines 7303 onward: persistent per-ID collapse and a compact
  summary. Playhub's `ControlSection.tsx` already provides a native DialogButton,
  aria-expanded, chevrons and conditional Focusable content, with parent-owned
  collapse preferences. A second collapse store/component is unnecessary.
- Color preview lifecycle, lines 4996-5025, 5102 onward, 5152-5178: pending
  preview/request state, epochs for context changes, discard and countdown.
  This is color preview evidence, not proof of Windows resolution/HDR support.
  Playhub already has the stronger monotonic deadline in `HdrConfirmModal`
  (`quickSettings/index.tsx:256`) and backend-token reconciliation in
  `changeDisplay` / `reconcileDisplay`, covered by `displayRecovery.test.mjs`.
  Keep those mechanisms; the new helper MUST NOT drive display transactions.

Also inspected Artwork's `src/utils/showRestartConfirm.tsx`: it uses Decky
showModal/ConfirmModal, consistent with native modal ownership. Steam's install
path was read from HKCU Valve/Steam and its steamui directory inspected. No Steam
code was modified or executed and no native UI behavior was claimed verified.
The user-owned `F:\Playhub\Plugin\Quick Settings` was not modified.

## Missing reusable mechanism

Playhub's generic `useDebounced` at `quickSettings/index.tsx:248` only resets a
timer: it does not dispose pending writes, serialize slow operations, or reject
superseded results. The AMD queue in that same file addresses a related problem
but is private and tied to AMD feature snapshots. It is not a general export.

`src/panelIntegration/settledWriter.ts` supplies a per-resource, latest-value
settling writer: one in-flight operation, trailing coalescing, stale readback
suppression, cancellation, disposal and an injectable scheduler. It adds no UI,
backend imports, hardware ranges or capability assumptions. It intentionally
does not replace existing audio serialization, AMD queues or CPU ownership.

## Exact parent integration hook

Import `createSettledWriter` from `./panelIntegration/settledWriter` (or
`../panelIntegration/settledWriter` from quickSettings). Create one instance in
an effect per resource identity, retain it in a ref, and dispose that exact
instance in effect cleanup. On identity change, dispose/recreate rather than
retargeting an in-flight write. Native SliderField/QuickSlider `onChange` should
update the local draft immediately and call `writerRef.current?.enqueue(value)`.

Supply `write` as an async adapter returning authoritative backend readback;
convert application-level `{ ok: false }` results into errors in that adapter.
`onSettled` installs the readback, `onError` reports/refetches it. Callbacks must
not throw. Avoid stale closures by creating the writer with the owning effect
and stable callbacks/ref-backed state. Independent polling still needs the
owner's existing generation guard; this helper does not arbitrate poll results.

`cancelPending()` invalidates current callbacks and unsent input but keeps the
writer usable. `dispose()` permanently rejects input. Neither aborts nor undoes
an already-started RPC; backend operations need their own safety guarantees.
Do not issue an inverse write on disposal. No automatic retry is attempted.

No shared integration hook was edited: adoption is explicitly left to the parent.

## Deterministic verification

Run `node --test tests/panelIntegrationSettledWriter.test.mjs` and
`node node_modules/typescript/bin/tsc --noEmit` from the plugin repository.
Tests execute the actual transpiled helper with a fake scheduler and deferred
promises, without network, backend, Steam, installation or hardware calls.
They cover repeated input settling, slow-write ordering, stale success/failure,
recovery after rejection, queued timer races, resource changes, unmount before
dispatch and unmount during dispatch. These are regression contracts for parent
adoption, not claims that existing shared callers were fixed or hardware tested.
