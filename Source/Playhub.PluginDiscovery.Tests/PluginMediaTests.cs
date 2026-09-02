using System.Net;
using System.Net.Http.Headers;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Playhub.Models;
using Playhub.Services;

await MediaTests.RunAsync();

internal static class MediaTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg==");

    public static async Task RunAsync()
    {
        await Run("status, HTML, signatures, and descriptions", InvalidMediaAsync);
        await Run("nested paths, branch slashes, spaces, and references", RelativePathsAsync);
        await Run("strict GitHub conversion and remote URL handling", GithubUrlsAsync);
        await Run("extensionless attachments, images, and videos", AttachmentsAsync);
        await Run("redirect validation and redirect bounds", RedirectsAsync);
        await Run("rate-limited API and default-branch fallback", DefaultBranchFallbackAsync);
        await Run("failure caching, expiry, and description preservation", FailureCacheAsync);
        await Run("concurrent deduplication and isolated media copies", CacheSharingAsync);
        await Run("bounded candidates and network concurrency", NetworkBoundsAsync);
        await Run("streamed byte limits and range validation", StreamBoundsAsync);
        await Run("timeouts preserve text and release requests", TimeoutAsync);
        await Run("code samples, badges, and ordinary links excluded", ExtractionNoiseAsync);
        await Run("linked screenshots, escaped destinations, and referenced video", LinkedMediaAsync);
        await Run("request concurrency is bounded across repositories", CrossRepositoryBoundsAsync);
        await Run("README cache capacity is bounded", CacheCapacityAsync);
        await Run("catalog loading stays offline and built-in API is unchanged", LazyLoadingAsync);
        await Run("card covers precede lazy screenshot fallback", PreviewSelectionAsync);
        await Run("card fallback reuses validated detail screenshots", PreviewDetailsAsync);
        await Run("social preview is validated and decoder failures are skipped", PreviewValidationAsync);
        await Run("card fallback requests are deduplicated and expire", PreviewCacheAsync);
        Console.WriteLine("PASS all 20 media regression groups");
    }

    private static async Task Run(string name, Func<Task> test)
    {
        await test();
        Console.WriteLine("PASS " + name);
    }

    private static DeckyPluginInfo Plugin(string slug = "demo/plugin") => new()
    {
        IsPlayhubPlugin = false, RepositorySlug = slug,
        Readme = "Existing README", LongDescription = "Existing description",
        Media = new() { new() { Url = "https://github.com/demo/plugin/blob/main/missing.png" } }
    };

    private static async Task InvalidMediaAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var service = new PluginCatalogService(http);
        handler.Readme("demo/plugin", "# Useful description\n" + string.Join("\n", new[]
        {
            "good", "missing", "html", "spoof", "empty", "truncated", "wrong-mime", "svg", "forbidden", "later"
        }.Select(name => $"![{name}](images/{name}.png)")));
        handler.Image("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/good.png", Png);
        handler.Respond("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/html.png", "text/html", Encoding.UTF8.GetBytes("<html>Not an image</html>"));
        handler.Respond("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/spoof.png", "image/png", Encoding.UTF8.GetBytes("<!doctype html><html>Login</html>"));
        handler.Image("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/empty.png", Array.Empty<byte>());
        handler.Image("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/truncated.png", Png.Take(8).ToArray());
        handler.Respond("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/wrong-mime.png", "video/mp4", Png);
        handler.Respond("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/svg.png", "image/svg+xml", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>") );
        handler.Respond("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/forbidden.png", "image/png", Png, HttpStatusCode.Forbidden);
        handler.Image("https://raw.githubusercontent.com/demo/plugin/feature/ui/images/later.png", Png);
        var plugin = Plugin();
        await service.EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 2 && plugin.Media.All(media => media.Kind == "image"), "Only real PNGs should survive, including candidates after the sixth");
        Check(plugin.LongDescription.Contains("Useful description"), "Description survives invalid media");
        Check(handler.Requests.All(request => request.Method == "GET"), "Verification is read-only");
    }

    private static async Task RelativePathsAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", """
            # Nested README
            ![Nested](./shots/capture%20one.png?raw=1#preview)
            ![Parent](../assets/Picture.png)
            ![Root](/assets/root.png)
            ![Spaced](<../assets/with space (1).png> "Screenshot")
            ![Reference][ screen  shot ]
            [screen shot]: ../assets/referenced.png "Screenshot"
            <img src=../assets/unquoted.png alt='Unquoted'>
            [Documentation](https://example.test/preview.png/docs)
            """, "docs/README.md");
        var expected = new[]
        {
            "docs/shots/capture%20one.png?raw=1", "assets/Picture.png", "assets/root.png",
            "assets/with%20space%20(1).png", "assets/referenced.png", "assets/unquoted.png"
        }.Select(path => "https://raw.githubusercontent.com/demo/plugin/feature/ui/" + path).ToArray();
        foreach (var url in expected) handler.Image(url, Png);
        var plugin = Plugin();
        await new PluginCatalogService(http).EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Select(media => media.Url).SequenceEqual(expected), "Resolve against the actual nested README and repository root: " + string.Join(",", plugin.Media.Select(media => media.Url)));
        Check(plugin.LongDescription.Contains("https://example.test/preview.png/docs"), "Do not truncate documentation URLs containing a media extension");
        Check(!plugin.LongDescription.Contains("!["), "Inline and reference images must not leak into descriptions");
    }

    private static async Task GithubUrlsAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", """
            ![Blob](https://github.com/Other/Repo/blob/feature/ui/Shot.png?raw=1#preview)
            ![Raw](https://github.com/Other/Repo/raw/refs/heads/release/v2/shot.png)
            ![External](https://media.example/shot.png?next=https://github.com/o/r/blob/main/a.png)
            ![Protocol relative](//media.example/small.png)
            ![Case sensitive](https://raw.githubusercontent.com/Other/Repo/feature/ui/shot.png)
            ![Readme](https://github.com/Other/Repo/blob/feature/ui/README.md)
            ![Repository](https://github.com/Other/Repo)
            ![File](file:///C:/missing.png)
            ![Data](data:image/png;base64,AAAA)
            ![Local](http://localhost/private.png)
            ![Anchor](#image)
            ![Escape](../../../../wrong.png)
            """);
        var expected = new[]
        {
            "https://raw.githubusercontent.com/Other/Repo/feature/ui/Shot.png?raw=1",
            "https://raw.githubusercontent.com/Other/Repo/refs/heads/release/v2/shot.png",
            "https://media.example/shot.png?next=https://github.com/o/r/blob/main/a.png",
            "https://media.example/small.png",
            "https://raw.githubusercontent.com/Other/Repo/feature/ui/shot.png"
        };
        foreach (var url in expected) handler.Image(url, Png);
        handler.Respond("https://raw.githubusercontent.com/Other/Repo/feature/ui/README.md", "text/plain", Encoding.UTF8.GetBytes("# Not an image"));
        var plugin = Plugin();
        await new PluginCatalogService(http).EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Select(media => media.Url).SequenceEqual(expected), "GitHub conversion must be host/path-specific and case-sensitive for assets");
        Check(handler.Requests.Count == 7, "Unsafe URLs, repository pages and escaped relative paths must not be probed");
    }

    private static async Task AttachmentsAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var video = "https://github.com/user-attachments/assets/video-id";
        var image = "https://github.com/user-attachments/assets/image-id";
        var legacy = "https://github.com/demo/plugin/assets/1234/legacy-id";
        handler.Readme("demo/plugin", $"![Image]({image})\n{video}\n{legacy}\n<video><source src='https://media.example/demo.webm'></video>\n![JPEG](https://media.example/photo)\n![GIF](https://media.example/animated)");
        handler.Respond(video, "video/mp4", Mp4());
        handler.Respond(image, "application/octet-stream", Png);
        handler.Respond(legacy, "video/quicktime", Mp4("qt  "));
        handler.Respond("https://media.example/demo.webm", "video/webm", new byte[] { 0x1a, 0x45, 0xdf, 0xa3, 0x8b, 0x42, 0x82, 0x84 }.Concat(Encoding.ASCII.GetBytes("webm12345678")).ToArray());
        handler.Respond("https://media.example/photo", "image/jpeg", new byte[] { 255, 216, 255, 224, 0, 16, 74, 70, 73, 70, 0 });
        handler.Respond("https://media.example/animated", "image/gif", Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
        var plugin = Plugin();
        await new PluginCatalogService(http).EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 6 && plugin.Media.Count(media => media.Kind == "video") == 3, "Infer kind from verified bytes/MIME, including extensionless GitHub assets");
        Check(plugin.Media.Single(media => media.Url == image).Kind == "image", "Octet-stream PNG detection");
    }

    private static byte[] Mp4(string brand = "isom") => new byte[] { 0, 0, 0, 24 }
        .Concat(Encoding.ASCII.GetBytes("ftyp" + brand)).Concat(new byte[] { 0, 0, 2, 0 })
        .Concat(Encoding.ASCII.GetBytes("isommp42")).ToArray();

    private static async Task RedirectsAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", "![Good](https://github.com/user-attachments/assets/good)\n![HTML](https://media.example/html.png)\n![Loop](https://media.example/loop.png)\n![Local](https://media.example/local.png)");
        handler.Redirect("https://github.com/user-attachments/assets/good", "https://cdn.example/signed?token=A%2BB&expires=123");
        handler.Image("https://cdn.example/signed?token=A%2BB&expires=123", Png);
        handler.Redirect("https://media.example/html.png", "/login");
        handler.Respond("https://media.example/login", "text/html", Encoding.UTF8.GetBytes("<html>Login</html>"));
        handler.Redirect("https://media.example/loop.png", "/loop.png");
        handler.Redirect("https://media.example/local.png", "file:///C:/private.png");
        var plugin = Plugin();
        await new PluginCatalogService(http).EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 1 && plugin.Media[0].Url == "https://github.com/user-attachments/assets/good", "Keep stable attachment URL, not an expiring redirect target");
        Check(handler.Count("https://media.example/loop.png") == 5, "Redirect loop is bounded");
        Check(handler.Requests.Where(request => request.Url.Contains("cdn.example")).All(request => request.Range == "bytes=0-4095"), "Redirects retain bounded range probes");
    }

    private static async Task DefaultBranchFallbackAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Respond("https://api.github.com/repos/demo/plugin/readme", "application/json", Encoding.UTF8.GetBytes("{}"), HttpStatusCode.Forbidden);
        handler.Respond("https://raw.githubusercontent.com/demo/plugin/HEAD/docs/README.md", "text/plain", Encoding.UTF8.GetBytes("# Fallback\n![Shot](../assets/shot.png)"));
        handler.Image("https://raw.githubusercontent.com/demo/plugin/HEAD/assets/shot.png", Png);
        var plugin = Plugin();
        await new PluginCatalogService(http).EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 1 && plugin.LongDescription.Contains("Fallback"), "Rate limits must not force a guessed main/master branch");
        Check(handler.Requests.Count == 7, "Fallback README search is bounded");
    }

    private static async Task FailureCacheAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var now = DateTimeOffset.Parse("2026-09-02T12:00:00Z");
        var service = new PluginCatalogService(http, () => now);
        var plugin = Plugin();
        await service.EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 0 && plugin.Readme == "Existing README" && plugin.LongDescription == "Existing description", "Missing README removes stale media but preserves all descriptions");
        var count = handler.Requests.Count;
        await service.EnsurePluginDetailsAsync(Plugin());
        Check(handler.Requests.Count == count, "Negative README cache prevents repeated failures");
        now = now.AddMinutes(6);
        handler.Readme("demo/plugin", "![Only media](https://media.example/only.png)");
        handler.Image("https://media.example/only.png", Png);
        await service.EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 1 && plugin.LongDescription == "Existing description", "Image-only README enriches media without deleting description");
        now = now.AddMinutes(6);
        handler.Respond("https://media.example/only.png", "text/html", Encoding.UTF8.GetBytes("<html>Gone</html>"));
        await service.EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 0 && plugin.LongDescription == "Existing description", "Expired positive media must be revalidated and removed");
        var mediaCount = handler.Count("https://media.example/only.png");
        handler.Readme("demo/other", "![Only media](https://media.example/only.png)");
        await service.EnsurePluginDetailsAsync(Plugin("demo/other"));
        Check(handler.Count("https://media.example/only.png") == mediaCount, "Negative media cache is shared across repositories");
    }

    private static async Task CacheSharingAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", "# Cached\n![Shared](https://media.example/shared.png)");
        handler.Readme("demo/other", "# Other\n![Shared](https://media.example/shared.png)");
        handler.Image("https://media.example/shared.png", Png);
        var service = new PluginCatalogService(http);
        var plugins = Enumerable.Range(0, 24).Select(_ => Plugin()).Append(Plugin("demo/other")).ToArray();
        await Task.WhenAll(plugins.Select(service.EnsurePluginDetailsAsync));
        Check(handler.Count("https://api.github.com/repos/demo/plugin/readme") == 1 && handler.Count("https://media.example/shared.png") == 1, "Concurrent cache misses must be single-flight");
        plugins[0].Media[0].Url = "broken";
        plugins[0].Media.Clear();
        var next = Plugin();
        await service.EnsurePluginDetailsAsync(next);
        Check(next.Media.Single().Url == "https://media.example/shared.png" && plugins[1].Media.Count == 1, "UI mutation cannot corrupt another object or the cache");
    }

    private static async Task NetworkBoundsAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", "# Many screenshots\n" + string.Join("\n", Enumerable.Range(0, 100).Select(index => $"![Shot](https://media.example/{index}.png)")));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var peak = 0;
        for (var index = 0; index < 100; index++)
            handler.Set($"https://media.example/{index}.png", async (_, token) =>
            {
                var count = Interlocked.Increment(ref active);
                Interlocked.Exchange(ref peak, Math.Max(peak, count));
                if (count == 4) entered.TrySetResult();
                try
                {
                    await release.Task.WaitAsync(token);
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }
                finally { Interlocked.Decrement(ref active); }
            });
        var pending = new PluginCatalogService(http).EnsurePluginDetailsAsync(Plugin());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(!pending.IsCompleted && peak == 4, "Requests are asynchronous with a maximum of four active probes");
        release.SetResult();
        await pending;
        Check(handler.Requests.Count(request => request.Url.StartsWith("https://media.example/")) == 12, "At most twelve media candidates per README");
        Check(peak <= 4 && active == 0, "All request slots are released");
    }

    private static async Task StreamBoundsAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", "![Large](https://media.example/large.png)\n![Partial](https://media.example/partial.png)\n![Bad range](https://media.example/bad.png)");
        var payload = new byte[1024 * 1024];
        Png.CopyTo(payload, 0);
        var stream = new TrackingStream(payload);
        handler.Set("https://media.example/large.png", (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return Task.FromResult(response);
        });
        handler.Partial("https://media.example/partial.png", Png, 0);
        handler.Partial("https://media.example/bad.png", Png, 42);
        var plugin = Plugin();
        await new PluginCatalogService(http).EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 2, "Only successful, start-of-file partial responses are valid");
        Check(stream.BytesRead == 4096 && stream.Disposed, "Ignored Range responses are streamed only to the probe limit and disposed");
    }

    private static async Task TimeoutAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", "# Keep this text\n![Slow](https://media.example/slow.png)");
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Set("https://media.example/slow.png", async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cancelled.TrySetResult(); }
            throw new InvalidOperationException("Unreachable");
        });
        var plugin = Plugin();
        await new PluginCatalogService(http, requestTimeout: TimeSpan.FromMilliseconds(200)).EnsurePluginDetailsAsync(plugin);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(plugin.Media.Count == 0 && plugin.LongDescription.Contains("Keep this text"), "Media timeouts cannot discard a fetched description");
    }

    private static async Task ExtractionNoiseAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", """
            # Actual text
            <!-- ![hidden](https://media.example/hidden.png) -->
            ```markdown
            ![code](https://media.example/code.png)
            ```
            `![inline](https://media.example/inline.png)`
            ![Badge](https://img.shields.io/demo.png)
            ![Donation](https://ko-fi.example/demo.png)
            [Read me](README.md)
            [Not media](https://github.com/demo/plugin)
            [Image documentation](https://media.example/image.png/readme)
            ![Visible](https://media.example/visible.png?token=abc&amp;size=large)
            """);
        handler.Image("https://media.example/visible.png?token=abc&size=large", Png);
        var plugin = Plugin();
        await new PluginCatalogService(http).EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 1 && handler.Requests.Count == 2, "Only the visible candidate should cause a probe");
        Check(plugin.LongDescription.Contains("Actual text") && plugin.LongDescription.Contains("Image documentation"), "Keep useful prose links");
    }

    private static async Task LazyLoadingAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var service = new PluginCatalogService(http);
        var catalog = await service.LoadAsync("Z:/nonexistent-playhub-test", "Z:/nonexistent-playhub-test");
        await service.EnsurePluginDetailsAsync(new DeckyPluginInfo { IsPlayhubPlugin = true, RepositorySlug = "demo/plugin" });
        await service.EnsurePluginDetailsAsync(Plugin("invalid"));
        Check(catalog.Count > 0 && handler.Requests.Count == 0, "No eager network requests from catalog loading or built-in details");
        Check(new PluginCatalogService() is not null, "Public parameterless constructor remains available");
    }

    private static async Task LinkedMediaAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        handler.Readme("demo/plugin", """
            # Linked media
            [![Screenshot](https://camo.githubusercontent.com/opaque)](https://example.test/details)
            ![Escaped](./assets/shot\ \(one\).png)
            [Watch demonstration][clip]
            [clip]: ./assets/video.mp4
            ![Collapsed][]
            [Collapsed]: ./assets/collapsed.png
            """);
        handler.Image("https://camo.githubusercontent.com/opaque", Png);
        handler.Image("https://raw.githubusercontent.com/demo/plugin/feature/ui/assets/shot%20(one).png", Png);
        handler.Respond("https://raw.githubusercontent.com/demo/plugin/feature/ui/assets/video.mp4", "video/mp4", Mp4());
        handler.Image("https://raw.githubusercontent.com/demo/plugin/feature/ui/assets/collapsed.png", Png);
        var plugin = Plugin();
        await new PluginCatalogService(http).EnsurePluginDetailsAsync(plugin);
        Check(plugin.Media.Count == 4 && plugin.Media.Count(media => media.Kind == "video") == 1, "Linked and referenced media must resolve without bogus truncated probes");
        Check(handler.Requests.Count == 5, "No probes for outer documentation links or partial destinations");
    }

    private static async Task CrossRepositoryBoundsAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var active = 0;
        var peak = 0;
        var gate = new object();
        for (var repo = 0; repo < 4; repo++)
        {
            handler.Readme($"demo/repo{repo}", string.Join("\n", Enumerable.Range(0, 4).Select(index => $"![Shot](https://media.example/{repo}-{index}.png)")));
            for (var index = 0; index < 4; index++)
                handler.Set($"https://media.example/{repo}-{index}.png", async (_, token) =>
                {
                    lock (gate) { active++; peak = Math.Max(active, peak); }
                    try
                    {
                        await Task.Delay(30, token);
                        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
                        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                        return response;
                    }
                    finally { lock (gate) active--; }
                });
        }
        var service = new PluginCatalogService(http);
        var plugins = Enumerable.Range(0, 4).Select(index => Plugin($"demo/repo{index}")).ToArray();
        await Task.WhenAll(plugins.Select(service.EnsurePluginDetailsAsync));
        Check(peak <= 4 && active == 0 && plugins.All(plugin => plugin.Media.Count == 4), "The request limit must apply across simultaneous detail views");
    }

    private static async Task CacheCapacityAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var now = DateTimeOffset.Parse("2026-09-02T12:00:00Z");
        var service = new PluginCatalogService(http, () => now);
        for (var index = 0; index < 257; index++)
        {
            handler.Readme($"demo/repo{index}", "# Description without media");
            await service.EnsurePluginDetailsAsync(Plugin($"demo/repo{index}"));
            now = now.AddMilliseconds(1);
        }
        await service.EnsurePluginDetailsAsync(Plugin("demo/repo0"));
        Check(handler.Count("https://api.github.com/repos/demo/repo0/readme") == 2, "The oldest completed README must be evicted at the capacity limit");
    }

    private static async Task PreviewSelectionAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var service = new PluginCatalogService(http);
        var plugin = Plugin();
        plugin.Media.Clear();
        plugin.CoverImage = "https://media.example/cover.png";
        plugin.Image = "https://media.example/alternate.png";
        handler.Image(plugin.CoverImage, Png);
        handler.Image(plugin.Image, Png);
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        Check(await service.FindPluginPreviewAsync(plugin, rejected) == plugin.CoverImage,
            "Keep a working declared cover without loading README media");
        Check(handler.Requests.Count == 1, "Working covers must not trigger README or social requests");
        rejected.Add(plugin.CoverImage);
        Check(await service.FindPluginPreviewAsync(plugin, rejected) == plugin.Image,
            "An alternate declared image precedes README fallback");
        Check(handler.Requests.Count == 2, "Do not reprobe decoder-rejected URLs");
    }

    private static async Task PreviewDetailsAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var service = new PluginCatalogService(http);
        var plugin = Plugin("Firemoon777/decky-notifications");
        plugin.CoverImage = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/artifact_images/Decky%20Notifications.png";
        handler.Readme(plugin.RepositorySlug, "# Notifications\n![](assets/cover.png)\n![](assets/pairing.jpg)");
        var cover = "https://raw.githubusercontent.com/Firemoon777/decky-notifications/feature/ui/assets/cover.png";
        handler.Image(cover, Png);
        await service.EnsurePluginDetailsAsync(plugin);
        var requests = handler.Requests.Count;
        var rejected = new HashSet<string>(StringComparer.Ordinal) { plugin.CoverImage };
        Check(await service.FindPluginPreviewAsync(plugin, rejected) == cover,
            "A failed store cover must use the same real image as the detail gallery");
        Check(handler.Requests.Count == requests, "Existing validated detail media requires no extra requests");
        var fresh = Plugin(plugin.RepositorySlug);
        fresh.Media.Clear();
        fresh.CoverImage = plugin.CoverImage;
        Check(await service.FindPluginPreviewAsync(fresh, rejected) == cover,
            "Recreated card objects must reuse the shared README cache");
        Check(handler.Requests.Count == requests, "Recycled cards must not refetch README or media");
    }

    private static async Task PreviewValidationAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var service = new PluginCatalogService(http);
        var plugin = Plugin();
        plugin.Media.Clear();
        plugin.CoverImage = "https://media.example/html.png";
        handler.Respond(plugin.CoverImage, "image/png", Encoding.UTF8.GetBytes("<html>Not an image</html>"));
        handler.Readme(plugin.RepositorySlug, "# Useful text\n![Shot](https://media.example/shot.png)");
        handler.Image("https://media.example/shot.png", Png);
        var social = "https://opengraph.githubassets.com/1/demo/plugin";
        handler.Image(social, Png);
        var rejected = new HashSet<string>(StringComparer.Ordinal) { "https://media.example/shot.png" };
        Check(await service.FindPluginPreviewAsync(plugin, rejected) == social,
            "Reject HTML covers and decoder-failed screenshots before validated GitHub fallback");
        rejected.Add(social);
        Check(await service.FindPluginPreviewAsync(plugin, rejected) is null,
            "Never hand the decoder an already rejected image or fabricate an unvalidated cover");
        plugin.RepositorySlug = "not a repository";
        var requests = handler.Requests.Count;
        Check(await service.FindPluginPreviewAsync(plugin, rejected) is null && handler.Requests.Count == requests,
            "Invalid repository identities must not generate fallback network requests");
    }

    private static async Task PreviewCacheAsync()
    {
        using var handler = new FixtureHandler();
        using var http = new HttpClient(handler);
        var now = DateTimeOffset.Parse("2026-09-02T12:00:00Z");
        var service = new PluginCatalogService(http, () => now);
        var plugin = Plugin();
        plugin.Media.Clear();
        plugin.CoverImage = "https://media.example/missing.png";
        handler.Readme(plugin.RepositorySlug, "# No screenshots");
        var social = "https://opengraph.githubassets.com/1/demo/plugin";
        handler.Respond(social, "text/html", Encoding.UTF8.GetBytes("<html>Unavailable</html>"));
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => service.FindPluginPreviewAsync(plugin, rejected)));
        Check(results.All(result => result is null) && handler.Requests.Count == 3,
            "Concurrent missing cards share negative cover, README and social caches");
        handler.Image(social, Png);
        Check(await service.FindPluginPreviewAsync(plugin, rejected) is null && handler.Requests.Count == 3,
            "Negative caching prevents failed-card network flooding");
        now = now.AddMinutes(6);
        Check(await service.FindPluginPreviewAsync(plugin, rejected) == social && handler.Requests.Count == 6,
            "Expired failures recover after media becomes available");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _fixtures = new(StringComparer.Ordinal);
        public ConcurrentBag<(string Url, string Method, string? Range)> Requests { get; } = new();
        public int Count(string url) => Requests.Count(request => request.Url == url);

        public void Set(string url, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fixture) => _fixtures[url] = fixture;

        public void Readme(string slug, string markdown, string path = "README.md", string branch = "feature/ui")
        {
            var json = JsonSerializer.Serialize(new
            {
                path,
                download_url = $"https://raw.githubusercontent.com/{slug}/{branch}/{path}",
                encoding = "base64",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown))
            });
            Respond($"https://api.github.com/repos/{slug}/readme", "application/json", Encoding.UTF8.GetBytes(json));
        }

        public void Image(string url, byte[] bytes) => Respond(url, "image/png", bytes);

        public void Respond(string url, string mime, byte[] bytes, HttpStatusCode status = HttpStatusCode.OK)
        {
            Set(url, (request, _) =>
            {
                var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(bytes), RequestMessage = request };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
                return Task.FromResult(response);
            });
        }

        public void Redirect(string url, string target)
        {
            Set(url, (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri(target, UriKind.RelativeOrAbsolute);
                return Task.FromResult(response);
            });
        }

        public void Partial(string url, byte[] bytes, long start)
        {
            Set(url, (_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, start + bytes.Length - 1, start + bytes.Length + 100);
                return Task.FromResult(response);
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add((url, request.Method.Method, request.Headers.Range?.ToString()));
            return _fixtures.TryGetValue(url, out var fixture)
                ? fixture(request, cancellationToken)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Not found"), RequestMessage = request });
        }
    }

    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

namespace Playhub.Services
{
    internal static class AppPaths
    {
        public static string LocalDataRoot => throw new InvalidOperationException("Media tests must not access production cache directories.");
    }
}
