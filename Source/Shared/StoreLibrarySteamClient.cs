// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed partial class StoreLibraryService
{
    public Task<StoreSteamSyncRequest> RequestSteamSyncAsync(StoreKind store, long expectedAccountRevision, CancellationToken token = default) =>
        Call<StoreSteamSyncRequest>("steam-sync-request", new { store, expectedAccountRevision }, token);
    public Task<StoreSteamSyncRequest> GetSteamSyncStatusAsync(Guid requestId, CancellationToken token = default) =>
        Call<StoreSteamSyncRequest>("steam-sync-status", new { requestId }, token);
    public Task<StoreSteamCollectionMigration> RequestSteamMigrationAsync(CancellationToken token = default) =>
        Call<StoreSteamCollectionMigration>("steam-migration-request", new { }, token);
    public Task<StoreSteamCollectionMigration> GetSteamMigrationStatusAsync(Guid requestId, CancellationToken token = default) =>
        Call<StoreSteamCollectionMigration>("steam-migration-status", new { requestId }, token);
}
