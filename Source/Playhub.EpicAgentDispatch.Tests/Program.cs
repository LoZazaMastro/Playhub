using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using GamingMode.Services;
using Playhub.GameSession;
using Playhub.Shared;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
string root = Path.Combine(Path.GetTempPath(), "Playhub-Epic-Broker-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string exe = Path.Combine(root, "Viewfinder.exe"), manifest = Path.Combine(root, "fixture.item");
    File.WriteAllBytes(exe, []); File.WriteAllBytes(manifest, []);
    string metadata = JsonSerializer.Serialize(new { CatalogNamespace = "space", CatalogItemId = "item", AppName = "app", InstallLocation = root,
        LaunchExecutable = "Viewfinder.exe", AppCategories = new[] { "games" }, bIsIncompleteInstall = false });
    var plan = EpicLaunchCatalog.Parse(manifest, metadata, "space", "item", "app", exe, File.Exists)!;
    Check(plan is not null, "actual installed metadata planner resolves a game without starting a process");
    var request = EpicProtocolLaunchRequest.Create(plan!);
    Check(request.Valid && request.Matches(plan!), "canonical request contains only verified installed identity");

    int shellOpens = 0;
    bool beforeFailed = false;
    try { ExplorerProtocolDispatch.Epic(plan!.LaunchUri, () => { shellOpens++; throw new InvalidOperationException("The Windows desktop is unavailable for the Epic launch request."); }); }
    catch (InvalidOperationException error) { beforeFailed = error.Message.Contains("desktop is unavailable"); }
    Check(beforeFailed && shellOpens == 1, "BEFORE actual Explorer dispatcher refuses with no Explorer before URI activation");

    long now = 1000; int osCalls = 0, resolutions = 0, normalReads = 0, shellStarts = 0;
    var auth = new XboxShellBroker(root, () => false, () => shellStarts++, () => now);
    string token = File.ReadAllText(Path.Combine(root, "xbox-shell-token"));
    bool allowed = true, failDispatch = false;
    EpicInstalledLaunch? current = plan;
    var dispatched = new List<string>();
    var broker = new EpicProtocolLaunchBroker(auth.IsAuthorized, value => { resolutions++; return current; },
        () => { normalReads++; return allowed; }, uri => { osCalls++; dispatched.Add(uri); if (failDispatch) throw new IOException("uncertain handler failure"); return EpicProtocolDispatchState.Submitted; }, () => now);
    Check(!broker.Submit(IPAddress.Loopback, "wrong", request).Ok && resolutions == 0 && osCalls == 0, "wrong token cannot inspect metadata or dispatch");
    Check(!broker.Submit(IPAddress.Parse("192.0.2.1"), token, request).Ok && resolutions == 0, "remote request cannot reuse the session token");
    Check(!broker.Submit(null, token, request).Ok, "unknown peer is refused");
    Check(!broker.Submit(IPAddress.Loopback, token, null).Ok, "missing body refused");
    var invalid = new EpicProtocolLaunchRequest[] {
        request with { Version = 2 }, request with { RequestId = "not-a-guid" }, request with { Namespace = "space?x" },
        request with { CatalogItemId = "item/action" }, request with { AppName = "app\n" }, request with { GameExecutable = "relative.exe" },
        request with { GameExecutable = @"\\server\share\game.exe" }, request with { GameExecutable = exe + ":stream" },
        request with { GameExecutable = Path.Combine(root,"document.txt") }, request with { ManifestIdentitySha256 = "invalid" },
        request with { Namespace = null! }, request with { GameExecutable = null! }, request with { GameExecutable = exe + "\0.exe" },
        request with { RequestId = request.RequestId.ToUpperInvariant() }
    };
    foreach (var value in invalid) Check(!broker.Submit(IPAddress.Loopback, token, value).Ok && osCalls == 0, "malformed identity has no dispatch: " + Array.IndexOf(invalid, value));
    Check(broker.Submit(IPAddress.Loopback, token, request).Ok && osCalls == 1 && shellStarts == 0 && resolutions == 2 && normalReads == 2,
        "AFTER exact normal URI is dispatched once from agent without any Explorer dependency");
    Check(dispatched.Single() == plan!.LaunchUri && !dispatched[0].Contains("Viewfinder.exe"), "only catalog-derived registered URI reaches OS boundary");
    Check(broker.Submit(IPAddress.IPv6Loopback, token, request).Ok && osCalls == 1, "same request is idempotent after successful dispatch");
    Check(!broker.Submit(IPAddress.Loopback, token, request with { AppName = "changed" }).Ok && osCalls == 1, "same request ID with another identity cannot launch");
    allowed = false;
    Check(!broker.Submit(IPAddress.Loopback, token, request with { RequestId = Guid.NewGuid().ToString("N") }).Ok && osCalls == 1, "elevated or service-session boundary refuses launch");
    allowed = true; current = null;
    Check(!broker.Submit(IPAddress.Loopback, token, request with { RequestId = Guid.NewGuid().ToString("N") }).Ok && osCalls == 1, "uninstalled or unreadable metadata refuses launch");
    current = plan! with { LaunchUri = "https://example.invalid" };
    Check(!broker.Submit(IPAddress.Loopback, token, request with { RequestId = Guid.NewGuid().ToString("N") }).Ok && osCalls == 1, "a noncanonical resolver URI cannot override registered Epic protocol");
    current = plan! with { ManifestIdentitySha256 = new string('0',64) };
    Check(!broker.Submit(IPAddress.Loopback, token, request with { RequestId = Guid.NewGuid().ToString("N") }).Ok && osCalls == 1, "installed identity fingerprint changed before dispatch is rejected");
    current = plan;
    int raceResolves = 0;
    var race = new EpicProtocolLaunchBroker(auth.IsAuthorized, _ => ++raceResolves == 1 ? plan : null, () => true, _ => { osCalls++; return EpicProtocolDispatchState.Submitted; }, () => now);
    Check(!race.Submit(IPAddress.Loopback, token, request).Ok && osCalls == 1, "metadata removed between admission and OS request cannot dispatch");
    int userCheck = 0;
    var ownerRace = new EpicProtocolLaunchBroker(auth.IsAuthorized, _ => plan, () => ++userCheck == 1, _ => { osCalls++; return EpicProtocolDispatchState.Submitted; }, () => now);
    Check(!ownerRace.Submit(IPAddress.Loopback, token, request).Ok && osCalls == 1, "interactive user boundary is rechecked immediately before OS request");
    failDispatch = true;
    var uncertain = request with { RequestId = Guid.NewGuid().ToString("N") };
    Check(!broker.Submit(IPAddress.Loopback, token, uncertain).Ok && osCalls == 2, "uncertain handler failure is reported rather than substituted");
    Check(!broker.Submit(IPAddress.Loopback, token, uncertain).Ok && osCalls == 2, "uncertain same-request retry never dispatches twice");
    failDispatch = false;
    int pendingCalls = 0;
    var pending = new EpicProtocolLaunchBroker(auth.IsAuthorized, _ => plan, () => true, _ => { pendingCalls++; return EpicProtocolDispatchState.Pending; }, () => now);
    var accepted = pending.Submit(IPAddress.Loopback, token, request);
    Check(accepted.Ok && accepted.Code == "pending" && pending.Submit(IPAddress.Loopback, token, request) == accepted && pendingCalls == 1,
        "slow handler remains one accepted pending request for actual wrapper observation, never a second OS launch");
    var single = new EpicProtocolDispatchWorker(); Action? complete = null; int starts=0;
    Check(single.TryStart(()=>{}, action=>{starts++;complete=action;}), "actual OS-worker gate admits first request");
    Check(!single.TryStart(()=>{}, _=>starts++) && starts==1, "pending worker rejects another identity before another OS thread starts");
    Parallel.For(0,64,_=> { if(single.TryStart(()=>{},_=>Interlocked.Increment(ref starts))) throw new Exception("Concurrent worker admitted"); });
    Check(starts==1, "64 concurrent requests cannot bypass the single in-flight native worker");
    complete!();
    Check(single.TryStart(()=>throw new IOException("fixture"), action=>complete=action), "worker completion permits the next legitimate request");
    try { complete!(); } catch(IOException) {}
    Check(single.TryStart(()=>{}, action=>complete=action), "worker finally releases the gate after an actual work exception");
    complete!();
    try { single.TryStart(()=>{},_=>throw new IOException("start failed")); } catch(IOException) {}
    Check(single.TryStart(()=>{},action=>action()), "failed thread startup releases the gate without retaining phantom work");

    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    bool extraRejected = false;
    try { JsonSerializer.Deserialize<EpicProtocolLaunchRequest>(JsonSerializer.Serialize(request, json).TrimEnd('}') + ",\"uri\":\"evil://override\"}", json); }
    catch (JsonException) { extraRejected = true; }
    Check(extraRejected, "unmapped URI/executable override field is rejected at HTTP schema boundary");
    var application = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = root }).Build();
    EpicProtocolLaunchBroker.Map(application,auth,broker);
    var endpoint = ((IEndpointRouteBuilder)application).DataSources.SelectMany(source=>source.Endpoints).OfType<RouteEndpoint>()
        .Single(value=>value.RoutePattern.RawText == "/session/epic/launch");
    async Task<(int Status, string Body)> Http(string body, string suppliedToken, long? length, string type="application/json")
    {
        var context = new DefaultHttpContext { RequestServices = application.Services };
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Method = "POST"; context.Request.Path = "/session/epic/launch";
        context.Request.Headers["X-Playhub-Shell-Token"] = suppliedToken;
        context.Request.ContentType = type; context.Request.ContentLength = length;
        context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position=0;
        return (context.Response.StatusCode,await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
    string body=JsonSerializer.Serialize(request with { RequestId=Guid.NewGuid().ToString("N") },json);
    int priorCalls=osCalls;
    Check((await Http(body,"bad",body.Length)).Status==401 && osCalls==priorCalls,"actual endpoint authenticates before reading catalog or dispatching");
    Check((await Http(body,token,null)).Status==400 && (await Http(body,token,40001)).Status==400 && osCalls==priorCalls,"actual endpoint bounds missing or oversized bodies");
    Check((await Http(body.TrimEnd('}')+",\"uri\":\"evil://override\"}",token,body.Length+30)).Status==400 && osCalls==priorCalls,"actual endpoint rejects URI override fields with no OS call");
    var good=await Http(body,token,System.Text.Encoding.UTF8.GetByteCount(body));
    Check(good.Status==200 && JsonSerializer.Deserialize<EpicProtocolLaunchResult>(good.Body,json)?.Ok==true && osCalls==priorCalls+1,"actual in-memory ASP endpoint dispatches one validated catalog URI without listening or native calls");
    Check((await Http(body,token,body.Length)).Status==200 && osCalls==priorCalls+1,"actual HTTP-level repeated request remains idempotent");
    await application.DisposeAsync();
    osCalls=2; // independent client checks retain their original baseline.
    File.WriteAllText(Path.Combine(root, "config.json"), "{\"safety\":{\"apiPort\":49120}}");
    using var handler = new Handler(broker, json, token);
    using var client = new HttpClient(handler);
    EpicProtocolBrokerClient.Dispatch(root, client, plan!);
    Check(handler.Calls == 1 && osCalls == 3 && handler.Path == "http://127.0.0.1:49120/session/epic/launch", "actual helper client sends one authenticated request to configured agent");
    Check(handler.ContentLength is > 0 and <= 40000 && handler.ContentType == "application/json", "actual serialized client body satisfies endpoint length/content-type admission");
    Check(handler.LastRequest?.Matches(plan!) == true && shellStarts == 0, "actual client-to-broker flow works with absent Explorer and unchanged catalog");
    handler.FailResponse = true;
    bool failed = false;
    try { EpicProtocolBrokerClient.Dispatch(root, client, plan!); } catch (InvalidOperationException) { failed = true; }
    Check(failed && handler.Calls == 2 && osCalls == 4, "ambiguous HTTP failure after dispatch does not retry or use a raw-executable fallback");
    handler.FailResponse = false; handler.Status = HttpStatusCode.Unauthorized;
    failed = false;
    try { EpicProtocolBrokerClient.Dispatch(root, client, plan!); } catch (InvalidOperationException) { failed = true; }
    Check(failed && handler.Calls == 3 && osCalls == 4, "HTTP authorization refusal propagates before launch");
    File.WriteAllText(Path.Combine(root,"xbox-shell-token"),"bad");
    failed = false;
    try { EpicProtocolBrokerClient.Dispatch(root, client, plan!); } catch (InvalidOperationException) { failed = true; }
    Check(failed && handler.Calls == 3, "invalid local session token cannot reach network dispatch");
    var native = typeof(WindowsEpicProtocolDispatcher).GetNestedType("ShellExecuteInfo", System.Reflection.BindingFlags.NonPublic)!;
    Check(Marshal.SizeOf(native) == 112 && Marshal.OffsetOf(native,"File").ToInt32() == 24 && Marshal.OffsetOf(native,"Process").ToInt32() == 104, "actual x64 ShellExecuteEx ABI matches Windows layout");
    Console.WriteLine($"{checks} actual checks PASS. Windows native calls=0; Explorer starts=0; game/launcher processes=0.");
}
finally { Directory.Delete(root,true); }

sealed class Handler(EpicProtocolLaunchBroker broker, JsonSerializerOptions json, string token) : HttpMessageHandler
{
    internal int Calls; internal string Path = "", ContentType = ""; internal long? ContentLength; internal bool FailResponse; internal HttpStatusCode Status = HttpStatusCode.OK;
    internal EpicProtocolLaunchRequest? LastRequest;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken stop) => Task.FromResult(Send(request,stop));
    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken stop)
    {
        Calls++; Path = request.RequestUri!.AbsoluteUri;
        ContentLength = request.Content?.Headers.ContentLength; ContentType = request.Content?.Headers.ContentType?.MediaType ?? "";
        if (Status != HttpStatusCode.OK) return new(Status);
        string receivedToken = request.Headers.GetValues("X-Playhub-Shell-Token").Single();
        if (receivedToken != token) return new(HttpStatusCode.Unauthorized);
        LastRequest = JsonSerializer.Deserialize<EpicProtocolLaunchRequest>(request.Content!.ReadAsStringAsync(stop).GetAwaiter().GetResult(),json);
        var result = broker.Submit(IPAddress.Loopback, receivedToken, LastRequest);
        if (FailResponse) throw new HttpRequestException("Fixture response lost after dispatch.");
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result,json)) };
    }
}
