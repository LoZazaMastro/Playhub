// SPDX-License-Identifier: MIT. Inert prerequisite checks; no installer/game/GUI/auth/network execution.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Playhub.StoreLibrary;

var root = Path.Combine(Path.GetTempPath(), "Playhub-AmazonSetup-Checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var checks = 0;
void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
void Refuses(Action action, string code)
{ try { action(); throw new Exception("accepted: " + code); } catch (StoreOperationException e) { Check(e.Code == code, code + " received " + e.Code); } }
if (args.Length == 3 && args[0] == "--actual-metadata-readonly")
{
    var path = new StoreInstallFileSystem().CanonicalDestination(args[1]);
    var manifest = AmazonManifest.Parse(File.ReadAllBytes(args[2]));
    var marker = File.ReadAllText(Path.Combine(path, ".playhub-install-owner"));
    var journal = Path.Combine(root, "actual-metadata-proof.json");
    var started = DateTimeOffset.UtcNow;
    try { AmazonPostInstall.Verify(Path.GetFileNameWithoutExtension(args[2]), path, Guid.ParseExact(marker, "D"), manifest, journal, default, new AmazonWindowsSetupProbe(false)); }
    catch (StoreOperationException error) { Console.WriteLine(JsonSerializer.Serialize(new { error = error.Code, runtime = error.Data["Runtime"], minimum = error.Data["Minimum"], observed = error.Data["Observed"] })); Environment.ExitCode = 1; return; }
    using var data = JsonDocument.Parse(File.ReadAllText(journal));
    Console.WriteLine(JsonSerializer.Serialize(new { root, verifiedSteps = data.RootElement.GetProperty("Steps").GetArrayLength(), verifiedArtifacts = data.RootElement.GetProperty("Artifacts").GetArrayLength(),
        runtimeProofs = data.RootElement.GetProperty("Steps").EnumerateArray().Sum(s => s.GetProperty("Runtimes").GetArrayLength()), elapsedMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds,
        noInstallerOrGameExecuted = true, noInstalledOrSourceFilesChanged = true }));
    return;
}
try
{
    Fixture New(string name, string? fuel = null) => new(Path.Combine(root, name), fuel);
    var f = New("satisfied"); f.Verify();
    using (var receipt = JsonDocument.Parse(File.ReadAllText(f.Journal)))
    {
        Check(receipt.RootElement.GetProperty("OwnerJob").GetGuid() == f.Job && receipt.RootElement.GetProperty("ManifestHash").GetString() == Convert.ToHexString(f.Manifest.RawHash), "receipt bound to exact owner job and signed-manifest hash");
        Check(receipt.RootElement.GetProperty("Steps")[0].GetProperty("Runtimes").GetArrayLength() == 2, "VC proof records both required C and C++ runtimes");
    }
    f.Verify(); Check(File.Exists(f.Journal), "already satisfied prerequisites verified again idempotently without execution");
    var sfx = New("self-extractor", Fixture.Fuel("vcredist_x86.exe").Replace("'/q','/norestart'", "'/Q'"));
    sfx.Probe.Description = "Win32 Cabinet Self-Extractor  "; sfx.Probe.InstallerVersion = new(6, 0, 2900, 2180);
    sfx.Verify(); Check(sfx.Probe.Runs == 1 && sfx.Probe.Arguments.SequenceEqual(new[] { "/Q" }), "manifest-covered SFX uses exact declared argument through setup adapter");
    var failedSfx = New("failed-self-extractor", Fixture.Fuel("vcredist_x86.exe").Replace("'/q','/norestart'", "'/Q'"));
    failedSfx.Probe.Description = sfx.Probe.Description; failedSfx.Probe.InstallerVersion = sfx.Probe.InstallerVersion; failedSfx.Probe.ExitCode = 1603;
    Refuses(() => failedSfx.Verify(), "amazon_setup_installer_failed"); Check(!File.Exists(failedSfx.Journal), "failed SFX cannot authorize completion");
    var missing = New("missing"); missing.Probe.Missing = true;
    Refuses(() => missing.Verify(), "amazon_setup_runtime_missing"); Check(!File.Exists(missing.Journal), "missing runtime never receives ready receipt");
    var old = New("old-runtime"); old.Probe.Version = new(12, 0, 1, 0); Refuses(() => old.Verify(), "amazon_setup_runtime_missing");
    var wrongArch = New("wrong-runtime-arch"); wrongArch.Probe.Architecture = "x64"; Refuses(() => wrongArch.Verify(), "amazon_setup_runtime_missing");
    var runtimeMetadata = New("runtime-company-is-not-signature"); runtimeMetadata.Probe.Company = "Windows DDK provider"; runtimeMetadata.Verify(); Check(File.Exists(runtimeMetadata.Journal), "runtime file presence/architecture/version is checked without pretending CompanyName is a trust signature");
    var untrusted = New("unsigned-setup"); untrusted.Probe.Signed = false; Refuses(() => untrusted.Verify(), "amazon_setup_publisher_unverified");
    var unknown = New("unknown-setup"); unknown.Probe.Description = "Unrecognized setup"; Refuses(() => unknown.Verify(), "amazon_setup_unsupported");
    var changed = New("changed-setup"); File.WriteAllText(Path.Combine(changed.Path, "vcredist_x86.exe"), "tampered installer"); Refuses(() => changed.Verify(), "amazon_setup_file_changed");
    var changedFuel = New("changed-fuel"); File.WriteAllText(Path.Combine(changedFuel.Path, "fuel.json"), "{}"); Refuses(() => changedFuel.Verify(), "amazon_setup_file_changed");
    var unknownPath = New("unmanifested-step", Fixture.Fuel("evil.exe")); Refuses(() => unknownPath.Verify(), "amazon_setup_file_unverified");
    var wrongOwner = New("wrong-owner"); File.WriteAllText(Path.Combine(wrongOwner.Path, ".playhub-install-owner"), Guid.NewGuid().ToString("D")); Refuses(() => wrongOwner.Verify(), "amazon_setup_ownership_lost");
    foreach (var command in new[] { "../outside.exe", "C:/outside.exe", "setup.exe:stream", "//network/setup.exe" })
    {
        var hostile = New("hostile-" + checks, Fixture.Fuel(command)); var rejected = false;
        try { hostile.Verify(); } catch (StoreOperationException) { rejected = true; }
        Check(rejected && !File.Exists(hostile.Journal), "fuel path never escapes signed owned files: " + command);
    }
    var invalidArgs = New("unexpected-args", Fixture.Fuel("vcredist_x86.exe").Replace("/norestart", "/launch-unrelated")); Refuses(() => invalidArgs.Verify(), "amazon_setup_unsupported");
    var invalidReturns = New("invalid-returns", Fixture.Fuel("vcredist_x86.exe").Replace("[0,5100]", "[0.5]")); Refuses(() => invalidReturns.Verify(), "amazon_setup_metadata_invalid");
    var duplicate = New("duplicate-keys", Fixture.Fuel("vcredist_x86.exe").Replace("PostInstall:", "PostInstall:[],PostInstall:")); Refuses(() => duplicate.Verify(), "amazon_launch_metadata_invalid");
    var noSteps = New("empty-setup", "{SchemaVersion:'2',Main:{Command:'game.exe',Args:[]},PostInstall:[],}"); noSteps.Verify(); Check(File.Exists(noSteps.Journal), "bounded JSON5 empty setup receives explicit zero-step receipt");
    var noDeclaredSetup = New("no-declared-setup", "{SchemaVersion:'2',Main:{Command:'game.exe',Args:[]},}"); noDeclaredSetup.Verify(); Check(File.Exists(noDeclaredSetup.Journal), "missing PostInstall means no declared setup, not arbitrary execution");

    var dx = New("directx", "{SchemaVersion:'2',Main:{Command:'game.exe',Args:[]},PostInstall:[{Command:'Redists/DXSETUP.exe',Args:['/silent']}],}");
    dx.Probe.Description = "Microsoft DirectX Setup"; dx.Probe.InstallerVersion = new(4, 9, 0, 904); dx.Probe.Version = new(9, 29, 952, 3111);
    dx.Add("Redists/DXSETUP.exe", Encoding.UTF8.GetBytes("inert signed-Microsoft probe fixture"));
    dx.Add("game.exe", NativePe()); dx.Add("Redists/Jun2010_D3DCompiler_43_x86.cab", Cabinet("d3dcompiler_43.dll", 100));
    dx.Verify(); Check(File.Exists(dx.Journal), "known DirectX native cabinet inventory verifies current architecture/version without extraction");
    dx.Probe.Missing = true; Refuses(() => dx.Verify(), "amazon_setup_runtime_missing");
    Refuses(() => AmazonPostInstall.CabinetFiles([1, 2, 3]), "amazon_setup_cabinet_invalid");
    var badCab = Cabinet("../escape.dll", 100); Refuses(() => AmazonPostInstall.CabinetFiles(badCab), "amazon_setup_cabinet_invalid");
    var truncate = Cabinet("d3dcompiler_43.dll", 100)[..40]; Refuses(() => AmazonPostInstall.CabinetFiles(truncate), "amazon_setup_cabinet_invalid");
    var stale = New("stale-receipt"); stale.Verify(); stale.Probe.Missing = true; Refuses(() => stale.Verify(), "amazon_setup_runtime_missing");
    Check(File.Exists(stale.Journal), "old receipt is historical proof only and cannot override missing current runtime");
    foreach (var missingRuntime in new[] { false, true })
    {
        var completedWorker = new CompletedWorker(); var causal = New("transfer-" + missingRuntime); causal.Probe.Missing = missingRuntime; var released = false;
        var transferType = typeof(StoreAmazonProvider).GetNestedType("Transfer", BindingFlags.NonPublic)!;
        var transfer = (IStoreTransfer)Activator.CreateInstance(transferType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            [completedWorker, (Func<Task<bool>>)(() => { causal.Verify(); return Task.FromResult(true); }), (Action)(() => released = true)], null)!;
        var result = await transfer.Completion;
        Check(result.Completed == !missingRuntime && result.InstallVerified == !missingRuntime, "actual drained zero-exit Amazon transfer completion depends on verified prerequisite postcondition");
        Check(missingRuntime ? result.ErrorCode == "amazon_setup_runtime_missing" && !File.Exists(causal.Journal) : File.Exists(causal.Journal), "specific prerequisite failure reaches existing transfer result; receipt precedes successful completion");
        Check(released, "drained worker releases owned gate even when setup verification fails"); await transfer.DisposeAsync();
    }
    Console.WriteLine($"PASS {checks} Amazon prerequisite checks; no installer or game executed");
}
catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }

