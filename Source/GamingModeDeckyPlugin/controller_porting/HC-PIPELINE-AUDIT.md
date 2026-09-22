# HC pipeline and device-family porting audit

2026-09-08. Source inspection only. No HC code/assets/binaries copied, no native
library loaded, driver installed, input sampled, output created or sensor enabled.
This is a porting map, not a supported-hardware announcement.

HC reference root:
`C:/Users/Andrea/AppData/Local/Temp/hc-study-80c450afcc76456bb02ef75eedba733a/HandheldCompanion-main`.
Paths in the next two sections are relative to its HandheldCompanion directory.
Read alongside the supplied sibling HC-STUDIO.md; its old TDP and UI status is
historical, not the current Playhub implementation.

## Complete HC pipeline

| Stage | Source evidence | Actual responsibility |
| --- | --- | --- |
| Device family | Devices/IDevice.cs:483 onward; fallback :933 | Manufacturer/product/board/CPU dispatch to OEM class; not a universal feature guarantee |
| Generic SDL discovery | Managers/ControllerManager.cs:447 SDL_GamepadAdded | SDL recognition, physical path -> PnP/container identity, deduplication |
| Generic SDL fallback | ControllerManager.cs:520; Controllers/SDL/Xbox360Controller.cs:5 | Unknown/Standard SDL types use a subclass of SDLController; its name does NOT mean virtual Xbox output |
| XInput alternative | ControllerManager.cs:974; Controllers/XInputController.cs:75,86 | OEM factory or generic XInputController; Tick -> UpdateXInputState, independent of Valve PID switch |
| DirectInput | Controllers/DInputController.cs:46 | SharpDX enumeration + GenericController HID helper; class existence is not evidence of blanket manager fallback |
| Standard and motion reader | Controllers/SDLController.cs:55,262,375,389,405 | Per-device buttons/axes/touch/sensor capabilities, input collection, motion processing |
| Sensor selection | Managers/SensorsManager.cs:331; ControllerManager.cs:329 | Controller IMU, Windows sensors or external serial IMU, not necessarily the selected gamepad's sensor |
| Physical frame loop | ControllerManager.cs:301 | Selected controller Tick, raw InputsUpdated event, motion, layout, then output |
| Motion calibration/fusion | Helpers/GamepadMotion.cs:53,91,120 | Native GamepadMotion.dll lifecycle and ProcessMotion; calibration, gravity/orientation; wrapper itself is HC code |
| Motion transforms | Managers/MotionManager.cs:80,196,265 | Calibrated/default/DSU channels; local/player/world space and steering; activation/sensitivity/filtering |
| Per-game selection | Managers/ProfileManager.cs:107,339,531 | Foreground event -> profile selection -> ApplyProfile -> Applied event |
| Layout activation | Managers/LayoutManager.cs:238 | Applied profile chooses current layout, not just a saved JSON document |
| Mapping/deadzones | LayoutManager.cs:548; Actions/AxisActions.cs:85 | Button/axis/trigger/keyboard/mouse actions, radial and anti-deadzones |
| UMC gyro output | LayoutManager.cs:758; Actions/MouseActions.cs:255 | Gyro -> joystick blending, mouse motion or touchpad action, not only XInput passthrough |
| Target selection | Managers/VirtualManager.cs:246,472,484,492,608 | Profile-driven DS4/original Steam Controller/Xbox360 target, then UpdateInputs |
| System output | Targets/VIIPERTarget.cs:78,123,211 | Connect bus/device, BuildReport -> SendInput -> ViiperService.SetInput, feedback and disconnect lifecycle |
| Xbox/DS4 encoding | Targets/Xbox360Target.cs; Targets/DualShock4Target.cs | Both use VIIPERTarget in THIS archive, not ViGEm |
| UDP motion | DSU/DSUServer.cs:217,439,507,513,649 | Client requests, UDP server, report encoding/CRC, controller/motion snapshots and send loop |
| InputsManager | Managers/InputsManager.cs:660,748 | Consumer of InputsUpdated for input/hotkey behavior; not the physical reader replacing ControllerManager |

ControllerManager:162 disables SDL XInput and :165-166 disables Steam/SteamDeck
HIDAPI paths to avoid overlap with dedicated readers. Therefore generic code
exists, but its effective build/hints/PnP route must be tested for 28DE:1304.
Absence from Gordon/Neptune PID cases alone does NOT prove unsupported input.
Conversely, receiver enumeration does not prove motion or touchpad support.
SDLController's constructor enables sensors: it is not a read-only probe.

