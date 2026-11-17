// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Text.Json;

namespace Playhub.StoreLibrary;

public sealed partial class StoreXboxAccountProvider : IStoreInstallVerifier
{
    public async Task<bool> IsInstalledAsync(string id, string path, CancellationToken token)
    {
        try
        {
            var owner = LaunchOwner(id);
            var package = await PackageLookup(owner.Family, token);
            var current = LaunchOwner(id);
            return ReferenceEquals(current.Account, owner.Account) && current.Identity == owner.Identity && current.Family == owner.Family
                && package?.StatusOk == true && !package.IsPlaceholder && string.Equals(package.PackageFamilyName, owner.Family, StringComparison.OrdinalIgnoreCase)
                && (!owner.LocalInstallation || package.StoreProductId == (owner.InstallProductId??id))
                && Path.IsPathFullyQualified(path) && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), package.InstallLocation, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is StoreOperationException or IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    internal Func<IEnumerable<string>, CancellationToken, Task<IReadOnlyDictionary<string, WindowsXboxPackage>>> PackageBatchLookup { private get; set; } = WindowsXboxPackageCatalog.FindManyAsync;
    internal Func<CancellationToken, Task<IReadOnlyList<WindowsXboxPackage>>> InstalledPackageLookup { private get; set; } =
        _ => Task.FromResult<IReadOnlyList<WindowsXboxPackage>>([]);
    readonly string? ownedLibraryCachePath;
    internal bool UseWindowsStoreManagedInstalls { get; set; }
    internal bool CurrentOwnedFamily(string productId, string family)
    {
        lock (sync)
        {
            Load(); LoadOwnedMapping();
            return !closed && saved is not null && ownedIdentity == Identity(saved.Xuid)
                && owned.Games.Any(g => g.ProductId == productId && g.SupportsPc && g.AccessKind == "owned"
                    && string.Equals(g.PackageFamilyName, family, StringComparison.OrdinalIgnoreCase));
        }
    }
    sealed record OwnedLibraryCache(int Version, string AccountIdentity, StoreXboxOwnedSnapshot Snapshot);

    // Caller holds sync. Only public product metadata and the existing account
    // hash are cached; credentials and the raw Microsoft account ID never are.
    void SaveOwnedMapping()
    {
        if (ownedLibraryCachePath is null || ownedIdentity is null) return;
        string temporary = ownedLibraryCachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ownedLibraryCachePath)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new OwnedLibraryCache(1, ownedIdentity, owned)));
            File.Move(temporary, ownedLibraryCachePath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    // Caller holds sync. Cached ownership is bound to the same current account;
    // Windows must independently verify the installed package on every launch.
    void LoadOwnedMapping()
    {
        if (ownedLibraryCachePath is null || saved is null || ownedIdentity == Identity(saved.Xuid)) return;
        try
        {
            if (!File.Exists(ownedLibraryCachePath) || new FileInfo(ownedLibraryCachePath).Length > 4 * 1024 * 1024) return;
            var cache = JsonSerializer.Deserialize<OwnedLibraryCache>(File.ReadAllBytes(ownedLibraryCachePath));
            if (cache is null || cache.Version != 1 || cache.AccountIdentity != Identity(saved.Xuid) || cache.Snapshot is not { } snapshot
                || snapshot.Revision < 0 || snapshot.Games is null || snapshot.Games.Count > 5000
                || snapshot.Games.Any(g => !StoreXboxAccountCollections.ValidId(g.ProductId) || g.Title is not { Length: > 0 and <= 2048 }
                    || g.Title.Any(char.IsControl) || g.AccessKind is not ("owned" or "conditional" or "installed")
                    || g.PackageFamilyName is not null && !WindowsXboxPackageCatalog.ValidFamily(g.PackageFamilyName)||!StoreXboxInstallProviders.Valid(g.InstallProvider))
                || snapshot.Games.Select(g => g.ProductId).Distinct(StringComparer.Ordinal).Count() != snapshot.Games.Count) return;
            ownedIdentity = cache.AccountIdentity;
            owned = snapshot with { IsStale = true, ErrorCode = "xbox_library_cached" };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }
}
