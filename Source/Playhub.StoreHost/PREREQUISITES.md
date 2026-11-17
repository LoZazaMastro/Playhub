Playhub finalizes prerequisites before reporting a Digital Stores installation
complete. Amazon uses its signed manifest and fuel.json; Epic reads the retained
installed Legendary manifest offline; GOG resolves the game's declared dependency
IDs through the official dependency repository and verifies the downloaded depot.

All three adapters share the Windows installer runner, including the original
declared arguments, actual exit status, Windows elevation when required and a
machine-wide installation mutex. The exact executable stays read-pinned through
execution. A canceled request retains its store ownership until setup exits.
Receipts bind the owner job, manifest authorization, executable bytes and arguments;
a receipt cannot override changed files or authorize a different installer.

GOG machine redistributables download into a private job-owned cache, outside the
game's uninstall inventory. ISI remains handled by the game support-data adapter;
it is not started as a standalone runtime installer without a game recipe.

Protocol references inspected against the bundled runtimes:
- Legendary 0.21.1, commit 42f6bdeadde3a9526dc8eb713763476999ac217b:
  https://github.com/legendary-gl/legendary/blob/0.21.1/legendary/cli.py
- gogdl 1.4.0, commit 6c6ef53fd4965d8d9b943c73d9400fe55eadc6ba:
  https://github.com/Heroic-Games-Launcher/heroic-gogdl/blob/v1.4.0/gogdl/dl/managers/dependencies.py

The inert Prerequisites and AmazonSetup checks exercise the actual adapters with
synthetic manifests and installer delegates. They never start a game or installer.
Physical prerequisite installation is a separate acceptance check.
