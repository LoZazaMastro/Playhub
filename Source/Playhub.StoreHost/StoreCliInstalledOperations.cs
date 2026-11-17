// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    public Task<bool> CanUninstallAsync(string id, string installPath, Guid ownerJob, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ValidateId(id);
        if (id is "." or "..") return Task.FromResult(false);
        try
        {
            var installed = ReadRemovalInstall(id);
            return Task.FromResult(installed != null &&
                StringComparer.OrdinalIgnoreCase.Equals(new StoreInstallFileSystem().CanonicalDestination(installPath), installed.Path) &&
                ownerJob != Guid.Empty && File.ReadAllText(Path.Combine(installed.Path, ".playhub-install-owner")) == ownerJob.ToString("D") &&
                (Store == StoreKind.Epic || File.Exists(GogManifestPath(id)) || OfficialInstallations.FindGogUninstaller(id,installed.Path,token) is not null));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or StoreOperationException) { return Task.FromResult(false); }
    }

    public async Task<StoreOwnedUninstallPlan> PrepareUninstallAsync(string id, string installPath, Guid ownerJob, CancellationToken token)
    {
        ValidateId(id);
        await WaitForRemovalGateAsync(token);
        try { return await ReadUninstallPlan(id, installPath, ownerJob, token); }
        finally { nativeGate.Release(); }
    }
    private string GogManifestPath(string id) => Path.Combine(Path.GetDirectoryName(nativeAuth)!, "heroic_gogdl", "manifests", id);

    private async Task<StoreOwnedUninstallPlan> ReadUninstallPlan(string id, string path, Guid job, CancellationToken token)
    {
        if (!await CanUninstallAsync(id, path, job, token)) throw new StoreOperationException("installation_ownership_lost");
        path = new StoreInstallFileSystem().CanonicalDestination(path);
        var installed = ReadRemovalInstall(id)!;
        StoreOwnedInstallFiles.RefuseRunning(path);
        RefuseSharedRemoval(id,path,token);
        if(Store==StoreKind.Gog&&!File.Exists(GogManifestPath(id)))return ReadGogOfficialUninstallPlan(id,path,job,token);
        IReadOnlyList<StoreOwnedInstallFile> manifest;
        string metadata;
        if (Store == StoreKind.Epic)
        {
            // Legendary 0.21.1 assigns a default platform in main(), which makes
            // list-files force a CDN/login path even without --force-download.
            // An explicit retained LOCAL manifest selects its offline branch.
            var source=EpicRemovalManifest(id,path,token);var manifestPath = source.Manifest;
            using var manifestPin = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (manifestPin.Length is < 1 or > 32 * 1024 * 1024) throw new StoreOperationException("uninstall_manifest_invalid");
            var manifestHash = Convert.ToHexString(SHA256.HashData(manifestPin));
            var result = await query(tool, ["list-files", id, "--json", "--manifest", manifestPath], environment, token);
            if (result.Code != 0) throw new StoreOperationException("uninstall_manifest_unavailable");
            using var doc = JsonDocument.Parse(result.Json);
            manifest = ReadEpicUninstallFiles(doc.RootElement);
            metadata = source.RegistrationHash+"\n"+manifestHash + "\n" + result.Json;
            // Never de-register another installed product/DLC sharing this tree.
            string recordsPath=Path.Combine(Path.GetDirectoryName(nativeAuth)!, "installed.json");
            using var records = JsonDocument.Parse(File.Exists(recordsPath)?File.ReadAllText(recordsPath):"{}");
            foreach (var item in records.RootElement.EnumerateObject())
                if (item.Name != id && item.Value.TryGetProperty("install_path", out var other) && other.GetString() is { } location &&
                    (StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(location), path) || Path.GetFullPath(location).StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    throw new StoreOperationException("shared_installation_refused");
        }
        else
        {
            var file = GogManifestPath(id);
            if (new FileInfo(file).Length > 16 * 1024 * 1024) throw new StoreOperationException("uninstall_manifest_invalid");
            metadata = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(metadata);
            manifest = await ReadGogUninstallFiles(id, doc.RootElement, token);
            var dependencies = ReadOwnedGogDependencies(id, path, job, doc.RootElement);
            if (dependencies.Count == 0)
            {
                // The official recipe establishes these paths; removal does not
                // require an installed prerequisite to still pass setup checks.
                dependencies = await ReadGogDependencyFiles(doc.RootElement, token);
            }
            manifest = manifest.Concat(dependencies).ToArray();
            manifest = StoreGogSupportData.MergeFiles(manifest, StoreGogSupportData.ReadOwnedFiles(GogSupportReceiptPath(id), path, id, job, manifest));
        }
        manifest=StoreOwnedInstallFiles.BindRemovalFiles(path,manifest,token);
        string executable=manifest.Any(f=>StoreOwnedInstallFiles.PathFor(path,f.RelativePath).Equals(installed.Executable,StringComparison.OrdinalIgnoreCase))?installed.Executable:
            StoreOwnedInstallFiles.PathFor(path,manifest.FirstOrDefault(f=>f.RelativePath.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))?.RelativePath??manifest.First().RelativePath);
        var files = manifest.OrderBy(f => StringComparer.OrdinalIgnoreCase.Equals(StoreOwnedInstallFiles.PathFor(path, f.RelativePath), executable) ? 1 : 0)
            .ThenBy(f => f.RelativePath, StringComparer.Ordinal).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(metadata + "\n" + JsonSerializer.Serialize(files))));
        var plan = new StoreOwnedUninstallPlan(new(Store, id), path, executable, job, fingerprint, Array.AsReadOnly(files));
        StoreOwnedInstallFiles.ValidatePlan(plan);
        return plan;
    }
    internal static IReadOnlyList<StoreOwnedInstallFile> ReadEpicUninstallFiles(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() is < 1 or > 100000) throw new StoreOperationException("uninstall_manifest_invalid");
        return root.EnumerateArray().Select(f => new StoreOwnedInstallFile(f.GetProperty("filename").GetString()!, f.GetProperty("file_size").GetInt64(), "SHA1", f.GetProperty("sha_hash").GetString())).ToArray();
    }

    private string EpicInstalledManifest(string id)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(nativeAuth)!, "installed.json")));
        var record = doc.RootElement.GetProperty(id);
        var version = record.GetProperty("version").GetString();
        var platform = record.TryGetProperty("platform", out var p) ? p.GetString() : "Windows";
        if (string.IsNullOrWhiteSpace(version) || version.Length > 512 || version.Any(char.IsControl) || platform is not ("Windows" or "Win32"))
            throw new StoreOperationException("uninstall_manifest_invalid");
        var directory = new StoreInstallFileSystem().CanonicalDestination(Path.Combine(Path.GetDirectoryName(nativeAuth)!, "manifests"));
        // Exact upstream LGDLFS naming/clean_filename and legacy fallback, never
        // title/mtime/glob selection and never an arbitrary manifest_path field.
        static string Clean(string value) => new(value.Where(c => !"<>:\"/\\|?*".Contains(c)).ToArray());
        foreach (var name in new[] { Clean($"{id}_{platform}_{version}") + ".manifest", Clean($"{id}_{version}") + ".manifest" })
        {
            var file = StoreOwnedInstallFiles.PathFor(directory, name);
            if (!File.Exists(file)) continue;
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new StoreOperationException("destination_reparse_point");
            return file;
        }
        throw new StoreOperationException("uninstall_manifest_unavailable");
    }

    private async Task<IReadOnlyList<StoreOwnedInstallFile>> ReadGogUninstallFiles(string id, JsonElement root, CancellationToken token)
    {
        var v1 = root.TryGetProperty("version", out var version) && version.GetInt32() == 1;
        var product = v1 ? root.GetProperty("product") : root;
        if ((v1 ? product.GetProperty("rootGameID") : product.GetProperty("baseProductId")).ToString() != id)
            throw new StoreOperationException("uninstall_manifest_invalid");
        var language = root.GetProperty("HGLInstallLanguage").GetString();
        if (string.IsNullOrWhiteSpace(language) || language.Length > 32 || !language.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw new StoreOperationException("uninstall_manifest_invalid");
        if (v1 && root.GetProperty("HGLPlatform").GetString() is not ("windows" or "Windows")) throw new StoreOperationException("uninstall_manifest_invalid");
        var depots = product.GetProperty("depots");
        if (depots.GetArrayLength() > 128) throw new StoreOperationException("uninstall_manifest_invalid");
        var files = new List<StoreOwnedInstallFile>();
        foreach (var depot in depots.EnumerateArray())
        {
            if (depot.TryGetProperty("redist", out _)) continue;
            if (v1 ? !depot.GetProperty("gameIDs").EnumerateArray().Any(g => g.ToString() == id) : depot.GetProperty("productId").ToString() != id) continue;
            if (!depot.GetProperty("languages").EnumerateArray().Any(l => l.GetString() is "*" or "Neutral" || l.GetString() == language)) continue;
            if (!v1 && depot.TryGetProperty("osBitness", out var bits) && bits.GetArrayLength() != 0 && !bits.EnumerateArray().Any(b => b.ToString() == "64")) continue;
            var hash = depot.GetProperty("manifest").GetString()!;
            if (hash.Length is < 4 or > 128 || !hash.All(Uri.IsHexDigit)) throw new StoreOperationException("uninstall_manifest_invalid");
            string relative;
            if (v1)
            {
                var timestamp = product.GetProperty("timestamp").ToString();
                if (timestamp.Length > 32 || !timestamp.All(char.IsAsciiDigit)) throw new StoreOperationException("uninstall_manifest_invalid");
                relative = $"content-system/v1/manifests/{id}/windows/{timestamp}/{hash}";
            }
            else relative = $"content-system/v2/meta/{hash[..2]}/{hash.Substring(2, 2)}/{hash}";
            using var response = await http.GetAsync(new Uri("https://gog-cdn-fastly.gog.com/" + relative), HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri?.Host != "gog-cdn-fastly.gog.com") throw new StoreOperationException("uninstall_manifest_unavailable");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var encoded = new MemoryStream(); await CopyBounded(input, encoded, token); encoded.Position = 0;
            using var plain = new MemoryStream();
            if (!v1 && encoded.Length > 0 && encoded.GetBuffer()[0] != (byte)'{')
            { using var zlib = new ZLibStream(encoded, CompressionMode.Decompress, true); await CopyBounded(zlib, plain, token); }
            else { await encoded.CopyToAsync(plain, token); }
            using var doc = JsonDocument.Parse(plain.ToArray()); var data = doc.RootElement.GetProperty("depot");
            var entries = data.GetProperty(v1 ? "files" : "items");
            foreach (var item in entries.EnumerateArray())
            {
                if (v1 ? item.TryGetProperty("directory", out _) : item.GetProperty("type").GetString() != "DepotFile") continue;
                var isSupport = v1 ? item.TryGetProperty("support", out var support) && support.ValueKind == JsonValueKind.True :
                    item.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array && flags.EnumerateArray().Any(f => f.GetString() == "support");
                var name = item.GetProperty("path").GetString()!.TrimStart('/');
                if (isSupport) name = "gog-support/" + (v1 ? name : id + "/" + name);
                var size = v1 ? item.GetProperty("size").GetInt64() : item.GetProperty("chunks").EnumerateArray().Sum(c => c.GetProperty("size").GetInt64());
                var digest = item.TryGetProperty(v1 ? "hash" : "md5", out var md5) && md5.ValueKind == JsonValueKind.String ? md5.GetString() : null;
                StoreOwnedInstallFile entry;
                if (digest != null) entry = new(name, size, "MD5", digest);
                else if (!v1 && item.TryGetProperty("sha256", out var sha256) && sha256.ValueKind == JsonValueKind.String) entry = new(name, size, "SHA256", sha256.GetString());
                else if (!v1 && size == 0 && item.GetProperty("chunks").GetArrayLength() == 0) entry = new(name, 0, "MD5", "D41D8CD98F00B204E9800998ECF8427E");
                else if (!v1) entry = new(name, size, null, null, item.GetProperty("chunks").EnumerateArray().Select(c => new StoreOwnedInstallChunk(c.GetProperty("size").GetInt64(), c.GetProperty("md5").GetString()!)).ToArray());
                else throw new StoreOperationException("uninstall_checksum_unavailable");
                StoreOwnedInstallFiles.ValidateChecksum(entry);
                files.Add(entry);
                if (files.Count > 100000) throw new StoreOperationException("uninstall_manifest_invalid");
            }
        }
        return files;
    }
    private static async Task CopyBounded(Stream input, Stream output, CancellationToken token)
    {
        var buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        { if (output.Length + count > 32L * 1024 * 1024) throw new StoreOperationException("uninstall_manifest_invalid"); await output.WriteAsync(buffer.AsMemory(0, count), token); }
    }

    public async Task UninstallAsync(StoreOwnedUninstallPlan plan, CancellationToken token)
    {
        if (plan.Key.Store != Store) throw new StoreOperationException("store_mismatch");
        await WaitForRemovalGateAsync(token);
        try
        {
            if(Store==StoreKind.Gog&&!File.Exists(GogManifestPath(plan.Key.Id)))
            {await ExecuteGogOfficialUninstall(plan,token);return;}
            var official=Store==StoreKind.Epic?OfficialInstallations.ReadForRemoval(Store,token).Find(plan.Key.Id):null;
            var metadataPins=new List<FileStream>();string? itemHash=null,manifestHash=null,secondaryHash=null;
            if(official is {EpicRegistrationPath:{} item,EpicManifestPath:{} binary}&&SameOfficialPath(plan.InstallPath,official.InstallPath))
            {
                try
                {
                    var itemPin=StoreOwnedInstallFiles.OpenReadFile(item);metadataPins.Add(itemPin);
                    var manifestPin=StoreOwnedInstallFiles.OpenReadFile(binary);metadataPins.Add(manifestPin);
                    itemHash=Convert.ToHexString(SHA256.HashData(itemPin));manifestHash=Convert.ToHexString(SHA256.HashData(manifestPin));
                    if(official.EpicSecondaryRegistrationPath is {} secondary)
                    {var secondaryPin=StoreOwnedInstallFiles.OpenReadFile(secondary);metadataPins.Add(secondaryPin);secondaryHash=Convert.ToHexString(SHA256.HashData(secondaryPin));}
                }
                catch{foreach(var pin in metadataPins)pin.Dispose();throw;}
            }
            try
            {
            var current = await ReadUninstallPlan(plan.Key.Id, plan.InstallPath, plan.OwnerJob, token);
            if (current.ManifestHash != plan.ManifestHash || current.GameExecutable != plan.GameExecutable) throw new StoreOperationException("installation_changed");
            // Execute only the server-retained, fully revalidated manifest.
            StoreOwnedInstallFiles.Delete(current, token);
            if (current.Files.Any(f => File.Exists(StoreOwnedInstallFiles.PathFor(current.InstallPath, f.RelativePath)))) throw new StoreOperationException("uninstall_incomplete");
            }
            finally{foreach(var pin in metadataPins)pin.Dispose();}
            if (Store == StoreKind.Epic)
            {
                // Files are already removed through retained handles. Legendary
                // updates its own installation database without running a game
                // uninstaller, deleting saves or touching any additional file.
                // Complete the small registration commit even if the requesting
                // UI has gone away after all payload files were removed.
                using var commit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var recordsPath=Path.Combine(Path.GetDirectoryName(nativeAuth)!,"installed.json");
                bool registered=false;using(var records=JsonDocument.Parse(File.Exists(recordsPath)?File.ReadAllText(recordsPath):"{}"))registered=records.RootElement.TryGetProperty(plan.Key.Id,out _);
                if(registered)
                {
                    var result = await query(tool, ["--yes", "uninstall", plan.Key.Id, "--keep-files", "--skip-uninstaller"], environment, commit.Token);
                    using var records = JsonDocument.Parse(File.ReadAllText(recordsPath));
                    if (result.Code != 0 || records.RootElement.TryGetProperty(plan.Key.Id, out _)) throw new StoreOperationException("uninstall_metadata_failed");
                }
                if(official is {EpicRegistrationPath:{} nativeItem,EpicManifestPath:{} nativeBinary}&&itemHash is not null&&manifestHash is not null)
                {
                    StoreOwnedInstallFiles.DeleteRegisteredMetadata(nativeItem,itemHash,1024*1024,commit.Token);
                    if(official.EpicSecondaryRegistrationPath is {} secondary&&secondaryHash is not null)
                        StoreOwnedInstallFiles.DeleteRegisteredMetadata(secondary,secondaryHash,262144,commit.Token);
                    StoreOwnedInstallFiles.DeleteRegisteredMetadata(nativeBinary,manifestHash,32*1024*1024,commit.Token);
                    try{Directory.Delete(Path.GetDirectoryName(nativeBinary)!,false);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }
            else
            {
                // gogdl 1.4.0 has no uninstall command. Only our exact local
                // installation registration and that game's manifest are removed.
                File.Delete(Path.Combine(root, "installed-" + plan.Key.Id + ".json"));
                File.Delete(GogManifestPath(plan.Key.Id));
                StoreGogSupportData.RemoveEmptyOwnedDirectories(GogSupportReceiptPath(plan.Key.Id), plan.InstallPath, plan.Key.Id, plan.OwnerJob);
                File.Delete(GogSupportReceiptPath(plan.Key.Id));
                File.Delete(GogDependencyReceiptPath(plan.Key.Id));
            }
            var marker = Path.Combine(plan.InstallPath, ".playhub-install-owner");
            if (File.ReadAllText(marker) != plan.OwnerJob.ToString("D")) throw new StoreOperationException("installation_ownership_lost");
            File.Delete(marker);
            try { Directory.Delete(plan.InstallPath, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        finally { nativeGate.Release(); }
    }
}
