using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Playhub.Integrations;

public sealed class ApplicationMediaSearch : IAsyncDisposable
{
    private readonly string _root;
    private readonly Func<IntegrationToolCommand, CancellationToken, Task<IntegrationToolResult>> _run;
    private readonly SemaphoreSlim _workers = new(2, 2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly HashSet<Task> _active = new();
    private bool _disposed;
    public ApplicationMediaSearch(string dataRoot, string toolsRoot)
    { _root = Path.GetFullPath(dataRoot); _run = new ApplicationToolRunner(toolsRoot, _root).RunAsync; }
    public ApplicationMediaSearch(string dataRoot, Func<IntegrationToolCommand, CancellationToken, Task<IntegrationToolResult>> run)
    { _root = Path.GetFullPath(dataRoot); _run = run; }
    public async Task<JsonArray> SearchAsync(string title, string category, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 512 || category is not ("themedeck" or "trailerhero")) throw new ArgumentException("Select a game and media category.");
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active.Count >= 4) throw new InvalidOperationException("Wait for current media searches to finish.");
            _active.Add(complete.Task);
        }
        string work = Path.Combine(_root, "search", Guid.NewGuid().ToString("N")); bool held = false;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token); limit.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            await _workers.WaitAsync(limit.Token); held = true;
            ApplicationIntegrationDataStore.GuardPath(work); Directory.CreateDirectory(work);
            string query = title + (category == "themedeck" ? " game soundtrack theme" : " official game trailer");
            var result = await _run(new("yt-dlp.exe", ["--ignore-config", "--no-plugin-dirs", "--no-remote-components", "--no-js-runtimes", "--flat-playlist", "--skip-download", "--dump-json", "--", "ytsearch20:" + query], work, TimeSpan.FromSeconds(40)), limit.Token);
            if (result.ExitCode != 0) throw new IOException("YouTube search did not complete.");
            var items = new JsonArray();
            foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var item = JsonNode.Parse(line) as JsonObject; string id = item?["id"]?.ToString() ?? "";
                if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") || item?["title"]?.ToString() is not string name || name.Length == 0) continue;
                double duration = double.TryParse(item["duration"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
                if (duration > 900 || duration < 0 || !double.IsFinite(duration)) continue;
                items.Add(new JsonObject { ["id"] = id, ["video_id"] = id, ["title"] = name, ["duration"] = duration,
                    ["thumbnail"] = "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg", ["url"] = "https://www.youtube.com/watch?v=" + id,
                    ["channel"] = item["channel"]?.DeepClone() ?? item["uploader"]?.DeepClone() });
                if (items.Count == 20) break;
            }
            return items;
        }
        finally
        {
            if (held) _workers.Release();
            try { ApplicationIntegrationDataStore.GuardPath(work); if (Directory.Exists(work) && !Directory.EnumerateFileSystemEntries(work).Any()) Directory.Delete(work); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            lock (_gate) { _active.Remove(complete.Task); complete.TrySetResult(); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        Task[] active;
        lock (_gate) { if (_disposed) return; _disposed = true; _lifetime.Cancel(); active = _active.ToArray(); }
        await Task.WhenAll(active); _workers.Dispose(); _lifetime.Dispose();
    }
}
