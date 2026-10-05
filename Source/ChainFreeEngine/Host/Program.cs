using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i < args.Length; i++)
    if (args[i].StartsWith("--")) options[args[i]] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
if (options.ContainsKey("--probe"))
{
    Console.WriteLine(JsonSerializer.Serialize(await Cdp.Discover()));
    return 0;
}
string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing " + key);
string workspace = Path.GetFullPath(Required("--workspace"));
// IL DIARIO DEL MOTORE.
//
// Il motore gira senza finestra, avviato dall'agente: senza un diario su file un
// avvio fallito sparisce senza lasciare traccia, ed e' esattamente cio' che e'
// successo. Qui si scrive, in ordine, che cosa e' riuscito e che cosa no.
string logPath = Path.Combine(workspace, ".local", "engine.log");
void Log(string message)
{
    string line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message;
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        FileInfo existing = new(logPath);
        if (existing.Exists && existing.Length > 1_000_000) existing.Delete();
        File.AppendAllText(logPath, line + Environment.NewLine);
    }
    catch { /* Il diario non deve mai fermare il motore. */ }
    Console.WriteLine(line);
}
AppDomain.CurrentDomain.UnhandledException += (_, error) => Log("FATAL: " + error.ExceptionObject);
Log($"Chain Free Engine starting (pid {Environment.ProcessId}).");
string bundle = Path.GetFullPath(Required("--bundle"));
string pluginRoot = Path.GetFullPath(Required("--plugin-root"));
string settings = Path.GetFullPath(Required("--settings-dir"));
string python = Path.GetFullPath(Required("--python"));
// La build originale si legge senza duplicare gli asset editoriali. Puo'
// risiedere fuori dal workspace; la copia installata richiede invece il flag
// esplicito controllato sotto, per evitare accessi involontari all'installazione.
string? pluginBuild = options.TryGetValue("--plugin-build", out var buildPath) ? Path.GetFullPath(buildPath) : null;
if (pluginBuild is not null)
{
    if (!File.Exists(Path.Combine(pluginBuild, "index.js")))
        throw new ArgumentException("--plugin-build must point at the plugin build output containing index.js.");
    // Un esperimento non deve servire per errore la copia installata: il flag
    // distingue un'installazione reale da un workspace isolato.
    if (pluginBuild.Replace('/', '\\').Contains("\\homebrew\\plugins\\", StringComparison.OrdinalIgnoreCase)
        && !options.ContainsKey("--allow-installed-plugin"))
        throw new ArgumentException("Pass --allow-installed-plugin to serve the installed plugin, or point --plugin-build at the build output.");
}
var instanceId = Guid.NewGuid().ToString("N");
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
string baseUrl = "http://127.0.0.1:0";
// Senza consenso esplicito gli ingressi restano nel workspace isolato: non si
// raggiungono per errore impostazioni o plugin dell'installazione corrente.
bool installedLayout = options.ContainsKey("--allow-installed-plugin");
bool Inside(string path) => path.StartsWith(workspace.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
if (!Inside(bundle)) throw new ArgumentException("The renderer bundle must be inside the engine workspace.");
foreach (var path in new[] { pluginRoot, settings })
    if (!Inside(path) && !installedLayout)
        throw new ArgumentException("Chain Free Engine inputs must be inside the workspace, or pass --allow-installed-plugin.");
using var singleton = new Mutex(true, "Local\\Playhub.ChainFreeEngine", out bool created);
if (!created)
{
    // Un motore precedente e' ancora vivo: probabilmente e' rimasto orfano quando
    // l'agente che lo aveva avviato e' stato chiuso. Si esce subito, ma lasciando
    // detto perche', invece di morire in silenzio.
    Log("Another Chain Free Engine is already running: this one stops here.");
    return 3;
}
Log($"Inputs: workspace={workspace}; plugin={pluginRoot}; settings={settings}; build={pluginBuild ?? "(none)"}; installed={installedLayout}.");
using var backend = new Backend(python, Path.Combine(workspace, "Backend", "runner.py"), workspace, pluginRoot, settings, instanceId, installedLayout);
var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseUrls(baseUrl);
builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 256 * 1024);
builder.Logging.ClearProviders();
var app = builder.Build();
string runtimeState = "waiting";
var startup = Stopwatch.StartNew();
double? readyAfterMs = null;
string? attachedSocket = null;
string? startupScript = null;
string? activeInjection = null;
CdpSession? session = null;
object? lastBackendStatus = null;
bool Authorized(HttpRequest request)
{
    var supplied = Encoding.UTF8.GetBytes(request.Headers.Authorization.ToString());
    var expected = Encoding.UTF8.GetBytes("Bearer " + token);
    return CryptographicOperations.FixedTimeEquals(supplied, expected);
}
app.Use(async (ctx, next) =>
{
    var host = ctx.Request.Host;
    if (host.Host != "127.0.0.1" || host.Port != new Uri(baseUrl).Port) { ctx.Response.StatusCode = 403; return; }
    string origin = ctx.Request.Headers.Origin.ToString();
    if (origin.Length > 0)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Host != "steamloopback.host" || uri.Scheme is not ("https" or "http"))
        { ctx.Response.StatusCode = 403; return; }
        ctx.Response.Headers.AccessControlAllowOrigin = origin;
        ctx.Response.Headers.Vary = "Origin";
        ctx.Response.Headers.AccessControlAllowHeaders = "Authorization,Content-Type";
        ctx.Response.Headers.AccessControlAllowMethods = "GET,POST,OPTIONS";
    }
    if (ctx.Request.Method == "OPTIONS") { ctx.Response.StatusCode = 204; return; }
    // Le immagini richieste da <img> non portano il bearer token: rimangono
    // confinati alla directory della build gli accessi in sola lettura e loopback.
    bool open = ctx.Request.Path == "/health" || ctx.Request.Path.StartsWithSegments("/plugin-assets");
    if (!open && !Authorized(ctx.Request)) { ctx.Response.StatusCode = 401; return; }
    await next(ctx);
});
app.MapGet("/health", () => new { product = "Playhub Chain Free Engine", instanceId, processId = Environment.ProcessId,
    runtimeState, readyAfterMs, backend = lastBackendStatus, fullPluginPort = pluginBuild is not null,
    cdpAttached = attachedSocket is not null, documentStartRegistered = startupScript is not null, injectionId = activeInjection });
