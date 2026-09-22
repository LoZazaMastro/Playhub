# Windows CPU power: integration handoff

Implemented diagnostic transport and transaction controller, **not an operational
7900X PPT setter yet**. No driver install, service start, HC constructor, native
SMU query or hardware write was executed. Tests use synthetic devices only.

## Implemented

- `capability.py`: passive CPU/service registry inventory plus `FAMILY_TABLE`,
  a **family -> allowed interval** table that replaced the single-CPU allowlist.
  Each row declares vendor/family/model range, an outer conservative clamp
  (min/max/step in watts), the bridge it would need (AMD SMU mailbox, Intel MSR
  RAPL) and a `verified` flag. Only `raphael_or_dragon_range` (19h, models
  60h-6Fh) is `verified`; every other row refuses with
  `cpu_family_backend_unverified`, and anything outside the table refuses with
  `cpu_not_allowlisted` while reporting the vendor/family/model it saw. The
  clamp is an outer bound, never a substitute for the limits read from hardware:
  the tighter of the two always wins. The exact 7900X name additionally reports
  AMD's nominal 170 W TDP, not measured power, active PPT or an adjustable range.
- `recovery.py`: `PptRecoveryJournal`, the crash-recovery journal for PPT. Same
  model as the display journal (`display-recovery.json`): atomic write (temp
  file, fsync, `os.replace`), validated schema, and `cpu-ppt-recovery.json` next
  to the plugin settings. The baseline is written **before** the hardware is
  touched; a record that cannot be written aborts the apply.
- `pawnio.py`: original Windows x64 client of the published device IOCTL ABI.
  Constructors do nothing; explicit `open(authorized=True)` requires reviewed
  driver/module SHA-256 pins and exact driver version. It checks Authenticode,
  running service and matching driver image path. It never installs/starts a
  service. Official Edition checks the signed module during load. No third-party
  DLL is loaded. The cross-process `Global\Access_PCI` mutex covers initialization
  and diagnostic queries. Raphael is package-aware; Dragon Range is rejected.
  Only codename, SMU version and PM table resolution are exposed. No arbitrary SMU
  command or tuning setter. Exact output lengths are checked. PM resolution uses
  mailbox traffic internally: semantically read-only, not passive/no-write bus IO.
- `transaction.py` (+ family clamp, journal and `recover()`): fresh independent readback and documented device/firmware
  min/max/step required. Exact milliwatts, no clamping; reduction-only apply,
  second preflight, baseline capture, exclusive lock over the whole transaction,
  independent confirmation, conditional restore, no blind retry. Unknown state
  is quarantined for manual recovery. `apply()` now also refuses outside the
  family interval (`outside_family_table_limits`) and on an unverified family,
  and writes the recovery journal around the transaction. `recover(authorized=True)`
  replays a journal left by a session that ended badly: it re-applies the baseline
  and reads it back, and keeps the record when it cannot confirm, so the next
  start retries. Profile persistence/replay is still out of scope.
- CPU-only `quick_settings/main.py`: legacy mobile `set_tdp` always rejects, even
  if old DLLs are added. Null unknowns, typed reasons and no handheld range.
  New `set_cpu_ppt`/`restore_cpu_ppt` require literal `confirmed: true`. The default
  controller has **no provider**. Provider capability uses `supported_read` and
  `supported_write`; legacy `available` stays false to keep the mobile slider off.

## Parent integration and blockers

1. Audit official signed driver/module releases and fill `AuditedArtifacts` from
   trusted deployment metadata, never RPC input or environment. No binaries or
   artifact pins are shipped by this change. Do not use Unrestricted Edition.
   An arbitrary signed driver is not sufficient: the hash must identify the
   reviewed official build. No installed artifact was verified live here.
2. Native diagnostic lifecycle: construct `RaphaelDiagnostic(artifacts)`, explicitly
   call `open(authorized=True)`, call `snapshot()`, always `close()` in `finally`.
   Do not add this to regular UI polling. Mutex wait is bounded at 2 seconds and
   signature inspection at 15 seconds; synchronous driver IOCTLs are **not bounded
   or cancellable in userspace**. Review isolation before UI-worker integration.
