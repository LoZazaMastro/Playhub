// SPDX-License-Identifier: MIT. Original inert Playhub Amazon launch regression checks.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.StoreLibrary;

internal static class AmazonLaunchChecks
{
    private static int count;
    private static void Check(bool condition, string name) { count++; if (!condition) throw new Exception("FAIL " + name); }
    private static async Task Reject(Func<Task> call, string name)
    { try { await call(); } catch (StoreOperationException) { Check(true, name); return; } throw new Exception("FAIL accepted " + name); }
    internal static async Task Run(string referenceBundle)
    {
        var root = Path.Combine(Path.GetTempPath(), "Playhub-Amazon-Launch-Inert-" + Guid.NewGuid().ToString("N"));
        var bundle = Path.Combine(root, "bundle"); Directory.CreateDirectory(bundle);
        File.Copy(Path.Combine(referenceBundle, "nile_windows_x86_64.exe"), Path.Combine(bundle, "nile_windows_x86_64.exe"));
        var state = Path.Combine(root, "state"); var native = Path.Combine(state, "amazon", "native", "nile");
        var game = Path.Combine(root, "chosen", "game"); Directory.CreateDirectory(Path.Combine(game, "bin"));
        File.WriteAllText(Path.Combine(game, ".playhub-install-owner"), Guid.NewGuid().ToString("D"));
        var fuel = Encoding.UTF8.GetBytes("{ // official field layout, JSON5 syntax\n SchemaVersion:'1.0', Main:{Command:'bin/game.exe', Args:['--lang','日本語',\"--title=Alice's Story\",], WorkingSubdirOverride:'bin',},}");
        var image = Encoding.UTF8.GetBytes("synthetic signed executable, never executed");
        var data = Encoding.UTF8.GetBytes("signed game data");
        var files = new Dictionary<string, byte[]> { ["fuel.json"] = fuel, ["bin/game.exe"] = image, ["data.bin"] = data };
        using var rsa = RSA.Create(2048);
        byte[] signed = Sign(files, rsa);
        var library = JsonSerializer.Serialize(new[] { new { id = "entitlement-1", product = new { id = "amazon-launch", sku = "sku-1", title = "Fixture game" } } });
        var queries = 0; var changeMaterializedAccount = false;
        Task<(int, string)> Query(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> env, CancellationToken token)
        {
            queries++; if (args.SequenceEqual(new[] { "library", "sync", "--force" })) return Task.FromResult((0, ""));
            if (args.SequenceEqual(new[] { "library", "list", "--json" }))
            {
                if(changeMaterializedAccount)
                {
                    using var aes=Aes.Create();aes.Key=SHA256.HashData(Encoding.UTF8.GetBytes("synthetic-user"));var iv=RandomNumberGenerator.GetBytes(16);
                    var value=Encoding.UTF8.GetBytes("{\"extensions\":{\"customer_info\":{\"user_id\":\"different-account\"}}}");
                    var file=Path.Combine(native,Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes("synthetic-user"))).ToLowerInvariant()+".enc");
                    File.WriteAllBytes(file,aes.EncryptEcb(iv,PaddingMode.None).Concat(aes.EncryptCbc(value,iv,PaddingMode.PKCS7)).ToArray());
                }
                return Task.FromResult((0, library));
            }
            throw new Exception("No launch CLI/process may run in these tests");
        }
        var provider = new StoreAmazonProvider(bundle, state, Path.Combine(root, "Playhub.StoreHost.exe"), new HttpClient(new NoNetwork()), Query,
            (_, _, _, _, _, _, _) => throw new Exception("No worker may run"), bytes => AmazonManifest.Parse(bytes, rsa));
        var auth = JsonNode.Parse("""{"extensions":{"customer_info":{"user_id":"synthetic-user","given_name":"Fixture player"}},"tokens":{"bearer":{"access_token":"fake-only","refresh_token":"fake-only","expires_in":3600}},"NILE":{"token_obtain_time":9999999999}}""")!;
        StoreCredentialVault.Write(Path.Combine(state, "amazon", "credentials.dpapi"), auth.ToJsonString());
        Directory.CreateDirectory(Path.Combine(native, "manifests"));
        void Reset()
        {
            foreach (var f in files) File.WriteAllBytes(Path.Combine(game, f.Key), f.Value);
            File.WriteAllBytes(Path.Combine(native, "manifests", "amazon-launch.raw"), signed);
            File.WriteAllText(Path.Combine(native, "installed.json"), JsonSerializer.Serialize(new[] { new { id = "amazon-launch", path = game, version = "installed-version-1", size = files.Values.Sum(b => b.Length) } }));
        }
        Reset();
        var listed = await provider.ListAsync(default);
        Check(listed.Single().Launchable && listed.Single().InstallPath == game, "authoritative signed fuel record is genuinely launchable");
        Check(queries == 2, "only the existing sync/list queries are performed");
        var descriptor = await provider.ResolveLaunchAsync("amazon-launch", game, default);
        Check(descriptor?.GameExecutablePath == Path.Combine(game, "bin", "game.exe"), "exact manifest-covered command; no executable search");
        Check(descriptor!.Arguments.SequenceEqual(new[] { "--launch", "amazon", "amazon-launch" }), "typed compatibility descriptor");
        var before = DateTimeOffset.UtcNow;
        var plan = await provider.PrepareLaunchAsync("amazon-launch", game, default);
        Check(plan.LaunchId != Guid.Empty && plan.Key == new StoreGameKey(StoreKind.Amazon, "amazon-launch"), "private one-shot lineage");
        Check(plan.GameExecutable == descriptor.GameExecutablePath && plan.WorkingDirectory == Path.Combine(game, "bin"), "authoritative executable and working directory");
        Check(plan.Arguments.SequenceEqual(new[] { "--lang", "日本語", "--title=Alice's Story" }), "exact Unicode arguments, never a shell string");
        Check(plan.Environment.Count == 5 && plan.Environment["AMAZON_GAMES_FUEL_ENTITLEMENT_ID"] == "entitlement-1" && plan.Environment["AMAZON_GAMES_FUEL_PRODUCT_SKU"] == "sku-1", "only authoritative five Nile environment keys");
        Check(plan.Environment["AMAZON_GAMES_FUEL_DISPLAY_NAME"] == "Fixture player" && plan.Environment["FUEL_DIR"] == Path.Combine(native, "SDK", "Amazon Games Services", "Legacy"), "own current account and canonical private SDK configuration path");
        Check(plan.ExpiresAt > before && plan.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(31), "existing bounded private launch deadline");
        Check(queries == 2, "resolving and preparing never execute Nile, authenticate or download SDKs");
        File.WriteAllText(Path.Combine(root,"Playhub.StoreHost.exe"),"synthetic host file, never executed");
        await using(var queue=new StoreQueueEngine([provider],new NoInstall(),Path.Combine(root,"queue.json")))
        {
            await queue.InitializeAsync();var account=(await queue.GetSnapshotAsync()).Accounts.Single(a=>a.Store==StoreKind.Amazon);
            Check((await queue.RefreshAsync(StoreKind.Amazon,account.Revision)).Accepted,"actual queue refresh mounts verified Amazon launchability");
            var descriptorFromQueue=await queue.GetVerifiedLaunchDescriptorAsync(plan.Key);
            Check(descriptorFromQueue.IsInstalled&&descriptorFromQueue.IsLaunchable&&descriptorFromQueue.GameExecutablePath==plan.GameExecutable,"actual public launch descriptor is complete");
            var privatePlan=await queue.PreparePrivateLaunchAsync(plan.Key,Guid.NewGuid(),default);
            Check(privatePlan.Key==plan.Key&&privatePlan.GameExecutable==plan.GameExecutable&&privatePlan.Arguments.SequenceEqual(plan.Arguments),"actual private GameSession boundary yields the same verified launch plan");
            Check(!JsonSerializer.Serialize(await queue.GetSnapshotAsync()).Contains("AMAZON_GAMES_FUEL",StringComparison.Ordinal),"private launch environment absent from public snapshots");
        }
        var snapshot = new StoreLibrarySnapshot(1, DateTimeOffset.UtcNow,
            [new(StoreKind.Amazon, StoreConnectionState.Connected, 7, "Fixture player", provider.Capabilities)],
            [new(plan.Key, "Fixture game", 7, provider.Capabilities, true, true, game)], []);
        var registry = new StoreSteamShortcutRegistry(_ => Task.FromResult(snapshot), Path.Combine(root, "Playhub.GameSession.exe"), Path.Combine(root, "Playhub.exe"), Path.Combine(root, "steam-registry.json"));
        using (var request = JsonDocument.Parse(JsonSerializer.Serialize(new { store = StoreKind.Amazon, expectedAccountRevision = 7 }, StoreLibraryService.JsonOptions)))
        {
            var requested = await registry.TryHandleAsync("steam-sync-request", request.RootElement, default);
            Check(requested.Handled && requested.Data is StoreSteamSyncRequest { Total: 1, State: "pending" }, "actual Steam registry accepts authenticated Amazon inventory");
            using var empty = JsonDocument.Parse("{}");
            var pending = await registry.TryHandleAsync("steam-sync-pending", empty.RootElement, default);
            Check(pending.Data is StoreSteamSyncPending { Plans.Count: 1 } p && p.Plans[0].Arguments == "--store-library amazon amazon-launch", "actual Steam plan uses normal GameSession Amazon route");
        }

        async Task Unlaunchable(string name)
        { Check(await provider.ResolveLaunchAsync("amazon-launch", game, default) == null, name + " descriptor refuses"); await Reject(() => provider.PrepareLaunchAsync("amazon-launch", game, default), name + " private plan refuses"); }
        File.WriteAllText(Path.Combine(native,"installed.json"),JsonSerializer.Serialize(new[]{new{id="entitlement-1",path=game,version="installed-version-1"}}));
        Check((await provider.ResolveLaunchAsync("amazon-launch",game,default))?.GameExecutablePath==plan.GameExecutable,"single authoritative legacy entitlement record remains supported");Reset();
        files["fuel.json"]=Encoding.UTF8.GetBytes("{SchemaVersion:1,Main:{Command:'bin/game.exe'}}");signed=Sign(files,rsa);Reset();
        var defaultPlan=await provider.PrepareLaunchAsync("amazon-launch",game,default);
        Check(defaultPlan.Arguments.Count==0&&defaultPlan.WorkingDirectory==game,"absent args and cwd use exact upstream empty/root defaults, not executable directory");
        files["fuel.json"]=fuel;signed=Sign(files,rsa);Reset();
        files.Remove("fuel.json");signed=Sign(files,rsa);File.WriteAllBytes(Path.Combine(native,"manifests","amazon-launch.raw"),signed);
        Check(await provider.ResolveLaunchAsync("amazon-launch",game,default)==null,"unmanifested local fuel never authorizes an executable");
        files["fuel.json"]=fuel;signed=Sign(files,rsa);Reset();
        File.WriteAllText(Path.Combine(game, "fuel.json"), "{}"); await Unlaunchable("stale/tampered fuel"); Reset();
        var changedImage=image.ToArray();changedImage[0]^=1;File.WriteAllBytes(Path.Combine(game, "bin", "game.exe"),changedImage);await Unlaunchable("same-size changed executable still fails its signed hash");Reset();
        File.WriteAllText(Path.Combine(game,"data.bin"),"changed by game or mod");
        Check(await provider.ResolveLaunchAsync("amazon-launch",game,default)!=null,"non-executable asset changes do not invalidate verified launch metadata");
        Check((await provider.PrepareLaunchAsync("amazon-launch",game,default)).GameExecutable==Path.Combine(game,"bin","game.exe"),"private launch retains exact signed executable despite modified asset");
        Check(!await provider.IsInstalledAsync("amazon-launch",game,default),"explicit whole-install verifier still detects modified signed assets");
        Reset();
        using(var heldAsset=new FileStream(Path.Combine(game,"data.bin"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            Check(await provider.ResolveLaunchAsync("amazon-launch",game,default)!=null,"launch metadata never opens unrelated bulk assets");
            Check((await provider.ListAsync(default)).Single().Launchable,"library refresh never reads unrelated bulk assets");
        }
        File.Delete(Path.Combine(game, "fuel.json")); await Unlaunchable("missing fuel"); Reset();
        File.Delete(Path.Combine(game, "bin", "game.exe")); await Unlaunchable("missing exact image"); Reset();
        File.WriteAllText(Path.Combine(native, "installed.json"), JsonSerializer.Serialize(new[] { new { id = "amazon-launch", path = game, version = "v1" }, new { id = "entitlement-1", path = game, version = "v1" } }));
        await Unlaunchable("ambiguous legacy/product installed records"); Reset();
        File.WriteAllText(Path.Combine(native, "installed.json"), JsonSerializer.Serialize(new[] { new { id = "amazon-launch", path = Path.Combine(root, "elsewhere"), version = "v1" } }));
        await Unlaunchable("stale installed path"); Reset();
        var oldRaw = File.ReadAllBytes(Path.Combine(native, "manifests", "amazon-launch.raw")); oldRaw[^1] ^= 1; File.WriteAllBytes(Path.Combine(native, "manifests", "amazon-launch.raw"), oldRaw);
        await Unlaunchable("untrusted raw manifest"); Reset();
        var oldLibrary = File.ReadAllText(Path.Combine(native, "library.json")); File.WriteAllText(Path.Combine(native, "library.json"), oldLibrary + " ");
        await Unlaunchable("library binding changed without fresh account sync"); File.WriteAllText(Path.Combine(native, "library.json"), oldLibrary);
        var savedAuth = auth.ToJsonString(); auth["extensions"]!["customer_info"]!["user_id"] = "replacement-user";
        StoreCredentialVault.Write(Path.Combine(state, "amazon", "credentials.dpapi"), auth.ToJsonString()); await Unlaunchable("new account cannot inherit cached old entitlement");
        StoreCredentialVault.Write(Path.Combine(state, "amazon", "credentials.dpapi"), savedAuth);
        var oldBinding=File.ReadAllText(Path.Combine(native,"library-account.json"));var knownLibrary=library;
        library=library.Replace("entitlement-1","different-entitlement",StringComparison.Ordinal);changeMaterializedAccount=true;
        await Reject(()=>provider.ListAsync(default),"a CLI account change refuses the new library");
        Check(File.ReadAllText(Path.Combine(native,"library-account.json"))==oldBinding&&File.ReadAllText(Path.Combine(native,"library.json"))==oldLibrary,"failed account sealing cannot bind a new launch entitlement");
        library=knownLibrary;changeMaterializedAccount=false;await provider.ListAsync(default);
        foreach(var command in new[] { "../escape.exe", "C:/escape.exe", "missing.exe" })
        {
            files["fuel.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { SchemaVersion = 1, Main = new { Command = command } })); signed = Sign(files, rsa); Reset();
            await Unlaunchable("signed fuel cannot escape or select unmanifested image");
        }
        files["fuel.json"] = Encoding.UTF8.GetBytes("{SchemaVersion:1,Main:{Command:'bin/game.exe',WorkingSubdirOverride:'missing'}}"); signed = Sign(files, rsa); Reset();
        await Unlaunchable("signed nonexistent working directory");
        files["fuel.json"] = fuel; signed = Sign(files, rsa); Reset();

        foreach (var text in new[] {
            "{SchemaVersion:1,Main:{Command:'../evil.exe'}}", "{SchemaVersion:1,Main:{Command:'C:/evil.exe'}}",
            "{SchemaVersion:1,Main:{Command:'C:evil.exe'}}", "{SchemaVersion:1,Main:{Command:'good.exe:stream'}}",
            "{SchemaVersion:1,Main:{Command:'NUL.exe'}}", "{SchemaVersion:1,Main:{Command:'script.cmd'}}",
            "{SchemaVersion:1,Main:{Command:'game.exe',Command:'other.exe'}}", "{SchemaVersion:1,Main:{Command:'game.exe'},Main:{Command:'other.exe'}}",
            "{SchemaVersion:1,Main:{Command:'game.exe',Args:'--one shell string'}}", "{SchemaVersion:1,Main:{Command:'game.exe',Args:[null]}}",
            "{SchemaVersion:1,Main:{Command:'game.exe',WorkingSubdirOverride:'..'}}", "{SchemaVersion:1,Main:{Command:'game.exe',WorkingSubdirOverride:'C:/outside'}}",
            "{SchemaVersion:1,Main:{Command:'game.exe',Args:['bad\\u0000argument']}}", "{SchemaVersion:1,Main:{Command:'game.exe',Args:['bad\\nargument']}}",
            "{", "{Main:", "{SchemaVersion:Infinity,Main:{Command:'game.exe'}}", "/* unfinished" })
            await Reject(() => Task.Run(() => AmazonLaunchMetadata.Parse(Encoding.UTF8.GetBytes(text))), "malformed/ambiguous/escaping JSON5");
        Check(AmazonLaunchMetadata.Parse(Encoding.UTF8.GetBytes("{SchemaVersion:1,Main:{Command:'bin/game.exe',WorkingSubdirOverride:'.'}}")).WorkingDirectory == ".", "explicit root working directory is supported");
        await Reject(() => Task.Run(() => AmazonLaunchMetadata.Parse(new byte[AmazonLaunchMetadata.MaximumBytes + 1])), "bounded fuel size");
        using (var canceled = new CancellationTokenSource())
        { canceled.Cancel(); try { await provider.PrepareLaunchAsync("amazon-launch", game, canceled.Token); throw new Exception("accepted cancellation"); } catch (OperationCanceledException) { Check(true, "cancel never yields a plan"); } }
        Check(StoreLibraryLaunchRequest.TryParse(["--store-library", "amazon", "amazon-launch"], "--store-library", out var parsed) && parsed == plan.Key, "normal GameSession command accepts Amazon");
        Check(!StoreLibraryLaunchRequest.TryParse(["--store-library", "amazon", "../outside"], "--store-library", out _), "normal parser still refuses invalid game IDs");
        await provider.DisconnectAsync(default); await Unlaunchable("signout cannot keep private launch authority");
        Console.WriteLine($"PASS {count} actual Amazon launch metadata/provider checks; fake account/HTTP/query only, no game/CLI/UI run");
    }
    private static byte[] V(ulong value) { var b = new List<byte>(); do { var n = (byte)(value & 127); value >>= 7; b.Add((byte)(n | (value > 0 ? 128 : 0))); } while (value > 0); return b.ToArray(); }
    private static byte[] N(int tag, ulong value) => V((ulong)(tag << 3)).Concat(V(value)).ToArray();
    private static byte[] B(int tag, byte[] value) => V((ulong)(tag << 3 | 2)).Concat(V((ulong)value.Length)).Concat(value).ToArray();
    private static byte[] Sign(Dictionary<string, byte[]> files, RSA key)
    {
        var entries = files.Select(f => B(2, B(1, Encoding.UTF8.GetBytes(f.Key)).Concat(N(3, (ulong)f.Value.Length)).Concat(B(5, N(1, 0).Concat(B(2, SHA256.HashData(f.Value))).ToArray())).ToArray())).SelectMany(x => x).ToArray();
        var plain = B(1, entries); var header = B(1, N(1, 0)).Concat(B(3, N(1, 0).Concat(B(2, key.SignData(plain, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))).ToArray())).ToArray();
        var size = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(size, (uint)header.Length); return size.Concat(header).Concat(plain).ToArray();
    }
    private sealed class NoNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("No network allowed"); }
    private sealed class NoInstall:IStoreInstallFileSystem
    {
        public IReadOnlyList<StoreInstallLocation> Locations()=>[];
        public string CanonicalDestination(string path)=>throw new Exception("No install");
        public long FreeBytes(string path)=>throw new Exception("No install");
        public bool CanClaim(string path,Guid? job)=>throw new Exception("No install");
        public bool Claim(string path,Guid job)=>throw new Exception("No install");
    }
}
