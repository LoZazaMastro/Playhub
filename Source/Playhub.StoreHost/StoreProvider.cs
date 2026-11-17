// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed record ProviderAccount(string AccountIdentity, string? DisplayName);
public sealed record ProviderGame(string Id, string Title, bool CanInstall, long? RequiredBytes, string? InstallPath = null, bool Launchable = false, string? ArtworkUrl = null, IReadOnlyList<string>? ArtworkFallbackUrls = null, string? LibraryKind = null,string? InstallProvider=null);
public sealed record TransferProgress(long DownloadedBytes, long? TotalBytes, double? BytesPerSecond, string Phase, double? ProgressPercent = null);
public sealed record TransferCompletion(bool Completed, bool Checkpointed, bool InstallVerified, string? ErrorCode = null, bool Stopped = false, bool DrainConfirmed = true);
public sealed record TransferRequest(Guid JobId, StoreGameKey Key, string InstallPath, string? Language);
public sealed record ProviderInstallSize(long RequiredBytes, long DownloadBytes);
public sealed record ProviderLaunch(string Executable, string WorkingDirectory, IReadOnlyList<string> Arguments, string? GameExecutablePath = null);
internal interface IStoreRegisteredInstallation
{
    Task<bool> HasRegisteredInstallationAsync(string id,string installPath,CancellationToken token);
}

public interface IStoreTransfer : IAsyncDisposable
{
    Task<TransferCompletion> Completion { get; }
    // Success is not published until the actual owned worker has stopped.
    Task RequestCheckpointAsync(CancellationToken cancellationToken);
}

public interface IStoreProvider
{
    StoreKind Store { get; }
    StoreCapabilities Capabilities { get; }
    Uri AuthorizationUri { get; }
    Task<ProviderAccount?> GetAccountAsync(CancellationToken cancellationToken);
    Task<ProviderAccount> ConnectAsync(string authorizationCode, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken cancellationToken);
    Task<ProviderInstallSize> GetInstallSizeAsync(string id, CancellationToken cancellationToken);
    Task<ProviderLaunch?> ResolveLaunchAsync(string id, string installPath, CancellationToken cancellationToken);
    Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id, string installPath, CancellationToken cancellationToken);
    Task<IStoreTransfer> StartAsync(TransferRequest request, Action<TransferProgress> progress, CancellationToken cancellationToken);
}

// This provider authenticates through its own typed session protocol, never a
// fabricated authorization code passed to the generic code-exchange endpoint.
public interface IStoreTypedSignInProvider { }

public interface IStoreInstallFileSystem
{
    IReadOnlyList<StoreInstallLocation> Locations();
    string CanonicalDestination(string path);
    long FreeBytes(string path);
    bool CanClaim(string path, Guid? existingJob);
    bool Claim(string path, Guid job);
}

public sealed record StoreSteamActivity(Guid OwnerId, long Sequence, DateTimeOffset ObservedAt, bool ActiveLocalDownload);
public sealed class StoreOperationException(string code) : Exception(code) { public string Code { get; } = code; }
