# RGB implementation boundary

Colores by Hooandee inspired this integration. No Colores GPL implementation,
Handheld Companion CC BY-NC-SA implementation, or HHD implementation is included.
This credit does not imply endorsement or Windows hardware verification.

## MSI protocol source

The MSI static packet layout, device ID filters and firmware addresses are
adapted from HueSync by honjow / Steam Deck Homebrew, BSD-3-Clause. The full
license is in LICENSE-HueSync.txt and must travel with the module.

Sources read on 2026-09-08:

- https://github.com/honjow/HueSync/blob/main/py_modules/led/msi_led_device_hid.py
- https://github.com/honjow/HueSync/blob/main/py_modules/devices/msi.py
- https://github.com/honjow/HueSync/blob/main/LICENSE

Exact SHA256 of the upstream MSI protocol file inspected:
`1213e9388408fa26324114cc0de715e6761ca487b77dee050609e15df71d71b3`.
The implementation is limited to a single static nine-zone frame. It does not
include controller mappings, firmware updates, animation or auxiliary commands.
The explicit firmware allowlist uses the revisions named by upstream comments:
0163/0211 use 01FA; 0166/0217/0308 use 024A. Upstream's broad future-version
fallback is intentionally not implemented. The device bcdDevice version comes
from HidD_GetAttributes; it is not an independently verified MCU version.

Review correction: 0163/0166 are names of upstream address tables, while
0211/0217/0308 appear in comments. These are candidate associations, not a
validated hardware allowlist. All MSI writes are now release-gated off with
firmware_write_semantics_unverified. Matching 64-byte output length does not
prove that report ID 0F exists on that collection or accepts this packet.
The 32-byte payload comprises five header fields and nine RGB triples; the
upstream legacy comment "31 bytes" conflicts with its actual length byte 20h.
The implemented encoder follows the actual 32-byte data, not that comment.
The write targets profile 1 at 01FA/024A; no evidence establishes whether this
profile is volatile or persistent. Licensing confidence is separate from device
safety. A successful OS WriteFile would not settle any of these questions.

VID 0DB0, PIDs 1901/1902, pages FFA0/FFF0 and usages 0001/0040 are the upstream
filters. They identify candidate interfaces, not a guarantee of a particular
Claw marketing model. A 64-byte output descriptor is additionally required.
Each caller must explicitly select a reported device ID; no first-device auto-selection.

## Blocked protocols

Legion Go / Go 2 VID 17EF with PIDs 6182-6185 and 61EB-61EE, usage FFA0:0001,
are detection-only. HueSync's legion_go_tablet_hid.py explicitly refers to HHD
implementation/provenance; the inspected HHD repository uses LGPL-2.1 and the
historical license chain was not resolved. No Legion report bytes are included.
The reported blocked_reason is protocol_provenance_unresolved.
Go S and all ROG variants are outside this implementation. Do not enable them
through the MSI sender or bypass the allowlist.

## Native transport

win32.py is an independent ctypes binding to inbox Windows APIs. It does not
vendor HIDAPI, pyhidapi, OEM DLLs or their code. No extra DLL/runtime/driver is
needed beyond the host's Python standard library and Windows system DLLs.

- https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetdeviceinterfacedetailw
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getattributes
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/nf-hidpi-hidp_getcaps
- https://learn.microsoft.com/en-us/windows/win32/fileio/canceling-pending-i-o-operations

Metadata queries open handles with zero desired access, request attributes and
cached preparsed descriptors, and close them. No feature/input/output reports
are sent by detection. Output uses WriteFile only after explicit authorization,
rechecking the descriptor on the opened handle. Windows access denied is not
worked around by installing drivers, taking exclusive ownership or elevating.

No hardware was enumerated or written during development. Offline tests cannot
validate driver behavior, report IDs against actual descriptors, OEM software
conflicts, visible colors, or firmware persistence.

## Follow-up semantic review (gate unchanged)

The blanket persistence uncertainty above is narrowed by additional evidence:
Linux upstream identifies the configuration as MCU RAM, opcode 21h as profile
write and 22h as ROM synchronization. Its RGB writer explicitly follows the RAM
write with ROM sync. Our encoder sends only 21h: it must not be described as a
firmware flash operation. Upstream also selects the RGB address from bcdDevice,
corroborating that association, and waits for device ACKs rather than equating
USB completion with application. This GPL source was read for protocol facts;
none of its implementation was copied.

