using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GamingMode.Models;
using GamingMode.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }

string root = Path.Combine(Path.GetTempPath(), "PlayhubCurrentModeChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    AppPaths paths = (AppPaths)Activator.CreateInstance(typeof(AppPaths), BindingFlags.NonPublic | BindingFlags.Instance,
        null, new object[] { root }, null)!;
    JsonStore store = new(paths, new FileLogger(paths.LogPath));
    store.SaveState(new ModeState { CurrentMode = ModeKind.Gaming });
    byte[] original = File.ReadAllBytes(paths.StatePath);
    var builder = WebApplication.CreateBuilder();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.Configure<JsonOptions>(options =>
    {
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
    await using WebApplication app = builder.Build();
    // Run the production route on a disposable server without constructing a
    // ModeManager or any native Gaming Mode service.
    typeof(AgentHost).GetMethod("MapCurrentModeEndpoint", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, new object[] { app, store });
    await app.StartAsync();
    string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using HttpClient client = new() { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };

    async Task Read(ModeKind expected)
    {
        using var response = await client.GetAsync("/mode/current");
        Check(response.IsSuccessStatusCode, "Production current-mode route succeeds");
        using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Check(json.RootElement.GetProperty("agentRunning").GetBoolean(), "Agent-running contract");
        Check(json.RootElement.GetProperty("currentMode").GetString() == expected.ToString(), "Current committed mode");
        Check(json.RootElement.EnumerateObject().Count() == 2, "Mode-only response contains no process report");
    }

    await Read(ModeKind.Gaming);
    Check(File.ReadAllBytes(paths.StatePath).SequenceEqual(original), "GET does not rewrite valid state");
    Check(!File.Exists(paths.ConfigPath) && !File.Exists(paths.LogPath), "GET never loads configuration or logs");
    store.SaveState(new ModeState { CurrentMode = ModeKind.Desktop });
    await Read(ModeKind.Desktop);
    store.SaveState(new ModeState { CurrentMode = ModeKind.Gaming });
    await Read(ModeKind.Gaming);
    using (var absent = await client.GetAsync("/status"))
        Check(absent.StatusCode == System.Net.HttpStatusCode.NotFound, "Mode route works without status service");
    var timer = Stopwatch.StartNew();
    for (int i = 0; i < 100; i++) await Read(ModeKind.Gaming);
    Console.WriteLine($"PASS isolated production HTTP route: two-field contract, immediate transitions, no status service or configuration access; 100 requests in {timer.Elapsed.TotalMilliseconds:F1} ms.");
    await app.StopAsync();
}
finally { Directory.Delete(root, recursive: true); }
