using System.Diagnostics;
using System.Reflection;
using GamingMode.Services;

var failures = new List<string>();
int passed = 0;
await Check("OS event bursts coalesce into one wake-up", async () =>
{
    var signal = new BackgroundWorkSignal();
    for (int i = 0; i < 1000; i++) signal.Notify();
    Equal(true, await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    Equal(false, await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
});
await Check("OS watcher is asleep until event or fallback", async () =>
{
    var signal = new BackgroundWorkSignal();
    Task<bool> wait = signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    await Task.Delay(50);
    Equal(false, wait.IsCompleted);
    signal.Notify();
    Equal(true, await wait.WaitAsync(TimeSpan.FromSeconds(1)));
});
await Check("OS watcher cancellation stops a long wait", async () =>
{
    using var cancel = new CancellationTokenSource();
    Task<bool> wait = new BackgroundWorkSignal().WaitAsync(TimeSpan.FromSeconds(30), cancel.Token);
    cancel.Cancel();
    try { await wait; throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { }
});
await Check("Haptics startup, blocked input and dispose never initialize SDL", () =>
{
    using var current = Process.GetCurrentProcess();
    HashSet<string> before = current.Modules.Cast<ProcessModule>().Select(m => m.ModuleName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    using (var haptics = new ControllerHapticsService(new FileLogger(Path.Combine(Path.GetTempPath(), "Playhub-haptics-check.log"))))
    {
        Equal("already-stopped", haptics.Stop().Path);
        Equal("steam-ui-only", haptics.Pulse(0, 0x045e, 0x028e, 0, 1, 20).Path);
    }
    current.Refresh();
    foreach (ProcessModule module in current.Modules)
        if (module.ModuleName.Contains("SDL", StringComparison.OrdinalIgnoreCase) && !before.Contains(module.ModuleName))
            throw new Exception("Loaded controller library while idle: " + module.ModuleName);
    Equal(false, typeof(ControllerHapticsService).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
        .Any(f => f.FieldType.Name.Contains("Sdl", StringComparison.OrdinalIgnoreCase)));
    return Task.CompletedTask;
});
await Check("No volume observer thread without an indicator", () =>
{
    using var volume = new SystemVolumeKeyService(new FileLogger(Path.Combine(Path.GetTempPath(), "Playhub-volume-check.log")));
    typeof(SystemVolumeKeyService).GetMethod("StartVolumeWatcher", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(volume, null);
    Equal<object?>(null, typeof(SystemVolumeKeyService).GetField("_watcherThread", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(volume));
    return Task.CompletedTask;
});

string root = Path.Combine(Path.GetTempPath(), "Playhub-active-log-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "logs"));
string log = Path.Combine(root, "logs", "gameprocess_log.txt");
using var self = Process.GetCurrentProcess();
string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
string added = $"[{timestamp}] AppID 123 adding PID {self.Id}\n";
MethodInfo find = typeof(OverlaySteamArtworkResolver).GetMethod("FindActiveGame", BindingFlags.NonPublic | BindingFlags.Static)!;
object? Find() => find.Invoke(null, [root, self.Id]);
try
{
    await Check("Tracked live process resolves from Steam log", () =>
    {
        File.WriteAllText(log, added);
        True(Find() is not null);
        return Task.CompletedTask;
    });
    await Check("Window batch uses cache while log is locked", () =>
    {
        using var locked = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        for (int i = 0; i < 100; i++) True(Find() is not null);
        return Task.CompletedTask;
    });
    await Check("Game removal invalidates shared cache promptly", async () =>
    {
        File.AppendAllText(log, $"[{timestamp}] AppID 123 no longer tracking PID {self.Id}\n");
        await Task.Delay(550);
        Equal<object?>(null, Find());
    });
    await Check("Old tracking cannot attach a recycled PID", async () =>
    {
        File.WriteAllText(log, $"[2000-01-01 00:00:00] AppID 123 adding PID {self.Id}\n");
        await Task.Delay(550);
        Equal<object?>(null, Find());
    });
    await Check("Steam restart drops earlier session tracking", async () =>
    {
        File.WriteAllText(log, added + "Client version: fixture\n");
        await Task.Delay(550);
        Equal<object?>(null, Find());
    });
    await Check("Idle windows do not trigger process-tree snapshots", () =>
    {
        FieldInfo tree = typeof(OverlaySteamArtworkResolver).GetField("_parentsCheckedAt", BindingFlags.NonPublic | BindingFlags.Static)!;
        long before = (long)tree.GetValue(null)!;
        long bytes = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < 100; i++) Equal<object?>(null, Find());
        long allocated = GC.GetTotalAllocatedBytes(true) - bytes;
        Equal(before, (long)tree.GetValue(null)!);
        True(allocated < 2_000_000); // Old code allocated ~644 MB for this batch.
        Console.WriteLine($"METRIC cached idle 100 lookups allocated {allocated} bytes");
        return Task.CompletedTask;
    });
    await Check("Removed log cannot retain a game identity", async () =>
    {
        File.Delete(log);
        await Task.Delay(550);
        Equal<object?>(null, Find());
    });
}
finally
{
    // This uniquely created fixture contains only our own single log.
    if (File.Exists(log)) File.Delete(log);
    Directory.Delete(Path.Combine(root, "logs"));
    Directory.Delete(root);
}
Console.WriteLine($"{passed} passed; {failures.Count} failed");
foreach (string failure in failures) Console.Error.WriteLine(failure);
Environment.ExitCode = failures.Count > 0 ? 1 : 0;

async Task Check(string name, Func<Task> run)
{
    try { await run(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures.Add("FAIL " + name + ": " + e); }
}
static void True(bool value) { if (!value) throw new Exception("Expected true"); }
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}");
}