## Families: evidence is per model and feature

For every row below: HC source evidence is present as stated; no corresponding
HC OEM/sensor implementation was ported by this task; no listed handheld or IMU
was detected or hardware-tested by this task. Existing generic Playhub SDL mock
tests are NOT family support tests. The previously observed desktop 7900X and
Valve receiver do not establish any handheld row.

| Family requested | HC source-supported evidence | Playhub ported | Runtime detected / tested |
| --- | --- | --- | --- |
| ASUS Ally / Ally X | IDevice.cs:827, RC71L/RC72LA; Devices/ASUS classes | No OEM port | Not assessed / no |
| ASUS Z13 | README.md:37; ASUS fallback IDevice.cs:844 -> AsusDevice | No dedicated port; exact Z13 feature path needs further audit | Not assessed / no |
| Lenovo Go / Go S | IDevice.cs:862; 83E1,83L3,83N6,83Q2,83Q3 and Go2 variants | No OEM port | Not assessed / no |
| MSI Claw gen1 / AI+ / A8 | IDevice.cs:885; MS-1T41,1T42,1T52,1T8K -> dedicated classes | No OEM port | Not assessed / no |
| AOKZOE | IDevice.cs:534; A1/A1Pro/A1X/A2Pro dispatch | No OEM port | Not assessed / no |
| Steam Deck | IDevice.cs:850; Jupiter/Galileo; Controllers/Steam/NeptuneController.cs | No OEM/Neptune port | Not assessed / no |
| AYANEO | IDevice.cs:555; AIR/NEXT/2/2S/KUN/SLIDE/FLIP variants | No OEM port | Not assessed / no |
| ONEXPLAYER | IDevice.cs:725; X1/X2/G1/F1/Mini/2 variants | No OEM port | Not assessed / no |
| GPD | Devices/GPD; IDevice.cs CPU/model-specific Win/Max/Mini dispatch | No OEM port | Not assessed / no |
| AYN | IDevice.cs:509; Loki Zero/MiniPro/Max CPU variants | No OEM port | Not assessed / no |
| Zotac | IDevice.cs:906; G0A1W -> GamingZone | No OEM port | Not assessed / no |
| Bosch BMI160 | README.md:64; IMUGyrometer/IMUAccelerometer Windows.Devices.Sensors route | No Bosch register-level port found/added; Windows driver exposure required | Not assessed / no |
| GY-USB002 | README.md:65; Sensors/SerialUSBIMU.cs + SerialPortEx.cs | No serial parser port | Not assessed / no |

Standard SDL/XInput controls, Windows IMU, serial IMU and OEM HID/EC functions
must remain independent capability axes. Use physical source/container identity,
sensor identity, transport, model/firmware evidence and per-feature reasons.
Missing source != unsupported; recognized model != supported write operation.
Do not import OEM logos, glyphs, images or HID/EC tables under a generic notice.

SerialUSBIMU selects 1A86:7523 and 115200 baud. This common USB-serial bridge ID
alone cannot authenticate a GY-USB002. Opening/configuring/calibrating the port
must be explicit, with actual packet validation and disconnect handling.
IDevice.cs:967 queries Windows Gyrometer/Accelerometer; :987 considers serial IMU.
SensorsManager:331 constructs separate gyrometer and accelerometer providers.

## Motion and use-case acceptance

Preserve raw source samples separately from calibrated and mapped output.
Proposed frame contract to coordinate with Newton/Pascal: session, stable source
identity, sequence, monotonic sample time; optional motion with gyro rad/s,
accel m/s^2, explicit coordinate frame, source sensor identity, calibration state
and per-sensor validity. Missing sensors are null/unsupported, never invented zero.
Convert once at an output/library boundary. SDL defines rad/s and m/s^2; upstream
GamepadMotionHelpers expects degrees/s and g, Y-up, delta seconds.

HC SDLController:375-395 uses approximately /10 accel and *50 gyro conversions.
These are source facts, NOT permission to adopt them as exact SDL unit conversion.
Add analytical unit/axis fixtures rather than reproducing those constants blindly.
MotionManager:99 retains raw DSU channels, applies device axis swaps/inversions;
DSUServer:586 writes TimerManager.GetElapsedSeconds(), whose implementation at
TimerManager:202 returns milliseconds * 1000 (microseconds despite its name).
DSUServer:629-643 applies additional output signs. Test final coordinates end to
end to avoid duplicate inversion; enforce monotonic timestamps and reject stale data.

