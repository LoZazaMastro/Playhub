// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using Playhub.StoreLibrary;

namespace Playhub.Services;

internal static class StoreLibrarySteamBridge
{
    internal static async Task<bool> SyncAsync(IStoreLibraryService service, StoreKind store, CancellationToken token)
    {
        if (service is not StoreLibraryService client) return false;
        var snapshot = await service.GetSnapshotAsync(token);
        var account = snapshot.Accounts.SingleOrDefault(a => a.Store == store && a.State == StoreConnectionState.Connected);
        if (account is null) return false;
        var request = await client.RequestSteamSyncAsync(store, account.Revision, token);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (request.State == "pending" && deadline.Elapsed < TimeSpan.FromMinutes(5))
        {
            await Task.Delay(500, token);
            request = await client.GetSteamSyncStatusAsync(request.RequestId, token);
        }
        return request.State == "completed" && request.Confirmed == request.Total;
    }
}
