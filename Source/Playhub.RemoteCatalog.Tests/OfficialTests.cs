using Playhub.Models;
using Playhub.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Security.Cryptography;

internal static class OfficialTests
{
    private static byte[] BasicPackage(string name, string folder, string repo)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(folder + "/plugin.json").Open())) writer.Write(JsonSerializer.Serialize(new { name }));
            using (var writer = new StreamWriter(zip.CreateEntry(folder + "/package.json").Open())) writer.Write(JsonSerializer.Serialize(new { homepage = "https://github.com/" + repo }));
        }
        return bytes.ToArray();
    }
    private static readonly byte[] AliasBytes = BasicPackage("NativeAlias", "AliasDisplay", "author/AliasDisplay");
    private static readonly byte[] NewBytes = BasicPackage("NewPlugin", "NewPlugin", "author/NewPlugin");
    private static readonly string AliasPin = Convert.ToHexString(SHA256.HashData(AliasBytes)).ToLowerInvariant();
    private static readonly string NewPin = Convert.ToHexString(SHA256.HashData(NewBytes)).ToLowerInvariant();
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); _checks++; Console.WriteLine("PASS official: " + message); }
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static RemotePluginCatalogEntry Identity(string name, int id = 0) => new()
    {
        Name = name, InstallFolder = name, Repository = "author/" + name, RepositoryUrl = "https://github.com/author/" + name,
        Author = "author", Version = "1.0", Category = "Utility", ShortDescription = "Curated", LongDescription = "Curated long",
        CatalogSource = id > 0 ? "decky-store" : "outside-store", CatalogStatus = id > 0 ? "decky" : "github", CatalogPluginId = id,
        ReleaseAssetName = id > 0 ? new string('a', 64) + ".zip" : "plugin.zip",
        CatalogReleaseUrl = id > 0 ? "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/" + new string('a', 64) + ".zip" : "https://github.com/author/" + name + "/releases/download/v1/plugin.zip",
        Compatibility = "Existing compatibility", Aliases = new[] { name }
    };
    private static readonly RemotePluginCatalog Local = new() { CatalogRevision = 1, Plugins = new[]
    {
        Identity("Known", 1), Identity("AliasDisplay") with { Aliases = new[] { "NativeAlias" } }, Identity("Extra")
    }};
    private static object Item(int id, string name, string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", string created = "2026-10-10T00:00:00Z", string version = "2.0") => new
    {
        id, name, author = "Official Author", description = "Fresh description", visible = true,
        image_url = "https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/artifact_images/image.jpg",
        versions = new[] { new { name = version, hash = hash == new string('a', 64) ? name == "NativeAlias" ? AliasPin : name == "NewPlugin" ? NewPin : hash : hash, created } }
    };

    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Playhub.OfficialCatalog.Tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow; var handler = new Handler(); using var http = new HttpClient(handler);
        var path = Path.Combine(directory, "catalog.json");
        RemotePluginCatalogService Service() => new(http, path, () => now, TimeSpan.FromSeconds(2));
        try
        {
            var service = Service();
            var first = await service.RefreshAsync(Local);
            Check(first.Catalog.Plugins.Count == 3 && first.Catalog.Plugins.Count(entry => entry.CatalogSource == "decky-store") == 2, "official IDs promote aliases without duplicating curated identity");
            var alias = first.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 2);
            Check(alias.Name == "AliasDisplay" && alias.InstallFolder == "AliasDisplay" && alias.Repository == "author/AliasDisplay" && alias.Compatibility == "Existing compatibility", "promotion preserves display/install/repository and known compatibility");
            Check(alias.Version == "2.0" && alias.ReleaseAssetName == AliasPin + ".zip" && alias.CatalogReleaseUrl.Contains("/versions/"), "official hash and version determine exact CDN artifact");
            var calls = handler.OfficialCalls;
            var cached = await service.RefreshAsync(Local);
            Check(handler.OfficialCalls == calls && cached.ContentVersion == first.ContentVersion, "bounded TTL reuses complete content and fingerprint");
            handler.Items = new[] { Item(1, "Known"), Item(2, "NativeAlias"), Item(3, "NewPlugin") };
            var added = await service.RefreshAsync(Local, forceRefresh: true);
            var fresh = added.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 3);
            Check(fresh.Repository == "author/NewPlugin" && fresh.InstallFolder == "NewPlugin" && added.Catalog.Plugins.Count == 4, "new official plugin requires registry plus matching upstream manifest");
            Check(added.ContentVersion != first.ContentVersion && handler.ManifestCalls == 1, "official additions change content version without Playhub revision bump");
            handler.Items = new[] { Item(2, "NativeAlias"), Item(3, "NewPlugin") };
            var removed = await service.RefreshAsync(Local, forceRefresh: true);
            Check(removed.Catalog.Plugins.All(entry => entry.CatalogPluginId != 1) && removed.Catalog.Plugins.Any(entry => entry.Name == "Extra"), "fresh official removal leaves curated extras intact");
            var officialCache = File.ReadAllBytes(path + ".decky-v1.json");
            handler.Status = HttpStatusCode.ServiceUnavailable;
            var offline = await service.RefreshAsync(Local, forceRefresh: true);
            Check(offline.Catalog.Plugins.Count == removed.Catalog.Plugins.Count && offline.Error is not null && offline.Origin == "cache" && File.ReadAllBytes(path + ".decky-v1.json").SequenceEqual(officialCache), "failed source retains last-good membership and durable bytes with truthful fallback");
            calls = handler.OfficialCalls; await service.RefreshAsync(Local);
            Check(handler.OfficialCalls == calls, "offline retry is bounded");
            var restarted = await Service().LoadCachedAsync(Local);
            Check(handler.OfficialCalls == calls && restarted.ContentVersion == removed.ContentVersion, "restart cached baseline is offline and preserves dynamic membership");
            handler.Status = HttpStatusCode.OK; handler.Items = new[] { Item(2, "NativeAlias"), Item(3, "NewPlugin"), Item(1, "Known", "not-a-digest") };
            var invalid = await service.RefreshAsync(Local, forceRefresh: true);
            Check(invalid.Error is not null && invalid.Catalog.Plugins.All(entry => entry.CatalogPluginId != 1) &&
                invalid.Catalog.Plugins.Count(entry => entry.CatalogSource == "decky-store") == 2, "malformed previously removed entry cannot resurrect an unverified baseline identity while valid entries refresh");
            handler.Items = new[] { Item(1, "Known"), Item(1, "Known") };
            Check((await service.RefreshAsync(Local, forceRefresh: true)).ContentVersion == invalid.ContentVersion, "duplicate IDs reject structurally ambiguous snapshot");
            handler.Items = new[] { Item(1, "Known"), Item(4, "UnmappedName") };
            var unmapped = await service.RefreshAsync(Local, forceRefresh: true);
            Check(unmapped.Error is not null && unmapped.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 1) && unmapped.Catalog.Plugins.All(entry => entry.CatalogPluginId != 4), "unmapped official name never invents repository or stalls valid official updates");
            handler.Items = new[] { Item(1, "Known"), Item(3, "NewPlugin") }; handler.ManifestName = "Wrong Title";
            // A new service has no resolved identity for NewPlugin; remove only its owned cache fixture.
            File.Delete(path + ".decky-v1.json");
            Check((await Service().RefreshAsync(Local, forceRefresh: true)).Catalog.Plugins.Single(entry => entry.CatalogPluginId == 3).Repository == "author/NewPlugin", "mismatched upstream registry manifest cannot override actual pinned package identity");
            handler.ManifestName = "NewPlugin"; handler.Registry = "[submodule \"plugins/NewPlugin\"]\n path = plugins/../NewPlugin\n url = https://github.com/author/NewPlugin.git\n";
            Check((await Service().RefreshAsync(Local, forceRefresh: true)).Catalog.Plugins.Single(entry => entry.CatalogPluginId == 3).InstallFolder == "NewPlugin", "registry path traversal cannot become install folder even when pinned package fallback succeeds");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); calls = handler.OfficialCalls;
            try { await service.RefreshAsync(Local, cancelled.Token, forceRefresh: true); throw new Exception("Cancellation accepted"); }
            catch (OperationCanceledException) { Check(handler.OfficialCalls == calls, "pre-cancelled fresh request sends no HTTP"); }
            Check(handler.Urls.All(url => !url.Contains("api.github.com", StringComparison.Ordinal)), "membership and new identity resolution use no GitHub API quota");
            handler.Registry = "";
            byte[] Package(string name, string folder, string repo, string? extra = null, bool symlink = false)
            {
                using var bytes = new MemoryStream();
                using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                {
                    void Add(string path, string body) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(body); }
                    Add(folder + "/plugin.json", JsonSerializer.Serialize(new { name }));
                    Add(folder + "/package.json", JsonSerializer.Serialize(new { homepage = "https://github.com/" + repo + "#readme" }));
                    if (extra is not null) Add(extra, "fixture");
                    if (symlink) zip.CreateEntry("linked/").ExternalAttributes = unchecked((int)0xA1FF0000);
                }
                return bytes.ToArray();
            }
            string Pin(byte[] bytes) { var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); handler.Packages[hash] = bytes; return hash; }
            var zipPin = Pin(Package("ZipOnly", "NativeZip", "zip-owner/ZipOnly"));
            handler.Items = new[] { Item(1, "Known"), Item(5, "ZipOnly", zipPin) };
            var zipResolved = await service.RefreshAsync(Local, forceRefresh: true);
            Check(zipResolved.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 5).Repository == "zip-owner/ZipOnly" && zipResolved.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 5).InstallFolder == "NativeZip", "hash-pinned official ZIP manifest resolves new identity absent from registry");
            var renamedPin = Pin(Package("ZipOnly", "RenamedNativeZip", "zip-owner/ZipOnly"));
            handler.Items = new[] { Item(1, "Known"), Item(5, "ZipOnly", renamedPin) };
            var renamedPackage = await service.RefreshAsync(Local, forceRefresh: true);
            Check(renamedPackage.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 5).InstallFolder == "RenamedNativeZip", "hash change for exact cached ID revalidates native folder from new pinned package");
            handler.Items = new[] { Item(1, "Known"), Item(5, "ZipOnly", new string('b', 64)) };
            var failedChangedPin = await service.RefreshAsync(Local, forceRefresh: true);
            Check(failedChangedPin.Error is not null && failedChangedPin.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 5).ReleaseAssetName == renamedPin + ".zip" && failedChangedPin.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 5).InstallFolder == "RenamedNativeZip", "unavailable changed pin retains last accepted exact artifact and native identity");
            handler.Items = new[] { Item(1, "Known"), Item(11, "NativeAlias", new string('b', 64)) };
            var failedAliasPin = await service.RefreshAsync(Local, forceRefresh: true);
            Check(failedAliasPin.Error is not null && failedAliasPin.Catalog.Plugins.All(entry => entry.CatalogPluginId != 11) && failedAliasPin.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 1), "unavailable new alias pin never assigns its baseline repository to a new official ID");
            handler.Items = new[] { Item(1, "Known"), Item(11, "NativeAlias", Pin(Package("NativeAlias", "DifferentNative", "different-owner/NativeAlias"))) };
            var replacedAlias = await service.RefreshAsync(Local, forceRefresh: true);
            Check(replacedAlias.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 11).InstallFolder == "DifferentNative" && replacedAlias.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 11).Repository == "different-owner/NativeAlias" && replacedAlias.Catalog.Plugins.Any(entry => entry.Repository == "author/AliasDisplay"), "new unique baseline alias ID gets actual pinned repository and folder without taking over curated identity");
            var malformedCover = JsonSerializer.SerializeToElement(Item(1, "Known"));
            var badFields = JsonSerializer.Deserialize<Dictionary<string, object>>(malformedCover.GetRawText())!; badFields["image_url"] = "https://untrusted.example/image.jpg";
            handler.Items = new object[] { badFields, Item(3, "NewPlugin") };
            var isolatedFields = await service.RefreshAsync(Local, forceRefresh: true);
            Check(isolatedFields.Error is not null && isolatedFields.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 3), "invalid per-entry cover cannot abort other valid official membership updates");
            handler.Items = new[] { Item(1, "Known"), Item(5, "ZipOnly", renamedPin) };
            handler.Packages[zipPin] = Package("ZipOnly", "NativeZip", "wrong/repository");
            var changed = await Service().RefreshAsync(Local, forceRefresh: true);
            // Restart may use its already verified cached identity; a genuinely new ID needs pinned bytes.
            handler.Items = new[] { Item(1, "Known"), Item(6, "HashChanged", zipPin) };
            Check((await service.RefreshAsync(Local, forceRefresh: true)).Catalog.Plugins.All(entry => entry.CatalogPluginId != 6), "different downloaded bytes cannot supply new official identity under old pin");
            foreach (var package in new[] { Package("Unsafe", "Unsafe", "zip-owner/Unsafe", "../outside.txt"), Package("Unsafe", "Unsafe", "zip-owner/Unsafe", symlink: true), Package("Unsafe", "Unsafe", "zip-owner/Unsafe", "Other/plugin.json") })
            {
                handler.Items = new[] { Item(1, "Known"), Item(7, "Unsafe", Pin(package)) };
                var rejected = await service.RefreshAsync(Local, forceRefresh: true);
                Check(rejected.Error is not null && rejected.Catalog.Plugins.All(entry => entry.CatalogPluginId != 7) && rejected.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 1), "unsafe or ambiguous pinned ZIP identity is isolated while valid official entries refresh");
            }
            var oldPin = Pin(Package("OldPackage", "SharedNative", "old-owner/OldPackage"));
            var newPin = Pin(Package("NewPackage", "SharedNative", "new-owner/NewPackage"));
            handler.Items = new[] { Item(1, "Known"), Item(8, "OldPackage", oldPin, "2025-01-01T00:00:00Z"), Item(9, "NewPackage", newPin) };
            var superseded = await service.RefreshAsync(Local, forceRefresh: true);
            Check(superseded.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 9) && superseded.Catalog.Plugins.All(entry => entry.CatalogPluginId != 8) && superseded.Error is not null, "same native package folder retains newer confirmed artifact with explicit duplicate diagnostic");
            var duplicatePath = Path.Combine(directory, "duplicate-music.json");
            var duplicateLocal = Local with { Plugins = Local.Plugins.Concat(new[] {
                Identity("SeedOne") with { Aliases = new[] { "Music Control" } },
                Identity("SeedTwo") with { Aliases = new[] { "Music Control" } }
            }).ToArray() };
            RemotePluginCatalogService DuplicateService() => new(http, duplicatePath, () => now, TimeSpan.FromSeconds(2));
            var oldMusicPin = Pin(Package("MusicControl", "MusicControl", "old-owner/music-control"));
            var newMusicPin = Pin(Package("Music Control", "MusicControl", "new-owner/music-control"));
            handler.Items = new[] { Item(12, "MusicControl", oldMusicPin, "2025-01-01T00:00:00Z"), Item(111, "Music Control", newMusicPin) };
            var duplicateFirst = await DuplicateService().RefreshAsync(duplicateLocal);
            Check(duplicateFirst.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 111) && duplicateFirst.Catalog.Plugins.All(entry => entry.CatalogPluginId != 12), "raw duplicate12/111 pins are verified while only canonical newer111 is visible");
            Check(duplicateFirst.Origin == "decky-store" && duplicateFirst.Error is null, "verified official same-name replacement is a successful fresh catalog rather than an error");
            var duplicateCache = File.ReadAllText(duplicatePath + ".decky-identities-v1.json");
            Check(duplicateCache.Contains("\"catalogPluginId\":12", StringComparison.Ordinal) && duplicateCache.Contains("\"catalogPluginId\":111", StringComparison.Ordinal), "identity checkpoint retains both raw duplicate IDs independently of visible deduplication");
            now = now.AddMinutes(3);
            var duplicateBefore = handler.Urls.Count;
            var duplicateWarm = await DuplicateService().RefreshAsync(duplicateLocal);
            var duplicateRequests = handler.Urls.Skip(duplicateBefore).ToArray();
            Check(duplicateWarm.Error is null, "warm verified same-name replacement remains healthy rather than reporting a permanent duplicate error");
            Check(duplicateRequests.SequenceEqual(new[] { RemotePluginCatalogService.OfficialCatalogUrl }) && duplicateWarm.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 111), "restart after official TTL refreshes duplicate metadata with exactly one GET and no registry or ZIP requests");
            duplicateBefore = handler.Urls.Count;
            var duplicateForced = await DuplicateService().RefreshAsync(duplicateLocal, forceRefresh: true);
            duplicateRequests = handler.Urls.Skip(duplicateBefore).ToArray();
            Check(duplicateRequests.Count(url => url == RemotePluginCatalogService.OfficialCatalogUrl) == 1 && duplicateRequests.All(url => url == RemotePluginCatalogService.OfficialCatalogUrl || url == RemotePluginCatalogService.CatalogUrl) && duplicateForced.Catalog.Plugins.All(entry => entry.CatalogPluginId != 12), "forced refresh preserves duplicate canonical identity without registry or ZIP, alongside explicit curated refresh");
            var updatedMusicPin = Pin(Package("Music Control", "MusicControl", "new-owner/music-control", "MusicControl/version.txt"));
            handler.Items = new[] { Item(12, "MusicControl", oldMusicPin, "2025-01-01T00:00:00Z"), Item(111, "Music Control", updatedMusicPin, version: "3.0") };
            var updatedMusic = await DuplicateService().RefreshAsync(duplicateLocal, forceRefresh: true);
            Check(updatedMusic.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 111).Version == "3.0" && handler.PackageCalls[updatedMusicPin] == 1, "new duplicate canonical hash and version require fresh pinned identity verification instead of reusing old cache");
            handler.Items = new[] { Item(12, "MusicControl", oldMusicPin, "2025-01-01T00:00:00Z"), Item(111, "Music Control", updatedMusicPin, version: "3.1") };
            duplicateBefore = handler.Urls.Count;
            var changedVersion = await DuplicateService().RefreshAsync(duplicateLocal, forceRefresh: true);
            Check(changedVersion.Catalog.Plugins.Single(entry => entry.CatalogPluginId == 111).Version == "3.1" && handler.Urls.Skip(duplicateBefore).All(url => url == RemotePluginCatalogService.OfficialCatalogUrl || url == RemotePluginCatalogService.CatalogUrl), "current official version metadata is applied even when the verified artifact pin is unchanged");
            Check(changedVersion.Error is null, "same artifact fresh version remains a successful replacement after cache restart");
            handler.Items = new[] { Item(12, "MusicControl", oldMusicPin, "2025-01-01T00:00:00Z"), Item(111, "Music Control", new string('e', 64), version: "4.0-unverified") };
            var unverifiedReplacement = await DuplicateService().RefreshAsync(duplicateLocal, forceRefresh: true);
            Check(unverifiedReplacement.Error is not null && unverifiedReplacement.Catalog.Plugins.Any(entry => entry.ReleaseAssetName == updatedMusicPin + ".zip") &&
                unverifiedReplacement.Catalog.Plugins.All(entry => entry.Version != "4.0-unverified"), "same-name replacement with an unverified new artifact retains last accepted identity and reports the verification failure");
            handler.Items = new[] { Item(12, "MusicControl", oldMusicPin), Item(111, "Music Control", updatedMusicPin) };
            var tiedReplacement = await DuplicateService().RefreshAsync(duplicateLocal, forceRefresh: true);
            Check(tiedReplacement.Error is not null && tiedReplacement.Origin == "decky-store", "equal-date official folder collision still reports ambiguity while other fresh membership remains available");
            handler.Items = new[] { Item(1, "Known"), Item(10, "CuratedCollision", Pin(Package("CuratedCollision", "Extra", "other-owner/Collision"))) };
            var collision = await service.RefreshAsync(Local, forceRefresh: true);
            Check(collision.Catalog.Plugins.Any(entry => entry.Name == "Extra") && collision.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 1) && collision.Catalog.Plugins.All(entry => entry.CatalogPluginId != 10), "curated folder conflict cannot block other official metadata or take over its destination");
            var resumePath = Path.Combine(directory, "resume.json");
            RemotePluginCatalogService ResumeService() => new(http, resumePath, () => now, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(250));
            handler.Items = new[] { Item(1, "Known"), Item(2, "NativeAlias") };
            var resume = ResumeService(); var warm = await resume.RefreshAsync(Local, forceRefresh: true);
            var beforeDeadline = File.ReadAllBytes(resumePath + ".decky-v1.json");
            var aPin = Pin(Package("ProgressA", "ProgressA", "progress-owner/A"));
            var bPin = Pin(Package("ProgressB", "ProgressB", "progress-owner/B"));
            handler.Items = new[] { Item(1, "Known"), Item(2, "NativeAlias"), Item(20, "ProgressA", aPin), Item(21, "ProgressB", bPin) };
            handler.DelayPin = bPin;
            var interrupted = await resume.RefreshAsync(Local, forceRefresh: true);
            Check(interrupted.ContentVersion == warm.ContentVersion && interrupted.Origin == "cache" &&
                File.ReadAllBytes(resumePath + ".decky-v1.json").SequenceEqual(beforeDeadline) && interrupted.Catalog.Plugins.All(entry => entry.CatalogPluginId != 20), "identity deadline preserves entire accepted membership without publishing partial additions");
            Check(File.Exists(resumePath + ".decky-identities-v1.json") && handler.PackageCalls[aPin] == 1, "verified identity before deadline is durably checkpointed separately from membership");
            handler.DelayPin = null;
            var completed = await ResumeService().RefreshAsync(Local, forceRefresh: true);
            Check(completed.Origin == "decky-store" && completed.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 20) && completed.Catalog.Plugins.Any(entry => entry.CatalogPluginId == 21) && handler.PackageCalls[aPin] == 1, "fresh service restart resumes verified exact pin without redownload and completes pending identity membership");
            Console.WriteLine($"PASS {_checks} official checks.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static async Task LiveAsync()
    {
        var root = Path.Combine(BundledManifest.Root, ".local", "red-code", "official-catalog-freshness-20261010", "live"); Directory.CreateDirectory(root);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        var service = new RemotePluginCatalogService(http, Path.Combine(root, "catalog.json"), () => DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        var result = await service.RefreshAsync(PluginCatalogService.GetBundledCatalog(), forceRefresh: true);
        var receipt = new { result.Origin, result.Error, result.ContentVersion, Count = result.Catalog.Plugins.Count,
            Official = result.Catalog.Plugins.Where(entry => entry.CatalogSource == "decky-store").Select(entry => new { entry.Name, entry.Repository, entry.CatalogPluginId, entry.Version }).ToArray() };
        File.WriteAllText(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { result.Origin, result.Error, Count = result.Catalog.Plugins.Count, Official = receipt.Official.Length }));
        if (receipt.Official.Length != 109 || result.Origin != "decky-store" || !receipt.Official.Any(entry => entry.CatalogPluginId == 117 && entry.Repository == "39sdev/Decky-CP") ||
            !receipt.Official.Any(entry => entry.CatalogPluginId == 111 && entry.Repository.Equals("martinpl/music-control", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Current official catalog was not verified.");
    }

    private sealed class Handler : HttpMessageHandler
    {
        public object[] Items = { Item(1, "Known"), Item(2, "NativeAlias") };
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string ManifestName = "NewPlugin";
        public string Registry = "[submodule \"plugins/NewPlugin\"]\n path = plugins/NewPlugin\n url = https://github.com/author/NewPlugin.git\n";
        public int OfficialCalls, ManifestCalls; public List<string> Urls = new();
        public string? DelayPin;
        public Dictionary<string,int> PackageCalls = new();
        public Dictionary<string, byte[]> Packages = new() { [AliasPin] = AliasBytes, [NewPin] = NewBytes };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith("https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/", StringComparison.Ordinal))
            {
                var pin = Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath);
                PackageCalls[pin] = PackageCalls.GetValueOrDefault(pin) + 1;
                if (pin == DelayPin) await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            return await RespondAsync(request, token);
        }
        private Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); var url = request.RequestUri!.AbsoluteUri; Urls.Add(url);
            if (url == RemotePluginCatalogService.CatalogUrl) return Task.FromResult(Response(Local));
            if (url == RemotePluginCatalogService.OfficialCatalogUrl) { OfficialCalls++; return Task.FromResult(Response(Items, Status)); }
            if (url == RemotePluginCatalogService.OfficialRegistryUrl) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Registry) });
            if (url == "https://raw.githubusercontent.com/author/NewPlugin/HEAD/plugin.json") { ManifestCalls++; return Task.FromResult(Response(new { name = ManifestName })); }
            if (url.StartsWith("https://cdn.tzatzikiweeb.moe/file/steam-deck-homebrew/versions/", StringComparison.Ordinal))
                return Task.FromResult(Packages.TryGetValue(Path.GetFileNameWithoutExtension(request.RequestUri.AbsolutePath), out var bytes)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new HttpResponseMessage(HttpStatusCode.NotFound));
            throw new InvalidOperationException("Unexpected endpoint " + url);
        }
        private static HttpResponseMessage Response(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(body, Options), Encoding.UTF8, "application/json") };
    }
}
