using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Playhub.DeckyHost;
using Playhub.Shared;

string? fixture = Environment.GetEnvironmentVariable("PLAYHUB_DECKY_FIXTURE");
if (fixture is not null)
{
    string role = args.FirstOrDefault() ?? "root";
    File.WriteAllText(Path.Combine(fixture, role == "--raceworker" ? role + "-" + Environment.ProcessId + ".pid" : role + ".pid"), Environment.ProcessId.ToString());
    string executable = Environment.ProcessPath!;
    if (role is "--worker" or "--raceworker")
    {
        using var locked = new FileStream(Path.Combine(Environment.GetEnvironmentVariable("TEMP")!, "_MEIfixture", "python-fixture.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (role == "--worker") File.WriteAllText(Path.Combine(fixture, "worker-ready"), "ready");
        Thread.Sleep(Timeout.Infinite);
        return;
    }
    if (role == "--external") { Thread.Sleep(Timeout.Infinite); return; }
    if (role == "--main")
    {
        using var worker = Process.Start(new ProcessStartInfo(executable, "--worker") { UseShellExecute = false })!;
        using var external = Process.Start(new ProcessStartInfo(Path.Combine(fixture, "DeckyExternal.exe"), "--external") { UseShellExecute = false })!;
        if (Environment.GetEnvironmentVariable("PLAYHUB_DECKY_SPAWN_RACE") == "1")
            while (true) { Process.Start(new ProcessStartInfo(executable, "--raceworker") { UseShellExecute = false })?.Dispose(); Thread.Sleep(20); }
        Thread.Sleep(Timeout.Infinite);
        return;
    }
    File.WriteAllText(Path.Combine(fixture, "environment"), string.Join("\n", Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("_PYI") || k == "_MEIPASS2" || k == "PLAYHUB_DECKY_TEST_TOKEN").Select(k => k + "=" + Environment.GetEnvironmentVariable(k))));
    string extraction = Path.Combine(Environment.GetEnvironmentVariable("TEMP")!, "_MEIfixture");
    File.WriteAllText(Path.Combine(fixture, "runtime"), Environment.GetEnvironmentVariable("TEMP")!);
    Directory.CreateDirectory(extraction);
    File.WriteAllText(Path.Combine(extraction, "python-fixture.dll"), "fixture");
    using var main = Process.Start(new ProcessStartInfo(executable, "--main") { UseShellExecute = false })!;
    main.WaitForExit();
    Thread.Sleep(100); // independent parent performs its own cleanup after main exits
    try { Directory.Delete(extraction, true); File.WriteAllText(Path.Combine(fixture, "root-cleaned"), "cleaned"); }
    catch (IOException) { File.WriteAllText(Path.Combine(fixture, "root-failed"), "worker held extraction"); }
    return;
}

int checks = 0;
if (args.Length == 2 && args[0] == "--inspect")
{
    var identity = DeckyProcessTree.Read(int.Parse(args[1]));
    Console.WriteLine("Root: " + identity);
    var snapshot = DeckyProcessTree.Snapshot();
    foreach (var entry in snapshot.Values) Console.WriteLine($"Depth { (identity is null ? -1 : DeckyProcessTree.Depth(entry, identity, snapshot))}: {entry}");
    return;
}
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
void Wait(Func<bool> condition, string label) { var clock = Stopwatch.StartNew(); while (!condition() && clock.ElapsedMilliseconds < 10000) Thread.Sleep(25); Check(condition(), label); }

var root = new DeckyProcessTree.Identity(100, 10, 1000, "C:\\fixture\\PluginLoader.exe", "S-1-5-21-fixture", 1);
var mainIdentity = root with { Pid = 101, ParentPid = 100, Birth = 1001 };
var workerIdentity = root with { Pid = 102, ParentPid = 101, Birth = 1002 };
var tree = new Dictionary<int, DeckyProcessTree.Identity> { [100] = root, [101] = mainIdentity, [102] = workerIdentity };
Check(DeckyProcessTree.Depth(workerIdentity, root, tree) == 2, "Owned worker lineage rejected");
foreach (var wrong in new[] { workerIdentity with { Path = "C:\\fixture\\PS5upload.exe" }, workerIdentity with { Sid = "foreign" }, workerIdentity with { Session = 2 }, workerIdentity with { Birth = 500 }, workerIdentity with { ParentPid = 777 } })
    Check(DeckyProcessTree.Depth(wrong, root, tree) == 0, "Foreign/unowned candidate accepted");
tree[101] = mainIdentity with { Birth = 1003 };
Check(DeckyProcessTree.Depth(workerIdentity, root, tree) == 0, "Recycled parent PID accepted");

string owned = DeckyRuntimeDirectory.Create(root.Path);
File.WriteAllText(Path.Combine(owned, "fixture"), "fixture");
using (var locked = File.Open(Path.Combine(owned, "fixture"), FileMode.Open, FileAccess.Read, FileShare.None))
    Check(!DeckyRuntimeDirectory.TryCleanup(owned, root.Path) && File.Exists(Path.Combine(owned, "fixture")), "Locked container changed");
Check(!DeckyRuntimeDirectory.TryCleanup(owned, root.Path + ".foreign") && Directory.Exists(owned), "Foreign ownership marker accepted");
Check(DeckyRuntimeDirectory.TryCleanup(owned, root.Path), "Owned inactive container not removed");
string unmarked = Path.Combine(DeckyRuntimeDirectory.BaseDirectory, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(unmarked);
Check(!DeckyRuntimeDirectory.TryCleanup(unmarked, root.Path), "Unmarked container removed");
Directory.Delete(unmarked);
string linked = DeckyRuntimeDirectory.Create(root.Path);
string outside = Path.Combine(Path.GetTempPath(), "Playhub-decky-external-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(outside);
File.WriteAllText(Path.Combine(outside, "keep"), "outside fixture");
try
{
    Directory.CreateSymbolicLink(Path.Combine(linked, "outside"), outside);
    Check(!DeckyRuntimeDirectory.TryCleanup(linked, root.Path) && File.Exists(Path.Combine(outside, "keep")), "Reparse cleanup crossed outside ownership");
    Directory.Delete(Path.Combine(linked, "outside"));
}
catch (UnauthorizedAccessException) { Console.WriteLine("Reparse fixture unavailable on this account"); }
Check(DeckyRuntimeDirectory.TryCleanup(linked, root.Path), "Owned container after link removal not cleaned");
Directory.Delete(outside, true);

int hostArg = Array.IndexOf(args, "--host");
if (hostArg < 0 || hostArg + 1 >= args.Length) throw new ArgumentException("Pass --host <published Playhub-DeckyHost.exe>");
string hostPath = Path.GetFullPath(args[hostArg + 1]);
string folder = Path.Combine(Path.GetTempPath(), "Playhub-decky-shutdown-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
foreach (string file in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
string loader = Path.Combine(folder, "PluginLoader.exe");
File.Copy(Environment.ProcessPath!, loader, true);
File.Copy(Environment.ProcessPath!, Path.Combine(folder, "DeckyExternal.exe"), true);
var start = new ProcessStartInfo(hostPath) { UseShellExecute = false };
start.ArgumentList.Add("--loader"); start.ArgumentList.Add(loader);
start.Environment["PLAYHUB_DECKY_FIXTURE"] = folder;
start.Environment["_PYI_APPLICATION_HOME_DIR"] = "C:\\foreign-extraction";
start.Environment["_MEIPASS2"] = "C:\\foreign-extraction";
start.Environment["PLAYHUB_DECKY_TEST_TOKEN"] = "preserved";
if (args.Contains("--spawn-race")) start.Environment["PLAYHUB_DECKY_SPAWN_RACE"] = "1";
bool attachThroughGuard = args.Contains("--attach-through-guard");
bool attachExisting = attachThroughGuard || args.Contains("--attach-existing");
Process? legacyRoot = null;
Process? adoptedHost = null;
if (attachExisting)
{
    var legacy = new ProcessStartInfo(loader) { UseShellExecute = false };
    foreach (var variable in start.Environment) legacy.Environment[variable.Key] = variable.Value;
    legacy.Environment["TEMP"] = legacy.Environment["TMP"] = Directory.CreateDirectory(Path.Combine(folder, "legacy-runtime")).FullName;
    legacyRoot = Process.Start(legacy)!;
    Wait(() => File.Exists(Path.Combine(folder, "worker-ready")), "Legacy fixture worker did not start");
    int workerPid = int.Parse(File.ReadAllText(Path.Combine(folder, "--worker.pid")));
    Check(DeckyProcessTree.FindRoot(workerPid, loader)?.Pid == legacyRoot.Id, "Existing listener ancestry did not resolve the onefile parent");
    start.ArgumentList.Add("--attach");
    start.ArgumentList.Add(workerPid.ToString());
    if (attachThroughGuard)
    {
        string runtime = Directory.CreateDirectory(Path.Combine(folder, "PlayhubRuntime")).FullName;
        string version = new string('A', 64);
        string runtimeVersion = Directory.CreateDirectory(Path.Combine(runtime, version)).FullName;
        string copiedHost = Path.Combine(runtimeVersion, "Playhub-DeckyHost.exe");
        File.Copy(hostPath, copiedHost);
        File.WriteAllText(Path.Combine(runtime, "current"), version);
        Check(DeckyStartupGuard.ProtectExistingLoader(loader, workerPid), "Startup guard did not adopt the legacy loader");
        Wait(() =>
        {
            foreach (var candidate in Process.GetProcessesByName("Playhub-DeckyHost"))
            {
                try { if (candidate.MainModule?.FileName == copiedHost) { _ = candidate.SafeHandle; adoptedHost = candidate; return true; } }
                catch (System.ComponentModel.Win32Exception) { }
                candidate.Dispose();
            }
            return false;
        }, "Guarded adoption host did not start");
    }
}
using var host = adoptedHost ?? Process.Start(start)!;
Process? externalProcess = null;
try
{
    Wait(() => File.Exists(Path.Combine(folder, "worker-ready")), "Fixture worker did not start");
    Wait(() => File.Exists(Path.Combine(folder, "--external.pid")), "Fixture external did not start");
    externalProcess = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(folder, "--external.pid"))));
    using var mainProcess = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(folder, "--main.pid"))));
    using var workerProcess = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(folder, "--worker.pid"))));
    Check(DeckyProcessTree.FindRoot(externalProcess.Id, loader) is null, "Foreign executable was accepted for adoption");
    nint window = 0;
    Wait(() =>
    {
        Native.EnumWindows((handle, _) =>
        {
            Native.GetWindowThreadProcessId(handle, out uint pid);
            var title = new StringBuilder(256); Native.GetWindowText(handle, title, title.Capacity);
            if (pid == host.Id && title.ToString() == "Playhub Decky lifecycle") window = handle;
            return true;
        }, 0);
        return window != 0;
    }, "Native host lifecycle window not found");
    string environment = File.ReadAllText(Path.Combine(folder, "environment"));
    Check(attachExisting
        ? environment.Contains("_PYI_APPLICATION_HOME_DIR=C:\\foreign-extraction") && !legacyRoot!.HasExited
        : !environment.Contains("_PYI") && !environment.Contains("_MEIPASS2") && environment.Contains("PLAYHUB_DECKY_TEST_TOKEN=preserved"), "Root environment reset/adoption preservation failed");
    Check(DeckyStartupGuard.ProtectExistingLoader(loader, workerProcess.Id), "Existing host was not reused during adoption");
    string runtimeDirectory = File.ReadAllText(Path.Combine(folder, "runtime"));
    Check(!DeckyRuntimeDirectory.TryCleanup(runtimeDirectory, loader) && Directory.Exists(runtimeDirectory), "Live loader extraction was cleaned");
    foreach (ProcessModule module in host.Modules)
        Check(!module.FileName.Contains("\\.net\\", StringComparison.OrdinalIgnoreCase) && !module.FileName.Contains("\\_MEI", StringComparison.OrdinalIgnoreCase), "Lifetime host extracted its own runtime to temporary storage");
    host.Refresh(); var cpu = host.TotalProcessorTime; Thread.Sleep(1000); host.Refresh();
    Check((host.TotalProcessorTime - cpu).TotalMilliseconds <= 50, "Idle host consumed CPU");
    Console.WriteLine($"Host idle: CPU delta {(host.TotalProcessorTime - cpu).TotalMilliseconds:0.0} ms/second, private memory {host.PrivateMemorySize64 / 1048576.0:0.0} MiB");
    Check(Native.SendMessage(window, 0x11, 0, 0) == 1, "QUERYENDSESSION did not consent");
    Native.SendMessage(window, 0x16, 0, 0);
    Thread.Sleep(100);
    Check(!mainProcess.HasExited && !workerProcess.HasExited && !externalProcess.HasExited, "Query/cancel changed process lifetime");
    var shutdown = Stopwatch.StartNew();
    if (args.Contains("--stop-installation")) DeckyProcessTree.StopInstallation(folder);
    else Native.SendMessage(window, 0x16, 1, 0);
    Check(shutdown.ElapsedMilliseconds < 2000, "Confirmed session-end handler exceeded budget");
    Wait(() => mainProcess.HasExited && workerProcess.HasExited, "Owned descendants survived confirmed end");
    Check(!externalProcess.HasExited, "Independent external fixture was terminated");
    Wait(() => File.Exists(Path.Combine(folder, "root-cleaned")), "Onefile parent could not clean extraction");
    Check(host.WaitForExit(10000) && host.ExitCode == 0, "Host did not exit with its loader parent");
    Check(attachExisting ? Directory.Exists(runtimeDirectory) : !Directory.Exists(runtimeDirectory), "Runtime ownership boundary was not preserved");
    foreach (var candidate in Process.GetProcessesByName("PluginLoader"))
        using (candidate) Check(candidate.MainModule?.FileName != loader, "A newly spawned owned worker survived confirmed end");
    Console.WriteLine($"Synthetic confirmed end: {shutdown.ElapsedMilliseconds} ms; external fixture preserved; parent cleaned extraction");
}
finally
{
    // Only these exact fixture handles are stopped; never enumerate or kill the
    // installed loader, Steam, upload jobs or any real session.
    if (!host.HasExited) host.Kill();
    if (legacyRoot is not null) { if (!legacyRoot.HasExited) legacyRoot.Kill(); legacyRoot.Dispose(); }
    if (externalProcess is not null) { if (!externalProcess.HasExited) externalProcess.Kill(); externalProcess.Dispose(); }
    foreach (string file in Directory.GetFiles(folder, "*.pid"))
    {
        try
        {
            int pid = int.Parse(File.ReadAllText(file));
            using var process = Process.GetProcessById(pid);
            if (process.MainModule?.FileName is string image && Path.GetDirectoryName(image) == folder && !process.HasExited) process.Kill();
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }
    foreach (var candidate in Process.GetProcessesByName("PluginLoader"))
        using (candidate)
            try { if (candidate.MainModule?.FileName == loader && !candidate.HasExited) candidate.Kill(); }
            catch (InvalidOperationException) { }
}
Console.WriteLine($"Decky shutdown checks: {checks} PASS; fixture retained at {folder}");

static class Native
{
    internal delegate bool Callback(nint window, nint param);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(Callback callback, nint param);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder title, int length);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] internal static extern nint SendMessage(nint window, uint message, nuint wparam, nint lparam);
}
