// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed partial class StoreXboxAccountProvider
{
    internal Func<string, CancellationToken, Task<WindowsXboxPackage?>> PackageLookup { private get; set; } = WindowsXboxPackageCatalog.FindAsync;
    internal string SessionExecutable { private get; set; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Playhub.GameSession.exe"));
    sealed record XboxLaunchOwner(Saved Account, string Identity, long Revision, string Family, bool LocalInstallation,string? InstallProductId=null);

    XboxLaunchOwner LaunchOwner(string id)
    {
        if (!StoreXboxAccountCollections.ValidId(id)) throw new StoreOperationException("xbox_product_invalid");
        lock (sync)
        {
            Load(); LoadOwnedMapping();
            var gamePass=GamePassLaunchRequest(id);
            // A previously confirmed purchased PC mapping remains usable offline.
            // Windows checks the current package license during registered
            // activation; a five-minute catalogue age is not a license failure.
            if (closed || saved is null || (ownedIdentity != Identity(saved.Xuid) || owned.ObservedAt is null)&&gamePass is null)
                throw new StoreOperationException("owned_library_changed_or_unavailable");
            var matches = ownedIdentity==Identity(saved.Xuid)&&owned.ObservedAt is not null?owned.Games.Where(g => g.ProductId == id && g.SupportsPc && g.AccessKind is "owned" or "installed").ToArray():[];
            if(matches.Length==0&&gamePass is not null)return new(saved,Identity(saved.Xuid),owned.Revision,gamePass.Family,true,gamePass.InstallProductId);
            if (matches.Length != 1 || !WindowsXboxPackageCatalog.ValidFamily(matches[0].PackageFamilyName)) throw new StoreOperationException("xbox_package_identity_unavailable");
            return new(saved, Identity(saved.Xuid), owned.Revision, matches[0].PackageFamilyName!, matches[0].AccessKind == "installed");
        }
    }

    async Task<WindowsXboxPackage> ReadOwnedLaunchAsync(string id, string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var owner = LaunchOwner(id);
        var package = await PackageLookup(owner.Family, token);
        token.ThrowIfCancellationRequested();
        // The package lookup may await Windows while sign-out, a refresh or a
        // new account replaces the entitlement snapshot. None can inherit it.
        var current = LaunchOwner(id);
        if (!ReferenceEquals(current.Account, owner.Account) || current.Identity != owner.Identity || current.Revision != owner.Revision || current.Family != owner.Family || current.InstallProductId!=owner.InstallProductId)
            throw new StoreOperationException("account_changed");
        if (package is null || !package.StatusOk || package.IsPlaceholder || !StringComparer.OrdinalIgnoreCase.Equals(package.PackageFamilyName, owner.Family) ||
            owner.LocalInstallation && package.StoreProductId != (owner.InstallProductId??id) ||
            package.Aumid is null || !package.Aumid.StartsWith(owner.Family + "!", StringComparison.Ordinal) ||
            package.ExecutablePath is null || package.ManifestExecutable is null || !File.Exists(package.ExecutablePath) ||
            !Path.IsPathFullyQualified(path) || !StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), package.InstallLocation))
            throw new StoreOperationException("xbox_launch_unverified");
        return package;
    }

    public async Task<ProviderLaunch?> ResolveLaunchAsync(string id, string path, CancellationToken token)
    {
        try
        {
            var package = await ReadOwnedLaunchAsync(id, path, token);
            if (!File.Exists(SessionExecutable)) return null;
            return new(SessionExecutable, Path.GetDirectoryName(SessionExecutable)!, ["--store-library", "xbox", id], package.ExecutablePath);
        }
        catch (Exception error) when (error is StoreOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    public async Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id, string path, CancellationToken token)
    {
        var package = await ReadOwnedLaunchAsync(id, path, token);
        return new(Guid.NewGuid(), new(StoreKind.Xbox, id), package.ExecutablePath!, package.InstallLocation, [],
            new Dictionary<string, string>(), now().AddSeconds(30), package.Aumid);
    }
}
