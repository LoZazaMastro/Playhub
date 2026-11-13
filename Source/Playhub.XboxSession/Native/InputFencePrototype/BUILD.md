# Building the prototype DLL

Use an x64 MSVC2022 developer command prompt with the Windows SDK. No live
target or controller is needed. From this directory:

```bat
mkdir obj
cl /nologo /MT /c /W3 /O2 /Foobj\ MinHook\src\buffer.c MinHook\src\hook.c MinHook\src\trampoline.c MinHook\src\hde\hde64.c
cl /nologo /MT /std:c++20 /W4 /WX /EHsc /O2 /LD Fence.cpp obj\buffer.obj obj\hook.obj obj\trampoline.obj obj\hde64.obj /link /Brepro /OUT:PlayhubInputFence.dll
```

Retain the MinHook BSD2/HDE copyright/license notices. Playhub source is
GPL-3.0-only. Updating the DLL requires a newly reviewed SHA in both
ExperimentalInputFence.cs and the sidecar csproj; the ordinary build deliberately
refuses an unreviewed binary. The file is published loose beside the sidecar
with its license. This command only compiles; it does not load/inject the DLL.


## Experimental renderer/readback revision (still default OFF)

Native attachment ABI2 is3728 bytes. The private companion supplies the exact
TrustedSteamFiles renderer path, SHA256, file PE image size/timestamp; the
target retains the read-only pinned file even when the module is explicitly absent.
When present, it also retains the exact loaded renderer module. All three XInput1_4
exports, including ordinal100, are mandatory. Direct callers inside that exact
MEM_IMAGE executable allocation are delegated unmodified; this is not a
global module-name allowlist or a SpecialK provenance claim.

FenceReadback.h describes a128-byte version2 diagnostic export-data snapshot, including exact refusal
stage/HRESULT and explicit present/absent renderer mode. Only the
companion reads it via fixed pinned-module RVA, identity and seqlock checks. It
cannot grant Shown/input authority. Counts distinguish non-Valve API reads,
exact main-image callers, delegated Valve calls, actual Guide observations and
filtered results. A counter is not game-consumption or physical-controller proof.

No driver/service/late API loading was added. The Root permit remains mandatory.
Missing/unknown metadata refuses attach. Capture revocation still passes input
through; hooks and module references deliberately stay resident until game exit.
Real Closed now publishes a distinct phase5 cleanup tail after phase2. Both
phases remove only digital bits observed held from original game-facing calls
while Shown, per user index, until a real release. New bits and axes pass. The
producer and consumer each bound this cleanup to5seconds with fresh250ms
private context; any negative/foreign/dead source loses the latch. No additional
UI or full-neutral capture authority is granted. Bits never observed while
Shown are not attributed or suppressed. This is not universal isolation or a
safe-default shipping claim.

Explicit absence requires ERROR_MOD_NOT_FOUND. Unknown observation or any late
renderer arrival/change permanently fails open. No renderer is loaded or called
by this path. A completed refused attachment can be diagnosed through the same
private read-only export; unknown remote work remains latched without retry.

## Rapid reopening during a Closed release tail

A fresh real Guide edge observed through the original ordinal100 can start the
same bounded1500ms opening capture while phase2 or5 remains current. This does
not publish Shown or reset the old held-button mask/deadline. Without Guide, new
buttons/axes still pass. A Guide hold already seen during Shown is not a new
edge after Closed; actual release is required. All frame/source/FG/freshness
checks and the finite cleanup budget remain unchanged.
