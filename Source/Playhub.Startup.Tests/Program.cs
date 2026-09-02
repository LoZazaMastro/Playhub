using System.Diagnostics;
using Playhub.Services;

WinRT.ComWrappersSupport.InitializeComWrappers();
if (args.Length == 2)
{
    using var service = new SingleInstanceService();
    var primary = await service.RegisterAsync(args[1], (_, _) => Console.WriteLine("ACTIVATED"));
    Console.WriteLine(primary ? "PRIMARY" : "REDIRECTED");
    if (primary) await Console.In.ReadLineAsync();
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
    await Stop(first);
    using var next = Start("primary");
    try { await ExpectLine(next, "PRIMARY"); }
    finally { await Stop(next); }
    Console.WriteLine("PASS normal restart obtains primary ownership after exit");
    return 0;
}
finally { await Stop(first); }
