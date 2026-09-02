using System.Diagnostics;
using System.Text.Json;
using Microsoft.Windows.AppLifecycle;
using Playhub.Models;
using Playhub.Services;

internal static class RestartProcesses
{
    private const string KeyPrefix = "Playhub.LanguageSettings.Tests.";

    public static async Task<int> RunChildAsync(string[] args)
    {
        if (args.Length < 3 || !args[1].StartsWith(KeyPrefix, StringComparison.Ordinal))
            throw new ArgumentException("Only isolated test instance keys are accepted");
        if (!Path.GetFileName(Environment.ProcessPath!).Equals("Playhub.LanguageSettings.Tests.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native restart is restricted to the test executable");

        var role = args[0];
        var key = args[1];
        var root = args[2];
        WinRT.ComWrappersSupport.InitializeComWrappers();
        using var service = new SingleInstanceService();
        var primary = await service.RegisterAsync(key, (_, _) => Console.WriteLine("ACTIVATED"));
        if (role == "probe")
        {
            Console.WriteLine(primary ? "PRIMARY" : "REDIRECTED");
            return primary ? 1 : 0;
        }
        if (role == "restarted")
        {
            var settings = JsonSerializer.Deserialize<PlayhubSettings>(File.ReadAllText(Path.Combine(root, "settings.json")))!;
            var oldAlive = false;
            try { using var old = Process.GetProcessById(int.Parse(args[4])); oldAlive = !old.HasExited; }
            catch (ArgumentException) { }
            await AppPaths.WriteAtomicAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
            {
                Primary = primary, OldAlive = oldAlive, settings.Language, ExpectedLanguage = args[3], ProcessId = Environment.ProcessId
            }));
            return primary && !oldAlive && settings.Language == args[3] ? 0 : 1;
        }
        if (role != "owner" || args.Length != 5 || !primary) return 1;
        await AppPaths.WriteAtomicAsync(Path.Combine(root, "settings.json"), JsonSerializer.Serialize(new PlayhubSettings { Language = args[3] }));
        Console.WriteLine("PRIMARY");
        if (await Console.In.ReadLineAsync() != "RESTART") return 0;
        await AppPaths.WriteAtomicAsync(Path.Combine(root, "settings.json"), JsonSerializer.Serialize(new PlayhubSettings { Language = args[4] }));
        var failure = AppInstance.Restart($"restarted {key} \"{root}\" {args[4]} {Environment.ProcessId}");
        Console.WriteLine("RESTART FAILED: " + failure);
        return 1;
    }

    public static async Task RunAsync()
    {
        foreach (var (from, to) in new[] { ("it", "en"), ("en", "it"), ("it", "ja"), ("ja", "it") })
        {
            var key = KeyPrefix + Guid.NewGuid().ToString("N");
            var root = Path.Combine(Path.GetTempPath(), key);
            Directory.CreateDirectory(root);
            using var owner = Start("owner", key, root, from, to);
            try
            {
                await ExpectLine(owner, "PRIMARY");
                // Reproduce the old launch ordering against the real single-instance service.
                using (var probe = Start("probe", key, root))
                {
                    await ExpectLine(probe, "REDIRECTED");
                    await probe.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                    await ExpectLine(owner, "ACTIVATED");
                    if (probe.ExitCode != 0 || owner.HasExited) throw new Exception("Old-order probe did not redirect");
                }
                await owner.StandardInput.WriteLineAsync("RESTART");
                var resultPath = Path.Combine(root, "result.json");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (!File.Exists(resultPath)) await Task.Delay(50, timeout.Token);
                using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
                var state = result.RootElement;
                if (!state.GetProperty("Primary").GetBoolean() || state.GetProperty("OldAlive").GetBoolean() ||
                    state.GetProperty("Language").GetString() != to)
                    throw new Exception("Replacement did not acquire the key after saved language/old exit: " + result.RootElement);
                await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                try
                {
                    using var next = Process.GetProcessById(state.GetProperty("ProcessId").GetInt32());
                    await next.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (ArgumentException) { }
                Console.WriteLine($"  native {from}->{to}: save durable, old exited, new primary");
            }
            finally
            {
                if (!owner.HasExited)
                {
                    await owner.StandardInput.WriteLineAsync("STOP");
                    try { await owner.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (TimeoutException) { owner.Kill(); await owner.WaitForExitAsync(); }
                }
                var target = Path.GetFullPath(root);
                if (Path.GetFileName(target) != key || !target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected test cleanup path");
                Directory.Delete(target, recursive: true);
            }
        }
    }

    private static Process Start(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Test child did not start");
    }

    private static async Task ExpectLine(Process process, string expected)
    {
        var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (line != expected) throw new Exception($"Expected {expected}; got {line}; exited={process.HasExited}");
    }
}
