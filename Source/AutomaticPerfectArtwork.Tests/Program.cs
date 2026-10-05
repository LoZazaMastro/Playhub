using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Playhub.Integrations;

string root=Path.Combine(Path.GetTempPath(),"Playhub-auto-perfect-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string identity=ApplicationIntegrationIdentity.Create("emu","exact-rom-id");
string hero=Path.Combine(root,"hero.jpg"),logo=Path.Combine(root,"logo.png"),output=Path.Combine(root,"output.jpg");
File.WriteAllBytes(hero,[1,2,3,4,5]);File.WriteAllBytes(logo,[6,7,8,9,0]);File.WriteAllBytes(output,[9,8,7,6,5]);
string originalHero=Hash(hero),originalLogo=Hash(logo);int assigned=0,hiddenCalls=0,compositions=0;bool? hidden=null;
var store=new ApplicationIntegrationDataStore(root);var service=new AutomaticPerfectArtwork(root);
Task<IReadOnlyDictionary<string,string>> Read(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult<IReadOnlyDictionary<string,string>>(new Dictionary<string,string>{{"hero",hero},{"logo",logo}});}
Task Assign(string path,CancellationToken ct){ct.ThrowIfCancellationRequested();assigned++;File.Copy(path,hero,true);return Task.CompletedTask;}
Task Hide(bool value,CancellationToken ct){ct.ThrowIfCancellationRequested();hiddenCalls++;hidden=value;return Task.CompletedTask;}
Task<PerfectArtworkResult> Compose(string id,string h,string l,PerfectArtworkLayout layout,CancellationToken ct){ct.ThrowIfCancellationRequested();compositions++;return Task.FromResult(new PerfectArtworkResult(output,h,Hash(output),Hash(h),Hash(l),3840,1240));}
async Task Fails(Func<Task> run,string name){try{await run();throw new Exception("Expected failure: "+name);}catch(IOException){checks++;}}
try
{
 var no=await service.DeliverHeroAsync(identity,false,_=>throw new Exception("unexpected read"),Assign,Hide,compose:Compose);
 Check(no["detail"]?.ToString()=="not_requested"&&assigned==0&&compositions==0,"disabled does no work");
 var missing=await service.DeliverHeroAsync(identity,true,_=>Task.FromResult<IReadOnlyDictionary<string,string>>(new Dictionary<string,string>{{"hero",hero}}),Assign,Hide,compose:Compose);
 Check(missing["delivered"]?.GetValue<bool>()==false&&hiddenCalls==0,"missing logo is not delivered");
 await store.PatchAsync(identity,"artwork",new JsonObject{{"foreign",17},{"steamLogoManualOverride",false}});
 var yes=await service.DeliverHeroAsync(identity,true,Read,Assign,Hide,compose:Compose);
 Check(yes["delivered"]?.GetValue<bool>()==true&&assigned==1&&hiddenCalls==1,"physical delivery and hide readback");
 Check(hidden==false,"explicit visible overrides default hide");
 Check(Hash(hero)==Hash(output)&&Hash(logo)==originalLogo,"only hero changed");
 var state=await store.ReadCategoryAsync(identity,"artwork");Check(state["foreign"]?.GetValue<int>()==17&&state["hero"]?.ToString()==output,"lossless own state");
 File.WriteAllBytes(hero,[1,2,3,4,5]);assigned=0;hiddenCalls=0;
 await Fails(()=>service.DeliverHeroAsync(identity,true,Read,Assign,Hide,compose:(id,h,l,layout,ct)=>{File.WriteAllBytes(hero,[4,3,2,1,0]);return Compose(id,h,l,layout,ct);}),"changed hero refuses stale publish");
 Check(assigned==0&&hiddenCalls==0,"stale selection gets no side effect");
 File.WriteAllBytes(hero,[1,2,3,4,5]);
 await Fails(()=>service.DeliverHeroAsync(identity,true,Read,(_,_)=>{assigned++;return Task.CompletedTask;},Hide,compose:Compose),"failed assignment readback");
 Check(hiddenCalls==0,"no logo hide before assignment proof");
 assigned=0;
 await Fails(()=>service.DeliverHeroAsync(identity,true,Read,Assign,Hide,compose:(id,h,l,layout,ct)=>Task.FromResult(new PerfectArtworkResult(output,h,"BAD",Hash(h),Hash(l),3840,1240))),"changed output refuses write");
 Check(assigned==0,"tampered output gets no write");
 using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{await service.DeliverHeroAsync(identity,true,Read,Assign,Hide,ct:cancel.Token,compose:Compose);throw new Exception("cancel ignored");}catch(OperationCanceledException){checks++;}}
 string own=Path.Combine(root,"compositions",ApplicationIntegrationDataStore.Key(identity),"hero");Directory.CreateDirectory(own);
 string pristine=Path.Combine(own,"pristine.jpg");File.WriteAllBytes(pristine,[1,2,3,4,5]);
 await store.PatchAsync(identity,"artwork",new JsonObject{{"perfect_hero",new JsonObject{{"sha256",Hash(hero)},{"logoSha256",Hash(logo)},{"pristineSourcePath",pristine},{"sourceSha256",Hash(pristine)}}},{"steamLogoManualOverride",null}});
 compositions=0;assigned=0;hiddenCalls=0;
 var repeat=await service.DeliverHeroAsync(identity,true,Read,Assign,Hide,compose:Compose);
 Check(repeat["delivered"]?.GetValue<bool>()==true&&compositions==0&&assigned==0&&hidden==true,"confirmed output keeps original and default hidden");
 File.WriteAllBytes(pristine,[0,0,0,0,0]);hiddenCalls=0;
 await Fails(()=>service.DeliverHeroAsync(identity,true,Read,Assign,Hide,compose:Compose),"tampered pristine blocks repeat");
 Check(hiddenCalls==0,"bad pristine no position write");
 Check(Hash(logo)==originalLogo,"logo bytes preserved throughout");
 await store.PatchAsync(identity,"artwork",new JsonObject{{"perfect_hero",null},{"steamLogoManualOverride",true}});
 File.WriteAllBytes(hero,[1,2,3,4,5]);
 await service.DeliverHeroAsync(identity,true,Read,Assign,Hide,compose:async(id,h,l,layout,ct)=>
 {await store.PatchAsync(identity,"artwork",new JsonObject{{"steamLogoManualOverride",false}},ct);return await Compose(id,h,l,layout,ct);});
 Check(hidden==false,"manual visibility changed during composition wins");
 Console.WriteLine($"{checks} automatic Perfect artwork checks PASS");
}
finally{if(Path.GetFullPath(root).StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
