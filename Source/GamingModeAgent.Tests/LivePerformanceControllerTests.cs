using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using GamingMode.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

internal static class LivePerformanceControllerTests
{
    internal static void Verify() => VerifyAsync().GetAwaiter().GetResult();
    private static async Task VerifyAsync()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "Playhub-LivePerformance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try {
            var store = new LivePerformanceSettingsStore(scratch);
            Require(store.Load() == new LivePerformanceOptions(), "A clean install has no active HUD or trace subscription");
            foreach (string color in new[] { "white", "yellow", "red", "green" }) {
                Require(store.Save(new(TextColor: color)) && store.Load().TextColor == color && store.Load().Hud.TextColor == color,
                    "Every supported foreground color persists through actual storage and renderer settings");
                var rgb = LivePerformanceHudPalette.Rgb(color);
                Require(rgb.Red + rgb.Green + rgb.Blue > 300, "Every selectable foreground remains bright, never black");
            }
            Require(!store.Save(new(TextColor: "black")) && !store.Save(new(TextColor: "#00ff00")), "Arbitrary or black text is not a supported palette setting");
            Require(store.Save(new(true, 4, 6, true, 50)), "Canonical settings save atomically");
            Require(store.Load() == new LivePerformanceOptions(true, 4, 6, true, 20), "Legacy cadence migrates while all visible options round trip without Steam/Windows writes");
            Require(JsonDocument.Parse(File.ReadAllText(Path.Combine(scratch, "performance-overlay.json")))
                .RootElement.GetProperty("refreshMs").GetInt32() == 20, "Saved settings contain only the provider's automatic minimum interval");
            foreach (int legacy in new[] { 50, 100 }) {
                File.WriteAllText(Path.Combine(scratch, "performance-overlay.json"),
                    $"{{\"enabled\":true,\"detail\":4,\"corner\":6,\"transparent\":true,\"refreshMs\":{legacy}}}");
                Require(store.Load() == new LivePerformanceOptions(true, 4, 6, true, 20),
                    "Loading an existing 50/100ms preference adopts real 20ms while preserving every visible option");
            }
            Require(!store.Save(new(true, 5)) && !store.Save(new(RefreshMs: 1)), "Invalid detail and fake refresh intervals are refused");
            Require(!Directory.EnumerateFiles(scratch, "*.tmp").Any(), "Atomic saves leave no staging file");
            File.WriteAllText(Path.Combine(scratch, "performance-overlay.json"), "{broken");
            Require(!store.Load().Enabled, "Malformed persisted settings fail closed");
            Require(store.Save(new()), "Reset fixture to a clean configuration");
            long now = 1000;
            var runtime = new Runtime();
            await using var controller = new LivePerformanceController(store, runtime, () => Volatile.Read(ref now));
            Require(controller.Read() is { Enabled: false, Status: "Off", Lease: null } && runtime.Starts == 0 && runtime.Resolves == 0,
                "Read/default-off does not resolve a game or start an idle collector");
            var started = await controller.ApplyAsync(new("configure", AppId: 42, Enabled: true, Detail: 3, RefreshMs: 50));
            string lease = started.State.Lease!;
            Require(started.Ok && Guid.TryParseExact(lease, "N", out _) && started.State.Status == "NoData",
                "An owned runtime lease starts in NoData, never an invented Ready");
            await Until(() => runtime.Starts == 1);
            Require(runtime.Interval == 20 && started.State.RefreshMs == 20 && runtime.UsageReads <= 1,
                "Continuous provider and readback adopt automatic 20ms while hardware remains independently paced");
            runtime.Emit(new(FrameMeasurementStatus.Ready, 10, 1000, 60, MeanFrameTimeMs: 16.67, ProcessId: 42, ProcessBirth: 700));
            Require(controller.Read() is { Status: "Ready", Sequence: 10, SampleAgeMs: 0 } && runtime.Published == 1,
                "Only actual incoming original-game frames establish Ready");
            using (var inBounds = new ManualResetEventSlim())
            using (var releaseBounds = new ManualResetEventSlim()) {
                runtime.Source!.BeforeBounds = () => { inBounds.Set(); Require(releaseBounds.Wait(2000), "Bounded callback barrier"); };
                var pending = Task.Run(() => runtime.Emit(new(FrameMeasurementStatus.Ready, 11, 1000, 60, MeanFrameTimeMs: 16.67)));
                Require(inBounds.Wait(2000), "Frame callback entered bounds check");
                await controller.ApplyAsync(new("renew", lease, Suspended: true));
                releaseBounds.Set(); await pending;
                runtime.Source.BeforeBounds = null;
                Require(runtime.Published == 1 && !runtime.RenderingEnabled,
                    "A queued pre-menu frame cannot resurrect the HUD after suspension has hidden it");
                await controller.ApplyAsync(new("renew", lease, Suspended: false));
            }
            Require(!(await controller.ApplyAsync(new("renew", Guid.NewGuid().ToString("N")))).Ok && runtime.Starts == 1,
                "Another client cannot renew or replace an active subscription");
            Require(!(await controller.ApplyAsync(new("configure", AppId: 43, Enabled: true))).Ok,
                "A configure without the owned lease cannot steal a running HUD");
            Require((await controller.ApplyAsync(new("renew", lease, Suspended: true))).Ok, "Opening a Steam menu suspends rendering under the same lease");
            int published = runtime.Published;
            runtime.Emit(new(FrameMeasurementStatus.Ready, 11, 1000, 60, MeanFrameTimeMs: 16.67));
            Require(controller.Read() is { Suspended: true, Status: "Suspended" } && runtime.Published == published && runtime.Starts == 1,
                "Menu suspension hides immediately and leaves the one provider running");
            await controller.ApplyAsync(new("renew", lease, Suspended: false));
            Require(controller.Read() is { Status: "NoData", Sequence: 0 }, "Closing the menu waits for fresh data instead of redrawing an old number");
            runtime.Emit(new(FrameMeasurementStatus.NoData, 0, 1200));
            Require(controller.Read() is { Status: "NoData", Sequence: 0, SampleAgeMs: null }, "Stale frames clear the measurement");
            Volatile.Write(ref now, 7001);
            Require(controller.Read() is { Status: "LeaseExpired", Lease: null }, "Expired identity is never advertised as a live lease");
            var expired = await controller.ApplyAsync(new("renew", lease));
            Require(!expired.Ok && expired.Error == "ExpiredLease" && runtime.Active == 0 && runtime.Source!.Disposed,
                "Lease expiry cancels the provider and disposes retained source before another launch");
            Require(controller.Read().Enabled, "Runtime expiration retains the user's display preference");
            var resumed = await controller.ApplyAsync(new("configure", AppId: 43, Enabled: true));
            await Until(() => runtime.Starts == 2);
            Require(resumed.Ok && resumed.State.Lease != lease && runtime.MaximumActive == 1, "Expired subscribers can start cleanly with no overlap");
            controller.Invalidate();
            await Until(() => runtime.Active == 0);
            Require(controller.Read().Lease is null, "Mode change/suspend revokes the exact runtime lease");
            var off = await controller.ApplyAsync(new("configure", Enabled: false));
            Require(off.Ok && off.State is { Enabled: false, Status: "Off", Lease: null } && runtime.Active == 0,
                "Off performs full cleanup and persists an off preference");
            int starts = runtime.Starts, usage = runtime.UsageReads;
            await Task.Delay(600);
            Require(runtime.Starts == starts && runtime.UsageReads == usage, "HUD off has no recurring provider or hardware sampling");