app.MapPost("/rpc", async (HttpRequest request, CancellationToken ct) =>
{
    try
    {
        using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        var root = json.RootElement;
        string method = root.GetProperty("method").GetString() ?? "";
        if (method.StartsWith("host.", StringComparison.Ordinal)) return Results.Json(new { ok = false, error = new { code = "DENIED", message = "Host lifecycle is not a renderer RPC." } });
        var argv = root.TryGetProperty("args", out var a) ? a.Clone() : JsonSerializer.SerializeToElement(Array.Empty<object>());
        return Results.Json(await backend.Call(method, argv, ct));
    }
    catch { return Results.Json(new { ok = false, error = new { code = "RPC_FAILED", message = "The isolated backend request failed." } }); }
});
app.MapPost("/runtime-event", async (HttpRequest request, CancellationToken ct) =>
{
    using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
    string state = json.RootElement.GetProperty("state").GetString() ?? "";
    if (state is not ("waiting" or "blocked" or "loaded" or "ready" or "disposed" or "failed")) return Results.BadRequest();
    if (!json.RootElement.TryGetProperty("instanceId", out var sourceInstance) || sourceInstance.GetString() != instanceId)
        return Results.StatusCode(409);
    if (json.RootElement.TryGetProperty("injectionId", out var sourceInjection) && sourceInjection.GetString() != activeInjection)
        return Results.StatusCode(409);
    runtimeState = state;
    if (state == "ready" && readyAfterMs is null) readyAfterMs = startup.Elapsed.TotalMilliseconds;
    Log("Renderer state: " + state + " " + json.RootElement.ToString());
    return Results.Ok();
});
app.MapGet("/plugin-bundle", () =>
{
    string? file = pluginBuild is null ? null : Path.Combine(pluginBuild, "index.js");
    return file is not null && File.Exists(file)
        ? Results.File(file, "text/javascript; charset=utf-8")
        : Results.NotFound(new { error = "The plugin build was not provided to this host." });
});
app.MapGet("/plugin-assets/{plugin}/{**assetPath}", (string plugin, string assetPath) =>
{
    if (pluginBuild is null || plugin != "Playhub") return Results.NotFound();
    string root = Path.GetFullPath(pluginBuild) + Path.DirectorySeparatorChar;
    string candidate = Path.GetFullPath(Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar)));
    if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) return Results.NotFound();
    string extension = Path.GetExtension(candidate).ToLowerInvariant();
    string type = extension switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
        ".gif" => "image/gif", ".svg" => "image/svg+xml", ".avif" => "image/avif",
        ".mp4" => "video/mp4", ".webm" => "video/webm", ".json" => "application/json",
        ".css" => "text/css", ".js" => "text/javascript",
        _ => "application/octet-stream",
    };
    return Results.File(candidate, type);
});
app.MapPost("/stop", () => { app.Lifetime.StopApplication(); return Results.Ok(); });
await app.StartAsync();
baseUrl = app.Urls.Single();
Directory.CreateDirectory(Path.Combine(workspace, ".local"));
await File.WriteAllTextAsync(Path.Combine(workspace, ".local", "host-endpoint.json"), JsonSerializer.Serialize(new { baseUrl, instanceId, processId = Environment.ProcessId }));
Log("Listening on " + baseUrl + ".");
// Il segnale nominato permette all'agente di chiedere /stop senza scrivere il
// bearer token su disco. Su arresto normale il renderer viene smontato prima
// di chiudere il CDP; Kill resta soltanto il ripiego dell'agente.
using var stopSignal = OperatingSystem.IsWindows()
    ? new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\Playhub.ChainFreeEngine.Stop." + Environment.ProcessId)
    : null;
