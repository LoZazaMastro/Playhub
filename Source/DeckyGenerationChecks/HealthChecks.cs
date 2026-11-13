using Playhub.DeckyHost;
using System.Runtime.InteropServices;

namespace DeckyGenerationChecks;

internal static class HealthChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; Console.WriteLine("PASS " + name); }
        var runtime = new DeckyProcessTree.Identity(200, 100, 1000, @"C:\homebrew\PluginLoader_noconsole.exe", "owner", 1);
        DeckyHealthSample Sample(long at, double core = 1, DeckyProcessTree.Identity? actual = null) => new(at, (long)(at * 10_000 * core), actual ?? runtime);
        var policy = new DeckyHealthPolicy(0);
        Check(!policy.ObserveNetwork(Sample(40_000), true, true), "Network failure alone cannot authorize a reset");
        Check(!policy.ObserveCpu(Sample(26_000)), "The initial CPU sample cannot be considered a delta");
        Check(!policy.ObserveCpu(Sample(28_000)), "Startup grace prevents recovery even for a busy runtime");
        for (int i = 0; i < 3; i++)
        {
            var sample = Sample(30_000 + i * 2_000);
            Check(policy.ObserveCpu(sample), "Sustained one-core usage permits a bounded health probe");
            Check(policy.ObserveNetwork(sample, true, true) == (i == 2), "Exactly three consecutive complete failures are required");
        }
        Check(policy.IsFresh(runtime, 34_500), "The proven runtime is fresh immediately after the third failed probe");
        Check(!policy.IsFresh(runtime with { Birth = 1001 }, 34_500), "A reused runtime PID cannot reuse a health proof");
        Check(!policy.IsFresh(runtime, 35_501), "A stale failure proof is rejected before stopping a tree");
        Check(!policy.IsFresh(runtime, 33_999), "A backwards timestamp cannot authorize recovery");
        var next = Sample(36_000);
        Check(policy.ObserveCpu(next) && !policy.ObserveNetwork(next, false, true), "Any listener, including a foreign one, vetoes recovery");
        next = Sample(38_000);
        Check(policy.ObserveCpu(next) && !policy.ObserveNetwork(next, null, true), "An unreadable TCP table vetoes recovery");
        next = Sample(40_000);
        Check(policy.ObserveCpu(next) && !policy.ObserveNetwork(next, true, false), "An HTTP reply proves the server loop is responsive");
        Check(!policy.IsFresh(runtime, 40_000), "A responsive server revokes the previous failure proof");
        next = Sample(42_000);
        Check(policy.ObserveCpu(next) && !policy.ObserveNetwork(next, true, true), "A healthy observation restarts the three-sample sequence");
        next = new(44_000, next.CpuTicks, runtime);
        Check(!policy.ObserveCpu(next), "An idle runtime never gets an HTTP probe or recovery");
        Check(!policy.ObserveNetwork(next, true, true), "A slow plugin with low runtime CPU cannot authorize recovery");
        next = Sample(46_000, actual: runtime with { Birth = 2000 });
        Check(!policy.ObserveCpu(next), "A new runtime birth starts with a new CPU baseline");
        next = Sample(60_000, actual: runtime with { Birth = 2000 });
        Check(!policy.ObserveCpu(next), "A delayed host tick is not treated as consecutive failure evidence");
        policy.Clear();
        Check(!policy.IsFresh(runtime, 60_000) && !policy.ObserveCpu(Sample(62_000)), "Steam exit or uncertain identity clears all health evidence");
        Check(!policy.ObserveCpu(new(64_000, 1, runtime)), "CPU counter regression cannot prove a busy runtime");
        var steam = new SteamGenerationIdentity(300, 3000, @"C:\Steam\steam.exe", "owner", 1);
        Check(DeckyHealthPolicy.CanReset(steam, steam, null, true, true, true, false), "A fresh complete failure permits one recovery in the current Steam generation");
        Check(!DeckyHealthPolicy.CanReset(steam, steam, steam, true, true, true, false), "A second health recovery in the same Steam generation is forbidden");
        var newSteam = steam with { Birth = 3001 };
        Check(DeckyHealthPolicy.CanReset(newSteam, newSteam, steam, true, true, true, false), "A genuinely new Steam generation receives its own bounded recovery budget");
        Check(!DeckyHealthPolicy.CanReset(steam, newSteam, null, true, true, true, false), "Changed Steam evidence revokes a pending health recovery");
        Check(!DeckyHealthPolicy.CanReset(steam, steam, null, false, true, true, false), "A stopped Steam process prevents health recovery");
        Check(!DeckyHealthPolicy.CanReset(steam, steam, null, true, false, true, false), "An already-stopped loader cannot be resurrected by health recovery");
        Check(!DeckyHealthPolicy.CanReset(steam, steam, null, true, true, false, false), "Lost or stale runtime evidence prevents recovery");
        Check(!DeckyHealthPolicy.CanReset(steam, steam, null, true, true, true, true), "Windows shutdown prevents health recovery");
        Check(DeckyHealthPolicy.SupportsEndpoint(null, null) && DeckyHealthPolicy.SupportsEndpoint("127.0.0.1", "1337"),
            "Only the default or explicitly identical upstream loopback endpoint is probed");
        Check(!DeckyHealthPolicy.SupportsEndpoint("192.0.2.1", null) && !DeckyHealthPolicy.SupportsEndpoint(null, "1338"),
            "Custom upstream host or port disables health recovery instead of probing the wrong endpoint");
        Check(DeckyHealthMonitor.OpenRuntime(runtime, _ => throw new ArgumentException("Process already exited")) is null,
            "A child exit between snapshot and opening is an uncertain sample, not a failed maintenance task");
        Microsoft.Win32.SafeHandles.SafeProcessHandle? opened = null;
        Check(DeckyHealthMonitor.OpenRuntime(runtime, _ =>
        {
            var process = System.Diagnostics.Process.GetCurrentProcess(); opened = process.SafeHandle; return process;
        }, _ => throw new IOException("Attestation failed")) is null && opened?.IsClosed == true,
            "A failed runtime attestation releases the retained process handle without terminating a process");
        foreach (int family in new[] { 2, 23 })
        {
            int rowSize = family == 2 ? 24 : 56;
            nint data = Marshal.AllocHGlobal(4 + rowSize);
            try
            {
                Marshal.Copy(new byte[4 + rowSize], 0, data, 4 + rowSize);
                Check(DeckyHealthMonitor.TableAbsent(data, 4, family) == true, "An empty native TCP listener table is parsed without assuming an owner");
                Marshal.WriteInt32(data, 1);
                Marshal.WriteInt32(data + 4, family == 2 ? 0 : 48, 2);
                Marshal.WriteInt16(data + 4, family == 2 ? 8 : 20, System.Net.IPAddress.HostToNetworkOrder((short)1337));
                Check(DeckyHealthMonitor.TableAbsent(data, 4 + rowSize, family) == false, "IPv4 or IPv6 port1337 listener vetoes recovery regardless of its PID");
                Marshal.WriteInt32(data, 2);
                Check(DeckyHealthMonitor.TableAbsent(data, 4 + rowSize, family) == null, "Truncated native listener rows cannot prove absence");
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        Console.WriteLine($"{checks}/{checks} Decky health checks passed.");
        return checks;
    }
}
