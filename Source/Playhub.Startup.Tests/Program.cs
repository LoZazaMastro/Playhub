using System.Diagnostics;
using Playhub.Services;

WinRT.ComWrappersSupport.InitializeComWrappers();
if (args.Length == 2)
{
    if (args[0] == "dead-owner") { await Task.Delay(200); return 0; }
    using var service = new SingleInstanceService();
    bool primary;
    try
    {
        primary = await service.RegisterAsync(args[1], (_, _) =>
        {
            if (args[0] == "unresponsive-primary") Thread.Sleep(8000);
            Console.WriteLine("ACTIVATED");
        }, args[0] == "recovery-secondary" ? message =>
        {
            if (message.StartsWith("Single-instance redirect begin", StringComparison.Ordinal)) Console.WriteLine("REDIRECTBEGIN");
        } : null);
    }
    catch (TimeoutException) when (args[0] == "timeout-secondary") { Console.WriteLine("TIMEOUT"); return 0; }
    Console.WriteLine(primary ? "PRIMARY" : "REDIRECTED");
    if (primary && await Console.In.ReadLineAsync() == "CLOSE")
    {
        service.Dispose();
        Console.WriteLine("KEYRELEASED");
        await Console.In.ReadLineAsync();
    }
    return primary && args[0] == "secondary" ? 1 : 0;
}

var key = "Playhub.StartupTest." + Guid.NewGuid().ToString("N");
Process Start(string role)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
    };
    start.ArgumentList.Add(role); start.ArgumentList.Add(key);
    return Process.Start(start)!;
}
async Task ExpectLine(Process process, string expected)
{
    var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
    if (line != expected) throw new Exception($"Expected {expected}, received {line}; exit={process.HasExited}");
}
async Task Stop(Process process)
{
    if (process.HasExited) return;
    await process.StandardInput.WriteLineAsync("STOP");
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
}

var neverAcknowledged = new TaskCompletionSource();
var timer = Stopwatch.StartNew();
try
{
    await SingleInstanceRedirect.WaitAsync(neverAcknowledged.Task, () => false, TimeSpan.FromMilliseconds(200));
    throw new Exception("Unresponsive live owner was accepted");
}
catch (TimeoutException) { if (timer.ElapsedMilliseconds > 1500) throw; }
Console.WriteLine("PASS unacknowledged live-owner redirect has a bounded timeout");
using (var deadOwner = Start("dead-owner"))
{
    _ = deadOwner.Handle;
    if (await SingleInstanceRedirect.WaitAsync(neverAcknowledged.Task, () => deadOwner.HasExited, TimeSpan.FromSeconds(3)))
        throw new Exception("Exited owner was treated as acknowledged");
    Console.WriteLine("PASS retained exact process exit permits reclaim without waiting for redirect acknowledgment");
}
await SingleInstanceRedirect.WaitAsync(Task.CompletedTask, () => false, TimeSpan.FromSeconds(1));
try
{
    await SingleInstanceRedirect.WaitAsync(Task.FromException(new IOException("fixture")), () => false, TimeSpan.FromSeconds(1));
    throw new Exception("Redirect failure was ignored");
}
catch (IOException) { }
Console.WriteLine("PASS acknowledgment and real redirect errors retain their results");

var copyRoot = Path.Combine(Path.GetTempPath(), "Playhub-StartupCopy", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(copyRoot);
var bundled = Path.Combine(copyRoot, "bundled.exe");
var installed = Path.Combine(copyRoot, "QuickSettingsAgent.exe");
File.WriteAllBytes(bundled, [1, 2, 3, 4]);
File.WriteAllBytes(installed, [1, 2, 3, 4]);
using (var locked = File.Open(installed, FileMode.Open, FileAccess.Read, FileShare.Read))
{
    BundledExecutableCopy.Copy(bundled, installed);
    File.WriteAllBytes(bundled, [4, 3, 2, 1]);
    try { BundledExecutableCopy.Copy(bundled, installed); throw new Exception("Changed locked executable was silently accepted"); }
    catch (IOException) { }
}
if (!File.ReadAllBytes(installed).SequenceEqual(new byte[] { 1, 2, 3, 4 })) throw new Exception("Locked executable changed");
BundledExecutableCopy.Copy(bundled, installed);
if (!File.ReadAllBytes(installed).SequenceEqual(new byte[] { 4, 3, 2, 1 })) throw new Exception("Unlocked executable was not updated");
Console.WriteLine("PASS identical locked executable is skipped; changed locked file fails and unlocked update copies exact bytes");

using var first = Start("primary");
try
{
    await ExpectLine(first, "PRIMARY");
    for (var i = 0; i < 40; i++)
    {
        using var second = Start("secondary");
        await ExpectLine(second, "REDIRECTED");
        await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if (second.ExitCode != 0) throw new Exception("Secondary instance became primary");
        await ExpectLine(first, "ACTIVATED");
    }
    Console.WriteLine("PASS 40 repeated launches redirect to one primary instance");
    var burst = Enumerable.Range(0, 8).Select(_ => Start("secondary")).ToArray();
    try
    {
        foreach (var child in burst)
        {
            await ExpectLine(child, "REDIRECTED");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (child.ExitCode != 0) throw new Exception("Concurrent launch created a primary instance");
            await ExpectLine(first, "ACTIVATED");
        }
    }
    finally { foreach (var child in burst) { await Stop(child); child.Dispose(); } }
    Console.WriteLine("PASS 8 simultaneous launches redirect without duplicate owners");
    await first.StandardInput.WriteLineAsync("CLOSE");
    await ExpectLine(first, "KEYRELEASED");
    using var next = Start("primary");
    try { await ExpectLine(next, "PRIMARY"); }
    finally { await Stop(next); }
    Console.WriteLine("PASS actual close unregisters key while old process still lives; new primary is accepted");
    await Stop(first);
    using var unresponsive = Start("unresponsive-primary");
    try
    {
        await ExpectLine(unresponsive, "PRIMARY");
        using var secondary = Start("timeout-secondary");
        await ExpectLine(secondary, "TIMEOUT");
        await secondary.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        if (secondary.ExitCode != 0 || unresponsive.HasExited) throw new Exception("Live primary was replaced or terminated");
        Console.WriteLine("PASS real SDK unresponsive owner bounds secondary wait and preserves sole live primary");
        using var recovery = Start("recovery-secondary");
        try
        {
            await ExpectLine(recovery, "REDIRECTBEGIN");
            await Stop(unresponsive);
            await ExpectLine(recovery, "PRIMARY");
            Console.WriteLine("PASS owner exit during actual SDK redirect lets the same waiting process reclaim primary");
        }
        finally { await Stop(recovery); }
    }
    finally { await Stop(unresponsive); }
    using var restarted = Start("primary");
    try { await ExpectLine(restarted, "PRIMARY"); }
    finally { await Stop(restarted); }
    Console.WriteLine("PASS normal restart obtains primary ownership after exit");
    return 0;
}
finally { await Stop(first); }
