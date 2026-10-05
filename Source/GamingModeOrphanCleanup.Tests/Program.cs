using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using GamingMode.Services;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

string executable = Environment.ProcessPath!;
if (args.Length > 0 && args[0] == "--observe-live-child")
{
    int parentPid = int.Parse(args[1]);
    using Process parent = Process.GetProcessById(parentPid);
    if (parent.MainModule?.FileName != Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamingMode", "GamingMode.exe")) throw new Exception("Unexpected parent identity");
    DateTime parentBirth = parent.StartTime;
    Dictionary<int, (SafeProcessHandle Handle, DeckyOrphanCleanup.Identity Identity)> captured = [];
    HashSet<(int Pid, long Birth)> seen = [];
    Stopwatch observation = Stopwatch.StartNew();
    string started = DateTime.UtcNow.ToString("o");
    Console.WriteLine("Read-only child observer started " + started);
    while (observation.Elapsed.TotalSeconds < 45)
    {
        foreach (Process process in Process.GetProcessesByName("powershell"))
        {
            using (process)
            {
                try
                {
                    long birth = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                    if (!seen.Add((process.Id, birth))) continue;
                    SafeProcessHandle query = Native.OpenProcess(0x1000, false, process.Id);
                    var child = DeckyOrphanCleanup.ReadIdentity(query, process.Id);
                    if (child?.ParentPid == parentPid && child.CommandLine.Contains("PluginLoader") && child.CommandLine.Contains("Get-CimInstance")) captured[process.Id] = (query, child);
                    else query.Dispose();
                }
                catch { }
            }
        }
        Thread.Sleep(100);
    }
    List<object> rows = []; double total = 0;
    foreach (var item in captured.Values)
    {
        using (item.Handle)
        {
            if (!Native.GetProcessTimes(item.Handle, out long birth, out long exited, out long kernel, out long user)) continue;
            double cpu = (kernel + user) / 10000000.0; total += cpu;
            rows.Add(new { item.Identity.Pid, item.Identity.CommandLine, CpuSeconds = cpu, KernelSeconds=kernel/10000000.0, UserSeconds=user/10000000.0, Exited=exited != 0, StartedUtc=DateTime.FromFileTimeUtc(birth).ToString("o") });
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new { StartedUtc=started, ParentPid=parentPid, ParentBirth=parentBirth, ObservationSeconds=observation.Elapsed.TotalSeconds, ChildCpuSeconds=total, ChildCpuPercentOneLogicalCore=100*total/observation.Elapsed.TotalSeconds, Children=rows, ReadOnly=true, Limits="100-ms read-only observer can miss a subprocess that exits before its first handle is captured. Parent CPU is separate; root may be using UI during this observation." },new JsonSerializerOptions{WriteIndented=true}));
    return;
}
if (args.Length > 0 && args[0].StartsWith("--multiprocessing-fork")) { Thread.Sleep(60000); return; }
if (args.Length > 0 && args[0] == "--fixture-spawn-orphan")
{
    using Process child = Start(executable, "--multiprocessing-fork", $"parent_pid={Environment.ProcessId}", "pipe_handle=1");
    File.WriteAllText(args[1], child.Id.ToString());
    return;
}
if (args.Length > 0 && args[0] == "--fixture-bootstrap") { Thread.Sleep(60000); return; }

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
List<Process> children = [];
string folder = Path.Combine(Path.GetTempPath(), "Playhub-orphan-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string[] allowed = [executable];
try
{
    using WindowsIdentity user = WindowsIdentity.GetCurrent();
    using Process self = Process.GetCurrentProcess();
    string sid = user.User!.Value; uint session = (uint)self.SessionId;
    Process healthy = Start(executable, "--multiprocessing-fork", $"parent_pid={Environment.ProcessId}", "pipe_handle=1"); children.Add(healthy);
    Process malformed = Start(executable, "--multiprocessing-fork-extra", $"parent_pid={Environment.ProcessId}", "pipe_handle=1"); children.Add(malformed);
    Process bootstrap = Start(executable, "--fixture-bootstrap"); children.Add(bootstrap);
    Thread.Sleep(120);
    Check(DeckyOrphanCleanup.Inspect(allowed).Count == 0, "Healthy loader child and bootstrap are preserved by actual process snapshot");
    using SafeProcessHandle handle = Native.OpenProcess(0x1000, false, healthy.Id);
    var identity = DeckyOrphanCleanup.ReadIdentity(handle, healthy.Id) ?? throw new Exception("Unreadable fixture identity");
    Check(identity.ParentPid == Environment.ProcessId, "Native command line, parent, birth, session and owner SID can be read from a held fixture handle");
    Check(DeckyOrphanCleanup.IsOwnedFork(identity, allowed, sid, session), "Exact owned multiprocessing fork accepted");
    Check(DeckyOrphanCleanup.IsOwnedFork(identity, [executable.ToUpperInvariant()], sid, session), "Exact image path uses Windows case-insensitive semantics");
    string spaced = "C:\\Fixture with spaces\\PluginLoader.exe";
    Check(DeckyOrphanCleanup.IsOwnedFork(identity with { Path=spaced, CommandLine=$"\"{spaced}\" \"--multiprocessing-fork\" \"pipe_handle=1\" \"parent_pid={identity.ParentPid}\"" }, [spaced], sid, session), "Quoted spaces and supported reversed argument order are parsed natively");
    Check(!DeckyOrphanCleanup.IsOwnedFork(identity with { Sid = "S-1-5-18" }, allowed, sid, session), "Different user rejected");
    Check(!DeckyOrphanCleanup.IsOwnedFork(identity with { Session = session + 1 }, allowed, sid, session), "Different session rejected");
    Check(!DeckyOrphanCleanup.IsOwnedFork(identity, [executable + ".other"], sid, session), "Other executable path rejected");
    Check(!DeckyOrphanCleanup.IsOwnedFork(identity with { Birth = 0 }, allowed, sid, session), "Unknown birth rejected");
    Check(!DeckyOrphanCleanup.IsOwnedFork(identity with { CommandLine = identity.CommandLine.Replace("--multiprocessing-fork", "--multiprocessing-fork-extra") }, allowed, sid, session), "Substring fork lookalike rejected");
    Check(!DeckyOrphanCleanup.IsOwnedFork(identity with { CommandLine = identity.CommandLine + " unexpected" }, allowed, sid, session), "Unexpected extra arguments rejected");
    Check(!DeckyOrphanCleanup.IsOwnedFork(identity with { CommandLine = identity.CommandLine.Replace("parent_pid=" + Environment.ProcessId, "parent_pid=1") }, allowed, sid, session), "Declared parent must match native parent");
    Check(!DeckyOrphanCleanup.IsOwnedFork(identity with { CommandLine = identity.CommandLine.Replace("pipe_handle=1", "pipe_handle=0") }, allowed, sid, session), "Invalid pipe handle rejected");
    Check(!DeckyOrphanCleanup.ParentEvidenceAllowsCleanup(100, 90, 0, executable), "Live original loader parent preserves its worker");
    Check(!DeckyOrphanCleanup.ParentEvidenceAllowsCleanup(100, 90, 0, "C:\\Fixture\\PluginLoader_versioned.exe"), "Existing parent-name aliases are conservatively preserved");
    Check(DeckyOrphanCleanup.ParentEvidenceAllowsCleanup(100, 110, 0, executable), "Recycled parent PID with later birth does not hide an orphan");
    Check(!DeckyOrphanCleanup.ParentEvidenceAllowsCleanup(100, null, 0, null), "Unreadable parent identity is preserved");
    Check(!DeckyOrphanCleanup.ParentEvidenceAllowsCleanup(100, 90, 0, null), "Unreadable live parent path is preserved");
    Check(DeckyOrphanCleanup.ParentEvidenceAllowsCleanup(100, 90, 120, executable), "Exited original parent is recognized");
    Check(DeckyOrphanCleanup.ParentEvidenceAllowsCleanup(100, 90, 0, "C:\\Windows\\cmd.exe"), "Known unrelated parent is recognized");
    Check(!DeckyOrphanCleanup.TryTerminate(identity with { Birth = identity.Birth + 1 }, allowed) && !healthy.HasExited, "Held termination handle rejects changed process birth");
    Check(!DeckyOrphanCleanup.TryTerminate(identity, allowed) && !healthy.HasExited, "Immediate parent revalidation prevents killing healthy fixture");

    string ready = Path.Combine(folder, "orphan-pid");
    using (Process parent = Start(executable, "--fixture-spawn-orphan", ready))
    { Check(parent.WaitForExit(5000) && parent.ExitCode == 0, "Disposable original parent exits naturally"); }
    int orphanPid = int.Parse(File.ReadAllText(ready));
    Process orphan = Process.GetProcessById(orphanPid); children.Add(orphan);
    Thread.Sleep(120);
    var found = DeckyOrphanCleanup.Inspect(allowed);
    Check(found.Count == 1 && found[0].Pid == orphanPid, "Actual orphan fixture is selected while healthy siblings survive");
    Check(DeckyOrphanCleanup.Cleanup([executable + ".other"]) == 0 && !orphan.HasExited, "Real unowned-path fixture is never terminated");
    Check(DeckyOrphanCleanup.Cleanup(allowed) == 1 && orphan.WaitForExit(3000), "Production cleanup terminates only the exact disposable orphan through its verified handle");
    Check(!healthy.HasExited && !malformed.HasExited && !bootstrap.HasExited, "Healthy, malformed and bootstrap fixture processes remain running");
    Check(DeckyOrphanCleanup.Cleanup(allowed) == 0, "Repeated cleanup is idempotent");

    // Read-only production snapshot benchmark: no termination or real cleanup.
    DeckyOrphanCleanup.Inspect(allowed);
    double cpuBefore = self.TotalProcessorTime.TotalSeconds;
    Stopwatch watch = Stopwatch.StartNew();
    const int iterations = 40;
    for (int i = 0; i < iterations; i++) DeckyOrphanCleanup.Inspect(allowed);
    self.Refresh(); double nativeCpu = self.TotalProcessorTime.TotalSeconds - cpuBefore;
    double nativeWall = watch.Elapsed.TotalSeconds;
    string query = "$items = @(Get-CimInstance Win32_Process -Filter \"Name='PluginLoader_noconsole.exe' OR Name='PluginLoader.exe'\" -ErrorAction SilentlyContinue); $ids=@{}; foreach($p in $items){$ids[[int]$p.ProcessId]=$true}; $orphan=0; foreach($p in $items){if($p.CommandLine -notlike '*--multiprocessing-fork*'){continue}; if(-not $ids.ContainsKey([int]$p.ParentProcessId)){ $parent=Get-Process -Id $p.ParentProcessId -ErrorAction SilentlyContinue; if($null -eq $parent -or $parent.ProcessName -notlike 'PluginLoader*'){$orphan++}}}; [Console]::Out.WriteLine($orphan)";
    List<object> legacy = [];
    for (int i = 0; i < 3; i++)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true };
        foreach(string argument in new[]{"-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", query}) start.ArgumentList.Add(argument);
        watch.Restart(); using Process p = Process.Start(start)!; _ = p.Handle;
        string output=p.StandardOutput.ReadToEnd(), error=p.StandardError.ReadToEnd(); p.WaitForExit();
        Check(p.ExitCode == 0 && string.IsNullOrWhiteSpace(error), "Read-only legacy PowerShell/CIM subprocess benchmark succeeds");
        legacy.Add(new { CpuSeconds=p.TotalProcessorTime.TotalSeconds, WallSeconds=watch.Elapsed.TotalSeconds, Result=output.Trim() });
    }
    Console.WriteLine(JsonSerializer.Serialize(new { Checks=checks, NativeIterations=iterations, NativeCpuSeconds=nativeCpu, NativeWallSeconds=nativeWall, NativeMeanWallMs=1000*nativeWall/iterations, NativeMeanCpuMs=1000*nativeCpu/iterations, LegacyReadOnlySubprocesses=legacy, NoLiveTermination=true, Scope="Only fixture executable path was permitted for real cleanup. Legacy benchmark omits Stop-Process and snapshots are read-only." }, new JsonSerializerOptions{WriteIndented=true}));
}
finally
{
    foreach (Process process in children) { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } process.Dispose(); }
    string full = Path.GetFullPath(folder), temp = Path.GetFullPath(Path.GetTempPath());
    if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("Playhub-orphan-checks-")) Directory.Delete(full, true);
}

static Process Start(string exe, params string[] arguments)
{ var info = new ProcessStartInfo(exe) { UseShellExecute=false,CreateNoWindow=true }; foreach(string argument in arguments)info.ArgumentList.Add(argument); return Process.Start(info)!; }
static class Native
{
    [DllImport("kernel32.dll")] internal static extern SafeProcessHandle OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")] internal static extern bool GetProcessTimes(SafeProcessHandle process,out long birth,out long exit,out long kernel,out long user);
}
