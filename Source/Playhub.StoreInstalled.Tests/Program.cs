// Inert contract tests: synthetic files/fake HTTP/query only. No tool, game or UI starts.
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

var checks = 0;
var root = Path.Combine(Path.GetTempPath(), "Playhub-Installed-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
void Refuses(Action action, string? expected = null)
{
    try { action(); throw new Exception("expected_refusal"); }
    catch (StoreOperationException e) { Check(expected == null || e.Code == expected, "specific_refusal:" + expected); }
}
Fixture New(string name, StoreKind store = StoreKind.Epic) => new(Path.Combine(root, name), store);
try
{
    var zeroProgress = New("verified-zero-progress",StoreKind.Gog);
    await using(var engine=zeroProgress.Engine(new FakeProvider(zeroProgress),()=>DateTimeOffset.UtcNow))
    {
        await engine.InitializeAsync();
        const BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        var rows=(List<StoreDownloadJob>)typeof(StoreQueueEngine).GetField("jobs",hidden)!.GetValue(engine)!;
        rows[0]=rows[0]with{State=StoreJobState.Downloading,ProgressPercent=0,DownloadedBytes=12142510,TotalBytes=null};
        var transfer=new CompletedTransfer();
        typeof(StoreQueueEngine).GetField("active",hidden)!.SetValue(engine,zeroProgress.Job);
        typeof(StoreQueueEngine).GetField("transfer",hidden)!.SetValue(engine,transfer);
        await (Task)typeof(StoreQueueEngine).GetMethod("FinishAsync",hidden)!.Invoke(engine,[zeroProgress.Job,transfer])!;
        var completed=(await engine.GetSnapshotAsync()).Jobs.Single();
        Check(completed.State==StoreJobState.Completed&&completed.ProgressPercent==100,"verified_completion_without_CLI_percentage_is_100_percent");
        Check(completed.DownloadedBytes==12142510&&completed.TotalBytes==null,"verified_completion_preserves_unknown_total_and_observed_bytes");
    }
    var direct = New("direct");
    Refuses(() => StoreOwnedInstallFiles.RefuseRunning(Path.GetDirectoryName(Environment.ProcessPath!)!), "game_running");
    foreach (var invalid in new[] { "../foreign.txt", "a/../../other", "C:\\foreign", "/absolute", "a:file", "folder/../game.exe", "folder./game.exe", "a//b", "a\0b" })
        Refuses(() => StoreOwnedInstallFiles.PathFor(direct.Path, invalid), "uninstall_manifest_invalid");
    Refuses(() => StoreOwnedInstallFiles.ValidatePlan(direct.Plan with { Files = [direct.Plan.Files[0], direct.Plan.Files[0]] }), "uninstall_manifest_invalid");
    Refuses(() => StoreOwnedInstallFiles.ValidatePlan(direct.Plan with { Files = [new(".PLAYHUB-INSTALL-OWNER", 36, null, null), direct.Plan.Files[0]] }), "uninstall_manifest_invalid");
    Refuses(() => StoreOwnedInstallFiles.ValidatePlan(direct.Plan with { GameExecutable = Path.Combine(root, "foreign.exe") }), "uninstall_executable_not_in_manifest");
    Refuses(() => StoreOwnedInstallFiles.ValidatePlan(direct.Plan with { OwnerJob = Guid.NewGuid() }), "installation_ownership_lost");
    Refuses(() => StoreOwnedInstallFiles.ValidatePlan(direct.Plan with { Files = [direct.Plan.Files[0] with { Hash = "zz" }] }), "uninstall_manifest_invalid");
    var modified = New("modified"); File.WriteAllText(modified.Exe, "changed");
    Refuses(() => StoreOwnedInstallFiles.Delete(modified.Plan, default), "installed_files_modified");
    Check(File.Exists(modified.Exe) && File.Exists(modified.Data), "whole_manifest_preflight_prevents_partial_delete");
    var busy = New("busy");
    using (var retained = new FileStream(busy.Data, FileMode.Open, FileAccess.Read, FileShare.None))
        Refuses(() => StoreOwnedInstallFiles.Delete(busy.Plan, default), "uninstall_file_busy");
    Check(File.Exists(busy.Exe), "busy_file_preserves_executable");
    StoreOwnedInstallFiles.Delete(direct.Plan, default);
    Check(!File.Exists(direct.Exe) && !File.Exists(direct.Data), "only_manifest_files_removed");
    Check(File.ReadAllText(direct.Foreign) == "user-save", "foreign_save_preserved");
    Check(File.Exists(direct.Marker), "provider_commit_owns_marker_removal");
    var canceled = New("canceled"); using (var c = new CancellationTokenSource())
    { c.Cancel(); try { StoreOwnedInstallFiles.Delete(canceled.Plan, c.Token); } catch (OperationCanceledException) { checks++; } }
    Check(File.Exists(canceled.Exe), "canceled_before_mutation");

    var history = New("completed-history"); var historyProvider = new FakeProvider(history);
    var historyPath = Path.Combine(root, "completed-history-queue.json");
    await using (var engine = history.Engine(historyProvider, () => DateTimeOffset.UtcNow))
    {
        await engine.InitializeAsync(); var beforeHistory = await engine.GetSnapshotAsync();
        Check(beforeHistory.Jobs.Single().ProgressPercent==100,"legacy_verified_completed_receipts_restore_at_100_percent");
        Check((await engine.GetInstalledGameAsync(history.Plan.Key)).CanUninstall, "completed_receipt_verifies_installed_ownership");
        var staleClear = await engine.ClearCompletedAsync(new(beforeHistory.Revision - 1, Guid.NewGuid()));
        Check(!staleClear.Accepted && staleClear.ErrorCode == "queue_changed" && staleClear.Snapshot.Jobs.Count == 1, "stale_history_clear_preserves_jobs");
        var request = new StoreClearCompletedRequest(beforeHistory.Revision, Guid.NewGuid());
        var clear = await engine.ClearCompletedAsync(request);
        Check(clear.Accepted && clear.Snapshot.Jobs.Count == 0, "completed_history_is_removed_from_public_downloads");
        Check(clear.Snapshot.Games.SequenceEqual(beforeHistory.Games) && clear.Snapshot.Accounts.SequenceEqual(beforeHistory.Accounts), "history_clear_does_not_modify_library_or_accounts");
        Check((await engine.GetInstalledGameAsync(history.Plan.Key)).CanUninstall, "clearing_history_keeps_exact_install_receipt_and_uninstall");
        Check((await engine.ClearCompletedAsync(request)).Accepted, "lost_clear_reply_is_idempotent");
        Check(!(await engine.ClearCompletedAsync(request with { ExpectedRevision = clear.Snapshot.Revision })).Accepted, "clear_request_id_cannot_change_revision");
        Check(File.Exists(history.Exe) && File.Exists(history.Data) && File.Exists(history.Foreign) && File.Exists(history.Marker), "history_clear_never_deletes_install_or_save_files");
    }
    await using (var restoredHistory = new StoreQueueEngine([historyProvider], new StoreInstallFileSystem(), historyPath))
    {
        await restoredHistory.InitializeAsync();
        Check((await restoredHistory.GetSnapshotAsync()).Jobs.Count == 0, "cleared_history_stays_clear_after_host_restart");
        Check((await restoredHistory.GetInstalledGameAsync(history.Plan.Key)).CanUninstall, "install_ownership_survives_cleared_history_restart");
    }
    var pausedHistory = New("paused-history");
    await using (var pausedEngine = pausedHistory.Engine(new FakeProvider(pausedHistory), () => DateTimeOffset.UtcNow, pending:true))
    {
        await pausedEngine.InitializeAsync(); var snapshotHistory = await pausedEngine.GetSnapshotAsync();
        var clear = await pausedEngine.ClearCompletedAsync(new(snapshotHistory.Revision, Guid.NewGuid()));
        Check(clear.Accepted && clear.Snapshot.Jobs.Single().State == StoreJobState.PausedByUser, "clear_completed_preserves_paused_download");
    }

    var queue = New("queue"); var provider = new FakeProvider(queue); var clock = DateTimeOffset.UtcNow;
    await using (var engine = queue.Engine(provider, () => clock))
    {
        await engine.InitializeAsync();
        // The unchanged pipe dispatcher rejected these methods before the new
        // extension was registered. No pipe, client, account or process starts.
        var installedArgs = JsonSerializer.SerializeToElement(new { key = queue.Plan.Key }, StoreLibraryService.JsonOptions);
        var extensions = Environment.GetEnvironmentVariable("PLAYHUB_INSTALLED_PARENT") == "1" ? Array.Empty<IStoreProtocolExtension>() : new IStoreProtocolExtension[] { new StoreInstalledGameProtocol(engine) };
        var pipe = new StorePipeServer(engine, _ => true, extensions);
        var boundary = typeof(StorePipeServer).GetMethod("ExtensionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var reply = await (Task<object>)boundary.Invoke(pipe, ["installed-game", installedArgs, CancellationToken.None])!;
        Check(reply is StoreInstalledGameStatus { CanUninstall: true, CanLaunch: true }, "actual_protocol_installed_boundary");
        var status = await engine.GetInstalledGameAsync(queue.Plan.Key);
        Check(status.InstallPath == queue.Path && status.IsInstalled, "exact_installed_status");
        // A populated active slot is a deletion veto, not a reason to disable an
        // unrelated installed game's already verified launch action.
        var activeField = typeof(StoreQueueEngine).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!;
        activeField.SetValue(engine, Guid.NewGuid());
        try { var otherActive = await engine.GetInstalledGameAsync(queue.Plan.Key); Check(otherActive.CanLaunch && !otherActive.CanUninstall && otherActive.ErrorCode == null, "active_download_does_not_disable_independent_launch"); }
        finally { activeField.SetValue(engine, null); }
        var wrongAccount = await engine.PrepareUninstallAsync(queue.Plan.Key, status.AccountRevision + 1, status.LibraryRevision);
        Check(!wrongAccount.CanUninstall && wrongAccount.Nonce == null, "wrong_account_no_nonce");
        var wrongLibrary = await engine.PrepareUninstallAsync(queue.Plan.Key, status.AccountRevision, status.LibraryRevision + 1);
        Check(wrongLibrary.ErrorCode == "library_changed", "stale_library_no_nonce");
        var prepare = await engine.PrepareUninstallAsync(queue.Plan.Key, status.AccountRevision, status.LibraryRevision);
        Check(prepare.CanUninstall && prepare.Nonce != null && prepare.InstallPath == queue.Path, "exact_confirmation");
        Check(!(await engine.UninstallAsync(prepare with { InstallPath = root }, Guid.NewGuid())).Accepted, "forged_path_refused");
        Check(!(await engine.UninstallAsync(prepare with { Key = new(StoreKind.Gog, "100") }, Guid.NewGuid())).Accepted, "cross_store_refused");
        Check(provider.Removals == 0, "no_mutation_before_confirmation");
        provider.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var id = Guid.NewGuid(); var pending = engine.UninstallAsync(prepare, id);
        await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check((await engine.DisconnectAsync(StoreKind.Epic, status.AccountRevision)).ErrorCode == "provider_busy", "disconnect_blocked_while_removing");
        Check((await engine.RefreshAsync(StoreKind.Epic, status.AccountRevision)).ErrorCode == "provider_busy", "refresh_blocked_while_removing");
        Check(!(await engine.GetVerifiedLaunchDescriptorAsync(queue.Plan.Key)).IsLaunchable, "launch_blocked_while_removing");
        Check(!(await engine.GetInstalledGameAsync(queue.Plan.Key)).CanUninstall, "second_uninstall_disabled");
        Check(!(await engine.UninstallAsync(prepare, Guid.NewGuid())).Accepted, "nonce_consumed_once");
        provider.Hold.SetResult(); var result = await pending;
        Check(result.Accepted && provider.Removals == 1, "actual_confirmed_removal");
        Check(!result.Snapshot.Games.Single().IsInstalled && result.Snapshot.Games.Single().InstallPath == null, "snapshot_changes_after_verified_completion");
        Check(File.Exists(queue.Foreign), "queue_does_not_delete_foreign_files");
        Check((await engine.UninstallAsync(prepare, id)).Accepted && provider.Removals == 1, "same_request_idempotent_after_lost_reply");
        Check(!(await engine.UninstallAsync(prepare with { InstallPath = root }, id)).Accepted, "reused_request_other_tuple_refused");
    }
    foreach (var kind in new[] { "expiry", "refresh", "pending", "no-owner", "prepare-reentry" })
    {
        var f = New(kind); var p = new FakeProvider(f); var instant = DateTimeOffset.UtcNow;
        if (kind == "no-owner") File.WriteAllText(f.Marker, Guid.NewGuid().ToString("D"));
        await using var engine = f.Engine(p, () => instant, pending: kind == "pending"); await engine.InitializeAsync();
        var s = await engine.GetInstalledGameAsync(f.Plan.Key);
        if (kind == "no-owner" || kind == "pending") { Check(!s.CanUninstall, kind + "_refused"); continue; }
        if (kind == "prepare-reentry") p.OnPrepare = () => { typeof(StoreQueueEngine).GetMethod("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, null); return Task.CompletedTask; };
        var prep = await engine.PrepareUninstallAsync(f.Plan.Key, s.AccountRevision, s.LibraryRevision);
        if (kind == "prepare-reentry") { Check(prep.CanUninstall&&p.Removals==0, "unrelated_revision_during_prepare_preserves_exact_confirmation_without_deleting"); continue; }
        if (kind == "expiry") instant = instant.AddMinutes(3);
        else await engine.RefreshAsync(StoreKind.Epic, s.AccountRevision);
        var outcome=await engine.UninstallAsync(prep,Guid.NewGuid());
        Check(kind=="refresh"?outcome.Accepted&&p.Removals==1&&File.Exists(f.Foreign):!outcome.Accepted&&p.Removals==0,kind+"_remains_scoped_to_original_verified_installation");
    }

    var offline = New("offline-gog", StoreKind.Gog);
    await using (var engine = offline.Engine(new FakeProvider(offline), () => DateTimeOffset.UtcNow))
    {
        await engine.InitializeAsync(); var before = await engine.GetInstalledGameAsync(offline.Plan.Key);
        Check((await engine.DisconnectAsync(StoreKind.Gog, before.AccountRevision)).Accepted, "gog_disconnect_legitimate");
        var after = await engine.GetInstalledGameAsync(offline.Plan.Key);
        Check(after.CanLaunch && after.CanUninstall && after.ErrorCode == null, "gog_offline_retains_verified_local_removal_authority");
        Check((await engine.PrepareUninstallAsync(offline.Plan.Key,after.AccountRevision,after.LibraryRevision)).CanUninstall,"gog_disconnected_can_prepare_local_uninstall");
        Check((await engine.GetVerifiedLaunchDescriptorAsync(offline.Plan.Key)).IsLaunchable, "gog_offline_descriptor_remains_available");
    }
    var launchRace = New("pending-launch"); var launchClock = DateTimeOffset.UtcNow;
    await using (var engine = launchRace.Engine(new FakeProvider(launchRace), () => launchClock))
    {
        await engine.InitializeAsync();
        var plan = await engine.PreparePrivateLaunchAsync(launchRace.Plan.Key, Guid.NewGuid(), default);
        var held = await engine.GetInstalledGameAsync(launchRace.Plan.Key);
        Check(held.CanLaunch && !held.CanUninstall, "unexpired_private_launch_plan_blocks_removal");
        launchClock = plan.ExpiresAt.AddMilliseconds(1);
        Check((await engine.GetInstalledGameAsync(launchRace.Plan.Key)).CanUninstall, "expired_private_launch_plan_does_not_pin_delete_forever");
    }
    var bundle = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Playhub.StoreHost/Runtime"));
    if (!File.Exists(Path.Combine(bundle, "legendary_windows_x64.exe"))) throw new Exception("pinned_runtime_test_path_missing");
    // Actual provider + actual Win32 file deletion; official binaries are hashed
    // by the constructor but never run. Fake queries inspect exact argv.
    var epic = New("actual-epic"); var epicState = Path.Combine(root, "epic-state");
    var epicNative = Path.Combine(epicState, "epic", "native"); Directory.CreateDirectory(epicNative);
    var epicRecord = Path.Combine(epicNative, "installed.json");
    void EpicRecord(string version = "1.1") => File.WriteAllText(epicRecord, JsonSerializer.Serialize(new Dictionary<string, object> { [epic.Plan.Key.Id] = new { install_path = epic.Path, executable = "game.exe", version, platform = "Windows" } }));
    var manifestDirectory = Path.Combine(epicNative,"manifests");Directory.CreateDirectory(manifestDirectory);
    var expectedManifest = Path.Combine(manifestDirectory,epic.Plan.Key.Id+"_Windows_1.1.manifest");
    var manifestBytes = Encoding.UTF8.GetBytes("synthetic offline manifest bytes");File.WriteAllBytes(expectedManifest,manifestBytes);
    EpicRecord(); var listCalls = 0; var uninstallCalls = 0;
    var actualEpic = new StoreCliProvider(StoreKind.Epic, bundle, epicState, Environment.ProcessPath!, toolQuery: (_, args, _, _) =>
    {
        // Actual pinned Legendary0.21.1 was reproduced exit1 on this original
        // argv: main's platform default forces list-files onto the login branch.
        if (args.SequenceEqual(new[] { "list-files", epic.Plan.Key.Id, "--json" })) return Task.FromResult((1,""));
        if (args.Count>0 && args[0]=="list-files")
        {
            Check(args.SequenceEqual(new[] {"list-files",epic.Plan.Key.Id,"--json","--manifest",expectedManifest}),"exact_local_manifest_no_cdn_platform_or_auth");
            var pinned=false;try{using var writer=new FileStream(expectedManifest,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);}catch(IOException){pinned=true;}
            Check(pinned,"manifest_bytes_pinned_through_cli_read");
            listCalls++; return Task.FromResult((0, JsonSerializer.Serialize(epic.Plan.Files.Select(f => new { filename=f.RelativePath, file_size=f.Size, sha_hash=f.Hash }))));
        }
        Check(args.SequenceEqual(new[] { "--yes", "uninstall", epic.Plan.Key.Id, "--keep-files", "--skip-uninstaller" }), "epic_only_metadata_command_no_game_uninstaller");
        uninstallCalls++; File.WriteAllText(epicRecord, "{}"); return Task.FromResult((0, ""));
    });
    // Missing/current-version/invalid metadata must fail before any tool query.
    File.Move(expectedManifest,expectedManifest+".held");
    try{await actualEpic.PrepareUninstallAsync(epic.Plan.Key.Id,epic.Path,epic.Job,default);throw new Exception("missing_manifest_must_refuse");}catch(StoreOperationException e){Check(e.Code=="uninstall_manifest_unavailable"&&listCalls==0,"no_glob_or_remote_manifest_fallback");}
    File.Move(expectedManifest+".held",expectedManifest);
    var ep = await actualEpic.PrepareUninstallAsync(epic.Plan.Key.Id, epic.Path, epic.Job, default);
    Check(File.Exists(epic.Exe) && uninstallCalls == 0, "provider_prepare_read_only");
    EpicRecord("bad\nversion");
    try{await actualEpic.PrepareUninstallAsync(epic.Plan.Key.Id,epic.Path,epic.Job,default);throw new Exception("bad_version_must_refuse");}catch(StoreOperationException e){Check(e.Code=="uninstall_manifest_invalid"&&listCalls==1,"invalid_manifest_version_no_tool");}
    EpicRecord();
    File.WriteAllText(expectedManifest,"changed manifest bytes");
    try{await actualEpic.UninstallAsync(ep,default);throw new Exception("changed_manifest_must_refuse");}catch(StoreOperationException e){Check(e.Code=="installation_changed"&&File.Exists(epic.Exe)&&uninstallCalls==0,"changed_local_manifest_invalidates_confirmation");}
    File.WriteAllBytes(expectedManifest,manifestBytes);
    await actualEpic.UninstallAsync(ep, default);
    Check(listCalls == 3 && uninstallCalls == 1, "provider_manifest_revalidated_before_effect");
    Check(!File.Exists(epic.Exe) && !File.Exists(epic.Marker) && File.Exists(epic.Foreign), "actual_epic_uninstall_preserves_save");
    Check(!(await actualEpic.CanUninstallAsync(epic.Plan.Key.Id, epic.Path, epic.Job, default)), "actual_epic_not_installed_after_uninstall");

    foreach (var version in new[] { 1, 2 })
    {
        var gog = New("actual-gog" + version, StoreKind.Gog); var state = Path.Combine(root, "gog-state" + version);
        var native = Path.Combine(state, "gog", "native"); var manifests = Path.Combine(native, "heroic_gogdl", "manifests"); Directory.CreateDirectory(manifests);
        var info = Path.Combine(gog.Path, "goggame-100.info");
        File.WriteAllText(info, JsonSerializer.Serialize(new { gameId="100", rootGameId="100", playTasks=new[] { new { isPrimary=true,type="FileTask",path="game.exe" } } }));
        File.WriteAllText(Path.Combine(state, "gog", "installed-100.json"), JsonSerializer.Serialize(gog.Path));
        var hash = new string('a', 32);
        var meta = version == 1 ? JsonSerializer.Serialize(new { version=1,HGLPlatform="windows",HGLInstallLanguage="en-US",product=new { rootGameID="100",timestamp=123,depots=new[] { new {gameIDs=new[] {"100"},languages=new[]{"Neutral"},manifest=hash} } } }) :
            JsonSerializer.Serialize(new { baseProductId="100",HGLInstallLanguage="en-US",depots=new[] { new {productId="100",languages=new[]{"*"},manifest=hash,osBitness=Array.Empty<string>()} } });
        File.WriteAllText(Path.Combine(manifests, "100"), meta);
        var all = gog.Plan.Files.Select(f => Path.Combine(gog.Path, f.RelativePath)).Append(info).ToArray();
        var entries = all.Select(path => (object)(version == 1 ? new { path="/"+Path.GetRelativePath(gog.Path,path).Replace('\\','/'),size=new FileInfo(path).Length,hash=Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path))) } :
            (object)new { type="DepotFile",path=Path.GetRelativePath(gog.Path,path),chunks=new[]{new{size=new FileInfo(path).Length}},md5=Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path))) })).ToArray();
        var depot = version == 1 ? JsonSerializer.Serialize(new { depot=new { files=entries } }) : JsonSerializer.Serialize(new { depot=new { items=entries } });
        var handler = new FakeHttp(depot, version == 2); using var http = new HttpClient(handler);
        var actual = new StoreCliProvider(StoreKind.Gog, bundle, state, Environment.ProcessPath!, client:http, toolQuery: (_,_,_,_) => throw new Exception("gogdl_has_no_uninstall_command"));
        var gp = await actual.PrepareUninstallAsync("100", gog.Path, gog.Job, default);
        Check(gp.Files.Count == 3 && handler.Calls == 1, "gog_manifest_actual_v" + version);
        await actual.UninstallAsync(gp, default);
        Check(!File.Exists(gog.Exe) && !File.Exists(info) && !File.Exists(gog.Marker) && File.Exists(gog.Foreign), "gog_only_manifest_files_v" + version);
        Check(!File.Exists(Path.Combine(manifests,"100")) && !File.Exists(Path.Combine(state,"gog","installed-100.json")), "gog_exact_registration_removed_v" + version);
        Check(handler.Calls == 2 && handler.LastHost == "gog-cdn-fastly.gog.com", "gog_only_official_metadata_v" + version);
    }
    var observational = new List<(string Mode, StoreUninstallPreparation Preparation)>();
    foreach(string raceMode in new[]{"before-prepare","during-prepare"})
    {
        var race=New("revision-"+raceMode,StoreKind.Amazon);var raceProvider=new FakeProvider(race);
        var otherStore=New("other-store-"+raceMode,StoreKind.Epic);var otherProvider=new FakeProvider(otherStore);
        await using var raceEngine=race.Engine(raceProvider,()=>DateTimeOffset.UtcNow,additional:[otherProvider]);await raceEngine.InitializeAsync();
        var rendered=await raceEngine.GetSnapshotAsync();var scoped=rendered.Games.Single();
        async Task BackgroundUpdate(){var state=await raceEngine.GetSnapshotAsync();Check((await raceEngine.RefreshAsync(StoreKind.Epic,state.Accounts.Single(a=>a.Store==StoreKind.Epic).Revision)).Accepted,"other store metadata refresh remains independent of Amazon preparation");}
        if(raceMode=="before-prepare")await BackgroundUpdate();else raceProvider.OnPrepare=BackgroundUpdate;
        var preparedRace=await raceEngine.PrepareUninstallAsync(scoped.Key,scoped.AccountRevision,rendered.Revision);
        observational.Add((raceMode,preparedRace));
        Console.WriteLine($"unrelated-library-update {raceMode}: accepted={preparedRace.CanUninstall}; error={preparedRace.ErrorCode??"none"}");
        if(preparedRace.CanUninstall)
        {
            Check(preparedRace.LibraryRevision==(await raceEngine.GetSnapshotAsync()).Revision,"preparation publishes current observational revision");
            await BackgroundUpdate();var removedRace=await raceEngine.UninstallAsync(preparedRace,Guid.NewGuid());
            Check(removedRace.Accepted&&raceProvider.Removals==1&&File.Exists(race.Foreign),"unrelated background update while dialog is open never invalidates exact scoped confirmation");
        }
    }
    Check(observational.All(row=>row.Preparation.CanUninstall),"unchanged installation must prepare on first click despite unrelated revision changes");
    foreach(string changedScope in new[]{"path","account","owner"})
    {
        var selection=New("changed-scope-"+changedScope,StoreKind.Amazon);var scopedProvider=new FakeProvider(selection);
        await using var scopedEngine=selection.Engine(scopedProvider,()=>DateTimeOffset.UtcNow);await scopedEngine.InitializeAsync();var observedScope=await scopedEngine.GetSnapshotAsync();var selectedGame=observedScope.Games.Single();
        var wrongPath=await scopedEngine.PrepareUninstallForPathAsync(selectedGame.Key,selectedGame.AccountRevision,observedScope.Revision,selection.Path+"-foreign");
        Check(!wrongPath.CanUninstall&&wrongPath.ErrorCode=="installation_changed"&&scopedProvider.Removals==0,"explicit selected path cannot be substituted before preparation");
        scopedProvider.OnPrepare=()=>
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            if(changedScope=="path")((Dictionary<StoreGameKey,StoreLibraryGame>)typeof(StoreQueueEngine).GetField("games",flags)!.GetValue(scopedEngine)!)[selectedGame.Key]=selectedGame with{InstallPath=selection.Path+"-foreign"};
            if(changedScope=="account"){var accounts=(Dictionary<StoreKind,StoreAccount>)typeof(StoreQueueEngine).GetField("accounts",flags)!.GetValue(scopedEngine)!;accounts[StoreKind.Amazon]=accounts[StoreKind.Amazon] with{Revision=accounts[StoreKind.Amazon].Revision+1};}
            if(changedScope=="owner"){var jobs=(List<StoreDownloadJob>)typeof(StoreQueueEngine).GetField("jobs",flags)!.GetValue(scopedEngine)!;jobs[0]=jobs[0] with{JobId=Guid.NewGuid()};}
            return Task.CompletedTask;
        };
        var rejectedScope=await scopedEngine.PrepareUninstallForPathAsync(selectedGame.Key,selectedGame.AccountRevision,observedScope.Revision,selection.Path);
        Check(!rejectedScope.CanUninstall&&rejectedScope.Nonce is null&&scopedProvider.Removals==0&&File.Exists(selection.Exe),"actual selected "+changedScope+" change during preparation never issues a deletion nonce");
    }
    Console.WriteLine($"PASS {checks} installed operations checks; synthetic files only");
}
catch (Exception e) { Console.Error.WriteLine("FAIL " + (e is StoreOperationException s ? s.Code : e.Message)); Environment.ExitCode = 1; }

