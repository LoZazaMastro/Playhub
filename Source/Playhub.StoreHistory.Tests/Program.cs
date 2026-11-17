// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Synthetic receipts, retained fake transfers and one private local named pipe.
// No installed host, account, provider executable, game or network is accessed.
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Playhub.StoreLibrary;

await Checks.Run();
static class Checks
{
    private static int passed;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StoreCapabilities Caps = new(StoreInstallKind.Direct, true, true, true, true, true);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    private static FieldInfo Field(string name) => typeof(StoreQueueEngine).GetField(name, Hidden)!;
    private static List<StoreDownloadJob> Rows(StoreQueueEngine engine) => (List<StoreDownloadJob>)Field("jobs").GetValue(engine)!;
    private static void StopWatchdog(StoreQueueEngine engine) => ((CancellationTokenSource)Field("shutdown").GetValue(engine)!).Cancel();
    private static StoreDownloadJob Job(Guid id, StoreJobState state, string path) => new(id, new(StoreKind.Epic, "namespace:game"), "Original title", state, 7, 4, path, 10, 100, null, DateTimeOffset.UtcNow, false, false, false);
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "Playhub-Canceled-History-Inert-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await SingleAndRestart(root);
            await CombinedHistory(root);
            await UnrelatedProgressSelections(root);
            await RetainedSteamCompletion(root);
            await StateAndDrainGuards(root);
            await ActualCanceledTransfer(root);
            await UnconfirmedTransfer(root);
            await PersistenceFailure(root);
            await PipeDispatch(root);
            Console.WriteLine($"PASS {passed} canceled-history checks; synthetic receipts/fake transfers/private local pipe only, no game/provider/account access.");
        }
        finally
        {
            string path = Path.GetFullPath(root), parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            if (Path.GetDirectoryName(path) != parent || !Path.GetFileName(path).StartsWith("Playhub-Canceled-History-Inert-", StringComparison.Ordinal)) throw new Exception("Fixture cleanup path escaped TEMP.");
            Directory.Delete(path, true);
        }
    }
    private static string Seed(string root, string name, params StoreDownloadJob[] jobs)
    {
        var path = Path.Combine(root, name + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Version = 1, Revision = 20L,
            Accounts = new[] { new StoreAccount(StoreKind.Epic, StoreConnectionState.Connected, 4, "Fixture account", Caps) },
            Games = new[] { new StoreLibraryGame(new(StoreKind.Epic, "namespace:game"), "Retained game", 4, Caps, false, false) },
            Jobs = jobs, AccountIdentities = new Dictionary<StoreKind, string> { [StoreKind.Epic] = "original-account-proof" },
            JobAccountIdentities = jobs.ToDictionary(job => job.JobId, _ => "original-account-proof") }));
        return path;
    }
    private static StoreQueueEngine Engine(string path, Files files)
    { var engine = new StoreQueueEngine([new Provider()], files, path); StopWatchdog(engine); return engine; }
    private static async Task SingleAndRestart(string root)
    {
        string payload = Path.Combine(root, "original-owned-folder"); Directory.CreateDirectory(payload);
        var canceled = Job(Guid.NewGuid(), StoreJobState.Canceled, payload); var other = canceled with { JobId = Guid.NewGuid() };
        var completed = canceled with { JobId = Guid.NewGuid(), State = StoreJobState.Completed };
        var external = canceled with { JobId = Guid.NewGuid(), State = StoreJobState.ExternalHandoff, ReasonCode = "windows_store_managed", ErrorCode = "windows_store_install_unconfirmed" };
        string marker = Path.Combine(payload, ".playhub-install-owner"), game = Path.Combine(payload, "user-game.bin");
        File.WriteAllText(marker, canceled.JobId.ToString("D")); File.WriteAllBytes(game, [10, 20, 30, 40]);
        string path = Seed(root, "single", canceled, other, completed, external); var files = new Files();
        await using (var engine = Engine(path, files))
        {
            var before = await engine.GetSnapshotAsync();
            Check((await engine.ClearCanceledAsync(new(before.Revision, Guid.Empty, canceled.JobId))).ErrorCode == "invalid_request", "empty request ID refused");
            Check((await engine.ClearCanceledAsync(new(before.Revision, Guid.NewGuid(), Guid.Empty))).ErrorCode == "invalid_request", "empty job ID refused");
            Check((await engine.ClearCanceledAsync(new(before.Revision - 1, Guid.NewGuid(), canceled.JobId))).ErrorCode == "queue_changed", "stale revision cannot hide history");
            Check((await engine.GetSnapshotAsync()).Jobs.Count == 4, "refusals leave all rows visible");
            var request = new StoreClearCanceledRequest(before.Revision, Guid.NewGuid(), canceled.JobId);
            var clear = await engine.ClearCanceledAsync(request);
            Check(clear.Accepted && clear.JobId == canceled.JobId, "single exact canceled job acknowledged");
            Check(clear.Snapshot.Revision == before.Revision + 1 && clear.Snapshot.Jobs.Count == 3, "one tombstone changes revision once");
            Check(clear.Snapshot.Jobs.All(job => job.JobId != canceled.JobId) && clear.Snapshot.Jobs.Any(job => job.JobId == other.JobId), "only selected canceled row hidden");
            Check((await engine.ClearCanceledAsync(request)).Accepted && (await engine.GetSnapshotAsync()).Revision == clear.Snapshot.Revision, "lost response replay is idempotent without another revision");
            Check((await engine.ClearCanceledAsync(request with { JobId = other.JobId })).ErrorCode == "request_id_reused", "request ID cannot target another row");
            Check((await engine.ClearCanceledAsync(request with { ExpectedRevision = clear.Snapshot.Revision })).ErrorCode == "request_id_reused", "request ID cannot change expected revision");
            var again = await engine.ClearCanceledAsync(new(clear.Snapshot.Revision, Guid.NewGuid(), canceled.JobId));
            Check(again.Accepted && again.Snapshot.Revision == clear.Snapshot.Revision, "already hidden exact row is a harmless no-op");
            Check((await engine.ClearCanceledAsync(new(clear.Snapshot.Revision, Guid.NewGuid(), Guid.NewGuid()))).ErrorCode == "job_not_found", "unknown ID never substitutes another row");
            Check(File.ReadAllBytes(game).SequenceEqual(new byte[] { 10, 20, 30, 40 }) && File.ReadAllText(marker) == canceled.JobId.ToString("D"), "payload and ownership marker untouched");
            using var disk = JsonDocument.Parse(File.ReadAllText(path)); var data = disk.RootElement;
            Check(data.GetProperty("Jobs").GetArrayLength() == 4 && data.GetProperty("Games").GetArrayLength() == 1, "original private jobs and game receipt remain stored");
            Check(data.GetProperty("JobAccountIdentities").GetProperty(canceled.JobId.ToString()).GetString() == "original-account-proof" && data.GetProperty("AccountIdentities").GetProperty("Epic").GetString() == "original-account-proof", "exact account/job provenance retained");
            Check(data.GetProperty("ClearedCanceledJobs").EnumerateArray().Single().GetGuid() == canceled.JobId, "only exact tombstone persisted");
            Check(files.Calls == 0, "history mutation never calls install filesystem");
        }
        await using (var restored = Engine(path, files))
        {
            var snapshot = await restored.GetSnapshotAsync();
            Check(snapshot.Jobs.All(job => job.JobId != canceled.JobId) && snapshot.Jobs.Any(job => job.JobId == other.JobId), "restart preserves hidden canceled row and remaining history");
            Check(Rows(restored).Count == 4, "restart retains all private original receipts");
            var bulk = await restored.ClearCanceledAsync(new(snapshot.Revision, Guid.NewGuid()));
            Check(bulk.Accepted && bulk.Snapshot.Jobs.Count == 2, "bulk history removal selects canceled rows only");
            Check(bulk.Snapshot.Jobs.Any(job => job.State == StoreJobState.Completed) && bulk.Snapshot.Jobs.Any(job => job.State == StoreJobState.ExternalHandoff), "completed and managed unconfirmed rows remain visible");
            Check(File.Exists(game) && File.Exists(marker) && files.Calls == 0, "restart and bulk remove no files");
        }
        // Old JSON without the append-only optional field remains readable.
        string legacy = Seed(root, "legacy", canceled);
        await using var old = Engine(legacy, files);
        Check((await old.GetSnapshotAsync()).Jobs.Single().JobId == canceled.JobId, "legacy queue schema remains compatible");
        var invalidDisk = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(legacy))!;
        invalidDisk["Jobs"]![0]!["State"] = (int)StoreJobState.Interrupted;
        invalidDisk["ClearedCanceledJobs"] = new System.Text.Json.Nodes.JsonArray(canceled.JobId.ToString("D"));
        string invalidPath = Path.Combine(root, "invalid-tombstone.json"); File.WriteAllText(invalidPath, invalidDisk.ToJsonString());
        await using var invalid = Engine(invalidPath, files);
        Check((await invalid.GetSnapshotAsync()).Jobs.Count == 0 && Rows(invalid).Single().State == StoreJobState.Interrupted, "dismissed request stays hidden while its retained interrupted receipt survives restart");
    }
    private static async Task StateAndDrainGuards(string root)
    {
        var job = Job(Guid.NewGuid(), StoreJobState.Canceled, root); var files = new Files();
        await using var engine = Engine(Seed(root, "guards", job), files);
        foreach (var state in Enum.GetValues<StoreJobState>().Where(state => state != StoreJobState.Canceled))
        {
            Rows(engine)[0] = job with { State = state };
            var before = await engine.GetSnapshotAsync(); var result = await engine.ClearCanceledAsync(new(before.Revision, Guid.NewGuid(), job.JobId));
            Check(!result.Accepted && result.ErrorCode == "job_not_canceled" && result.Snapshot.Jobs.Count == 1 && result.Snapshot.Revision == before.Revision, "non-canceled state refused: " + state);
        }
        Rows(engine)[0] = job; Field("active").SetValue(engine, job.JobId);
        var active = await engine.ClearCanceledAsync(new((await engine.GetSnapshotAsync()).Revision, Guid.NewGuid(), job.JobId));
        Check(!active.Accepted && active.ErrorCode == "provider_busy", "even a canceled label cannot hide a retained active worker");
        Field("active").SetValue(engine, null);
        Rows(engine)[0] = job with { ErrorCode = "worker_drain_unconfirmed" };
        var unconfirmed = await engine.ClearCanceledAsync(new((await engine.GetSnapshotAsync()).Revision, Guid.NewGuid(), job.JobId));
        Check(!unconfirmed.Accepted && unconfirmed.ErrorCode == "provider_busy", "unconfirmed drain diagnostic prevents hidden row");
        var bulk = await engine.ClearCanceledAsync(new((await engine.GetSnapshotAsync()).Revision, Guid.NewGuid()));
        Check(bulk.Accepted && bulk.Snapshot.Jobs.Count == 1, "bulk preserves an unconfirmed drain row");
        Check(files.Calls == 0, "rejected states cannot touch install filesystem");
    }
    private static async Task CombinedHistory(string root)
    {
        var canceled = Job(Guid.NewGuid(), StoreJobState.Canceled, root);
        var completed = canceled with { JobId = Guid.NewGuid(), State = StoreJobState.Completed };
        var remainingCanceled = canceled with { JobId = Guid.NewGuid() };
        var remainingCompleted = completed with { JobId = Guid.NewGuid() };
        var failed = canceled with { JobId = Guid.NewGuid(), State = StoreJobState.Failed };
        var unconfirmed = canceled with { JobId = Guid.NewGuid(), State = StoreJobState.ExternalHandoff, ErrorCode = "windows_store_install_unconfirmed" };
        var files = new Files(); string path = Seed(root, "combined", canceled, completed, remainingCanceled, remainingCompleted, failed, unconfirmed);
        await using (var engine = Engine(path, files))
        {
            var initial = await engine.GetSnapshotAsync();
            Check((await engine.ClearCompletedAsync(new(initial.Revision - 1, Guid.NewGuid()))).ErrorCode == "queue_changed" && (await engine.GetSnapshotAsync()).Jobs.Count == 6, "combined stale clear hides neither completed nor canceled rows");
            var remove = new StoreRemoveHistoryRequest(initial.Revision, Guid.NewGuid(), completed.JobId);
            var first = await engine.RemoveHistoryAsync(remove);
            Check(first.Accepted && first.JobId == completed.JobId && first.Snapshot.Jobs.Count == 5 && first.Snapshot.Jobs.Any(job => job.JobId == remainingCompleted.JobId), "single completed removal cannot silently clear every completed row");
            Check((await engine.RemoveHistoryAsync(remove)).Accepted && (await engine.GetSnapshotAsync()).Revision == first.Snapshot.Revision, "single completed removal response replay is idempotent");
            Check((await engine.RemoveHistoryAsync(remove with { JobId = canceled.JobId })).ErrorCode == "request_id_reused", "single history request cannot change target");
            Check((await engine.RemoveHistoryAsync(new(first.Snapshot.Revision, Guid.NewGuid(), failed.JobId))).ErrorCode == "job_not_terminal", "failed job cannot be silently discarded as terminal history");
            Check((await engine.RemoveHistoryAsync(new(first.Snapshot.Revision, Guid.NewGuid(), unconfirmed.JobId))).ErrorCode == "job_not_terminal", "Windows Store unconfirmed handoff cannot be removed");
            var second = await engine.RemoveHistoryAsync(new(first.Snapshot.Revision, Guid.NewGuid(), canceled.JobId));
            Check(second.Accepted && second.Snapshot.Jobs.Count == 4 && second.Snapshot.Jobs.Any(job => job.JobId == remainingCanceled.JobId), "single canceled removal also leaves other canceled rows");
            var request = new StoreClearCompletedRequest(second.Snapshot.Revision, Guid.NewGuid());
            var clear = await engine.ClearCompletedAsync(request);
            Check(clear.Accepted && clear.Snapshot.Jobs.Count == 2 && clear.Snapshot.Jobs.All(job => job.State is StoreJobState.Failed or StoreJobState.ExternalHandoff), "Clear completed clears both safe Completed and Canceled history");
            Check(clear.Snapshot.Revision == second.Snapshot.Revision + 1 && (await engine.ClearCompletedAsync(request)).Accepted, "combined clear commits one revision and supports lost-ACK replay");
            Check(Rows(engine).Count == 6 && files.Calls == 0, "combined and single removals retain all private receipts without filesystem/provider calls");
        }
        await using var restored = Engine(path, files);
        Check((await restored.GetSnapshotAsync()).Jobs.Count == 2 && Rows(restored).Count == 6, "both completed and canceled tombstones survive restart while receipts remain");
    }
    private static StoreHistorySelection Selection(StoreDownloadJob job)=>new(job.JobId,job.Revision,job.Key,job.AccountRevision);
    private static async Task UnrelatedProgressSelections(string root)
    {
        var complete=Job(Guid.NewGuid(),StoreJobState.Completed,root);var canceled=complete with{JobId=Guid.NewGuid(),State=StoreJobState.Canceled};
        var remaining=complete with{JobId=Guid.NewGuid()};var active=complete with{JobId=Guid.NewGuid(),State=StoreJobState.Downloading};var files=new Files();
        await using var engine=Engine(Seed(root,"scoped-progress",complete,canceled,remaining,active),files);
        Rows(engine)[3]=active;Field("active").SetValue(engine,active.JobId);
        async Task Tick()=>await (Task)typeof(StoreQueueEngine).GetMethod("ProgressAsync",Hidden)!.Invoke(engine,[active.JobId,new TransferProgress(20,100,10,"downloading")])!;
        var reviewed=await engine.GetSnapshotAsync();await Tick();var current=await engine.GetSnapshotAsync();
        Check(current.Revision>reviewed.Revision&&current.Jobs.Single(j=>j.JobId==complete.JobId).Revision==complete.Revision,"actual unrelated worker progress changes global revision while terminal selection stays unchanged");
        var legacy=await engine.RemoveHistoryAsync(new(reviewed.Revision,Guid.NewGuid(),complete.JobId));
        Check(!legacy.Accepted&&legacy.ErrorCode=="queue_changed","reproduces original BLiNX history failure from unrelated active worker progress");
        var request=new StoreRemoveHistoryRequest(reviewed.Revision,Guid.NewGuid(),complete.JobId,complete.Revision,complete.Key,complete.AccountRevision);
        Check((await engine.RemoveHistoryAsync(request with{ExpectedRevision=current.Revision+100})).ErrorCode=="queue_changed","scoped history cannot claim a future global observation");
        Check((await engine.RemoveHistoryAsync(request with{RequestId=Guid.NewGuid(),ExpectedKey=new(StoreKind.Gog,"other")})).ErrorCode=="job_selection_changed","history cannot substitute selected game or store");
        Check((await engine.RemoveHistoryAsync(request with{RequestId=Guid.NewGuid(),ExpectedAccountRevision=999})).ErrorCode=="job_selection_changed","history selection keeps exact captured account scope");
        Check((await engine.RemoveHistoryAsync(request with{RequestId=Guid.NewGuid(),ExpectedJobRevision=complete.Revision-1})).ErrorCode=="job_selection_changed","changed selected job lifecycle remains protected");
        Check((await engine.RemoveHistoryAsync(request with{RequestId=Guid.NewGuid(),ExpectedKey=null})).ErrorCode=="invalid_request","partial selection cannot downgrade to legacy authority");
        await Tick();var removed=await engine.RemoveHistoryAsync(request);
        Check(removed.Accepted&&removed.Snapshot.Jobs.All(j=>j.JobId!=complete.JobId)&&removed.Snapshot.Jobs.Any(j=>j.JobId==active.JobId),"scoped terminal removal succeeds despite new progress and preserves active worker");
        Check((await engine.RemoveHistoryAsync(request)).Accepted,"scoped terminal lost-response replay is idempotent");
        Check((await engine.RemoveHistoryAsync(request with{ExpectedAccountRevision=999})).ErrorCode=="request_id_reused","replayed scoped request cannot change captured account");
        var newCanceled=canceled with{JobId=Guid.NewGuid()};Rows(engine).Add(newCanceled);await Tick();
        var clearCanceled=await engine.ClearCanceledAsync(new(reviewed.Revision,Guid.NewGuid(),Selections:[Selection(canceled)]));
        Check(clearCanceled.Accepted&&clearCanceled.Snapshot.Jobs.All(j=>j.JobId!=canceled.JobId)&&clearCanceled.Snapshot.Jobs.Any(j=>j.JobId==newCanceled.JobId),"scoped bulk canceled clear removes reviewed rows only, leaving later terminal arrivals");
        var newCompleted=remaining with{JobId=Guid.NewGuid()};Rows(engine).Add(newCompleted);await Tick();
        var clearComplete=await engine.ClearCompletedAsync(new(reviewed.Revision,Guid.NewGuid(),[Selection(remaining)]));
        Check(clearComplete.Accepted&&clearComplete.Snapshot.Jobs.All(j=>j.JobId!=remaining.JobId)&&clearComplete.Snapshot.Jobs.Any(j=>j.JobId==newCompleted.JobId)&&clearComplete.Snapshot.Jobs.Any(j=>j.JobId==newCanceled.JobId),"scoped bulk complete clear ignores unrelated progress and never clears unreviewed terminal rows");
        Check((await engine.ClearCompletedAsync(new(reviewed.Revision,Guid.NewGuid(),[Selection(active)]))).ErrorCode=="job_not_terminal","scope cannot turn an active download into removable history");
        Check(files.Calls==0&&Field("active").GetValue(engine) is Guid id&&id==active.JobId,"all history actions preserve worker slot and perform zero installation filesystem calls");
        var activeReviewed=(await engine.GetSnapshotAsync()).Jobs.Single(j=>j.JobId==active.JobId);
        await (Task)typeof(StoreQueueEngine).GetMethod("ProgressAsync",Hidden)!.Invoke(engine,[active.JobId,new TransferProgress(50,100,10,"installing")])!;
        Check((await engine.CancelCleanupAsync(new(active.JobId,activeReviewed.Revision,Guid.NewGuid(),false))).ErrorCode=="job_changed","legacy active cancellation reproduces lifecycle progress revision refusal");
        var cancel=new StoreCancelCleanupRequest(active.JobId,activeReviewed.Revision,Guid.NewGuid(),false,activeReviewed.Key,activeReviewed.AccountRevision,activeReviewed.InstallPath);
        Check((await engine.CancelCleanupAsync(cancel with{ExpectedInstallPath=Path.Combine(root,"foreign")})).ErrorCode=="job_selection_changed","scoped cancellation cannot cross reviewed install destination");
        var accepted=await engine.CancelCleanupAsync(cancel);
        Check(accepted.Accepted&&accepted.Snapshot.Jobs.All(j=>j.JobId!=active.JobId)&&Field("active").GetValue(engine) is Guid retained&&retained==active.JobId,"scoped active cancellation survives progress and removes row while retaining original drainage lease");
        Check((await engine.CancelCleanupAsync(cancel with{ExpectedKey=new(StoreKind.Gog,"foreign")})).ErrorCode=="request_id_reused","accepted cancellation nonce cannot be replayed with substituted scope");

    }
    private static async Task RetainedSteamCompletion(string root)
    {
        string install=Path.Combine(root,"retained-install"),registryPath=Path.Combine(root,"retained-steam.json");
        var completed=Job(Guid.NewGuid(),StoreJobState.Completed,install) with{Key=new(StoreKind.Epic,"RetainedCompletion"),UpdatedAt=DateTimeOffset.UtcNow.AddMinutes(-7)};
        string path=Seed(root,"retained-completion",completed);var files=new Files();var engine=Engine(path,files);
        void ConnectFixtureAccount(StoreQueueEngine owner){var accounts=(Dictionary<StoreKind,StoreAccount>)Field("accounts").GetValue(owner)!;accounts[StoreKind.Epic]=accounts[StoreKind.Epic] with{State=StoreConnectionState.Connected};}
        ConnectFixtureAccount(engine);
        var games=(Dictionary<StoreGameKey,StoreLibraryGame>)Field("games").GetValue(engine)!;games.Clear();
        var game=new StoreLibraryGame(completed.Key,completed.Title,completed.AccountRevision,Caps,true,true,install);
        games.Add(game.Key,game);typeof(StoreQueueEngine).GetMethod("Changed",Hidden)!.Invoke(engine,null);
        StoreSteamShortcutRegistry Registry(StoreQueueEngine owner)=>new(owner.GetSnapshotAsync,Path.Combine(root,"Playhub.GameSession.exe"),Path.Combine(root,"Playhub.exe"),registryPath,activeSteamAccount:()=>"76561198000000000",retainedCompletions:owner.GetRetainedCompletedInstallsAsync);
        var registry=Registry(engine);
        async Task<T> Call<T>(string method,object args){var reply=await registry.TryHandleAsync(method,JsonSerializer.SerializeToElement(args,StoreLibraryService.JsonOptions),default);Check(reply.Handled&&reply.Data is T,"retained completion registry handles "+method);return(T)reply.Data!;}
        var pending=await Call<StoreSteamSyncPending>("steam-sync-pending",new{});Check(pending.Plans.Count==1,"verified same-account installed fixture has one Steam ownership plan");var plan=pending.Plans.Single();
        var ack=new StoreSteamShortcutAck(plan.RequestId,plan.ClaimId,3456789123,plan.Executable,plan.Arguments,plan.StartDirectory);
        await Call<StoreSteamSyncRequest>("steam-sync-created",ack with{Arguments=""});await Call<StoreSteamSyncRequest>("steam-sync-ack",ack);
        var visible=(await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned",new{})).Single();
        Check(visible.Job?.JobId==completed.JobId&&visible.Job.UpdatedAt==completed.UpdatedAt,"visible completion uses the original terminal timestamp");
        var snapshot=await engine.GetSnapshotAsync();Check((await engine.ClearCompletedAsync(new(snapshot.Revision,Guid.NewGuid(),[Selection(completed)]))).Accepted,"actual clear-history hides the reviewed completion");
        Check((await engine.GetSnapshotAsync()).Jobs.Count==0,"public snapshot and Downloads stay empty after history clear");
        var owned=(await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned",new{})).Single();
        Check(owned.Job?.JobId==completed.JobId&&owned.Job.State==StoreJobState.Completed&&owned.Job.UpdatedAt==completed.UpdatedAt,"Steam retains the verified completion event and exact timestamp after history is cleared");
        Check((await engine.GetSnapshotAsync()).Jobs.Count==0&&Rows(engine).Count==1,"steam-owned read does not resurrect a visible queue row or create a new receipt");
        await engine.DisposeAsync();engine=Engine(path,files);ConnectFixtureAccount(engine);((Dictionary<StoreGameKey,StoreLibraryGame>)Field("games").GetValue(engine)!)[game.Key]=game;registry=Registry(engine);
        try
        {
            var restarted=(await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned",new{})).Single();
            Check(restarted.Job?.JobId==completed.JobId&&restarted.Job.UpdatedAt==completed.UpdatedAt&&(await engine.GetSnapshotAsync()).Jobs.Count==0,"retained completion survives host restart without new timestamps or queue resurrection");
            games=(Dictionary<StoreGameKey,StoreLibraryGame>)Field("games").GetValue(engine)!;
            games[game.Key]=game with{InstallPath=Path.Combine(root,"foreign-install")};
            Check((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned",new{})).Single().Job is null,"retained completion cannot cross an installed destination change");games[game.Key]=game;
            var identities=(Dictionary<StoreKind,string>)Field("accountIdentities").GetValue(engine)!;identities[StoreKind.Epic]="different-account-proof";
            Check((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned",new{})).Single().Job is null,"retained completion cannot borrow a different account identity at the same revision");identities[StoreKind.Epic]="original-account-proof";
            var active=completed with{JobId=Guid.NewGuid(),State=StoreJobState.Downloading,UpdatedAt=DateTimeOffset.UtcNow};Rows(engine).Add(active);
            Check((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned",new{})).Single().Job?.JobId==active.JobId,"retained completion never replaces a visible active download context");Rows(engine).Remove(active);
            games[game.Key]=game with{IsInstalled=false,IsLaunchable=false};
            Check((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned",new{})).Single().Job is null,"uninstalled games cannot inherit a cleared completion event");
            Check(files.Calls==0,"retained completion publication performs zero installation filesystem operations");
        }
        finally{await engine.DisposeAsync();}
    }
    private static async Task ActualCanceledTransfer(string root)
    {
        var job = Job(Guid.NewGuid(), StoreJobState.Canceled, root); var files = new Files();
        await using var engine = Engine(Seed(root, "drain", job), files);
        Rows(engine)[0] = job with { State = StoreJobState.Downloading, CanCancel = true };
        var transfer = new Transfer(); Field("active").SetValue(engine, job.JobId); Field("transfer").SetValue(engine, transfer);
        var cancel = await engine.ControlAsync(new(job.JobId, job.Revision, StoreJobAction.Cancel, Guid.NewGuid()));
        Check(cancel.Accepted && cancel.Snapshot.Jobs.Single().State == StoreJobState.Canceling && transfer.Stops == 1, "actual cancel enters Canceling and requests owned worker stop");
        Check(!(await engine.ClearCanceledAsync(new(cancel.Snapshot.Revision, Guid.NewGuid(), job.JobId))).Accepted, "Canceling stays visible until actual drain acknowledgement");
        transfer.Done.SetResult(new(false, false, false, Stopped: true, DrainConfirmed: true));
        await (Task)typeof(StoreQueueEngine).GetMethod("FinishAsync", Hidden)!.Invoke(engine, [job.JobId, transfer])!;
        var drained = await engine.GetSnapshotAsync();
        Check(drained.Jobs.Single().State == StoreJobState.Canceled && transfer.Disposals == 1, "completion path confirms stopped transfer and releases its slot");
        Check((await engine.ClearCanceledAsync(new(drained.Revision, Guid.NewGuid(), job.JobId))).Snapshot.Jobs.Count == 0, "only confirmed terminal cancellation becomes removable");
        Check(files.Calls == 0, "confirmed removal still performs zero install filesystem calls");
    }
    private static async Task PipeDispatch(string root)
    {
        var job = Job(Guid.NewGuid(), StoreJobState.Canceled, root); var files = new Files();
        await using var engine = Engine(Seed(root, "pipe", job), files);
        string pipe = "Playhub-Canceled-History-Test-" + Guid.NewGuid().ToString("N"); using var stop = new CancellationTokenSource();
        var serving = new StorePipeServer(engine, _ => true, pipeName: pipe).RunAsync(stop.Token);
        try
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000); using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true }; using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            var id = Guid.NewGuid(); var request = new StoreRemoveHistoryRequest(20, Guid.NewGuid(), job.JobId);
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { protocol = 1, id, method = "remove-history", args = request }, StoreLibraryService.JsonOptions));
            using var response = JsonDocument.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!);
            var data = response.RootElement;
            Check(data.GetProperty("id").GetGuid() == id && data.GetProperty("ok").GetBoolean(), "actual pipe dispatcher admits typed single remove-history request");
            Check(data.GetProperty("data").GetProperty("accepted").GetBoolean() && data.GetProperty("data").GetProperty("snapshot").GetProperty("jobs").GetArrayLength() == 0, "actual pipe ACK carries authoritative hidden history snapshot");
            Check(typeof(IStoreCanceledDownloadHistoryService).IsAssignableFrom(typeof(StoreLibraryService)) && typeof(IStoreHistoryRemovalService).IsAssignableFrom(typeof(StoreLibraryService)), "pipe client publishes both optional history capabilities");
        }
        finally { stop.Cancel(); try { await serving; } catch (OperationCanceledException) { } }
    }
    private static async Task UnconfirmedTransfer(string root)
    {
        var job = Job(Guid.NewGuid(), StoreJobState.Canceled, root); var files = new Files();
        await using var engine = Engine(Seed(root, "unconfirmed", job), files);
        Rows(engine)[0] = job with { State = StoreJobState.Downloading, CanCancel = true };
        var transfer = new Transfer(); Field("active").SetValue(engine, job.JobId); Field("transfer").SetValue(engine, transfer);
        await engine.ControlAsync(new(job.JobId, job.Revision, StoreJobAction.Cancel, Guid.NewGuid()));
        transfer.Done.SetResult(new(false, false, false, "fixture stop unconfirmed", Stopped: true, DrainConfirmed: false));
        await (Task)typeof(StoreQueueEngine).GetMethod("FinishAsync", Hidden)!.Invoke(engine, [job.JobId, transfer])!;
        var snapshot = await engine.GetSnapshotAsync();
        Check(snapshot.Jobs.Single().State == StoreJobState.Interrupted && snapshot.Jobs.Single().ErrorCode == "worker_drain_unconfirmed", "real completion path refuses to label an unconfirmed stopped worker as canceled");
        var clear = await engine.ClearCanceledAsync(new(snapshot.Revision, Guid.NewGuid(), job.JobId));
        Check(!clear.Accepted && clear.ErrorCode == "job_not_canceled" && clear.Snapshot.Jobs.Count == 1, "unconfirmed worker remains visible after clear request");
        Check((Guid?)Field("active").GetValue(engine) == job.JobId && ReferenceEquals(Field("transfer").GetValue(engine), transfer), "history request cannot release an unconfirmed worker slot");
        Check(transfer.Disposals == 0 && files.Calls == 0, "history request cannot dispose an unconfirmed worker or touch files");
        // Release this inert fake explicitly so test teardown does not wait for
        // a deliberately unconfirmed imaginary process. Production never does this.
        Field("active").SetValue(engine, null); Field("transfer").SetValue(engine, null); await transfer.DisposeAsync();
    }
    private static async Task PersistenceFailure(string root)
    {
        var job = Job(Guid.NewGuid(), StoreJobState.Canceled, root); var files = new Files();
        string path = Seed(root, "persistence-failure", job), original = File.ReadAllText(path);
        await using var engine = Engine(path, files);
        var before = await engine.GetSnapshotAsync();
        File.Delete(path); Directory.CreateDirectory(path); // exact uniquely owned TEMP fixture only
        try
        {
            bool refused = false;
            try { await engine.ClearCanceledAsync(new(before.Revision, Guid.NewGuid(), job.JobId)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { refused = true; }
            Check(refused, "failed persistence cannot acknowledge history removal");
            var after = await engine.GetSnapshotAsync();
            Check(after.Revision == before.Revision && after.Jobs.Single().JobId == job.JobId, "failed persistence rolls back the in-memory tombstone and revision");
        }
        finally { Directory.Delete(path); File.WriteAllText(path, original); }
    }
    private sealed class Files : IStoreInstallFileSystem
    {
        internal int Calls;
        private Exception Unexpected() { Calls++; return new Exception("History touched install filesystem."); }
        public IReadOnlyList<StoreInstallLocation> Locations() => throw Unexpected(); public string CanonicalDestination(string path) => throw Unexpected();
        public long FreeBytes(string path) => throw Unexpected(); public bool CanClaim(string path, Guid? job) => throw Unexpected(); public bool Claim(string path, Guid job) => throw Unexpected();
    }
    private sealed class Transfer : IStoreTransfer
    {
        internal readonly TaskCompletionSource<TransferCompletion> Done = new(TaskCreationOptions.RunContinuationsAsynchronously); internal int Stops, Disposals;
        public Task<TransferCompletion> Completion => Done.Task; public Task RequestCheckpointAsync(CancellationToken token) { Stops++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Provider : IStoreProvider
    {
        public StoreKind Store => StoreKind.Epic; public StoreCapabilities Capabilities => Caps;
        public Uri AuthorizationUri => throw new Exception("No authorization");
        public Task<ProviderAccount?> GetAccountAsync(CancellationToken token) => throw new Exception("No account");
        public Task<ProviderAccount> ConnectAsync(string code, CancellationToken token) => throw new Exception("No account");
        public Task DisconnectAsync(CancellationToken token) => throw new Exception("No account");
        public Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken token) => throw new Exception("No library request");
        public Task<ProviderInstallSize> GetInstallSizeAsync(string id, CancellationToken token) => throw new Exception("No provider metadata");
        public Task<ProviderLaunch?> ResolveLaunchAsync(string id, string path, CancellationToken token) => throw new Exception("No game launch");
        public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id, string path, CancellationToken token) => throw new Exception("No game launch");
        public Task<IStoreTransfer> StartAsync(TransferRequest request, Action<TransferProgress> progress, CancellationToken token) => throw new Exception("No provider execution");
    }
}
