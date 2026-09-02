using System.Diagnostics;
using Playhub.Services;

var failures = 0;
var count = 0;
async Task Test(string name, Func<Task> run)
{
    count++;
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
}
void Require(bool value, string message) { if (!value) throw new Exception(message); }
Task<IReadOnlyList<string>> Upper(IReadOnlyList<string> segments, string language, CancellationToken token)
    => Task.FromResult<IReadOnlyList<string>>(segments.Select(value => value.ToUpperInvariant()).ToArray());
void Original(ReleaseNotesTranslationService.Translation result, string source)
    => Require(!result.IsAutomatic && result.Markdown == source, "original fallback was changed or marked translated");

await Test("unconfigured service returns original without network", async () =>
{
    var service = new ReleaseNotesTranslationService();
    Require(!service.IsConfigured, "network provider enabled by default");
    Original(await service.TranslateAsync("## Changes", "it"), "## Changes");
});

await Test("headings, CRLF, quotes, lists, bold, links and code remain byte-for-byte intact", async () =>
{
    const string source = "# Changes\r\n\r\n> ## Important\r\n> - **Fixed** the [restart](https://github.com/test/Keep_(Case)?x=1&y=2 \"Keep title\").\r\n1. Keep `Playhub-Setup-1.2.1.exe` and __settings__.\r\nhttps://github.com/test/Keep\r\n";
    const string expected = "# CHANGES\r\n\r\n> ## IMPORTANT\r\n> - **FIXED** THE [RESTART](https://github.com/test/Keep_(Case)?x=1&y=2 \"Keep title\").\r\n1. KEEP `Playhub-Setup-1.2.1.exe` AND __SETTINGS__.\r\nhttps://github.com/test/Keep\r\n";
    var service = new ReleaseNotesTranslationService((segments, language, token) =>
    {
        Require(segments.All(value => !value.Contains("https://") && !value.Contains('`') &&
            !value.Contains("Keep title") && !value.Contains("Setup")), "protected syntax leaked to provider");
        return Upper(segments, language, token);
    });
    var result = await service.TranslateAsync(source, "it");
    Require(result.IsAutomatic && result.Markdown == expected, "Markdown structure changed: " + result.Markdown);
});

await Test("relative links, references, escapes and HTML attributes are retained", async () =>
{
    const string source = "[guide](../Keep_(Case).md#Setup) and [help][KeepId]\n[KeepId]: https://github.com/Keep \"Title\"\n<em title=\"Keep\">hello</em> \\*world\\*";
    const string expected = "[GUIDE](../Keep_(Case).md#Setup) AND [HELP][KeepId]\n[KeepId]: https://github.com/Keep \"Title\"\n<em title=\"Keep\">HELLO</em> \\*WORLD\\*";
    var result = await new ReleaseNotesTranslationService(Upper).TranslateAsync(source, "it");
    Require(result.IsAutomatic && result.Markdown == expected, "protected source fields changed: " + result.Markdown);
});

