using Playhub.GameSession;
using Playhub.StoreLibrary;
using System.Runtime.InteropServices;

var count = 0;
void Check(bool ok) { if (!ok) throw new Exception("Store launch boundary check " + count + " failed"); count++; }
void Reject(Dictionary<string,string> values) { try { StoreLaunchEnvironment.Compose(values, new Dictionary<string,string>()); throw new Exception("Accepted invalid environment"); } catch (InvalidDataException) { count++; } }
var inherited = new Dictionary<string,string> { ["SteamAppId"] = "123", ["SteamGameId"] = "456", ["PATH"] = "inherited", ["Other"] = "preserved" };
var merged = StoreLaunchEnvironment.Compose(new Dictionary<string,string> { ["steamAPPID"] = "evil", ["SteamGameId"] = "evil", ["Path"] = "new", ["EOS_TOKEN"] = "ephemeral" }, inherited);
Check(merged.Contains("SteamAppId=123\0")); Check(merged.Contains("SteamGameId=456\0"));
Check(!merged.Contains("evil")); Check(merged.Contains("PATH=new\0")); Check(merged.Contains("Other=preserved\0"));
Check(merged.EndsWith("\0\0")); Check(merged.Contains("EOS_TOKEN=ephemeral\0"));
Reject(new() { ["X=Y"] = "bad" }); Reject(new() { ["X\n"] = "bad" }); Reject(new() { ["X"] = "bad\0" });
Reject(new() { [""] = "bad" }); Reject(new() { [new string('X', 257)] = "bad" });
Reject(Enumerable.Range(0,129).ToDictionary(i=>"KEY"+i,i=>"value"));
using (var empty = StoreLaunchEnvironment.Create(null)) Check(empty.Pointer == IntPtr.Zero);
var environment = StoreLaunchEnvironment.Create(new Dictionary<string,string> { ["PLAYHUB_LAUNCH_FIXTURE"] = "actual-block" });
Check(environment.Pointer != IntPtr.Zero);
environment.Dispose(); Check(environment.Pointer == IntPtr.Zero); environment.Dispose(); Check(environment.Pointer == IntPtr.Zero);
foreach (var provider in new[] { "epic", "gog", "xbox", "amazon" }) Check(StoreLibraryLaunchRequest.TryParse(new[] { "--store-library", provider, "product_123-foo" }, "--store-library", out var key) && key!.Id == "product_123-foo");
foreach (var bad in new[] { "../bad", "id space", "uri:bad", "", "bad\0id", new string('x',251) }) Check(!StoreLibraryLaunchRequest.TryParse(new[] { "--store-library", "epic", bad }, "--store-library", out _));
Check(!StoreLibraryLaunchRequest.TryParse(new[] { "--store-library", "bogus", "id" }, "--store-library", out _));
Check(!StoreLibraryLaunchRequest.TryParse(new[] { "--store-library", "epic", "id", "extra" }, "--store-library", out _));
Console.WriteLine($"PASS {count} actual launch environment and argument boundary checks; no game or account operations.");
