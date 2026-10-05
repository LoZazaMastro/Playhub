using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Playhub.Integrations;

public sealed record ApplicationMediaRequest(uint AppId, string Category, string VideoId, int Quality = 1080, bool NormalizeAudio = false, bool UpmixAudio = false, string? StableIdentity = null);

/// <summary>App acquisition jobs; plugin installation and delivery are handled by the caller.</summary>
public sealed class ApplicationMediaAcquisition : IAsyncDisposable
{
    private readonly string _root;
    private readonly Func<IntegrationToolCommand, CancellationToken, Task<IntegrationToolResult>> _run;
    private readonly Func<string, string> _resolve;
    private readonly SemaphoreSlim _workers = new(2, 2);
    private readonly Dictionary<string, (SemaphoreSlim Semaphore, int Users)> _contentLocks = new();
    private readonly Dictionary<string, Job> _jobs = new();
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private sealed record ProbeSummary(int Width, int Height, double Duration);
    private sealed class Job(string id, ApplicationMediaRequest request)
    {
        public readonly string Id = id;
        public readonly ApplicationMediaRequest Request = request;
        public readonly CancellationTokenSource Cancellation = new();
        public Task Work = Task.CompletedTask;
        public string Status = "queued", Path = "", Error = "";
        public ProbeSummary? Verified;
    }

    public ApplicationMediaAcquisition(string dataRoot, string toolsRoot)
    {
        _root = Path.GetFullPath(dataRoot);
        var runner = new ApplicationToolRunner(toolsRoot, _root);
        _run = runner.RunAsync; _resolve = runner.Resolve;
    }
    public ApplicationMediaAcquisition(string dataRoot, Func<string, string> resolve,
        Func<IntegrationToolCommand, CancellationToken, Task<IntegrationToolResult>> run)
    { _root = Path.GetFullPath(dataRoot); _resolve = resolve; _run = run; }