RegisteredWaitHandle? stopWait = stopSignal is null ? null : ThreadPool.RegisterWaitForSingleObject(
    stopSignal, (_, _) => app.Lifetime.StopApplication(), null, Timeout.Infinite, true);
string bundleSource = await File.ReadAllTextAsync(bundle);

string InjectionScript(string injectionId)
{
    string config = JsonSerializer.Serialize(new { baseUrl, token, instanceId, injectionId,
        bundleUrl = baseUrl + "/plugin-bundle", assetBase = baseUrl + "/plugin-assets/" });
    return $$"""
    (async()=>{
      const config={{config}};
      const oldConfig=window.__PLAYHUB_HOST_CONFIG__;
      const previous=window.__PLAYHUB_STANDALONE__;
      const boot=window.__PLAYHUB_BOOTSTRAP__;
      const same=oldConfig?.instanceId===config.instanceId && oldConfig?.injectionId===config.injectionId;
      if(same && (['mounting','ready'].includes(previous?.state) || ['waiting','bootstrapped'].includes(boot?.state))) return 'already-active';
      // Un motore terminato forzatamente puo' lasciare il suo renderer montato.
      // Si smonta solo il namespace Playhub, mai il loader o i plugin di Decky.
      if(oldConfig?.instanceId) {
        boot?.cancel?.();
        if(previous?.dispose) await previous.dispose();
        window.__PLAYHUB_HOST__?.releaseOwnedGlobals?.();
        if(window.__PLAYHUB_STANDALONE__===previous) delete window.__PLAYHUB_STANDALONE__;
      }
      if(window.__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit || window.deckyLoader ||
         ['SP_REACT','SP_REACTDOM','SP_JSX'].some(name=>window[name]!==undefined)) return 'blocked';
      window.__PLAYHUB_HOST_CONFIG__=config;
      const marker={instanceId:config.instanceId,injectionId:config.injectionId,state:'evaluating'};
      window.__PLAYHUB_ENGINE_INJECTION__=marker;
      try {
        await (function(){
    {{bundleSource}}
        }).call(window);
        marker.state='evaluated';
        return 'evaluated';
      } catch(error) {
        marker.state='failed'; marker.error=String(error);
        return 'failed';
      }
    })()
    """;
}

string ProbeScript() => """
    (()=>({
      instanceId:window.__PLAYHUB_HOST_CONFIG__?.instanceId,
      injectionId:window.__PLAYHUB_HOST_CONFIG__?.injectionId,
      state:window.__PLAYHUB_STANDALONE__?.state || null,
      boot:window.__PLAYHUB_BOOTSTRAP__?.state || null,
      injection:window.__PLAYHUB_ENGINE_INJECTION__?.state || null,
      foreign:!!window.__DECKY_SECRET_INTERNALS_DO_NOT_USE_OR_YOU_WILL_BE_FIRED_deckyLoaderAPIInit || !!window.deckyLoader ||
        !!window.__PLAYHUB_HOST__?.hasForeignRenderer?.()
    }))()
    """;

