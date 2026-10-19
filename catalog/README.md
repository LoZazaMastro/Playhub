# Playhub public content

The application reads these public files anonymously:

- Plugin Store: https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/plugins.json
- Accade oggi: https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/catalog/on-this-day.json
- Editorial media: https://raw.githubusercontent.com/LoZazaMastro/Playhub/main/assets/on-this-day/v1/

These are ordinary raw requests: no GitHub login, token, application registration
or GitHub REST/GraphQL API quota is needed. GitHub still controls availability,
CDN propagation, traffic limits and acceptable use. Hosting is not unlimited.

## Editing the Plugin Store

Edit catalog/plugins.json on main. Keep schemaVersion: 3 and increase
catalogRevision with every publication. Names, descriptions, categories,
versions, exact release ZIP URLs, compatible covers, aliases and keywords are
editable. Add repository records without updating the application.

Repository identity, host, install folder, source and official Decky ID cannot
be reassigned over an existing record. External releases must be versioned ZIPs
belonging to their repositories, or official Decky CDN artifacts. New schema
versions or URL hosts require a client update. An explicit active: false removes
the catalog record. Omitted bundled records survive. Disabling a record does
not uninstall an already installed plugin.

The supplied 19 October workbook adds 47 installable records, including the 20
newest entries, and removes LumaDeck. There are 243 records: 13 Playhub, 108
Decky, 122 outside the Store. Two built-ins are disabled; the active fallback
has 241 entries. See workbook-import-2026-10-19.json for source rows and exclusions.
Nineteen older candidates lack a supported versioned installable ZIP.
Unverified radar leads are excluded. Catalog membership does not mean a plugin
has been tested on Windows or on the user's hardware.

A bundled snapshot and last-good disk cache render first. Background refresh
reads the public file every six hours or on explicit Store refresh. Changed
content updates the visible catalog even at the same publication revision.
Lower revisions are rejected. Official Decky membership refreshes independently.
Optional release notes/discovery are separate from catalog availability.

The reader limits decoded JSON to 1 MiB, 1,000 entries, depth 16 and a 10-second
HTTP deadline. Redirects, duplicate identities/properties, source conflicts,
untrusted URLs and excluded identities including LumaDeck are rejected. Valid
documents replace cache atomically. Failure preserves cache and retries after
five minutes.

From Source/Playhub.RemoteCatalog.Tests:

    dotnet run -c Release -- --validate-manifest ../../catalog/plugins.json
    dotnet run -c Release
    dotnet run -c Release -p:PlayhubUiReview=true

## Editing Accade oggi

catalog/on-this-day.json owns calendar copy, imported editions, fallback days,
image assignments, URLs and expected SHA-256/byte counts. Keep schemaVersion: 1
and increase revision with every publication. The initial document reuses curated
copy and 2,638 image assignments. Existing photos retain immutable commit URLs;
368 formerly bundled covers now live in the same public media directory.

The backend validates this raw file and passes selected remote text/media
through the story RPC. New stories/images do not depend on compiled frontend
lists. Bundled text/media metadata remain initial offline fallbacks. No editorial
image is imported into the frontend bundle. Only displayed images download.

For a new image, add the file to assets/on-this-day/v1/, retain its source/credit,
and add filename, URL, subject, kind, sha256 and bytes to its images group.
Prefer a new filename when bytes change. URLs must use this repository's raw
main or an immutable 40-character commit. Preserve IDs and chapter bindings.
Supported formats are JPEG, PNG, WebP and GIF.

The backend refreshes every six hours, retries after five minutes and saves
last-good cache atomically. Maximum JSON is 24 MiB. Redirects, unsupported
schemas, duplicate keys, malformed subjects/editions and invalid media
integrity/URLs are rejected. Socket I/O times out after ten seconds. Invalid
data never replaces good cache bytes. Images are hash checked and cached:
256 images / 256 MiB persistently, 24 images / 32 MiB in memory, three parallel
downloads. Digest changes at one URL cannot reuse old in-memory bytes.

tools/export-on-this-day.py recreates the initial feed from source snapshots.
It is a bootstrap helper. Do not overwrite later curated public edits unless
those edits have also been reconciled into the source snapshots.

From Source/GamingModeDeckyPlugin, validate remote edits, offline fallback,
all 366 dates and every referenced local image:

    python -m unittest discover -s tests -p test_history_remote.py
    node --test tests/historyPhotoCache.test.mjs
    node node_modules/typescript/bin/tsc --noEmit

Publication is separate from local tests. A successful local build does not
confirm an uploaded public URL or interaction with Steam's live UI.
