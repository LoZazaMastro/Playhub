// SPDX-License-Identifier: MIT. Original one-shot Steam installation intent checks.
using Playhub.StoreLibrary;
using System.Text.Json;
var checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
var now=DateTimeOffset.Parse("2026-11-15T12:00:00Z");
var key=new StoreGameKey(StoreKind.Gog,"1207658901");
var caps=new StoreCapabilities(StoreInstallKind.Direct,true,true,true,true,true);
StoreLibrarySnapshot snapshot=new(1,now,[],[],[]);
var protocol=new StoreUiIntentProtocol(_=>Task.FromResult(snapshot),()=>now);
JsonElement Args(object value)=>JsonSerializer.SerializeToElement(value,StoreLibraryService.JsonOptions);
async Task<T> Call<T>(string method,object args){var result=await protocol.TryHandleAsync(method,Args(args),default);Check(result.Handled,"owned method handled");return (T)result.Data!;}
var opened=await Call<StoreUiOpenResult>("ui-install-request",new{key});
Check(opened.Accepted,"uninstalled shortcut requests installation in Steam");
var first=(await Call<StoreUiIntentReadback>("ui-install-take",new{})).Intent;
Check(first?.Key==key&&first.Action=="install"&&first.RequestId!=Guid.Empty&&first.ExpiresAt==now.AddSeconds(30),"fresh bounded exact-game intent");
Check((await Call<StoreUiIntentReadback>("ui-install-take",new{})).Intent is null,"Back cannot replay a consumed navigation");
await Call<StoreUiOpenResult>("ui-install-request",new{key});now=now.AddSeconds(30);
Check((await Call<StoreUiIntentReadback>("ui-install-take",new{})).Intent is null,"stale shortcut intent never interrupts later browsing");
snapshot=snapshot with{Games=[new(key,"Synthetic",0,caps,true,true,"C:\\OwnedSynthetic")]};
await Call<StoreUiOpenResult>("ui-install-request",new{key});
Check((await Call<StoreUiIntentReadback>("ui-install-take",new{})).Intent?.Action=="manage","installed game opens management view");
var other=new StoreGameKey(StoreKind.Epic,"other");
await Call<StoreUiOpenResult>("ui-install-request",new{key});await Call<StoreUiOpenResult>("ui-install-request",new{key=other});
Check((await Call<StoreUiIntentReadback>("ui-install-take",new{})).Intent?.Key==other,"latest explicit request replaces pending older game");
Check((await Call<StoreUiIntentReadback>("ui-install-take",new{})).Intent is null,"replacement still delivered once");
foreach(var invalid in new[]{new StoreGameKey((StoreKind)500,"id"),new(StoreKind.Gog," id"),new(StoreKind.Gog,"bad\nkey"),new(StoreKind.Gog,new string('a',513))})
 Check(!(await Call<StoreUiOpenResult>("ui-install-request",new{key=invalid})).Accepted,"invalid game never becomes navigation");
await Call<StoreUiOpenResult>("ui-install-request",new{key});
using(var canceled=new CancellationTokenSource()){canceled.Cancel();try{await protocol.TryHandleAsync("ui-install-take",Args(new{}),canceled.Token);throw new Exception("canceled request consumed intent");}catch(OperationCanceledException){checks++;}}
Check((await Call<StoreUiIntentReadback>("ui-install-take",new{})).Intent?.Key==key,"canceled reader preserves user intent");
Check(!(await protocol.TryHandleAsync("foreign-method",Args(new{}),default)).Handled,"unrelated protocol not claimed");
foreach(var entry in new[]{("epic",StoreKind.Epic),("gog",StoreKind.Gog),("xbox",StoreKind.Xbox),("amazon",StoreKind.Amazon)})
 Check(StoreSignInLaunchRequest.TryParse(["--store-sign-in",entry.Item1],out var parsed)&&parsed==entry.Item2,"auth-only entry matches exact store");
foreach(string[] arguments in new string[][]{[],["--store-sign-in"],["--store-sign-in","0"],["--store-sign-in","GOG"],["--store-sign-in","gog","extra"],["--store-sign-in","amazon --extra"],["--foreign","gog"]})
 Check(!StoreSignInLaunchRequest.TryParse(arguments,out _),"foreign arguments never activate sign-in entry");
Console.WriteLine($"PASS {checks} installation-intent and sign-in-entry checks; no UI, Steam, host, auth or download processes");
