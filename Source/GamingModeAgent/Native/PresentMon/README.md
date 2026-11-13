# PresentMon live output

The bundled x64 console is built from Intel PresentMon commit
`007f1cf009670e8b90d014324515ed41e5c54422` (2.6.1). The original MIT license
and upstream third-party notices accompany the executable.

`live-output.patch` adds `--live_output_ms 20|50|100`. This option flushes the
process's own realtime ETW session and drains new events at that interval.
Flush and shutdown share a mutex so a closing session handle cannot be reused
by the output thread. Frame calculations retain the original ETW timestamps.
Without this argument, console behavior remains the upstream default.

The continuous Playhub HUD uses 20 ms event delivery and publishes new samples
at 100 or 50 ms. Its frame metrics use a rolling 500 ms window; the publication
interval is independent of the measurement window. The four-second A/B capture
keeps its existing arguments and default upstream delivery behavior.

To build, use Visual Studio 2022 with the C++ workload and Windows SDK, Python
3.10+, CMake 4.2.2+, and the Visual Studio vcpkg component. Clone the upstream
repository, check out the commit above, and apply this directory's patch:

```powershell
git apply path/to/live-output.patch
cmake -S . -B build-console -G "Visual Studio 17 2022" -A x64 -DPMON_BUILD_SCOPE=CONSOLE -DPMON_AUTO_PREPARE_DEPENDENCIES=OFF -DPMON_DEPENDENCY_ROOT=C:/Temp/pmon-deps
cmake --build build-console --config Release --target PresentMonConsole
```

Keep dependency directories short on Windows to avoid the compiler's legacy
path limits. `provenance.json` records the exact packaged binary and patch
digests. Rebuilding requires updating the runtime executable pin after
verification. No service, driver, CEF frontend or installer is built for this
component.

The CPU/GPU/RAM reader is separate from PresentMon. It owns a CPU/GPU-only PDH
query with a reusable buffer and reads physical memory through Windows. It
samples every 100 ms on a background worker while the HUD is active, skips
Steam-menu suspension, and closes the query and buffer when the runtime stops.
Unavailable counters remain empty. Windows counter quantization can make some
reads unavailable; a 50 ms FPS setting does not alter hardware sensor polling.
