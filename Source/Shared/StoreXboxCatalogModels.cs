// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

// Public catalogue membership is independent from account ownership/subscription.
public sealed record StoreXboxCatalogGame(string ProductId, string Title, string? ArtworkUrl,
    bool IsCurrentMember, string PcAvailability, string EntitlementState = "not-checked",
    string InstalledState = "not-checked", bool CanOpenOfficialPage = true,string? InstallProvider=null,string? InstallProviderId=null);
public sealed record StoreXboxCatalogSnapshot(long Revision, string SourceRevision, string Source,
    string Market, string Language, string Platform, string Plan, DateTimeOffset? ValidatedAt,
    DateTimeOffset? ExpiresAt, bool IsStale, IReadOnlyList<StoreXboxCatalogGame> Games,
    string? ErrorCode = null, string? SourceWarning = null, bool CanRequestInstall = false);
public sealed record StoreXboxHandoffRequest(string ProductId, long ExpectedRevision, Guid RequestId, string? VolumeName = null);
public sealed record StoreXboxHandoffResult(bool Accepted, string State, string? ErrorCode,
    string ProductId, Guid RequestId, string LocationChoice = "official-client",
    string InstallState = "not-observed", string EntitlementState = "not-checked");
public interface IStoreXboxCatalogService
{
    Task<StoreXboxCatalogSnapshot> GetXboxCatalogAsync(CancellationToken cancellationToken = default);
    Task<StoreXboxCatalogSnapshot> RefreshXboxCatalogAsync(bool force = false, CancellationToken cancellationToken = default);
    Task<StoreXboxHandoffResult> OpenXboxInstallPageAsync(StoreXboxHandoffRequest request, CancellationToken cancellationToken = default);
}


// Current-user package registration is not Microsoft account purchase ownership.
public sealed record StoreXboxInstalledGame(string Aumid,string Title,string PackageFamilyName,
    string InstalledState="registered-current-user",string OwnershipState="not-checked",string AccessState="not-checked");
public sealed record StoreXboxInstalledSnapshot(long Revision,DateTimeOffset? ObservedAt,
    IReadOnlyList<StoreXboxInstalledGame> Games,bool OfficialAppInstalled,bool IsStale=true,
    string AccountState="external-client-not-observed",string Scope="current-windows-user-installed",string? ErrorCode=null);
public sealed record StoreXboxAppRequest(long ExpectedRevision,Guid RequestId);
public sealed record StoreXboxAppResult(bool Accepted,string State,string? ErrorCode,Guid RequestId,
    string AccountState="external-client-not-observed");
public interface IStoreXboxInstalledService
{
    Task<StoreXboxInstalledSnapshot> GetXboxInstalledLibraryAsync(CancellationToken cancellationToken=default);
    Task<StoreXboxInstalledSnapshot> RefreshXboxInstalledLibraryAsync(CancellationToken cancellationToken=default);
    Task<StoreXboxAppResult> OpenXboxAppAsync(StoreXboxAppRequest request,CancellationToken cancellationToken=default);
}
