The executable tests use synthetic manifests, private synthetic credentials and a
supervisor stub that reports an exit without starting gogdl or a game. They cover
the actual `StoreCliProvider.StartAsync` / `CliTransfer` completion path, launch
arguments, installation receipts and the actual safe uninstaller.

GOG post-download finalization supports `supportData` file copies, recursive
manifest-selected folder copies, folder creation and explicit overwrite of an
unchanged manifest-owned file. Sources must be checksum-proven files beneath
`gog-support/<product-id>`; targets stay under the same job-owned installation.
Unknown existing files and user-modified configurations are preserved. Selected
language actions are applied; uninstall-only and unselected-language actions are
not installation work.

The two GOG DOSBox legacy locator registry values `DOSBOXFOLDER` and `DOSBOXCONF`
under `HKLM/Software/GOG.com/Games/<this-product-id>` are optional launcher
metadata and are deliberately not written. Other registry operations, `Execute`,
unknown installation actions, and nonempty `temp_executable` instructions block
verified completion. No external script interpreter or installer is executed.

GOG game-local dependencies such as DOSBox are verified using the official GOG
dependency manifests. Their exact checksums are retained in a private receipt
bound to the game manifest, job and destination, so a future repository update
cannot authorize different files during uninstall. Global `__redist` installers
are not owned game files and are not executed or removed by this adapter.

GOG v2 whole-file SHA256/MD5 is used when available; otherwise every ordered
`chunks[].md5` is verified. A chunk digest is never reused as a whole-file hash.
Both original support files and copied targets enter the removal plan. Removal
preflights the complete plan and preserves saves, unrelated files and modified
configuration. Only empty generated directories are removed.

Run:

```powershell
dotnet run --project Source/Playhub.StoreHost.GogSupport.Tests -c Release
```

An optional metadata-only diagnostic clones the already downloaded Tyrian
metadata/support/bundled runtime files into a new TEMP directory, applies the
actual public GOG manifests there, and prepares launch/removal plans. It never
launches a game, removes files or changes the original install:

```powershell
dotnet run --project Source/Playhub.StoreHost.GogSupport.Tests -c Release -- --metadata-copy-check 'F:\Tyrian 2000' '<retained GOG manifest path>'
```
