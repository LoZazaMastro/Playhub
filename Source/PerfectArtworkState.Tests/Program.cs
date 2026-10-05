using Playhub.Integrations;
using Playhub.Emulation.Workbench;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

string fixture=Path.Combine(Path.GetTempPath(),"Playhub-perfect-state-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);int count=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
async Task Refuses(Func<Task> action,string name){try{await action();}catch(IOException){count++;return;}throw new Exception(name);}
try
{
 string grid=Path.Combine(fixture,"Steam","userdata","7","config","grid");Directory.CreateDirectory(grid);
 string file=Path.Combine(grid,"2485417537.json");
 var original=new JsonObject { ["nVersion"]=7,["foreign"]=new JsonObject{["preserve"]=true},["logoPosition"]=new JsonObject{["pinnedPosition"]="TopRight",["nWidthPct"]=63,["nHeightPct"]=41} };
 await File.WriteAllTextAsync(file,original.ToJsonString());
 var store=new ApplicationIntegrationDataStore(Path.Combine(fixture,"own"));var state=new PerfectArtworkState(store,()=>false);
 await state.SetHiddenAsync("pc:cuphead",2485417537,[grid],true,default);
 var hidden=JsonNode.Parse(await File.ReadAllTextAsync(file))!;
 Check(hidden["logoPosition"]!["nWidthPct"]!.GetValue<double>()==.01 && JsonNode.DeepEquals(hidden["foreign"],original["foreign"]) && hidden["nVersion"]!.GetValue<int>()==7,"Hide preserves foreign fields and version");
 await state.SetHiddenAsync("pc:cuphead",2485417537,[grid],true,default);
 await state.SetHiddenAsync("pc:cuphead",2485417537,[grid],false,default);
 Check(JsonNode.DeepEquals(JsonNode.Parse(await File.ReadAllTextAsync(file)),original),"Repeated composition restores the exact original position");
 await state.SetHiddenAsync("emu:before-import",0,[],true,default);
 Check((await store.ReadCategoryAsync("emu:before-import","artwork"))["logoHiddenRequested"]!.GetValue<bool>(),"AppID zero prepares owned state without Steam writes");
 await Refuses(()=>state.SetHiddenAsync("pc:bad",2485417537,[fixture],true,default),"Untrusted account refused");
 string prior=await File.ReadAllTextAsync(file);
 var missingReadback=new PerfectArtworkState(store,()=>true,(_,_)=>Task.FromResult<JsonNode?>(new JsonObject{["ok"]=true}));
 await Refuses(()=>missingReadback.SetHiddenAsync("pc:cuphead",2485417537,[grid],true,default),"Native acknowledgement without actual file readback refused");
 Check(await File.ReadAllTextAsync(file)==prior,"Running Steam never gets a competing direct disk write");
 var live=new PerfectArtworkState(store,()=>true,async(expression,ct)=>{Check(expression.Contains("SetCustomLogoPositionForApp")&&!expression.Contains("DeckyBackend"),"Uses native Steam API only");var root=(JsonObject)original.DeepClone();root["logoPosition"]=new JsonObject{["pinnedPosition"]="BottomLeft",["nWidthPct"]=.01,["nHeightPct"]=.01};await File.WriteAllTextAsync(file,root.ToJsonString(),ct);return new JsonObject{["ok"]=true};});
 await live.SetHiddenAsync("pc:cuphead",2485417537,[grid],true,default);
 Check((await store.ReadCategoryAsync("pc:cuphead","artwork"))["logoPositionBackups"]!.AsObject().Count==1,"Live and offline paths share one immutable backup");
 string otherGrid=Path.Combine(fixture,"Steam","userdata","8","config","grid");Directory.CreateDirectory(otherGrid);
 int calls=0;var multi=new PerfectArtworkState(store,()=>true,(_,_)=>{calls++;return Task.FromResult<JsonNode?>(new JsonObject{["ok"]=true});});
 await Refuses(()=>multi.SetHiddenAsync("pc:multi",2485417537,[grid,otherGrid],true,default),"Multiple accounts cannot mutate the active Steam session");
 Check(calls==0&&!File.Exists(Path.Combine(otherGrid,"2485417537.json")),"Inactive account refused before native call or disk write");
 string node=args.FirstOrDefault()??Path.GetFullPath("Source/Playhub/Tools/Media/node.exe");
 string expression=PerfectArtworkState.BuildLogoPositionExpression(2485417537,"7",original.ToJsonString());
 string probe=Path.Combine(fixture,"account-guard.cjs");
 await File.WriteAllTextAsync(probe,"const vm=require('node:vm');const expression="+System.Text.Json.JsonSerializer.Serialize(expression)+";(async()=>{let writes=0;global.window={App:{BHasCurrentUser:()=>true,GetServicesInitialized:()=>true,GetCurrentUser:()=>({strSteamID:'76561197960265736'})},SteamClient:{Apps:{SetCustomLogoPositionForApp:async()=>{writes++;}}}};try{await vm.runInThisContext(expression);throw Error('wrong account accepted');}catch(e){if(e.message!=='steam_account_mismatch')throw e;}if(writes!==0)throw Error('wrong account wrote');window.App.GetCurrentUser=()=>({strSteamID:'76561197960265735'});if(!(await vm.runInThisContext(expression)).ok||writes!==1)throw Error('exact account failed');window.App.GetServicesInitialized=()=>false;try{await vm.runInThisContext(expression);throw Error('unready accepted');}catch(e){if(e.message!=='steam_account_unavailable')throw e;}if(writes!==1)throw Error('unready wrote');console.log('3 native account protocol cases PASS');})().catch(e=>{console.error(e);process.exitCode=1;});");
 using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(node){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,ArgumentList={probe}})!)
 {string stdout=await process.StandardOutput.ReadToEndAsync(),stderr=await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Check(process.ExitCode==0&&stdout.Contains("3 native account protocol cases PASS"),"Actual generated JS rejects wrong and unready account before write: "+stderr);}
 string home=Path.Combine(fixture,"homebrew"),settings=Path.Combine(home,"settings","Playhub-Artworks","playhub_artworks.json"),sources=Path.Combine(home,"data","Playhub-Artworks","perfect_sources");
 Directory.CreateDirectory(Path.GetDirectoryName(settings)!);Directory.CreateDirectory(sources);
 string actual=Path.Combine(fixture,"actual.jpg"),pristine=Path.Combine(sources,"42_hero.jpg");await File.WriteAllBytesAsync(actual,[1,2,3]);await File.WriteAllBytesAsync(pristine,[4,5,6]);
 var marker=new JsonObject{["perfect_hero_info_42"]=new JsonObject{["sha256"]=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(actual)))}};await File.WriteAllTextAsync(settings,marker.ToJsonString());
 Check(await PerfectArtworkState.LegacyPristineAsync(Path.Combine(home,"plugins"),42,"hero",actual,default)==pristine,"Matching actual composition marker resolves one pristine original");
 await File.WriteAllBytesAsync(actual,[7,8,9]);
 Check(await PerfectArtworkState.LegacyPristineAsync(Path.Combine(home,"plugins"),42,"hero",actual,default) is null,"Stale legacy marker cannot replace a manually selected image");
 await File.WriteAllBytesAsync(actual,[1,2,3]);await File.WriteAllBytesAsync(Path.Combine(sources,"42_hero.png"),[4,5,6]);
 await Refuses(async()=>{await PerfectArtworkState.LegacyPristineAsync(Path.Combine(home,"plugins"),42,"hero",actual,default);},"Ambiguous preserved originals refused");
 Console.WriteLine($"Perfect artwork state: {count} PASS");
}
finally{Directory.Delete(fixture,true);}
