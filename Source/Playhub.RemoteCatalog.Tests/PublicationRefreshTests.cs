using Playhub.Models;
using Playhub.Services;
using System.Net;

internal static class PublicationRefreshTests
{
    internal static async Task RunAsync()
    {
        await UnwritableCacheAsync(newerDate: false);
        await UnwritableCacheAsync(newerDate: true);
        await FailedRequestIsThrottledAsync();
        await CancellationReleasesLeaseAsync();
        Console.WriteLine("PASS 10 publication HTTP assertions: unwritable cache, unchanged/changed metadata, finite scans, retry throttling and cancellation lease.");
    }

    private static async Task UnwritableCacheAsync(bool newerDate)
    {
        using var fixture = new PublicationFixture();
        fixture.Handler.Published = newerDate ? "2025-10-19T23:30:00Z" : fixture.Plugin.UpdatedAt;
        var callbacks = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await fixture.Service.RefreshPublicationDatesAsync(new[] { fixture.Plugin }, () => callbacks++, deadline.Token);
        IntegrationTests.Check(fixture.Handler.AtomCalls == 1 && fixture.Handler.PageCalls == 1 && !deadline.IsCancellationRequested,
            "An unwritable release cache caused publication discovery to repeat instead of finishing one scan.");
        IntegrationTests.Check(callbacks == (newerDate ? 1 : 0),
            "Publication discovery emitted a callback for unchanged visible metadata or lost a genuine newer publication.");
        IntegrationTests.Check(Directory.Exists(fixture.Blocker) && !File.Exists(fixture.Blocker) && fixture.Plugin.UpdatedAt == "2025-10-18T23:30:00Z",
            "The cache failure fixture was bypassed or publication discovery mutated the UI-owned model.");
        await fixture.Service.RefreshPublicationDatesAsync(new[] { fixture.Plugin }, () => callbacks++, deadline.Token);
        IntegrationTests.Check(fixture.Handler.AtomCalls == 1 && fixture.Handler.PageCalls == 1 && callbacks == (newerDate ? 1 : 0),
            "A successful source without a durable cache was fetched or published again on the next scan.");
    }

    private static async Task FailedRequestIsThrottledAsync()
    {
        using var fixture = new PublicationFixture();
        fixture.Handler.FailAtom = true;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var callbacks = 0;
        await fixture.Service.RefreshPublicationDatesAsync(new[] { fixture.Plugin }, () => callbacks++, deadline.Token);
        await fixture.Service.RefreshPublicationDatesAsync(new[] { fixture.Plugin }, () => callbacks++, deadline.Token);
        IntegrationTests.Check(fixture.Handler.AtomCalls == 1 && fixture.Handler.PageCalls == 0 && callbacks == 0,
            "Failed publication requests were immediately retried despite their in-memory retry lease.");
    }

    private static async Task CancellationReleasesLeaseAsync()
    {
        using var fixture = new PublicationFixture();
        fixture.Handler.BlockAtom = true;
        using var cancellation = new CancellationTokenSource();
        var callbacks = 0;
        var pending = fixture.Service.RefreshPublicationDatesAsync(new[] { fixture.Plugin }, () => callbacks++, cancellation.Token);
        await fixture.Handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        try
        {
            await pending.WaitAsync(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException("Publication scan swallowed its caller cancellation.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        fixture.Handler.BlockAtom = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await fixture.Service.RefreshPublicationDatesAsync(new[] { fixture.Plugin }, () => callbacks++, deadline.Token);
        IntegrationTests.Check(fixture.Handler.AtomCalls == 2 && fixture.Handler.PageCalls == 1 && callbacks == 0,
            "A cancelled source retained its retry lease and could not resume on the next Store visit.");
    }

    private sealed class PublicationFixture : IDisposable
    {
        internal readonly DeckyPluginInfo Plugin;
        internal readonly PublicationHandler Handler;
        private readonly HttpClient _http;
        internal readonly PluginCatalogService Service;
        internal readonly string Blocker;

        internal PublicationFixture()
        {
            var repository = "publication-fixture/Cache" + Guid.NewGuid().ToString("N");
            Plugin = new DeckyPluginInfo { Name = "Publication cache fixture", RepositorySlug = repository,
                RepositoryUrl = "https://github.com/" + repository, CatalogSource = "outside-store", Version = "1.0",
                FirstPublishedAt = "2020-01-01T00:00:00Z", UpdatedAt = "2025-10-18T23:30:00Z" };
            Handler = new PublicationHandler(repository, Plugin.UpdatedAt);
            _http = new HttpClient(Handler); Service = new PluginCatalogService(_http);
            // This is the exact production cache-file path beneath the harness-only AppPaths.
            Blocker = Path.Combine(AppPaths.LocalDataRoot, "cache", "plugin-releases", repository.Replace('/', '-') + ".json");
            Directory.CreateDirectory(Blocker);
        }

        public void Dispose()
        {
            _http.Dispose();
            // Delete only this owned, empty directory; never recurse into the shared harness cache.
            Directory.Delete(Blocker);
        }
    }

    private sealed class PublicationHandler(string repository, string published) : HttpMessageHandler
    {
        internal int AtomCalls, PageCalls;
        internal bool FailAtom, BlockAtom;
        internal string Published = published;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri == "https://github.com/" + repository + "/releases.atom")
            {
                Interlocked.Increment(ref AtomCalls); Entered.TrySetResult();
                if (BlockAtom) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                if (FailAtom) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><link rel=\"alternate\" href=\"https://github.com/" +
                    repository + "/releases/tag/v1\"/></entry></feed>") };
            }
            if (request.RequestUri?.AbsoluteUri == "https://github.com/" + repository + "/releases/tag/v1")
            {
                Interlocked.Increment(ref PageCalls);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "<div>released this <relative-time datetime=\"" + Published + "\"></relative-time></div>") };
            }
            throw new InvalidOperationException("Unexpected fake publication HTTP request: " + request.RequestUri);
        }
    }
}