await Test("new markup, links, control characters and malformed responses fall back atomically", async () =>
{
    const string source = "**Changes** and [guide](https://github.com/Keep)";
    foreach (var bad in new[] { "# Injected", "**Injected**", "[new](https://example.com)", "hello\nworld",
        "<b>HTML</b>", "&#60;img&#62;", "https://example.com", "1. List", "- List", "  ", "bad\u0000text",
        "new (syntax)", "image!" })
    {
        var service = new ReleaseNotesTranslationService((segments, _, _) =>
            Task.FromResult<IReadOnlyList<string>>(segments.Select(_ => bad).ToArray()));
        Original(await service.TranslateAsync(source, "it"), source);
    }
    var missing = new ReleaseNotesTranslationService((_, _, _) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
    Original(await missing.TranslateAsync(source, "it"), source);
});

await Test("valid same-language output is cached but never labelled automatic", async () =>
{
    var calls = 0;
    var service = new ReleaseNotesTranslationService((segments, _, _) => { calls++; return Task.FromResult(segments); });
    Original(await service.TranslateAsync("## Changes", "en"), "## Changes");
    Original(await service.TranslateAsync("## Changes", "en"), "## Changes");
    Require(calls == 1, "identity result was not cached");
});

await Test("memory cache keys include exact source and normalized target language", async () =>
{
    var calls = 0;
    var service = new ReleaseNotesTranslationService((segments, language, token) =>
    {
        calls++;
        return Task.FromResult<IReadOnlyList<string>>(segments.Select(value => language + " " + value).ToArray());
    });
    var first = await service.TranslateAsync("Changes", " IT ");
    Require(first == await service.TranslateAsync("Changes", "it"), "cache mismatch");
    await service.TranslateAsync("Changes", "de");
    await service.TranslateAsync("Other changes", "it");
    Require(calls == 3, "language/source cache collision");
});

await Test("memory cache remains bounded", async () =>
{
    var calls = 0;
    var service = new ReleaseNotesTranslationService((segments, language, token) => { calls++; return Upper(segments, language, token); });
    for (var index = 0; index < 33; index++) await service.TranslateAsync("Changes " + index, "it");
    await service.TranslateAsync("Changes 32", "it");
    Require(calls == 33, "newest result was not cached");
    await service.TranslateAsync("Changes 0", "it");
    Require(calls == 34, "oldest result was not evicted");
});

await Test("cancelled request never invokes provider, including a cache hit", async () =>
{
    var calls = 0;
    var service = new ReleaseNotesTranslationService((segments, language, token) => { calls++; return Upper(segments, language, token); });
    await service.TranslateAsync("Changes", "it");
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    Original(await service.TranslateAsync("Changes", "it", cancellation.Token), "Changes");
    Original(await service.TranslateAsync("Other", "it", cancellation.Token), "Other");
    Require(calls == 1, "cancelled request invoked provider");
});

await Test("closing/cancelling suppresses late results and does not cache them", async () =>
{
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var late = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var calls = 0;
    var service = new ReleaseNotesTranslationService((segments, language, token) =>
    {
        calls++;
        started.TrySetResult();
        return calls == 1 ? late.Task : Upper(segments, language, token);
    });
    using var cancellation = new CancellationTokenSource();
    var pending = service.TranslateAsync("Changes", "it", cancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
    cancellation.Cancel();
    Original(await pending.WaitAsync(TimeSpan.FromSeconds(2)), "Changes");
    late.SetResult(new[] { "Late translation" });
    await late.Task;
    Require((await service.TranslateAsync("Changes", "it")).Markdown == "CHANGES" && calls == 2,
        "late cancelled translation entered cache");
});

await Test("timeout bounds a provider that ignores cancellation", async () =>
{
    var late = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
    var service = new ReleaseNotesTranslationService((_, _, _) => late.Task, TimeSpan.FromMilliseconds(60));
    var watch = Stopwatch.StartNew();
    Original(await service.TranslateAsync("Changes", "it").WaitAsync(TimeSpan.FromSeconds(2)), "Changes");
    Require(watch.Elapsed < TimeSpan.FromSeconds(1), "timeout did not bound the request");
    late.SetResult(new[] { "Late translation" });
    await late.Task;
});

await Test("provider synchronous work runs off the calling thread", async () =>
{
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var service = new ReleaseNotesTranslationService((_, _, _) =>
    {
        release.Task.GetAwaiter().GetResult();
        finished.TrySetResult();
        return Task.FromResult<IReadOnlyList<string>>(new[] { "CHANGES" });
    }, TimeSpan.FromMilliseconds(60));
    var watch = Stopwatch.StartNew();
    var pending = service.TranslateAsync("Changes", "it");
    Require(watch.Elapsed < TimeSpan.FromSeconds(1), "provider blocked caller");
    try { Original(await pending.WaitAsync(TimeSpan.FromSeconds(2)), "Changes"); }
    finally { release.TrySetResult(); }
    await finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
});

await Test("provider failure falls back and a subsequent attempt can succeed", async () =>
{
    var calls = 0;
    var service = new ReleaseNotesTranslationService((segments, language, token) =>
    {
        if (++calls == 1) throw new InvalidOperationException("offline fixture");
        return Upper(segments, language, token);
    });
    Original(await service.TranslateAsync("Changes", "it"), "Changes");
    Require((await service.TranslateAsync("Changes", "it")).IsAutomatic && calls == 2, "failure was cached");
});

await Test("unsupported blocks, excessive source and non-prose stay original", async () =>
{
    var calls = 0;
    var service = new ReleaseNotesTranslationService((segments, language, token) => { calls++; return Upper(segments, language, token); });
    foreach (var source in new[] { "```cs\nvar value = 1;\n```", "~~~\ncode\n~~~", "    code", "\tcode",
        new string('x', 20_001), "", " \n ", "`Keep.exe`\nhttps://github.com/Keep" })
        Original(await service.TranslateAsync(source, "it"), source);
    Require(calls == 0, "unsupported/non-prose source was sent to provider");
});

await Test("invalid language and timeout are rejected without provider work", async () =>
{
    var calls = 0;
    var service = new ReleaseNotesTranslationService((segments, language, token) => { calls++; return Upper(segments, language, token); });
    foreach (var language in new[] { "", "auto", "../it", "it&key=private", new string('x', 40) })
        Original(await service.TranslateAsync("Changes", language), "Changes");
    Require(calls == 0, "invalid language reached provider");
    foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromMinutes(2) })
    {
        var rejected = false;
        try { _ = new ReleaseNotesTranslationService(timeout: timeout); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected, "invalid timeout accepted");
    }
});

