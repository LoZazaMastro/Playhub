// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;

namespace Playhub.StoreLibrary;

// This registry never reads or rewrites shortcuts.vdf. Steam owns that file.
// New shortcuts are created only for verified installed games. Failed
// authentication and uninstall never prune existing Steam ownership records.
public sealed partial class StoreSteamShortcutRegistry : IStoreProtocolExtension
{
    private readonly Func<CancellationToken, Task<StoreLibrarySnapshot>> snapshot;
    private readonly string executable, startDirectory, path;
    private readonly Func<IReadOnlyList<StoreLibraryGame>, CancellationToken, Task<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>>>? nativeIdentities;
    private readonly Func<string?> activeSteamAccount;
    private readonly Func<CancellationToken,Task<IReadOnlyList<StoreDownloadJob>>>? retainedCompletions;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = StoreLibraryService.JsonOptions;
    private sealed record Ownership(uint AppId, StoreGameKey Key, long AccountRevision,
        string Title, string Executable, string Arguments, string StartDirectory, string? NativeApplicationId = null,
        string? ManifestExecutable = null, string? GameExecutablePath = null, string? EpicNamespace = null, string? EpicCatalogItemId = null);
    private sealed record Claim(Guid Id, StoreGameKey Key, string Title, uint? AppId = null, uint? CreatedAppId = null);
    private sealed record Intent(Guid Id, StoreKind Store, long AccountRevision, DateTimeOffset Created,
        Claim[] Claims, string? ErrorCode = null);
    private sealed record State(int Version, Ownership[] Owned, Intent[] Requests, string? CreatedRuntimeExecutable = null,
        StoreSteamCollectionMigration[]? Migrations = null);
    private State state;

