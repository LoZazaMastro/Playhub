// SPDX-License-Identifier: MIT. Inert actual provider and GameSession checks; no OS query, game process, account, UI or input.
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.GameSession;
using Playhub.StoreLibrary;

internal static class XboxLaunchChecks
{
    const string Id = "9AAA00000001", Family = "Fixture.Game_abcdefghijklm";
    static int checks;
    static void Check(bool okay, string name) { checks++; if (!okay) throw new Exception(name); }
    static async Task Reject(Func<Task> action, string code)
    { try { await action(); throw new Exception("Accepted " + code); } catch (StoreOperationException e) { Check(e.Code == code, code); } }
    static async Task Main()
    {
        await using var fixture = new Fixture();
        await Metadata(fixture); LocalLibrary(fixture); await Launch(fixture); await ChangedContext(fixture); await OfflineCache(fixture); await GameSession(fixture); SteamInstallIntent();
        Console.WriteLine($"PASS {checks} Xbox exact package/provider/UWP lifetime checks; fake OS metadata, account snapshot and IUwpRuntime only.");
    }
    static async Task Metadata(Fixture f)
    {
        string body = f.Body(); int calls = 0;
        var package = await WindowsXboxPackageCatalog.ReadAsync(Family, (exe, args, env, token) =>
        {
            calls++; Check(exe.EndsWith(@"WindowsPowerShell\v1.0\powershell.exe", StringComparison.OrdinalIgnoreCase), "only system PowerShell");
            Check(args.Count == 5 && args[3] == "-EncodedCommand" && env.Count == 0, "fixed metadata query arguments");
            string script = Encoding.Unicode.GetString(Convert.FromBase64String(args[4]));
            Check(script.Contains(Family) && !script.Contains("AllUsers") && !script.Contains("Process.Start"), "current-user exact PFN query only");
            return Task.FromResult((0, body));
        }, default);
        Check(calls == 1 && package is { StatusOk: true } && package.Aumid == Family + "!App" && package.ExecutablePath == f.Game, "registered manifest yields exact AUMID and EXE");
        Check(WindowsXboxPackageCatalog.Parse(Family, "{\"Version\":1,\"Packages\":[]}") is null, "uninstalled registration returns no package");
        await Reject(() => WindowsXboxPackageCatalog.ReadAsync(Family + "';evil", (_, _, _, _) => throw new Exception("query reached"), default), "xbox_package_identity_invalid");
        await Reject(() => Task.FromResult(WindowsXboxPackageCatalog.Parse(Family, f.Body("Foreign.Game_abcdefghijklm"))), "xbox_package_scan_invalid");
        var unhealthy = WindowsXboxPackageCatalog.Parse(Family, f.Body(status: false));
        Check(unhealthy is { StatusOk: false, Aumid: null }, "broken deployment is never launch proof");
        Check(WindowsXboxPackageCatalog.Parse(Family, f.Body(signature: "Developer")) is { StatusOk: false }, "developer registration cannot stand in for Store package");
        f.Manifest("../Foreign.exe"); Check(WindowsXboxPackageCatalog.Parse(Family, body)?.Aumid is null, "escaping executable refused");
        f.Manifest("Game.exe", extra: "<Application Id=\"Other\" Executable=\"Game.exe\" />");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.Aumid is null, "multiple registered applications are not guessed");
        f.Manifest("GameLaunchHelper.exe", entry: "Windows.FullTrustApplication"); File.WriteAllText(Path.Combine(f.Folder, "GameLaunchHelper.exe"), "inert helper");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.Aumid is null, "GDK helper without exact config is not guessed");
        File.WriteAllText(Path.Combine(f.Folder, "MicrosoftGame.Config"), "<Game><ExecutableList><Executable Id=\"Other\" Name=\"Game.exe\" /></ExecutableList></Game>");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.Aumid is null, "foreign GDK Application Id refused");
        File.WriteAllText(Path.Combine(f.Folder, "MicrosoftGame.Config"), "<Game><ExecutableList><Executable Id=\"App\" Name=\"Game.exe\" /></ExecutableList></Game>");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.ExecutablePath == f.Game, "exact GDK config resolves real game behind official helper");
        File.WriteAllText(Path.Combine(f.Folder, "MicrosoftGame.Config"), "<Game><ExecutableList><Executable Name=\"Game.exe\" TargetDeviceFamily=\"PC\" IsDevOnly=\"false\"/><Executable Name=\"Debug.exe\" TargetDeviceFamily=\"PC\" IsDevOnly=\"true\"/></ExecutableList></Game>");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.ExecutablePath == f.Game, "registered single-app retail GDK resolves optional Id while ignoring developer executables");
        File.WriteAllText(Path.Combine(f.Folder, "MicrosoftGame.Config"), "<Game><ExecutableList><Executable Name=\"Game.exe\" TargetDeviceFamily=\"PC\"/><Executable Name=\"Other.exe\" TargetDeviceFamily=\"PC\"/></ExecutableList></Game>");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.Aumid is null, "ambiguous implicit GDK identities are not guessed");
        File.WriteAllText(Path.Combine(f.Folder, "MicrosoftGame.Config"), "<Game><ExecutableList><Executable Name=\"Game.exe\" TargetDeviceFamily=\"XboxOne\"/></ExecutableList></Game>");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.Aumid is null, "console-only executable cannot satisfy PC launch");
        File.WriteAllText(Path.Combine(f.Folder, "MicrosoftGame.Config"), "<Game><ExecutableList><Executable Id=\"App\" Name=\"Game.exe\" TargetDeviceFamily=\"PC\" IsDevOnly=\"true\"/></ExecutableList></Game>");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.Aumid is null, "explicit developer-only executable cannot satisfy retail launch");
        File.WriteAllText(Path.Combine(f.Folder, "MicrosoftGame.Config"), "<Game><ExecutableList><Executable Name=\"Game.exe\" TargetDeviceFamily=\"PC\"/></ExecutableList></Game>");
        f.Manifest("GameLaunchHelper.exe", entry: "Windows.FullTrustApplication", extra: "<Application Id=\"Other\" Executable=\"Missing.exe\" />");
        Check(WindowsXboxPackageCatalog.Parse(Family, body)?.Aumid is null, "implicit GDK executable is never assigned across multiple registered applications");
        File.WriteAllText(Path.Combine(f.Folder,"GDKStubGame.exe"),"inert preorder placeholder");
        f.Manifest("GDKStubGame.exe");
        Check(WindowsXboxPackageCatalog.Parse(Family,body) is {StatusOk:true,IsPlaceholder:true,Aumid:null,ExecutablePath:null},"registered partner preorder stub never becomes a playable installed game");
        f.Manifest("GameLaunchHelper.exe",entry:"Windows.FullTrustApplication");
        File.WriteAllText(Path.Combine(f.Folder,"MicrosoftGame.Config"),"<Game><ExecutableList><Executable Name=\"GDKStubGame.exe\" TargetDeviceFamily=\"PC\"/></ExecutableList></Game>");
        Check(WindowsXboxPackageCatalog.Parse(Family,body) is {IsPlaceholder:true,Aumid:null},"official helper cannot turn a GDK placeholder into a completed game");
        f.Manifest("Game.exe");
        int batches = 0;
        var many = await WindowsXboxPackageCatalog.ReadManyAsync(new[] { Family, "Missing.Game_abcdefghijklm" }, (_, args, _, _) =>
        {
            batches++; string script = Encoding.Unicode.GetString(Convert.FromBase64String(args[4]));
            Check(script.Length < 5000 && script.Contains("[int]$_.Status -eq 0") && !script.Contains("VerifyIsOK"), "actual AppxStatus enum contract, bounded batch command");
            return Task.FromResult((0, body));
        }, default);
        Check(batches == 1 && many.Count == 1 && many[Family].Aumid == Family + "!App", "one batch query produces exact installed families only");
        File.WriteAllText(Path.Combine(f.Folder, "AppxManifest.xml"), "<!DOCTYPE Package [<!ENTITY x SYSTEM 'file:///C:/no'>]><Package />");
        Check(WindowsXboxPackageCatalog.Parse(Family, body) is { StatusOk: true, Aumid: null }, "DTD refused without falsifying installed readback");
        f.Manifest("Game.exe");
    }
    static async Task Launch(Fixture f)
    {
        var descriptor = await f.Provider.ResolveLaunchAsync(Id, f.Folder, default);
        Check(descriptor?.Executable == f.Session && descriptor.GameExecutablePath == f.Game && descriptor.Arguments.SequenceEqual(new[] { "--store-library", "xbox", Id }), "public launch re-enters verified store session");
        var plan = await f.Provider.PrepareLaunchAsync(Id, f.Folder, default);
        Check(plan.Key == new StoreGameKey(StoreKind.Xbox, Id) && plan.GameExecutable == f.Game && plan.XboxAumid == Family + "!App", "private plan binds owned ProductId to registered AUMID");
        Check(plan.Arguments.Count == 0 && plan.Environment.Count == 0 && plan.ExpiresAt == f.Now.AddSeconds(30), "registered activation does not carry guessed commands or credentials");
        Check(await f.Provider.ResolveLaunchAsync(Id, f.Folder + "-foreign", default) is null, "foreign install path rejected");
        File.Delete(f.Game); Check(await f.Provider.ResolveLaunchAsync(Id, f.Folder, default) is null, "removed game executable immediately disables launch"); File.WriteAllText(f.Game, "inert fixture");
        f.Provider.PackageLookup = (_, _) => Task.FromResult<WindowsXboxPackage?>(null);
        Check(await f.Provider.ResolveLaunchAsync(Id, f.Folder, default) is null, "OS uninstall readback disables launch");
        f.ResetProbe();
        f.Provider.PackageLookup = (_, _) => Task.FromResult(WindowsXboxPackageCatalog.Parse("Foreign.Game_abcdefghijklm", f.Body("Foreign.Game_abcdefghijklm")));
        Check(await f.Provider.ResolveLaunchAsync(Id, f.Folder, default) is null, "foreign package cannot satisfy owned mapping"); f.ResetProbe();
    }
    static void LocalLibrary(Fixture f)
    {
        var package = new WindowsXboxPackage(Family, "Fixture.Game_1.0.0.0_x64__abcdefghijklm", f.Folder,
            Family + "!App", f.Game, true, "Game.exe", Id, "Installed native game");
        var merged = StoreXboxLocalLibrary.Merge([], [package], []);
        Check(merged.Length == 1 && merged[0] is { AccessKind: "installed", SupportsPc: true, PackageFamilyName: Family },
            "local exact StoreId adds a registered PC game without fabricating a purchased grant");
        Check(StoreXboxLocalLibrary.Merge([], [package with { StoreProductId = null }], []).Length == 0,
            "local package without exact StoreId cannot inherit an account title");
        Check(StoreXboxLocalLibrary.Merge([], [package, package with { PackageFullName = "Other" }], []).Length == 0,
            "ambiguous local product editions require an explicit association");
        Check(StoreXboxLocalLibrary.Merge([], [package with { StatusOk = false }], []).Length == 0,
            "unhealthy local registration never appears as installed");
        var owned = new StoreXboxOwnedGame("9AAA00000002", "Owned bundle", true, true, "owned", PackageFamilyName: Family);
        merged = StoreXboxLocalLibrary.Merge([owned], [package], []);
        Check(merged.Length == 1 && merged[0] == owned, "exact owned bundle family keeps its existing identity instead of a duplicate installed edition");
        owned = new(Id, "Owned game", false, true, "owned");
        merged = StoreXboxLocalLibrary.Merge([owned], [package], []);
        Check(merged.Length == 1 && merged[0].AccessKind == "owned" && merged[0].SupportsPc && merged[0].PackageFamilyName == Family,
            "actual PC registration supplies missing technical metadata for the same owned product only");
    }
    static async Task ChangedContext(Fixture f)
    {
        f.Now = f.Now.AddDays(30); Check(await f.Provider.ResolveLaunchAsync(Id, f.Folder, default) is not null, "last confirmed owned mapping launches offline without a five-minute network gate"); f.RestoreOwner();
        f.Owner(access: "conditional"); Check(await f.Provider.ResolveLaunchAsync(Id, f.Folder, default) is null, "conditional license cannot inherit purchased grant"); f.RestoreOwner();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Provider.PackageLookup = async (_, _) => { entered.SetResult(); await release.Task; return WindowsXboxPackageCatalog.Parse(Family, f.Body()); };
        var pending = f.Provider.ResolveLaunchAsync(Id, f.Folder, default); await entered.Task;
        await f.Provider.DisconnectAsync(default); release.SetResult(); Check(await pending is null, "sign-out while OS query runs cannot publish an old launch"); f.RestoreOwner(); f.ResetProbe();
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await f.Provider.ResolveLaunchAsync(Id, f.Folder, canceled.Token); throw new Exception("cancellation ignored"); } catch (OperationCanceledException) { checks++; }
    }
    static async Task OfflineCache(Fixture f)
    {
        string cache = Path.Combine(f.Folder, "owned-cache.json");
        f.Set("ownedLibraryCachePath", cache);
        void Write(string identity)
        {
            var snapshot = new StoreXboxOwnedSnapshot(5, f.Now.AddDays(-30), true, new[] { new StoreXboxOwnedGame(Id, "Cached game", true, false, "owned", PackageFamilyName: Family) });
            File.WriteAllText(cache, JsonSerializer.Serialize(new { Version = 1, AccountIdentity = identity, Snapshot = snapshot }));
            f.Set("ownedIdentity", null); f.Set("owned", new StoreXboxOwnedSnapshot(0, null, true, Array.Empty<StoreXboxOwnedGame>()));
        }
        Write(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("xbox:999"))));
        Check((await f.Provider.PrepareLaunchAsync(Id, f.Folder, default)).XboxAumid == Family + "!App", "offline restart loads last confirmed product/PFN mapping only for same account");
        Write(new string('A', 64));
        Check(await f.Provider.ResolveLaunchAsync(Id, f.Folder, default) is null, "foreign-account persisted mapping never grants launch");
        f.Set("ownedLibraryCachePath", null); f.RestoreOwner();
    }
    static async Task GameSession(Fixture f)
    {
        // The earlier offline-cache check intentionally advanced the provider's
        // clock by 30 days; a new private launch plan must use the live clock.
        f.Now = DateTimeOffset.UtcNow;
        var plan = await f.Provider.PrepareLaunchAsync(Id, f.Folder, default); var runtime = new Runtime();
        Check(StoreXboxUwpLaunch.Run(plan, runtime) == 17, "actual UWP lifetime keeps the store session alive until game exit");
        Check(runtime.Aumid == Family + "!App" && runtime.Preparations == 1 && runtime.Publications == 1 && runtime.Waits == 1, "normal companion preparation, exact activation, identity and wait reused");
        foreach (var invalid in new[] { plan with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) }, plan with { Key = new(StoreKind.Epic, Id) }, plan with { GameExecutable = Path.Combine(Path.GetDirectoryName(f.Folder)!, "Foreign.exe") }, plan with { Arguments = ["injected"] }, plan with { Environment = new Dictionary<string, string> { ["TOKEN"] = "x" } } })
        {
            var rejected = new Runtime();
            try { StoreXboxUwpLaunch.Run(invalid, rejected); throw new Exception("accepted invalid registered launch"); } catch (InvalidDataException) { Check(rejected.Aumid is null, "invalid registered launch rejected before activation"); }
        }
        string old = JsonSerializer.Serialize(plan with { XboxAumid = null });
        Check(JsonSerializer.Deserialize<StorePrivateLaunchPlan>(old)!.XboxAumid is null, "optional append-only field preserves old provider plans");
    }
    static void SteamInstallIntent()
    {
        var key = new StoreGameKey(StoreKind.Xbox, Id);
        foreach (bool installed in new[] { false, true })
        {
            int requests = 0;
            int code = StoreLibraryGameLaunch.Run(key, () => new(key, 1, 1, installed, false, null, null, []),
                () => { requests++; return new(true); }, () => throw new Exception("uninstalled game prepared"), _ => throw new Exception("uninstalled game launched"));
            Check(code == 0 && requests == 1, "install/manage asks once for Steam fullscreen intent and exits without desktop launcher");
        }
        try
        {
            StoreLibraryGameLaunch.Run(key, () => new(key, 1, 1, false, false, null, null, []), () => new(false),
                () => throw new Exception("uninstalled game prepared"), _ => throw new Exception("uninstalled game launched"));
            throw new Exception("rejected Steam UI intent treated as success");
        }
        catch (InvalidOperationException error) { Check(error.Message.Contains("Steam"), "rejected fullscreen request fails with actionable Steam message"); }
    }
    sealed class Runtime : IUwpRuntime
    {
        internal string? Aumid; internal int Preparations, Publications, Waits;
        public long Now => 1; public bool ShellReady => true; public bool PrepareDirectActivation() => true;
        public void StartShell() => throw new Exception("unexpected shell"); public void Delay(int milliseconds) => throw new Exception("unexpected delay");
        public void Log(string message) { }
        public IDisposable? PrepareLaunch(string aumid, string executable) { Preparations++; Check(executable == "Game.exe", "manifest relative executable reaches companion"); return null; }
        public IDisposable? PublishIdentity(string aumid, IUwpProcess game) { Publications++; return null; }
        public IUwpProcess Activate(string aumid, string arguments) { Aumid = aumid; Check(arguments == "", "registered activation has empty launch arguments"); return new Game(this); }
        public IUwpProcess? FindGame(string family, string executable) => null;
        sealed class Game(Runtime owner) : IUwpProcess { public uint Id => 1; public string Executable => "Game.exe"; public string PackageFamily => Family; public int WaitForExit() { owner.Waits++; return 17; } public void Dispose() { } }
    }
    sealed class Fixture : IAsyncDisposable
    {
        internal readonly string Folder = Path.Combine(Path.GetTempPath(), "Playhub-Xbox-Launch-Inert-" + Guid.NewGuid().ToString("N"));
        internal string Game => Path.Combine(Folder, "Game.exe"); internal string Session => Path.Combine(Folder, "Playhub.GameSession.exe");
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly StoreXboxAccountProvider Provider; readonly HttpClient client = new(new NoHttp());
        internal Fixture()
        {
            Directory.CreateDirectory(Folder); File.WriteAllText(Game, "inert fixture"); File.WriteAllText(Session, "never executed"); Manifest("Game.exe");
            Provider = new(client, () => null, _ => { }, () => Now); Provider.SessionExecutable = Session; RestoreOwner(); ResetProbe();
        }
        internal void Manifest(string executable, string entry = "Fixture.App", string extra = "") => File.WriteAllText(Path.Combine(Folder, "AppxManifest.xml"), $"<Package><Identity Name=\"Fixture.Game\" /><Applications><Application Id=\"App\" Executable=\"{executable}\" EntryPoint=\"{entry}\" />{extra}</Applications></Package>");
        internal string Body(string family = Family, bool status = true, string signature = "Store") => JsonSerializer.Serialize(new { Version = 1, Packages = new[] { new { PackageFamilyName = family, PackageFullName = family[..family.LastIndexOf('_')] + "_1.0.0.0_x64__abcdefghijklm", InstallLocation = Folder, StatusOk = status, SignatureKind = signature, IsFramework = false, IsResourcePackage = false } } });
        internal void ResetProbe()
        {
            Provider.PackageLookup = (_, _) => Task.FromResult(WindowsXboxPackageCatalog.Parse(Family, Body()));
            Provider.PackageBatchLookup = (_, _) => Task.FromResult<IReadOnlyDictionary<string, WindowsXboxPackage>>(new Dictionary<string, WindowsXboxPackage> { [Family] = WindowsXboxPackageCatalog.Parse(Family, Body())! });
        }
        internal void Owner(string access = "owned") => Set("owned", new StoreXboxOwnedSnapshot(1, Now, false, new[] { new StoreXboxOwnedGame(Id, "Inert game", true, false, access, PackageFamilyName: Family) }));
        internal void RestoreOwner()
        {
            var saved = typeof(StoreXboxAccountProvider).GetNestedType("Saved", BindingFlags.NonPublic)!;
            Set("saved", Activator.CreateInstance(saved, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [StoreXboxAccountAuth.ClientId, "inert", "999", "Fixture"], null));
            Set("loaded", true); Set("ownedIdentity", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("xbox:999")))); Owner();
        }
        internal void Set(string name, object? value) => typeof(StoreXboxAccountProvider).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Provider, value);
        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync(); client.Dispose();
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Folder).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Folder).StartsWith("Playhub-Xbox-Launch-Inert-", StringComparison.Ordinal)) throw new Exception("Unsafe fixture cleanup");
            Directory.Delete(Folder, true);
        }
        sealed class NoHttp : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("Network forbidden in inert launch checks"); }
    }
}
