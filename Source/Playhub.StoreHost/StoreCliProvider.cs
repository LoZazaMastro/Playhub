// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Original Windows adapter. Protocol facts: pinned Legendary 0.21.1 / gogdl 1.4.0.
// Upstream binaries remain unmodified, separately licensed and source-pinned.
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

namespace Playhub.StoreLibrary;
public delegate Task<(int Code, string Json)> StoreToolQuery(string executable, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, CancellationToken token);

public sealed partial class StoreCliProvider : IStoreProvider, IStoreInstalledGameProvider
{
    public StoreKind Store { get; }
    private readonly string tool, root, vault, nativeAuth, host;
    private readonly StoreNativeOperationGate nativeGate;
    private readonly Dictionary<string, string> environment;
    private readonly HttpClient http;
    private readonly StoreToolQuery query;
    internal IStoreOfficialInstallations OfficialInstallations { get; set; } = new StoreOfficialInstallations();
    // Public application identifiers already embedded by the pinned upstream CLI;
    // user codes/tokens are exchanged over HTTPS and never process arguments.
    private const string EpicClient = "34a02cf8f4414e29b15921876da36f9a";
    private const string EpicPublicSecret = "daafbccc737745039dffe53d94fc76cf";
    private const string GogClient = "46899977096215655";
    private const string GogPublicSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
    public StoreCapabilities Capabilities { get; }
    public Uri AuthorizationUri => Store == StoreKind.Epic
        ? new("https://www.epicgames.com/id/login?redirectUrl=" + Uri.EscapeDataString("https://www.epicgames.com/id/api/redirect?clientId=" + EpicClient + "&responseType=code"))
        : new("https://auth.gog.com/auth?client_id=" + GogClient + "&redirect_uri=" + Uri.EscapeDataString("https://embed.gog.com/on_login_success?origin=client") + "&response_type=code&layout=client2");

