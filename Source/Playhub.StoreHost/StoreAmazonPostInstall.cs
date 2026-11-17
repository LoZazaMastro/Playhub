// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed partial class StoreAmazonProvider
{
    private void VerifyPostInstall(string id, string path, AmazonManifest manifest, CancellationToken token, bool allowInstallation = false)
    {
        using var owner = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(path, ".playhub-install-owner"));
        using var reader = new StreamReader(owner, leaveOpen: true);
        if (owner.Length != 36 || !Guid.TryParseExact(reader.ReadToEnd(), "D", out var job) || job == Guid.Empty) throw new StoreOperationException("amazon_setup_ownership_lost");
        AmazonPostInstall.Verify(id, path, job, manifest, Path.Combine(native, "post-install-" + id + ".json"), token, new AmazonWindowsSetupProbe(allowInstallation));
    }
}