    public JsonObject Start(ApplicationMediaRequest request)
    {
        Validate(request);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_jobs.Values.Count(job => job.Status is "queued" or "running") >= 4) throw new InvalidOperationException("Wait for current media jobs to finish.");
            if (_jobs.Count >= 64)
                foreach (var old in _jobs.Values.Where(job => job.Work.IsCompleted).Take(32).ToArray()) { _jobs.Remove(old.Id); old.Cancellation.Dispose(); }
            var job = new Job(Guid.NewGuid().ToString("N"), request);
            _jobs.Add(job.Id, job);
            job.Work = Task.Run(() => AcquireAsync(job));
            return Snapshot(job);
        }
    }
    public JsonObject Get(string id) { lock (_gate) return Snapshot(Find(id)); }
    public JsonObject Cancel(string id) { lock (_gate) { var job = Find(id); job.Cancellation.Cancel(); return Snapshot(job); } }
    public Task WaitAsync(string id) { lock (_gate) return Find(id).Work; }
    private Job Find(string id) => _jobs.TryGetValue(id, out var job) ? job : throw new ArgumentException("Unknown app media job.");
    private static JsonObject Snapshot(Job job) => new()
    {
        ["jobId"] = job.Id, ["ok"] = job.Status != "failed", ["status"] = job.Status,
        ["path"] = job.Path, ["filename"] = Path.GetFileName(job.Path), ["error"] = job.Error,
        ["acquired"] = job.Status == "done", ["delivered"] = false,
        ["normalized"] = job.Request.NormalizeAudio, ["upmix"] = job.Request.UpmixAudio,
        ["upmixConsumerOption"] = job.Request.UpmixAudio, ["upmixEncoded"] = false,
        ["actualWidth"] = job.Verified?.Width, ["actualHeight"] = job.Verified?.Height,
        ["width"] = job.Verified?.Width, ["duration"] = job.Verified?.Duration
    };
    private static void Validate(ApplicationMediaRequest request)
    {
        if (request.StableIdentity is not null) ApplicationIntegrationIdentity.Validate(request.StableIdentity);
        if ((request.AppId == 0 && request.StableIdentity is null) || request.Category is not ("themedeck" or "trailerhero") ||
            !Regex.IsMatch(request.VideoId ?? "", "^[A-Za-z0-9_-]{11}$") || request.Quality is not (720 or 1080 or 1440 or 2160))
            throw new ArgumentException("Select a valid game and YouTube media item.");
        if (request.Category == "trailerhero" && (request.NormalizeAudio || request.UpmixAudio)) throw new ArgumentException("Audio preferences apply only to music.");
    }

    private async Task AcquireAsync(Job job)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, job.Cancellation.Token);
        limit.CancelAfter(TimeSpan.FromMinutes(20));
        bool acquired = false, locked = false;
        string pending = Path.Combine(_root, "pending", job.Id);
        var request = job.Request;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.Category}/{request.VideoId}/{request.Quality}/{request.NormalizeAudio}")));
        SemaphoreSlim contentLock;
        lock (_gate)
        {
            var entry = _contentLocks.TryGetValue(key, out var prior) ? prior : (new SemaphoreSlim(1, 1), 0);
            contentLock = entry.Item1;
            _contentLocks[key] = (contentLock, entry.Item2 + 1);
        }
        try
        {
            await _workers.WaitAsync(limit.Token); acquired = true;
            await contentLock.WaitAsync(limit.Token); locked = true;
            lock (_gate) job.Status = "running";
            string destination = Path.Combine(_root, "media", request.Category, key + (request.Category == "themedeck" ? ".m4a" : ".mp4"));
            ApplicationIntegrationDataStore.GuardPath(destination);
            ApplicationIntegrationDataStore.GuardPath(pending);
            Directory.CreateDirectory(pending);
            ProbeSummary verified;
            if (ValidMedia(destination))
            {
                _resolve("ffprobe.exe");
                verified = await ProbeAsync(destination, request, pending, limit.Token);
            }
            else
            {
                _resolve("yt-dlp.exe");
                string ffmpeg = _resolve("ffmpeg.exe");
                _resolve("ffprobe.exe");
                var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-playlist", "--no-warnings", "--no-progress", "--no-js-runtimes", "--ffmpeg-location", Path.GetDirectoryName(ffmpeg)!, "--socket-timeout", "20", "--retries", "2", "--output", Path.Combine(pending, "payload.%(ext)s"), "--match-filter", "duration <= 900" };
                try { args.AddRange(["--js-runtimes", "node:" + _resolve("node.exe")]); } catch (FileNotFoundException) { }
                if (request.Category == "themedeck") args.AddRange(["--format", "bestaudio", "--extract-audio", "--audio-format", "m4a", "--audio-quality", "0", "--max-filesize", "100M"]);
                else args.AddRange(["--format", $"bestvideo[height<={request.Quality}][vcodec^=avc1]+bestaudio[acodec^=mp4a]/best[height<={request.Quality}][ext=mp4]", "--merge-output-format", "mp4", "--max-filesize", "800M"]);
                args.AddRange(["--", "https://www.youtube.com/watch?v=" + request.VideoId]);
                var result = await _run(new("yt-dlp.exe", args, pending, TimeSpan.FromMinutes(15)), limit.Token);
                if (result.ExitCode != 0) throw new IOException("Media download did not complete.");
                string output = Path.Combine(pending, request.Category == "themedeck" ? "payload.m4a" : "payload.mp4");
                if (!ValidMedia(output)) throw new IOException("The download produced no valid media file.");
                if (request.Category == "themedeck" && request.NormalizeAudio)
                {
                    string normalized = Path.Combine(pending, "normalized.m4a");
                    var processing = await _run(new("ffmpeg.exe", ["-hide_banner", "-nostdin", "-y", "-i", output, "-vn", "-sn", "-dn", "-af", "loudnorm=I=-16:TP=-1.5:LRA=11", "-c:a", "aac", "-b:a", "384k", "-ar", "48000", normalized], pending, TimeSpan.FromMinutes(5)), limit.Token);
                    if (processing.ExitCode != 0 || !ValidMedia(normalized)) throw new IOException("Audio normalization did not complete.");
                    output = normalized;
                }
                verified = await ProbeAsync(output, request, pending, limit.Token);
                limit.Token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                ApplicationIntegrationDataStore.GuardPath(destination);
                File.Move(output, destination, overwrite: true);
            }
            lock (_gate) { job.Path = destination; job.Verified = verified; job.Status = "done"; }
        }
        catch (OperationCanceledException) { lock (_gate) { job.Status = "cancelled"; job.Error = "Acquisition cancelled; no plugin assignment was made."; } }
        catch (Exception error) { lock (_gate) { job.Status = "failed"; job.Error = error is FileNotFoundException ? "Playhub media tools are unavailable." : error.Message; } }
        finally
        {
            if (locked) contentLock.Release();
            lock (_gate)
            {
                var entry = _contentLocks[key];
                if (entry.Users == 1) { _contentLocks.Remove(key); entry.Semaphore.Dispose(); }
                else _contentLocks[key] = (entry.Semaphore, entry.Users - 1);
            }
            if (acquired) _workers.Release();
            DeletePending(pending);
        }
    }
    private async Task<ProbeSummary> ProbeAsync(string path, ApplicationMediaRequest request, string workspace, CancellationToken ct)
    {
        var probe = await _run(new("ffprobe.exe", ["-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height", "-of", "json", path], workspace, TimeSpan.FromSeconds(20)), ct);
        var data = probe.ExitCode == 0 ? JsonNode.Parse(probe.Output) as JsonObject : null;
        if (!double.TryParse(data?["format"]?["duration"]?.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double duration) || !double.IsFinite(duration) || duration is <= 0 or > 900)
            throw new IOException("The downloaded media has no supported duration.");
        var streams = (data?["streams"] as JsonArray ?? new()).OfType<JsonObject>();
        bool supported = request.Category == "themedeck"
            ? streams.Any(stream => stream["codec_type"]?.ToString() == "audio" && stream["codec_name"]?.ToString() == "aac")
            : streams.Any(stream => stream["codec_type"]?.ToString() == "video" && stream["codec_name"]?.ToString() == "h264" &&
                stream["height"] is JsonValue height && height.TryGetValue<int>(out int pixels) && pixels > 0 && pixels <= request.Quality);
        if (request.Category == "trailerhero" && streams.Any(stream => stream["codec_type"]?.ToString() == "audio" && stream["codec_name"]?.ToString() != "aac")) supported = false;
        if (!supported) throw new IOException("The downloaded media is not compatible with Steam playback.");
        var video = streams.FirstOrDefault(stream => stream["codec_type"]?.ToString() == "video" && stream["codec_name"]?.ToString() == "h264");
        if (request.Category == "trailerhero")
        {
            if (video?["width"] is not JsonValue width || !width.TryGetValue<int>(out int pixels) || pixels is <= 0 or > 8192)
                throw new IOException("The downloaded video has no supported width.");
            return new(pixels, video["height"]!.GetValue<int>(), duration);
        }
        return new(0, 0, duration);
    }
    private static bool ValidMedia(string path)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path); Span<byte> header = stackalloc byte[12];
        return stream.Length > 12 && stream.Read(header) == 12 && header[4..8].SequenceEqual("ftyp"u8);
    }
    private void DeletePending(string path)
    {
        string root = Path.GetFullPath(Path.Combine(_root, "pending")) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(path);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(target)) return;
        try
        {
            ApplicationIntegrationDataStore.GuardPath(target);
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) return;
            if (Directory.EnumerateFileSystemEntries(target, "*", SearchOption.AllDirectories).Any(item => (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)) return;
            Directory.Delete(target, recursive: true);
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public async ValueTask DisposeAsync()
    {
        Task[] work;
        lock (_gate) { if (_disposed) return; _disposed = true; _lifetime.Cancel(); work = _jobs.Values.Select(job => job.Work).ToArray(); }
        await Task.WhenAll(work);
        lock (_gate) { foreach (var job in _jobs.Values) job.Cancellation.Dispose(); _jobs.Clear(); }
        foreach (var entry in _contentLocks.Values) entry.Semaphore.Dispose();
        _workers.Dispose(); _lifetime.Dispose();
    }
}
