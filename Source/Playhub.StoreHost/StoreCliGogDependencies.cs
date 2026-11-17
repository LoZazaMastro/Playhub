// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    private sealed record GogDependencyReceipt(int Version, string ProductId, string InstallPath, Guid OwnerJob,
        string GameManifestHash, IReadOnlyList<StoreOwnedInstallFile> Files);
    private string GogDependencyReceiptPath(string id) => Path.Combine(root, "dependencies-owned-" + id + ".json");
    private static string GogMetadataHash(JsonElement metadata) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata.GetRawText())));

    private async Task<IReadOnlyList<StoreOwnedInstallFile>> ReadGogDependencyFiles(JsonElement metadata, CancellationToken token,
        bool globalPrerequisites = false, List<StorePrerequisiteStep>? installers = null, List<string>? selectedIds = null)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (metadata.TryGetProperty("dependencies", out var declared))
        {
            if (declared.ValueKind != JsonValueKind.Array || declared.GetArrayLength() > 256) throw new StoreOperationException("gog_dependency_manifest_invalid");
            foreach (var item in declared.EnumerateArray()) ids.Add(item.GetString() ?? throw new StoreOperationException("gog_dependency_manifest_invalid"));
        }
        else if (metadata.TryGetProperty("product", out var product) && product.TryGetProperty("depots", out var depots))
            foreach (var item in depots.EnumerateArray()) if (item.TryGetProperty("redist", out var redist)) ids.Add(redist.GetString() ?? throw new StoreOperationException("gog_dependency_manifest_invalid"));
        if (ids.Count == 0) return [];
        foreach (var id in ids) ValidateId(id);
        using var descriptor = await GogPublicMetadata(new("https://content-system.gog.com/dependencies/repository?generation=2"), false, token);
        var address = descriptor.RootElement.GetProperty("repository_manifest").GetString();
        if (!Uri.TryCreate(address, UriKind.Absolute, out var repositoryUri) || repositoryUri.Host != "gog-cdn-fastly.gog.com" ||
            !repositoryUri.AbsolutePath.StartsWith("/content-system/v2/dependencies/meta/", StringComparison.Ordinal)) throw new StoreOperationException("gog_dependency_manifest_invalid");
        using var repository = await GogPublicMetadata(repositoryUri, true, token);
        var entries = repository.RootElement.GetProperty("depots");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 4096) throw new StoreOperationException("gog_dependency_manifest_invalid");
        var found = new HashSet<string>(StringComparer.Ordinal); var files = new List<StoreOwnedInstallFile>();
        foreach (var depot in entries.EnumerateArray())
        {
            var id = depot.GetProperty("dependencyId").GetString()!;
            if (!ids.Contains(id)) continue;
            if (!found.Add(id)) throw new StoreOperationException("gog_dependency_manifest_invalid");
            var executable = depot.GetProperty("executable").GetProperty("path").GetString() ?? "";
            // gogdl's game-deps-only mode downloads bundled runtime payloads (e.g.
            // DOSBox) into this game. Global __redist setup programs are separate
            // machine prerequisites, not files owned by this game/uninstaller.
            var global = executable.Replace('\\', '/').StartsWith("__redist/", StringComparison.OrdinalIgnoreCase);
            if (global != globalPrerequisites) continue;
            // ISI interprets per-game scripts and is handled by the support-data
            // adapter. Starting it without a declared game recipe is not setup.
            if (globalPrerequisites && id == "ISI") continue;
            if (globalPrerequisites)
            {
                var arguments = depot.GetProperty("executable").GetProperty("arguments").GetString() ?? "";
                installers!.Add(new(executable.Replace('\\', '/'), SplitWindowsArguments(arguments), [0, 1641, 3010]));
                selectedIds!.Add(id);
            }
            var hash = depot.GetProperty("manifest").GetString()!;
            if (hash.Length != 32 || !hash.All(Uri.IsHexDigit)) throw new StoreOperationException("gog_dependency_manifest_invalid");
            using var manifest = await GogPublicMetadata(new($"https://gog-cdn-fastly.gog.com/content-system/v2/dependencies/meta/{hash[..2]}/{hash.Substring(2, 2)}/{hash}"), true, token);
            var rows = manifest.RootElement.GetProperty("depot").GetProperty("items");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 100000) throw new StoreOperationException("gog_dependency_manifest_invalid");
            foreach (var row in rows.EnumerateArray())
            {
                if (row.GetProperty("type").GetString() != "DepotFile") continue;
                if (row.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Array && flags.EnumerateArray().Any(f => f.GetString() == "support")) throw new StoreOperationException("gog_dependency_manifest_invalid");
                var name = row.GetProperty("path").GetString()!.TrimStart('/');
                var chunks = row.GetProperty("chunks").EnumerateArray().Select(c => new StoreOwnedInstallChunk(c.GetProperty("size").GetInt64(), c.GetProperty("md5").GetString()!)).ToArray();
                var size = chunks.Sum(c => c.Size); StoreOwnedInstallFile file;
                if (row.TryGetProperty("sha256", out var sha) && sha.ValueKind == JsonValueKind.String) file = new(name, size, "SHA256", sha.GetString());
                else if (row.TryGetProperty("md5", out var md5) && md5.ValueKind == JsonValueKind.String) file = new(name, size, "MD5", md5.GetString());
                else if (chunks.Length == 0) file = new(name, 0, "MD5", "D41D8CD98F00B204E9800998ECF8427E");
                else file = new(name, size, null, null, chunks);
                StoreOwnedInstallFiles.ValidateChecksum(file); files.Add(file);
                if (files.Count > 100000) throw new StoreOperationException("gog_dependency_manifest_invalid");
            }
        }
        if (!found.SetEquals(ids)) throw new StoreOperationException("gog_dependency_manifest_unavailable");
        return files;
    }
    private async Task<JsonDocument> GogPublicMetadata(Uri uri, bool compressed, CancellationToken token)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.Host is not ("content-system.gog.com" or "gog-cdn-fastly.gog.com")) throw new StoreOperationException("gog_dependency_manifest_invalid");
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri != uri) throw new StoreOperationException("gog_dependency_manifest_unavailable");
        await using var input = await response.Content.ReadAsStreamAsync(token); using var encoded = new MemoryStream(); await CopyBounded(input, encoded, token); encoded.Position = 0;
        if (compressed)
        {
            // Dependency CDN identifiers are MD5 of the compressed manifest.
            var hash = uri.Segments[^1];
            if (hash.Length != 32 || !hash.All(Uri.IsHexDigit) || !StringComparer.OrdinalIgnoreCase.Equals(hash, Convert.ToHexString(MD5.HashData(encoded)))) throw new StoreOperationException("gog_dependency_manifest_invalid");
            encoded.Position = 0; using var plain = new MemoryStream(); using var zlib = new ZLibStream(encoded, CompressionMode.Decompress, true); await CopyBounded(zlib, plain, token);
            return JsonDocument.Parse(plain.ToArray());
        }
        return JsonDocument.Parse(encoded.ToArray());
    }
    private void RetainGogDependencies(string id, string path, Guid ownerJob, JsonElement metadata, IReadOnlyList<StoreOwnedInstallFile> files, CancellationToken token)
    {
        if (files.Count == 0) return;
        path = new StoreInstallFileSystem().CanonicalDestination(path);
        using var rootPins = StoreOwnedInstallFiles.PinRoot(path);
        using var owner = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(path, ".playhub-install-owner"));
        using var reader = new StreamReader(owner, leaveOpen: true);
        if (ownerJob == Guid.Empty || owner.Length > 64 || reader.ReadToEnd() != ownerJob.ToString("D")) throw new StoreOperationException("destination_ownership_lost");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var full = StoreOwnedInstallFiles.PathFor(path, file.RelativePath);
            if (!names.Add(full) || Path.GetFileName(full).StartsWith(".playhub", StringComparison.OrdinalIgnoreCase)) throw new StoreOperationException("gog_dependency_manifest_invalid");
            using var pins = StoreOwnedInstallFiles.PinRoot(Path.GetDirectoryName(full)!);
            using var stream = StoreOwnedInstallFiles.OpenReadFile(full); StoreOwnedInstallFiles.VerifyContents(stream, file, token);
        }
        var receipt = new GogDependencyReceipt(1, id, path, ownerJob, GogMetadataHash(metadata), files);
        var receiptPath = GogDependencyReceiptPath(id); var temporary = receiptPath + ".new-" + Guid.NewGuid().ToString("N");
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(stream, receipt); stream.Flush(true); } File.Move(temporary, receiptPath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private IReadOnlyList<StoreOwnedInstallFile> ReadOwnedGogDependencies(string id, string path, Guid ownerJob, JsonElement metadata)
    {
        var receiptPath = GogDependencyReceiptPath(id); if (!File.Exists(receiptPath)) return [];
        using var stream = StoreOwnedInstallFiles.OpenReadFile(receiptPath);
        if (stream.Length is < 1 or > 16 * 1024 * 1024) throw new StoreOperationException("gog_dependency_receipt_invalid");
        var receipt = JsonSerializer.Deserialize<GogDependencyReceipt>(stream) ?? throw new StoreOperationException("gog_dependency_receipt_invalid");
        if (receipt.Version != 1 || receipt.ProductId != id || receipt.OwnerJob != ownerJob || !StringComparer.OrdinalIgnoreCase.Equals(receipt.InstallPath, path) || receipt.GameManifestHash != GogMetadataHash(metadata) || receipt.Files == null || receipt.Files.Count is < 1 or > 100000) throw new StoreOperationException("gog_dependency_receipt_invalid");
        foreach (var file in receipt.Files) { _ = StoreOwnedInstallFiles.PathFor(path, file.RelativePath); StoreOwnedInstallFiles.ValidateChecksum(file); }
        return receipt.Files;
    }
}
