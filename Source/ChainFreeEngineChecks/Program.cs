using System.Text.Json;
using GamingMode.Services;

int failures = 0, count = 0;
void Check(string name, Action body)
{
    count++;
    try { body(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
void Require(bool value, string reason) { if (!value) throw new Exception(reason); }

string workspace = Path.Combine(Path.GetTempPath(), "PlayhubChainFreeEngineChecks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
string executable = Path.Combine(workspace, "Playhub.ChainFreeEngine.exe");
File.WriteAllText(executable, "fixture");
string descriptorPath = Path.Combine(workspace, ChainFreeEngineDescriptor.FileName);
void WriteDescriptor(object value) => File.WriteAllText(descriptorPath, JsonSerializer.Serialize(value));

var started = new List<FakeProcess>();
DateTimeOffset now = DateTimeOffset.UnixEpoch;
ChainFreeEngineService Build(ChainFreeEngineDescriptor descriptor, List<string>? log = null) =>
    new(descriptor, _ => { var process = new FakeProcess(); started.Add(process); return process; }, () => now, line => log?.Add(line));

var descriptor = new ChainFreeEngineDescriptor(executable, new[] { "--workspace", workspace });

Check("no descriptor file means the engine stays off", () =>
    Require(ChainFreeEngineDescriptor.Read(Path.Combine(workspace, "absent.json")) is null, "a missing file must not enable the engine"));

Check("a descriptor pointing at a missing executable is refused", () =>
{
    WriteDescriptor(new { executable = Path.Combine(workspace, "not-here.exe") });
    Require(ChainFreeEngineDescriptor.Read(descriptorPath) is null, "a missing executable must not be launched");
});

Check("enabled:false keeps the engine off", () =>
{
    WriteDescriptor(new { enabled = false, executable });
    Require(ChainFreeEngineDescriptor.Read(descriptorPath) is null, "the switch must be honoured");
});

Check("a valid descriptor is read with its arguments", () =>
{
    WriteDescriptor(new { executable, arguments = new[] { "--workspace", workspace, "--plugin-build", "dist" } });
    var read = ChainFreeEngineDescriptor.Read(descriptorPath);
    Require(read is not null, "the descriptor should have been read");
    Require(read!.Arguments.Count == 4, "arguments were lost");
    Require(File.Exists(read.ExecutablePath), "the executable path must resolve");
});

Check("a full Playhub installation configures itself", () =>
{
    string install = Path.Combine(workspace, "install");
    string profile = Path.Combine(workspace, "profile");
    string engineDir = Path.Combine(install, "ChainFreeEngine");
    Directory.CreateDirectory(Path.Combine(engineDir, "Renderer"));
    Directory.CreateDirectory(Path.Combine(engineDir, "Python"));
    string pluginRoot = Path.Combine(profile, "homebrew", "plugins", "gaming-mode");
    Directory.CreateDirectory(Path.Combine(pluginRoot, "dist"));
    Directory.CreateDirectory(Path.Combine(profile, "homebrew", "settings", "gaming-mode"));
    Require(ChainFreeEngineDescriptor.Discover(new[] { install }, profile) is null, "an incomplete installation must not be started");
    File.WriteAllText(Path.Combine(engineDir, "Playhub.ChainFreeEngine.exe"), "fixture");
    File.WriteAllText(Path.Combine(engineDir, "Renderer", "playhub-standalone.js"), "fixture");
    File.WriteAllText(Path.Combine(engineDir, "Python", "python.exe"), "fixture");
    var found = ChainFreeEngineDescriptor.Discover(new[] { install }, profile);
    Require(found is not null, "a complete installation should configure itself");
    Require(found!.Arguments.Contains("--allow-installed-plugin"), "the installed layout must be declared");
    Require(found.Arguments.Contains(Path.Combine(pluginRoot, "dist")), "the plugin build should be served from the installed plugin");
    Require(found.ExecutablePath.EndsWith("Playhub.ChainFreeEngine.exe"), "the engine executable should be resolved");
});

Check("a new Steam gets a new engine", () =>
{
    started.Clear();
    using var service = Build(descriptor);
    service.SetSteam(100);
    service.SetSteam(100);
    Require(started.Count == 1, "the same Steam must not be served twice");
    service.SetSteam(200);
    Require(started.Count == 2 && started[0].Stopped, "a restarted Steam needs a fresh engine");
});

Check("the engine is looked for where Playhub actually installs it", () =>
{
    string install = Path.Combine(workspace, "install");
    string profile = Path.Combine(workspace, "profile");
    var roots = ChainFreeEngineDescriptor.DefaultInstallRoots(Path.Combine(install, "Plugins", "Gaming Mode", "gaming-mode-win-x64")).ToList();
    Require(roots.Contains(install), "the installation folder above the agent must be searched, got: " + string.Join(", ", roots));
    Require(roots.Count >= 5, "the usual installation folders should be searched too");
    Require(ChainFreeEngineDescriptor.Discover(roots, profile) is not null, "the engine installed under the app should be found");
});

Check("the engine starts with Steam and stops with it", () =>
{
    started.Clear();
    using var service = Build(descriptor);
    Require(!service.Running, "nothing should run before Steam");
    service.SetSteam(4242);
    Require(service.Running && started.Count == 1, "the engine should start with Steam");
    service.SetSteam(4242);
    Require(started.Count == 1, "a second notification must not start a second engine");
    service.SetSteam(null);
    Require(!service.Running && started[0].Stopped, "closing Steam must stop the engine");
});

Check("a crash during the session is restarted", () =>
{
    started.Clear();
    using var service = Build(descriptor);
    service.SetSteam(4242);
    started[0].HasExited = true;
    service.Poll();
    Require(started.Count == 2 && service.Running, "the engine should be restarted while Steam is up");
});

Check("a crash loop is not fought forever", () =>
{
    started.Clear();
    var log = new List<string>();
    using var service = Build(descriptor, log);
    service.SetSteam(4242);
    for (int i = 0; i < 10; i++) { started[^1].HasExited = true; service.Poll(); }
    Require(started.Count == ChainFreeEngineService.MaxRestartsPerWindow, "restarts should be capped, got " + started.Count);
    Require(log.Exists(line => line.Contains("restart budget exhausted")), "the cap should be reported");
    now += ChainFreeEngineService.RestartWindow + TimeSpan.FromMinutes(1);
    service.Poll();
    Require(started.Count == ChainFreeEngineService.MaxRestartsPerWindow + 1, "a later session should be allowed to start again");
});

Check("nothing is restarted once Steam is gone", () =>
{
    started.Clear();
    using var service = Build(descriptor);
    service.SetSteam(4242);
    service.SetSteam(null);
    started[0].HasExited = true;
    service.Poll();
    Require(started.Count == 1, "a stopped session must not be revived");
});

Check("disposing the agent stops and releases the engine", () =>
{
    started.Clear();
    var service = Build(descriptor);
    service.SetSteam(4242);
    service.Dispose();
    Require(started[0].Stopped && started[0].Disposed, "the engine must be stopped and released");
    service.SetSteam(4242);
    Require(started.Count == 1, "a disposed service must not start anything");
});

Check("a launch failure is contained", () =>
{
    var log = new List<string>();
    using var service = new ChainFreeEngineService(descriptor, _ => throw new IOException("denied"), () => now, line => log.Add(line));
    service.SetSteam(4242);
    Require(!service.Running, "a failed launch must not look like a running engine");
    Require(log.Exists(line => line.Contains("could not start")), "the failure should be reported");
});

Directory.Delete(workspace, true);
Console.WriteLine($"RESULT {count - failures}/{count} passed.");
return failures == 0 ? 0 : 1;

sealed class FakeProcess : IChainFreeEngineProcess
{
    public bool HasExited { get; set; }
    public bool Stopped { get; private set; }
    public bool Disposed { get; private set; }
    public void Stop() { Stopped = true; HasExited = true; }
    public void Dispose() => Disposed = true;
}
