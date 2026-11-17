// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public enum StoreXboxSignInState { Pending, Connected, Denied, Expired, Canceled, Failed }
// The official URL already contains Microsoft's short verification code. The UI
// opens it directly without showing a code. The device credential stays private.
public sealed record StoreXboxSignInSession(Guid SessionId, Uri AuthorizationUri, DateTimeOffset ExpiresAt, int PollAfterSeconds);
public sealed record StoreXboxSignInResult(Guid SessionId, StoreXboxSignInState State, int PollAfterSeconds, StoreLibrarySnapshot Snapshot, string? ErrorCode = null);
public sealed record StoreXboxOwnedGame(string ProductId, string Title, bool SupportsPc, bool SupportsXbox, string AccessKind, string? ArtworkUrl = null,
    string? PackageFamilyName = null, long? RequiredBytes = null, IReadOnlyList<string>? ArtworkFallbackUrls = null, string? InstallProductId = null,string? InstallProvider=null,string? InstallProviderId=null);
public sealed record StoreXboxOwnedSnapshot(long Revision, DateTimeOffset? ObservedAt, bool IsStale, IReadOnlyList<StoreXboxOwnedGame> Games, string? ErrorCode = null);
public interface IStoreXboxAccountService
{
    Task<StoreXboxSignInSession> BeginXboxSignInAsync(long expectedAccountRevision, CancellationToken cancellationToken = default);
    Task<StoreXboxSignInResult> PollXboxSignInAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<StoreXboxSignInResult> CancelXboxSignInAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<StoreXboxOwnedSnapshot> GetXboxOwnedLibraryAsync(CancellationToken cancellationToken = default);
    Task<StoreXboxHandoffResult> OpenXboxOwnedInstallPageAsync(StoreXboxHandoffRequest request, CancellationToken cancellationToken = default);
}
