using System.Text.Json.Nodes;
using Playhub.Emulation.Workbench;
using Playhub.Integrations;

string fixture = Path.Combine(Path.GetTempPath(), "playhub-consumer-" + Guid.NewGuid().ToString("N"));
string plugins = Path.Combine(fixture, "plugins"), settings = Path.Combine(fixture, "settings");
int checks = 0;
void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; }
async Task Reject(Func<Task> action, string message) { try { await action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException or System.Text.Json.JsonException or IOException) { checks++; return; } throw new Exception(message); }
async Task<string> FileAt(string folder, string file, JsonObject data) { string path = Path.Combine(settings, folder, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, data.ToJsonString()); return path; }
try
{
    Directory.CreateDirectory(plugins);
    foreach (var (folder,name) in new[] { ("Playhub Metadata","Playhub Metadata"), ("themedeck","ThemeDeck"), ("launch-curtain","Launch Curtain"), ("TrailerHero","TrailerHero") })
    { string dir = Path.Combine(plugins, folder); Directory.CreateDirectory(dir); await File.WriteAllTextAsync(Path.Combine(dir,"plugin.json"), new JsonObject { ["name"]=name,["version"]="0.0.1" }.ToJsonString()); }
    var sink = new PluginConsumerDelivery(plugins, settings);
    Check(sink.IsInstalled("metadata") && sink.IsInstalled("metadatadeck"), "Old installed versions must enable delivery.");
    await Reject(()=>sink.DeliverAsync("metadata",0,new()),"Zero shortcut accepted.");
    string metadataFile = await FileAt("Playhub Metadata","playhub_metadata.json",new JsonObject { ["account"]="keep",["metadata"]=new JsonObject { ["42"]=new JsonObject { ["title"]="Old",["foreign"]="preserve" },["99"]=new JsonObject { ["title"]="Other game" } } });
    var delivered = await sink.DeliverAsync("metadata",42,new JsonObject { ["title"]="New",["release_date"]="2024-05-01",["developers"]=new JsonArray(new JsonObject { ["name"]="Developer",["url"]="" }) });
    Check(delivered["delivered"]!.GetValue<bool>() && !delivered["runtimeSynced"]!.GetValue<bool>(),"Offline delivery misreported runtime synchronization.");
    var disk = JsonNode.Parse(await File.ReadAllTextAsync(metadataFile))!;
    Check(disk["account"]!.GetValue<string>()=="keep" && disk["metadata"]!["99"]!["title"]!.GetValue<string>()=="Other game" && disk["metadata"]!["42"]!["foreign"]!.GetValue<string>()=="preserve","Metadata overwrote unrelated fields.");
    Check(disk["metadata"]!["42"]!["release_date"]!.GetValue<long>()==1714521600,"Native ISO date did not become plugin Unix timestamp.");
    await Reject(()=>sink.DeliverAsync("metadata",42,new JsonObject { ["release_date"]="bad" }),"Invalid date accepted.");
    await Reject(()=>sink.DeliverAsync("metadata",42,new JsonObject { ["arbitrary_setting"]=1 }),"Unknown metadata injection accepted.");
    string curtainFile = await FileAt("launch-curtain","launch-curtain.json",new JsonObject { ["enabled"]=true,["show_logo"]=true,["curtain_timeout"]=23,["background_scale"]=150,["game_settle_seconds"]=4,["per_game"]=new JsonObject { ["42"]=new JsonObject { ["custom_future"]=true },["99"]=new JsonObject { ["logo_scale"]=77 } } });
    string curtainLegacy=curtainFile,legacyOriginal=await File.ReadAllTextAsync(curtainLegacy);
    await sink.DeliverAsync("launch-curtain",42,new JsonObject { ["logo_scale"]=125,["enabled"]=false });
    curtainFile=Path.Combine(fixture,"data","launch-curtain","launch-curtain.json");
    Check(File.Exists(curtainFile) && await File.ReadAllTextAsync(curtainLegacy)==legacyOriginal,"LC delivery did not publish its primary data file or overwrote legacy settings.");
    disk=JsonNode.Parse(await File.ReadAllTextAsync(curtainFile))!;
    Check(disk["enabled"]!.GetValue<bool>() && disk["per_game"]!["42"]!["custom_future"]!.GetValue<bool>() && disk["per_game"]!["99"]!["logo_scale"]!.GetValue<int>()==77,"Curtain fallback reset globals or other games.");
    await File.WriteAllTextAsync(curtainLegacy,new JsonObject { ["show_logo"]=false,["per_game"]=new JsonObject { ["42"]=new JsonObject { ["logo_scale"]=90 } } }.ToJsonString());
    var primaryRaw=(await sink.ReadAsync("launch-curtain",42))!;
    var resolved=await sink.ResolveCurtainAsync(primaryRaw);
    Check(primaryRaw["logo_scale"]!.GetValue<int>()==125 && !resolved["enabled"]!.GetValue<bool>() && resolved["show_logo"]!.GetValue<bool>(),"Legacy LC settings took priority over actual primary data settings.");
    Check(resolved["timeout_seconds"]!.GetValue<int>()==23 && resolved["background_scale"]!.GetValue<int>()==150 && resolved["exit_delay_seconds"]!.GetValue<int>()==4,"LC effective settings did not inherit the actual curtain_timeout/background/game_settle schema.");
    Check((await sink.ResolveCurtainAsync(new()))["enabled"]!.GetValue<bool>() && (await sink.ResolveCurtainAsync(new()))["logo_position_x"]!.GetValue<int>()==50,"LC absent per-game enabled/logo-position differs from native resolved defaults.");
    await Reject(()=>sink.DeliverAsync("launchcurtain",42,new JsonObject { ["logo_scale"]=900 }),"Unbounded curtain scale accepted.");
    string audio=Path.Combine(fixture,"track.mp3"); await File.WriteAllBytesAsync(audio,[1,2,3,4]);
    string trackFile=await FileAt("themedeck","tracks.json",new JsonObject { ["__global__"]=new JsonObject { ["volume"]=0.25 },["42"]=new JsonObject { ["volume"]=0.4,["start_offset"]=3,["loop"]=false,["foreign"]="keep" },["99"]=new JsonObject { ["path"]="other" } });
    await sink.DeliverAsync("themedeck",42,new JsonObject { ["path"]=audio,["normalized"]=true });
    disk=JsonNode.Parse(await File.ReadAllTextAsync(trackFile))!;
    Check(disk["42"]!["path"]!.GetValue<string>()==audio && disk["42"]!["volume"]!.GetValue<double>()==0.4 && !disk["42"]!["loop"]!.GetValue<bool>() && disk["42"]!["foreign"]!.GetValue<string>()=="keep","Track selection lost playback settings.");
    Check(disk["__global__"]!["volume"]!.GetValue<double>()==0.25 && disk["99"]!["path"]!.GetValue<string>()=="other","Track selection altered foreign assignments.");
    int destructiveSetters=0;
    var cachedConsumer=new PluginConsumerDelivery(plugins,settings,async (plugin,method,args,ct)=>
    {
        if(method.StartsWith("set_",StringComparison.Ordinal) || method is "save_game_settings" or "remove_track")
        {
            destructiveSetters++;
            // Actual cached setter contracts replace the whole file from in-memory state.
            await File.WriteAllTextAsync(plugin=="ThemeDeck" ? trackFile : curtainFile,"{}",ct);
        }
        return plugin=="Launch Curtain" ? new JsonObject { ["settings"]=new JsonObject { ["logo_scale"]=50 } } : new JsonObject { ["path"]=audio,["volume"]=0.4 };
    });
    var cachedTrack=await cachedConsumer.DeliverAsync("themedeck",42,new JsonObject { ["volume"]=0.8 });
    var cachedCurtain=await cachedConsumer.DeliverAsync("launch-curtain",42,new JsonObject { ["logo_scale"]=140 });
    Check(destructiveSetters==0 && !cachedTrack["runtimeSynced"]!.GetValue<bool>() && !cachedCurtain["runtimeSynced"]!.GetValue<bool>(),"Cached destructive setter was used or stale state claimed synced.");
    Check((await sink.ReadAsync("themedeck",42))!["foreign"]!.GetValue<string>()=="keep" && JsonNode.Parse(await File.ReadAllTextAsync(curtainFile))!["per_game"]!["99"]!["logo_scale"]!.GetValue<int>()==77,"Optional runtime observation discarded foreign persisted data.");
    await sink.RemoveTrackAsync(42); Check(await sink.ReadAsync("themedeck",42) is null && File.Exists(audio),"Remove assignment deleted source or failed.");
    string video=Path.Combine(fixture,"acquired.mp4"); await File.WriteAllBytesAsync(video,[5,6,7,8]);
    string trailerFile=await FileAt("trailerhero","trailer-library.json",new JsonObject { ["version"]=1,["futureRoot"]="keep",["assignments"]=new JsonObject { ["99"]=new JsonObject { ["video"]="other.mp4" } } });
    delivered=await sink.DeliverAsync("trailerhero",42,new JsonObject { ["path"]=video,["title"]="Game",["videoId"]="abcdefghijk" });
    disk=JsonNode.Parse(await File.ReadAllTextAsync(trailerFile))!; string copied=Path.Combine(settings,"trailerhero","trailers",disk["assignments"]!["42"]!["video"]!.GetValue<string>());
    Check(File.Exists(copied) && (await File.ReadAllBytesAsync(copied)).SequenceEqual(await File.ReadAllBytesAsync(video)),"Trailer consumer did not copy actual acquired file.");
    Check(disk["futureRoot"]!.GetValue<string>()=="keep" && disk["assignments"]!["99"]!["video"]!.GetValue<string>()=="other.mp4","Trailer library foreign state lost.");
    Check(delivered["data"]!["sha256"]!.GetValue<string>().Length==64 && delivered["data"]!["videoUrl"] is null,"Offline trailer fabricated runtime URL or hash.");
    string before=await File.ReadAllTextAsync(metadataFile); await File.WriteAllTextAsync(metadataFile,"{broken");
    await Reject(()=>sink.DeliverAsync("metadata",42,new JsonObject { ["title"]="overwrite" }),"Corrupt plugin settings overwritten.");
    Check(await File.ReadAllTextAsync(metadataFile)=="{broken","Corrupt settings not preserved."); await File.WriteAllTextAsync(metadataFile,before);
    string missing=Path.Combine(fixture,"absent"); var absent=new PluginConsumerDelivery(missing,settings);
    Check(!(await absent.DeliverAsync("metadata",42,new JsonObject { ["title"]="No" }))["delivered"]!.GetValue<bool>() && await File.ReadAllTextAsync(metadataFile)==before,"Missing plugin delivered or changed live contract.");
    var runtimeData=new Dictionary<string,JsonObject>();
    var online=new PluginConsumerDelivery(plugins,settings,(plugin,method,args,ct)=> { if(method=="save_metadata") runtimeData["metadata"]=(JsonObject)args[1]!.DeepClone(); return Task.FromResult<JsonNode?>(method=="get_metadata" ? runtimeData["metadata"].DeepClone() : null); });
    Check((await online.DeliverAsync("metadata",43,new JsonObject { ["title"]="Synced" }))["runtimeSynced"]!.GetValue<bool>(),"Matching actual getter not recognized.");
    var stale=new PluginConsumerDelivery(plugins,settings,(_,_,_,_)=>Task.FromResult<JsonNode?>(new JsonObject { ["title"]="Stale" }));
    delivered=await stale.DeliverAsync("metadata",42,new JsonObject { ["title"]="Newest" });
    Check(delivered["delivered"]!.GetValue<bool>() && !delivered["runtimeSynced"]!.GetValue<bool>(),"Stale runtime readback reported synchronized.");
    var throwing=new PluginConsumerDelivery(plugins,settings,(_,_,_,_)=>throw new IOException("Offline"));
    Check((await throwing.DeliverAsync("metadata",42,new JsonObject { ["title"]="Offline again" }))["delivered"]!.GetValue<bool>(),"Backend failure blocked persistent fallback.");
    await File.WriteAllTextAsync(Path.Combine(plugins,"Playhub Metadata","plugin.json"),"{\"name\":\"Another plugin\",\"version\":\"99\"}");
    Check(!sink.IsInstalled("metadata"),"Folder name accepted without verified plugin identity.");
    Check(ApplicationIntegrationCoordinator.StorageCategory("metadata")=="metadatadeck" && ApplicationIntegrationCoordinator.StorageCategory("artworks")=="artwork" && ApplicationIntegrationCoordinator.StorageCategory("launch-curtain")=="launchcurtain","UI categories are not mapped to storage explicitly.");
    await File.WriteAllTextAsync(Path.Combine(plugins,"Playhub Metadata","plugin.json"),"{\"name\":\"Playhub Metadata\",\"version\":\"0.0.1\"}");
    string stable=ApplicationIntegrationIdentity.Create("pc","xbox:exact-package!App");
    var ownStore=new ApplicationIntegrationDataStore(Path.Combine(fixture,"drafts"));
    int toolCalls=0;
    Task<IntegrationToolResult> Runner(IntegrationToolCommand command,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); toolCalls++;
        if(command.Tool=="ffprobe.exe") return Task.FromResult(new IntegrationToolResult(0,"{\"format\":{\"duration\":\"30.5\"},\"streams\":[{\"codec_type\":\"audio\",\"codec_name\":\"aac\"},{\"codec_type\":\"video\",\"codec_name\":\"h264\",\"width\":1920,\"height\":1080}]}"));
        string path=command.Tool=="yt-dlp.exe" ? Path.Combine(command.WorkingDirectory,command.Arguments.Contains("--extract-audio") ? "payload.m4a":"payload.mp4") : command.Arguments[^1];
        File.WriteAllBytes(path,[0,0,0,20,102,116,121,112,105,115,111,109,0,0,0,0,1,2,3,4]);
        return Task.FromResult(new IntegrationToolResult(0,"fixture tool output"));
    }
    var media=new ApplicationMediaAcquisition(Path.Combine(fixture,"media"),name=>Path.Combine(fixture,"tools",name),Runner);
    await using(var coordinator=new ApplicationIntegrationCoordinator(stable,0,ownStore,media,sink,
        (identity,title,ct)=>Task.FromResult(new JsonObject { ["status"]="completed",["identity"]=identity,["metadata"]=new JsonObject { ["title"]=title,["source"]="IGN",["release_date"]="2024-05-01" } })))
    {
        var draft=await coordinator.CallAsync("Playhub Metadata","auto_fetch_metadata",new JsonArray(0,"Exact game"),CancellationToken.None);
        Check(draft!["title"]!.GetValue<string>()=="Exact game" && (await ownStore.ReadCategoryAsync(stable,"metadatadeck"))["source"]!.GetValue<string>()=="IGN","Preimport fetch did not persist autonomous metadata draft.");
        Check(await ownStore.ResolveSteamAppIdAsync(42) is null && !coordinator.LastDelivery!["delivered"]!.GetValue<bool>(),"Preimport draft invented Steam ownership or delivery.");
        var selection=await coordinator.CallAsync("TrailerHero","select_streaming_trailer",new JsonArray(0,"abcdefghijk"),CancellationToken.None);
        Check(selection!["selectionSaved"]!.GetValue<bool>() && !selection["downloaded"]!.GetValue<bool>() && !selection["delivered"]!.GetValue<bool>(),"Streaming draft falsely claimed download or runtime delivery.");
        Check((await coordinator.CallAsync("TrailerHero","get_streaming_trailer",new JsonArray(0),CancellationToken.None))!["videoId"]!.GetValue<string>()=="abcdefghijk","Streaming selection was not persisted before export.");
        await Reject(()=>coordinator.CallAsync("Playhub Metadata","get_metadata",new JsonArray(99),CancellationToken.None),"Coordinator accepted unrelated app request.");
        var job=await coordinator.CallAsync("ThemeDeck","start_game_download",new JsonArray(0,"https://www.youtube.com/watch?v=abcdefghijk",false,false),CancellationToken.None);
        JsonNode? observed=null;
        for(int n=0;n<100;n++) { observed=await coordinator.CallAsync("ThemeDeck","get_discover_download_progress",new JsonArray(job!["jobId"]!.GetValue<string>()),CancellationToken.None); if(observed!["status"]!.GetValue<string>() is "done" or "failed") break; await Task.Delay(10); }
        Check(observed!["status"]!.GetValue<string>()=="done" && observed["acquired"]!.GetValue<bool>() && !observed["delivered"]!.GetValue<bool>() && toolCalls>0,"Preimport media acquisition relied on consumer or falsely reported delivery.");
        var track=await coordinator.CallAsync("ThemeDeck","get_track",new JsonArray(0),CancellationToken.None);
        Check(File.Exists(track!["path"]!.GetValue<string>()) && await ownStore.ResolveSteamAppIdAsync(42) is null,"Draft media did not use physical app artifact or invented binding.");
        job=await coordinator.CallAsync("TrailerHero","start_trailer_download",new JsonArray(0,"youtube","abcdefghijk",1080,"Selected trailer"),CancellationToken.None);
        for(int n=0;n<100;n++) { observed=await coordinator.CallAsync("TrailerHero","get_trailer_job",new JsonArray(job!["jobId"]!.GetValue<string>()),CancellationToken.None); if(observed!["status"]!.GetValue<string>() is "done" or "failed") break; await Task.Delay(10); }
        var verifiedTrailer=await ownStore.ReadCategoryAsync(stable,"trailerhero");
        Check(verifiedTrailer["actualHeight"]!.GetValue<int>()==1080 && verifiedTrailer["duration"]!.GetValue<double>()==30.5 && verifiedTrailer["preferredSource"]!.ToString()=="local","Actual verified trailer metrics or local preference were lost between acquisition and stored draft.");
    }
    var mediaAfter=new ApplicationMediaAcquisition(Path.Combine(fixture,"media-after"),name=>Path.Combine(fixture,"tools",name),Runner);
    await using(var coordinator=new ApplicationIntegrationCoordinator(stable,42,ownStore,mediaAfter,sink,(_,_,_)=>throw new Exception("Unexpected acquisition")))
    {
        var prepared=await coordinator.DeliverPreparedAsync(["metadata","themedeck"]);
        Check(prepared.Count==2 && (await sink.ReadAsync("metadata",42))!["description"]?.ToString()==(await ownStore.ReadCategoryAsync(stable,"metadatadeck"))["description"]?.ToString() && (await sink.ReadAsync("metadata",42))!["source"]!.ToString()=="IGN","Prepared export delivery did not publish the complete existing draft.");
        File.Delete(Path.Combine(plugins,"Playhub Metadata","plugin.json"));
        prepared=await coordinator.DeliverPreparedAsync(["metadata"]);
        Check(prepared[0]!["detail"]!.ToString()=="plugin_not_installed","Export delivery reused stale installed state after plugin removal.");
        await File.WriteAllTextAsync(Path.Combine(plugins,"Playhub Metadata","plugin.json"),"{\"name\":\"Playhub Metadata\",\"version\":\"0.0.1\"}");
        await coordinator.CallAsync("Playhub Metadata","save_metadata",new JsonArray(42,new JsonObject { ["title"]="Exported draft" }),CancellationToken.None);
        Check(await ownStore.ResolveSteamAppIdAsync(42)==stable && coordinator.LastDelivery!["delivered"]!.GetValue<bool>() && (await sink.ReadAsync("metadata",42))!["title"]!.GetValue<string>()=="Exported draft","Real export binding did not deliver exact stored identity.");
        await coordinator.CallAsync("ThemeDeck","set_volume",new JsonArray(42,0.7),CancellationToken.None);
        Check((await ownStore.ReadCategoryAsync(stable,"themedeck"))["volume"]!.GetValue<double>()==0.7,"Music edits were not saved independently of runtime.");
    }
    var legacyStore=new ApplicationIntegrationDataStore(Path.Combine(fixture,"fresh-imported-drafts"));
    var legacyMedia=new ApplicationMediaAcquisition(Path.Combine(fixture,"fresh-media"),name=>Path.Combine(fixture,"tools",name),Runner);
    await using(var coordinator=new ApplicationIntegrationCoordinator(stable,42,legacyStore,legacyMedia,sink,(_,_,_)=>throw new Exception("Unexpected acquisition")))
    {
        var beforeTrack=await sink.ReadAsync("themedeck",42);
        await coordinator.CallAsync("ThemeDeck","set_volume",new JsonArray(42,0.3),CancellationToken.None);
        var actual=await coordinator.CallAsync("ThemeDeck","get_track",new JsonArray(42),CancellationToken.None);
        Check(actual?["path"]?.GetValue<string>()==beforeTrack!["path"]!.GetValue<string>() && actual["volume"]!.GetValue<double>()==0.3,"First partial edit hid an existing imported track.");
        var actualTrailer=await coordinator.CallAsync("TrailerHero","get_local_trailer",new JsonArray(42),CancellationToken.None);
        Check(actualTrailer!["assigned"]!.GetValue<bool>() && File.Exists(actualTrailer["path"]!.GetValue<string>()) && Path.GetFullPath(actualTrailer["path"]!.GetValue<string>())==Path.GetFullPath(copied),"Existing imported trailer was falsely unassigned or did not resolve verified media path.");
        var consumerTitle=(await sink.ReadAsync("metadata",42))!["title"]!.GetValue<string>();
        await legacyStore.PatchAsync(stable,"metadatadeck",new JsonObject { ["description"]="Manual draft" });
        var mergedMetadata=await coordinator.CallAsync("Playhub Metadata","get_metadata",new JsonArray(42),CancellationToken.None);
        Check(mergedMetadata!["description"]!.GetValue<string>()=="Manual draft" && mergedMetadata["title"]!.GetValue<string>()==consumerTitle,"Partial own metadata hid untouched existing consumer fields.");
        var job=await coordinator.CallAsync("ThemeDeck","start_game_download",new JsonArray(42,"abcdefghijk",false,true,"Actual selected theme"),CancellationToken.None);
        JsonNode? observed=null;
        for(int n=0;n<100;n++) { observed=await coordinator.CallAsync("ThemeDeck","get_discover_download_progress",new JsonArray(job!["jobId"]!.GetValue<string>()),CancellationToken.None); if(observed!["status"]!.GetValue<string>() is "done" or "failed") break; await Task.Delay(10); }
        var replacement=await coordinator.CallAsync("ThemeDeck","get_track",new JsonArray(42),CancellationToken.None);
        Check(observed!["acquired"]!.GetValue<bool>() && observed["delivered"]!.GetValue<bool>() && replacement!["volume"]!.GetValue<double>()==0.3 && replacement["upmixConsumerOption"]!.GetValue<bool>() && replacement["title"]!.ToString()=="Actual selected theme","Replacement delivery lost existing track settings/title, rejected foreign imported fields, or dropped requested consumer upmix option.");
    }
    disk=JsonNode.Parse(await File.ReadAllTextAsync(trailerFile))!; disk["assignments"]!["42"]!["video"]="../../escape.mp4"; await File.WriteAllTextAsync(trailerFile,disk.ToJsonString());
    await Reject(()=>sink.ReadTrailerPathAsync(42),"Trailer relative traversal accepted.");
    await Task.WhenAll(sink.DeliverAsync("metadata",123,new JsonObject { ["title"]="First concurrent" }),new PluginConsumerDelivery(plugins,settings).DeliverAsync("metadata",124,new JsonObject { ["title"]="Second concurrent" }));
    Check((await sink.ReadAsync("metadata",123))!["title"]!.GetValue<string>()=="First concurrent" && (await sink.ReadAsync("metadata",124))!["title"]!.GetValue<string>()=="Second concurrent","Concurrent app publishers lost another exact game entry.");
    var batch=await Playhub.Importing.PreparedIntegrationDelivery.RunAsync(new[] { 125,126 },async (id,ct)=>
        id==125 ? await sink.DeliverAsync("themedeck",125,new JsonObject { ["path"]=Path.Combine(fixture,"missing.m4a") },ct)
        : await sink.DeliverAsync("metadata",126,new JsonObject { ["title"]="Successful import survives" },ct),id=>"Game "+id);
    Check(batch.Count==2 && batch[0]!["delivered"]!.GetValue<bool>()==false && batch[0]!["detail"]!.ToString()=="prepared_delivery_failed" && batch[1]!["delivered"]!.GetValue<bool>() && (await sink.ReadAsync("metadata",126))!["title"]!.ToString()=="Successful import survives","One prepared delivery failure aborted later successful imported games or falsely reported delivery.");
    using(var cancelled=new CancellationTokenSource())
    {
        cancelled.Cancel();
        try { await Playhub.Importing.PreparedIntegrationDelivery.RunAsync(new[] { 127 },(_,ct)=>throw new Exception("Cancelled batch reached delivery"),_=>"Game",cancelled.Token); throw new Exception("Explicit import cancellation swallowed"); }
        catch(OperationCanceledException) { checks++; }
    }
    int mediaFiles=Directory.EnumerateFiles(Path.Combine(settings,"trailerhero","trailers"),"*",SearchOption.AllDirectories).Count();
    disk["assignments"]=new JsonArray(); await File.WriteAllTextAsync(trailerFile,disk.ToJsonString());
    await Reject(()=>sink.DeliverAsync("trailerhero",42,new JsonObject { ["path"]=video }),"Incompatible trailer map overwritten.");
    Check(Directory.EnumerateFiles(Path.Combine(settings,"trailerhero","trailers"),"*",SearchOption.AllDirectories).Count()==mediaFiles,"Rejected library created orphaned copied media.");
    disk["assignments"]=new JsonObject(); disk["version"]=2; await File.WriteAllTextAsync(trailerFile,disk.ToJsonString());
    await Reject(()=>sink.DeliverAsync("trailerhero",42,new JsonObject { ["path"]=video }),"Unrecognized trailer file contract accepted as compatible.");
    Console.WriteLine($"PASS {checks} production-linked consumer checks; only temporary fixture directories used.");
}
finally { if(Directory.Exists(fixture)) Directory.Delete(fixture,true); }
