using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playhub.Services;

public sealed class CssLoaderInstallService
{
    private const long MaximumPackageBytes = 50L * 1024 * 1024;
    private const string Repository = "DeckThemes/SDH-CssLoader";
    private const string AssetName = "SDH-CSSLoader-Decky.zip";
    private static readonly IReadOnlyDictionary<string, string> PinnedReleaseHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["v2.1.2"] = "0105b29cba25eacbfb8574b8a72e24b82cd1a8fb1ea6a9b3460597c9583e79ed"
        };

    private static readonly HttpClient Client = CreateClient();

    public sealed record CssLoaderStatus(bool Installed, string Version, string PluginFolder, string Message);
    public sealed record CssLoaderInstallResult(bool Success, string Message, string? Version = null);
    private sealed record Release(string Version, string AssetName, string DownloadUrl, string Sha256, long Size);

    public CssLoaderStatus GetStatus(string? deckyPluginsPath)
    {
        var root = ResolvePluginRoot(deckyPluginsPath);
        var folder = Path.Combine(root, "SDH-CssLoader");
        try
        {
            var pluginJson = Path.Combine(folder, "plugin.json");
            var packageJson = Path.Combine(folder, "package.json");
            var frontend = Path.Combine(folder, "dist", "index.js");
            if (!File.Exists(pluginJson) || !File.Exists(packageJson) || !File.Exists(frontend))
            {
                return new(false, "", folder, "CSS Loader non installato.");
            }

            using var plugin = JsonDocument.Parse(File.ReadAllText(pluginJson));
            using var package = JsonDocument.Parse(File.ReadAllText(packageJson));
            var name = plugin.RootElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? ""
                : "";
            var version = package.RootElement.TryGetProperty("version", out var versionElement)
                ? versionElement.GetString() ?? ""
                : "";
            if (!string.Equals(name, "CSS Loader", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(version))
            {
                return new(false, "", folder, "La cartella CSS Loader esistente non supera la verifica.");
            }
            return new(true, version, folder, $"CSS Loader {version} installato.");
        }
        catch
        {
            return new(false, "", folder, "La cartella CSS Loader esistente non è leggibile.");
        }
    }

    public async Task<CssLoaderInstallResult> InstallLatestAsync(
        string? deckyPluginsPath,
        IProgress<(double Percent, string Status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var root = ResolvePluginRoot(deckyPluginsPath);
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "SDH-CssLoader");
        var stage = Path.Combine(root, ".playhub-cssloader-stage-" + Guid.NewGuid().ToString("N"));
        var rollback = Path.Combine(root, ".playhub-cssloader-rollback-" + Guid.NewGuid().ToString("N"));
        var downloadFolder = Path.Combine(AppPaths.DownloadsRoot, "plugins", "css-loader");
        Directory.CreateDirectory(downloadFolder);
        var partial = Path.Combine(downloadFolder, "SDH-CSSLoader-Decky.zip.part");
        try
        {
            DeleteFile(partial);
            progress?.Report((0.02, "Cerco l'ultima versione…"));
            var release = await ResolveLatestAsync(cancellationToken).ConfigureAwait(false);
            if (!IsSha256(release.Sha256))
            {
                Diag.Crash("CssLoaderInstallService.InstallLatestAsync", "Hash SHA-256 della release non verificabile.");
                return new(false, "Non riesco a verificare il download di CSS Loader. Riprova più tardi.");
            }

            await DownloadAsync(release, partial, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report((0.62, "Download completato. Preparo l'installazione…"));
            Directory.CreateDirectory(stage);
            await ExtractAsync(partial, stage, progress, cancellationToken).ConfigureAwait(false);

            var stagedPlugin = Path.Combine(stage, "SDH-CssLoader");
            var stagedStatus = GetStatusFromFolder(stagedPlugin);
            if (!stagedStatus.Installed ||
                !string.Equals(NormalizeVersion(stagedStatus.Version), NormalizeVersion(release.Version), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Il contenuto estratto non corrisponde alla release dichiarata.");
            }
            ApplyWindowsBrowserHookFix(stagedPlugin, stagedStatus.Version);

            if (Directory.Exists(destination)) Directory.Move(destination, rollback);
            try
            {
                Directory.Move(stagedPlugin, destination);
            }
            catch
            {
                if (Directory.Exists(rollback) && !Directory.Exists(destination)) Directory.Move(rollback, destination);
                throw;
            }

            var verified = GetStatus(deckyPluginsPath);
            if (!verified.Installed ||
                !string.Equals(NormalizeVersion(verified.Version), NormalizeVersion(release.Version), StringComparison.OrdinalIgnoreCase))
            {
                var invalid = destination + ".invalid-" + Guid.NewGuid().ToString("N");
                Directory.Move(destination, invalid);
                if (Directory.Exists(rollback)) Directory.Move(rollback, destination);
                DeleteManagedDirectory(invalid, root);
                throw new InvalidDataException("Verifica post-installazione non riuscita.");
            }

            DeleteManagedDirectory(rollback, root);
            progress?.Report((1, $"CSS Loader {verified.Version} è pronto."));
            return new(true,
                $"CSS Loader {verified.Version} è pronto. Riavvia Steam per usarlo.",
                verified.Version);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Installazione annullata senza modificare CSS Loader.");
        }
        catch (Exception ex)
        {
            if (!Directory.Exists(destination) && Directory.Exists(rollback)) Directory.Move(rollback, destination);
            Diag.Crash("CssLoaderInstallService.InstallLatestAsync", ex);
            return new(false, "Non riesco a verificare il download di CSS Loader. Riprova più tardi.");
        }
        finally
        {
            DeleteFile(partial);
            DeleteManagedDirectory(stage, root);
        }
    }

    public Task<CssLoaderInstallResult> UninstallAsync(string? deckyPluginsPath)
    {
        var root = ResolvePluginRoot(deckyPluginsPath);
        var destination = Path.Combine(root, "SDH-CssLoader");
        try
        {
            if (!Directory.Exists(destination))
            {
                return Task.FromResult(new CssLoaderInstallResult(true, "CSS Loader non è installato."));
            }

            var status = GetStatus(deckyPluginsPath);
            if (!status.Installed || !string.Equals(
                    Path.GetFullPath(status.PluginFolder).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CssLoaderInstallResult(false,
                    "La cartella esistente non è un'installazione verificata di CSS Loader."));
            }

            DeleteManagedDirectory(destination, root);
            return Task.FromResult(Directory.Exists(destination)
                ? new CssLoaderInstallResult(false, "Non riesco a rimuovere CSS Loader. Chiudi Steam e riprova.")
                : new CssLoaderInstallResult(true, "CSS Loader è stato disinstallato."));
        }
        catch (Exception ex)
        {
            Diag.Crash("CssLoaderInstallService.UninstallAsync", ex);
            return Task.FromResult(new CssLoaderInstallResult(false,
                "Non riesco a rimuovere CSS Loader. Chiudi Steam e riprova."));
        }
    }

    private static CssLoaderStatus GetStatusFromFolder(string folder)
    {
        try
        {
            var pluginJson = Path.Combine(folder, "plugin.json");
            var packageJson = Path.Combine(folder, "package.json");
            var frontend = Path.Combine(folder, "dist", "index.js");
            if (!File.Exists(pluginJson) || !File.Exists(packageJson) || !File.Exists(frontend))
            {
                return new(false, "", folder, "Pacchetto incompleto.");
            }
            using var plugin = JsonDocument.Parse(File.ReadAllText(pluginJson));
            using var package = JsonDocument.Parse(File.ReadAllText(packageJson));
            var name = plugin.RootElement.GetProperty("name").GetString() ?? "";
            var version = package.RootElement.GetProperty("version").GetString() ?? "";
            return string.Equals(name, "CSS Loader", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(version)
                ? new(true, version, folder, "Pacchetto valido.")
                : new(false, "", folder, "Manifest non valido.");
        }
        catch
        {
            return new(false, "", folder, "Manifest non leggibile.");
        }
    }

    private static void ApplyWindowsBrowserHookFix(string folder, string version)
    {
        if (!string.Equals(NormalizeVersion(version), "2.1.2", StringComparison.OrdinalIgnoreCase)) return;

        var file = Path.Combine(folder, "css_browserhook.py");
        var source = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);

        static string ApplyPatch(string value, string original, string patched, string error)
        {
            if (value.Contains(patched, StringComparison.Ordinal)) return value;
            if (!value.Contains(original, StringComparison.Ordinal)) throw new InvalidDataException(error);
            return value.Replace(original, patched, StringComparison.Ordinal);
        }

        const string oldWait = """
            while await_response:
                result = await queue.get()

                if (start_time + 5) < time.time():
                    Result(False, f"Request for {method} took more than 5s. Assuming it failed ({len(self.connected_tabs)})")
                    self.ws_response.remove(queue)
                    del queue
                    return None
            """;
        const string newWait = """
            while await_response:
                timeout = start_time + 5 - time.time()
                if timeout <= 0:
                    Result(False, f"Request for {method} took more than 5s. Assuming it failed ({len(self.connected_tabs)})")
                    self.ws_response.remove(queue)
                    del queue
                    return None

                try:
                    result = await asyncio.wait_for(queue.get(), timeout)
                except asyncio.TimeoutError:
                    Result(False, f"Request for {method} took more than 5s. Assuming it failed ({len(self.connected_tabs)})")
                    self.ws_response.remove(queue)
                    del queue
                    return None
            """;
        source = ApplyPatch(source, oldWait, newWait, "Timeout CSS Loader 2.1.2 non riconosciuto.");

        source = ApplyPatch(source,
            "MAX_QUEUE_SIZE = 500\n\nclass BrowserTabHook:",
            """
            MAX_QUEUE_SIZE = 500
            SUPPORTED_POPUP_TITLE = re.compile(r"^(MainMenu|QuickAccess|notificationtoasts)(?:_uid\d+)?$")

            class BrowserTabHook:
            """,
            "Costanti browser hook CSS Loader 2.1.2 non riconosciute.");

        source = ApplyPatch(source,
            """
                    else:
                        Log(f"Failed to connect to tab with id {self.id}")
                        self.hook.connected_tabs.remove(self)
                        return

                    self.init_done = True
            """,
            """
                    else:
                        Log(f"Failed to connect to tab with id {self.id}")
                        self.hook.mark_target_failed(self.id, self.title, self.url)
                        if self in self.hook.connected_tabs:
                            self.hook.connected_tabs.remove(self)
                        return

                    self.hook.mark_target_ready(self.id)
                    self.init_done = True
            """,
            "Inizializzazione tab CSS Loader 2.1.2 non riconosciuta.");

        source = ApplyPatch(source,
            """
                    self.ws_response : List[asyncio.Queue] = []
                    self.connected_tabs : List[BrowserTabHook] = []

                    asyncio.create_task(self.on_new_tab())
            """,
            """
                    self.ws_response : List[asyncio.Queue] = []
                    self.connected_tabs : List[BrowserTabHook] = []
                    self.pending_targets = {}
                    self.failed_targets = {}

                    asyncio.create_task(self.on_new_tab())
            """,
            "Stato browser hook CSS Loader 2.1.2 non riconosciuto.");

        source = ApplyPatch(source,
            """
                def get_id(self) -> int:
                    self.current_id += 1
                    return self.current_id

                async def open_websocket(self):
            """,
            """
                def get_id(self) -> int:
                    self.current_id += 1
                    return self.current_id

                def is_supported_target(self, target_info : dict) -> bool:
                    if target_info.get("type") != "page":
                        return False

                    title = target_info.get("title", "")
                    url = target_info.get("url", "")
                    if url.startswith("https://steamloopback.host/") or url.startswith("http://steamloopback.host/"):
                        return True
                    if url.startswith("about:blank?createflags="):
                        return True

                    return url.startswith("about:blank?browserviewpopup=1") and SUPPORTED_POPUP_TITLE.match(title) != None

                def can_attach_target(self, target_id : str) -> bool:
                    now = time.monotonic()
                    pending_until = self.pending_targets.get(target_id)
                    if pending_until != None:
                        if pending_until > now:
                            return False
                        del self.pending_targets[target_id]

                    return target_id not in self.failed_targets

                def mark_target_failed(self, target_id : str, title : str, url : str):
                    self.pending_targets.pop(target_id, None)
                    self.failed_targets[target_id] = (title, url)
                    Log(f"Target {target_id} quarantined after initialization failure")

                def mark_target_ready(self, target_id : str):
                    self.pending_targets.pop(target_id, None)
                    self.failed_targets.pop(target_id, None)

                async def attach_target(self, target_info : dict):
                    target_id = target_info.get("targetId", "")
                    if not target_id or not self.is_supported_target(target_info) or not self.can_attach_target(target_id):
                        return

                    self.pending_targets[target_id] = time.monotonic() + 10
                    try:
                        await self.send_command("Target.attachToTarget", {"targetId": target_id, "flatten": True}, None, False)
                    except:
                        self.pending_targets.pop(target_id, None)
                        raise

                async def open_websocket(self):
            """,
            "Metodi browser hook CSS Loader 2.1.2 non riconosciuti.");

        source = ApplyPatch(source,
            """
                async def close_websocket(self):
                    self.connected_tabs.clear()
                    await self.websocket.close()
            """,
            """
                async def close_websocket(self):
                    self.connected_tabs.clear()
                    self.pending_targets.clear()
                    await self.websocket.close()
            """,
            "Chiusura browser hook CSS Loader 2.1.2 non riconosciuta.");

        source = ApplyPatch(source,
            """
                async def _tab_exists(self, tab_id : str):
                    result = await self.send_command("Target.getTargets", {}, None)
                    return tab_id in [x["targetId"] for x in result["result"]["targetInfos"] if x["type"] == "page"]
            """,
            """
                async def _tab_exists(self, tab_id : str):
                    result = await self.send_command("Target.getTargets", {}, None)
                    if result == None:
                        return False
                    return tab_id in [x["targetId"] for x in result["result"]["targetInfos"] if self.is_supported_target(x)]
            """,
            "Discovery tab CSS Loader 2.1.2 non riconosciuta.");

        source = ApplyPatch(source,
            """
                        if "method" in message and message["method"] == "Target.targetCreated":
                            if message["params"]["targetInfo"]["type"] != "page":
                                continue

                            if not await self._tab_exists(message["params"]["targetInfo"]["targetId"]):
                                continue

                            await self.send_command("Target.attachToTarget", {"targetId": message["params"]["targetInfo"]["targetId"], "flatten": True}, None, False)
            """,
            """
                        if "method" in message and message["method"] == "Target.targetCreated":
                            target_info = message["params"]["targetInfo"]
                            if not self.is_supported_target(target_info):
                                continue

                            if not await self._tab_exists(target_info["targetId"]):
                                continue

                            await self.attach_target(target_info)
            """,
            "Evento creazione tab CSS Loader 2.1.2 non riconosciuto.");

        source = ApplyPatch(source,
            """
                        if "method" in message and message["method"] == "Target.targetInfoChanged":
                            target_info = message["params"]["targetInfo"]

                            if not await self._tab_exists(message["params"]["targetInfo"]["targetId"]):
                                continue
            """,
            """
                        if "method" in message and message["method"] == "Target.targetInfoChanged":
                            target_info = message["params"]["targetInfo"]
                            target_id = target_info["targetId"]

                            if not self.is_supported_target(target_info) or not await self._tab_exists(target_id):
                                continue

                            failed_info = self.failed_targets.get(target_id)
                            if failed_info != None and failed_info != (target_info.get("title", ""), target_info.get("url", "")):
                                self.failed_targets.pop(target_id, None)
                                await self.attach_target(target_info)
            """,
            "Evento aggiornamento tab CSS Loader 2.1.2 non riconosciuto.");

        source = ApplyPatch(source,
            """
                        if "method" in message and message["method"] == "Target.attachedToTarget":
                            self.connected_tabs.append(BrowserTabHook(self, message["params"]["sessionId"], message["params"]["targetInfo"]))
            """,
            """
                        if "method" in message and message["method"] == "Target.attachedToTarget":
                            target_info = message["params"]["targetInfo"]
                            target_id = target_info["targetId"]
                            self.pending_targets.pop(target_id, None)
                            if not self.is_supported_target(target_info):
                                await self.send_command("Target.detachFromTarget", {"sessionId": message["params"]["sessionId"]}, None, False)
                                continue
                            if target_id in [x.id for x in self.connected_tabs]:
                                continue
                            self.connected_tabs.append(BrowserTabHook(self, message["params"]["sessionId"], target_info))
            """,
            "Evento attach tab CSS Loader 2.1.2 non riconosciuto.");

        const string oldSanity = """
                async def sanity_check_tabs(self):
                    while True:
                        try:
                            result = await self.send_command("Target.getTargets", {}, None, True)
                            target_infos = result["result"]["targetInfos"]
                            target_ids = [x["targetId"] for x in target_infos if x["type"] == "page"]
                            for x in self.connected_tabs: # Remove tabs that are no longer connected
                                if x.id not in target_ids:
                                    Log(f"Disconnected from tab: {x.title}")
                                    self.connected_tabs.remove(x)

                            connected_ids = [x.id for x in self.connected_tabs]
                            for x in target_infos:
                                if x["targetId"] not in connected_ids: # Attach tabs that are not connected
                                    await self.send_command("Target.attachToTarget", {"targetId": x["targetId"], "flatten": True}, None, False)
                                else:
                                    for connected_tab in self.connected_tabs: # Update info on tabs that are connected
                                        if connected_tab.id == x["targetId"]:
                                            reinject = False
                                            if (x["title"] != connected_tab.title):
                                                connected_tab.title = x["title"]
                                                reinject = True

                                            if (x["url"] != connected_tab.url):
                                                connected_tab.url = x["url"]
                                                reinject = True

                                            if reinject:
                                                asyncio.create_task(connected_tab.force_reinject())

                                            break
                        except:
                            pass

                        await asyncio.sleep(5)
            """;
        const string newSanity = """
                async def sanity_check_tabs(self):
                    while True:
                        try:
                            result = await self.send_command("Target.getTargets", {}, None, True)
                            if result == None:
                                await asyncio.sleep(5)
                                continue
                            target_infos = result["result"]["targetInfos"]
                            all_target_ids = [x["targetId"] for x in target_infos]
                            for target_id in list(self.failed_targets):
                                if target_id not in all_target_ids:
                                    del self.failed_targets[target_id]

                            supported_targets = [x for x in target_infos if self.is_supported_target(x)]
                            target_ids = [x["targetId"] for x in supported_targets]
                            for x in list(self.connected_tabs): # Remove tabs that are no longer connected
                                if x.id not in target_ids:
                                    Log(f"Disconnected from tab: {x.title}")
                                    self.connected_tabs.remove(x)

                            connected_ids = [x.id for x in self.connected_tabs]
                            for x in supported_targets:
                                if x["targetId"] not in connected_ids: # Attach tabs that are not connected
                                    await self.attach_target(x)
                                else:
                                    for connected_tab in self.connected_tabs: # Update info on tabs that are connected
                                        if connected_tab.id == x["targetId"]:
                                            reinject = False
                                            if (x["title"] != connected_tab.title):
                                                connected_tab.title = x["title"]
                                                reinject = True

                                            if (x["url"] != connected_tab.url):
                                                connected_tab.url = x["url"]
                                                reinject = True

                                            if reinject:
                                                asyncio.create_task(connected_tab.force_reinject())

                                            break
                        except:
                            pass

                        await asyncio.sleep(5)
            """;
        source = ApplyPatch(source, oldSanity, newSanity, "Controllo tab CSS Loader 2.1.2 non riconosciuto.");

        File.WriteAllText(file, source, new UTF8Encoding(false));
    }

    private static async Task<Release> ResolveLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/repos/DeckThemes/SDH-CssLoader/releases/latest");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
        {
            throw new InvalidOperationException("Limite GitHub raggiunto; riprova più tardi.");
        }
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var version = json.RootElement.GetProperty("tag_name").GetString() ?? "";
        var publishedAssets = json.RootElement.GetProperty("assets").EnumerateArray().ToList();
        var assets = publishedAssets
            .Where(asset => string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (assets.Count == 0)
        {
            assets = publishedAssets.Where(asset =>
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var normalized = name.Replace("-", "", StringComparison.Ordinal)
                    .Replace("_", "", StringComparison.Ordinal);
                return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                       normalized.Contains("cssloader", StringComparison.OrdinalIgnoreCase) &&
                       normalized.Contains("decky", StringComparison.OrdinalIgnoreCase) &&
                       !normalized.Contains("source", StringComparison.OrdinalIgnoreCase) &&
                       !normalized.Contains("standalone", StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }
        if (assets.Count != 1)
        {
            throw new InvalidDataException(assets.Count == 0
                ? "La release ufficiale non contiene un unico pacchetto ZIP CSS Loader per Decky."
                : "La release contiene più pacchetti ZIP CSS Loader per Decky: selezione ambigua, installazione bloccata.");
        }

        var selected = assets[0];
        var selectedName = selected.GetProperty("name").GetString() ?? "";
        var url = selected.GetProperty("browser_download_url").GetString() ?? "";
        if (!IsOfficialDownload(url, version, selectedName))
        {
            throw new InvalidDataException("La release ha restituito un URL inatteso.");
        }
        var digest = selected.TryGetProperty("digest", out var digestElement)
            ? digestElement.GetString() ?? ""
            : "";
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) digest = digest[7..];
        if (!IsSha256(digest) &&
            string.Equals(selectedName, AssetName, StringComparison.OrdinalIgnoreCase) &&
            PinnedReleaseHashes.TryGetValue(version, out var pinned)) digest = pinned;
        var size = selected.TryGetProperty("size", out var sizeElement) ? sizeElement.GetInt64() : 0;
        return new(version, selectedName, url, digest, size);
    }

    private static async Task DownloadAsync(
        Release release,
        string destination,
        IProgress<(double Percent, string Status)>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendDownloadRequestAsync(release.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var declared = response.Content.Headers.ContentLength ?? release.Size;
        if (declared <= 0 || declared > MaximumPackageBytes)
        {
            throw new InvalidDataException("Dimensione del pacchetto non valida.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 64, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 64];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaximumPackageBytes) throw new InvalidDataException("Pacchetto troppo grande.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            hash.AppendData(buffer, 0, read);
            var fraction = Math.Clamp((double)total / declared, 0, 1);
            progress?.Report((0.08 + fraction * 0.5, $"Scarico CSS Loader · {Math.Round(fraction * 100)}%"));
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(true);

        if (release.Size > 0 && total != release.Size) throw new InvalidDataException("Dimensione download non coerente.");
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(release.Sha256)))
        {
            throw new CryptographicException("Hash SHA-256 del pacchetto non valido.");
        }
    }

    private static async Task<HttpResponseMessage> SendDownloadRequestAsync(
        string downloadUrl,
        CancellationToken cancellationToken)
    {
        var current = new Uri(downloadUrl);
        for (var redirect = 0; redirect < 6; redirect++)
        {
            if (current.Scheme != Uri.UriSchemeHttps ||
                !(current.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                  current.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Redirect verso un host non consentito.");
            }
            var response = await Client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, current),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
            {
                var next = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current, response.Headers.Location);
                response.Dispose();
                current = next;
                continue;
            }
            return response;
        }
        throw new InvalidDataException("Troppi redirect durante il download.");
    }

    private static async Task ExtractAsync(
        string zipPath,
        string stage,
        IProgress<(double Percent, string Status)>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count is < 5 or > 10_000) throw new InvalidDataException("Archivio non valido.");
        var root = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        long total = archive.Entries.Sum(entry => entry.Length);
        if (total <= 0 || total > 250L * 1024 * 1024) throw new InvalidDataException("Contenuto espanso non valido.");
        long written = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(stage, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Percorso non sicuro nel pacchetto.");
            }
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000) throw new InvalidDataException("Collegamenti simbolici non consentiti.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 64, true);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            written += entry.Length;
            var fraction = Math.Clamp((double)written / total, 0, 1);
            progress?.Report((0.64 + fraction * 0.26, $"Preparo CSS Loader · {Math.Round(fraction * 100)}%"));
        }
    }

    private static string ResolvePluginRoot(string? value) => string.IsNullOrWhiteSpace(value)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "homebrew", "plugins")
        : Path.GetFullPath(value);

    private static bool IsOfficialDownload(string url, string version, string assetName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var expected = $"/{Repository}/releases/download/{version}/";
        return uri.AbsolutePath.StartsWith(expected, StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.EndsWith("/" + assetName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static string NormalizeVersion(string value) => value.Trim().TrimStart('v', 'V');

    private static void DeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void DeleteManagedDirectory(string path, string pluginRoot)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            var root = Path.GetFullPath(pluginRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(full, recursive: true);
        }
        catch
        {
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.4 (+https://github.com/Lozaz/AIO-Decky-for-Windows)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
