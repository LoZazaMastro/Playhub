using System.Diagnostics;
using System.Runtime.InteropServices;
using Playhub.GameSession;

internal static class UwpTests
{
    private const string Aumid = "Fixture.Game_123!App";
    private const string Executable = "Cuphead.exe";

    internal static void Run(string folder)
    {
        var game = new Game(31, Executable, "Fixture.Game_123");
        var runtime = new Runtime { ReadyAt = 0, Activated = game };
        var result = UwpLifetime.Run(Aumid, Executable, "--language it", runtime);
        Check(result == 7 && runtime.ShellStarts == 0 && runtime.Delays == 7 && game.Waits == 1 && game.Disposed,
            "Ready shell: exact activation process is awaited without periodic work");
        Check(runtime.Arguments == "--language it" && runtime.Searches == 0, "Game arguments reach activation unchanged");

        runtime = new Runtime { ReadyAt = 700 };
        UwpLifetime.Run(Aumid, Executable, "", runtime);
        Check(runtime.ShellStarts == 1 && runtime.ActivationAt >= 1000 && runtime.ActivationAt < 15000,
            "Absent shell becomes responsive before activation");

        runtime = new Runtime { Readiness = time => time >= 200 && (time < 500 || time >= 900) };
        UwpLifetime.Run(Aumid, Executable, "", runtime);
        Check(runtime.ShellStarts == 1 && runtime.ActivationAt == 1600,
            "Transient Explorer readiness resets stability before activation instead of failing the launch");

        runtime = new Runtime { ReadyAt = long.MaxValue };
        Expect<TimeoutException>(() => UwpLifetime.Run(Aumid, Executable, "", runtime),
            "Unavailable shell times out without activating a game");
        Check(runtime.Activations == 0 && runtime.Now <= 15000, "Shell timeout has a finite startup budget");

        runtime = new Runtime { ReadyAt = 0, ActivationError = new COMException("fixture", unchecked((int)0x80040900)) };
        Expect<COMException>(() => UwpLifetime.Run(Aumid, Executable, "", runtime), "Activation failure is preserved without a second launch");
        Check(runtime.Activations == 1 && runtime.Searches == 0, "Activation failure cannot attach a guessed process");

        var bootstrap = new Game(41, "GameLaunchHelper.exe", "Fixture.Game_123");
        var target = new Game(42, Executable, "Fixture.Game_123");
        runtime = new Runtime { ReadyAt = 0, Activated = bootstrap, Target = target, TargetAt = 1200 };
        UwpLifetime.Run(Aumid, Executable, "", runtime);
        Check(target.Waits == 1 && bootstrap.Waits == 0 && target.Disposed && bootstrap.Disposed && runtime.Now == 1200,
            "GDK bootstrap hands off to the matching game and releases both handles");

        var unrelated = new Game(43, Executable, "Unrelated.Game_123");
        runtime = new Runtime { ReadyAt = 0, Activated = new Game(41, "GameLaunchHelper.exe", "Fixture.Game_123"), Target = unrelated };
        Expect<TimeoutException>(() => UwpLifetime.Run(Aumid, Executable, "", runtime), "Same executable in a different package cannot hold Steam open");
        Check(unrelated.Waits == 0 && unrelated.Disposed, "Rejected handoff is never awaited or terminated");

        runtime = new Runtime { ReadyAt = 0, Activated = new Game(99, "Other.exe", "Fixture.Game_123") };
        Expect<InvalidOperationException>(() => UwpLifetime.Run(Aumid, Executable, "", runtime), "Unexpected activation PID cannot start a package search");
        Check(runtime.Searches == 0 && runtime.Activated.Disposed, "Unexpected activation releases its exact handle");

        runtime = new Runtime { ReadyAt = 0 };
        Expect<ArgumentException>(() => UwpLifetime.Run("invalid", Executable, "", runtime), "Invalid identity fails before shell or activation work");
        Check(runtime.Activations == 0 && runtime.ShellStarts == 0, "Invalid launch has no runtime side effects");

        var custom = "--flag \"a b\" \"\" --path \"C:\\my games\\\" --token=a\\\"b";
        var legacy = Aumid + " " + Executable + " " + custom;
        var encoded = UwpShortcutArguments.Build(Aumid, Executable, legacy);
        var args = NativeArguments(encoded);
        Check(args.SequenceEqual(new[] { "--uwp", Aumid, Executable, custom }), "UWPHook executable metadata is removed while preserving raw user arguments");
        Check(UwpShortcutArguments.Build(Aumid, Executable, encoded) == encoded, "Repeated import does not nest or change game options");
        Check(NativeArguments(UwpShortcutArguments.Build(Aumid, Executable, encoded + " --new-option"))[3] == custom + " --new-option",
            "User options added after import survive relinking");
        Check(NativeArguments(UwpShortcutArguments.Build(Aumid, Executable, Aumid + " " + Executable))[3] == "",
            "Cuphead executable is never passed as a game argument");
        Check(UwpShortcutArguments.Matches(encoded, Aumid) && UwpShortcutArguments.Matches(legacy, Aumid) &&
            !UwpShortcutArguments.Matches("--uwp \"" + Aumid + "extra\" \"" + Executable + "\"", Aumid),
            "Legacy and session imports match only their exact application identity");

        var output = Path.Combine(folder, "uwp-handle.json");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        foreach (var argument in new[] { "child", "450", output }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        using (var handle = WindowsUwpProcess.TryOpen(unchecked((uint)child.Id))!)
        {
            var timer = Stopwatch.StartNew();
            Check(handle.WaitForExit() == 7 && timer.ElapsedMilliseconds >= 300 && File.Exists(output),
                "Native exact process handle blocks until the isolated child exits and retains its exit code");
        }
        using var second = Process.Start(start)!;
        WindowsUwpProcess.TryOpen(unchecked((uint)second.Id))!.Dispose();
        Check(!second.HasExited, "Disposing a tracked handle leaves the game alive");
        second.WaitForExit();
        XboxShellTests.Run(folder);
    }

    private static string[] NativeArguments(string options)
    {
        var memory = CommandLineToArgvW("fixture.exe " + options, out var count);
        if (memory == IntPtr.Zero) throw new Exception("CommandLineToArgvW failed.");
        try { return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(memory); }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }
    private static void Expect<T>(Action action, string name) where T : Exception
    {
        try { action(); } catch (T) { Check(true, name); return; }
        throw new Exception(name);
    }

    private sealed class Runtime : IUwpRuntime
    {
        public long Now { get; private set; }
        public long ReadyAt, TargetAt, ActivationAt;
        public int ShellStarts, Delays, Activations, Searches;
        public Game Activated = new(31, Executable, "Fixture.Game_123");
        public Game? Target;
        public Exception? ActivationError;
        public string Arguments = "";
        public Func<long, bool>? Readiness;
        public bool ShellReady => Readiness?.Invoke(Now) ?? Now >= ReadyAt;
        public void StartShell() => ShellStarts++;
        public void Delay(int milliseconds) { Now += milliseconds; Delays++; }
        public IUwpProcess Activate(string aumid, string arguments)
        {
            if (!ShellReady) throw new Exception("Activated before shell ready.");
            ActivationAt = Now; Activations++; Arguments = arguments;
            if (ActivationError is not null) throw ActivationError;
            return Activated;
        }
        public IUwpProcess? FindGame(string family, string executable) { Searches++; return Now >= TargetAt ? Target : null; }
        public void Log(string message) { }
    }
    private sealed class Game(uint id, string executable, string family) : IUwpProcess
    {
        public uint Id => id;
        public string Executable => executable;
        public string PackageFamily => family;
        public int Waits;
        public bool Disposed;
        public int WaitForExit() { Waits++; return 7; }
        public void Dispose() => Disposed = true;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
