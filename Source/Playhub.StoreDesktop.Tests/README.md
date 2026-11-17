# Desktop accounts and Steam migration checks

The production desktop page contains four account connections. It does not instantiate the game catalog, downloads, Game Pass, folder picker, install review, Steam sync or uninstall controls. Authentication uses the existing store-owned sign-in windows. Account state is read back from the service.

The migration controller observes `steam-migration-request` and `steam-migration-status`. Only server `completed` with equal confirmed/total values is success. Waiting, errors and unknown totals are explicit; Retry remains available, and Later does not cancel the durable migration. Announcement IDs are presentation-only and cannot complete a migration. A completed server receipt suppresses the dialog on reopening.

Run inert checks:

```powershell
dotnet run --project Source/Playhub.StoreDesktop.Tests -c Release
```

Compile the actual WinUI components with synthetic services:

```powershell
python -X utf8 Source/Playhub.StoreDesktop.Tests/UiReview/Run.py
```

Add `--private-desktop` to execute the actual controls and native dialogs on an owned, non-input Windows desktop. The fixture refuses to run on the user's desktop. It checks four account controls, stable native focus on snapshot updates, disconnect, rejected/unconfirmed login, confirmed login, all 12 saved languages, widths 356/640/1280, a competing native dialog, mounting before XamlRoot, confirmed progress, completed receipt, waiting Steam, native Retry and unknown-total presentation. It never contacts a real account or Steam service and never executes a game/installer. The authentication browser itself is substituted, so these checks do not establish live account authorization.

The capture reports are isolated WinUI fixture evidence, not installed application screenshots. Real Steam collection persistence is verified by the native migration owner and subsequent integrated QA.
