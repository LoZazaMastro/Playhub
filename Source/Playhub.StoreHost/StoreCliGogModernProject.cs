// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    private sealed record GogModernProjectReceipt(int Version, string ProductId, string InstallPath,
        Guid OwnerJob, string GameManifestHash, string ProjectHash);
    private string GogModernProjectReceiptPath(string id) => Path.Combine(root, "modern-project-owned-" + id + ".json");
    private const int MaxGogProjectBytes = 1024 * 1024;

    // scriptInterpreter describes a build format, not a promise that the legacy
    // goggame-ID.script exists. Modern builds can instead publish project.json
    // in offlineDepot. Admit only a verified project with no installation work.
    internal async Task VerifyGogModernProjectAsync(string id, string path, Guid ownerJob, JsonElement metadata, CancellationToken token)
    {
        try
        {
            if (metadata.GetProperty("baseProductId").ToString() != id || !metadata.TryGetProperty("offlineDepot", out var offline) ||
                offline.ValueKind != JsonValueKind.Object || offline.GetProperty("productId").ToString() != id ||
                offline.GetProperty("size").GetInt64() is < 1 or > MaxGogProjectBytes || offline.GetProperty("compressedSize").GetInt64() is < 1 or > MaxGogProjectBytes)
                throw new StoreOperationException("gog_support_script_missing");
            var hash = ProjectDigest(offline, "manifest");
            using var depot = await GogPublicMetadata(new($"https://gog-cdn-fastly.gog.com/content-system/v2/meta/{hash[..2]}/{hash.Substring(2, 2)}/{hash}"), true, token);
            var rows = depot.RootElement.GetProperty("depot").GetProperty("items");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 1) throw new StoreOperationException("gog_support_action_unsupported");
            var file = rows[0];
            if (file.GetProperty("type").GetString() != "DepotFile" || file.GetProperty("path").GetString() != "project.json")
                throw new StoreOperationException("gog_support_action_unsupported");
            var chunks = file.GetProperty("chunks");
            if (chunks.ValueKind != JsonValueKind.Array || chunks.GetArrayLength() is < 1 or > 64) throw new StoreOperationException("gog_support_manifest_invalid");
            using var projectBytes = new MemoryStream(); long encodedTotal = 0;
            foreach (var chunk in chunks.EnumerateArray())
            {
                var compressedHash = ProjectDigest(chunk, "compressedMd5"); var plainHash = ProjectDigest(chunk, "md5");
                long size = chunk.GetProperty("size").GetInt64(), compressedSize = chunk.GetProperty("compressedSize").GetInt64();
                if (size is < 1 or > MaxGogProjectBytes || compressedSize is < 1 or > MaxGogProjectBytes || projectBytes.Length > MaxGogProjectBytes - size || encodedTotal > MaxGogProjectBytes - compressedSize)
                    throw new StoreOperationException("gog_support_manifest_invalid");
                var uri = new Uri($"https://gog-cdn-fastly.gog.com/content-system/v2/offline/{compressedHash[..2]}/{compressedHash.Substring(2, 2)}/{compressedHash}");
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri != uri) throw new StoreOperationException("gog_support_project_unavailable");
                await using var input = await response.Content.ReadAsStreamAsync(token); using var encoded = new MemoryStream();
                await CopyGogProjectBounded(input, encoded, token);
                if (encoded.Length != compressedSize || !ProjectHashMatches(encoded, compressedHash)) throw new StoreOperationException("gog_support_project_changed");
                encoded.Position = 0; using var plain = new MemoryStream(); using (var zlib = new ZLibStream(encoded, CompressionMode.Decompress, true)) await CopyGogProjectBounded(zlib, plain, token);
                if (plain.Length != size || !ProjectHashMatches(plain, plainHash)) throw new StoreOperationException("gog_support_project_changed");
                encodedTotal += compressedSize; plain.Position = 0; await plain.CopyToAsync(projectBytes, token);
            }
            if (projectBytes.Length != offline.GetProperty("size").GetInt64() || encodedTotal != offline.GetProperty("compressedSize").GetInt64()) throw new StoreOperationException("gog_support_project_changed");
            projectBytes.Position = 0; using var document = JsonDocument.Parse(projectBytes, new JsonDocumentOptions { MaxDepth = 32 });
            VerifyNoGogProjectActions(id, metadata, document.RootElement);
            path = new StoreInstallFileSystem().CanonicalDestination(path);
            using var pins = StoreOwnedInstallFiles.PinRoot(path); using var marker = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(path, ".playhub-install-owner"));
            using var reader = new StreamReader(marker, leaveOpen: true);
            if (ownerJob == Guid.Empty || marker.Length != 36 || reader.ReadToEnd() != ownerJob.ToString("D")) throw new StoreOperationException("destination_ownership_lost");
            projectBytes.Position = 0;
            var receipt = new GogModernProjectReceipt(1, id, path, ownerJob, GogMetadataHash(metadata), Convert.ToHexString(SHA256.HashData(projectBytes)));
            string destination = GogModernProjectReceiptPath(id), temporary = destination + "." + Guid.NewGuid().ToString("N") + ".new";
            try { using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(output, receipt); output.Flush(true); } File.Move(temporary, destination, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or InvalidDataException or OverflowException)
        { throw new StoreOperationException("gog_support_manifest_invalid"); }
    }

    internal static void VerifyNoGogProjectActions(string id, JsonElement metadata, JsonElement document)
    {
        static void Fields(JsonElement value, params string[] allowed)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new StoreOperationException("gog_support_manifest_invalid");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in value.EnumerateObject())
                if (!names.Add(field.Name) || !allowed.Contains(field.Name, StringComparer.Ordinal)) throw new StoreOperationException("gog_support_action_unsupported");
        }
        Fields(document, "project"); var project = document.GetProperty("project");
        Fields(project, "baseProductId", "version", "installDirectory", "name", "platform", "scriptInterpreter", "tags", "products", "clientId", "clientSecret");
        if (project.GetProperty("baseProductId").ToString() != id || project.GetProperty("platform").GetString() != "windows" ||
            project.GetProperty("installDirectory").GetString() != metadata.GetProperty("installDirectory").GetString()) throw new StoreOperationException("gog_support_manifest_invalid");
        var products = project.GetProperty("products");
        if (products.ValueKind != JsonValueKind.Array || products.GetArrayLength() is < 1 or > 256) throw new StoreOperationException("gog_support_manifest_invalid");
        var declared = metadata.GetProperty("products").EnumerateArray().Select(p => p.GetProperty("productId").ToString()).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var product in products.EnumerateArray())
        {
            Fields(product, "name", "productId", "depots", "tasks");
            string productId = product.GetProperty("productId").ToString();
            if (!declared.Contains(productId) || !seen.Add(productId)) throw new StoreOperationException("gog_support_manifest_invalid");
            // Depot source folders and launch tasks are descriptive metadata,
            // never filesystem input and never commands executed by this adapter.
            if (product.TryGetProperty("depots", out var depots))
            {
                if (depots.ValueKind != JsonValueKind.Array || depots.GetArrayLength() > 4096) throw new StoreOperationException("gog_support_manifest_invalid");
                foreach (var depot in depots.EnumerateArray()) Fields(depot, "languages", "folder", "name");
            }
            if (product.TryGetProperty("tasks", out var tasks))
            {
                if (tasks.ValueKind != JsonValueKind.Array || tasks.GetArrayLength() > 256) throw new StoreOperationException("gog_support_manifest_invalid");
                foreach (var task in tasks.EnumerateArray())
                {
                    Fields(task, "languages", "name", "type", "isPrimary", "path", "category", "url", "arguments", "workingDir", "icon");
                    if (task.GetProperty("type").GetString() != "FileTask" || task.TryGetProperty("category", out var category) && category.GetString() != "game" ||
                        task.TryGetProperty("url", out var url) && !string.IsNullOrEmpty(url.GetString())) throw new StoreOperationException("gog_support_action_unsupported");
                }
            }
        }
        if (!seen.Contains(id)) throw new StoreOperationException("gog_support_manifest_invalid");
    }
    private static string ProjectDigest(JsonElement element, string name)
    {
        string? hash = element.GetProperty(name).GetString();
        return hash is { Length: 32 } && hash.All(Uri.IsHexDigit) ? hash : throw new StoreOperationException("gog_support_manifest_invalid");
    }
    private static bool ProjectHashMatches(MemoryStream stream, string hash)
    { stream.Position = 0; return StringComparer.OrdinalIgnoreCase.Equals(Convert.ToHexString(MD5.HashData(stream)), hash); }
    private static async Task CopyGogProjectBounded(Stream input, Stream output, CancellationToken token)
    {
        var buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        { if (output.Length > MaxGogProjectBytes - count) throw new StoreOperationException("gog_support_manifest_invalid"); await output.WriteAsync(buffer.AsMemory(0, count), token); }
    }
}
