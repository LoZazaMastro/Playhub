# Standalone composition host

This prototype gives Playhub an independent Direct3D 11 / DirectComposition presentation surface. It does not use the Explorer callback or change the production overlay backend. It is built separately and is not included in the public installer.

`CompositionPresenter` owns a premultiplied-alpha composition swap chain attached to its caller's window. Its frame ingress accepts a compatible texture from the same Direct3D device. The diagnostic producer is separate from Steam's overlay and Quick Access Menu.

## Build and exercise

```powershell
cmake -S . -B build -A x64
cmake --build build --config Release
.\build\Release\ExplorerFreeDiagnostic.exe C:\Temp\composition-report.json
```

The output directory must exist. The diagnostic runs five child processes on a private, inactive Windows desktop. It checks shell absence, GPU texture contents, alpha and cleanup while preserving the user's input desktop. It reports occluded presents separately from failures. These results do not establish physical visibility above a game or exclusive-fullscreen compatibility.

The separate `PLAYHUB_UIACCESS_HOST` build profile prepares a manifest for evaluation of a signed, securely installed helper. It is disabled by default. It does not grant UIAccess, install certificates, change system policy or launch the unsigned helper. Eligibility, signing and real-game validation must be settled before that profile can become a deployment route.

## References

- [DirectComposition window targets](https://learn.microsoft.com/en-us/windows/win32/api/dcomp/nf-dcomp-idcompositiondesktopdevice-createtargetforhwnd)
- [Composition swap chains](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgifactory2-createswapchainforcomposition)
- [PresentMon's overlay implementation notes](https://github.com/GameTechDev/PresentMon/blob/007f1cf009670e8b90d014324515ed41e5c54422/README-CaptureApplication.md)
- [Windows UIAccess requirements](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview)

The prototype code is GPL-3.0-only, matching Playhub. The references above informed the design; their source code is not bundled here.
