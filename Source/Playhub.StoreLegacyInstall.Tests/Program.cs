// Actual provider/engine/Win32 handles; synthetic payloads and fake tool metadata only.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.StoreLibrary;

var checks = 0;
var root = Path.Combine(Path.GetTempPath(), "Playhub-Legacy-Install-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
async Task Refuses(Func<Task> action, string code)
{
    try { await action(); throw new Exception("expected_refusal:" + code); }
    catch (StoreOperationException e) { Check(e.Code == code, "refusal:" + e.Code); }
}
try
{
    var bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Playhub.StoreHost/Runtime"));
    var f = new Fixture(Path.Combine(root, "provider"), bundle);
    var nativeGame = new StoreLibraryGame(f.Key, "Unrelated custom title", 7, f.Actual.Capabilities, true, true, f.Path);
    string wrapper = Path.Combine(root, "Playhub.GameSession.exe");
    string metadataDirectory = Path.Combine(f.State, "epic", "native", "metadata"); Directory.CreateDirectory(metadataDirectory);
    string metadataFile = Path.Combine(metadataDirectory, f.Key.Id + ".json");
    File.WriteAllText(metadataFile, JsonSerializer.Serialize(new { app_name = f.Key.Id, metadata = new { @namespace = "official-namespace", id = "official-catalog-item" } }));
    var native = (await f.Actual.ReadSteamNativeIdentitiesAsync([nativeGame], wrapper, default)).Single().Value;
    Check(native.GameExecutablePath == f.Exe && native.EpicNamespace == "official-namespace" && native.EpicCatalogItemId == "official-catalog-item" && native.StartDirectory == f.Path,
        "native_import_identity_comes_from_exact_installed_record_and_official_metadata_never_title");
    Check((await f.Actual.ReadSteamNativeIdentitiesAsync([nativeGame with { InstallPath = f.Path + "-foreign" }], wrapper, default)).Count == 0,
        "native_import_identity_refuses_another_installation_path");
    Check((await f.Actual.ReadSteamNativeIdentitiesAsync([nativeGame with { IsInstalled = false }], wrapper, default)).Count == 0,
        "native_import_identity_never_claims_uninstalled_catalogue_game");
    File.WriteAllText(metadataFile, JsonSerializer.Serialize(new { app_name = "foreign-product", metadata = new { @namespace = "official-namespace", id = "official-catalog-item" } }));
    Check((await f.Actual.ReadSteamNativeIdentitiesAsync([nativeGame], wrapper, default)).Count == 0 && f.Queries == 0 && f.Mutations == 0,
        "foreign_metadata_rejected_without_cli_or_game_mutation");
    File.Delete(metadataFile);
    var verified = await f.Verify();
    Check(verified is not null && verified.OwnerJob == f.Job, "legacy_marker_and_exact_offline_manifest_recover_owner");
    Check(f.Queries == 1 && f.Mutations == 0 && File.ReadAllText(f.Save) == "user-save", "verification_reads_exact_offline_manifest_and_preserves_payload");
    File.WriteAllText(f.Exe, "tampered");
    Check((await f.Verify())!.Files.Single(row=>row.RelativePath=="game.exe").Size==new FileInfo(f.Exe).Length,"modified_manifest_asset_can_be_explicitly_removed");
    f.RestorePayload();
    File.Delete(f.Data);
    Check(await f.Verify() is not null,"missing_manifest_asset_does_not_disable_removal");
    f.RestorePayload();
    File.WriteAllText(f.Marker, "not-a-guid");
    Check(await f.Verify() is null, "invalid_marker_does_not_adopt_installation");
    File.WriteAllText(f.Marker, Guid.Empty.ToString("D"));
    Check(await f.Verify() is null, "empty_owner_does_not_adopt_installation");
    File.WriteAllText(f.Marker, f.Job.ToString("D"));
    File.Move(f.Manifest, f.Manifest + ".held");
    await Refuses(async () => { await f.Verify(); }, "uninstall_manifest_unavailable");
    File.Move(f.Manifest + ".held", f.Manifest);
    f.WriteRegistration(sharedPath: Path.GetDirectoryName(f.Path)!);
    await Refuses(async () => { await f.Verify(); }, "shared_installation_refused");
    f.WriteRegistration(sharedPath: Path.Combine(f.Path, "nested"));
    await Refuses(async () => { await f.Verify(); }, "shared_installation_refused");
    f.WriteRegistration();
    f.InvalidManifest = true;
    await Refuses(async () => { await f.Verify(); }, "uninstall_manifest_invalid");
    f.InvalidManifest = false;
    Check(f.Mutations == 0 && File.Exists(f.Exe) && File.ReadAllText(f.Save) == "user-save", "all_refusals_leave_every_payload_and_save_untouched");

    var c = new Fixture(Path.Combine(root, "queue"), bundle); string migratedJson = "";
    var fake = new Provider(c);
    c.WriteQueue();
    await using (var engine = new StoreQueueEngine([fake], new StoreInstallFileSystem(), c.Queue))
    {
        await engine.InitializeAsync();
        var before = await engine.GetSnapshotAsync();
        Check(before.Jobs.Count == 0, "no_fabricated_completed_job_before_migration");
        var status = await engine.GetInstalledGameAsync(c.Key);
        Check(!status.CanUninstall && status.IsInstalled && fake.Verifications==0, "poll_never_creates_legacy_claim_or_enumerates_payload");
        var plan = await engine.PrepareUninstallAsync(c.Key, status.AccountRevision, status.LibraryRevision);
        Check(plan.CanUninstall && plan.Nonce is not null, "legacy_install_uses_normal_expiring_confirmation");
        Check(fake.Verifications == 1 && (await engine.GetSnapshotAsync()).Jobs.Count == 0, "migration_receipt_is_not_download_history");
        migratedJson = File.ReadAllText(c.Queue);
        Check((await engine.GetInstalledGameAsync(c.Key)).CanUninstall && fake.Verifications == 1, "poll_does_not_rehash_entire_legacy_payload");
        Check(fake.Removals == 0 && File.Exists(c.Exe), "preparation_does_not_uninstall");
    }
    foreach (var invalid in new[] { "receipt", "Key", "Id", "ManifestHash", "InstallPath", "AccountIdentity" })
    {
        var bad = JsonNode.Parse(migratedJson)!;
        var receipt = bad["LegacyInstalls"]![0]!;
        if (invalid == "receipt") bad["LegacyInstalls"]![0] = null;
        else if (invalid == "Id") receipt["Key"]!["Id"] = null;
        else receipt[invalid] = null;
        var malformed = Path.Combine(root, "malformed-" + invalid + ".json"); File.WriteAllText(malformed, bad.ToJsonString());
        try { await using var ignored = new StoreQueueEngine([fake], new StoreInstallFileSystem(), malformed); throw new Exception("malformed_must_refuse"); }
        catch (StoreOperationException error) { Check(error.Code == "queue_invalid", "malformed_receipt_" + invalid + "_is_queue_invalid"); }
    }
    var modified = new Fixture(Path.Combine(root, "modified-diagnostic"), bundle); var modifiedProvider = new Provider(modified); modified.WriteQueue(); File.WriteAllText(modified.Data, "modified");
    await using (var engine = new StoreQueueEngine([modifiedProvider], new StoreInstallFileSystem(), modified.Queue))
    {
        await engine.InitializeAsync(); var status = await engine.GetInstalledGameAsync(modified.Key);
        Check(status.CanLaunch && modifiedProvider.Verifications==0,"unverified_status_preserves_launch_without_expensive_or_mutating_verification");
        Check((await engine.PrepareUninstallAsync(modified.Key,status.AccountRevision,status.LibraryRevision)).CanUninstall,"modified_legacy_payload_remains_removable");
    }
    await using (var engine = new StoreQueueEngine([fake], new StoreInstallFileSystem(), c.Queue))
    {
        await engine.InitializeAsync();
        var status = await engine.GetInstalledGameAsync(c.Key);
        Check(status.CanUninstall && fake.Verifications == 1, "private_receipt_survives_host_restart_without_bulk_reverification");
        var plan = await engine.PrepareUninstallAsync(c.Key, status.AccountRevision, status.LibraryRevision);
        var removed = await engine.UninstallAsync(plan, Guid.NewGuid());
        Check(removed.Accepted && fake.Removals == 1 && !File.Exists(c.Exe) && File.Exists(c.Save), "normal_confirmed_uninstall_removes_only_synthetic_manifest_files");
        Check(removed.Snapshot.Jobs.Count == 0 && !removed.Snapshot.Games.Single().IsInstalled, "legacy_uninstall_updates_library_without_invented_job");
    }
    var account = new Fixture(Path.Combine(root, "account-race"), bundle); var racing = new Provider(account); account.WriteQueue();
    await using (var engine = new StoreQueueEngine([racing], new StoreInstallFileSystem(), account.Queue))
    {
        await engine.InitializeAsync();
        racing.OnVerify = async () =>
        {
            var disconnect = await engine.DisconnectAsync(StoreKind.Epic, 7);
            Check(!disconnect.Accepted, "provider_reservation_refuses_concurrent_disconnect");
            // Inert completion seam: emulate an already-running account result
            // arriving after the migration released the engine gate.
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var accounts = (Dictionary<StoreKind, StoreAccount>)typeof(StoreQueueEngine).GetField("accounts", flags)!.GetValue(engine)!;
            accounts[StoreKind.Epic] = accounts[StoreKind.Epic] with { Revision = 8 };
        };
        var before=await engine.GetSnapshotAsync();
        Check(!(await engine.PrepareUninstallAsync(account.Key,7,before.Revision)).CanUninstall, "account_change_during_full_verification_never_grants_receipt");
        Check((await engine.GetSnapshotAsync()).Jobs.Count == 0 && racing.Removals == 0, "account_race_never_creates_job_or_deletes_payload");
    }
    var changed = new Fixture(Path.Combine(root, "manifest-race"), bundle); var changedProvider = new Provider(changed); changed.WriteQueue();
    await using (var engine = new StoreQueueEngine([changedProvider], new StoreInstallFileSystem(), changed.Queue))
    {
        await engine.InitializeAsync(); var status = await engine.GetInstalledGameAsync(changed.Key);
        changedProvider.OverrideHash = new string('B', 64);
        var denied = await engine.PrepareUninstallAsync(changed.Key, status.AccountRevision, status.LibraryRevision);
        Check(!denied.CanUninstall && denied.ErrorCode == "installation_changed", "changed_manifest_cannot_reuse_migrated_receipt");
        Check(changedProvider.Removals == 0 && File.Exists(changed.Exe), "manifest_race_has_no_filesystem_effect");
    }
    Console.WriteLine($"PASS {checks} legacy installation checks; synthetic data only");
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }

sealed class Fixture
{
    public string Path { get; } public string State { get; } public string Queue => System.IO.Path.Combine(State, "queue.json");
    public string Exe => System.IO.Path.Combine(Path, "game.exe"); public string Data => System.IO.Path.Combine(Path, "data", "data.bin");
    public string Save => System.IO.Path.Combine(Path, "saves", "user.save"); public string Marker => System.IO.Path.Combine(Path, ".playhub-install-owner");
    public Guid Job { get; } = Guid.NewGuid(); public StoreGameKey Key { get; } = new(StoreKind.Epic, "synthetic");
    public StoreOwnedUninstallPlan Plan { get; private set; } = null!;
    public string Manifest { get; } private string Registration { get; }
    public StoreCliProvider Actual { get; } public int Queries, Mutations; public bool InvalidManifest;
    public Fixture(string root, string bundle)
    {
        Path = System.IO.Path.Combine(root, "payload"); State = System.IO.Path.Combine(root, "state");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Data)!); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Save)!);
        RestorePayload(); File.WriteAllText(Marker, Job.ToString("D")); File.WriteAllText(Save, "user-save");
        var native = System.IO.Path.Combine(State, "epic", "native"); Directory.CreateDirectory(System.IO.Path.Combine(native, "manifests"));
        Registration = System.IO.Path.Combine(native, "installed.json"); Manifest = System.IO.Path.Combine(native, "manifests", "synthetic_Windows_1.manifest");
        File.WriteAllText(Manifest, "synthetic retained manifest"); WriteRegistration();
        Actual = new(StoreKind.Epic, bundle, State, Environment.ProcessPath!, toolQuery: (_, args, _, _) =>
        {
            if (args[0] != "list-files") { Mutations++; throw new Exception("unexpected_tool_mutation"); }
            if (!args.SequenceEqual(new[] { "list-files", "synthetic", "--json", "--manifest", Manifest })) throw new Exception("unbounded_manifest_query");
            Queries++;
            return Task.FromResult((0, JsonSerializer.Serialize(Plan.Files.Select(f => new { filename = InvalidManifest ? "../foreign" : f.RelativePath, file_size = f.Size, sha_hash = f.Hash }))));
        });
    }
    public void RestorePayload()
    {
        File.WriteAllText(Exe, "synthetic executable"); File.WriteAllText(Data, "synthetic data");
        StoreOwnedInstallFile Row(string file) => new(System.IO.Path.GetRelativePath(Path, file), new FileInfo(file).Length, "SHA1", Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(file))));
        Plan = new(Key, Path, Exe, Job, new string('A', 64), [Row(Data), Row(Exe)]);
    }
    public void WriteRegistration(string? sharedPath = null)
    {
        var records = new Dictionary<string, object> { [Key.Id] = new { install_path = Path, executable = "game.exe", version = "1", platform = "Windows" } };
        if (sharedPath is not null) records["foreign"] = new { install_path = sharedPath };
        File.WriteAllText(Registration, JsonSerializer.Serialize(records));
    }
    public Task<StoreOwnedUninstallPlan?> Verify() => ((IStoreLegacyInstalledGameProvider)Actual).VerifyLegacyInstallationAsync(Key.Id, Path, default);
    public void WriteQueue() => File.WriteAllText(Queue, JsonSerializer.Serialize(new { Version = 1, Revision = 10L,
        Accounts = new[] { new StoreAccount(StoreKind.Epic, StoreConnectionState.Connected, 7, "Synthetic", Actual.Capabilities) },
        Games = new[] { new StoreLibraryGame(Key, "Synthetic", 7, Actual.Capabilities, true, true, Path) }, Jobs = Array.Empty<StoreDownloadJob>(),
        AccountIdentities = new Dictionary<StoreKind, string> { [StoreKind.Epic] = "synthetic-account" }, JobAccountIdentities = new Dictionary<Guid, string>() }));
}
sealed class Provider(Fixture fixture) : IStoreProvider, IStoreInstalledGameProvider, IStoreLegacyInstalledGameProvider
{
    public int Verifications, Removals; public string? OverrideHash; public Func<Task>? OnVerify;
    public StoreKind Store => StoreKind.Epic; public StoreCapabilities Capabilities => fixture.Actual.Capabilities; public Uri AuthorizationUri => new("https://example.invalid/");
    public Task<ProviderAccount?> GetAccountAsync(CancellationToken token) => Task.FromResult<ProviderAccount?>(new("synthetic-account", "Synthetic"));
    public Task<ProviderAccount> ConnectAsync(string code, CancellationToken token) => throw new NotSupportedException();
    public Task DisconnectAsync(CancellationToken token) => Task.CompletedTask;
    public Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken token) => throw new NotSupportedException();
    public Task<ProviderInstallSize> GetInstallSizeAsync(string id, CancellationToken token) => throw new NotSupportedException();
    public Task<ProviderLaunch?> ResolveLaunchAsync(string id, string path, CancellationToken token) => Task.FromResult<ProviderLaunch?>(new(Environment.ProcessPath!, path, [], fixture.Exe));
    public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id, string path, CancellationToken token) => throw new NotSupportedException();
    public Task<IStoreTransfer> StartAsync(TransferRequest request, Action<TransferProgress> progress, CancellationToken token) => throw new NotSupportedException();
    public async Task<StoreOwnedUninstallPlan?> VerifyLegacyInstallationAsync(string id, string path, CancellationToken token)
    { Verifications++; var result = await fixture.Verify(); if (OnVerify is not null) await OnVerify(); return result; }
    public Task<bool> CanUninstallAsync(string id, string path, Guid job, CancellationToken token) => fixture.Actual.CanUninstallAsync(id, path, job, token);
    public async Task<StoreOwnedUninstallPlan> PrepareUninstallAsync(string id, string path, Guid job, CancellationToken token)
    { var plan = await fixture.Actual.PrepareUninstallAsync(id, path, job, token); return OverrideHash is null ? plan : plan with { ManifestHash = OverrideHash }; }
    public Task UninstallAsync(StoreOwnedUninstallPlan plan, CancellationToken token)
    { StoreOwnedInstallFiles.Delete(plan, token); Removals++; return Task.CompletedTask; }
}
