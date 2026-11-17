// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Text.Json;
namespace Playhub.StoreLibrary;

public sealed partial class StoreSteamShortcutRegistry
{
    private async Task<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>> ResolveIdentities(StoreLibrarySnapshot current, CancellationToken token)
    {
        if (nativeIdentities is null) return new Dictionary<StoreGameKey, StoreSteamNativeIdentity>();
        var games = current.Games.Where(g => g.IsInstalled && Current(current, g.Key.Store, g.AccountRevision)).ToArray();
        var result = await nativeIdentities(games, token);
        if (result.Count > 10000 || result.Any(pair => pair.Key != pair.Value.Key || !ValidKey(pair.Key) ||
            !SamePath(pair.Value.RuntimeExecutable, executable) || CanonicalDirectory(pair.Value.InstallPath) is null ||
            CanonicalDirectory(pair.Value.StartDirectory) is null ||
            pair.Value.NativeLaunchKind is not null && pair.Value.NativeLaunchKind != "official" ||
            pair.Value.NativeLaunchKind == "official" && OfficialArguments(pair.Value) is null || !games.Any(g => g.Key == pair.Key &&
                g.AccountRevision == pair.Value.AccountRevision && SamePath(g.InstallPath, pair.Value.InstallPath))))
            throw new InvalidOperationException("steam_native_identity_invalid");
        return result;
    }
    private static bool SamePath(string? left, string? right) => CanonicalDirectory(left) is { } a &&
        CanonicalDirectory(right) is { } b && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    private static string? OfficialArguments(StoreSteamNativeIdentity native)
    {
        if (native.NativeLaunchKind != "official" || native.Key.Store is not (StoreKind.Epic or StoreKind.Gog) ||
            CanonicalDirectory(native.InstallPath) is not { } root || CanonicalDirectory(native.GameExecutablePath) is not { } game ||
            CanonicalDirectory(native.StartDirectory) is not { } working ||
            !game.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !(working.Equals(root, StringComparison.OrdinalIgnoreCase) || working.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ||
            !game.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        static bool Id(string? value) => value is { Length: > 0 and <= 256 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
        if (native.Key.Store == StoreKind.Epic)
            return Id(native.EpicNamespace) && Id(native.EpicCatalogItemId) && Id(native.Key.Id)
                ? "--epic " + string.Join(" ", new[] { native.EpicNamespace!, native.EpicCatalogItemId!, native.Key.Id, game }.Select(StoreSteamShortcutIdentity.Quote)) : null;
        if (native.GameArguments is not { } arguments || StoreSteamShortcutIdentity.Tokens(arguments) is null) return null;
        return "--game " + StoreSteamShortcutIdentity.Quote(game) + (arguments.Length == 0 ? "" : " " + arguments);
    }
    private static string PlannedArguments(StoreGameKey key, StoreSteamNativeIdentity? native) =>
        native?.NativeLaunchKind == "official" ? OfficialArguments(native) ?? throw new InvalidOperationException("steam_native_identity_invalid") :
        native?.NativeApplicationId is { } aumid && native.ManifestExecutable is { } manifest
            ? StoreSteamShortcutIdentity.UwpArguments(aumid, manifest) : Arguments(key);
    private bool ExpectedImported(string exe, string args, string directory, StoreSteamNativeIdentity native)
    {
        if (!SamePath(exe, executable) || !SamePath(native.RuntimeExecutable, executable) || !SamePath(directory, native.StartDirectory)) return false;
        var tokens = StoreSteamShortcutIdentity.Tokens(args);
        if (tokens is null) return false;
        if (native.Key.Store == StoreKind.Xbox)
            return tokens.Count == 4 && tokens[0] == "--uwp" && native.NativeApplicationId is { Length: > 0 and <= 512 } aumid &&
                aumid.Contains('!') && native.ManifestExecutable is { Length: > 0 and <= 2048 } manifest &&
                !Path.IsPathFullyQualified(manifest) && !manifest.Split('\\', '/').Contains("..") &&
                tokens[1].Equals(aumid, StringComparison.OrdinalIgnoreCase) && tokens[2].Replace('/', '\\').Equals(manifest.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
        if (native.GameExecutablePath is null || !SamePath(native.GameExecutablePath, tokens.LastOrDefault()) && tokens.FirstOrDefault() == "--epic") return false;
        if (tokens.Count == 5 && tokens[0] == "--epic" && native.Key.Store == StoreKind.Epic)
            return native.EpicNamespace is { Length: > 0 } && native.EpicCatalogItemId is { Length: > 0 } &&
                tokens[1] == native.EpicNamespace && tokens[2] == native.EpicCatalogItemId && tokens[3] == native.Key.Id && SamePath(tokens[4], native.GameExecutablePath);
        return tokens.Count >= 2 && tokens[0] == "--game" && SamePath(tokens[1], native.GameExecutablePath);
    }
    private bool ExpectedProof(string exe, string args, string directory, StoreGameKey key, StoreSteamNativeIdentity? native) =>
        native is not null && ExpectedImported(exe, args, directory, native) ||
        key.Store != StoreKind.Xbox && Expected(exe, args, directory, key);
    private bool PersistedOwnerValid(Ownership owner) => Expected(owner.Executable, owner.Arguments, owner.StartDirectory, owner.Key) ||
        ExpectedImported(owner.Executable, owner.Arguments, owner.StartDirectory,
            new(owner.Key, owner.AccountRevision, owner.NativeApplicationId, owner.ManifestExecutable, owner.StartDirectory,
                executable, owner.StartDirectory, owner.GameExecutablePath, owner.EpicNamespace, owner.EpicCatalogItemId));
    private StoreSteamImportAdoptionResult AdoptImport(StoreSteamImportAdoption input, StoreLibrarySnapshot current,
        IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity> identities)
    {
        if (!Current(current, input.Key.Store, input.ExpectedAccountRevision)) throw new InvalidOperationException("account_changed");
        if (input.AppId == 0 || input.Title is not { Length: > 0 and <= 2048 } || input.Title.Any(char.IsControl) ||
            !identities.TryGetValue(input.Key, out var native) || native.AccountRevision != input.ExpectedAccountRevision ||
            !ExpectedImported(input.Executable, input.Arguments, input.StartDirectory, native)) throw new InvalidOperationException("steam_import_unverified");
        var previous = state.Owned.SingleOrDefault(o => o.Key == input.Key);
        if (previous is not null && previous.AppId != input.AppId && previous.AppId != input.ExpectedOwnedAppId ||
            input.ExpectedOwnedAppId is not null && previous?.AppId != input.ExpectedOwnedAppId && previous?.AppId != input.AppId ||
            state.Owned.Any(o => o.AppId == input.AppId && o.Key != input.Key) ||
            state.Requests.Any(r => r.Claims.Any(c => c.Key != input.Key && (c.AppId == input.AppId || c.CreatedAppId == input.AppId))))
            throw new InvalidOperationException("steam_identity_conflict");
        var owner = new Ownership(input.AppId, input.Key, input.ExpectedAccountRevision, input.Title, input.Executable,
            input.Arguments, input.StartDirectory, native.NativeApplicationId, native.ManifestExecutable,
            native.GameExecutablePath, native.EpicNamespace, native.EpicCatalogItemId);
        Commit(state with { Owned = state.Owned.Where(o => o.Key != input.Key).Append(owner).ToArray(),
            Requests = state.Requests.Select(r => r with { Claims = r.Claims.Select(c => c.Key == input.Key ? c with { AppId = input.AppId, CreatedAppId = null } : c).ToArray() }).ToArray() });
        return new(true, Owned(current).Single(o => o.Key == input.Key));
    }
    private object ForgetUninstalled(JsonElement args, StoreLibrarySnapshot current)
    {
        var key = args.GetProperty("key").Deserialize<StoreGameKey>(Json) ?? throw new InvalidOperationException("steam_key_invalid");
        uint appId = args.GetProperty("appId").GetUInt32();
        var owner = state.Owned.SingleOrDefault(o => o.Key == key && o.AppId == appId);
        if (owner is null) return new { accepted = true };
        if (!Current(current, key.Store, owner.AccountRevision) || !current.Games.Any(g => g.Key == key &&
            g.AccountRevision == owner.AccountRevision && !g.IsInstalled)) throw new InvalidOperationException("steam_uninstall_unverified");
        Commit(state with { Owned = state.Owned.Where(o => o != owner).ToArray(),
            Requests = state.Requests.Select(r => r with { Claims = r.Claims.Where(c => c.Key != key).ToArray() }).ToArray() });
        return new { accepted = true };
    }
    private StoreSteamSyncRequest RepairMissing(JsonElement args, StoreLibrarySnapshot current)
    {
        var key = args.GetProperty("key").Deserialize<StoreGameKey>(Json) ?? throw new InvalidOperationException("steam_key_invalid");
        long revision = args.GetProperty("expectedAccountRevision").GetInt64();
        uint? expected = args.TryGetProperty("expectedOwnedAppId", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetUInt32() : null;
        var game = current.Games.SingleOrDefault(g => g.Key == key && g.AccountRevision == revision && g.IsInstalled);
        if (!ValidKey(key) || !Current(current, key.Store, revision) || game is null) throw new InvalidOperationException("steam_sync_expired");
        var owner = state.Owned.SingleOrDefault(o => o.Key == key);
        if (owner?.AppId != expected) throw new InvalidOperationException("steam_identity_conflict");
        var pending = state.Requests.SingleOrDefault(r => r.AccountRevision == revision && Status(r, current).State == "pending" && r.Claims.Any(c => c.Key == key));
        if (owner is null && pending is not null) return Status(pending, current);
        // Only the native Steam caller can establish absence. This operation
        // retires the stale local receipt; it never removes a Steam shortcut.
        var request = new Intent(Guid.NewGuid(), key.Store, revision, DateTimeOffset.UtcNow,
            [new Claim(Guid.NewGuid(), key, game.Title)]);
        Commit(state with { Owned = state.Owned.Where(o => o.Key != key).ToArray(), Requests = state.Requests
            .Select(r => r with { Claims = r.Claims.Where(c => c.Key != key).ToArray() }).TakeLast(31).Append(request).ToArray() });
        return Status(request, current);
    }
}
