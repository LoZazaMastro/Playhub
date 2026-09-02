# Remote Plugin Catalog

Local implementation only: nothing in this change publishes, pushes, or contacts
GitHub during tests. The application packages this document as
`Assets/PluginCatalog/store-catalog.json`. `MainWindow.RemoteCatalog.cs` integrates
the local/cache baseline and background refresh.

## Deployment

The future document URL is
`https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/plugins.json`.
Owner/repository came from `.git/config`; branch came from
`refs/remotes/origin/HEAD`. The path is newly proposed, not verified as published.
An authorized maintainer can eventually publish `catalog/plugins.json` at that
path. Increment `catalogRevision` on each publication; keep `schemaVersion: 3`.
No application update is needed to add entries within the supported schema and
hosts. New hosts/schema rules require a reviewed client update.

The revision-1 document losslessly carries all 161 configured external records
from `Source/Playhub/Assets/PluginCatalog/external-plugins.json`, plus all 12
`PluginCatalogService.Definitions`, versions, keywords, glyphs and aliases.
It retains 108 `decky-store` records (including three GitLab repositories),
53 `outside-store` records, and the original verification date/Decky URL.
Playhub cover keys are the install-folder keys; actual bundled media, local
source folders, cached releases and compiled translations stay application-owned.

## Application Integration

Use one service instance, with a separate cache under `AppPaths.LocalDataRoot`:

```csharp
var remote = new RemotePluginCatalogService(
    Path.Combine(AppPaths.LocalDataRoot, "remote-plugin-catalog-v3.json"));
var baseline = await remote.LoadCachedAsync(bundledDefinitions, token);
// Render baseline.Catalog.Plugins now; schedule/observe refresh separately.
var refreshed = await remote.RefreshAsync(bundledDefinitions, token);
// Reconcile refreshed.Catalog.Plugins on the UI dispatcher.
```

`PluginCatalogService.GetBundledCatalog()` combines compiled Playhub definitions
and the external snapshot, then applies the packaged document. Missing/invalid
assets fall back to the original definitions. Its `LoadAsync` optional third
argument takes the effective catalog; mapping and installed-state hydration run
on a worker, in one installed-folder pass. Bundled images/source folders/ZIPs and
version comparison are retained. Explicit remote covers override bundled images.
New own-repository plugins retain `playhub` source/status and install resolution.

`RefreshPluginsAsync` delegates to the new partial: baseline render first, then
low-priority remote refresh. Only a newer revision triggers an automatic rebuild.
Install/uninstall/bulk guards run before and after the worker load; deferred data
applies on the next natural refresh. `PLAYHUB_UI_REVIEW` neither schedules remote
work nor accesses the user cache. No other MainWindow method was changed.
This service only reads one cache and fetches one manifest, never media,
README files, release APIs or installers. `LoadCachedAsync` never performs HTTP
or waits behind an in-flight network refresh; it also accepts stale offline cache.
`RefreshAsync` is throttled and bounded; `LoadAsync` aliases it for convenience.
Decky version/release/description updates from this trusted document are applied
too, while source, repository host, install folder and Decky ID remain immutable.
The official Decky URL is provenance metadata, not a new fetch implementation.
Existing release resolution remains separate; do not treat its per-plugin
lookups as a dynamic store browse feed. A future live feed can be merged by the
parent without replacing source provenance or reviving disabled definitions.

## Rules And Bounds

Repository slugs are case-insensitive IDs. Duplicate IDs/folders/Decky IDs,
source/status mismatches, source/folder changes to local identities, Gaming Mode
and Varta identities (including aliases), invalid Windows names, unknown/null fields,
untrusted URL hosts, credentials, HTTP, redirects and traversal are rejected.
Playhub source requires the confirmed owner. Outside-store release ZIPs must
belong to their GitHub repository; Decky CDN ZIPs retain Decky provenance.

Omitted local entries survive. An explicit full entry with `active: false` is a
tombstone: it removes the matching definition, including Decky/Playhub entries,
and remains effective from offline cache. Installed plugins are not uninstalled;
the uncatalogued installed-plugin scan may still expose them. Active records
replace metadata, including Decky versions/releases; blank cover/glyph/compatibility and empty keywords
retain local values, aliases are unioned. Required fields must be present.
Defaults: active=true, source=outside-store, status=github, Decky ID=0, optional
strings/lists empty, repository URL=https://github.com/{repository}. Playhub may
omit release URL/asset to retain the existing release-resolution workflow.

Limits: 1 MiB decoded JSON, 1,000 entries, depth 16, bounded strings/lists,
10-second HTTP deadline, 32 KiB headers, no redirects/cookies; refresh every six
hours, retry after five minutes. All cache/parse/merge work is dispatched off the
caller context. Successful documents are atomically cached; stale cache works
offline. Invalid/oversized/failed responses never replace known-good bytes or
local definitions. Caller cancellation propagates; cache-write failure retains
the valid memory result. Older revisions cannot replace bundled/cached revisions.

## Verification

From `Source/Playhub.RemoteCatalog.Tests`, run `dotnet run -c Release`.
The project-local SDK pin keeps its parser and net8 test runtime compatible.
Production services/models and the new UI partial are source-linked, with small
UI/AppPaths fakes. No app build/package or NuGet packages are required.
Fake HTTP covers success/cache/offline, schema/source/identity/URL/size/version
failures, Decky updates/tombstones, cache-only startup during refresh, merge,
concurrency, worker dispatch, cancellation and disk failure. Actual mapping and
UI-hook tests cover additions, installed versions, assets, revision gating and
operation deferral: 14 groups. Also run
`dotnet run -c Release -p:PlayhubUiReview=true` for review isolation.
Other source-linked catalog test projects must include the two new remote
service/model files; the application project includes them automatically.
For a future edited manifest, run
`dotnet run -c Release -- --validate-manifest ../../catalog/plugins.json`.
`--print-bundled-manifest` prints the initial source-derived document; it is a
bootstrap tool, not a command for replacing later remotely curated entries.