sealed class Fixture
{
    public string Path { get; } public string Exe => System.IO.Path.Combine(Path,"game.exe"); public string Data=>System.IO.Path.Combine(Path,"data","data.bin");
    public string Marker=>System.IO.Path.Combine(Path,".playhub-install-owner");public string Foreign=>System.IO.Path.Combine(Path,"saves","user.save");
    public Guid Job {get;}=Guid.NewGuid();public StoreOwnedUninstallPlan Plan{get;}
    public Fixture(string path,StoreKind kind)
    {
        Path=path;Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Data)!);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Foreign)!);
        File.WriteAllText(Exe,"synthetic game payload");File.WriteAllText(Data,"synthetic data");File.WriteAllText(Foreign,"user-save");File.WriteAllText(Marker,Job.ToString("D"));
        StoreOwnedInstallFile Row(string file)=>new(System.IO.Path.GetRelativePath(path,file),new FileInfo(file).Length,"SHA1",Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(file))));
        Plan=new(new(kind,kind==StoreKind.Gog?"100":"synthetic"),path,Exe,Job,new string('A',64),Array.AsReadOnly(new[]{Row(Data),Row(Exe)}));
    }
    public StoreQueueEngine Engine(FakeProvider provider,Func<DateTimeOffset> now,bool pending=false,IStoreProvider[]? additional=null)
    {
        var queue=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!,System.IO.Path.GetFileName(Path)+"-queue.json");
        var account=new StoreAccount(Plan.Key.Store,StoreConnectionState.Connected,7,"synthetic",provider.Capabilities);
        var game=new StoreLibraryGame(Plan.Key,"Synthetic",7,provider.Capabilities,true,true,Path);
        var job=new StoreDownloadJob(Job,Plan.Key,"Synthetic",pending?StoreJobState.PausedByUser:StoreJobState.Completed,1,7,Path,1,1,null,now(),false,false,false);
        File.WriteAllText(queue,JsonSerializer.Serialize(new{Version=1,Revision=10L,Accounts=new[]{account},Games=new[]{game},Jobs=new[]{job},AccountIdentities=new Dictionary<StoreKind,string>{{Plan.Key.Store,"same-account"}},JobAccountIdentities=new Dictionary<Guid,string>{{Job,"same-account"}}}));
        return new(new IStoreProvider[]{provider}.Concat(additional??[]),new StoreInstallFileSystem(),queue,now);
    }
}
sealed class FakeProvider(Fixture fixture):IStoreProvider,IStoreInstalledGameProvider
{
    public StoreKind Store=>fixture.Plan.Key.Store;public StoreCapabilities Capabilities=>new(StoreInstallKind.Direct,true,true,true,true,true);public Uri AuthorizationUri=>new("https://example.invalid/");
    public int Removals;public TaskCompletionSource? Hold;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public Func<Task>? OnPrepare;
    public Task<ProviderAccount?> GetAccountAsync(CancellationToken t)=>Task.FromResult<ProviderAccount?>(new("same-account","Synthetic"));
    public Task<ProviderAccount> ConnectAsync(string c,CancellationToken t)=>throw new NotSupportedException();public Task DisconnectAsync(CancellationToken t)=>Task.CompletedTask;
    public Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken t)=>Task.FromResult<IReadOnlyList<ProviderGame>>([new(fixture.Plan.Key.Id,"Synthetic",true,1,fixture.Path,true)]);
    public Task<ProviderInstallSize> GetInstallSizeAsync(string id,CancellationToken t)=>Task.FromResult(new ProviderInstallSize(1,1));
    public Task<ProviderLaunch?> ResolveLaunchAsync(string id,string path,CancellationToken t)=>Task.FromResult<ProviderLaunch?>(new(Environment.ProcessPath!,fixture.Path,[],fixture.Exe));
    public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id,string path,CancellationToken t)=>Task.FromResult(new StorePrivateLaunchPlan(Guid.NewGuid(),fixture.Plan.Key,fixture.Exe,fixture.Path,[],new Dictionary<string,string>(),DateTimeOffset.UtcNow.AddSeconds(30)));
    public Task<IStoreTransfer> StartAsync(TransferRequest request,Action<TransferProgress> progress,CancellationToken t)=>throw new NotSupportedException();
    public Task<bool> CanUninstallAsync(string id,string path,Guid job,CancellationToken t)=>Task.FromResult(File.ReadAllText(fixture.Marker)==job.ToString("D"));
    public async Task<StoreOwnedUninstallPlan> PrepareUninstallAsync(string id,string path,Guid job,CancellationToken t){if(OnPrepare!=null)await OnPrepare();return fixture.Plan;}
    public async Task UninstallAsync(StoreOwnedUninstallPlan plan,CancellationToken t){Entered.TrySetResult();if(Hold!=null)await Hold.Task;StoreOwnedInstallFiles.Delete(plan,t);Removals++;}
}
sealed class CompletedTransfer:IStoreTransfer
{
    public Task<TransferCompletion> Completion=>Task.FromResult(new TransferCompletion(true,false,true));
    public Task RequestCheckpointAsync(CancellationToken token)=>Task.CompletedTask;
    public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
}
sealed class FakeHttp(string document,bool compressed=false):HttpMessageHandler
{
    public int Calls;public string? LastHost;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        Calls++;LastHost=request.RequestUri!.Host;var bytes=Encoding.UTF8.GetBytes(document);
        if(compressed){using var output=new MemoryStream();using(var z=new System.IO.Compression.ZLibStream(output,System.IO.Compression.CompressionMode.Compress,true))z.Write(bytes);bytes=output.ToArray();}
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(bytes)});
    }
}
