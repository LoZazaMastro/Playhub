# Localization and Language Restart Audit

Final coordinator gate: PASS (`Inventory/verification.final.json`, `Complete: true`, zero failures). Translation assets are stable; all 12 workers and all follow-ups are exited. Parent native language results were read and confirmed at `Source/Playhub/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64/ui-review/results-languages.json` (340/340). Parent also confirmed the full UI regression passed 549/549, with navigation median 21.82 ms and p95 23.76 ms. Installer builds remain parent-owned; no further application changes are planned by the coordinator.

## Coordinator Changes

- `Source/Playhub/MainWindow.xaml.cs:3208`: one-line language selection delegation; `:8925`: SDK restart replaces launch-before-close. No other UI ranges were edited by the coordinator.
- `Source/Playhub/MainWindow.LanguageSettings.cs:12`: guarded save/restart flow and failure recovery.
- `Source/Playhub/Services/LocalizationService.cs:111`: supplemental lookup before Italian passthrough, typed formatting and cached template matching; `:499`: the four advanced Decky translations. Removed only the obsolete combined LG/Sony trademark entry.
- `Source/Playhub/Assets/Localization/{en,es,fr,de,pt,uk,zh,ja,ko,hi,ru,it}.json`: isolated per-language additions/overrides.
- `Source/Playhub.LanguageSettings.Tests/` and `Source/Playhub.LocalizationAudit/`: isolated tests, source inventory, native-state contracts, quality samples and coverage reports.

## Scope and Inventory

Roslyn parses every `MainWindow*.cs`, service and model source file; XML parsing covers root XAML text properties. Classification uses source contexts, not an Italian-word heuristic. English-source diagnostics and legacy startup labels are included. Each inventory entry retains its exact key, file, line and contextual occurrence. Long repeated contexts are abbreviated, not keys.

Current inventory: 55 C# files plus XAML, 2,008 unique expressions, 494 interface keys, 12 existing native inline notices, zero unreviewed expressions and 1,502 validated exclusions. The separate composition inventory contains 133 concatenations; all five containing interface text localize their fragments before composing them.

`Inventory/coverage.json` and `missing.<language>.json` describe the original base-table assignment: 320 covered keys plus 174 gaps per non-Italian language. `Inventory/verification.final.json` is the release gate for the integrated runtime output, not the baseline assignment.

## Language Review

Exactly one dedicated worker reviewed each language. The first English, Spanish and French workers resumed their original sessions to review all 494 live keys, not only gaps. Other workers reviewed existing live entries alongside new keys. Every worker performed a second quality pass. Korean and Hindi received additional same-session corrections after coordinator sampling. All 12 worker processes and all follow-up processes have exited.

| Language | New Keys | Targeted Overrides | JSON Entries |
|---|---:|---:|---:|
| English | 174 | 31 | 205 |
| Spanish | 174 | 29 | 203 |
| French | 174 | 30 | 204 |
| German | 174 | 8 | 182 |
| Portuguese (existing Brazilian voice) | 174 | 24 | 198 |
| Ukrainian | 174 | 33 | 207 |
| Simplified Chinese | 174 | 8 | 182 |
| Japanese | 174 | 12 | 186 |
| Korean | 174 | 15 | 189 |
| Hindi | 174 | 13 | 187 |
| Russian | 174 | 21 | 195 |
| Italian source QA | 11 English-source mappings | 0 | 11 |

Total: 1,914 new non-Italian translations, 224 targeted improvements to existing translations, and 11 Italian mappings. Existing complete tables remain in place. Italian `Mica`, `Acrylic`, `Sfondo pieno` and `Cover` are unchanged.

The coordinator reviewed 15 contextual runtime samples per non-Italian language: advanced Decky headings and descriptions, startup/controller instructions, streaming, library import, dynamic CSS Loader version, installation errors, restart, appearance, cursor inactivity, the Windows security error and the Valve independence paragraph. `Inventory/quality-samples.json` records the exact outputs. This caught and corrected omitted inactivity in Korean and mixed English grammatical fragments in Hindi; the existing-entry reviews also restored omitted conditions, accents and consistent voice in English, Spanish and French. No language was filled with English sentences.

