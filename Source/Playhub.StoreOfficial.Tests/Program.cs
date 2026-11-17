// Synthetic manifests, fake account responses and inert activation delegates only.
// No launcher, installer, account service, registry mutation or game is started.
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Playhub.GameSession;
using Playhub.Shared;
using Playhub.StoreLibrary;

int checks = 0;
string root = Path.Combine(Path.GetTempPath(), "Playhub-Official-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
void Refuses(Action action, string name)
{ try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception(name); }
async Task Code(Func<Task> action, string code)
{ try { await action(); } catch (StoreOperationException error) when(error.Code == code) { checks++; return; } throw new Exception("expected:" + code); }
string FileAt(string path, string value = "synthetic, never executable")
{ Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); return path; }
string Folder(string name) { string path = Path.Combine(root,name); Directory.CreateDirectory(path); return path; }
var caps = new StoreCapabilities(StoreInstallKind.Direct,true,true,true,true,true);
string EpicJson(string id, string folder) => JsonSerializer.Serialize(new {
    AppName=id,CatalogNamespace="test-space",CatalogItemId="catalog-"+id,
    InstallLocation=folder,LaunchExecutable="bin/game.exe",bIsApplication=true,bIsIncompleteInstall=false,AppCategories=new[]{"games"}
});
string GogJson(string id) => JsonSerializer.Serialize(new {
    gameId=id,rootGameId=id,playTasks=new[]{new {isPrimary=true,type="FileTask",path="bin/game.exe",workingDir="bin",arguments="--save \"my saves\" --mode test"}}
});
StoreOfficialInstallations EpicInventory(string directory) => new(epicDirectories:()=>[directory]);
StoreOfficialInstallations GogInventory(params StoreOfficialInstallations.GogRegistration[] registrations) => new(gogRegistrations:()=>registrations);
StoreCliProvider Provider(StoreKind store,string name,IStoreOfficialInstallations official,string ownedJson)
{
    var state=Folder("state-"+name);
    var provider=new StoreCliProvider(store,Folder("no-cli"),state,Path.Combine(root,"never-started-host.exe"),
        new HttpClient(new FakeHttp(ownedJson)),(_,_,_,_)=>Task.FromResult((0,ownedJson))) {OfficialInstallations=official};
    // Supply synthetic credentials and an inert query seam. Do not bypass the actual
    // List/Resolve/Prepare/identity methods under test or execute any bundled CLI.
    typeof(StoreCliProvider).GetField("<Capabilities>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(provider,caps);
    string auth=store==StoreKind.Epic?"{\"account_id\":\"current-account\",\"access_token\":\"synthetic\"}":
        JsonSerializer.Serialize(new Dictionary<string,object>{{"46899977096215655",new{user_id="current-account",access_token="synthetic",loginTime=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),expires_in=3600}}});
    StoreCredentialVault.Write(Path.Combine(state,store.ToString().ToLowerInvariant(),"credentials.dpapi"),auth);
    return provider;
}
string OwnedEpic(string id,string space="test-space",string? item=null) => JsonSerializer.Serialize(new[]{new{
    app_name=id,app_title="Same title is not identity",asset_infos=new{Windows=new{}},metadata=new{@namespace=space,id=item??"catalog-"+id}
}});

