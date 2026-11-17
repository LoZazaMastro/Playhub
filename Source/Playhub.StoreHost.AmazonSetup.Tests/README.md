Amazon prerequisite verification runs before the existing transfer can report
`Completed/InstallVerified`. Existing installations undergo the same current
checks before a verified launch is prepared. The private journal binds the
product, installation owner/job, destination, signed manifest, fuel hash,
verified artifacts and observed runtime versions; writing it precedes success.
Historical journals never override current missing prerequisites.

The adapter executes **no installer, shell, script, game or DLL**. It supports:

- Microsoft-signed VC++ 2010/2012/2013 `vcredist_x86.exe` / `vcredist_x64.exe`
  with the declared quiet arguments. The C and C++ runtime files must exist in
  the architecture's Windows system directory with matching PE architecture
  and a version at least as new as the declared installer.
- Microsoft-signed DirectX SDK June 2010 native setup. The manifest-verified
  CAB CFFILE tables enumerate the native versioned DLL inventory; no CAB is
  extracted. Every applicable x86/x64 runtime must exist in its Windows system
  directory, have matching architecture and the known SDK version family.
  XInput 9.1.0 accepts the Windows Vista+ OS 6.x version line. Installed shim or
  security-update file sizes/company-description strings are not signatures
  or semantic version numbers.

Missing runtimes, unknown setup families/arguments, managed DirectX/GAC setup,
invalid metadata, unmanifested paths and changed artifacts return a specific
failure through the existing Steam install result. They do not report ready and
do not trigger external installers, elevation prompts or new permissions.
Other VC generations and SDKs require separately tested adapters.

Absent `PostInstall` means zero declared steps. A manifest without fuel retains
the prior content-only installation behavior and does not gain launchability.

Tests use synthetic manifests/files and injected inert probes. They cover the
actual Amazon transfer completion class as well as parser, path, runtime,
signature, CAB and journal boundaries. The optional read-only mode verifies the
already installed QA games with their real signed manifests/current Windows
runtime metadata and writes its evidence only under a fresh TEMP directory.

```powershell
dotnet run --project Source/Playhub.StoreHost.AmazonSetup.Tests -c Release
dotnet run --project Source/Playhub.StoreHost.AmazonSetup.Tests -c Release -- --actual-metadata-readonly '<game folder>' '<retained signed Amazon .raw manifest>'
```
