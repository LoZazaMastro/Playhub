// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Security.Cryptography;
using System.Text.Json;

namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    internal Func<string, IReadOnlyList<string>, CancellationToken, int>? PrerequisiteRunner { get; set; }

    internal async Task FinalizeEpicPrerequisitesAsync(string id, string path, Guid ownerJob, CancellationToken token)
    {
        var manifestPath = EpicInstalledManifest(id);
        using var manifest = StoreOwnedInstallFiles.OpenReadFile(manifestPath);
        if (manifest.Length is < 1 or > 32 * 1024 * 1024) throw new StoreOperationException("prerequisite_metadata_invalid");
        var authorization = Convert.ToHexString(SHA256.HashData(manifest));
        var info = await query(tool, ["info", manifestPath, "--offline", "--json"], environment, token);
        if (info.Code != 0) throw new StoreOperationException("prerequisite_metadata_unavailable");
        using var document = JsonDocument.Parse(info.Json);
        var metadata = document.RootElement.GetProperty("manifest");
        if (metadata.GetProperty("app_name").GetString() != id) throw new StoreOperationException("prerequisite_metadata_invalid");
        var steps = new List<StorePrerequisiteStep>();
        if (metadata.TryGetProperty("prerequisites", out var prerequisite) && prerequisite.ValueKind != JsonValueKind.Null)
        {
            var executable = prerequisite.GetProperty("path").GetString() ?? "";
            var arguments = prerequisite.GetProperty("args").GetString() ?? "";
            steps.Add(new(executable.Replace('\\', '/'), SplitWindowsArguments(arguments), [0, 1641, 3010]));
        }
        if (steps.Count == 0) return;
        var listing = await query(tool, ["list-files", id, "--json", "--manifest", manifestPath], environment, token);
        if (listing.Code != 0) throw new StoreOperationException("prerequisite_metadata_unavailable");
        using var files = JsonDocument.Parse(listing.Json);
        StorePrerequisiteInstaller.Apply(path, ownerJob, authorization, ReadEpicUninstallFiles(files.RootElement), steps,
            Path.Combine(root, "prerequisites-" + id + ".json"), token, PrerequisiteRunner);
    }
}