- https://github.com/torvalds/linux/blob/master/drivers/hid/hid-msi.c
- Inspected SHA256: e3947ffd77a58e936d24b894ddfa4bf55581eb64b61fda0f8a3f925ae311a805

A separate Windows MIT project, ClawConfigurator, documents RAM-only RGB writes
on Claw 8 EX AI+, profile reads (04h/05h), output 0Fh/input 10h and matching
responses by offset/length. Its channel selector byte is 00h, whereas the
HueSync packet here uses 01h. That difference is unresolved for this backend.
Do not transfer its model-specific measurements to every Claw 8 AI firmware.
Its LICENSE-NOTES.md describes independent implementation; no source was copied.

- https://github.com/Stev3FrencH/ClawConfigurator/blob/main/docs/hardware-notes.md
- https://github.com/Stev3FrencH/ClawConfigurator/blob/main/src/Hardware/Windows/MsiLightingProtocol.cs
- https://github.com/Stev3FrencH/ClawConfigurator/blob/main/src/Hardware/Windows/MsiVendorHidChannel.cs
- https://github.com/Stev3FrencH/ClawConfigurator/blob/main/LICENSE
- https://github.com/Stev3FrencH/ClawConfigurator/blob/main/LICENSE-NOTES.md
- Hardware notes SHA256: 41d3247df9b16332dd1839dca90fd2e85f1c36f56394b811e83cc1d361a766aa

The gate primarily protects against unverified per-model addressing/channel
selection and missing ACK/readback/snapshot restoration, not a demonstrated
flash-write hazard. Software work can implement those checks and exercise
captured fixtures. Proving descriptor matching, RAM read/write/restore behavior,
reconnect/suspend behavior and coexistence with MSI software still needs exact
device evidence or controlled hardware testing. ROM sync by another writer is
also outside this backend's control. No UI confirmation overrides the release
gate; no RGB parent integration is warranted yet.

## MIT RAM transaction implementation

msi_ram.py now adapts the documented ClawConfigurator packet fields and lighting
layout, retaining its MIT notice in LICENSE-ClawConfigurator.txt. Its transaction
policy is new: two stable snapshots, a single 32-byte RAM update, full readback,
an in-memory original snapshot and explicit conflict-checked restoration. Bare
ACKs are not treated as proof that a write was applied. The implementation never
encodes 22h (ROM sync), mode changes or resets. This is an offline-tested protocol
component, not a newly enabled Win32 backend path.

Identity evidence found in that repository:

- System product: Claw 8 EX AI+ CG3EM
- Baseboard: MS-1T91
- BIOS: E1T91IMS.10A, dated 2026-07-28
- EC firmware: 1T91EMS1.10
- Observed vendor channel: VID 0DB0, PID 1901, MI_01, usage FFA0:0001
- Reports: output 0Fh and input 10h, 64 bytes
- Controller USB bcdDevice / controller MCU version: NOT FOUND in the inspected
  docs, source and Diagnostics text, including nested diagnostic ZIP transcripts.

The BIOS and EC versions identify different firmware from the USB controller.
They must not be substituted for its missing revision. ClawConfigurator's own
gate uses broad model markers; its HID channel also caches a path and has a
fallback length of 64. Those permissive checks are not copied. An exact
model/controller-firmware allowlist cannot be filled from the available evidence
without inventing the missing version. No such identity is currently enabled.

The transport now also has a gate-protected duplex Win32 channel using overlapped
ReadFile/WriteFile with cancellation and retained buffer lifetime. Its sender
accepts only canonical bounded RAM read/static-write packets; ROM sync is rejected.
HidP_GetButtonCaps/HidP_GetValueCaps now supply the actual report IDs from cached
preparsed descriptors; the duplex path requires exactly input 10h/output 0Fh
and 64-byte lengths. No report query is sent to discover these IDs.
The new component still stays disconnected from RgbBackend. Enabling it requires
a documented exact model/controller-firmware identity and its runtime enforcement. These are
software/protocol prerequisites, not a demand that this
developer personally own every supported device. Reproducible upstream capture
and version evidence can close the identity gap. Visible behavior and independent
writer interference remain separate deployment uncertainties.

Windows descriptor ABI references (independent ctypes bindings):

- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_value_caps
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_button_caps