    public StoreCliProvider(StoreKind store, string bundleDirectory, string stateDirectory, string hostExecutable, HttpClient? client = null, StoreToolQuery? toolQuery = null)
    {
        if (store is not (StoreKind.Epic or StoreKind.Gog)) throw new ArgumentException("unsupported_store");
        Store = store; host = Path.GetFullPath(hostExecutable);
        root = Path.Combine(stateDirectory, store.ToString().ToLowerInvariant()); StoreCredentialVault.PrivateDirectory(root);
        nativeGate = new(Path.Combine(root, "native-operation.lock"));
        vault = Path.Combine(root, "credentials.dpapi");
        var native = Path.Combine(root, "native"); Directory.CreateDirectory(native);
        nativeAuth = Path.Combine(native, store == StoreKind.Epic ? "user.json" : "auth.json");
        var name = store == StoreKind.Epic ? "legendary_windows_x64.exe" : "gogdl_windows_x86_64.exe";
        var expected = store == StoreKind.Epic ? "6BE77857DC0A6DD33EA7442CE8B5291944EC6DA53757F81CA95B57CE794BAF5B" : "291B653E6DBF7A394A3342ED0EBCEA2CBC83E5F66D030777D65CCDD60D18AB02";
        tool = Path.Combine(bundleDirectory, name);
        var verified = File.Exists(tool) && StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tool))), expected);
        Capabilities = verified ? new(StoreInstallKind.Direct, true, true, true, true, true) : new(StoreInstallKind.Unavailable, false, false, false, false, false, "runtime_missing_or_modified");
        environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().Where(e => e.Key is string && e.Value is string).ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase);
        environment["LEGENDARY_CONFIG_PATH"] = native; environment["GOGDL_CONFIG_PATH"] = native;
        environment["PYTHONUTF8"] = "1";
        File.WriteAllText(Path.Combine(native, "config.ini"), "[Legendary]\ndisable_update_check=true\ndisable_update_notice=true\n");
        http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        query = toolQuery ?? StoreToolProcess.QueryAsync;
    }
    public async Task<ProviderAccount> ConnectAsync(string authorizationCode, CancellationToken token)
    {
        if (!Capabilities.CanConnect) throw new StoreOperationException("runtime_missing_or_modified");
        await nativeGate.WaitAsync(token);
        try
        {
            var code = authorizationCode.Trim();
            if (code.StartsWith('{')) { using var pasted = JsonDocument.Parse(code); code = pasted.RootElement.GetProperty("authorizationCode").GetString() ?? ""; }
            if (string.IsNullOrWhiteSpace(code) || code.Length > 4096) throw new StoreOperationException("authorization_invalid");
            var values = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["code"] = code };
            using var request = new HttpRequestMessage(Store == StoreKind.Epic ? HttpMethod.Post : HttpMethod.Get, Store == StoreKind.Epic ? "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token" : "https://auth.gog.com/token?" + Query(new() { ["client_id"] = GogClient, ["client_secret"] = GogPublicSecret, ["grant_type"] = "authorization_code", ["redirect_uri"] = "https://embed.gog.com/on_login_success?origin=client", ["code"] = code }));
            if (Store == StoreKind.Epic)
            {
                values["token_type"] = "eg1"; request.Content = new FormUrlEncodedContent(values);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(EpicClient + ":" + EpicPublicSecret)));
            }
            using var response = await http.SendAsync(request, token); if (!response.IsSuccessStatusCode) throw new StoreOperationException("authentication_failed");
            var raw = await response.Content.ReadAsStringAsync(token); using var doc = JsonDocument.Parse(raw); var auth = doc.RootElement;
            if (!auth.TryGetProperty("access_token", out _) || !auth.TryGetProperty("refresh_token", out _)) throw new StoreOperationException("authentication_failed");
            var id = auth.GetProperty(Store == StoreKind.Epic ? "account_id" : "user_id").ToString();
            if (string.IsNullOrWhiteSpace(id)) throw new StoreOperationException("authentication_failed");
            if (Store == StoreKind.Gog)
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw)!;
                data["loginTime"] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                raw = JsonSerializer.Serialize(new Dictionary<string, object> { [GogClient] = data });
            }
            StoreCredentialVault.Write(vault, raw);
            return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))), auth.TryGetProperty("displayName", out var display) ? display.GetString() : null);
        }
        finally { nativeGate.Release(); }
    }
    public async Task<ProviderAccount?> GetAccountAsync(CancellationToken token)
    {
        await nativeGate.WaitAsync(token);
        try
        {
            if (!File.Exists(vault)) return null;
            using var doc = JsonDocument.Parse(StoreCredentialVault.Read(vault));
            var auth = Store == StoreKind.Epic ? doc.RootElement : doc.RootElement.GetProperty(GogClient);
            var id = auth.GetProperty(Store == StoreKind.Epic ? "account_id" : "user_id").ToString();
            return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))), auth.TryGetProperty("displayName", out var name) ? name.GetString() : null);
        }
        finally { nativeGate.Release(); }
    }
    public async Task DisconnectAsync(CancellationToken token)
    {
        await nativeGate.WaitAsync(token);
        try { if (File.Exists(vault)) File.Delete(vault); if (File.Exists(nativeAuth)) File.Delete(nativeAuth); }
        finally { nativeGate.Release(); }
    }
    private void Materialize()
    {
        if (!Capabilities.CanRefresh || !File.Exists(vault)) throw new StoreOperationException("authentication_required");
        File.WriteAllText(nativeAuth, StoreCredentialVault.Read(vault));
    }
    private void Seal()
    {
        if (File.Exists(nativeAuth)) { StoreCredentialVault.Write(vault, File.ReadAllText(nativeAuth)); File.Delete(nativeAuth); }
    }
    private sealed class QueryScope(string authPath, Dictionary<string, string> environment, bool ownsCredentials, Action close) : IDisposable
    {
        public string AuthPath { get; } = authPath;
        public Dictionary<string, string> Environment { get; } = environment;
        public bool OwnsCredentials { get; } = ownsCredentials;
        public void Dispose() => close();
    }
    private QueryScope OpenQuery(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (nativeGate.TryWait())
        {
            try { Materialize(); return new(nativeAuth, environment, true, () => { try { Seal(); } finally { nativeGate.Release(); } }); }
            catch { nativeGate.Release(); throw; }
        }
        // Never wait behind an hours-long download or share its refresh token.
        // A read-only access snapshot can query metadata but cannot rotate the
        // transfer's credentials. Missing/expired access fails, preserving catalog.
        var queries = Path.Combine(root, "queries"); Directory.CreateDirectory(queries);
        var directory = Path.Combine(queries, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        void Remove()
        {
            var resolved = Path.GetFullPath(directory);
            if (resolved.StartsWith(Path.GetFullPath(queries) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(Path.GetFileName(resolved), "N", out _) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
        try
        {
            var auth = JsonNode.Parse(File.ReadAllText(nativeAuth)) ?? throw new StoreOperationException("provider_busy");
            void Strip(JsonNode node)
            {
                if (node is JsonObject obj) foreach (var property in obj.ToArray()) { if (property.Key == "refresh_token") obj[property.Key] = ""; else if (property.Value != null) Strip(property.Value); }
                else if (node is JsonArray array) foreach (var child in array) if (child != null) Strip(child);
            }
            Strip(auth);
            var source = Path.GetDirectoryName(nativeAuth)!; long copied = 0; var count = 0;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested(); var relative = Path.GetRelativePath(source, file);
                if (relative.Equals(Path.GetFileName(nativeAuth), StringComparison.OrdinalIgnoreCase) || relative.StartsWith("tmp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || file.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(file); copied += info.Length;
                if (++count > 20000 || copied > 128 * 1024 * 1024 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new StoreOperationException("provider_busy");
                var target = Path.Combine(directory, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var path = Path.Combine(directory, Path.GetFileName(nativeAuth)); File.WriteAllText(path, auth.ToJsonString());
            var env = new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase) { ["LEGENDARY_CONFIG_PATH"] = directory, ["GOGDL_CONFIG_PATH"] = directory };
            return new(path, env, false, Remove);
        }
        catch { Remove(); throw; }
    }
    public async Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken token)
    {
        using var scope = OpenQuery(token);
        StoreOfficialInventory? official = null;
        StoreOfficialInstallation? Official(string id) => (official ??= OfficialInstallations.ReadForRemoval(Store, token)).Find(id);
        try
        {
            if (Store == StoreKind.Epic)
            {
                var response = await query(tool, ["list-games", "--json", "--include-non-installable", "--platform", "Windows"], scope.Environment, token);
                if (response.Code != 0) throw new StoreOperationException("library_unavailable");
                using var doc = JsonDocument.Parse(response.Json);
                return doc.RootElement.EnumerateArray().Select(g =>
                {
                    var id = g.GetProperty("app_name").GetString()!;
                    var installed = ReadEpicInstall(id)??ReadRemovalInstall(id,false);
                    if(installed is not null&&!Directory.Exists(installed.Path))installed=null;
                    if (installed is null && Official(id) is { } native &&
                        g.TryGetProperty("metadata", out var identity) && identity.ValueKind == JsonValueKind.Object &&
                        identity.TryGetProperty("namespace", out var space) && space.ValueKind == JsonValueKind.String && space.GetString() == native.EpicNamespace &&
                        identity.TryGetProperty("id", out var item) && item.ValueKind == JsonValueKind.String && item.GetString() == native.EpicCatalogItemId)
                        installed = new(native.InstallPath, native.Executable);
                    var installable = g.TryGetProperty("asset_infos", out var assets) && assets.TryGetProperty("Windows", out var asset) && asset.ValueKind == JsonValueKind.Object;
                    string[] covers = [];
                    if (g.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("keyImages", out var images) && images.ValueKind == JsonValueKind.Array)
                        covers = images.EnumerateArray().Where(i => i.TryGetProperty("type", out var type) && type.GetString() is "DieselGameBoxTall" or "OfferImageTall" or "DieselGameBox" or "OfferImageWide")
                            .OrderBy(i => i.GetProperty("type").GetString() is "DieselGameBoxTall" or "OfferImageTall" ? 0 : 1).Select(i => i.TryGetProperty("url", out var url) ? OfficialArtwork(StoreKind.Epic, url.GetString()) : null).OfType<string>()
                            .SelectMany(url => StoreMetadataArtwork.CoverVariants(StoreKind.Epic, url)).Distinct(StringComparer.Ordinal).Take(4).ToArray();
                    return new ProviderGame(id, g.GetProperty("app_title").GetString()!, installable, null, installed?.Path, installed != null&&File.Exists(installed.Executable), covers.FirstOrDefault(), covers.Skip(1).ToArray());
                }).ToArray();
            }
            if (scope.OwnsCredentials) await RefreshGogToken(token);
            using var auth = JsonDocument.Parse(File.ReadAllText(scope.AuthPath)); var access = auth.RootElement.GetProperty(GogClient).GetProperty("access_token").GetString();
            var games = new List<ProviderGame>(); var pages = 1;
            for (var page = 1; page <= pages; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://embed.gog.com/account/getFilteredProducts?mediaType=1&page=" + page);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                using var response = await http.SendAsync(request, token); if (!response.IsSuccessStatusCode) throw new StoreOperationException("library_unavailable");
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (page == 1) pages = doc.RootElement.GetProperty("totalPages").GetInt32(); if (pages < 0 || pages > 1000) throw new StoreOperationException("library_invalid");
                foreach (var g in doc.RootElement.GetProperty("products").EnumerateArray())
                {
                    var id = g.GetProperty("id").ToString(); var installed = ReadGogInstall(id)??ReadRemovalInstall(id,false);
                    if(installed is not null&&!Directory.Exists(installed.Path))installed=null;
                    if (installed is null && Official(id) is { } native) installed = new(native.InstallPath, native.Executable);
                    var windows = !g.TryGetProperty("worksOn", out var works) || !works.TryGetProperty("Windows", out var win) || win.ValueKind == JsonValueKind.True;
                    var image = StoreMetadataArtwork.GogAccountCover(g);
                    games.Add(new(id, g.GetProperty("title").GetString()!, windows, null, installed?.Path, installed != null&&File.Exists(installed.Executable), image));
                }
            }
            return await EnrichGogArtworkBatch(games, token);
        }
        finally { }
    }
    private async Task RefreshGogToken(CancellationToken token)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(nativeAuth)); var auth = doc.RootElement.GetProperty(GogClient);
        var expiry = auth.GetProperty("loginTime").GetDouble() + auth.GetProperty("expires_in").GetDouble();
        if (expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60) return;
        var url = "https://auth.gog.com/token?" + Query(new() { ["client_id"] = GogClient, ["client_secret"] = GogPublicSecret, ["grant_type"] = "refresh_token", ["refresh_token"] = auth.GetProperty("refresh_token").GetString()! });
        using var response = await http.GetAsync(url, token); if (!response.IsSuccessStatusCode) throw new StoreOperationException("authentication_expired");
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await response.Content.ReadAsStringAsync(token))!;
        data["loginTime"] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        File.WriteAllText(nativeAuth, JsonSerializer.Serialize(new Dictionary<string, object> { [GogClient] = data }));
    }
    public async Task<ProviderInstallSize> GetInstallSizeAsync(string id, CancellationToken token)
    {
        ValidateId(id); using var scope = OpenQuery(token);
        try
        {
            string[] args = Store == StoreKind.Epic ? ["info", id, "--json", "--platform", "Windows"] : ["--auth-config-path", scope.AuthPath, "info", id, "--platform", "windows", "--skip-dlcs"];
            var response = await query(tool, args, scope.Environment, token); if (response.Code != 0) throw new StoreOperationException("install_size_unavailable");
            using var doc = JsonDocument.Parse(response.Json);
            if (Store == StoreKind.Epic) { var manifest = doc.RootElement.GetProperty("manifest"); return new(manifest.GetProperty("disk_size").GetInt64(), manifest.GetProperty("download_size").GetInt64()); }
            // gogdl 1.4.0 v1/v2 info returns size_data[game_id] under root.size,
            // not under a root property named after the requested product.
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count(p => p.Name == "size") != 1 ||
                !root.TryGetProperty("size", out var sizes) || sizes.ValueKind != JsonValueKind.Object)
                throw new StoreOperationException("install_size_invalid");
            long disk = 0, download = 0; var selected = new HashSet<string>(StringComparer.Ordinal);
            // The pinned Windows downloader defaults to English (en-US), plus
            // neutral depots. Explicit zero bytes is valid; absent depots is not.
            foreach (var language in sizes.EnumerateObject().Where(p => p.Name is "*" or "en-US"))
            {
                var size = language.Value;
                if (!selected.Add(language.Name) || size.ValueKind != JsonValueKind.Object ||
                    size.EnumerateObject().Count(p => p.Name == "disk_size") != 1 || size.EnumerateObject().Count(p => p.Name == "download_size") != 1 ||
                    !size.TryGetProperty("disk_size", out var diskValue) || diskValue.ValueKind != JsonValueKind.Number || !diskValue.TryGetInt64(out var diskBytes) || diskBytes < 0 ||
                    !size.TryGetProperty("download_size", out var downloadValue) || downloadValue.ValueKind != JsonValueKind.Number || !downloadValue.TryGetInt64(out var downloadBytes) || downloadBytes < 0)
                    throw new StoreOperationException("install_size_invalid");
                try { disk = checked(disk + diskBytes); download = checked(download + downloadBytes); }
                catch (OverflowException) { throw new StoreOperationException("install_size_invalid"); }
            }
            if (selected.Count == 0) throw new StoreOperationException("install_size_invalid");
            return new(disk, download);
        }
        finally { }
    }
    public Task<ProviderLaunch?> ResolveLaunchAsync(string id, string installPath, CancellationToken token)
    {
        ValidateId(id); token.ThrowIfCancellationRequested();
        try
        {
            var installed = Store == StoreKind.Epic ? ReadEpicInstall(id) : ReadGogInstall(id);
            if (installed is null)
            {
                var official = OfficialInstallations.Read(Store, token).Find(id);
                if (official is null || !SameOfficialPath(official.InstallPath, installPath)) return Task.FromResult<ProviderLaunch?>(null);
                var arguments = Store == StoreKind.Gog ? SplitWindowsArguments(official.GameArguments) : [];
                return Task.FromResult<ProviderLaunch?>(new(official.Executable, official.WorkingDirectory, arguments, official.Executable));
            }
            if (installed == null || !StringComparer.OrdinalIgnoreCase.Equals(installed.Path, Path.GetFullPath(installPath))) return Task.FromResult<ProviderLaunch?>(null);
            return Task.FromResult<ProviderLaunch?>(new(host, Path.GetDirectoryName(host)!, ["--launch", Store.ToString().ToLowerInvariant(), id], installed.Executable));
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException) { return Task.FromResult<ProviderLaunch?>(null); }
    }
    public async Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id, string installPath, CancellationToken token)
    {
        ValidateId(id); token.ThrowIfCancellationRequested();
        var installed = Store == StoreKind.Epic ? ReadEpicInstall(id) : ReadGogInstall(id);
        if (installed is null)
        {
            var official = OfficialInstallations.Read(Store, token).Find(id);
            if (official is null || !SameOfficialPath(official.InstallPath, installPath)) throw new StoreOperationException("installation_unverified");
            string[] nativeArguments = Store == StoreKind.Gog ? SplitWindowsArguments(official.GameArguments) : [];
            if (nativeArguments.Length > 256) throw new StoreOperationException("launch_parameters_invalid");
            return new(Guid.NewGuid(), new(Store, id), official.Executable, official.WorkingDirectory, nativeArguments,
                new Dictionary<string, string>(), DateTimeOffset.UtcNow.AddSeconds(30),
                EpicNamespace: official.EpicNamespace, EpicCatalogItemId: official.EpicCatalogItemId,
                EpicAppName: Store == StoreKind.Epic ? id : null);
        }
        if (installed == null || !StringComparer.OrdinalIgnoreCase.Equals(installed.Path, Path.GetFullPath(installPath))) throw new StoreOperationException("installation_unverified");
        string executable, directory; string[] args; Dictionary<string, string> env;
        if (Store == StoreKind.Epic)
        {
            using var scope = OpenQuery(token);
            string[] launchArguments=["launch",id,"--json"]; if(ReadInstalledLanguage(id,installPath) is {} launchLanguage)launchArguments=[..launchArguments,"--language",launchLanguage];
            var result = await query(tool, launchArguments, scope.Environment, token);
            if (result.Code != 0) throw new StoreOperationException("launch_prepare_failed");
            using var doc = JsonDocument.Parse(result.Json); var data = doc.RootElement;
            if (!data.TryGetProperty("launch_command", out var wrapper) || wrapper.ValueKind != JsonValueKind.Array || wrapper.GetArrayLength() != 0 || data.TryGetProperty("pre_launch_command", out var before) && before.ValueKind is not JsonValueKind.Null && (before.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(before.GetString()))) throw new StoreOperationException("launch_wrapper_refused");
            var gameDirectory = data.GetProperty("game_directory").GetString()!;
            var relative = data.GetProperty("game_executable").GetString()!;
            var verified = ValidateInstalled(gameDirectory, relative);
            if (verified == null || !StringComparer.OrdinalIgnoreCase.Equals(verified.Executable, installed.Executable)) throw new StoreOperationException("launch_executable_mismatch");
            executable = installed.Executable;
            directory = data.TryGetProperty("working_directory", out var wd) && wd.ValueKind == JsonValueKind.String ? wd.GetString()! : installed.Path;
            string[] Values(string key) => data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(v => v.GetString() ?? throw new StoreOperationException("launch_parameters_invalid")).ToArray() : throw new StoreOperationException("launch_parameters_invalid");
            args = Values("game_parameters").Concat(Values("user_parameters")).Concat(Values("egl_parameters")).ToArray();
            env = data.TryGetProperty("environment", out var environmentData) && environmentData.ValueKind == JsonValueKind.Object ? environmentData.Deserialize<Dictionary<string, string>>()! : [];
        }
        else
        {
            executable = installed.Executable; env = [];
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(installed.Path, "goggame-" + id + ".info")));
            var task = doc.RootElement.GetProperty("playTasks").EnumerateArray().Single(t => t.TryGetProperty("isPrimary", out var p) && p.ValueKind == JsonValueKind.True && t.TryGetProperty("type", out var type) && type.GetString() == "FileTask");
            var raw = task.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.String ? arguments.GetString()! : "";
            args = SplitWindowsArguments(raw);
            var working = task.TryGetProperty("workingDir", out var workingData) && workingData.ValueKind == JsonValueKind.String ? workingData.GetString() : null;
            if (working != null && Path.IsPathFullyQualified(working)) throw new StoreOperationException("launch_working_directory_invalid");
            directory = string.IsNullOrEmpty(working) ? installed.Path : Path.GetFullPath(Path.Combine(installed.Path, working));
        }
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory) || !StringComparer.OrdinalIgnoreCase.Equals(directory, installed.Path) && !directory.StartsWith(Path.TrimEndingDirectorySeparator(installed.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new StoreOperationException("launch_working_directory_invalid");
        if (args.Length > 1024 || args.Any(a => a.IndexOfAny(['\0', '\r', '\n']) >= 0) || args.Sum(a => (long)a.Length + 3) > 30000 || env.Any(e => string.IsNullOrWhiteSpace(e.Key) || e.Key.Contains('=') || e.Key.StartsWith("Steam", StringComparison.OrdinalIgnoreCase) || e.Key.IndexOfAny(['\0', '\r', '\n']) >= 0 || e.Value == null || e.Value.IndexOf('\0') >= 0)) throw new StoreOperationException("launch_parameters_invalid");
        return new(Guid.NewGuid(), new(Store, id), executable, directory, args, env, DateTimeOffset.UtcNow.AddSeconds(30));
    }
    private static string[] SplitWindowsArguments(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var pointer = CommandLineToArgvW("game.exe " + value, out var count); if (pointer == IntPtr.Zero || count > 1025) throw new StoreOperationException("launch_parameters_invalid");
        try { return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(pointer); }
    }
    [DllImport("shell32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32")] private static extern IntPtr LocalFree(IntPtr value);
    public async Task<IStoreTransfer> StartAsync(TransferRequest request, Action<TransferProgress> progress, CancellationToken token)
    {
        ValidateId(request.Key.Id); if (request.Key.Store != Store) throw new StoreOperationException("store_mismatch");
        await nativeGate.WaitAsync(token);
        try
        {
            Materialize();
            var path = Path.GetFullPath(request.InstallPath);
            // Both aliases preserve the exact chosen path. With an earlier
            // manifest, repair verifies actual destination files: update alone
            // can report an empty diff even after the old files were removed.
            var gogOperation = File.Exists(Path.Combine(Path.GetDirectoryName(nativeAuth)!, "heroic_gogdl", "manifests", request.Key.Id)) ? "repair" : "update";
            string[] args = Store == StoreKind.Epic
                ? ["--yes", "install", request.Key.Id, "--base-path", Path.GetDirectoryName(path)!, "--game-folder", Path.GetFileName(path), "--platform", "Windows", "--disable-sdl", "--skip-dlcs"]
                : ["--auth-config-path", nativeAuth, gogOperation, request.Key.Id, "--path", path, "--platform", "windows", "--skip-dlcs"];
            if (Store == StoreKind.Gog && ValidInstallLanguage(request.Language)) args=[..args,"--lang",request.Language!];
            token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, ArgumentList = { "--supervise" } };
            var process = Process.Start(start) ?? throw new StoreOperationException("supervisor_start_failed");
            try
            {
                // After creation, finish the owned handshake even if startup was
                // canceled; the returned transfer then confirms actual cleanup.
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new SupervisedToolRequest(tool, args, environment, root), StoreLibraryService.JsonOptions)); await process.StandardInput.FlushAsync();
            }
            catch
            {
                process.StandardInput.Close();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(35)); } catch { if (!process.HasExited) process.Kill(true); }
                process.Dispose(); throw;
            }
            return new CliTransfer(process, Store, progress, async () =>
            {
                if (Store == StoreKind.Gog && !File.Exists(Path.Combine(Path.GetDirectoryName(nativeAuth)!, "heroic_gogdl", "manifests", request.Key.Id))) return false;
                if (Store == StoreKind.Gog) await FinalizeGogInstallationAsync(request.Key.Id, path, request.JobId);
                if (Store == StoreKind.Epic) await FinalizeEpicPrerequisitesAsync(request.Key.Id, path, request.JobId, token);
                var installed = Store == StoreKind.Epic ? ReadEpicInstall(request.Key.Id) : ReadGogInstall(request.Key.Id, path);
                if(installed != null && StringComparer.OrdinalIgnoreCase.Equals(installed.Path,path)) SaveInstalledLanguage(request);
                return installed != null && StringComparer.OrdinalIgnoreCase.Equals(installed.Path, path);
            }, () => File.ReadAllText(Path.Combine(path, ".playhub-install-owner")) == request.JobId.ToString("D"), () => { try { Seal(); } finally { nativeGate.Release(); } });
        }
        catch { Seal(); nativeGate.Release(); throw; }
    }
    public async Task<int> LaunchAsync(string id, CancellationToken token)
    {
        ValidateId(id); await nativeGate.WaitAsync(token); var released = 0;
        void ReleaseNative() { if (Interlocked.Exchange(ref released, 1) == 0) { try { Seal(); } finally { nativeGate.Release(); } } }
        try
        {
            Materialize(); var installed = Store == StoreKind.Epic ? ReadEpicInstall(id) : ReadGogInstall(id);
            if (installed == null) throw new StoreOperationException("installation_unverified");
            var args = Store == StoreKind.Epic ? new[] { "launch", id } : new[] { "--auth-config-path", nativeAuth, "launch", installed.Path, id, "--platform", "windows", "--no-wine" };
            if(Store==StoreKind.Epic&&ReadInstalledLanguage(id,installed.Path) is {} language)args=[..args,"--language",language];
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, ArgumentList = { "--supervise" } };
            using var process = Process.Start(start) ?? throw new StoreOperationException("supervisor_start_failed");
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new SupervisedToolRequest(tool, args, environment, root), StoreLibraryService.JsonOptions).AsMemory(), token); await process.StandardInput.FlushAsync(token);
            var errors = Task.Run(async () => { while (await process.StandardError.ReadLineAsync(token) != null) { } }, token);
            var result = 3;
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (line.Length > 131072) throw new StoreOperationException("supervisor_response_invalid");
                using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
                if (row.GetProperty("kind").GetString() == "root-exit") ReleaseNative();
                else if (row.GetProperty("kind").GetString() == "exit")
                    result = row.GetProperty("drained").GetBoolean() && !row.GetProperty("forced").GetBoolean() ? row.GetProperty("exitCode").GetInt32() : 3;
            }
            await Task.WhenAll(errors, process.WaitForExitAsync(token)); return result;
        }
        finally { ReleaseNative(); }
    }
    private sealed record Installed(string Path, string Executable);
    private static bool SameOfficialPath(string left, string right) =>
        Path.IsPathFullyQualified(right) && Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private Installed? ReadEpicInstall(string id)
    {
        var file = Path.Combine(Path.GetDirectoryName(nativeAuth)!, "installed.json"); if (!File.Exists(file)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(file)); if (!doc.RootElement.TryGetProperty(id, out var record)) return null;
        var path = record.GetProperty("install_path").GetString(); var executable = record.GetProperty("executable").GetString();
        return ValidateInstalled(path, executable);
    }
    private Installed? ReadGogInstall(string id, string? knownPath = null)
    {
        var marker = Path.Combine(root, "installed-" + id + ".json");
        var path = knownPath ?? (File.Exists(marker) ? JsonSerializer.Deserialize<string>(File.ReadAllText(marker)) : null);
        if (path == null) return null;
        var info = Path.Combine(path, "goggame-" + id + ".info"); if (!File.Exists(info)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(info)); var data = doc.RootElement;
        if (data.GetProperty("gameId").ToString() != id || data.TryGetProperty("rootGameId", out var rootId) && rootId.ToString() != id) return null;
        var tasks = data.GetProperty("playTasks").EnumerateArray().Where(t => t.TryGetProperty("isPrimary", out var primary) && primary.ValueKind == JsonValueKind.True && t.TryGetProperty("type", out var type) && type.GetString() == "FileTask").ToArray();
        if (tasks.Length != 1) return null;
        var result = ValidateInstalled(path, tasks[0].GetProperty("path").GetString());
        if (result != null && knownPath != null) File.WriteAllText(marker, JsonSerializer.Serialize(path));
        return result;
    }
    private static Installed? ValidateInstalled(string? path, string? relative)
    {
        if (path == null || relative == null || !Path.IsPathFullyQualified(path) || Path.IsPathFullyQualified(relative)) return null;
        var directory = Path.GetFullPath(path); var executable = Path.GetFullPath(Path.Combine(directory, relative));
        if (!executable.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        return new(directory, executable);
    }
    internal static void ValidateId(string id) { if (string.IsNullOrWhiteSpace(id) || id.Length > 250 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))) throw new StoreOperationException("game_id_invalid"); }
    internal static string? OfficialArtwork(StoreKind store, string? value)
    {
        if (value?.StartsWith("//", StringComparison.Ordinal) == true) value = "https:" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.AbsoluteUri.Length > 8192 || uri.Fragment.Length != 0) return null;
        var allowed = store switch
        {
            StoreKind.Epic => uri.Host is "cdn1.epicgames.com" or "cdn2.epicgames.com" or "cdn.unrealengine.com",
            StoreKind.Gog => uri.Host is "images.gog.com" or "images.gog-statics.com" or "images-1.gog-statics.com" or "images-2.gog-statics.com" or "images-3.gog-statics.com" or "images-4.gog-statics.com",
            StoreKind.Amazon => uri.Host is "m.media-amazon.com" or "images-na.ssl-images-amazon.com" or "images-eu.ssl-images-amazon.com",
            _ => false
        };
        return allowed ? uri.AbsoluteUri : null;
    }
    private static string Query(Dictionary<string, string> values) => string.Join("&", values.Select(v => Uri.EscapeDataString(v.Key) + "=" + Uri.EscapeDataString(v.Value)));
    internal sealed class CliTransfer : IStoreTransfer
    {
        private readonly Process process; private readonly Task<TransferCompletion> completion; private int stop;
        private readonly TaskCompletionSource reaped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TransferCompletion> Completion => completion;
        public CliTransfer(Process process, StoreKind store, Action<TransferProgress> progress, Func<Task<bool>> verify, Func<bool> checkpointPlan, Action release)
        { this.process = process; completion = Observe(store, progress, verify, checkpointPlan, release); }
        private async Task<TransferCompletion> Observe(StoreKind store, Action<TransferProgress> progress, Func<Task<bool>> verify, Func<bool> checkpointPlan, Action release)
        {
            long done = 0; long? total = null; double? speed = null; double? percent = null; PrivateConsoleSupervisor.Outcome? result = null; var abortObserved = false; var completedMarker = false; bool outputEnded = false;
            var errors = Task.Run(async () => { while (await process.StandardError.ReadLineAsync() != null) { } });
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    if (line.Length > 131072) throw new StoreOperationException("supervisor_response_invalid");
                    using var doc = JsonDocument.Parse(line); var item = doc.RootElement; var kind = item.GetProperty("kind").GetString();
                    if (kind is "stdout" or "stderr")
                    {
                        var text = item.GetProperty("line").GetString() ?? "";
                        if (store == StoreKind.Epic && (text.Contains("Command was aborted via KeyboardInterrupt", StringComparison.Ordinal) || text.Contains("Immediate exit requested!", StringComparison.Ordinal))) abortObserved = true;
                        if (store == StoreKind.Epic && (text.Contains("All done! Download manager quitting...", StringComparison.Ordinal) || text.Contains("Verification finished successfully.", StringComparison.Ordinal))) completedMarker = true;
                        if (StoreToolProcess.ParseProgress(store, text, ref done, ref total, ref speed, ref percent)) progress(new(done, total, speed, "downloading", percent));
                    }
                    else if (kind == "exit") result = new(item.GetProperty("exitCode").GetInt32(), item.GetProperty("stopRequested").GetBoolean(), item.GetProperty("drained").GetBoolean(), item.GetProperty("forced").GetBoolean());
                }
                outputEnded = true;
                await Task.WhenAll(errors, process.WaitForExitAsync());
                if (result is not { Drained: true }) return new(false, false, false, "worker_drain_unconfirmed", DrainConfirmed: false);
                if (result.StopRequested)
                {
                    var graceful = !result.Forced && (store == StoreKind.Epic ? abortObserved : result.ExitCode == -2);
                    var checkpointed = graceful && checkpointPlan();
                    return new(false, checkpointed, false, checkpointed ? null : "checkpoint_unconfirmed", Stopped: true, DrainConfirmed: true);
                }
                if (result.Forced) return new(false, false, false, "worker_forced_stop", Stopped: true, DrainConfirmed: true);
                var installed = result.ExitCode == 0 && (store != StoreKind.Epic || completedMarker) && await verify(); return new(installed, false, installed, installed ? null : "installation_unverified", DrainConfirmed: true);
            }
            catch (StoreOperationException e) { return new(false, false, false, e.Code, DrainConfirmed: outputEnded && errors.IsCompletedSuccessfully && result is { Drained: true } && process.HasExited); }
            catch { return new(false, false, false, "worker_failed", DrainConfirmed: outputEnded && errors.IsCompletedSuccessfully && result is { Drained: true } && process.HasExited); }
            finally
            {
                // A parser/stream failure is no evidence that the owned process
                // exited. Keep its provider lease until exact exit and both pipe
                // readers finish; never relabel an unconfirmed result successful.
                _ = ReapAsync(errors, outputEnded, release);
            }
        }
        private async Task ReapAsync(Task errors, bool outputEnded, Action release)
        {
            bool exited = false;
            try
            {
                if (!outputEnded)
                    try { while (await process.StandardOutput.ReadLineAsync() != null) { } } catch (Exception error) when (error is not OutOfMemoryException) { }
                try { await errors; } catch (Exception error) when (error is not OutOfMemoryException) { }
                await process.WaitForExitAsync();
                exited = true;
            }
            finally
            {
                // These are only our redirected handles and exact owned process.
                // DisposeAsync awaits this reaper rather than closing them early.
                if (exited)
                {
                    try { process.StandardOutput.Dispose(); process.StandardError.Dispose(); process.StandardInput.Dispose(); process.Dispose(); }
                    finally { try { release(); } finally { reaped.TrySetResult(); } }
                }
            }
        }
        public async Task RequestCheckpointAsync(CancellationToken token)
        { if (Interlocked.Exchange(ref stop, 1) == 0 && !reaped.Task.IsCompleted && !process.HasExited) { await process.StandardInput.WriteLineAsync("checkpoint".AsMemory(), token); await process.StandardInput.FlushAsync(token); } }
        public ValueTask DisposeAsync() => new(reaped.Task);
    }
}
