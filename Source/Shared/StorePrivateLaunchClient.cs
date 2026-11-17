// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed partial class StoreLibraryService
{
    // The server authenticates the exact installed GameSession binary for this
    // operation. App and Decky clients cannot request private launch material.
    internal Task<StorePrivateLaunchPlan> PreparePrivateLaunchAsync(StoreGameKey key, CancellationToken token = default) =>
        Call<StorePrivateLaunchPlan>("prepare-launch", new { key, requestId = Guid.NewGuid() }, token);
}
