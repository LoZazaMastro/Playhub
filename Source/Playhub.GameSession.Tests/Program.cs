using System.Diagnostics;
using System.Text.Json;
using Playhub.GameSession;

internal static class SessionTests
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "child")
        {
            Thread.Sleep(int.Parse(args[1]));
            File.WriteAllText(args[2], JsonSerializer.Serialize(args.Skip(3).ToArray()));
            return 7;
        }
        if (args.Length > 0 && args[0] == "bootstrap")
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            foreach (var argument in new[] { "child", "1200", args[1], "child survived bootstrap exit" }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            return 9;
        }
        var folder = Path.Combine(Path.GetTempPath(), "Playhub-GameSession-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var direct = Path.Combine(folder, "direct.json");
        var expected = new[] { "a b", "", "a\"b", "trailing\\", "two\\\\\"quotes", "日本語" };
        var timer = Stopwatch.StartNew();
        var code = GameLifetime.Run(Environment.ProcessPath!, new[] { "child", "300", direct }.Concat(expected));
        Check(code == 7, "Direct process exit code");
        Check(timer.ElapsedMilliseconds >= 250, "Waits for direct process");
        Check(JsonSerializer.Deserialize<string[]>(File.ReadAllText(direct))!.SequenceEqual(expected), "Windows argument round trip");
        var handoff = Path.Combine(folder, "handoff.json");
        timer.Restart();
        code = GameLifetime.Run(Environment.ProcessPath!, new[] { "bootstrap", handoff });
        Check(code == 9, "Bootstrap exit code retained");
        Check(File.Exists(handoff) && timer.ElapsedMilliseconds >= 1100, "Waits for child after immediate bootstrap exit");
        Check(timer.ElapsedMilliseconds < 5000, "Session ends when process tree exits");
        var missing = false;
        try { GameLifetime.Run(Path.Combine(folder, "missing.exe"), Array.Empty<string>()); }
        catch (FileNotFoundException) { missing = true; }
        Check(missing, "Missing executable fails before launch");
        var unrelatedOutput = Path.Combine(folder, "unrelated.json");
        var unrelatedStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var argument in new[] { "child", "1800", unrelatedOutput }) unrelatedStart.ArgumentList.Add(argument);
        using (var unrelated = Process.Start(unrelatedStart)!)
        {
            GameLifetime.Run(Environment.ProcessPath!, new[] { "child", "50", direct });
            Check(!unrelated.HasExited, "Unrelated process remains alive and does not hold the session open");
            unrelated.WaitForExit();
        }
        if (args.Length == 2 && args[0] == "--launcher")
        {
            var packagedOutput = Path.Combine(folder, "packaged.json");
            var portableDirectory = Path.Combine(folder, "single-file-launcher");
            Directory.CreateDirectory(portableDirectory);
            var portableLauncher = Path.Combine(portableDirectory, "Playhub.GameSession.exe");
            File.Copy(args[1], portableLauncher);
            var start = new ProcessStartInfo(portableLauncher) { UseShellExecute = false };
            foreach (var argument in new[] { "--game", Environment.ProcessPath!, "bootstrap", packagedOutput }) start.ArgumentList.Add(argument);
            using var launcher = Process.Start(start)!;
            Check(launcher.WaitForExit(10000) && launcher.ExitCode == 9 && File.Exists(packagedOutput),
                "Single file launcher in an isolated directory waits for bootstrap child and exits correctly");
        }
        UwpTests.Run(folder);
        GdkProofTests.Run(folder);
        Console.WriteLine("All checks passed. Native Windows process handoff exercised.");
        return 0;
    }
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
}