| Requested use | Existing HC mechanism | Required Playhub acceptance, not yet passed |
| --- | --- | --- |
| UMC / games without gyro | MotionManager -> LayoutManager gyro actions -> mouse or target stick | Measured physical rotation changes only intended output; profile gains/deadzones, neutral/release, no duplicate input; test named games |
| High-precision motion in Steam | Calibrated motion and capable virtual target | Correct units/time/axis, sample rate/latency and Steam-visible sensor verification; no universal precision claim |
| PS Remote Play DS4 | DualShock4Target -> VIIPER | Actual target accepted by installed Remote Play version; buttons/touch/motion requirements and teardown verified; target type alone is insufficient |
| Dolphin/Cemu/Switch emulator motion | DSUServer Cemuhook-style UDP | Packet/version/CRC/timestamp/coordinate fixtures plus actual named emulator configuration/version; not every Switch emulator assumed compatible |
| Per-game profiles | ProfileManager + LayoutManager + VirtualManager | Profile selection changes worker transform/output, restores owned state, handles foreground overlay and app switch without oscillation |

No HC test project or captured motion fixtures were identified in the inspected
archive. Source paths prove implementation, not hardware correctness. Playhub's
existing 24 SDL/passive Python tests and native/profile/diagnostic mocks do not
test HC motion, DSU, OEM families, Remote Play or applied remapping.

## Reuse and licensing gates

- HC root LICENSE.md is CC BY-NC-SA 4.0. Copying/translating a complete manager,
  encoder or profile/motion block is an adaptation, not merely inspiration.
  Attribution alone does not satisfy noncommercial/share-alike restrictions.
  A commercial/unrestricted MIT distribution cannot simply absorb those blocks.
  Direct port requires an approved compliant distribution scope or separate
  permission covering the relevant rightsholders, plus dependency audits.
- Independent reuse should obtain the ORIGINAL upstream component, not an HC
  wrapper: SDL (zlib), a verified SDL binding, GamepadMotionHelpers (MIT), or
  VIIPER's documented MIT TCP client. Pin each exact revision and ship notices.
- The upstream GamepadMotionHelpers license does NOT automatically license HC's
  GamepadMotion.cs, its added filters or its supplied GamepadMotion.dll. Binary
  provenance/build recipe and wrapper ownership remain separate checks.
- HC hidapi.net and steam-hidapi.net carry GPLv3 texts; audit controller-hidapi.net
  transitively. Do not treat everything in an HC archive as CC-only or MIT.
- VIIPER reference 679f7e0e4e8ed73addfd7c570f1bc42489bf6be7 includes the original
  Steam Controller target. HC binary metadata records that revision, not proof
  that any arbitrary released server supports it. Do not deny it from stale docs.
- VIIPER server/TCP boundary is the upstream recommended MIT-client route;
  GPL server redistribution/source obligations remain. In-process libVIIPER
  has a GPL-compatible application licensing gate. Windows USBIP signing and
  trusted-root installation issues require separate approval, never auto-install.

Sources checked:
https://creativecommons.org/licenses/by-nc-sa/4.0/
https://wiki.libsdl.org/SDL3/SDL_SensorType
https://github.com/JibbSmart/GamepadMotionHelpers
https://alia5.github.io/VIIPER/stable/getting-started/installation/
https://github.com/Alia5/VIIPER/tree/679f7e0e4e8ed73addfd7c570f1bc42489bf6be7/device/steamcontroller

## Current implementation handoff

No new runtime architecture is approved by this audit. The incomplete, unconnected
input_runtime.py draft from this task was withdrawn; main.py was never wired to it.
Newton owns reader/motion, Pascal output. Decky is the control plane, not a
frame-timed feeder. A process/main-thread owner must run source -> calibrated
motion -> selected profile -> real output locally, with watchdog and cleanup.
It must exclude its own virtual target by verified ancestry, not VID/PID alone.

Existing Steam SDL file rechecked passively:
`C:/Program Files (x86)/Steam/SDL3.dll`, ProductVersion `03.05.00.00`, SHA256
`95F4D71B795EFC11DED38271609715FBEEB7C57DBE22AF277DD493BD6858E6B3`.
This is NOT evidence of an exact SDL 3.4.10 runtime or licensed redistribution.
No SDL_GetVersion or DLL load was performed. Existing agent FindLibrary uses
this same Steam path. Version/ABI/license/Steam coexistence remain explicit gates.

Actual remapping completion requires an observed output report/consumer result,
not saved profiles, synthetic previews, accepted RPCs or virtual driver presence.
