// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Playhub.StoreLibrary;

if (args.Length == 1 && args[0] == "--protocol-version") { Console.WriteLine("playhub-store-host:1"); return; }
if (args.Length == 1 && args[0] == "--supervise") { await StoreToolProcess.RunSupervisorAsync(); return; }
if (args.Length == 3 && args[0] == "--launch" && args[1] is "epic" or "gog" or "amazon")
{
    StoreMaintenance.ThrowIfActive();
    var launchState = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "StoreLibrary");
    StoreCredentialVault.PrivateDirectory(launchState);
    if(args[1]=="amazon")
    {
        Environment.ExitCode=await new StoreAmazonProvider(AppContext.BaseDirectory,launchState,Environment.ProcessPath!).LaunchAsync(args[2],CancellationToken.None);return;
    }
    var provider = new StoreCliProvider(args[1] == "epic" ? StoreKind.Epic : StoreKind.Gog, AppContext.BaseDirectory, launchState, Environment.ProcessPath!);
    Environment.ExitCode = await provider.LaunchAsync(args[2], CancellationToken.None); return;
}
if (args.Length != 1 || args[0] != "--serve") return;
StoreMaintenance.ThrowIfActive();
using var singleton = new Mutex(true, "Local\\" + StoreLibraryService.CurrentPipeName + ".host", out var first);
if (!first) return;
var bundle = AppContext.BaseDirectory;
var app = Path.GetFullPath(Path.Combine(bundle, ".."));
using var runtime = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "store-runtime.json")));
if (runtime.RootElement.GetProperty("protocol").GetInt32() != 1) throw new StoreOperationException("runtime_manifest_invalid");
var clients = StoreRuntimeClientPolicy.Read(runtime.RootElement.GetProperty("allowedClients"), app,
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew", "services"));
bool Authorized(uint pid)
{
    try
    {
        using var process = Process.GetProcessById(checked((int)pid)); var path = process.MainModule?.FileName;
        if (process.HasExited || path == null) return false;
        return StoreRuntimeClientPolicy.IsAuthorizedImage(clients, path);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
}
bool MethodAllowed(uint pid, string method)
{
    try
    {
        using var process = Process.GetProcessById(checked((int)pid));
        return !process.HasExited && process.MainModule?.FileName is { } path
            && StoreRuntimeClientPolicy.AllowsMethod(clients, path, method);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
}
var state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "StoreLibrary");
StoreCredentialVault.PrivateDirectory(state);
var host = Environment.ProcessPath ?? throw new StoreOperationException("runtime_path_invalid");
var xboxAccount = new StoreXboxAccountProvider(state);
IStoreProvider[] providers = [new StoreCliProvider(StoreKind.Epic, bundle, state, host), new StoreCliProvider(StoreKind.Gog, bundle, state, host), new StoreAmazonProvider(bundle, state, host), xboxAccount];
await using var engine = new StoreQueueEngine(providers, new StoreInstallFileSystem(), Path.Combine(state, "queue-v1.json"));
await using var xboxAccountOwner = xboxAccount;
xboxAccount.Bind(engine);
await engine.InitializeAsync();
var catalogCulture = System.Globalization.CultureInfo.CurrentCulture;
var catalogLanguage = string.IsNullOrEmpty(catalogCulture.Name) ? "en-US" : catalogCulture.Name;
if (catalogLanguage.Length != 5 || catalogLanguage[2] != '-') catalogLanguage = "en-US";
var catalogMarket = catalogLanguage[^2..].ToUpperInvariant();
var xboxCatalog = new StoreXboxCatalog(Path.Combine(state, "xbox-catalog-v1.json"), catalogMarket, catalogLanguage);
var xboxInstalled = new StoreXboxInstalledLibrary(Path.Combine(state, "xbox-installed"));
var extensions = new List<IStoreProtocolExtension> { new StoreGamePassProtocol(xboxCatalog,xboxAccount), xboxCatalog, xboxInstalled, xboxAccount, new StoreInstalledGameProtocol(engine), new StoreWindowsManagedHistoryProtocol(engine) };
extensions.Add(new StoreUiIntentProtocol(engine.GetSnapshotAsync));
extensions.Add(new StoreAchievementsProtocol(engine,providers,state));
async Task<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>> NativeSteamIdentities(IReadOnlyList<StoreLibraryGame> games, CancellationToken token)
{
    var result = new Dictionary<StoreGameKey, StoreSteamNativeIdentity>(await xboxAccount.ReadSteamNativeIdentitiesAsync(games, token));
    string wrapper = Path.Combine(app, "Playhub.GameSession.exe");
    foreach (var provider in providers.OfType<StoreCliProvider>())
        foreach (var pair in await provider.ReadSteamNativeIdentitiesAsync(games, wrapper, token)) result.Add(pair.Key, pair.Value);
    foreach (var game in games.Where(g => g.Key.Store == StoreKind.Amazon && g.IsInstalled && g.InstallPath is not null))
    {
        var launch = await providers.Single(p => p.Store == StoreKind.Amazon).ResolveLaunchAsync(game.Key.Id, game.InstallPath!, token);
        if (launch?.GameExecutablePath is { } file && File.Exists(file))
            result.Add(game.Key, new(game.Key, game.AccountRevision, null, null, game.InstallPath!, wrapper, Path.GetDirectoryName(file)!, file));
    }
    return result;
}
try { extensions.Add(new StoreSteamShortcutRegistry(engine.GetSnapshotAsync, Path.Combine(app, "Playhub.GameSession.exe"), Path.Combine(app, "Playhub.exe"),
    nativeIdentities: NativeSteamIdentities,retainedCompletions:engine.GetRetainedCompletedInstallsAsync)); }
catch (Exception e) when (e is not OutOfMemoryException) { /* Do not erase damaged claims or disable downloads. */ }
using var stop = new CancellationTokenSource();
await using var startupRefresh = new StoreStartupRefresh(engine, stop.Token, publicCatalogRefresh: async token => { await xboxCatalog.RefreshXboxCatalogAsync(false, token); });
var authority = new SteamDownloadAuthority(SteamDownloadAuthority.ResolveInstalledSteam, SteamDownloadAuthority.ProcessBirth,
    onRegistered: startupRefresh.ObserveSteamStart);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
var absence = Task.Run(async () =>
{
    long sequence = 0;
    try
    {
        while (!stop.IsCancellationRequested)
        {
            var observation = authority.Absence(++sequence);
            if (observation.Revoke is { } old) await engine.InvalidateSteamAsync(old, stop.Token);
            if (observation.Idle is { } idle) await engine.ObserveSteamAsync(idle, true, stop.Token);
            await Task.Delay(1000, stop.Token);
        }
    }
    catch (OperationCanceledException) { }
});
bool PrivateLaunch(uint pid)
{
    if (!Authorized(pid)) return false;
    try { using var process = Process.GetProcessById(checked((int)pid)); return StringComparer.OrdinalIgnoreCase.Equals(process.MainModule?.FileName, Path.Combine(app, "Playhub.GameSession.exe")); }
    catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
}
try { await new StorePipeServer(engine, Authorized, extensions, steamAuthority: authority, authorizePrivateLaunch: PrivateLaunch, exitAfterQuiesce: stop.Cancel, authorizeMethod: MethodAllowed).RunAsync(stop.Token); }
catch (OperationCanceledException) { }
finally { stop.Cancel(); await absence; }
