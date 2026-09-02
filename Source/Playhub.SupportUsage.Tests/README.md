# Support Reminder Integration

## Parent Hooks

Only the parent should edit `MainWindow.xaml.cs`:

1. Call `InitializeSupportReminder();` near the end of successful `LoadAsync`,
   after the final `ApplyLanguage()` and after settings have been loaded. The
   initializer restores `SupportReminderUsageSeconds` from the loaded object,
   refuses an unbound/default settings object, and is a no-op in UI review.
2. Add `CaptureSupportReminderUsageForSave();` before the existing save in
   `SaveSettingsSilentlyAsync`. It only samples the counter and cannot recurse.
3. Add `using var supportOperation = BeginSupportReminderOperation();` inside
   the click handlers for the generic async `Button`, `IconButton` and
   `StoreIconButton` helpers, before awaiting the action. Those scopes cover
   Decky/import actions that currently expose no shared busy flag. They survive
   navigation and dispose on failure too. Support-page actions (including Test)
   are excluded; existing update/plugin/scan/repair flags are checked directly.
4. For this preview build only, below the existing donation action:

```csharp
#if PLAYHUB_UPDATE_PREVIEW
support.Children.Add(Button("Test", () => ShowSupportReminderAsync(force: true)));
#endif
```

The forced preview does not initialize, consume, reset, or save the real usage
counter. It still declines to open while another dialog/popup or operation is
active. The button can be clicked again once that operation is finished.

## UI Review Hooks

`BuildSupportReminderForReview(fakeDonation, onClose, width, maxHeight)` returns
the same popup content without starting a timer, writing settings, waiting two
hours, or launching a browser. Both callbacks are optional and default to local
no-ops/close. Use a fake donation callback to check click/double-click behavior.
`ShowSupportReminderAsync(force: true)` can also show the native dialog in a UI
review build; its donation callback is compiled to a no-op. Normal review boot
never starts the reminder. UI QA remains with the parent/reviewer.

## Timing And Persistence

- Due after 7,200 seconds of cumulative use across launches, not installation age.
- Counts only activated, foreground, visible, non-minimized app use with global
  input within the last five minutes. An idle boundary inside a sample is split;
  returning input does not fill the preceding idle gap.
- Uses a monotonic Stopwatch and a 30-second foreground-only timer. Gaps over 90
  seconds are discarded conservatively to exclude sleep/locked dispatcher gaps.
  No per-frame work. Unavailable input telemetry fails closed.
- The due amount saturates at one reminder; deferral cannot create a backlog.
  Only a successful automatic dialog `Opened` resets usage to zero. Closing the
  dialog starts no new interval and does not launch a browser.
- Saves through the existing atomic settings-save path every active checkpoint,
  on deactivation and on close. Reminder saves are serialized/coalesced. Forced
  termination can lose the final unsaved checkpoint (normally at most 30 seconds).

## Offline Tests

```powershell
dotnet run --project Source/Playhub.SupportUsage.Tests/Playhub.SupportUsage.Tests.csproj
```

This compiles only the pure clock, settings model and localization dictionary.
It does not build the WinUI project, write user settings, install anything, open
a donation page, or launch the application. Integration checks inspect source;
they are not a substitute for parent UI review.
