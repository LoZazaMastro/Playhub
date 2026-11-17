// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

public sealed record StoreUiOpenResult(bool Accepted, string? ErrorCode = null);
public sealed record StoreUiInstallIntent(Guid RequestId, StoreGameKey Key, string Action, DateTimeOffset ExpiresAt);
public sealed record StoreUiIntentReadback(StoreUiInstallIntent? Intent);

public sealed partial class StoreLibraryService
{
    public Task<StoreUiOpenResult> RequestInstallUiAsync(StoreGameKey key, CancellationToken cancellationToken = default) =>
        Call<StoreUiOpenResult>("ui-install-request", new { key }, cancellationToken);
}