    public StoreSteamShortcutRegistry(Func<CancellationToken, Task<StoreLibrarySnapshot>> snapshot,
        string gameSessionExecutable, string appExecutable, string? registryPath = null,
        Func<IReadOnlyList<StoreLibraryGame>, CancellationToken, Task<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>>>? nativeIdentities = null,
        Func<string?>? activeSteamAccount = null,
        Func<CancellationToken,Task<IReadOnlyList<StoreDownloadJob>>>? retainedCompletions = null)
    {
        this.snapshot = snapshot;
        this.nativeIdentities = nativeIdentities;
        this.activeSteamAccount = activeSteamAccount ?? ReadActiveSteamAccount;
        this.retainedCompletions=retainedCompletions;
        executable = Path.GetFullPath(gameSessionExecutable);
        startDirectory = Path.GetDirectoryName(executable)!;
        if (Path.GetFileName(executable) != "Playhub.GameSession.exe" ||
            !Path.GetDirectoryName(Path.GetFullPath(appExecutable))!.Equals(startDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("steam_runtime_invalid");
        path = registryPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Playhub", "StoreLibrary", "steam-library-v1.json");
        state = File.Exists(path) ? JsonSerializer.Deserialize<State>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException("steam_registry_invalid") : new(1, [], []);
        if (state.Version != 1 || state.Owned.Select(x => x.AppId).Distinct().Count() != state.Owned.Length ||
            state.Owned.Select(x => x.Key).Distinct().Count() != state.Owned.Length ||
            state.Owned.Any(x => !PersistedOwnerValid(x)) || state.Migrations is { } migrations &&
            (migrations.Length > 64 || migrations.Select(m => m.RequestId).Distinct().Count() != migrations.Length ||
                migrations.Any(m => m.RequestId == Guid.Empty || m.Version is not { Length: > 0 and <= 120 } ||
                    m.State is not ("waitingSteam" or "pending" or "completed" or "failed") ||
                    (m.State == "waitingSteam" ? m.SteamAccountId is not null : !ValidSteamAccount(m.SteamAccountId)) ||
                    m.Total < 0 || m.Total > 50000 || m.Confirmed < 0 || m.Confirmed > m.Total || m.State == "completed" && m.Confirmed != m.Total)))
            throw new InvalidDataException("steam_registry_invalid");
        var drafts = state.Requests.SelectMany(r => r.Claims).Where(c => c.CreatedAppId is not null && c.AppId is null).ToArray();
        if (drafts.Any(c => c.CreatedAppId == 0 || !ValidKey(c.Key)) ||
            drafts.Select(c => c.CreatedAppId).Distinct().Count() != drafts.Length ||
            drafts.Any(c => state.Owned.Any(o => o.AppId == c.CreatedAppId && o.Key != c.Key)) ||
            state.Requests.SelectMany(r => r.Claims).Any(c => c.AppId is not null && c.CreatedAppId is not null && c.AppId != c.CreatedAppId) ||
            drafts.Length > 0 && !string.Equals(state.CreatedRuntimeExecutable, executable, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("steam_registry_invalid");
    }

    public async Task<(bool Handled, object? Data)> TryHandleAsync(string method, JsonElement args, CancellationToken token)
    {
        if (method is not ("steam-sync-request" or "steam-sync-status" or "steam-sync-pending" or "steam-sync-created" or "steam-sync-ack" or "steam-owned" or "steam-native-identities" or "steam-adopt-import" or "steam-forget-uninstalled" or "steam-repair-missing" or
            "steam-migration-request" or "steam-migration-status" or "steam-migration-pending" or "steam-migration-report"))
            return (false, null);
        await gate.WaitAsync(token);
        try
        {
            var current = await snapshot(token);
            if(method=="steam-owned"&&retainedCompletions is not null)
            {
                var completed=await retainedCompletions(token);
                // Fence account/path changes while the retained view is read.
                current=await snapshot(token);
                return(true,Owned(current,completed));
            }
            IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity> identities = new Dictionary<StoreGameKey, StoreSteamNativeIdentity>();
            if (method is "steam-native-identities" or "steam-adopt-import" or "steam-sync-pending" or "steam-sync-created" or "steam-sync-ack")
            {
                identities = await ResolveIdentities(current, token);
                // A sign-out or an uninstall while Windows is answering must
                // never borrow the earlier account's native identity proof.
                current = await snapshot(token);
                identities = identities.Where(pair => Current(current, pair.Key.Store, pair.Value.AccountRevision) &&
                    current.Games.Any(g => g.Key == pair.Key && g.AccountRevision == pair.Value.AccountRevision && g.IsInstalled &&
                        SamePath(g.InstallPath, pair.Value.InstallPath))).ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            return (true, method switch
            {
                "steam-sync-request" => Request(args, current),
                "steam-sync-status" => Status(Find(args.GetProperty("requestId").GetGuid()), current),
                "steam-sync-pending" => Pending(current, identities),
                "steam-sync-created" => Created(args.Deserialize<StoreSteamShortcutAck>(Json) ?? throw new InvalidDataException("steam_ack_invalid"), current, identities),
                "steam-sync-ack" => Ack(args.Deserialize<StoreSteamShortcutAck>(Json) ?? throw new InvalidDataException("steam_ack_invalid"), current, identities),
                "steam-owned" => Owned(current),
                "steam-native-identities" => identities.Values.ToArray(),
                "steam-adopt-import" => AdoptImport(args.Deserialize<StoreSteamImportAdoption>(Json) ?? throw new InvalidDataException("steam_ack_invalid"), current, identities),
                "steam-forget-uninstalled" => ForgetUninstalled(args, current),
                "steam-repair-missing" => RepairMissing(args, current),
                "steam-migration-request" => RequestMigration(),
                "steam-migration-status" => MigrationStatus(args.GetProperty("requestId").GetGuid()),
                "steam-migration-pending" => PendingMigrations(),
                "steam-migration-report" => ReportMigration(args),
                _ => throw new InvalidOperationException("steam_method_invalid")
            });
        }
        finally { gate.Release(); }
    }

    private StoreSteamSyncRequest Request(JsonElement args, StoreLibrarySnapshot current)
    {
        var store = args.GetProperty("store").Deserialize<StoreKind>(Json);
        var revision = args.GetProperty("expectedAccountRevision").GetInt64();
        if (!Current(current, store, revision)) throw new InvalidOperationException("account_changed");
        EnsureInstalledRequests(current);
        var games = current.Games.Where(g => g.Key.Store == store && g.AccountRevision == revision && g.IsInstalled)
            .DistinctBy(g => g.Key).OrderBy(g => g.Key.Id, StringComparer.Ordinal).ToArray();
        if (games.Length > 10000 || games.Any(g => !ValidKey(g.Key))) throw new InvalidDataException("steam_inventory_invalid");
        var existing = state.Requests.LastOrDefault(r => r.Store == store && r.AccountRevision == revision && Status(r, current).State == "pending");
        if (existing is not null) return Status(existing, current);
        var claims = games.Select(g =>
        {
            var previous = state.Owned.SingleOrDefault(o => o.Key == g.Key);
            var title = g.Title;
            return new Claim(Guid.NewGuid(), g.Key, title);
        }).ToArray();
        var request = new Intent(Guid.NewGuid(), store, revision, DateTimeOffset.UtcNow, claims);
        Commit(state with { Requests = state.Requests.Where(r => r.Store != store || r.AccountRevision != revision).TakeLast(31).Append(request).ToArray() });
        return Status(request, current);
    }

    private StoreSteamSyncPending Pending(StoreLibrarySnapshot current, IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity> identities)
    {
        EnsureInstalledRequests(current);
        var plans = new List<StoreSteamShortcutPlan>();
        foreach (var request in state.Requests.Where(r => Status(r, current).State == "pending"))
        foreach (var claim in request.Claims.Where(c => c.AppId is null && current.Games.Any(g => g.Key == c.Key && g.AccountRevision == request.AccountRevision && g.IsInstalled)))
        {
            var previous = state.Owned.SingleOrDefault(o => o.Key == claim.Key);
            var native = identities.GetValueOrDefault(claim.Key);
            if (claim.Key.Store == StoreKind.Xbox && native is null) continue;
            string arguments = previous?.Arguments ?? PlannedArguments(claim.Key, native);
            plans.Add(new(request.Id, claim.Id, claim.Key, request.AccountRevision, claim.Title,
                previous?.Executable ?? executable, arguments, previous?.StartDirectory ?? (native?.NativeLaunchKind == "official" ? native.StartDirectory : startDirectory), previous?.AppId, claim.CreatedAppId,
                native?.NativeApplicationId ?? previous?.NativeApplicationId, native?.ManifestExecutable ?? previous?.ManifestExecutable,
                current.Games.Single(g => g.Key == claim.Key && g.AccountRevision == request.AccountRevision).LibraryKind));
            if (plans.Count == 32) return new(plans);
        }
        return new(plans);
    }
    private void EnsureInstalledRequests(StoreLibrarySnapshot current)
    {
        var next = state;
        bool changed = false;
        // A game can be uninstalled while its Steam plan waits for the UI.
        // Retire only unapplied claims, retaining all confirmed shortcuts.
        var requests = state.Requests.Select(r =>
        {
            if (Status(r, current).State != "pending") return r;
            var claims = r.Claims.Where(c => c.AppId is not null || c.CreatedAppId is not null || current.Games.Any(g => g.Key == c.Key && g.AccountRevision == r.AccountRevision && g.IsInstalled)).ToArray();
            if (claims.Length == r.Claims.Length) return r;
            changed = true; return r with { Claims = claims };
        }).ToList();
        foreach (var account in current.Accounts.Where(a => a.State == StoreConnectionState.Connected))
        {
            var installed = current.Games.Where(g => g.Key.Store == account.Store && g.AccountRevision == account.Revision && g.IsInstalled)
                .DistinctBy(g => g.Key).OrderBy(g => g.Key.Id, StringComparer.Ordinal).ToArray();
            if (installed.Length > 10000 || installed.Any(g => !ValidKey(g.Key))) throw new InvalidDataException("steam_inventory_invalid");
            var pending = requests.LastOrDefault(r => r.Store == account.Store && r.AccountRevision == account.Revision && Status(r,current).State == "pending");
            var missing = installed.Where(g => !state.Owned.Any(o => o.Key == g.Key && o.AccountRevision == account.Revision)
                && (pending is null || !pending.Claims.Any(c => c.Key == g.Key))).ToArray();
            if (missing.Length == 0) continue;
            // Same naming/provenance rules as an explicit sync. No plan may
            // adopt a matching user shortcut without the existing ACK proof.
            Claim ClaimFor(StoreLibraryGame g)
            {
                var previous = state.Owned.SingleOrDefault(o => o.Key == g.Key);
                var title = g.Title;
                var drafts = requests.Where(r => r.Store == account.Store && r.AccountRevision == account.Revision)
                    .SelectMany(r => r.Claims).Where(c => c.AppId is null && c.CreatedAppId is not null && c.Key == g.Key && c.Title == title).ToArray();
                if (drafts.Length > 1) throw new InvalidDataException("steam_identity_conflict");
                return new(Guid.NewGuid(), g.Key, title, CreatedAppId: drafts.SingleOrDefault()?.CreatedAppId);
            }
            var additions = missing.Select(ClaimFor).ToArray();
            var transferred = additions.Where(c => c.CreatedAppId is not null).Select(c => c.CreatedAppId).ToHashSet();
            if (transferred.Count > 0)
                requests = requests.Select(r => r.Claims.Any(c => c.CreatedAppId is not null && transferred.Contains(c.CreatedAppId))
                    ? r with { Claims = r.Claims.Select(c => transferred.Contains(c.CreatedAppId) ? c with { CreatedAppId = null } : c).ToArray() } : r).ToList();
            if (pending is null)
                requests.Add(new(Guid.NewGuid(), account.Store, account.Revision, DateTimeOffset.UtcNow, additions));
            else
                requests[requests.IndexOf(pending)] = pending with { Claims = pending.Claims.Concat(additions).ToArray() };
            changed = true;
        }
        if (changed) Commit(next with { Requests = requests.TakeLast(32).ToArray() });
    }

    private StoreSteamSyncRequest Created(StoreSteamShortcutAck ack, StoreLibrarySnapshot current, IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity> identities)
    {
        var request = Find(ack.RequestId);
        if (Status(request, current).State != "pending") throw new InvalidOperationException("steam_sync_expired");
        var claim = request.Claims.SingleOrDefault(c => c.Id == ack.ClaimId) ?? throw new InvalidOperationException("steam_claim_missing");
        if (!current.Games.Any(g => g.Key == claim.Key && g.AccountRevision == request.AccountRevision && g.IsInstalled))
            throw new InvalidOperationException("steam_sync_expired");
        // This draft records only an ID returned by Steam plus its initial
        // exact target proof. It is not a library/collection/launch ownership ACK.
        var native = identities.GetValueOrDefault(claim.Key);
        string intended = PlannedArguments(claim.Key, native);
        if (ack.AppId == 0 || !ExpectedProof(ack.Executable, intended, ack.StartDirectory, claim.Key, native) ||
            ack.Arguments != "" && ack.Arguments != intended) throw new InvalidOperationException("steam_ack_invalid");
        if (claim.AppId is not null && claim.AppId != ack.AppId || claim.CreatedAppId is not null && claim.CreatedAppId != ack.AppId ||
            state.Owned.Any(o => o.AppId == ack.AppId && o.Key != claim.Key || o.Key == claim.Key && o.AppId != ack.AppId) ||
            state.Requests.Any(r => r.Claims.Any(c => c.AppId is null && c.CreatedAppId == ack.AppId && (r.Id != request.Id || c.Id != claim.Id))))
            throw new InvalidOperationException("steam_identity_conflict");
        if (claim.CreatedAppId == ack.AppId) return Status(request, current);
        var updated = request with { Claims = request.Claims.Select(c => c.Id == claim.Id ? c with { CreatedAppId = ack.AppId } : c).ToArray() };
        Commit(state with { Requests = state.Requests.Select(r => r.Id == request.Id ? updated : r).ToArray(), CreatedRuntimeExecutable = executable });
        return Status(updated, current);
    }
    private StoreSteamSyncRequest Ack(StoreSteamShortcutAck ack, StoreLibrarySnapshot current, IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity> identities)
    {
        var request = Find(ack.RequestId);
        if (Status(request, current).State != "pending") throw new InvalidOperationException("steam_sync_expired");
        var claim = request.Claims.SingleOrDefault(c => c.Id == ack.ClaimId) ?? throw new InvalidOperationException("steam_claim_missing");
        if (!current.Games.Any(g => g.Key == claim.Key && g.AccountRevision == request.AccountRevision && g.IsInstalled))
            throw new InvalidOperationException("steam_sync_expired");
        var native = identities.GetValueOrDefault(claim.Key);
        if (ack.AppId == 0 || !ExpectedProof(ack.Executable, ack.Arguments, ack.StartDirectory, claim.Key, native))
            throw new InvalidOperationException("steam_ack_invalid");
        var sameApp = state.Owned.SingleOrDefault(o => o.AppId == ack.AppId);
        var sameKey = state.Owned.SingleOrDefault(o => o.Key == claim.Key);
        if (sameApp is not null && sameApp.Key != claim.Key || sameKey is not null && sameKey.AppId != ack.AppId ||
            claim.AppId is not null && claim.AppId != ack.AppId || claim.CreatedAppId is not null && claim.CreatedAppId != ack.AppId ||
            state.Requests.Any(r => r.Claims.Any(c => c.AppId is null && c.CreatedAppId == ack.AppId && (r.Id != request.Id || c.Id != claim.Id))))
            throw new InvalidOperationException("steam_identity_conflict");
        var owner = new Ownership(ack.AppId, claim.Key, request.AccountRevision, claim.Title, executable, ack.Arguments,
            Expected(ack.Executable, ack.Arguments, ack.StartDirectory, claim.Key) ? startDirectory : native!.StartDirectory,
            native?.NativeApplicationId, native?.ManifestExecutable,
            native?.GameExecutablePath, native?.EpicNamespace, native?.EpicCatalogItemId);
        var updated = request with { Claims = request.Claims.Select(c => c.Id == claim.Id ? c with { AppId = ack.AppId } : c).ToArray() };
        Commit(state with { Owned = state.Owned.Where(o => o.Key != claim.Key).Append(owner).ToArray(),
            Requests = state.Requests.Select(r => r.Id == request.Id ? updated : r).ToArray() });
        return Status(updated, current);
    }

    private IReadOnlyList<StoreSteamOwnedGame> Owned(StoreLibrarySnapshot current,IReadOnlyList<StoreDownloadJob>? retained=null) => state.Owned.Select(o =>
    {
        var game = current.Games.SingleOrDefault(g => g.Key == o.Key && g.AccountRevision == o.AccountRevision);
        var visibleJob=current.Jobs.Where(j=>j.Key==o.Key&&j.AccountRevision==o.AccountRevision).OrderByDescending(j=>j.UpdatedAt).FirstOrDefault();
        var completion=visibleJob is null&&Current(current,o.Key.Store,o.AccountRevision)&&game is{IsInstalled:true,IsLaunchable:true}
            ?retained?.Where(j=>j.State==StoreJobState.Completed&&j.Key==o.Key&&j.AccountRevision==o.AccountRevision&&SamePath(j.InstallPath,game.InstallPath))
                .OrderByDescending(j=>j.UpdatedAt).FirstOrDefault():null;
        return new StoreSteamOwnedGame(o.AppId, o.Key, o.Executable, o.Arguments, o.StartDirectory, o.Title,
            Current(current, o.Key.Store, o.AccountRevision), game,
            visibleJob??completion, o.NativeApplicationId, o.ManifestExecutable);
    }).ToArray();
    private static bool Current(StoreLibrarySnapshot s, StoreKind kind, long revision) =>
        s.Accounts.Count(a => a.Store == kind && a.Revision == revision && a.State == StoreConnectionState.Connected) == 1;
    private Intent Find(Guid id) => state.Requests.SingleOrDefault(r => r.Id == id) ?? throw new InvalidOperationException("steam_sync_missing");
    private static StoreSteamSyncRequest Status(Intent r, StoreLibrarySnapshot s) => new(r.Id, r.Store, r.AccountRevision,
        r.ErrorCode is not null ? "failed" : !Current(s, r.Store, r.AccountRevision) ? "accountChanged" :
        r.Claims.All(c => c.AppId is not null) ? "completed" : DateTimeOffset.UtcNow - r.Created > TimeSpan.FromMinutes(20) ? "expired" : "pending",
        r.Claims.Count(c => c.AppId is not null), r.Claims.Length, r.ErrorCode);
    private static bool ValidKey(StoreGameKey k) => k.Store is StoreKind.Epic or StoreKind.Gog or StoreKind.Xbox or StoreKind.Amazon &&
        k.Id.Length is > 0 and <= 250 && k.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    private static string Arguments(StoreGameKey key)
    {
        if (!ValidKey(key)) throw new InvalidDataException("steam_key_invalid");
        return $"--store-library {key.Store.ToString().ToLowerInvariant()} {key.Id}";
    }
    private bool Expected(string exe, string args, string directory, StoreGameKey key) => ValidKey(key) &&
        exe.Trim('"').Equals(executable, StringComparison.OrdinalIgnoreCase) && args == Arguments(key) &&
        CanonicalDirectory(directory) is { } actualDirectory &&
        actualDirectory.Equals(CanonicalDirectory(startDirectory), StringComparison.OrdinalIgnoreCase);
    private static string? CanonicalDirectory(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 32767 || value.Any(char.IsControl)) return null;
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
        if (value.Contains('"') || !Path.IsPathFullyQualified(value)) return null;
        try
        {
            string full = Path.GetFullPath(value.Replace('/', Path.DirectorySeparatorChar));
            string root = Path.GetPathRoot(full)!;
            // Steam may append its native directory separator to StartDir.
            // Keep drive/UNC roots absolute: C: must never compare equal to C:\.
            return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    private void Commit(State next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(JsonSerializer.SerializeToUtf8Bytes(next, Json)); file.Flush(true); }
            File.Move(temp, path, true); state = next;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