            var graphOnly = new Runtime();
            await using (var graph = new LivePerformanceController(new(Path.Combine(scratch, "graph-only")), graphOnly)) {
                var result = await graph.ApplyAsync(new("configure", AppId: 42, Enabled: true, Detail: 2, RefreshMs: 100));
                await Until(() => graphOnly.Starts == 1); await Task.Delay(320);
                Require(result.Ok && graphOnly.Interval == 20 && graphOnly.UsageReads == 0,
                    "FPS and graph use automatic cadence without polling unused CPU/GPU/RAM counters");
            }

            var unavailable = new Runtime { Available = false };
            await using (var failed = new LivePerformanceController(store, unavailable, () => now)) {
                var result = await failed.ApplyAsync(new("configure", AppId: 55, Enabled: true));
                Require(result.State.Status == "TargetUnavailable" && unavailable.Starts == 0, "Unresolved source never starts arbitrary ETW collection");
                unavailable.Available = true;
                await failed.ApplyAsync(new("renew", result.State.Lease));
                await Until(() => unavailable.Starts == 1);
                Require(unavailable.MaximumActive == 1, "A verified source becoming ready is resolved on renew without overlapping workers");
            }

            var uncertain = new Runtime { Terminal = FrameMeasurementStatus.CleanupFailed };
            await using (var blocked = new LivePerformanceController(store, uncertain, () => now)) {
                var result = await blocked.ApplyAsync(new("configure", AppId: 66, Enabled: true));
                await Until(() => uncertain.Starts == 1);
                await blocked.ApplyAsync(new("stop", result.State.Lease));
                var retry = await blocked.ApplyAsync(new("configure", AppId: 66, Enabled: true));
                Require(retry.State.Status == "CleanupFailed" && uncertain.Starts == 1, "Uncertain ETW cleanup blocks a new provider in the agent");
            }
            foreach (int corner in Enumerable.Range(1, 6)) {
                var position = new LivePerformanceOptions(Corner: corner).Hud.Position;
                Require(position is not HudPosition.Center, "Six explicit corner values never rely on incompatible enum ordinals");
            }
            await VerifyHttpAsync(Path.Combine(scratch, "http"));
        } finally {
            string full = Path.GetFullPath(scratch);
            if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(full).StartsWith("Playhub-LivePerformance-", StringComparison.Ordinal)) throw new InvalidOperationException("Fixture cleanup boundary");
            Directory.Delete(full, recursive: true);
        }
    }
    private static async Task VerifyHttpAsync(string scratch)
    {
        var runtime = new Runtime();
        await using var controller = new LivePerformanceController(new(scratch), runtime);
        var shell = new XboxShellBroker(scratch, () => true, () => throw new InvalidOperationException("HTTP fixture never starts Explorer"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        LivePerformanceEndpoints.Map(app, shell, controller);
        await app.StartAsync();
        try {
            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var unauthorized = await http.GetAsync("/session/live-performance");
            Require(unauthorized.StatusCode == HttpStatusCode.Unauthorized && runtime.Starts == 0, "Loopback without the shell secret cannot read or create a HUD lease");
            http.DefaultRequestHeaders.Add("X-Playhub-Shell-Token", File.ReadAllText(Path.Combine(scratch, "xbox-shell-token")));
            using (var state = JsonDocument.Parse(await http.GetStringAsync("/session/live-performance"))) {
                Require(state.RootElement.GetProperty("enabled").GetBoolean() == false
                    && state.RootElement.GetProperty("status").GetString() == "Off", "GET JSON uses the frontend's flat camelCase contract");
            }
            async Task<HttpResponseMessage> Post(string json) => await http.PostAsync("/session/live-performance", new StringContent(json, Encoding.UTF8, "application/json"));
            foreach (string json in new[] { "{\"action\":\"configure\",\"pid\":42}", "{\"action\":\"configure\",\"enabled\":true,\"enabled\":false}",
                "{\"action\":\"configure\",\"refreshMs\":1}", "{\"action\":\"configure\",\"enabled\":\"true\"}",
                "{\"action\":\"configure\",\"textColor\":\"black\"}",
                "{\"action\":\"renew\",\"appId\":42}", "{\"action\":\"configure\",\"lease\":\"" + new string('a', 4096) + "\"}" }) {
                using var response = await Post(json);
                Require(response.StatusCode == HttpStatusCode.BadRequest, "Malformed, duplicate, unbounded and arbitrary process requests are rejected before runtime access");
            }
            using var configured = await Post("{\"action\":\"configure\",\"enabled\":true,\"appId\":0,\"refreshMs\":20,\"textColor\":\"yellow\",\"suspended\":true}");
            using var body = JsonDocument.Parse(await configured.Content.ReadAsStringAsync());
            string lease = body.RootElement.GetProperty("state").GetProperty("lease").GetString()!;
            Require(body.RootElement.GetProperty("ok").GetBoolean() && runtime.Starts == 0 && runtime.Resolves == 0,
                "An idle configure saves options and returns an owned lease without enumerating games");
            Require(body.RootElement.GetProperty("state").GetProperty("refreshMs").GetInt32() == 20
                && body.RootElement.GetProperty("state").GetProperty("textColor").GetString() == "yellow",
                "The real HTTP boundary accepts the frontend's automatic 20ms request and confirms its actual stored color");
            using var renewed = await Post("{\"action\":\"renew\",\"lease\":\"" + lease + "\",\"suspended\":false}");
            Require(renewed.StatusCode == HttpStatusCode.OK, "Renew needs only the owned lease and optional menu suspension");
            using var stopped = await Post("{\"action\":\"stop\",\"lease\":\"" + lease + "\"}");
            Require(stopped.StatusCode == HttpStatusCode.OK && controller.Read() is { Enabled: true, Lease: null },
                "Runtime stop retains the saved preference and releases the subscription");
        } finally { await app.StopAsync(); }
    }
    private static async Task Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { if (clock.ElapsedMilliseconds > 2500) throw new InvalidOperationException("Bounded worker completion"); await Task.Delay(10); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Source : ILivePerformanceSource
    {
        public PresentMonTarget Target => new(42, 700);
        public bool Current => !Disposed;
        public bool Disposed;
        internal Action? BeforeBounds;
        public LivePerformanceHudForegroundProof? ForegroundProof => null;
        public bool TryGetBounds(out LivePerformanceHudBounds bounds) { BeforeBounds?.Invoke(); bounds = new(0, 0, 1920, 1080); return Current; }
        public void Dispose() => Disposed = true;
    }
    private sealed class Runtime : ILivePerformanceRuntime
    {
        internal int Starts, Resolves, UsageReads, Published, Interval, Active, MaximumActive;
        internal bool Available = true;
        internal bool RenderingEnabled;
        internal Source? Source;
        internal FrameMeasurementStatus Terminal = FrameMeasurementStatus.Canceled;
        private Action<LiveFrameTelemetry>? _publish;
        public Task<ILivePerformanceSource?> ResolveAsync(uint appId, CancellationToken cancellation)
        {
            Resolves++; Source = Available ? new() : null;
            return Task.FromResult<ILivePerformanceSource?>(Source);
        }
        public async Task<FrameMeasurementStatus> StreamAsync(PresentMonTarget target, int refreshMs,
            Action<LiveFrameTelemetry> publish, CancellationToken cancellation)
        {
            _publish = publish; Interval = refreshMs;
            Interlocked.Increment(ref Starts); int active = Interlocked.Increment(ref Active);
            MaximumActive = Math.Max(MaximumActive, active);
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            catch (OperationCanceledException) { }
            finally { Interlocked.Decrement(ref Active); }
            return Terminal;
        }
        internal void Emit(LiveFrameTelemetry value) => _publish!(value);
        public void Configure(LivePerformanceHudSettings settings) => RenderingEnabled = settings.Enabled;
        public void Publish(LiveFrameTelemetry sample, LivePerformanceHudUsage? usage, LivePerformanceHudBounds bounds,
            LivePerformanceHudForegroundProof? foreground) { if (RenderingEnabled) Published++; }
        public void Hide() { }
        public LivePerformanceHudUsage? ReadUsage() { UsageReads++; return new(CpuPercent: 4); }
        public void Dispose() { }
    }
}