try
{
    string epicRoot=Folder("epic-installed"), epicDir=Folder("epic-manifests");
    string epicExe=FileAt(Path.Combine(epicRoot,"bin","game.exe"));
    string epicItem=FileAt(Path.Combine(epicDir,"game-a.item"),EpicJson("game-a",epicRoot));
    var epic=EpicInventory(epicDir);
    var observed=epic.Read(StoreKind.Epic,default);
    Check(observed.IsComplete&&observed.Games.Count==1,"official_epic_manifest_observed");
    Check(observed.Find("game-a") is {EpicNamespace:"test-space",EpicCatalogItemId:"catalog-game-a"} native&&native.Executable==epicExe,"exact_epic_product_tuple");
    Check(observed.Find("same-title-but-another-id")==null,"never_match_title");
    Check(!File.Exists(Path.Combine(epicRoot,".playhub-install-owner")),"read_only_no_owner_marker");
    foreach(var (field,value) in new (string,JsonNode?)[]{("bIsIncompleteInstall",JsonValue.Create(true)),("bIsApplication",JsonValue.Create(false)),("AppCategories",new JsonArray("dlc")),("CatalogNamespace",JsonValue.Create("ue")),("LaunchExecutable",JsonValue.Create("../../foreign.exe")),("LaunchExecutable",JsonValue.Create("bin/missing.exe"))})
    {
        var json=JsonNode.Parse(EpicJson("game-a",epicRoot))!.AsObject();json[field]=value;
        File.WriteAllText(epicItem,json.ToJsonString());
        Check(epic.Read(StoreKind.Epic,default).Find("game-a")==null,"epic_rejects_"+field+":"+value);
    }
    File.WriteAllText(epicItem,EpicJson("game-a",epicRoot));
    string duplicate=FileAt(Path.Combine(epicDir,"duplicate.item"),EpicJson("game-a",epicRoot));
    Check(epic.Read(StoreKind.Epic,default).Find("game-a")==null,"duplicate_epic_manifests_ambiguous");
    File.Delete(duplicate);
    var unknownEpic=new StoreOfficialInstallations(epicDirectories:()=>throw new UnauthorizedAccessException());
    Check(!unknownEpic.Read(StoreKind.Epic,default).IsComplete,"epic_io_unknown_not_absent");
    await Code(()=>Task.FromResult(unknownEpic.Read(StoreKind.Epic,default).Find("game-a")),"installation_inventory_unavailable");
    var boundedEpic=new StoreOfficialInstallations(epicDirectories:()=>[epicDir],enumerate:(_,_)=>Enumerable.Range(0,513).Select(i=>"x"+i));
    Check(!boundedEpic.Read(StoreKind.Epic,default).IsComplete,"epic_enumeration_bounded");
    using(var canceled=new CancellationTokenSource())
    { canceled.Cancel();try{epic.Read(StoreKind.Epic,canceled.Token);throw new Exception("cancel_not_observed");}catch(OperationCanceledException){checks++;} }

    // Keep the existing importer's narrowly verified Fortnite InstalledItems-v4
    // fallback. An authoritative .item with this install ID always wins.
    string egiDir=Folder("epic-installeditems"),fnRoot=Folder("fortnite"),fnId=Guid.NewGuid().ToString("N");
    FileAt(Path.Combine(fnRoot,"bin","game.exe"));
    string binary=FileAt(Path.Combine(fnRoot,".egstore",fnId+".manifest"));
    var magic=new byte[41];System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(magic,0x44BEC00C);File.WriteAllBytes(binary,magic);
    string egi=FileAt(Path.Combine(egiDir,fnId+".egi"),JsonSerializer.Serialize(new{v4=new{
        installationId=fnId,sandboxId="fn",itemId="fn-item",artifactId="Fortnite",state="Installed",platform="Windows",stagedVersion="",pendingTags=Array.Empty<string>(),
        dir=fnRoot,metaDir=Path.Combine(fnRoot,".egstore"),manifestPath=binary,
        manifestData=new{version="fixture-v1",launchCommand="",buildSize=1,launchExe="bin/game.exe"},pendingManifestData=new{version=""}
    }}));
    var installedItems=new StoreOfficialInstallations(epicDirectories:()=>[epicDir],epicInstalledItems:()=>egiDir);
    Check(installedItems.Read(StoreKind.Epic,default).Find("Fortnite") is {EpicNamespace:"fn"},"verified_fortnite_installeditems_fallback");
    string preferred=FileAt(Path.Combine(epicDir,fnId+".item"),"{}");
    Check(installedItems.Read(StoreKind.Epic,default).Find("Fortnite")==null,"authoritative_item_shadows_installeditems_even_if_incomplete");
    File.Delete(preferred);File.WriteAllBytes(binary,new byte[41]);
    Check(installedItems.Read(StoreKind.Epic,default).Find("Fortnite")==null,"installeditems_binary_manifest_magic_required");

    string gogRoot=Folder("gog-installed"),gogExe=FileAt(Path.Combine(gogRoot,"bin","game.exe"));
    string info=FileAt(Path.Combine(gogRoot,"goggame-123.info"),GogJson("123"));
    var registration=new StoreOfficialInstallations.GogRegistration("123",gogRoot);
    var gog=GogInventory(registration,registration);
    Check(gog.Read(StoreKind.Gog,default).Games.Count==1,"duplicate_gog_registry_views_collapse");
    Check(gog.Read(StoreKind.Gog,default).Find("123") is {GameArguments:"--save \"my saves\" --mode test"} g&&g.WorkingDirectory==Path.Combine(gogRoot,"bin"),"gog_exact_arguments_working_directory");
    foreach(var mutate in new Action<JsonObject>[]{j=>j["gameId"]="456",j=>j["rootGameId"]="456",j=>j["playTasks"]!.AsArray().Add(j["playTasks"]![0]!.DeepClone()),j=>j["playTasks"]![0]!["path"]="../outside.exe",j=>j["playTasks"]![0]!["path"]="bin/missing.exe",j=>j["playTasks"]![0]!["workingDir"]="../../",j=>j["playTasks"]![0]!["arguments"]="bad\narg",j=>j["playTasks"]![0]!["type"]="URLTask"})
    {
        var json=JsonNode.Parse(GogJson("123"))!.AsObject();mutate(json);File.WriteAllText(info,json.ToJsonString());
        Check(gog.Read(StoreKind.Gog,default).Find("123")==null,"gog_rejects_malformed_primary_identity");
    }
    File.WriteAllText(info,GogJson("123"));
    var numeric=JsonNode.Parse(GogJson("123"))!;numeric["playTasks"]![0]!["type"]=0;File.WriteAllText(info,numeric.ToJsonString());
    Check(gog.Read(StoreKind.Gog,default).Find("123")!=null,"gog_numeric_file_task");
    File.WriteAllText(info,GogJson("123"));
    string secondGog=Folder("gog-ambiguous");FileAt(Path.Combine(secondGog,"bin","game.exe"));FileAt(Path.Combine(secondGog,"goggame-123.info"),GogJson("123"));
    Check(GogInventory(registration,new("123",secondGog)).Read(StoreKind.Gog,default).Find("123")==null,"gog_different_roots_ambiguous");
    var unknownGog=new StoreOfficialInstallations(gogRegistrations:()=>[registration],read:_=>throw new IOException("synthetic sharing failure"));
    Check(!unknownGog.Read(StoreKind.Gog,default).IsComplete,"gog_read_failure_unknown");
    var boundGog=new StoreOfficialInstallations(gogRegistrations:()=>Enumerable.Repeat(registration,8193));
    Check(!boundGog.Read(StoreKind.Gog,default).IsComplete,"gog_registry_enumeration_bounded");
    Check(GogInventory(new StoreOfficialInstallations.GogRegistration("../123",gogRoot)).Read(StoreKind.Gog,default).Games.Count==0,"gog_id_must_be_product_id");

    var ep=Provider(StoreKind.Epic,"epic",epic,OwnedEpic("game-a"));
    var owned=await ep.ListAsync(default);
    Check(owned.Single() is {Launchable:true} e&&e.InstallPath==epicRoot,"owned_epic_official_install_visible");
    foreach(var (space,item) in new[]{("foreign-space","catalog-game-a"),("test-space","foreign-item")})
    {
        var foreign=Provider(StoreKind.Epic,"foreign-"+space+item,epic,OwnedEpic("game-a",space,item));
        Check((await foreign.ListAsync(default)).Single().InstallPath==null,"foreign_owned_tuple_not_adopted");
    }
    var other=Provider(StoreKind.Epic,"other",epic,OwnedEpic("unowned-other"));
    Check((await other.ListAsync(default)).Single().InstallPath==null,"os_games_do_not_expand_account_entitlements");
    var epicLaunch=await ep.ResolveLaunchAsync("game-a",epicRoot,default);
    Check(epicLaunch?.Executable==epicExe&&epicLaunch.GameExecutablePath==epicExe,"official_epic_verified_descriptor");
    Check(await ep.ResolveLaunchAsync("game-a",secondGog,default)==null,"official_path_cannot_be_client_substituted");
    var epicPlan=await ep.PrepareLaunchAsync("game-a",epicRoot,default);
    Check(epicPlan.EpicNamespace=="test-space"&&epicPlan.EpicCatalogItemId=="catalog-game-a"&&epicPlan.EpicAppName=="game-a","official_epic_typed_plan");
    Check(epicPlan.GameExecutable==epicExe&&epicPlan.Arguments.Count==0&&epicPlan.Environment.Count==0,"epic_no_cli_auth_or_nested_wrapper");
    var libraryGame=new StoreLibraryGame(new(StoreKind.Epic,"game-a"),"Title ignored",7,caps,true,true,epicRoot);
    string wrapper=Path.Combine(root,"GameSession.exe");
    var identities=await ep.ReadSteamNativeIdentitiesAsync([libraryGame],wrapper,default);
    var identity=identities.Single().Value;
    Check(identity.NativeLaunchKind=="official"&&identity.AccountRevision==7&&identity.GameExecutablePath==epicExe,"official_identity_account_revision_and_executable");
    Check(identity.RuntimeExecutable==wrapper&&identity.EpicNamespace=="test-space"&&identity.GameArguments=="","official_epic_identity_preserves_native_route_fields");
    Check((await ep.ReadSteamNativeIdentitiesAsync([libraryGame with{IsInstalled=false}],wrapper,default)).Count==0,"uninstalled_not_candidate");
    Check((await ep.ReadSteamNativeIdentitiesAsync([libraryGame with{InstallPath=secondGog}],wrapper,default)).Count==0,"mismatched_installed_path_not_candidate");
    Check(!await ep.CanUninstallAsync("game-a",epicRoot,Guid.NewGuid(),default),"external_epic_never_fabricates_uninstall_ownership");
    Check(!File.Exists(Path.Combine(root,"state-epic","epic","native","installed.json")),"official_epic_no_private_install_record");

    string gogOwned=JsonSerializer.Serialize(new{totalPages=1,products=new[]{new{id=123,title="Owned GOG",worksOn=new{Windows=true},image="//images.gog-statics.com/"+new string('a',32)}}});
    var gp=Provider(StoreKind.Gog,"gog",gog,gogOwned);
    Check((await gp.ListAsync(default)).Single().InstallPath==gogRoot,"owned_gog_official_install_visible");
    var gogLaunch=await gp.ResolveLaunchAsync("123",gogRoot,default);
    Check(gogLaunch?.Executable==gogExe&&gogLaunch.WorkingDirectory==Path.Combine(gogRoot,"bin")&&gogLaunch.Arguments.SequenceEqual(new[]{"--save","my saves","--mode","test"}),"gog_descriptor_native_arguments_and_working_directory");
    var gogPlan=await gp.PrepareLaunchAsync("123",gogRoot,default);
    Check(gogPlan.GameExecutable==gogExe&&gogPlan.EpicNamespace==null&&gogPlan.Arguments.SequenceEqual(gogLaunch!.Arguments),"gog_typed_plan_not_cli");
    var gogGame=libraryGame with{Key=new(StoreKind.Gog,"123"),InstallPath=gogRoot};
    var gogIdentity=(await gp.ReadSteamNativeIdentitiesAsync([gogGame],wrapper,default)).Single().Value;
    Check(gogIdentity.NativeLaunchKind=="official"&&gogIdentity.GameArguments=="--save \"my saves\" --mode test"&&gogIdentity.StartDirectory==Path.Combine(gogRoot,"bin"),"gog_identity_raw_command_round_trip");
    Check(!await gp.CanUninstallAsync("123",gogRoot,Guid.NewGuid(),default),"external_gog_uninstall_readonly_fail_closed");
    Check(!File.Exists(Path.Combine(root,"state-gog","gog","installed-123.json"))&&!File.Exists(Path.Combine(gogRoot,".playhub-install-owner")),"official_gog_no_private_receipt_or_marker");

    // The in-process Epic dispatcher runs the exact same authoritative manifest
    // resolver as the existing --epic route, immediately before one activation.
    EpicInstalledLaunch? Resolve(string ns,string item,string app,string exe)=>EpicLaunchCatalog.ResolveInstalled(ns,item,app,exe,[epicDir],File.ReadAllText,EpicLaunchCatalog.PlainExistingPath);
    int activations=0;
    int? code=StoreOfficialEpicLaunch.TryRun(epicPlan,i=>{activations++;Check(i.GameExecutable==epicExe,"typed_activation_verified_target");return 0;},Resolve);
    Check(code==0&&activations==1,"zero_exit_handled_once_without_fallback");
    Check(StoreOfficialEpicLaunch.TryRun(gogPlan,_=>throw new Exception("must_not_activate"),Resolve)==null,"private_or_gog_plan_keeps_existing_route");
    foreach(var invalid in new[]{epicPlan with{EpicAppName="other"},epicPlan with{EpicCatalogItemId=null},epicPlan with{Key=new(StoreKind.Gog,"game-a")},epicPlan with{Arguments=["extra"]},epicPlan with{Environment=new Dictionary<string,string>{{"token","no"}}},epicPlan with{XboxAumid="wrong"},epicPlan with{GameExecutable=gogExe}})
        Refuses(()=>StoreOfficialEpicLaunch.TryRun(invalid,_=>throw new Exception("must_not_activate"),Resolve),"invalid_typed_epic_plan");
    File.WriteAllText(epicItem,EpicJson("game-a",secondGog));
    Refuses(()=>StoreOfficialEpicLaunch.TryRun(epicPlan,_=>throw new Exception("must_not_activate"),Resolve),"manifest_changed_between_prepare_and_activation");
    File.WriteAllText(epicItem,EpicJson("game-a",epicRoot));

    // Private CLI records retain priority and the old provider route. The
    // fallback seam must not even be read for an already-private installation.
    string privateInstalled=Path.Combine(root,"state-epic","epic","native","installed.json");
    FileAt(privateInstalled,JsonSerializer.Serialize(new Dictionary<string,object>{{"game-a",new{install_path=epicRoot,executable="bin/game.exe"}}}));
    ep.OfficialInstallations=new FailingInventory();
    Check((await ep.ResolveLaunchAsync("game-a",epicRoot,default))!.Executable.EndsWith("never-started-host.exe"),"private_cli_descriptor_route_unchanged");
    Check((await ep.ListAsync(default)).Single().Launchable,"private_install_list_does_not_read_official_inventory");
    Check((await ep.ReadSteamNativeIdentitiesAsync([libraryGame],wrapper,default)).Single().Value.NativeLaunchKind==null,"private_identity_mode_unchanged");

    // Startup inventory errors are uncertainty, never an observed uninstall.
    foreach(var store in new[]{StoreKind.Epic,StoreKind.Gog})
    foreach(bool connected in new[]{false,true})
    {
        var key=new StoreGameKey(store,"remembered");string disk=Path.Combine(root,$"queue-{store}-{connected}.json");
        var original=new StoreLibraryGame(key,"Previous installation",7,caps,true,true,gogRoot);
        File.WriteAllText(disk,JsonSerializer.Serialize(new{Version=1,Revision=10L,Accounts=new[]{new StoreAccount(store,StoreConnectionState.Connected,7,"test",caps)},Games=new[]{original},Jobs=Array.Empty<StoreDownloadJob>(),AccountIdentities=new Dictionary<StoreKind,string>{{store,"same-account"}},JobAccountIdentities=new Dictionary<Guid,string>()}));
        var uncertain=new UnknownProvider(store,connected);
        await using var engine=new StoreQueueEngine([uncertain],new StoreInstallFileSystem(),disk);
        await engine.InitializeAsync();var restored=(await engine.GetSnapshotAsync()).Games.Single();
        Check(restored.IsInstalled&&restored.InstallPath==gogRoot&&!restored.IsLaunchable&&restored.UnavailableReason=="installation_inventory_unavailable","unknown_inventory_preserves_install:"+store+connected);
        Check(!(await engine.GetVerifiedLaunchDescriptorAsync(key)).IsLaunchable,"unknown_inventory_does_not_authorize_launch");
        uncertain.Recovered=true;await engine.InitializeAsync();
        var recovered=(await engine.GetSnapshotAsync()).Games.Single();
        Check(recovered.IsInstalled&&recovered.UnavailableReason==null&&recovered.IsLaunchable==(store==StoreKind.Gog||connected),"inventory_recovery_clears_only_transient_reason");
    }
    // Original native .item + .egstore manifest supply offline removal authority
    // for an installation never registered with Legendary. Assets can be damaged;
    // file paths, product identity and retained registration remain authoritative.
    string removalRoot=Folder("native-removal"),removalDirectory=Folder("native-removal-items"),removalGuid=Guid.NewGuid().ToString("N");
    string removalExe=FileAt(Path.Combine(removalRoot,"bin","game.exe")),removalData=FileAt(Path.Combine(removalRoot,"content.bin"),"modified by user"),removalSave=FileAt(Path.Combine(removalRoot,"saves","user.sav"),"preserve save");
    string removalBinary=FileAt(Path.Combine(removalRoot,".egstore",removalGuid+".manifest"),"retained original binary fixture");
    var removalJson=JsonNode.Parse(EpicJson("Fortnite",removalRoot))!;
    removalJson["CatalogNamespace"]="fn";removalJson["CatalogItemId"]="fn-item";
    removalJson["InstallationGuid"]=removalGuid;removalJson["CompleteManifestPath"]=removalBinary;
    string removalItem=FileAt(Path.Combine(removalDirectory,removalGuid+".item"),removalJson.ToJsonString());
    var removalMagic=new byte[41];System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(removalMagic,0x44BEC00C);File.WriteAllBytes(removalBinary,removalMagic);
    string removalHelpers=Folder("native-removal-helper");
    string removalCompanion=FileAt(Path.Combine(removalHelpers,removalGuid+".egi"),JsonSerializer.Serialize(new{v4=new{
        installationId=removalGuid,sandboxId="fn",itemId="fn-item",artifactId="Fortnite",state="Installed",platform="Windows",stagedVersion="",pendingTags=Array.Empty<string>(),
        dir=removalRoot,metaDir=Path.Combine(removalRoot,".egstore"),manifestPath=removalBinary,
        manifestData=new{version="fixture-v1",launchCommand="",buildSize=1,launchExe="bin/game.exe"},pendingManifestData=new{version=""}
    }}));
    var removalInventory=new StoreOfficialInstallations(epicDirectories:()=>[removalDirectory],epicInstalledItems:()=>removalHelpers);
    int removalQueries=0;
    var removalProvider=new StoreCliProvider(StoreKind.Epic,Folder("native-no-cli"),Folder("native-removal-state"),Environment.ProcessPath!,toolQuery:(_,args,_,_)=>
    {
        Check(args.SequenceEqual(new[]{"list-files","Fortnite","--json","--manifest",removalBinary}),"native_removal_uses_only_exact_offline_manifest");removalQueries++;
        return Task.FromResult((0,JsonSerializer.Serialize(new[]{new{filename="bin/game.exe",file_size=9999L,sha_hash=new string('a',40)},new{filename="content.bin",file_size=9999L,sha_hash=new string('b',40)}})));
    }){OfficialInstallations=removalInventory};
    File.Delete(removalExe);
    Check(removalInventory.Read(StoreKind.Epic,default).Find("Fortnite")==null,"damaged_native_executable_never_grants_launch_authority");
    Check(removalInventory.ReadForRemoval(StoreKind.Epic,default).Find("Fortnite") is {EpicSecondaryRegistrationPath:{} companionPath} removalNative&&removalNative.InstallPath==removalRoot&&companionPath==removalCompanion,"damaged_native_install_retains_exact_removal_identity_and_matching_companion");
    var removalPlan=await ((IStoreLegacyInstalledGameProvider)removalProvider).VerifyLegacyInstallationAsync("Fortnite",removalRoot,default);
    Check(removalPlan is not null&&removalPlan.OwnerJob!=Guid.Empty&&removalPlan.Files.Single(r=>r.RelativePath=="content.bin").Size==new FileInfo(removalData).Length,"native_without_auth_or_cli_receipt_can_prepare_modified_incomplete_game");
    Check(File.Exists(removalData)&&File.ReadAllText(removalSave)=="preserve save","native_preparation_never_deletes_payload_or_save");
    string originalItem=File.ReadAllText(removalItem);removalJson["BuildVersion"]="changed";File.WriteAllText(removalItem,removalJson.ToJsonString());
    await Code(()=>removalProvider.UninstallAsync(removalPlan!,default),"installation_changed");
    Check(File.Exists(removalData)&&File.Exists(removalBinary),"native_registration_change_blocks_payload_removal");
    File.WriteAllText(removalItem,originalItem);
    await removalProvider.UninstallAsync(removalPlan!,default);
    Check(!File.Exists(removalData)&&!File.Exists(removalItem)&&!File.Exists(removalBinary)&&!File.Exists(removalCompanion)&&File.ReadAllText(removalSave)=="preserve save","native_confirmed_removal_deletes_only_declared_synthetic_assets_and_exact_registrations");
    Check(removalInventory.ReadForRemoval(StoreKind.Epic,default).Find("Fortnite")==null,"native_companion_cannot_resurrect_removed_installation");
    Check(!File.Exists(Path.Combine(removalRoot,".playhub-install-owner"))&&removalQueries>=3,"native_removal_cleans_only_its_ownership_marker");

    // Galaxy/offline installer without a gogdl manifest delegates to the exact
    // registered GOG uninstaller. Only the process activation is inert here.
    string nativeGogRoot=Folder("native-gog-removal"),nativeGogExe=FileAt(Path.Combine(nativeGogRoot,"bin","game.exe")),nativeGogInfo=FileAt(Path.Combine(nativeGogRoot,"goggame-321.info"),GogJson("321"));
    string uninstaller=FileAt(Path.Combine(nativeGogRoot,"unins000.exe")),uninstallData=FileAt(Path.Combine(nativeGogRoot,"unins000.dat")),nativeSave=FileAt(Path.Combine(nativeGogRoot,"saves","user.sav"),"keep");
    bool nativeRegistered=true;string nativeCommand="\""+uninstaller+"\"";
    var nativeGogInventory=new StoreOfficialInstallations(gogRegistrations:()=>nativeRegistered?[new("321",nativeGogRoot,nativeCommand,"fixture:321_is1")]:Array.Empty<StoreOfficialInstallations.GogRegistration>());
    var nativeGogProvider=new StoreCliProvider(StoreKind.Gog,Folder("native-gog-no-cli"),Folder("native-gog-state"),Environment.ProcessPath!){OfficialInstallations=nativeGogInventory};
    Check(StoreGogImageTrust.Publisher("GOG  sp. z o.o","CN=GOG  sp. z o.o, O=GOG  sp. z o.o, C=PL")&&StoreGogImageTrust.Publisher("GOG Sp. z o.o.","CN=GOG Sp. z o.o., O=GOG Sp. z o.o., C=PL"),"gog_real_legal_publisher_spacing_variants_accepted");
    Check(!StoreGogImageTrust.Publisher("GOG Sp. z o.o. malware","O=GOG Sp. z o.o. malware")&&!StoreGogImageTrust.Publisher("GOG Sp. z o.o.","O=Other publisher"),"gog_publisher_prefix_or_organization_mismatch_rejected");
    int nativeRuns=0;
    nativeGogProvider.GogUninstallerTrust=path=>path==uninstaller;
    nativeGogProvider.GogUninstallerRun=async start=>
    {
        Check(start.FileName==uninstaller&&start.WorkingDirectory==nativeGogRoot&&start.UseShellExecute&&start.ArgumentList.SequenceEqual(new[]{"/SILENT","/NORESTART"}),"gog_delegate_exact_image_with_only_documented_fixed_flags");
        nativeRuns++;await Task.Yield();
        // Simulate the vendor removing only its original synthetic installation.
        if(File.Exists(nativeGogExe))File.Delete(nativeGogExe);File.Delete(nativeGogInfo);File.Delete(uninstaller);File.Delete(uninstallData);nativeRegistered=false;return 0;
    };
    foreach(string badCommand in new[]{nativeCommand+" /anything",nativeCommand+" & calc.exe",Path.Combine(root,"unins000.exe"),nativeCommand.Replace("unins000.exe","other.exe")})
    {nativeCommand=badCommand;Check(nativeGogInventory.FindGogUninstaller("321",nativeGogRoot,default)==null,"gog_rejects_arbitrary_registered_command");nativeCommand="\""+uninstaller+"\"";}
    var nativeGogPlan=await ((IStoreLegacyInstalledGameProvider)nativeGogProvider).VerifyLegacyInstallationAsync("321",nativeGogRoot,default);
    Check(nativeGogPlan is {Files.Count:3}&&nativeRuns==0&&File.Exists(nativeGogExe),"gog_native_prepare_has_exact_three_file_witness_and_never_starts_uninstaller");
    File.Delete(nativeGogExe);Directory.Delete(Path.GetDirectoryName(nativeGogExe)!,false);
    string damagedQueue=Path.Combine(root,"damaged-gog-queue.json");var damagedKey=new StoreGameKey(StoreKind.Gog,"321");
    File.WriteAllText(damagedQueue,JsonSerializer.Serialize(new{Version=1,Revision=10L,Accounts=new[]{new StoreAccount(StoreKind.Gog,StoreConnectionState.Disconnected,7,null,nativeGogProvider.Capabilities)},Games=new[]{new StoreLibraryGame(damagedKey,"Damaged",7,nativeGogProvider.Capabilities,true,false,nativeGogRoot)},Jobs=Array.Empty<StoreDownloadJob>(),AccountIdentities=new Dictionary<StoreKind,string>(),JobAccountIdentities=new Dictionary<Guid,string>()}));
    await using(var damagedEngine=new StoreQueueEngine([nativeGogProvider],new StoreInstallFileSystem(),damagedQueue))
    {
        await damagedEngine.InitializeAsync();var damagedSnapshot=await damagedEngine.GetSnapshotAsync();var damaged=damagedSnapshot.Games.Single();
        Check(damaged.IsInstalled&&!damaged.IsLaunchable&&damaged.InstallPath==nativeGogRoot,"startup_retains_registered_damaged_install_even_without_executable_workdir_or_login");
        Check((await damagedEngine.PrepareUninstallAsync(damagedKey,damaged.AccountRevision,damagedSnapshot.Revision)).CanUninstall&&nativeRuns==0,"damaged_offline_registered_game_can_prepare_removal_after_host_restart");
    }
    nativeGogProvider.GogUninstallerTrust=_=>false;
    await Code(()=>nativeGogProvider.UninstallAsync(nativeGogPlan!,default),"uninstall_image_untrusted");
    Check(nativeRuns==0&&File.Exists(nativeSave),"gog_unsigned_image_never_activates");nativeGogProvider.GogUninstallerTrust=path=>path==uninstaller;
    File.AppendAllText(uninstallData,"changed");
    await Code(()=>nativeGogProvider.UninstallAsync(nativeGogPlan!,default),"installation_changed");
    Check(nativeRuns==0,"gog_native_data_change_invalidates_retained_confirmation");
    File.WriteAllText(uninstallData,"synthetic, never executable");
    await nativeGogProvider.UninstallAsync(nativeGogPlan!,default);
    Check(nativeRuns==1&&!nativeRegistered&&!File.Exists(nativeGogExe)&&File.ReadAllText(nativeSave)=="keep","gog_native_completion_requires_registry_commit_and_preserves_synthetic_save");
    Check(!File.Exists(Path.Combine(nativeGogRoot,".playhub-install-owner")),"gog_native_success_releases_only_its_owner_claim");
    Console.WriteLine($"PASS {checks} official install, account, native identity and typed launch checks; inert fixtures only");
    Console.WriteLine("Fixtures: "+root);
}
catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}