## Validation

- Zero missing keys across 11 non-Italian languages: 5,434 actual `LocalizationService` coverage checks.
- Placeholder indices, specifiers, multiplicity and newline counts checked in both JSON overrides and every live interface translation.
- 31 formatted source messages per non-Italian language: 341 checks compare translating preformatted messages against translating the template before formatting.
- All 12 existing inline automatic-translation notices verified; Italian English aliases verified before Italian passthrough.
- Dictionary and format-pattern caches are reused; 1,000 warmed CSS Loader dynamic lookups completed in 1 ms in the local audit run (diagnostic measurement, not a timing assertion).
- Read-only MSBuild evaluation includes every language JSON in the existing `Assets/**/*.*` content rule with `PreserveNewest`. No application project build or package was performed by the coordinator.
- Standalone restart suite: 8/8 groups, all 132 language directions, same/alias/loading guards, reentry, save failure rollback/retry, restart failure handling, Decky translations and source wiring.
- Four isolated Windows App SDK restart integrations (`it->en`, `en->it`, `it->ja`, `ja->it`) proved durable settings, old test process exit and replacement acquisition of a unique test-only instance key. Only disposable test executables were launched, never the user's Playhub process.
- Parent native language review: 340/340 checks across all 12 languages, including real lazy dialogs, six pages per language, dynamic text, inputs and original-content protection, menu/popup/rich text, and forced collection/finalization of native-only controls, Runs and tooltips. Parent screenshots of German and Chinese were visually verified.

Commands:

```powershell
dotnet run --project Source/Playhub.LocalizationAudit/Playhub.LocalizationAudit.csproj -- F:/Playhub/Plugin/Playhub/Source/Playhub F:/Playhub/Plugin/Playhub/Source/Playhub.LocalizationAudit/Inventory --verify
# Run from Source/Playhub.LanguageSettings.Tests to use its pinned .NET 8 SDK:
dotnet run --no-launch-profile
```

## Root Causes

The old restart launched a replacement before closing the old process. `SingleInstanceService` correctly redirected that replacement to the still-registered old instance; the replacement then exited. The isolated probe reproduced that race. No Italian-specific branch exists in selection, saving or relaunch, and the inspected logs did not establish a deterministic language-specific cause. The fix awaits the atomic save, prevents overlapping selections, and delegates shutdown/wait/relaunch to Windows App SDK `AppInstance.Restart`. Save failure restores the selection; restart failure preserves the saved language and unlocks the selector.

Localization gaps had multiple causes: absent table keys; popup/dialog and lazy construction routes outside the old walker; preformatted messages needing indexed template matching; and source keys tied to collectible managed WinRT wrappers. The parent owns the new logical traversal, construction hooks, tooltip capture, per-native-control state and native rendering QA. See `RUNTIME-HOOKS.md` for that contract.

## Validated Exclusions

`Inventory/exclusions.json` records all 1,502 excluded expressions and their reasons. Major groups are 300 original plugin description/README and plugin identity expressions, 144 invariant non-UI support-export expressions, 35 standalone brand names, and the original MIT license text. The remainder is technical paths/URLs, code/scripts, parser and registry tokens, dependency-property registration names, file filters, numeric/version formats, diagnostic logs, punctuation, or language-invariant value composition. The `No content.` README parser sentinel was verified to be discarded before display and removed from supplemental dictionaries. User-generated names, paths and original README/changelog bodies are not localization templates.

The obsolete combined LG/Sony trademark translation entry was removed. No other legal text or installer agreement was changed by the coordinator.

## Source Wording Notes

Approved Italian wording was preserved. Existing source ambiguities were recorded rather than silently changing conditions: splash hints refer to game readiness although the surrounding section describes entering Gaming Mode; a streaming executable picker retains a folder placeholder; and the CSS Loader "one ZIP" diagnostic also covers zero matching packages. CSS Loader "unrecognized" diagnostics concern patch-code patterns, not runtime service detection. The shared `Attiva` gender concern affects detached status text that `BuildDeckyStep` intentionally does not render, not a visible current label.