string CleanupScript(bool onlyThisInstance) => $$"""
    (async()=>{
      const config=window.__PLAYHUB_HOST_CONFIG__;
      if(!config?.instanceId {{(onlyThisInstance ? "|| config.instanceId!==" + JsonSerializer.Serialize(instanceId) : "")}}) return;
      const previous=window.__PLAYHUB_STANDALONE__;
      window.__PLAYHUB_BOOTSTRAP__?.cancel?.();
      if(previous?.dispose) await previous.dispose();
      window.__PLAYHUB_HOST__?.releaseOwnedGlobals?.();
      if(window.__PLAYHUB_STANDALONE__===previous) delete window.__PLAYHUB_STANDALONE__;
      if(window.__PLAYHUB_HOST_CONFIG__===config) delete window.__PLAYHUB_HOST_CONFIG__;
    })()
    """;

async Task DetachAsync(bool cleanup)
{
    var current = session;
    session = null;
    attachedSocket = null;
    if (current is null) return;
    try
    {
        // I due passi hanno budget separati: una registrazione gia' rimossa
        // non deve impedire lo smontaggio del renderer ancora raggiungibile.
        if (startupScript is not null)
        {
            using var removeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try { await current.Send("Page.removeScriptToEvaluateOnNewDocument", new { identifier = startupScript }, removeTimeout.Token); }
            catch (Exception error) { Log("CDP detach registration: " + error.Message); }
        }
        if (cleanup)
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await current.Evaluate(CleanupScript(true), cleanupTimeout.Token); }
            catch (Exception error) { Log("CDP detach renderer: " + error.Message); }
        }
    }
    finally { startupScript = null; await current.DisposeAsync(); }
}

