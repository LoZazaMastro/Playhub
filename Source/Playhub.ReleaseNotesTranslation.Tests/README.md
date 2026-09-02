# Release Notes Translation

## Offline Tests

From the repository root:

```powershell
dotnet run --project Source/Playhub.ReleaseNotesTranslation.Tests/Playhub.ReleaseNotesTranslation.Tests.csproj
```

This standalone console project compiles only the translation service. It does
not reference, build, publish, or launch the WinUI application. All providers in
the tests are in-memory fakes. Popup source checks do not replace parent UI QA.

## Integration Hooks

- `ConfigurePlayhubReleaseNotesTranslation(service)` supplies an explicit
  provider for future popup openings. The default service is unconfigured and
  does no networking. No Google key, SDK, environment lookup or signup is added.
- `ReleaseNotesTranslationService.TranslateSegmentsAsync` receives plain-text
  runs and a resolved target language. Return one translated run per input, in
  exactly the same order, and honor the cancellation token. The app passes the
  output of its existing `PrepareDescriptionForDisplay` normalizer; links,
  formatting delimiters and code stay local and unchanged. Unsupported code
  blocks, malformed results or new markup cause an all-original fallback.
- Timeout is five seconds by default, including providers that ignore
  cancellation. Successful results use a bounded, 32-entry in-memory cache keyed
  by exact normalized source and target language. Failed requests are not cached.
- Original notes appear first; optional translation starts only after `Opened`.
  Closing, unloading, replacement or a language change prevents stale writes.
  Translation never shares cancellation/state with the installer download.
- `PLAYHUB_UI_REVIEW` never calls a translation provider. Parent UI QA can call
  `ApplyPlayhubUpdateDialogTranslationForReview(expectedContent, literalMarkdown)`
  on the UI thread with the element returned by `BuildPlayhubUpdateDialogForReview`.
  It returns false for a closed/replaced content reference. Use a literal fixture,
  not a live provider. Attribution is localized within the owned popup file and
  links to the original GitHub release. Shared localization is unchanged.

Suggested parent checks: all supported locales, normal/compact sizes, equal
button/progress widths, the stable disabled idle command plus busy status, the
literal translated-success marker, and stale injection after close/replacement.
The popup still accepts `UpdatePlayhubUpdateDialogProgress(fraction, status,
failed)` without reading Settings controls. Retained fraction/status/failure and
the pending-operation guard survive dialog disposal. Failure restores the
localized retry command at the same width; retry clears the previous failure.

## Google Research and One Read-Only Trial

Google's [Cloud Translation setup](https://docs.cloud.google.com/translate/docs/setup)
requires an enabled API, authentication and billing. Its
[authentication documentation](https://docs.cloud.google.com/translate/docs/authentication)
says Basic v2 supports API keys, while Advanced v3 uses authenticated credentials
and does not support API keys. No paid API or account setup was used.

The consumer endpoint `https://translate.googleapis.com/translate_a/single` is
not the supported Cloud Translation API. The standalone opt-in experiment makes
one GET to GitHub and one GET to this endpoint, with no credentials, cookies,
redirects or retries. Its only possible input is the public body of
[Playhub v1.2.1](https://github.com/LoZazaMastro/Playhub/releases/tag/v1.2.1).

Observed on 2026-09-02 at 16:05:23 UTC: 1,325 source characters, one Google
request, HTTP 429 after 475 ms. No translation was returned, so Markdown
round-trip quality could not be assessed. No retry was made. This does not
establish a usable or supported production provider; the app remains offline
by default. Future provider activation needs a separate explicit decision.

The opt-in reproduction hook is not part of the offline test run:

```powershell
& Source/Playhub.ReleaseNotesTranslation.Tests/GooglePublicTrial.ps1 -AllowPublicGoogleTrial
```

It never reads local release notes, accepts arbitrary text, invokes a paid API,
writes to GitHub, installs anything or opens a window.
