// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Offline causal checks: actual production acquisition, persistence and image decoder.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Playhub.Services;
using Playhub.StoreLibrary;

await Checks.Run();

static class Checks
{
    private static int passed;
    private static readonly byte[] Png = ImageBytes(Color.CornflowerBlue);
    private static readonly string Base = "https://m.media-amazon.com/images/";
    private static readonly StoreCapabilities Capabilities = new(StoreInstallKind.Direct, true, true, true, true, true);
    private static void Check(bool result, string name)
    { if (!result) throw new Exception("FAIL: " + name); passed++; }
    private static byte[] ImageBytes(Color color)
    {
        using var image = new Bitmap(4, 6); using (var graphics = Graphics.FromImage(image)) graphics.Clear(color);
        using var stream = new MemoryStream(); image.Save(stream, ImageFormat.Png); return stream.ToArray();
    }
    private static StoreLibraryGame Game(string id, string? kind = "cover", string[]? alternatives = null) =>
        new(new(StoreKind.Amazon, id), "Original " + id, 3, Capabilities, false, false,
            ArtworkKey: "amazon:" + id, ArtworkUrl: Base + id + ".png", ArtworkFallbackUrls: alternatives, ArtworkKind: kind);
    private static async Task Until(Func<bool> condition, string name)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(6)) throw new Exception("Timeout: " + name); await Task.Delay(10); }
    }
    private static async Task<string> Resolve(StoreLibraryArtworkCache cache, StoreLibraryGame game)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? initial = cache.Resolve(game, () => changed.TrySetResult());
        if (initial is not null) return initial;
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(6));
        return cache.Resolve(game, () => { }) ?? throw new Exception("Published cover missing.");
    }
    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    internal static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "Playhub-StoreArtworkCache-Checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Alternate(root, "404", _ => new(HttpStatusCode.NotFound), 5);
            await Alternate(root, "html", _ => Bytes(Encoding.UTF8.GetBytes("<html>not a cover, despite HTTP 200</html>")), 5);
            var corrupt = Png.ToArray(); corrupt[29] ^= 1;
            await Alternate(root, "corrupt", _ => Bytes(corrupt), 4);
            await BoundAndProvider(root);
            await KindAndIdentity(root);
            await SingleFlightAndPersistent(root);
            await Legacy(root);
            await Cancel(root);
            await Concurrency(root);
            Check(passed == 39, "complete 40-check matrix");
            Console.WriteLine($"PASS {passed} offline desktop artwork-cache checks (production SaveAsync/System.Drawing decoder; no actual HTTP).");
        }
        finally
        {
            string absolute = Path.GetFullPath(root), temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith("Playhub-StoreArtworkCache-Checks-", StringComparison.Ordinal))
                throw new Exception("Refusing cleanup outside this uniquely owned TEMP fixture.");
            Directory.Delete(absolute, true);
        }
    }
    private static async Task Alternate(string root, string id, Func<HttpRequestMessage, HttpResponseMessage> primary, int assertions)
    {
        string alternate = Base + id + "-alternate.png";
        var handler = new Replay((r, _) => Task.FromResult(r.RequestUri!.AbsoluteUri == alternate ? Bytes(Png) : primary(r)));
        using var http = new HttpClient(handler); int fallback = 0;
        using var cache = new StoreLibraryArtworkCache(Path.Combine(root, id), (_, _) => { Interlocked.Increment(ref fallback); return Task.FromResult<byte[]?>(Png); }, http);
        string path = await Resolve(cache, Game(id, alternatives: [alternate]));
        Check(handler.Urls.SequenceEqual(new[] { Base + id + ".png", alternate }), id + " advances to exact official alternate");
        Check(fallback == 0, id + " does not bypass alternate for provider");
        Check(File.ReadAllBytes(path).SequenceEqual(Png), id + " stores validated actual bytes");
        using var decoded = Image.FromFile(path);
        Check(decoded.Width == 4 && decoded.Height == 6, id + " real decoder dimensions");
        if (assertions == 5) { using var bitmap = new Bitmap(decoded); Check(bitmap.GetPixel(0, 0).ToArgb() == Color.CornflowerBlue.ToArgb(), id + " actual decoded pixels"); }
        Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, id + " invalid primary never publishes an image or partial file");
    }
    private static async Task BoundAndProvider(string root)
    {
        var handler = new Replay((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var http = new HttpClient(handler); int fallback = 0;
        var alternatives = Enumerable.Range(1, 6).Select(i => Base + "bound-" + i + ".png").ToArray();
        using var cache = new StoreLibraryArtworkCache(Path.Combine(root, "bound"), (game, _) => { Check(game.Key.Id == "bound", "provider receives exact identity"); Interlocked.Increment(ref fallback); return Task.FromResult<byte[]?>(Png); }, http);
        string path = await Resolve(cache, Game("bound", alternatives: [alternatives[0], alternatives[0], ..alternatives.Skip(1)]));
        Check(handler.Urls.SequenceEqual(new[] { Base + "bound.png", alternatives[0], alternatives[1], alternatives[2] }), "trusted distinct official candidates bounded to four");
        Check(fallback == 1 && File.ReadAllBytes(path).SequenceEqual(Png), "provider fallback publishes real validated image once");
    }
    private static async Task KindAndIdentity(string root)
    {
        var handler = new Replay((_, _) => throw new Exception("Unexpected logo or foreign HTTP"));
        using var http = new HttpClient(handler); int fallback = 0;
        using var cache = new StoreLibraryArtworkCache(Path.Combine(root, "kind"), (_, _) => { Interlocked.Increment(ref fallback); return Task.FromResult<byte[]?>(Png); }, http);
        string path = await Resolve(cache, Game("legacy", kind: null));
        Check(handler.Urls.Count == 0, "missing cover kind never requests old logo URL");
        Check(fallback == 1 && File.Exists(path), "missing cover kind may use exact cover provider");
        var foreign = Game("foreign") with { ArtworkKey = "amazon:some-other-game" };
        Check(cache.Resolve(foreign, () => throw new Exception("Unexpected callback")) is null, "foreign artwork identity rejected synchronously");
        await Task.Delay(40);
        Check(handler.Urls.Count == 0 && fallback == 1, "foreign identity triggers no HTTP or fallback");
    }
    private static async Task SingleFlightAndPersistent(string root)
    {
        string directory = Path.Combine(root, "dedup"); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Replay(async (_, ct) => { await release.Task.WaitAsync(ct); return Bytes(Png); });
        using var http = new HttpClient(handler); var game = Game("dedup"); int changed = 0;
        using (var cache = new StoreLibraryArtworkCache(directory, httpClient: http))
        {
            for (int n = 0; n < 50; n++) cache.Resolve(game, () => Interlocked.Increment(ref changed));
            await Until(() => handler.Urls.Count == 1, "single in-flight HTTP");
            Check(handler.Urls.Count == 1, "50 concurrent resolves share one HTTP");
            release.SetResult(); await Until(() => Volatile.Read(ref changed) == 1, "dedup publish");
            string path = cache.Resolve(game, () => { })!;
            Check(path is not null && File.Exists(path) && changed == 1, "single-flight publishes once");
            Check(Directory.GetFiles(Path.Combine(directory, "store-library-artwork", "images")).Length == 1, "single persistent image");
            Check(Directory.GetFiles(Path.Combine(directory, "store-library-artwork"), "*.txt").Length == 1, "single persistent index");
        }
        using var restarted = new StoreLibraryArtworkCache(directory, httpClient: http);
        Check(restarted.Resolve(game, () => throw new Exception("Persistent callback")) is { } saved && File.Exists(saved), "restart reads persisted v2 image");
        Check(handler.Urls.Count == 1, "restart avoids all HTTP");
    }
    private static async Task Legacy(string root)
    {
        string directory = Path.Combine(root, "old"), store = Path.Combine(directory, "store-library-artwork");
        Directory.CreateDirectory(Path.Combine(store, "images")); var game = Game("old");
        string filename = new string('a', 64) + ".png", oldFile = Path.Combine(store, "images", filename);
        byte[] oldLogo = ImageBytes(Color.Red); File.WriteAllBytes(oldFile, oldLogo);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(game.ArtworkKey + "\0" + game.ArtworkUrl))).ToLowerInvariant();
        string index = Path.Combine(store, key + ".txt"); File.WriteAllText(index, filename);
        var handler = new Replay((_, _) => Task.FromResult(Bytes(Png))); using var http = new HttpClient(handler);
        using var cache = new StoreLibraryArtworkCache(directory, httpClient: http);
        string path = await Resolve(cache, game);
        Check(path != oldFile && handler.Urls.Count == 1, "v2 never adopts legacy cached logo");
        Check(File.ReadAllBytes(oldFile).SequenceEqual(oldLogo) && File.ReadAllText(index) == filename, "legacy foreign image and index preserved");
        Check(File.ReadAllBytes(path).SequenceEqual(Png), "v2 independently acquires correct cover");
    }
    private static async Task Cancel(string root)
    {
        string directory = Path.Combine(root, "cancel"); var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Replay(async (_, ct) => { try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { canceled.TrySetResult(); throw; } return Bytes(Png); });
        using var http = new HttpClient(handler); int changed = 0;
        var cache = new StoreLibraryArtworkCache(directory, httpClient: http);
        cache.Resolve(Game("cancel"), () => Interlocked.Increment(ref changed));
        await Until(() => handler.Urls.Count == 1, "cancel HTTP started"); cache.Dispose(); await canceled.Task.WaitAsync(TimeSpan.FromSeconds(6));
        Check(canceled.Task.IsCompletedSuccessfully, "dispose cancels in-flight HTTP token");
        await Task.Delay(80);
        Check(changed == 0 && (!Directory.Exists(directory) || Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length == 0), "cancel publishes no image/index/callback");
        Check(cache.Resolve(Game("after-dispose"), () => { }) is null && handler.Urls.Count == 1, "disposed cache never starts new work");
    }
    private static async Task Concurrency(string root)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int active = 0, maximum = 0, changed = 0;
        var handler = new Replay(async (_, ct) => { int current = Interlocked.Increment(ref active); int initial; do { initial = Volatile.Read(ref maximum); if (current <= initial) break; } while (Interlocked.CompareExchange(ref maximum, current, initial) != initial); try { await gate.Task.WaitAsync(ct); return Bytes(Png); } finally { Interlocked.Decrement(ref active); } });
        using var http = new HttpClient(handler); using var cache = new StoreLibraryArtworkCache(Path.Combine(root, "parallel"), httpClient: http);
        for (int n = 0; n < 9; n++) cache.Resolve(Game("parallel-" + n), () => Interlocked.Increment(ref changed));
        await Until(() => handler.Urls.Count >= 4, "parallel gate");
        Check(handler.Urls.Count == 4 && active == 4, "only four different covers acquire concurrently");
        gate.SetResult(); await Until(() => Volatile.Read(ref changed) == 9, "parallel all complete");
        Check(maximum == 4 && handler.Urls.Count == 9, "parallel bound remains four across all nine requests");
        Check(changed == 9 && active == 0, "all exact cover acquisitions finish once");
    }
    private sealed class Replay(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> replay) : HttpMessageHandler
    {
        internal ConcurrentQueue<string> Urls { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Urls.Enqueue(request.RequestUri!.AbsoluteUri); return replay(request, cancellationToken); }
    }
}
