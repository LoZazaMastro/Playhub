# Experimental Windows RGB backend

CURRENT RELEASE: DETECTION ONLY. Both MSI and Legion expose false write
capabilities. MSI is blocked by firmware_write_semantics_unverified, Legion by
protocol_provenance_unresolved. Explicit user confirmation cannot bypass these
gates. The write implementation below is retained for offline testing and future
review, not offered as a currently enabled feature.

Parent integration (not performed here):

```python
from rgb import RgbBackend

rgb = RgbBackend(config_path=plugin_data_dir / "rgb.json")
status = rgb.get_status()  # metadata only; never changes LEDs
# Only inside an explicit, informed user action, using an ID from status:
result = rgb.set_color(device_id, [40, 100, 200], brightness=50,
                       power=True, user_requested=True)
rgb.close()  # no implicit off or restore
```

All methods are synchronous. An async Decky caller should use asyncio.to_thread
for these methods. Keep one instance per plugin process. Calls are serialized
through a worker and a gate, including status and close. Do not use set_color
from a startup handler, device discovery, polling or automatic profile replay.
user_requested is a parent-enforced contract, not an authentication mechanism.

## Parent API contract

`get_status()` returns the following shape (numeric IDs are integers):

```json
{
  "ok": true,
  "devices": [{
    "id": "SHA256 of the case-normalized device path",
    "family": "msi_claw",
    "vid": 3504,
    "pid": 6401,
    "usage_page": 65440,
    "usage": 1,
    "release": 355,
    "output_length": 64,
    "blocked_reason": "firmware_write_semantics_unverified",
    "hardware_tested": false,
    "requires_explicit_request": true,
    "capabilities": {
      "static_color": false, "brightness": false, "off": false,
      "readback": false, "rollback": false, "per_zone": false, "effects": false
    }
  }],
  "last_request": null,
  "hardware_tested": false,
  "automatic_replay": false
}
```

The example is illustrative, not a detected device. Empty devices means no
accessible matching descriptor. Enumeration failure returns ok=false, error,
devices=[] and hardware_tested=false. After close, calls return only ok=false
and error=closed. Treat omitted devices on errors as unavailable, never as a
reason to create example controls. Blocked candidates have all write
capabilities false and a non-null blocked_reason. Do not infer marketing model
or Windows verification from family or USB IDs.

`set_color(id, [r,g,b], brightness=100, power=True, user_requested=True)`
returns ok=true, hardware_state=sent_not_verified, hardware_tested=false,
rollback_available=false and persisted=true/false after a complete OS write.
A disk error adds persistence_error while ok remains true. A write error
returns ok=false, error, hardware_state=unknown, retried=false and
rollback_available=false. Validation/detection failures return ok=false/error
without hardware_state because no write was attempted.

Do not show last_request as the actual LED state; it is an in-memory record of
this instance's last attempt. It may contain only device_id/state following a
write error, and is null on a fresh instance. Parent controls should represent
an editable request, applying only on explicit user input. Never send color
changes from render/effect initialization or from a status refresh.

The gated encoder is limited to candidate MSI descriptors/firmware for static
color, brightness and off. Off is a black frame with brightness zero, not a hardware power
switch. All nine zones receive one color. Legion Go/Go 2 are detection-only with
an explicit provenance blocker. There is no claim of Claw 8 AI Windows testing.

Successful writes return hardware_state=sent_not_verified. The candidate command
writes an RGB RAM profile, without the separate ROM-sync opcode. There is no
original-state readback or rollback. The UI must explain this before opt-in.
Write errors report an unknown hardware state and are never retried. No force
control, OEM service termination, HidHide, controller reset or automatic recovery.

WriteFile waits one second before cancellation. Cancellation must be drained
before releasing its memory; a broken driver can delay that drain. This is not
a hard real-time bound. close waits for outstanding work rather than freeing
in-flight native buffers. For process-level fault isolation, the parent can host
this module in a dedicated helper, but that is not implemented here.

The optional JSON file records only the last completely sent explicit request,
using a unique temporary file, flush/fsync and os.replace. It is not read or
replayed automatically and is not evidence of hardware state. Disk failure after
a successful write is reported separately as persistence_error. Use a private
plugin data directory, not a user-provided arbitrary path. One instance must own
that path; multiple processes are not coordinated.

Do not change the internal MSI release gate based only on consent or USB IDs.
Before enabling a device, verify the output report ID/size against that exact
collection's descriptor, establish the relation between bcdDevice and MCU layout,
and establish a safe readback/restore strategy. Sources now distinguish RAM
writes from ROM synchronization; see the follow-up in PROVENANCE.md. Exact model
validation, selector 00h versus 01h and interference by other writers remain
unresolved. No claim that our encoder flashes firmware is made.

Setup must include this entire rgb directory, LICENSE-HueSync.txt and
LICENSE-ClawConfigurator.txt and PROVENANCE.md. No additional native dependency is bundled. Parent UI, main.py,
build and installer packaging were not modified.

## Offline RAM transaction component

msi_ram.RamTransaction is not connected to the production API and does not load
Windows APIs. It accepts a test/validated duplex channel, reads the 883-byte light
block using correlated 04h/05h packets, and changes only the first 32 bytes through
21h. It validates the known layout, preserves inactive frames and reserved bytes,
refuses audio-rhythm mode, and compares the entire block after writing. Generic
06h ACKs are ignored because they cannot identify the acknowledged request.

apply_static and restore both require user_requested=True. Restoration uses the
original in-memory snapshot only if the current block still equals our expected
state; an external change is a conflict, not something to overwrite. Multiple
applies retain the first original snapshot. No automatic rollback or retry occurs
after ambiguous writes. A read timeout taints the channel to prevent late replies
from satisfying a later request; the session must then be reopened. Snapshots are
not persisted across process restarts, and there is no crash-recovery guarantee.
Comparison cannot make separate HID writes atomic against an unrelated OEM writer.

Protocol tests use modeled fixtures and a fake duplex channel, not captured
device traffic. They establish packet construction and transaction logic, not
the missing controller-firmware identity. See PROVENANCE.md for that precise gap.

The Win32 transport also contains open_channel(..., user_requested=True), still
blocked by the release gate. It requires 64-byte input/output descriptors, opens
a shared read/write handle, requires actual descriptor report IDs 10h/0Fh,
rechecks identity and returns a context-managed
duplex channel with a unique session identity. Its sender rejects all commands
except bounded profile reads and the 32-byte static RAM update. Close that channel
explicitly or through its context manager. Report IDs are obtained through
HidP_GetButtonCaps/HidP_GetValueCaps, without sending discovery reports. The exact
model/controller-firmware allowlist is not populated or enforced yet because the
source lacks that version evidence; it must not be replaced with the broad legacy
USB filter when enabling this path. get_status additionally returns input_length,
input_report_ids and output_report_ids for detected candidates.

Run only the isolated offline tests from the plugin directory:

```text
python -B -m unittest discover -s tests -p "test_rgb*.py" -v
```
