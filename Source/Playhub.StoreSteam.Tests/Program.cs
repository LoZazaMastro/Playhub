using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Playhub.StoreLibrary;

var root = Path.Combine(Path.GetTempPath(), "Playhub-Store-SteamChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var capabilities = new StoreCapabilities(StoreInstallKind.Direct, true, true, true, true, true);
var key = new StoreGameKey(StoreKind.Epic, "TestGame");
var account = new StoreAccount(StoreKind.Epic, StoreConnectionState.Connected, 7, "Fixture", capabilities);
var game = new StoreLibraryGame(key, "Fixture Game", 7, capabilities, true, true);
var snapshot = new StoreLibrarySnapshot(3, DateTimeOffset.UtcNow, [account], [game], []);
var path = Path.Combine(root, "registry.json");
StoreSteamShortcutRegistry Create() => new(_ => Task.FromResult(snapshot), Path.Combine(root, "Playhub.GameSession.exe"), Path.Combine(root, "Playhub.exe"), path);
var registry = Create();
int count = 0;
void Assert(bool ok, string label) { if (!ok) throw new Exception(label); ++count; }
async Task<T> Call<T>(string method, object input)
{
    var response = await registry.TryHandleAsync(method, JsonSerializer.SerializeToElement(input, StoreLibraryService.JsonOptions), default);
    Assert(response.Handled && response.Data is T, "Typed response " + method); return (T)response.Data!;
}
async Task Reject(string method, object input, string expected)
{
    try { await Call<object>(method, input); throw new Exception("Expected rejection: " + expected); }
    catch (InvalidOperationException e) when (e.Message == expected) { ++count; }
}
var request = await Call<StoreSteamSyncRequest>("steam-sync-request", new { store = StoreKind.Epic, expectedAccountRevision = 7 });
Assert(request.State == "pending" && request.Total == 1 && request.Confirmed == 0, "Explicit sync stays pending before Steam receipt");
Assert((await Call<StoreSteamSyncRequest>("steam-sync-request", new { store = StoreKind.Epic, expectedAccountRevision = 7 })).RequestId == request.RequestId, "Duplicate clicks share pending intent");
var plan = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
Assert(plan.Arguments == "--store-library epic TestGame" && plan.AppId is null, "Installed product has stable wrapper plan, no invented AppID");
var ack = new StoreSteamShortcutAck(request.RequestId, plan.ClaimId, 3456789012, plan.Executable, plan.Arguments, plan.StartDirectory);
await Reject("steam-sync-ack", ack with { Arguments = "--game C:\\foreign.exe" }, "steam_ack_invalid");
await Reject("steam-sync-ack", ack with { Executable = Path.Combine(root, "foreign.exe") }, "steam_ack_invalid");
await Reject("steam-sync-ack", ack with { StartDirectory = plan.StartDirectory + "-foreign" }, "steam_ack_invalid");
await Reject("steam-sync-ack", ack with { StartDirectory = plan.StartDirectory + "\0" }, "steam_ack_invalid");
Assert(!((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Any()), "Rejected proof claims no manual shortcut");
// Actual Windows Steam readback uses a trailing separator. Quote/slash/case
// differences describe the same absolute directory, not another launcher.
var nativeDirectory = "\"" + plan.StartDirectory.Replace('\\', '/').ToUpperInvariant() + "/\"";
var legacy = JsonNode.Parse(File.ReadAllText(path))!;
legacy.AsObject().Remove("createdRuntimeExecutable");
foreach (var intent in legacy["requests"]!.AsArray()) foreach (var claim in intent!["claims"]!.AsArray()) claim!.AsObject().Remove("createdAppId");
File.WriteAllText(path, legacy.ToJsonString()); registry = Create();
Assert((await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single().ClaimId == plan.ClaimId,
    "Old registry JSON without created fields remains compatible");
await Reject("steam-sync-created", ack with { AppId = 0, Arguments = "" }, "steam_ack_invalid");
await Reject("steam-sync-created", ack with { Executable = Path.Combine(root, "foreign.exe"), Arguments = "" }, "steam_ack_invalid");
await Reject("steam-sync-created", ack with { Arguments = "--foreign" }, "steam_ack_invalid");
await Reject("steam-sync-created", ack with { StartDirectory = plan.StartDirectory + "-foreign", Arguments = "" }, "steam_ack_invalid");
var draft = await Call<StoreSteamSyncRequest>("steam-sync-created", ack with { Arguments = "", StartDirectory = nativeDirectory });
Assert(draft.State == "pending" && draft.Confirmed == 0, "Native creation is durable draft, never confirmed ownership");
Assert(!(await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Any(), "Created draft grants zero owned games before final readback ACK");
registry = Create();
var resumed = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
Assert(resumed.CreatedAppId == ack.AppId && resumed.AppId is null && resumed.ClaimId == plan.ClaimId, "Host restart retains exact created ID without another shortcut creation");
var sealedDraftBytes = File.ReadAllBytes(path);
try
{
    _ = new StoreSteamShortcutRegistry(_ => Task.FromResult(snapshot), Path.Combine(root, "other-install", "Playhub.GameSession.exe"),
        Path.Combine(root, "other-install", "Playhub.exe"), path);
    throw new Exception("Draft silently moved to a foreign wrapper install");
}
catch (InvalidDataException) { ++count; }
Assert(File.ReadAllBytes(path).SequenceEqual(sealedDraftBytes), "Foreign runtime seal rejection preserves persisted proof");
await Reject("steam-sync-created", ack with { AppId = 3567890123u, Arguments = "" }, "steam_identity_conflict");
await Reject("steam-sync-ack", ack with { AppId = 12345 }, "steam_identity_conflict");
var otherGame = game with { Key = new(StoreKind.Epic, "OtherPending"), Title = "Other pending game" };
snapshot = snapshot with { Games = [game, otherGame] };
var otherPlan = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single(p => p.Key == otherGame.Key);
await Reject("steam-sync-created", new StoreSteamShortcutAck(otherPlan.RequestId, otherPlan.ClaimId, ack.AppId,
    otherPlan.Executable, "", otherPlan.StartDirectory), "steam_identity_conflict");
snapshot = snapshot with { Games = [game] };
_ = await Call<StoreSteamSyncPending>("steam-sync-pending", new { });
var disk = JsonNode.Parse(File.ReadAllText(path))!;
disk["requests"]!.AsArray().Single(r => r!["id"]!.GetValue<Guid>() == ack.RequestId)!["created"] = DateTimeOffset.UtcNow.AddMinutes(-21).ToString("O");
File.WriteAllText(path, disk.ToJsonString()); registry = Create();
await Reject("steam-sync-created", ack with { Arguments = "" }, "steam_sync_expired");
var renewed = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
Assert(renewed.CreatedAppId == ack.AppId && renewed.AppId is null && renewed.ClaimId != plan.ClaimId,
    "Expired claim transfers only durable same-account same-key draft to fresh intent");
Assert(!(await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Any(), "Renewed draft is still not owned or confirmed");
ack = ack with { RequestId = renewed.RequestId, ClaimId = renewed.ClaimId };
var done = await Call<StoreSteamSyncRequest>("steam-sync-ack", ack with { StartDirectory = nativeDirectory });
Assert(done.State == "completed" && done.Confirmed == 1, "Matching real Steam fields complete receipt");
Assert((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Single().StartDirectory == plan.StartDirectory,
    "Normalized readback preserves canonical stored wrapper directory");
snapshot = snapshot with { Games = [game, otherGame] };
var ownedConflictPlan = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
await Reject("steam-sync-created", new StoreSteamShortcutAck(ownedConflictPlan.RequestId, ownedConflictPlan.ClaimId, ack.AppId,
    ownedConflictPlan.Executable, "", ownedConflictPlan.StartDirectory), "steam_identity_conflict");
snapshot = snapshot with { Games = [game] }; _ = await Call<StoreSteamSyncPending>("steam-sync-pending", new { });
var originalRegistry = registry;
string driveRoot = Path.GetPathRoot(root)!;
registry = new(_ => Task.FromResult(snapshot), Path.Combine(driveRoot, "Playhub.GameSession.exe"),
    Path.Combine(driveRoot, "Playhub.exe"), Path.Combine(root, "root-directory-registry.json"));
var rootPlan = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
var rootAck = new StoreSteamShortcutAck(rootPlan.RequestId, rootPlan.ClaimId, 3456789013,
    rootPlan.Executable, rootPlan.Arguments, driveRoot);
await Reject("steam-sync-ack", rootAck with { StartDirectory = driveRoot.TrimEnd('\\', '/') }, "steam_ack_invalid");
Assert((await Call<StoreSteamSyncRequest>("steam-sync-ack", rootAck with { StartDirectory = "\"" + driveRoot.Replace('\\', '/') + "\"" })).State == "completed",
    "Drive root separator retained; drive-relative C: never accepted as C root");
registry = originalRegistry;
var before = File.ReadAllBytes(path);
snapshot = snapshot with { Accounts = [account with { State = StoreConnectionState.Expired }] };
await Reject("steam-sync-request", new { store = StoreKind.Epic, expectedAccountRevision = 7 }, "account_changed");
var preserved = (await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Single();
Assert(!preserved.AccountCurrent && preserved.AppId == ack.AppId && File.ReadAllBytes(path).SequenceEqual(before), "Expired session preserves Steam ownership and disk bytes");
registry = Create();
Assert((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned", new { })).Single().AppId == ack.AppId, "Owned identity survives host restart");
snapshot = snapshot with { Accounts = [account], Games = [game with { IsInstalled = true, IsLaunchable = true, InstallPath = Path.Combine(root, "game") }] };
var second = await Call<StoreSteamSyncRequest>("steam-sync-request", new { store = StoreKind.Epic, expectedAccountRevision = 7 });
var installedPlan = (await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Single();
Assert(installedPlan.AppId == ack.AppId && installedPlan.Arguments == plan.Arguments && installedPlan.Executable == plan.Executable, "Install never changes shortcut identity or wrapper");
await Reject("steam-sync-ack", ack with { RequestId = second.RequestId, ClaimId = installedPlan.ClaimId, AppId = 12 }, "steam_identity_conflict");
snapshot = snapshot with { Accounts = [account with { Revision = 8 }] };
await Reject("steam-sync-ack", ack with { RequestId = second.RequestId, ClaimId = installedPlan.ClaimId }, "steam_sync_expired");
Assert((await Call<StoreSteamSyncPending>("steam-sync-pending", new { })).Plans.Count == 0, "Account replacement retires stale sync claims");
snapshot=snapshot with { Accounts=[account],Games=[game with{IsInstalled=false,IsLaunchable=false}] };
var uninstalled=await Call<StoreSteamSyncRequest>("steam-sync-request",new{store=StoreKind.Epic,expectedAccountRevision=7});
Assert(uninstalled.State=="completed"&&uninstalled.Total==0,"sync does not import uninstalled catalog games");
Assert((await Call<IReadOnlyList<StoreSteamOwnedGame>>("steam-owned",new{})).Single().AppId==ack.AppId,"uninstall preserves existing shortcut ownership for reinstall");
var fresh=new StoreLibraryGame(new(StoreKind.Gog,"new-installed"),"New installed game",3,capabilities,true,true);
snapshot=snapshot with { Accounts=[account,new(StoreKind.Gog,StoreConnectionState.Connected,3,"Fixture",capabilities)],Games=[game with{IsInstalled=false},fresh] };
var automatic=(await Call<StoreSteamSyncPending>("steam-sync-pending",new{})).Plans.Single();
Assert(automatic.Key==fresh.Key,"confirmed installation automatically creates the exact store plan");
Assert((await Call<StoreSteamSyncPending>("steam-sync-pending",new{})).Plans.Single().ClaimId==automatic.ClaimId,"automatic polling retains one pending claim");
snapshot=snapshot with{Games=[game with{IsInstalled=false},fresh with{IsInstalled=false}]};
Assert((await Call<StoreSteamSyncPending>("steam-sync-pending",new{})).Plans.Count==0,"uninstall retires unapplied automatic plans");
Assert(!(await registry.TryHandleAsync("connect-complete", default, default)).Handled, "Registry cannot authenticate accounts");
var corrupt = File.ReadAllBytes(path); File.WriteAllText(path, "{broken");
try { registry = Create(); throw new Exception("Corrupt registry accepted"); } catch (JsonException) { ++count; }
Assert(File.ReadAllText(path) == "{broken", "Unreadable registry is never overwritten with an empty library");
File.WriteAllBytes(path, corrupt);
count += await ImportAdoptionChecks.Run(root);
count += await OfficialImportPlansChecks.Run(root);
count += await MigrationChecks.Run(root);
var duplicateTitleSnapshot=snapshot with {Accounts=[account,account with{Store=StoreKind.Gog}],Games=[game,game with{Key=new(StoreKind.Gog,"OriginalGog")} ]};
var duplicateTitles=new StoreSteamShortcutRegistry(_=>Task.FromResult(duplicateTitleSnapshot),Path.Combine(root,"Playhub.GameSession.exe"),Path.Combine(root,"Playhub.exe"),Path.Combine(root,"duplicate-titles.json"));
var duplicateReply=await duplicateTitles.TryHandleAsync("steam-sync-pending",JsonSerializer.SerializeToElement(new{}),default);
var duplicatePlans=((StoreSteamSyncPending)duplicateReply.Data!).Plans;
Assert(duplicatePlans.Count==2&&duplicatePlans.All(p=>p.Title==game.Title),"Same title across stores is preserved verbatim without store or hash suffixes");
Console.WriteLine($"PASS {count} actual registry checks; no Steam, account or game mutations. Fixture: {root}");

namespace Playhub.StoreLibrary
{
    public interface IStoreProtocolExtension { Task<(bool Handled, object? Data)> TryHandleAsync(string method, JsonElement args, CancellationToken token); }
    public static class StoreLibraryService { public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }; }
}
