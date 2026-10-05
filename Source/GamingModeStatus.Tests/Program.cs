using System.Diagnostics;
using System.Reflection;
using GamingMode.Models;
using GamingMode.Services;

MethodInfo capture = typeof(ModeManager).Assembly.GetType("GamingMode.Services.ModeStatusProcessSnapshot")!
    .GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Static)!;

ProcessState[] Read<T>(Func<T[]> enumerate, Func<T, int> id, Func<T, string> name,
    Func<T, string?> path, Action<T> dispose) => (ProcessState[])capture.MakeGenericMethod(typeof(T))
        .Invoke(null, new object[] { enumerate, id, name, path, dispose })!;

void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }

int enumerations = 0, pathReads = 0;
var fixtures = new[] {
    new Fixture(9, "steamwebhelper", "wrong"), new Fixture(6, "PLUGINLOADER_noconsole", "secondary"),
    new Fixture(4, "PluginLoader", "preferred"), new Fixture(8, "STEAM", "steam-path"),
    new Fixture(7, "apollo", "apollo-path"), new Fixture(3, "sunshine", null),
    new Fixture(2, "vibepollo", "vibepollo-path"), new Fixture(11, "vibeshine", "vibeshine-path"),
    new Fixture(8, "steam", "duplicate"), new Fixture(1, "explorer", "explorer-path"),
    new Fixture(12, "exited", null) { IdentityFails = true }, new Fixture(13, "steam-helper", "wrong")
};
ProcessState[] states = Read(() => { enumerations++; return fixtures; }, process => process.Id,
    process => process.IdentityFails ? throw new InvalidOperationException("Exited") : process.Name,
    process => { pathReads++; if (process.Name == "sunshine") throw new InvalidOperationException("Denied"); return process.Path; },
    process => process.Disposals++);
Check(enumerations == 1, "Only one global enumeration");
Check(states[0].ProcessIds.SequenceEqual(new[] { 8 }) && states[0].Path == "steam-path", "Exact Steam match and deduplication");
Check(states[1].ProcessIds.SequenceEqual(new[] { 4, 6 }) && states[1].Path == "preferred", "Decky aliases preserve path priority");
Check(states[2].ProcessIds.SequenceEqual(new[] { 2, 3, 7, 11 }) && states[2].Path == "apollo-path", "Sunshine fallback aliases and inaccessible paths");
Check(states[3].ProcessIds.SequenceEqual(new[] { 1 }) && states[3].Path == "explorer-path", "Explorer identity");
Check(pathReads == 5, "Only read paths until first successful path per group");
Check(fixtures.All(process => process.Disposals == 1), "Dispose matched, unrelated, duplicate and exited processes");
Console.WriteLine("PASS single snapshot, exact aliases, sorted IDs, paths and disposal counters");

states = Read(() => Array.Empty<Fixture>(), process => process.Id, process => process.Name,
    process => process.Path, process => process.Disposals++);
Check(states.Length == 4 && states.All(state => !state.Running && state.ProcessIds.Length == 0 && state.Path == null), "Empty status");
var next = new Fixture(23, "steam", "new-steam");
states = Read(() => new[] { next }, process => process.Id, process => process.Name,
    process => process.Path, process => process.Disposals++);
Check(states[0].ProcessIds.SequenceEqual(new[] { 23 }) && next.Disposals == 1, "Next response sees new process immediately");
Console.WriteLine("PASS empty state and immediate next-response process change");

ProcessState[] New() => Read(Process.GetProcesses, process => process.Id, process => process.ProcessName,
    process => process.MainModule?.FileName, process => process.Dispose());
ProcessState[] Old()
{
    string[][] aliases = { new[] { "steam" }, new[] { "PluginLoader", "PluginLoader_noconsole" },
        new[] { "sunshine", "apollo", "vibepollo", "vibeshine" }, new[] { "explorer" } };
    return aliases.Select(group => {
        Process[] processes = group.SelectMany(Process.GetProcessesByName).DistinctBy(process => process.Id).ToArray();
        try {
            string? path = processes.Select(process => { try { return process.MainModule?.FileName; } catch { return null; } })
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            return new ProcessState { Running = processes.Length > 0, ProcessIds = processes.Select(process => process.Id).Order().ToArray(), Path = path };
        } finally { foreach (Process process in processes) process.Dispose(); }
    }).ToArray();
}
// Both variants execute read-only process queries. The baseline additionally
// disposes its matches so the comparison cannot accumulate benchmark handles.
for (int i = 0; i < 3; i++) { Old(); New(); }
using Process own = Process.GetCurrentProcess();
(double Wall, double Cpu) Measure(Func<ProcessState[]> read) {
    double initial = own.TotalProcessorTime.TotalMilliseconds;
    var timer = Stopwatch.StartNew();
    for (int i = 0; i < 40; i++) read();
    return (timer.Elapsed.TotalMilliseconds / 40, (own.TotalProcessorTime.TotalMilliseconds - initial) / 40);
}
var old = Measure(Old);
var current = Measure(New);
Console.WriteLine($"BENCH 40 responses each: baseline wall={old.Wall:F3} ms CPU={old.Cpu:F3} ms; snapshot wall={current.Wall:F3} ms CPU={current.Cpu:F3} ms");
Console.WriteLine("No response cache; every request observes a fresh process snapshot.");

sealed class Fixture(int id, string name, string? path)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string? Path { get; } = path;
    public bool IdentityFails { get; init; }
    public int Disposals { get; set; }
}
