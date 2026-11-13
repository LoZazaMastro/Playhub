using System.Diagnostics;
using System.Runtime.InteropServices;
using Playhub.DeckyHost;

namespace DeckyGenerationChecks;

internal static class Checks
{
    private static int _checks;
    private static void Check(bool condition, string label)
    { if (!condition) throw new InvalidOperationException(label); _checks++; Console.WriteLine("PASS " + label); }
    private static void Wait(Func<bool> condition, string label)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 7000) Thread.Sleep(25);
        Check(condition(), label);
    }

    private static int Main(string[] args)
    {
        string? fixture = Environment.GetEnvironmentVariable("PLAYHUB_DECKY_GENERATION_FIXTURE");
        if (fixture is not null) return Child(fixture, args.Contains("--child"));
        _checks += HealthChecks.Run();
        var owner = new DeckyProcessTree.Identity(100, 10, 1000, @"C:\Steam\steam.exe", "fixture-user", 1);
        Check(SteamGenerationPolicy.Accept(owner, @"c:\steam\STEAM.exe", owner.Sid, owner.Session), "Steam generation requires the exact registered path, owner and session");
        foreach (var bad in new[] { owner with { Pid = 0 }, owner with { Birth = 0 }, owner with { Sid = "foreign" },
            owner with { Session = 2 }, owner with { Path = @"C:\other\steam.exe" } })
            Check(!SteamGenerationPolicy.Accept(bad, owner.Path, owner.Sid, owner.Session), "Wrong Steam process evidence is rejected");
        var first = new SteamGenerationIdentity(owner.Pid, owner.Birth, owner.Path, owner.Sid, owner.Session);
        Check(SteamGenerationPolicy.CanReuse(first, first.Pid, first.Path.ToUpperInvariant(), first.Sid, first.Session, true),
            "A live retained Steam handle plus matching registered identity avoids a repeated token query");
        Check(!SteamGenerationPolicy.CanReuse(first, first.Pid, first.Path, first.Sid, first.Session, false),
            "A recycled PID cannot reuse a signaled old Steam handle");
        Check(!SteamGenerationPolicy.CanReuse(first, first.Pid + 1, first.Path, first.Sid, first.Session, true),
            "Changed registered PID requires full validation");
        Check(!SteamGenerationPolicy.CanReuse(first, first.Pid, @"C:\Other\steam.exe", first.Sid, first.Session, true),
            "Changed registered image requires full validation");
        Check(!SteamGenerationPolicy.CanReuse(first, first.Pid, first.Path, "foreign", first.Session, true),
            "A retained Steam generation cannot cross the owner boundary");
        Check(!SteamGenerationPolicy.CanReuse(first, first.Pid, first.Path, first.Sid, first.Session + 1, true),
            "A retained Steam generation cannot cross the Windows session boundary");
        Check(SteamGenerationPolicy.Observe(null, first, false, true, false) == SteamGenerationAction.Bind, "First observation binds without restarting a loader");
        Check(SteamGenerationPolicy.Observe(first, first, false, true, false) == SteamGenerationAction.Ignore, "Same Steam generation keeps upstream crash protection intact");
        var second = first with { Pid = 200, Birth = 2000 };
        Check(SteamGenerationPolicy.Observe(first, second, false, true, false) == SteamGenerationAction.Ignore, "A still-live previous Steam process prevents replacement");
        Check(SteamGenerationPolicy.Observe(first, second, true, true, false) == SteamGenerationAction.ResetLoader, "A confirmed new Steam process after an actual exit resets the owned loader");
        Check(SteamGenerationPolicy.Observe(first, first with { Birth = 3000 }, true, true, false) == SteamGenerationAction.ResetLoader, "Recycled Steam PID requires and preserves a distinct process birth");
        Check(SteamGenerationPolicy.Observe(first, null, true, true, false) == SteamGenerationAction.Ignore, "Absent or unreadable identity never fabricates a new generation");
        Check(SteamGenerationPolicy.Observe(first, second, true, false, false) == SteamGenerationAction.Ignore, "A stopped loader is never automatically resurrected");
        Check(SteamGenerationPolicy.Observe(first, second, true, true, true) == SteamGenerationAction.Ignore, "Windows shutdown prevents a reset");
        var current = first;
        for (int i = 0; i < 4; i++)
        {
            var next = current with { Pid = current.Pid + 1, Birth = current.Birth + 1 };
            Check(SteamGenerationPolicy.Observe(current, next, true, true, false) == SteamGenerationAction.ResetLoader, "Each of four real Steam restarts has one reset decision"); current = next;
        }
        for (int i = 0; i < 4; i++) Check(SteamGenerationPolicy.Observe(current, current, false, true, false) == SteamGenerationAction.Ignore, "Repeated CEF reconnects in one Steam process do not reset the official loader");
        if (args.Contains("--native-fixture"))
        {
            NativeFixture(); NativeFixture(shutdown: true);
            NativeFixture(rejectedStart: 1); NativeFixture(rejectedStart: 1, throwOnRead: true);
            NativeFixture(rejectedStart: 2); NativeFixture(rejectedStart: 2, throwOnRead: true);
            NativeFixture(rejectedStart: 2, shutdownDuringRead: true);
            NativeFixture(health: true);
        }
        Console.WriteLine($"{_checks}/{_checks} Decky generation checks passed. No Steam, installed loader or user profiles were changed.");
        return 0;
    }

    private static int Child(string fixture, bool child)
    {
        if (child)
        {
            File.AppendAllText(Path.Combine(fixture, "children"), Environment.ProcessId + Environment.NewLine);
            Thread.Sleep(Timeout.Infinite); return 0;
        }
        File.WriteAllText(Path.Combine(fixture, "runtime-" + Environment.ProcessId), Environment.GetEnvironmentVariable("TEMP")!);
        using var worker = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "--child" } })!;
        File.AppendAllText(Path.Combine(fixture, "roots"), Environment.ProcessId + Environment.NewLine);
        worker.WaitForExit();
        File.AppendAllText(Path.Combine(fixture, "cleaned"), Environment.ProcessId + Environment.NewLine);
        return 0;
    }

    private static void NativeFixture(bool shutdown = false, int rejectedStart = 0, bool throwOnRead = false,
        bool shutdownDuringRead = false, bool health = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Playhub-Decky-Generation-Fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (string path in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(path, Path.Combine(directory, Path.GetFileName(path)));
        string loader = Path.Combine(directory, "PluginLoader.exe");
        // Only this inert fixture apphost is copied. A GUI subsystem keeps its
        // sleeping children invisible; no installed loader bytes are modified.
        var executable = File.ReadAllBytes(Environment.ProcessPath!);
        int pe = BitConverter.ToInt32(executable, 0x3c);
        executable[pe + 24 + 68] = 2; executable[pe + 24 + 69] = 0;
        File.WriteAllBytes(loader, executable);
        string mutex = @"Local\Playhub.Decky.Generation.Fixture." + Guid.NewGuid().ToString("N");
        var source = new Source();
        int reads = 0;
        var rejectedProcesses = new List<Process>();
        string? rejectedRuntime = null;
        using var readEntered = new ManualResetEventSlim();
        using var releaseRead = new ManualResetEventSlim();
        DeckyProcessTree.Identity? ReadStarted(Process process)
        {
            var identity = DeckyProcessTree.Read(process.SafeHandle, process.Id);
            if (Interlocked.Increment(ref reads) != rejectedStart) return identity;
            var ready = Stopwatch.StartNew();
            string roots = Path.Combine(directory, "roots"), children = Path.Combine(directory, "children");
            bool Ready()
            {
                try { return File.Exists(roots) && File.ReadAllLines(roots).Length == rejectedStart
                    && File.Exists(children) && File.ReadAllLines(children).Length == rejectedStart; }
                catch (IOException) { return false; }
            }
            while (!Ready() && ready.ElapsedMilliseconds < 4000) Thread.Sleep(10);
            if (!Ready() || identity is null) throw new InvalidOperationException("The inert child was not ready for attestation failure.");
            rejectedProcesses.Add(Process.GetProcessById(process.Id));
            var tree = DeckyProcessTree.Snapshot();
            foreach (var owned in tree.Values.Where(candidate => DeckyProcessTree.Depth(candidate, identity, tree) > 0))
                rejectedProcesses.Add(Process.GetProcessById(owned.Pid));
            foreach (var retained in rejectedProcesses) _ = retained.SafeHandle;
            rejectedRuntime = File.ReadAllText(Path.Combine(directory, "runtime-" + process.Id));
            readEntered.Set();
            if (shutdownDuringRead)
            {
                if (!releaseRead.Wait(5000)) throw new IOException("Fixture attestation release timed out.");
                return identity;
            }
            if (throwOnRead) throw new IOException("Fixture attestation read failed.");
            return null;
        }
        string? previous = Environment.GetEnvironmentVariable("PLAYHUB_DECKY_GENERATION_FIXTURE");
        Environment.SetEnvironmentVariable("PLAYHUB_DECKY_GENERATION_FIXTURE", directory);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { using var lifetime = new DeckyLifetime(loader, readSteam: source.Read, startupMutex: mutex, readStarted: ReadStarted,
                healthFactory: health ? identity => new FailedHealth(identity) : null); lifetime.Run(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true;
        try
        {
            thread.Start();
            string roots = Path.Combine(directory, "roots");
            int Count() { try { return File.Exists(roots) ? File.ReadAllLines(roots).Length : 0; } catch (IOException) { return 0; } }
            nint HostWindow()
            {
                nint window = 0;
                Native.EnumWindows((handle, _) =>
                {
                    Native.GetWindowThreadProcessId(handle, out uint pid);
                    var title = new System.Text.StringBuilder(128); Native.GetWindowText(handle, title, title.Capacity);
                    if (pid == Environment.ProcessId && title.ToString() == "Playhub Decky lifecycle") window = handle;
                    return true;
                }, 0);
                return window;
            }
            void CheckRejected()
            {
                Check(rejectedProcesses.Count >= 2 && rejectedProcesses.All(process => process.HasExited),
                    "Failed attestation releases only the retained new root and its proven loader children");
                Check(rejectedRuntime is not null && !Directory.Exists(rejectedRuntime),
                    "A rejected start cleans its exact marked extraction container after process exit");
                Check(reads == rejectedStart && Count() == rejectedStart,
                    "Failed attestation never triggers another automatic spawn");
                Check(File.ReadAllLines(Path.Combine(directory, "cleaned")).Length == rejectedStart,
                    "Rejected onefile parents are allowed to clean up after their workers stop");
            }
            if (rejectedStart == 1)
            {
                Check(thread.Join(7000), "Failed initial attestation closes the lifecycle host within its cleanup budget");
                Check(failure is IOException, "Initial null or throwing attestation is reported as a failed start");
                CheckRejected(); return;
            }
            Wait(() => Count() == 1, "Owned hidden-window host launches its inert fixture loader");
            source.Set(100, 1000, exitPrevious: false);
            Thread.Sleep(1300); Check(Count() == 1, "First Steam identity binds without restarting the running fixture");
            if (health)
            {
                Wait(() => Count() == 2, "A proven unresponsive owned fixture is replaced without changing Steam generation");
                Thread.Sleep(2200);
                Check(Count() == 2, "Repeated health failures cannot cause a second reset in one Steam generation");
                Check(File.ReadAllLines(Path.Combine(directory, "cleaned")).Length == 1,
                    "Health recovery leaves the old onefile parent time to clean its private extraction");
                var identities = DeckyProcessTree.Snapshot();
                int last = int.Parse(File.ReadAllLines(roots).Last());
                var exact = identities[last];
                DeckyProcessTree.StopRoot(exact);
                Check(thread.Join(7000), "An explicit loader stop still closes the host after health recovery");
                if (failure is not null) throw failure;
                Check(Count() == 2, "Health recovery never resurrects a manually stopped loader");
                return;
            }
            if (rejectedStart == 2)
            {
                source.Set(200, 2000, exitPrevious: true);
                Check(readEntered.Wait(7000), "Replacement fixture reaches attestation with a running owned child");
                if (shutdownDuringRead)
                {
                    var clock = Stopwatch.StartNew();
                    Native.SendMessage(HostWindow(), 0x16, 1, 0);
                    Check(clock.ElapsedMilliseconds < 1000, "Windows shutdown is not blocked by pending child attestation");
                    releaseRead.Set();
                }
                Check(thread.Join(7000), "Rejected or shutdown-interrupted replacement closes the old host");
                if (failure is not null) throw failure;
                CheckRejected(); return;
            }
            if (shutdown)
            {
                nint window = HostWindow();
                Check(window != 0 && Native.SendMessage(window, 0x11, 0, 0) == 1, "The fixture host consents to Windows session shutdown");
                Native.SendMessage(window, 0x16, 0, 0);
                Check(thread.IsAlive && Count() == 1, "A cancelled Windows shutdown preserves the loader");
                source.Set(200, 2000, exitPrevious: true);
                Native.SendMessage(window, 0x16, 1, 0);
                Check(thread.Join(7000), "A confirmed Windows shutdown releases owned workers and closes the fixture host");
                if (failure is not null) throw failure;
                Check(Count() == 1 && File.ReadAllLines(Path.Combine(directory, "cleaned")).Length == 1,
                    "Shutdown cancels a pending new-generation reset and preserves parent cleanup");
                return;
            }
            for (int generation = 1; generation <= 4; generation++)
            {
                source.Set(100 + generation, 1000 + generation, exitPrevious: true);
                int expected = generation + 1;
                Wait(() => Count() == expected, "New verified generation restarts the exact fixture group once");
            }
            Thread.Sleep(1300); Check(Count() == 5, "An unchanged generation never causes a restart loop");
            source.Clear(); Thread.Sleep(1300); Check(Count() == 5, "Steam closed leaves the loader available for a subsequent start");
            using (var gate = new Mutex(true, mutex))
            {
                source.Set(999, 9999, exitPrevious: true);
                Thread.Sleep(1300); Check(Count() == 5, "An explicit installer/stop mutex blocks automatic restart");
                int pid = int.Parse(File.ReadAllLines(roots)[^1]);
                var identity = DeckyProcessTree.Read(pid) ?? throw new InvalidOperationException("Fixture root unavailable");
                Check(DeckyProcessTree.StopRoot(identity) > 0, "Explicit stop removes only the fixture's owned process tree");
                gate.ReleaseMutex();
            }
            Check(thread.Join(7000), "An explicit stop exits the host instead of resurrecting the loader");
            if (failure is not null) throw failure;
            Check(Count() == 5 && File.ReadAllLines(Path.Combine(directory, "cleaned")).Length == 5, "All five fixture parents clean up after their own children exit");
        }
        finally
        {
            releaseRead.Set();
            Environment.SetEnvironmentVariable("PLAYHUB_DECKY_GENERATION_FIXTURE", previous);
            DeckyProcessTree.StopInstallation(directory);
            thread.Join(3000);
            foreach (var retained in rejectedProcesses) retained.Dispose();
            if (!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "Playhub-Decky-Generation-Fixture-", StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe fixture directory");
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FailedHealth(DeckyProcessTree.Identity identity) : IDeckyHealthMonitor
    {
        public Task<DeckyProcessTree.Identity?> ReadFailure(CancellationToken cancellation)
            => Task.FromResult<DeckyProcessTree.Identity?>(identity);
        public bool Confirm(DeckyProcessTree.Identity value) => value == identity;
        public void Pause() { }
        public void Dispose() { }
    }

    private sealed class State(SteamGenerationIdentity identity)
    {
        internal SteamGenerationIdentity Identity = identity;
        internal bool Exited;
    }
    private sealed class Lease(State state) : ISteamGenerationLease
    {
        public SteamGenerationIdentity Identity => state.Identity;
        public bool Exited => Volatile.Read(ref state.Exited);
        public bool Alive => !Exited;
        public void Dispose() { }
    }
    private sealed class Source
    {
        private State? _current, _previous;
        internal void Set(int pid, long birth, bool exitPrevious)
        {
            if (exitPrevious && _previous is not null) Volatile.Write(ref _previous.Exited, true);
            var next = new State(new(pid, birth, "fixture", "fixture", 1));
            _previous = next; Volatile.Write(ref _current, next);
        }
        internal void Clear() { if (_previous is not null) Volatile.Write(ref _previous.Exited, true); Volatile.Write(ref _current, null); }
        internal ISteamGenerationLease? Read(string sid, uint session)
        {
            var value = Volatile.Read(ref _current);
            return value is null ? null : new Lease(value);
        }
    }
    private static class Native
    {
        internal delegate bool EnumCallback(nint window, nint param);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumCallback callback, nint param);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] internal static extern int GetWindowText(nint window, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern nint SendMessage(nint window, uint message, nuint wparam, nint lparam);
    }
}
