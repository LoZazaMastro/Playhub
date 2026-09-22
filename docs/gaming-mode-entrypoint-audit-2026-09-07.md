# Gaming Mode entrypoint and deployment audit

## Scope and safeguards

Read-only live-process and source inspection on 2026-09-07. No processes stopped,
no UI closed, no configuration changes, no build, publish, installer, or agent
CLI executed. Only this report was added. Other contributors have active edits;
entrypoint, project, package, and all shared service files were left unchanged.

## Live evidence

- One GamingMode process observed: PID 145516, parent PID 143520.
- Executable: `C:\Users\Andrea\AppData\Local\GamingMode\GamingMode.exe`.
- Command line: `"C:\Users\Andrea\AppData\Local\GamingMode\GamingMode.exe" agent`.
- Start time: 2026-09-07 20:34:39 local (Europe/Rome).
- MainWindowHandle: 0; MainWindowTitle empty. This does not enumerate every
  native/helper window and is not proof that the process has no helper HWNDs.
- Snapshot working set: 218628096 bytes; private bytes: 104411136.
  These are not measurements attributable to the standalone UI.
- Installed and repository-bundled EXEs: 214073507 bytes, FileVersion 1.4.0.0,
  ProductVersion 1.4.0, identical SHA-256:
  `E37B60F4522626AAB7AC86B989999C561DE563167C66B6A7DF797A7D07DDD5BB`.
- Installed LastWriteTimeUtc: 18:30:54; bundled LastWriteTimeUtc: 19:02:29.
  These timestamps are file metadata, not reliable compilation identities.

## UI lifecycle and retained dependencies

`Source/GamingModeAgent/GamingMode/Program.cs:69` dispatches `agent` and `shell`
to AgentHost and returns before `Application.Run(new MainWindow())` at line 84.
The running command therefore does not instantiate the standalone main window
through this entrypoint. No-argument startup and unrecognized commands retain
the standalone fallback. Recognized CLI commands retain their result MessageBox.
Changing these routes would change existing semantics, not remove proven hidden
UI overhead.

`GamingModeService.OpenCompanion` (line 340) still launches without arguments;
the source search found its declaration but no caller in Playhub. `StartAgent`
(line 359) launches with `agent` and hidden process presentation. Hidden launch
presentation is not evidence of a hidden WPF MainWindow.

`AgentHost.RunAsync` has an existing `GamingMode.Agent` mutex gate and creates
backend services. `SplashScreenService` still uses WPF windows, media, animation,
and a dispatcher. Removing UseWPF or the desktop framework is not a bounded,
functionality-preserving cleanup. The self-contained EXE also packages runtimes;
its 214 MB disk size must not be equated with resident standalone UI overhead.

## Deployment findings

There is no installed-versus-repository-bundle byte mismatch in this snapshot.
The source tree can nevertheless be newer than the running agent: AgentHost.cs
was modified at 19:16:59 UTC, after the recorded package generation at 19:02.
This establishes a source/artifact freshness gap, not which individual changes
are absent from the binary. No binary decompilation or loaded-module identity
inspection was performed.

`build-agent.bat` publishes to a temporary folder and copies the result into
`publish` and Playhub's `Plugins/Gaming Mode/gaming-mode-win-x64` package. The
latest existing log records successful copies at 21:02 local. Playhub's build,
build-installer, and build-all batch workflows already reference this agent
build. However, Playhub.csproj only copies plugin content and has no agent
project dependency: invoking that project directly can reuse an old package.
Publishing an agent also does not replace an already running process.

`GamingModeService.NeedsAgentUpdate` compares length and timestamp, not content.
Here identical binaries have different timestamps, so those inputs would report
an update if the inspected repository package were the runtime bundle. This can
cause a redundant reinstall; equal-size changed binaries with non-newer dates
can conversely be missed. The running Playhub package path was not inspected,
so this report does not claim an actual redundant reinstall occurred.

## Safe disposition

No objectively unnecessary running standalone window was found. Keep the
entrypoint and WPF dependencies unchanged under the preserve-semantics contract.
The current integration already uses a separate internal agent process; merging
its lifetime into Playhub would require auditing shell/boot and recovery behavior.

For a future authorized release, coordinate with the packaging owner: generate
the agent after source changes settle, validate content hashes through package
and installed paths, then explicitly arrange activation at an acceptable time.
Do not run install.ps1 as a diagnostic: it closes/kills agents, copies files,
and changes startup integration. Consider content identity instead of timestamp
in the update predicate as a separate service-owner change. A future removal of
the standalone route requires explicit behavior-change approval and coordinated
cleanup of OpenCompanion and CLI expectations.

Verification was source inspection and read-only metadata/hash collection. No
runtime tests were needed because no executable code was changed.
