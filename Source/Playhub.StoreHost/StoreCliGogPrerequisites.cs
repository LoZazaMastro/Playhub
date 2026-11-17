// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;

namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    internal async Task FinalizeGogPrerequisitesAsync(string id, string gamePath, Guid ownerJob, JsonElement metadata, CancellationToken token)
    {
        var steps = new List<StorePrerequisiteStep>(); var ids = new List<string>();
        var files = await ReadGogDependencyFiles(metadata, token, true, steps, ids);
        if (steps.Count == 0) return;
        // Global redistributables live in a private, job-owned cache, never in
        // the game tree or its uninstall inventory. The game lease stays held.
        using var gamePins = StoreOwnedInstallFiles.PinRoot(gamePath);
        using var gameOwner = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(gamePath, ".playhub-install-owner"));
        using var ownerReader = new StreamReader(gameOwner, leaveOpen: true);
        if (ownerJob == Guid.Empty || gameOwner.Length > 64 || ownerReader.ReadToEnd() != ownerJob.ToString("D"))
            throw new StoreOperationException("prerequisite_ownership_lost");
        var cache = Path.Combine(root, "prerequisites", ownerJob.ToString("N"));
        if (!Directory.Exists(cache))
        {
            Directory.CreateDirectory(cache);
            using var marker = new FileStream(Path.Combine(cache, ".playhub-install-owner"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(marker); writer.Write(ownerJob.ToString("D"));
        }
        cache = new StoreInstallFileSystem().CanonicalDestination(cache);
        using var cachePins = StoreOwnedInstallFiles.PinRoot(cache);
        using var cacheOwner = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(cache, ".playhub-install-owner"));
        using var cacheReader = new StreamReader(cacheOwner, leaveOpen: true);
        if (cacheOwner.Length > 64 || cacheReader.ReadToEnd() != ownerJob.ToString("D")) throw new StoreOperationException("prerequisite_ownership_lost");
        var download = await query(tool, ["--auth-config-path", nativeAuth, "dependencies", "--ids", string.Join(',', ids), "--path", cache], environment, token);
        if (download.Code != 0) throw new StoreOperationException("prerequisite_download_failed");
        // Include installer companions/CABs in checksum verification before setup.
        var retainedFiles = new List<IDisposable>();
        try
        {
            foreach (var file in files)
            {
                var path = StoreOwnedInstallFiles.PathFor(cache, file.RelativePath);
                retainedFiles.Add(StoreOwnedInstallFiles.PinRoot(Path.GetDirectoryName(path)!));
                var retained = StoreOwnedInstallFiles.OpenReadFile(path); retainedFiles.Add(retained);
                StoreOwnedInstallFiles.VerifyContents(retained, file, token);
            }
            StorePrerequisiteInstaller.Apply(cache, ownerJob, GogMetadataHash(metadata), files, steps,
                Path.Combine(root, "prerequisites-" + id + ".json"), token, PrerequisiteRunner);
        }
        finally { for (var i = retainedFiles.Count - 1; i >= 0; i--) retainedFiles[i].Dispose(); }
    }
}
