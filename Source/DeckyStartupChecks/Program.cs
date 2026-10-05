using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Playhub.Shared;

if (args.Contains("--inspect-existing"))
{
    Console.WriteLine("Existing listener reusable: " + DeckyStartupGuard.ReuseListener(Console.WriteLine));
    return;
}
if (args.Contains("--reuse-probe"))
{
    using var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    int probePort = ((IPEndPoint)probe.LocalEndpoint).Port;
    if (!DeckyStartupGuard.ReuseListener(null, probePort) || DeckyStartupGuard.ListenerOwner(probePort) != Environment.ProcessId)
        throw new Exception("Existing Decky listener was not reused");
    Console.WriteLine("Existing Decky listener reused without replacement");
    return;
}
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
Check(DeckyStartupGuard.ListenerOwner(port) == Environment.ProcessId, "Real IPv4 listener owner mismatch");
try { DeckyStartupGuard.ReuseListener(null, port); throw new Exception("Foreign process was accepted as Decky"); }
catch (IOException error) { Check(error.Message.Contains("left untouched"), "Foreign owner diagnostic missing"); }
Check(DeckyStartupGuard.ListenerOwner(port) == Environment.ProcessId, "Foreign listener was stopped");
listener.Stop();
Check(DeckyStartupGuard.ListenerOwner(port) is null, "Closed listener still detected");
using var ipv6 = new TcpListener(IPAddress.IPv6Loopback, 0);
ipv6.Start();
Check(DeckyStartupGuard.ListenerOwner(((IPEndPoint)ipv6.LocalEndpoint).Port) == Environment.ProcessId, "Real IPv6 listener owner mismatch");
int active = 0, maximum = 0;
Task.WaitAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => DeckyStartupGuard.RunExclusive(() => {
    int now = Interlocked.Increment(ref active); maximum = Math.Max(maximum, now);
    Thread.Sleep(5); Interlocked.Decrement(ref active); return true;
}))).ToArray());
Check(maximum == 1, "Concurrent startups overlapped");
Check(DeckyStartupGuard.RunExclusive(() => DeckyStartupGuard.RunExclusive(() => true)), "Nested same-thread startup deadlocked");
var folder = Path.Combine(Path.GetTempPath(), "Playhub-decky-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
var loader = Path.Combine(folder, "PluginLoader.exe");
var command = DeckyStartupGuard.CreateCommand(loader);
var script = File.ReadAllText(Path.Combine(folder, "Playhub-StartDecky.ps1"));
Check(script.Contains("Global\\Playhub.Decky.Start") && script.Contains("Test-DeckyListener"), "Autostart bypasses shared lock/listener check");
Check(command.Contains("-WindowStyle Hidden"), "Autostart window is visible");
foreach (string file in Directory.GetFiles(AppContext.BaseDirectory))
    if (Path.GetFileName(file).StartsWith("DeckyStartupChecks", StringComparison.Ordinal))
        File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), true);
File.Copy(Path.Combine(AppContext.BaseDirectory, "DeckyStartupChecks.exe"), loader, true);
using var child = Process.Start(new ProcessStartInfo(loader, "--reuse-probe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
Check(child.WaitForExit(10000) && child.ExitCode == 0, "Real existing Decky owner was not reusable: " + child.StandardError.ReadToEnd());
Console.WriteLine($"Decky startup checks: {checks} PASS; temporary helper retained at {folder}");
