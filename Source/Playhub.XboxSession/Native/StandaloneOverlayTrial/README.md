# Standalone overlay trial

The TextureProbe, OverlayMirror and Host subdirectories preserve the native source snapshots used by the standalone-overlay test build. The normal overlay implementation remains in its original directories.

Steam renders the original Steam overlay and Quick Access Menu through a live DWM thumbnail in an owned, non-activating topmost window. The Host components use the same placement mode for the performance display and launch cover. This path does not request Explorer's band-16 callback. An ordinary topmost window is not a claim of exclusive-fullscreen visibility.

The private setup pins the three native components in `Assets/OverlayPresentation/standalone-trial.json`, then embeds the manifest hash in the Gaming Mode agent. Missing, changed or incomplete components keep the existing shell requirement. A normal build does not enable this policy.

The trial retains the existing source authority, game identity, overlay input protocol and launch-cover handoff. Entering Gaming Mode can stop Explorer only when the complete private component group is verified and the user's existing shell settings request it. Restarting the agent does not stop Explorer. Explicit launcher or file-manager operations can still require the Windows shell.

Changes to the public installer require a separate verified release step. The experimental setup is for user testing; offline contract checks do not establish game compatibility or physical fullscreen presentation.
