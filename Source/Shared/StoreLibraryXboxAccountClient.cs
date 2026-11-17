// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed partial class StoreLibraryService : IStoreXboxAccountService
{
    public Task<StoreXboxSignInSession> BeginXboxSignInAsync(long expectedAccountRevision, CancellationToken cancellationToken = default)
        => Call<StoreXboxSignInSession>("xbox-account-begin", new { expectedAccountRevision }, cancellationToken);
    public Task<StoreXboxSignInResult> PollXboxSignInAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => Call<StoreXboxSignInResult>("xbox-account-poll", new { sessionId }, cancellationToken);
    public Task<StoreXboxSignInResult> CancelXboxSignInAsync(Guid sessionId, CancellationToken cancellationToken = default)
        => Call<StoreXboxSignInResult>("xbox-account-cancel", new { sessionId }, cancellationToken);
    public Task<StoreXboxOwnedSnapshot> GetXboxOwnedLibraryAsync(CancellationToken cancellationToken = default)
        => Call<StoreXboxOwnedSnapshot>("xbox-account-owned", new { }, cancellationToken);
    public Task<StoreXboxHandoffResult> OpenXboxOwnedInstallPageAsync(StoreXboxHandoffRequest request, CancellationToken cancellationToken = default)
        => Call<StoreXboxHandoffResult>("xbox-account-open-owned", request, cancellationToken);
}
