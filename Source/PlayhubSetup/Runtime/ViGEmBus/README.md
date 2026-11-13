# Controller dependency

This directory contains the unmodified, signed Nefarius ViGEmBus 1.22.0
installer and its BSD-3-Clause license. `SOURCE.json` records the official
release, release digest, driver version and redistribution provenance.

When packaging the x64 Steam Controller relay, copy these files into
`Dependencies/ViGEmBus/` in the Playhub payload. The complete dependency is
available offline. The build must verify the release and license digests and
the Nefarius Authenticode signature. Ordinary builds without the relay do not
need this dependency.
The setup is built with `-p:PlayhubControllerDriverRequired=true` only for a
payload containing the relay. This immutable assembly opt-in prevents files
left from a previous preview from requesting driver installation in a later
ordinary build.

Playhub setup preserves an existing controller driver. A matching, started
bus is reused without elevation. Setup runs this dependency once, through
the normal Windows administrator consent prompt, only when the driver,
device and installed product are absent. It never disables driver-signing
checks, replaces an existing driver, runs an uninstaller or restarts Windows.
Windows Installer owns its installation transaction; setup does not kill it
or repeat an unsuccessful installation. Its exit status and the actual bus
capability determine the final controller result separately from app setup.

The upstream setup supports x86, x64 and ARM64. Playhub's current controller
relay is x64; that limitation is checked separately. The upstream project is
archived. Its 1.22.0 release removes the old updater while retaining driver
1.21.442.0. Existing installations, including their updater, are left intact.
Playhub uninstall keeps the shared system driver.

The dependency checks can be exercised without installing it:

```powershell
dotnet run --project Source/Playhub.ControllerDriver.Tests -- --artifact-proof Source/PlayhubSetup/Runtime/ViGEmBus
```

The artifact check validates hashes and signatures and reads the existing
driver capability. It never executes the packaged installer or changes a
service, device, driver, registry value or Steam setting. Clean-PC installation,
administrator refusal and restart behavior still require an isolated Windows
installation test before public release.