3. Establish independent version-specific active-PPT field semantics. RyzenSMU
   provides PM transport, not a decoded active-limit getter. Unknown versions must
   not use generic tables/guessed offsets. `diagnostic_read_supported` does not
   mean numeric PPT read support. No decoder is claimed in this delivery.
4. Establish documented 7900X/firmware-specific adjustable bounds and setter units.
   AMD's 170 W TDP does not establish PPT min/max. Community RSMU/MP1 command numbers
   alone do not establish vendor-supported safe limits. No HC/ZenStates command
   tables or 4-40/4-60 W default were incorporated. Test bounds are synthetic.
5. Implement the reviewed native PPT provider with `exclusive()`, `snapshot()` and
   `set_ppt_mw(int) -> bool`. `snapshot()` returns `transaction.Snapshot` with a fresh
   monotonic timestamp, real active limit, documented bounds and provenance.
   It must not use measured package power, nominal TDP, fused maximum or setter
   echoes as active-limit readback. A source string is not external proof. Provider
   constructors/reads must not tune; the hardware lock must cover the entire
   transaction, without recursive acquisition by its snapshot/setter methods.
6. Before shipping writes: durable journal/recovery, timeout isolation and lifecycle
   handling. The current controller owns one in-memory transaction; it does not
   restore automatically on unload, crash or reboot. No startup/profile replay.
   Parent owns authorized live diagnostic and reduction/verify/restore acceptance.
7. `build-plugin.bat` now includes `cpu_power` in the freshness scan and copies
   only its `.py` sources and README, never native drivers/binaries or bytecode.
   The build/installer has not been run by this task. Parent owns final packaging
   and UI acceptance. This source-only package does not enable live TDP control.

## Independent sources and licenses

New Python is original Playhub code under the repository license. No third-party
code or binary is redistributed. HC CC BY-NC-SA 4.0 was knowledge only; no tables,
wrappers or assets were incorporated. No license workaround by helper processes.
Sources inspected 2026-09-08:

- [PawnIO device ABI](https://github.com/namazso/PawnIO/blob/9d52965895588b0ffa3703b72eec75ba19f4ccc0/PawnIO/include/pawnio_um.h),
  commit `9d52965895588b0ffa3703b72eec75ba19f4ccc0`: GPL-2.0-or-later plus the
  independent device-IOCTL client exception. This code uses device IOCTL, not a
  linked driver library. Review full exception/source obligations before release.
- [Official PawnIO distribution](https://pawnio.eu/): reviewed Official Edition only.
- [RyzenSMU](https://github.com/namazso/PawnIO.Modules/blob/52a7e536dff3e53c96917a28caac5e0fa6510696/RyzenSMU.p),
  commit `52a7e536dff3e53c96917a28caac5e0fa6510696`: LGPL-2.1-or-later. API reference
  for identity/metadata. Module redistribution has separate source/notices duties.
- PawnIOLib at the inspected PawnIO commit is LGPL-2.1-or-later; this corrects the
  earlier unresolved note about that library. It is not linked or bundled here.
- [ZenStates-Core](https://github.com/irusanov/ZenStates-Core), GPL-3.0: independent
  evidence for distinct Raphael RSMU/MP1 paths, not incorporated.
- [AMD 7900X specification](https://www.amd.com/en/products/processors/desktops/ryzen/7000-series/amd-ryzen-9-7900x.html):
  nominal product rating only, not inferred PPT safety bounds.

No WinRing0/inpout, Defender exclusions, bypasses or GPU tuning.

## Verification

`python -m unittest discover -s tests -p 'test_cpu_power*.py'`: **42 tests pass**.
Coverage: passive identity, nominal/observed distinction, hash/authorization/ABI,
package-aware identity, protocol sizes, null values, disabled legacy RPC, fresh
readback, numerical bounds/step, exclusive transactions, concurrent changes,
apply/restore confirmation, rollback and uncertain-state quarantine.
All hardware objects are fakes. Native Windows/Authenticode acceptance is untested.