await Test("popup source retains localized heading, measured sizing and offline review guard", () =>
{
    var popup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PopupSource.txt"));
    Require(popup.Contains("new Thickness(30, 40, 30, 24)") && popup.Contains("new Thickness(0, 36, 0, 16)"), "spacing regressed");
    Require(popup.Contains("Text = T(\"Novit\u00e0\")"), "heading is not localized");
    Require(popup.Contains("(216 + idleLabelMeasure.DesiredSize.Width) / 2"), "idle label not measured");
    Require(popup.Contains("actionButton.Width = progress.Width = Math.Min(preferredActionWidth, innerWidth)"), "responsive widths differ");
    Require(popup.Contains("#if !PLAYHUB_UI_REVIEW\n") || popup.Contains("#if !PLAYHUB_UI_REVIEW\r\n"), "review network guard missing");
    Require(popup.Contains("token.IsCancellationRequested") && popup.Contains("ReferenceEquals(_playhubUpdateDialogContent, content)"), "late result guards missing");
    Require(popup.Contains("ApplyPlayhubUpdateDialogTranslationForReview"), "literal offline review hook missing");
    return Task.CompletedTask;
});

await Test("popup progress contract has no Settings dependency and retains retry state", () =>
{
    var popup = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PopupSource.txt"));
    Require(!popup.Contains("_playhubUpdateBar") && !popup.Contains("_playhubUpdateStatus."), "popup uses Settings progress controls");
    Require(!popup.Contains("_playhubUpdateRunning ="), "popup took ownership of downloader running flag");
    Require(popup.Contains("var label = _playhubUpdateDialogFailed ? T(\"Riprova\") : T(\"Aggiorna ora\")"), "retry or stable busy label regressed");
    Require(popup.Contains("_playhubUpdateDialogFraction = fraction is double value") &&
        popup.Contains("_playhubUpdateDialogProgressBar.Value = _playhubUpdateDialogFraction ?? 0"), "reported progress is not retained/reapplied");
    var cleanupStart = popup.IndexOf("xamlRoot.Changed -= OnRootChanged", StringComparison.Ordinal);
    var cleanupEnd = popup.IndexOf("private PlayhubUpdateService.UpdateInfo Select", cleanupStart, StringComparison.Ordinal);
    var cleanup = popup[cleanupStart..cleanupEnd];
    Require(!cleanup.Contains("_playhubUpdateDialogFraction =") && !cleanup.Contains("_playhubUpdateDialogFailed =") &&
        !cleanup.Contains("_playhubUpdateDialogStatusText =") && !cleanup.Contains("_playhubUpdateDialogActionPending ="),
        "dialog close cleared operation state");
    return Task.CompletedTask;
});

Console.WriteLine($"{count - failures}/{count} passed; no network or UI launched.");
return failures == 0 ? 0 : 1;