static byte[] Cabinet(string name, uint size)
{
    var text = Encoding.UTF8.GetBytes(name); var data = new byte[36 + 16 + text.Length + 1]; "MSCF"u8.CopyTo(data); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 36); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(28), 1); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(36), size); text.CopyTo(data, 52); return data;
}
static byte[] NativePe()
{
    var bytes = new byte[1024]; bytes[0] = 77; bytes[1] = 90; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 128); "PE\0\0"u8.CopyTo(bytes.AsSpan(128));
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(132), 0x14c); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(148), 224); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(152), 0x10b); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(212), 1024); return bytes;
}
sealed class Fixture
{
    internal string Path { get; } internal string Journal => System.IO.Path.Combine(Path, "private", "setup-receipt.json"); internal Guid Job { get; } = Guid.NewGuid();
    internal List<AmazonFile> Files { get; } = []; internal Probe Probe { get; } = new(); internal AmazonManifest Manifest => new(SHA256.HashData("synthetic manifest"u8), Files);
    internal Fixture(string path, string? fuel)
    {
        Path = path; Directory.CreateDirectory(path); File.WriteAllText(System.IO.Path.Combine(path, ".playhub-install-owner"), Job.ToString("D"));
        Add("fuel.json", Encoding.UTF8.GetBytes(fuel ?? Fuel("vcredist_x86.exe"))); Add("vcredist_x86.exe", Encoding.UTF8.GetBytes("inert setup"));
    }
    internal void Add(string name, byte[] bytes)
    {
        var path = System.IO.Path.Combine(Path, name); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes);
        Files.RemoveAll(f => f.RelativePath == name); Files.Add(new(name, bytes.Length, 0, SHA256.HashData(bytes)));
    }
    internal void Verify() => AmazonPostInstall.Verify("fixture", Path, Job, Manifest, Journal, default, Probe);
    internal static string Fuel(string command) => "{SchemaVersion:'2',Main:{Command:'game.exe',Args:[]},PostInstall:[{Command:'" + command + "',Args:['/q','/norestart'],ValidReturns:[0,5100]}],}";
}
sealed class Probe : IAmazonSetupProbe
{
    internal int Runs, ExitCode; internal IReadOnlyList<string> Arguments = [];
    internal bool Missing, Signed = true; internal Version Version = new(12, 0, 40664, 0), InstallerVersion = new(12, 0, 30501, 0);
    internal string Description = "Microsoft Visual C++ 2013 Redistributable (x86)", Architecture = "x86", Company = "Microsoft Corporation";
    public AmazonSetupImage InspectInstaller(string path) => new(Signed, Description, InstallerVersion);
    public AmazonSetupRuntime? Runtime(string name, string architecture) => Missing ? null : new(name, Architecture, Version, 10000000, Company);
    public int RunVerifiedInstaller(string path, IReadOnlyList<string> arguments, CancellationToken token) { token.ThrowIfCancellationRequested(); Runs++; Arguments = arguments.ToArray(); return ExitCode; }
}
sealed class CompletedWorker : IAmazonWorker
{
    public Task<AmazonWorkerResult> Completion => Task.FromResult(new AmazonWorkerResult(0, false, true, false));
    public Task StopAsync(CancellationToken token) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
