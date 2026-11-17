// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;

namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    private string GogSupportReceiptPath(string id) => Path.Combine(root, "support-owned-" + id + ".json");

    // Called only after the owned CLI has exited successfully and drained. The
    // install registration/verified completion is committed after this returns.
    internal async Task FinalizeGogInstallationAsync(string id, string path, Guid ownerJob)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        var token = timeout.Token;
        var manifestPath = GogManifestPath(id);
        using var manifestFile = StoreOwnedInstallFiles.OpenReadFile(manifestPath);
        if (manifestFile.Length is < 1 or > 16 * 1024 * 1024) throw new StoreOperationException("gog_support_manifest_invalid");
        using var manifest = JsonDocument.Parse(manifestFile);
        var metadata = manifest.RootElement;
        var files = await ReadGogUninstallFiles(id, metadata, token);
        var dependencies = await ReadGogDependencyFiles(metadata, token);
        RetainGogDependencies(id, path, ownerJob, metadata, dependencies, token);
        files = files.Concat(dependencies).ToArray();
        var language = metadata.GetProperty("HGLInstallLanguage").GetString()!;
        var required = metadata.TryGetProperty("scriptInterpreter", out var interpreter) && interpreter.ValueKind == JsonValueKind.True;
        // Native post-download executables can perform arbitrary machine writes;
        // they need a separately implemented, verified prerequisite adapter.
        if (metadata.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array && products.EnumerateArray().Any(p =>
            p.TryGetProperty("temp_executable", out var executable) && executable.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(executable.GetString())))
            throw new StoreOperationException("gog_support_action_unsupported");
        if (required && !files.Any(file => file.RelativePath.Equals("goggame-" + id + ".script", StringComparison.OrdinalIgnoreCase)))
        {
            await VerifyGogModernProjectAsync(id, path, ownerJob, metadata, token);
            required = false;
        }
        StoreGogSupportData.Apply(GogSupportReceiptPath(id), path, id, ownerJob, language, required, files, token);
        await FinalizeGogPrerequisitesAsync(id, path, ownerJob, metadata, token);
    }
}
