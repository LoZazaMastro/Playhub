// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
namespace Playhub.StoreLibrary;
public sealed partial class StoreXboxAccountProvider
{
    public async Task<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>> ReadSteamNativeIdentitiesAsync(
        IReadOnlyList<StoreLibraryGame> games, CancellationToken token)
    {
        var owners = new Dictionary<StoreGameKey, XboxLaunchOwner>();
        foreach (var game in games.Where(g => g.Key.Store == StoreKind.Xbox && g.IsInstalled))
        {
            try { owners.Add(game.Key, LaunchOwner(game.Key.Id)); }
            catch (StoreOperationException) { }
        }
        var packages = await PackageBatchLookup(owners.Values.Select(o => o.Family), token);
        var result = new Dictionary<StoreGameKey, StoreSteamNativeIdentity>();
        foreach (var game in games.Where(g => owners.ContainsKey(g.Key)))
        {
            token.ThrowIfCancellationRequested(); var before = owners[game.Key];
            XboxLaunchOwner after;
            try { after = LaunchOwner(game.Key.Id); } catch (StoreOperationException) { continue; }
            if (!ReferenceEquals(before.Account, after.Account) || before.Identity != after.Identity || before.Revision != after.Revision || before.Family != after.Family ||
                !packages.TryGetValue(before.Family, out var package) || !package.StatusOk ||
                !StringComparer.OrdinalIgnoreCase.Equals(package.PackageFamilyName, before.Family) ||
                before.LocalInstallation && package.StoreProductId != (before.InstallProductId??game.Key.Id) ||
                package.Aumid is null || !package.Aumid.StartsWith(before.Family + "!", StringComparison.Ordinal) ||
                package.ManifestExecutable is null || package.ExecutablePath is null || !File.Exists(package.ExecutablePath) ||
                game.InstallPath is null || !Path.IsPathFullyQualified(game.InstallPath) ||
                !StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.InstallPath)), package.InstallLocation)) continue;
            result.Add(game.Key, new(game.Key, game.AccountRevision, package.Aumid, package.ManifestExecutable,
                package.InstallLocation, SessionExecutable, Path.GetDirectoryName(SessionExecutable)!, package.ExecutablePath, LibraryKind: game.LibraryKind));
        }
        return result;
    }
}
