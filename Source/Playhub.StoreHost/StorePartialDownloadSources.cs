// SPDX-License-Identifier: MIT. Original Playhub adapters, Copyright (c) 2026 LoZazaMastro.
// Schema facts come from the bundled, unmodified Legendary/gogdl/Nile sources.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playhub.StoreLibrary;

public sealed partial class StoreAmazonProvider : IStorePartialDownloadSource
{
    bool IStorePartialDownloadSource.HasOperationLease => gate.IsHeld;
    async Task<StorePartialManifest> IStorePartialDownloadSource.ReadPartialManifestAsync(string id, CancellationToken token)
    {
        StoreCliProvider.ValidateId(id); await gate.WaitAsync(token);
        try
        {
            Materialize();
            var plan = await Plan(id, token);
            var files = plan.Manifest.Files.Select(f => new StoreOwnedInstallFile(f.RelativePath, f.Size,
                f.Algorithm == 0 ? "SHA256" : "SHAKE128", Convert.ToHexString(f.Hash))).ToArray();
            return new(Convert.ToHexString(plan.Manifest.RawHash), files, [".patch"]);
        }
        finally { try { Seal(); } finally { gate.Release(); } }
    }
}

public sealed partial class StoreCliProvider : IStorePartialDownloadSource, IStorePartialLanguageDownloadSource
{
    bool IStorePartialDownloadSource.HasOperationLease => nativeGate.IsHeld;
    Task<StorePartialManifest> IStorePartialDownloadSource.ReadPartialManifestAsync(string id,CancellationToken token)=>ReadLanguagePartialManifestAsync(id,null,token);
    async Task<StorePartialManifest> IStorePartialLanguageDownloadSource.ReadLanguagePartialManifestAsync(string id,string? language,CancellationToken token)=>await ReadLanguagePartialManifestAsync(id,language,token);
    async Task<StorePartialManifest> ReadLanguagePartialManifestAsync(string id,string? language,CancellationToken token)
    {
        ValidateId(id); using var scope = OpenQuery(token);
        IReadOnlyList<StoreOwnedInstallFile> files;
        if (Store == StoreKind.Epic)
        {
            var result = await query(tool, ["list-files", id, "--json", "--platform", "Windows"], scope.Environment, token);
            if (result.Code != 0 || result.Json.Length > 32 * 1024 * 1024) throw new StoreOperationException("partial_cleanup_manifest_unavailable");
            using var doc = JsonDocument.Parse(result.Json); files = ReadEpicUninstallFiles(doc.RootElement);
        }
        else
        {
            var result = await query(tool, ["--auth-config-path", scope.AuthPath, "info", id, "--platform", "windows", "--skip-dlcs"], scope.Environment, token);
            if (result.Code != 0 || result.Json.Length > 32 * 1024 * 1024) throw new StoreOperationException("partial_cleanup_manifest_unavailable");
            using var info = JsonDocument.Parse(result.Json);
            var builds = info.RootElement.GetProperty("builds").GetProperty("items");
            if (builds.ValueKind != JsonValueKind.Array || builds.GetArrayLength() is < 1 or > 10000) throw new StoreOperationException("partial_cleanup_manifest_invalid");
            // gogdl 1.4 selects the first default-branch build, or its first
            // build when none is marked default. No DLC/branch override is used.
            var target = builds[0];
            foreach (var build in builds.EnumerateArray())
                if (build.TryGetProperty("branch", out var branch) && branch.ValueKind == JsonValueKind.Null) { target = build; break; }
            var address = target.GetProperty("link").GetString();
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
                uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host is not ("gog-cdn-fastly.gog.com" or "content-system.gog.com") ||
                !uri.AbsolutePath.StartsWith("/content-system/", StringComparison.Ordinal)) throw new StoreOperationException("partial_cleanup_manifest_invalid");
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri != uri) throw new StoreOperationException("partial_cleanup_manifest_unavailable");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var encoded = new MemoryStream(); await CopyBounded(stream, encoded, token); encoded.Position = 0;
            using var plain = new MemoryStream();
            if (encoded.Length > 0 && encoded.GetBuffer()[0] != (byte)'{')
            { using var zlib = new ZLibStream(encoded, CompressionMode.Decompress, true); await CopyBounded(zlib, plain, token); }
            else await encoded.CopyToAsync(plain, token);
            var metadata = JsonNode.Parse(plain.ToArray())?.AsObject() ?? throw new StoreOperationException("partial_cleanup_manifest_invalid");
            metadata["HGLInstallLanguage"] = language ?? "en-US"; metadata["HGLPlatform"] = "windows";
            using var doc = JsonDocument.Parse(metadata.ToJsonString());
            files = (await ReadGogUninstallFiles(id, doc.RootElement, token)).Concat(await ReadGogDependencyFiles(doc.RootElement, token)).ToArray();
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(files))));
        return new(hash, files, Store == StoreKind.Epic ? [".tmp"] : [".tmp", ".delta"], Store == StoreKind.Gog ? ".gogdl-resume" : null);
    }
}