sealed class FakeHttp(string json):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json),RequestMessage=request});
}
sealed class FailingInventory:IStoreOfficialInstallations
{ public StoreOfficialInventory Read(StoreKind store,CancellationToken token)=>throw new Exception("private route touched fallback"); }
sealed class UnknownProvider(StoreKind store,bool connected):IStoreProvider
{
    public bool Recovered;
    public StoreKind Store=>store;public StoreCapabilities Capabilities=>new(StoreInstallKind.Direct,true,true,true,true,true);public Uri AuthorizationUri=>new("https://example.invalid/");
    public Task<ProviderAccount?> GetAccountAsync(CancellationToken token)=>Task.FromResult<ProviderAccount?>(connected?new("same-account","Synthetic"):null);
    public Task<ProviderAccount> ConnectAsync(string code,CancellationToken token)=>throw new NotSupportedException();
    public Task DisconnectAsync(CancellationToken token)=>throw new NotSupportedException();
    public Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken token)=>throw new NotSupportedException();
    public Task<ProviderInstallSize> GetInstallSizeAsync(string id,CancellationToken token)=>throw new NotSupportedException();
    public Task<ProviderLaunch?> ResolveLaunchAsync(string id,string path,CancellationToken token)=>Recovered?Task.FromResult<ProviderLaunch?>(new(Path.Combine(path,"bin","game.exe"),path,[])):throw new StoreOperationException("installation_inventory_unavailable");
    public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id,string path,CancellationToken token)=>throw new NotSupportedException();
    public Task<IStoreTransfer> StartAsync(TransferRequest request,Action<TransferProgress> progress,CancellationToken token)=>throw new NotSupportedException();
}