try
{
    var stopping = app.Lifetime.ApplicationStopping;
    var status = await backend.Call("host.status", JsonSerializer.SerializeToElement(Array.Empty<object>()), stopping);
    if (!status.GetProperty("ok").GetBoolean() || !status.GetProperty("result").GetProperty("backendReady").GetBoolean()
        || status.GetProperty("result").GetProperty("instanceId").GetString() != instanceId)
        throw new IOException("Backend readiness was not confirmed.");
    lastBackendStatus = status.GetProperty("result").Clone();
    Log("Backend ready.");
    string? lastTargets = null;
    string? lastWait = null;
    DateTimeOffset retryAfter = DateTimeOffset.MinValue;
    int failures = 0;
    string script = "";
    bool needsEvaluation = false;
    while (!stopping.IsCancellationRequested)
    {
        try
        {
            var targets = await Cdp.Discover(stopping);
            string description = targets.Length == 0 ? "none" : string.Join(", ", targets.Select(t => t.Title));
            if (description != lastTargets) { Log("Steam debugger targets: " + description + "."); lastTargets = description; }
            var target = targets.FirstOrDefault(t => t.Title == "SharedJSContext");
            if (target is null)
            {
                if (session is not null) await DetachAsync(false);
                runtimeState = "waiting";
            }
            else
            {
                if (session is null || attachedSocket != target.Socket)
                {
                    await DetachAsync(true);
                    session = await CdpSession.Connect(target.Socket, stopping);
                    attachedSocket = target.Socket;
                    activeInjection = Guid.NewGuid().ToString("N");
                    script = InjectionScript(activeInjection);
                    runtimeState = "waiting";
                    readyAfterMs = null;
                    try
                    {
                        // Page.enable e registrazione condividono lo stesso WebSocket.
                        // La sessione resta aperta finche' vive il target: un nuovo
                        // SharedJSContext richiede sempre una nuova registrazione.
                        await session.Send("Page.enable", new { }, stopping);
                        var added = await session.Send("Page.addScriptToEvaluateOnNewDocument", new { source = script }, stopping);
                        startupScript = added.TryGetProperty("identifier", out var id) ? id.GetString() : null;
                        Log(startupScript is null ? "Document-start returned no identifier." : "Document-start registered on persistent session.");
                    }
                    catch (Exception error) when (!stopping.IsCancellationRequested)
                    { Log("Document-start unavailable; retaining late attach: " + error.Message); }
                    needsEvaluation = true;
                    retryAfter = DateTimeOffset.MinValue;
                    Log("Attached to SharedJSContext; monitoring target replacement.");
                }
                var probe = await session.Evaluate(ProbeScript(), stopping);
                var value = probe.GetProperty("result").GetProperty("value");
                bool foreign = value.TryGetProperty("foreign", out var foreignValue) && foreignValue.ValueKind == JsonValueKind.True;
                string? state = value.TryGetProperty("state", out var stateValue) ? stateValue.GetString() : null;
                string? bootState = value.TryGetProperty("boot", out var bootValue) ? bootValue.GetString() : null;
                string? injectionState = value.TryGetProperty("injection", out var injectionValue) ? injectionValue.GetString() : null;
                if (foreign)
                {
                    if (state is "mounting" or "ready" or "failed") await session.Evaluate(CleanupScript(true), stopping);
                    runtimeState = "blocked";
                    needsEvaluation = true;
                }
                else
                {
                    bool failed = state == "failed" || injectionState == "failed" || runtimeState == "failed";
                    bool absent = state is null && bootState is null;
                    if (failed && !needsEvaluation)
                    {
                        needsEvaluation = true;
                        retryAfter = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, 5 * ++failures));
                        Log("Renderer failed; next in-process attempt at " + retryAfter.ToLocalTime().ToString("HH:mm:ss") + ".");
                    }
                    if (absent || state == "disposed") needsEvaluation = true;
                    if (needsEvaluation && DateTimeOffset.UtcNow >= retryAfter)
                    {
                        // Cambiare generation invalida gli eventi delle vecchie mount.
                        // La registrazione precedente si elimina nella stessa sessione.
                        if (failed || state == "disposed")
                        {
                            await session.Evaluate(CleanupScript(false), stopping);
                            if (startupScript is not null)
                                await session.Send("Page.removeScriptToEvaluateOnNewDocument", new { identifier = startupScript }, stopping);
                            activeInjection = Guid.NewGuid().ToString("N");
                            script = InjectionScript(activeInjection);
                            startupScript = null;
                            try
                            {
                                var added = await session.Send("Page.addScriptToEvaluateOnNewDocument", new { source = script }, stopping);
                                startupScript = added.TryGetProperty("identifier", out var id) ? id.GetString() : null;
                            }
                            catch (Exception error) when (!stopping.IsCancellationRequested) { Log("Document-start retry registration: " + error.Message); }
                        }
                        runtimeState = "waiting";
                        await session.Evaluate(script, stopping);
                        needsEvaluation = false;
                        Log("Renderer bundle evaluated in SharedJSContext.");
                    }
                    bool ours = value.TryGetProperty("instanceId", out var ownerInstance) && ownerInstance.GetString() == instanceId
                        && value.TryGetProperty("injectionId", out var ownerInjection) && ownerInjection.GetString() == activeInjection;
                    if (ours && state == "ready") { runtimeState = "ready"; failures = 0; }
                    else if (bootState == "waiting") runtimeState = "waiting";
                }
            }
            lastWait = null;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
        catch (Exception error)
        {
            // Il debugger sparisce normalmente durante la chiusura di Steam.
            // L'attesa non e' un crash del motore e non consuma riavvii dell'agente.
            runtimeState = "waiting";
            if (lastWait != error.Message) { Log("Waiting for Steam/CDP: " + error.Message); lastWait = error.Message; }
            await DetachAsync(false);
        }
        try { await Task.Delay(TimeSpan.FromSeconds(2), stopping); }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { break; }
    }
}
catch (OperationCanceledException) when (app.Lifetime.ApplicationStopping.IsCancellationRequested) { }
catch (Exception fatal)
{
    Log("FATAL: " + fatal);
    throw;
}
finally
{
    stopWait?.Unregister(null);
    await DetachAsync(true);
    await app.StopAsync();
}
return 0;

