using System.Text.Json.Nodes;
using Playhub.Importing;
using Playhub.Emulation.Workbench;
using VDFParser.Models;
using Playhub.Services;

var checks = 0;
void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
void Invalid(Action action, string reason) { try { action(); throw new Exception(reason); } catch (ArgumentException) { checks++; } }
Check(!await new SteamPluginBridge().AppReadyAsync(0, CancellationToken.None), "Unimported identity never attempts CDP readiness");
var firstScope=Playhub.Integrations.ApplicationIntegrationIdentity.Create("emu","rom-exact-one");
var secondScope=Playhub.Integrations.ApplicationIntegrationIdentity.Create("emu","rom-exact-two");
var scopedResults=new Dictionary<string,string> { [ImportedIntegrationIdentity.ResultKey(0,firstScope)!]="first result" };
Check(!scopedResults.ContainsKey(ImportedIntegrationIdentity.ResultKey(0,secondScope)!) && ImportedIntegrationIdentity.ResultKey(0,null) is null,"Unimported same-title games shared previous search results through zero shortcut ID.");
Check(ImportedIntegrationIdentity.ResultKey(4000000000,null)=="shortcut:4000000000" && ImportedIntegrationIdentity.ResultKey(0,firstScope)==ImportedIntegrationIdentity.ResultKey(42,firstScope),"Source search-result identity changed after export or lost unsigned shortcut ID.");
var exact = new VDFEntry { appid = unchecked((int)2485417537), AppName = "Outer Wilds" };
bool Match(VDFEntry entry) => entry.AppName == "Outer Wilds";
Check(ImportedShortcutIdentity.Resolve([new[] { exact }], Match) == 2485417537, "Preserve unsigned shortcut identity");
Check(ImportedShortcutIdentity.Resolve([Array.Empty<VDFEntry>()], Match) is null, "No imported shortcut");
Check(ImportedShortcutIdentity.Resolve([new[] { exact, exact }], Match) is null, "Reject duplicate exact candidates");
Check(ImportedShortcutIdentity.Resolve([new[] { exact }, new[] { new VDFEntry { appid = 123, AppName = "Outer Wilds" } }], Match) is null, "Reject conflicting profile identity");
Check(ImportedShortcutIdentity.Resolve([new[] { exact }, new[] { exact }], Match) == 2485417537, "Agreeing profiles are one identity");
Check(ImportedShortcutIdentity.Resolve([new[] { new VDFEntry { appid = 0, AppName = "Outer Wilds" } }], Match) is null, "Reject zero ID");
Check(ImportedShortcutIdentity.Resolve([new[] { new VDFEntry { appid = 753640, AppName = "Other game" }, exact }], Match) == 2485417537, "Unrelated store ID never used");
Invalid(() => new ImportIntegrationPlan(0, "Game", new Dictionary<string, string>(), ["metadata"]), "Reject unimported plugin writes");
var sourceArtwork = new Dictionary<string, string> { ["hero"] = "owned-original.jpg", ["logo"] = "owned-logo.png" };
var plan = new ImportIntegrationPlan(2485417537, "Outer Wilds", sourceArtwork, ["metadata", "metadata", "themedeck", "trailerhero", "launch-curtain", "unknown"]);
Check(plan.Preview["integrations"]!.AsArray().Count == 4, "Allowed categories only, no duplicates");
sourceArtwork["hero"] = "changed.jpg";
Check(plan.Game["steamArtwork"]!["hero"]!.GetValue<string>() == "owned-original.jpg", "Snapshot preserves original artwork");
var request = plan.Request(); plan.Game["title"] = "Changed";
Check(request.Game["title"]!.GetValue<string>() == "Outer Wilds", "Queued request is immutable snapshot");
Check(!request.Game.ContainsKey("system") && !request.Game.ContainsKey("raGameId"), "No emulator achievement route inferred for store games");
var calls = new List<(string Plugin, string Method, JsonArray Args)>();
Task<JsonNode?> Call(string plugin, string method, JsonArray args, CancellationToken ct)
{
    calls.Add((plugin, method, (JsonArray)args.DeepClone()));
    JsonNode? response = method switch
    {
        "auto_fetch_metadata" => new JsonObject { ["title"] = "Outer Wilds", ["short_description"] = "Description" },
        "search_youtube" or "search_youtube_videos" => new JsonObject { ["ok"] = true, ["results"] = new JsonArray(new JsonObject { ["id"] = "abcdefghijk", ["url"] = "https://www.youtube.com/watch?v=abcdefghijk", ["title"] = "Real result" }) },
        "get_track" => null,
        "get_local_trailer" => new JsonObject { ["ok"] = false },
        "get_game_settings" => new JsonObject { ["settings"] = new JsonObject { ["fullscreen_image_path"] = "user-owned.jpg" } },
        _ => throw new Exception("Unexpected operation " + method)
    };
    return Task.FromResult(response);
}
var service = new PostImportIntegrationService(_ => Task.FromResult(true), Call, TimeSpan.Zero, TimeSpan.Zero);
var result = await service.RunAsync(request, CancellationToken.None);
var outcomes = result["outcomes"]!.AsArray();
Check(outcomes.Any(x => x?["category"]?.ToString() == "metadata" && x["status"]?.ToString() == "completed"), "Metadata real response");
Check(outcomes.Count(x => x?["status"]?.ToString() == "selection_required") == 2, "Music/trailer require human selection");
Check(!calls.Any(x => x.Method.Contains("download") || x.Method == "save_game_settings"), "No unsolicited download or existing curtain overwrite");
Check(calls.All(x => x.Args[0]?.ToString() == "2485417537" || x.Method.StartsWith("search_") || x.Method == "get_game_settings"), "Operations address exact shortcut");
Check(!calls.Any(x => x.Method.Contains("retroachievements") || x.Method.Contains("rpcs3")), "Store import never chooses emulator provider");
var candidates = ImportIntegrationPlan.Candidates(outcomes.First(x => x?["category"]?.ToString() == "themedeck"));
candidates[0]!["title"] = "Edited preview";
Check(outcomes.First(x => x?["category"]?.ToString() == "themedeck")!["data"]!["results"]![0]!["title"]!.ToString() == "Real result", "Preview selection cannot mutate saved result");
var unavailableCalls = 0;
var unavailable = new PostImportIntegrationService(_ => Task.FromResult(false), (_, _, _, _) => { unavailableCalls++; return Task.FromResult<JsonNode?>(null); }, TimeSpan.Zero);
var missing = await unavailable.RunAsync(request, CancellationToken.None);
Check(unavailableCalls == 0 && missing["outcomes"]!.AsArray().All(x => x?["status"]?.ToString() == "unavailable"), "Unavailable Steam never reports ready or calls plugin");
var existing = new JsonObject { ["title"] = "Custom title", ["description"] = "Keep full description", ["rating"] = 90 };
var saved = new JsonObject();
var edit = new GameIntegrationEditorService((_, method, args, _) =>
{
    if (method == "get_metadata") return Task.FromResult<JsonNode?>(existing.DeepClone());
    saved = args[1]!.DeepClone().AsObject(); return Task.FromResult<JsonNode?>(saved.DeepClone());
});
await edit.MetadataAsync(new(2485417537, new JsonObject { ["short_description"] = "New short description" }), CancellationToken.None);
Check(saved["title"]!.ToString() == "Custom title" && saved["description"]!.ToString() == "Keep full description" && saved["rating"]!.GetValue<int>() == 90, "Metadata edit merges only dirty fields");
Invalid(() => GameIntegrationEditorService.ValidateCurtain(new JsonObject { ["timeout_seconds"] = 1000 }), "Native input respects plugin range");
Check(ImportIntegrationPlan.State("queued", false) != "Ready" && ImportIntegrationPlan.State("failed", false) != "Ready", "Incomplete work is never ready");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await unavailable.RunAsync(request, cancelled.Token); throw new Exception("Cancellation lost"); } catch (OperationCanceledException) { checks++; }
var editCalls = new List<string>();
var readback = new JsonObject();
var settings = new ImportIntegrationEdits((_, method, args, _) =>
{
    editCalls.Add(method);
    if (method == "get_metadata") return Task.FromResult<JsonNode?>(readback.DeepClone());
    if (method == "save_metadata") { readback = args[1]!.DeepClone().AsObject(); return Task.FromResult<JsonNode?>(new JsonObject { ["ok"] = true }); }
    if (method == "set_volume") return Task.FromResult<JsonNode?>(new JsonObject());
    if (method == "get_track") return Task.FromResult<JsonNode?>(new JsonObject { ["volume"] = .1 });
    if (method == "save_game_settings") return Task.FromResult<JsonNode?>(new JsonObject { ["ok"] = false });
    throw new Exception(method);
});
await settings.SaveAsync(2485417537, new(), new(), new(), CancellationToken.None);
Check(editCalls.Count == 0, "Opening or cancelling an unchanged draft has no writes");
await settings.SaveAsync(2485417537, new JsonObject { ["title"] = "Edited title" }, new(), new(), CancellationToken.None);
Check(editCalls.SequenceEqual(new[] { "get_metadata", "save_metadata", "get_metadata" }), "Save metadata requires real readback");
try { await settings.SaveAsync(2485417537, new(), new JsonObject { ["volume"] = .5 }, new(), CancellationToken.None); throw new Exception("Stale music readback accepted"); } catch (InvalidOperationException) { checks++; }
try { await settings.SaveAsync(2485417537, new(), new(), new JsonObject { ["enabled"] = true }, CancellationToken.None); throw new Exception("Rejected curtain save accepted"); } catch (InvalidOperationException) { checks++; }
var falselySaved = new ImportIntegrationEdits((_, _, _, _) => Task.FromResult<JsonNode?>(new JsonObject { ["title"] = "Unchanged" }));
try { await falselySaved.SaveAsync(2485417537, new JsonObject { ["title"] = "Draft" }, new(), new(), CancellationToken.None); throw new Exception("Transport success accepted without persisted draft"); } catch (InvalidOperationException) { checks++; }
using var inFlight = new CancellationTokenSource();
var waiting = new PostImportIntegrationService(_ => Task.FromResult(true), async (_, _, _, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return null; });
var pending = waiting.RunAsync(request, inFlight.Token);
inFlight.Cancel();
try { await pending; throw new Exception("Dialog cancellation left plugin operation waiting"); } catch (OperationCanceledException) { checks++; }
var fixture = Path.Combine(AppContext.BaseDirectory, "artwork-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
var imagePath = Path.Combine(fixture, "valid.png");
var smallPath = Path.Combine(fixture, "small.png");
var invalidPath = Path.Combine(fixture, "invalid.png");
await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEUlEQVR4nGP4z8DwH4QZYAwAR8oH+WdZbrcAAAAASUVORK5CYII="));
await File.WriteAllBytesAsync(smallPath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="));
await File.WriteAllTextAsync(invalidPath, "not an image");
var evaluations = new List<string>();
var hero = new PerfectHeroService((expression, _) => { evaluations.Add(expression); return Task.FromResult<JsonNode?>(new JsonObject { ["ok"] = true }); });
JsonObject Assets(string path) => new() { ["steamArtwork"] = new JsonObject { ["hero"] = path, ["logo"] = imagePath } };
try
{
    var applied = await hero.ApplyAsync(2485417537, Assets(imagePath), CancellationToken.None);
    var stableSource = ImportedIntegrationIdentity.Pc("Exact.Package!App","","");
    var beforeExport = new ImportIntegrationPlan(0,"Game",new Dictionary<string,string>(),["metadata"],stableSource);
    Check(beforeExport.AppId == 0 && beforeExport.Request().Identity == stableSource,"Native preimport source never fabricates a Steam shortcut ID");
    Check(ImportedIntegrationIdentity.Pc("Other.Package!App","","") != stableSource && ImportedIntegrationIdentity.Pc("",imagePath,"--first") != ImportedIntegrationIdentity.Pc("",imagePath,"--second"),"Different source identities and exact launch arguments keep separate app drafts");
    var imageCache = new ApplicationImageCache(Path.Combine(fixture,"owned-cache"));
    var cachedImage = await imageCache.SaveAsync(stableSource,"hero",await File.ReadAllBytesAsync(imagePath),CancellationToken.None);
    Check(File.Exists(cachedImage) && (await File.ReadAllBytesAsync(cachedImage)).SequenceEqual(await File.ReadAllBytesAsync(imagePath)),"App image cache publishes actual decoded bytes with verified readback");
    try { await imageCache.SaveAsync(stableSource,"hero",await File.ReadAllBytesAsync(smallPath),CancellationToken.None); throw new Exception("Tiny cached image accepted"); } catch(InvalidDataException) { checks++; }
    try { await imageCache.SaveAsync(stableSource,"hero",await File.ReadAllBytesAsync(invalidPath),CancellationToken.None); throw new Exception("Corrupt cached image accepted"); } catch(InvalidDataException) { checks++; }
    var corruptPixels = await File.ReadAllBytesAsync(imagePath); corruptPixels[41] = 255;
    bool refusedCorruptPixels = false;
    try { await imageCache.SaveAsync(stableSource,"hero",corruptPixels,CancellationToken.None); } catch(Exception) { refusedCorruptPixels = true; }
    Check(refusedCorruptPixels,"Valid image dimensions with corrupt compressed pixels cannot become an acquired cache file");
    Check(applied?["ok"]?.GetValue<bool>() == true && evaluations.Count == 1 && evaluations[0].Contains("2485417537"), "Actual WinRT decoder accepts valid assets and retains exact AppID");
    try { await hero.ApplyAsync(2485417537, Assets(smallPath), CancellationToken.None); throw new Exception("Tiny artwork accepted"); } catch (IOException) { checks++; }
    Check(evaluations.Count == 1, "Invalid dimensions cannot reach Steam evaluator");
    var rejected = false;
    try { await hero.ApplyAsync(2485417537, Assets(invalidPath), CancellationToken.None); } catch { rejected = true; }
    Check(rejected && evaluations.Count == 1, "Actual decoder rejects corrupt images before Steam evaluator");
    try { await hero.ApplyAsync(2485417537, Assets(imagePath), cancelled.Token); throw new Exception("Artwork cancellation lost"); } catch (OperationCanceledException) { checks++; }
    var bannerAssets = new JsonObject { ["steamArtwork"] = new JsonObject { ["banner"] = imagePath, ["logo"] = imagePath } };
    await hero.ApplyTargetAsync(2485417537, bannerAssets, "banner", CancellationToken.None);
    var bannerInputStart = evaluations.Last().IndexOf("const p=",StringComparison.Ordinal)+8;
    var bannerInputEnd = evaluations.Last().IndexOf(";const call",bannerInputStart,StringComparison.Ordinal);
    var bannerInput = JsonNode.Parse(evaluations.Last()[bannerInputStart..bannerInputEnd])!;
    Check(bannerInput["target"]!.GetValue<string>() == "grid_l" && bannerInput["width"]!.GetValue<int>() == 1926 && bannerInput["height"]!.GetValue<int>() == 900 && bannerInput["artworkType"]!.GetValue<int>() == 0, "Banner actual decoded input addresses banner geometry/type and original target");
    await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory,"info-compose-fixtures.json"),System.Text.Json.JsonSerializer.Serialize(evaluations));
}
finally
{
    string absoluteFixture = Path.GetFullPath(fixture);
    if (!absoluteFixture.StartsWith(Path.GetFullPath(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absoluteFixture).StartsWith("artwork-fixture-",StringComparison.Ordinal)) throw new IOException("Fixture cleanup left its test directory.");
    Directory.Delete(absoluteFixture,true);
}
var queried = new List<string>();
Check(!XboxGameBarStartupPolicy.HasActiveLaunchHelper<ProcessProbe>(name => { queried.Add(name); return []; }), "Idle launchers permit normal startup reset");
Check(queried.SequenceEqual(new[] { "UWPHook", "Playhub.GameSession", "Playhub.XboxSession" }), "All three real launcher names participate");
foreach (var helper in queried.ToArray())
{
    var probes = new[] { new ProcessProbe(), new ProcessProbe() };
    var reset = false;
    if (!XboxGameBarStartupPolicy.HasActiveLaunchHelper(name => name == helper ? probes : [])) reset = true;
    Check(!reset, "Active " + helper + " cannot reset Game Bar");
    Check(probes.All(probe => probe.Disposals == 1), "Active process objects disposed once for " + helper);
}
var lookupFailed = false;
try { XboxGameBarStartupPolicy.HasActiveLaunchHelper<ProcessProbe>(_ => throw new IOException("Process enumeration unavailable")); }
catch (IOException) { lookupFailed = true; }
Check(lookupFailed, "Unknown process state propagates to existing no-reset catch");
Console.WriteLine($"PASS {checks} production-linked import integration and startup checks");

// Info owns only initialized players and must tolerate a COM cleanup failure.
var mediaOrder=new List<string>();
var mediaResource=new ImportMediaResource(()=>mediaOrder.Add("pause"),()=>mediaOrder.Add("detach"),()=>mediaOrder.Add("clear"),()=>mediaOrder.Add("source"),()=>mediaOrder.Add("player"));
mediaResource.Dispose();mediaResource.Dispose();
Check(mediaOrder.SequenceEqual(new[] {"pause","detach","clear","source","player"}),"Native media cleanup released an attached player or disposed it twice.");
int detachAttempts=0,releases=0;
var delayedDetach=new ImportMediaResource(()=>{},()=>{if(++detachAttempts==1)throw new InvalidOperationException("Renderer still attached");},()=>{},()=>releases++,()=>releases++);
delayedDetach.Dispose();Check(releases==0,"Failed native detach still disposed a player bound to XAML.");
delayedDetach.Dispose();Check(detachAttempts==2 && releases==2,"Unloaded retry did not release the detached owned resources exactly once.");
var infoSession = new ImportInfoSession(); var infoToken = infoSession.Token; var cleaned = 0;
infoSession.Register(() => cleaned++); infoSession.Register(() => throw new InvalidOperationException("Disposed media COM object"));
infoSession.Dispose(); infoSession.Dispose();
Check(cleaned == 1 && infoToken.IsCancellationRequested && infoSession.IsClosed, "Close cancels work, isolates cleanup failure, and is idempotent");
infoSession.Register(() => cleaned++);
Check(cleaned == 2, "Resource arriving after close is immediately released");
var largeInfo = ImportInfoSize.ForRoot(1920, 1080); var smallInfo = ImportInfoSize.ForRoot(680, 500);
Check(largeInfo == new ImportInfoSize(1000, 600), "Large dialog has fixed generous stage across tabs");
Check(smallInfo.Width + 120 <= 680 && smallInfo.Height + 240 <= 500, "Small viewport retains native title/footer margins");
Check(ImportInfoSize.ForRoot(30, 30) == new ImportInfoSize(0, 0), "No negative dimensions on constrained roots");
var savedFields = 0; var fieldsDirty = true; var saveStates = new List<string>();
var saveQueue = new ImportInfoSaveQueue(() => { savedFields++; fieldsDirty = false; return Task.CompletedTask; }, saveStates.Add, TimeSpan.FromMilliseconds(80), () => fieldsDirty);
for (var i = 0; i < 5; i++) saveQueue.FieldsChanged();
await saveQueue.FlushAsync();
Check(savedFields == 1, "Close flushes a burst of typing exactly once");
var enteredSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var actionOrder = new List<int>();
var firstAction = saveQueue.ChangeAsync(async () => { actionOrder.Add(1); enteredSave.SetResult(); await releaseSave.Task; actionOrder.Add(2); });
await enteredSave.Task;
var secondAction = saveQueue.ChangeAsync(() => { actionOrder.Add(3); return Task.CompletedTask; });
var flushClose = saveQueue.FlushAsync();
Check(!flushClose.IsCompleted && actionOrder.SequenceEqual(new[] { 1 }), "Close waits for in-flight selection and later actions cannot overtake it");
releaseSave.SetResult(); await flushClose;
Check(actionOrder.SequenceEqual(new[] { 1, 2, 3 }), "Explicit actions save in user order");
await saveQueue.ChangeAsync(() => throw new IOException("Rejected selection")); await saveQueue.FlushAsync();
Check(saveStates.Last() == "error" && saveQueue.HasFailure, "No false Saved on close after a rejected selection");
var providerCalls = new List<(string Plugin,string Method,JsonArray Args)>();
Task<JsonNode?> Provider(string plugin,string method,JsonArray value,CancellationToken token)
{
 providerCalls.Add((plugin,method,(JsonArray)value.DeepClone()));
 return Task.FromResult<JsonNode?>(new JsonArray(new JsonObject { ["url"]="https://assets.example/hero.jpg",["thumb"]="https://assets.example/preview.jpg",["width"]=3840,["height"]=2160 }, new JsonObject { ["url"]="file:///untrusted" }));
}
var searchedArtwork = await ImportArtworkSearch.SearchAsync(Provider,"xbox","Outer Wilds","banner",true,CancellationToken.None);
Check(providerCalls.Single().Args[2]!.GetValue<string>() == "grid_l" && !providerCalls.Single().Args[3]!.GetValue<bool>(), "Banner uses actual Artworks type without cover-only square constraint");
Check(searchedArtwork.Count == 1 && searchedArtwork[0].Preview.EndsWith("preview.jpg"), "Provider thumbnails preserved and non-HTTPS selections rejected");
Invalid(() => ImportArtworkSearch.AssetType("unknown"), "Unknown category rejected");
var lcCalls = new List<(string Method,JsonArray Args)>();
Task<JsonNode?> CurtainCall(string plugin,string method,JsonArray value,CancellationToken token)
{
 lcCalls.Add((method,(JsonArray)value.DeepClone()));
 return Task.FromResult<JsonNode?>(new JsonObject { ["ok"]=true,["path"]="owned-background.jpg" });
}
await ImportCurtainSearch.SearchAsync(CurtainCall,2485417537,"Outer Wilds","xbox",CancellationToken.None);
await ImportCurtainSearch.ChooseAsync(CurtainCall,2485417537,"Outer Wilds",new JsonObject { ["image_url"]="https://store-images.s-microsoft.com/chosen.jpg",["source"]="Xbox",["resolution"]="3840x2160" },CancellationToken.None);
Check(lcCalls[0].Args[0]!["services"]![0]!.GetValue<string>() == "xbox", "Background search uses requested real provider");
Check(lcCalls[1].Method == "download_google_image" && lcCalls[1].Args[0]!["app_id"]!.GetValue<uint>() == 2485417537, "Only exact selected background is saved for exact game, no bulk preset");
Check(!lcCalls[1].Args[0]!.AsObject().ContainsKey("settings"), "Choosing a background cannot overwrite other curtain controls");
var newerFields = new JsonObject { ["title"]="New typing",["volume"]=.5 };
ImportIntegrationEdits.ClearConfirmedFields(newerFields,new JsonObject { ["title"]="Old saved typing",["volume"]=.5 });
Check(newerFields["title"]!.GetValue<string>() == "New typing" && !newerFields.ContainsKey("volume"), "Late save acknowledgement keeps newer field edits and clears only confirmed revision");
var fetchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var releaseFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var manualDuringFetch = new JsonObject();
async Task<JsonObject> DelayedRead() { fetchEntered.SetResult(); await releaseFetch.Task; return ImportIntegrationEdits.OverlayManualFields(new JsonObject { ["title"]="Fetched title",["short_description"]="Fetched description",["source"]="IGN" },manualDuringFetch); }
var lateMetadata = DelayedRead(); await fetchEntered.Task;
manualDuringFetch["title"]="Typed while fetching"; releaseFetch.SetResult();
var renderedMetadata = await lateMetadata;
Check(renderedMetadata["title"]!.GetValue<string>() == "Typed while fetching" && renderedMetadata["short_description"]!.GetValue<string>() == "Fetched description", "Delayed fetch preserves user typing while filling untouched fields");
Check(renderedMetadata["source"]!.GetValue<string>() == "IGN" && manualDuringFetch.Count == 1, "Manual overlay retains acquired provenance and does not mutate field tracking");
Check(PreparedIntegrationDelivery.Unresolved("pc:ready","Game",new JsonObject{["integrations"]=new JsonObject{["metadatadeck"]=new JsonObject{["title"]="Prepared"}}})?["detail"]?.ToString()=="shortcut_unresolved","Missing exact shortcut retains a truthful prepared delivery warning");
Check(PreparedIntegrationDelivery.Unresolved("pc:empty","Game",new JsonObject()) is null,"Missing shortcut with no prepared data produces no spurious warning");
Check(PreparedIntegrationDelivery.HasIncomplete(new JsonArray(new JsonObject{["outcomes"]=new JsonArray(new JsonObject{["delivered"]=false})})),"Nested prepared delivery failures surface discreetly");
Check(!PreparedIntegrationDelivery.HasIncomplete(new JsonArray(new JsonObject{["outcomes"]=new JsonArray(new JsonObject{["delivered"]=true})})),"Successful deliveries produce no warning");
Check(MediaDisplayName.Resolve(null,"Floral_Fury [2clUY-OsCpY].mp3")=="Floral Fury","Existing cached music has a readable fallback without the technical video ID");
Check(MediaDisplayName.Resolve("Floral Fury (Live)","ignored.mp3")=="Floral Fury (Live)" && MediaDisplayName.Resolve(null,"Floral_Fury [Live].mp3")=="Floral Fury [Live]","Actual acquisition titles and meaningful bracketed title text remain unchanged");
if (args.Length == 2 && args[0] == "--locale-audit")
{
 var infoSource = File.ReadAllText(args[1]);
 var keys = System.Text.RegularExpressions.Regex.Matches(infoSource, "\\bT\\(\"([^\"]+)\"\\)").Select(m => m.Groups[1].Value)
  .Concat(System.Text.RegularExpressions.Regex.Matches(infoSource, "ImportText\\(\"[^\"]*\", \"([^\"]*)\"\\)").Select(m => m.Groups[1].Value)).Distinct().ToArray();
 var localeMissing = LocalizationService.Languages.Where(l => l.Key != "it").ToDictionary(l => l.Key, l => keys.Where(key => !LocalizationService.HasTranslation(l.Key,key)).ToArray());
 foreach (var language in localeMissing) Check(language.Value.Length == 0, "Every Info label translated in " + language.Key + ": " + string.Join(", ",language.Value));
 Check(LocalizationService.ResolveLanguage("IT") == "it" && LocalizationService.ResolveLanguage("it-IT") == "it" && LocalizationService.ResolveLanguage("auto") == LocalizationService.ResolveLanguage(null), "Info uses production locale normalization including automatic language");
 Check(!infoSource.Contains("PrimaryButtonText") && !infoSource.Contains("CloseButtonText = ImportText(\"Cancel"), "Info has only Close and never requires Apply/Cancel");
 Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(localeMissing));
}
Console.WriteLine($"Info tests including lifecycle, autosave and provider contracts: {checks} PASS");
sealed class ProcessProbe : IDisposable
{
    internal int Disposals;
    public void Dispose() => Disposals++;
}

