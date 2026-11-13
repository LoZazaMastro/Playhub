using System.Diagnostics;
using System.Text.Json;
using Playhub.GameSession;

internal static class SessionTests
{
    private static int Main(string[] args)
    {
        using var logs = SessionFixtureLogs.Start();
        if (args.Length > 0 && args[0] is "startup" or "raw-startup")
        {
            string[] details = [Environment.CurrentDirectory, File.ReadAllText("fixture config.ini"),
                Environment.GetEnvironmentVariable("PLAYHUB_FIXTURE_STARTUP") ?? "", .. args.Skip(2)];
            File.WriteAllText(args[1], args[0] == "raw-startup"
                ? JsonSerializer.Serialize(new { Details = details, CommandLine = Win32LaunchCommand.ReadProcessCommandLine() })
                : JsonSerializer.Serialize(details));
            return 8;
        }
        if (args.Length > 0 && args[0] == "child")
        {
            Thread.Sleep(int.Parse(args[1]));
            File.WriteAllText(args[2], JsonSerializer.Serialize(args.Skip(3).ToArray()));
            return 7;
        }
        if (args.Length > 0 && args[0] is "bootstrap" or "grand-bootstrap")
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            var childArgs = args[0] == "bootstrap"
                ? new[] { "child", "1200", args[1], "child survived bootstrap exit" }
                : new[] { "bootstrap", args[1] };
            foreach (var argument in childArgs) start.ArgumentList.Add(argument);
            using var child = Process.Start(start)!;
            return 9;
        }
        var folder = Path.Combine(Path.GetTempPath(), "Playhub-GameSession-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var direct = Path.Combine(folder, "direct.json");
        var expected = new[] { "a b", "", "a\"b", "trailing\\", "two\\\\\"quotes", "日本語" };
        string rawFixture = "--profile=\"two  spaces\"  \"\" --quoted=\\\"value\\\" --unicode=\"日本語 λ\"";
        Check(Win32LaunchCommand.TryReadRawArguments("\"C:\\Playhub App\\Playhub.GameSession.exe\" --game \"C:\\Game folder\\Game.exe\" " + rawFixture,
            @"C:\Game folder\Game.exe", out string opaque) && opaque == rawFixture,
            "Win32 launch prefix preserves opaque game quotes, empty arguments and spacing");
        foreach (string prefix in new[] { "wrapper.exe --uwp \"C:\\Game.exe\"", "wrapper.exe --game", "wrapper.exe --game \"C:\\Other.exe\"" })
            Check(!Win32LaunchCommand.TryReadRawArguments(prefix, @"C:\Game.exe", out _),
                "Unrelated or incomplete wrapper prefix is not treated as raw game arguments");
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
        var delegated = Path.Combine(folder, "delegated.json");
        code = GameLifetime.Run(Environment.ProcessPath!, new[] { "grand-bootstrap", delegated });
        Check(code == 9 && File.Exists(delegated), "Retains Steam session until a delegated grandchild exits");
        var accountingQueries = 0;
        var completionWaits = 0;
        GameLifetime.WaitForJobDrain(() => ++accountingQueries < 3, () => { completionWaits++; return true; });
        Check(accountingQueries == 3 && completionWaits == 2,
            "Completion hints and timeouts still require confirmed zero active processes");
        accountingQueries = completionWaits = 0;
        GameLifetime.WaitForJobDrain(() => ++accountingQueries < 3, () => { completionWaits++; return false; });
        Check(accountingQueries == 3 && completionWaits == 1,
            "Failed completion wait switches to accounting fallback until the job drains");
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

            var workingDirectory = Path.Combine(folder, "Game data");
            Directory.CreateDirectory(workingDirectory);
            File.WriteAllText(Path.Combine(workingDirectory, "fixture config.ini"), "relative config found");
            var startupOutput = Path.Combine(folder, "startup.json");
            var gameStart = new ProcessStartInfo(portableLauncher) { UseShellExecute = false, WorkingDirectory = workingDirectory };
            gameStart.Environment["PLAYHUB_FIXTURE_STARTUP"] = "parent launch environment";
            foreach (var argument in new[] { "--game", Environment.ProcessPath!, "startup", startupOutput }.Concat(expected))
                gameStart.ArgumentList.Add(argument);
            using var game = Process.Start(gameStart)!;
            Check(game.WaitForExit(10000) && game.ExitCode == 8 && JsonSerializer.Deserialize<string[]>(File.ReadAllText(startupOutput))!
                .SequenceEqual(new[] { workingDirectory, "relative config found", "parent launch environment" }.Concat(expected)),
                "Packaged launcher preserves Steam-style working directory, environment and Windows argument tokens");

            string rawOutput = Path.Combine(folder, "raw-startup.json");
            string rawTail = "raw-startup " + GameLifetime.Quote(rawOutput) + "   " + rawFixture;
            var rawStart = new ProcessStartInfo(portableLauncher)
            {
                UseShellExecute = false, WorkingDirectory = workingDirectory,
                Arguments = "--game " + GameLifetime.Quote(Environment.ProcessPath!) + " " + rawTail
            };
            rawStart.Environment["PLAYHUB_FIXTURE_STARTUP"] = "raw launch environment";
            using var rawGame = Process.Start(rawStart)!;
            Check(rawGame.WaitForExit(10000) && rawGame.ExitCode == 8, "Packaged raw Win32 launch exits with the game's result");
            using var rawResult = JsonDocument.Parse(File.ReadAllText(rawOutput));
            Check(rawResult.RootElement.GetProperty("CommandLine").GetString()!.EndsWith(rawTail, StringComparison.Ordinal),
                "Packaged launcher preserves the original raw game command line without reserializing quotes");
            Check(rawResult.RootElement.GetProperty("Details").EnumerateArray().Select(value => value.GetString()).SequenceEqual(
                new[] { workingDirectory, "relative config found", "raw launch environment", "--profile=two  spaces", "", "--quoted=\"value\"", "--unicode=日本語 λ" }),
                "Raw Win32 launch retains cwd, relative files, environment and actual Windows argument values");
        }
        UnrealDisplayTests.Run(folder);
        UwpCompanionPreparationTests.Run();
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