sealed class Backend : IDisposable
{
    readonly Process process;
    readonly SemaphoreSlim queue = new(1, 1);
    long sequence;
    public Backend(string python, string runner, string workspace, string root, string settings, string instance, bool installedLayout = false)
    {
        var start = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-u", runner, "--workspace", workspace, "--plugin-root", root, "--settings-dir", settings,
            "--instance-id", instance, "--allow-isolated-writes", "--runtime-reads" }) start.ArgumentList.Add(arg);
        if (installedLayout) start.ArgumentList.Add("--allow-installed-plugin");
        process = Process.Start(start) ?? throw new IOException("Could not start backend.");
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
        process.BeginErrorReadLine();
    }
    public async Task<JsonElement> Call(string method, JsonElement args, CancellationToken ct)
    {
        await queue.WaitAsync(ct);
        try
        {
            if (process.HasExited) throw new IOException("Backend exited.");
            long id = ++sequence;
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, args }));
            await process.StandardInput.FlushAsync();
            string line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15), ct)
                ?? throw new IOException("Backend closed protocol.");
            using var response = JsonDocument.Parse(line);
            if (response.RootElement.GetProperty("id").GetInt64() != id) throw new IOException("Backend correlation mismatch.");
            return response.RootElement.Clone();
        }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        finally { queue.Release(); }
    }
    public void Dispose()
    {
        try { process.StandardInput.Close(); if (!process.WaitForExit(2000)) process.Kill(entireProcessTree: true); } catch { }
        process.Dispose();
    }
}
record Target(string Title, string Url, string Socket);
static class Cdp
{
    public static async Task<Target[]> Discover(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var json = JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:8080/json", ct));
        return json.RootElement.EnumerateArray().Where(t => t.TryGetProperty("webSocketDebuggerUrl", out _))
            .Select(t => new Target(t.GetProperty("title").GetString() ?? "", t.GetProperty("url").GetString() ?? "",
                t.GetProperty("webSocketDebuggerUrl").GetString() ?? "")).ToArray();
    }
}

sealed class CdpSession : IAsyncDisposable
{
    readonly ClientWebSocket socket = new();
    readonly SemaphoreSlim queue = new(1, 1);
    long sequence;

    public static async Task<CdpSession> Connect(string address, CancellationToken ct)
    {
        var uri = new Uri(address);
        if (uri.Scheme != "ws" || uri.Host is not ("127.0.0.1" or "localhost") || uri.Port != 8080)
            throw new IOException("Unexpected Steam debugger endpoint.");
        var session = new CdpSession();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await session.socket.ConnectAsync(uri, timeout.Token); return session; }
        catch { await session.DisposeAsync(); throw; }
    }

    public Task<JsonElement> Evaluate(string expression, CancellationToken ct)
        => Send("Runtime.evaluate", new { expression, awaitPromise = true, returnByValue = true }, ct);

    public async Task<JsonElement> Send(string method, object parameters, CancellationToken ct)
    {
        await queue.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            long requestId = ++sequence;
            await socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(new { id = requestId, method, @params = parameters })),
                WebSocketMessageType.Text, true, timeout.Token);
            var buffer = new byte[65536];
            while (true)
            {
                using var data = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                    if (result.MessageType == WebSocketMessageType.Close) throw new IOException("Steam debugger disconnected.");
                    data.Write(buffer, 0, result.Count);
                    if (data.Length > 64 * 1024 * 1024) throw new IOException("Oversized debugger response.");
                } while (!result.EndOfMessage);
                using var response = JsonDocument.Parse(data.ToArray());
                if (!response.RootElement.TryGetProperty("id", out var id) || id.GetInt64() != requestId) continue;
                if (response.RootElement.TryGetProperty("error", out var error))
                    throw new IOException("CDP " + method + ": " + error.ToString());
                var commandResult = response.RootElement.GetProperty("result");
                if (commandResult.TryGetProperty("exceptionDetails", out var exception))
                {
                    string detail = exception.TryGetProperty("text", out var text) ? text.GetString() ?? "JavaScript exception" : "JavaScript exception";
                    if (exception.TryGetProperty("exception", out var remote) && remote.TryGetProperty("description", out var description))
                        detail = description.GetString() ?? detail;
                    throw new IOException("CDP " + method + ": " + detail[..Math.Min(detail.Length, 500)]);
                }
                return commandResult.Clone();
            }
        }
        finally { queue.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        // Non aspettiamo un close handshake da un target che Steam ha distrutto.
        socket.Abort(); socket.Dispose(); queue.Dispose();
        return ValueTask.CompletedTask;
    }
}
