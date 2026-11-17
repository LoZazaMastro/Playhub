// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

public sealed record StoreInstalledGameStatus(StoreGameKey Key, long AccountRevision, long LibraryRevision,
    bool IsInstalled, bool CanLaunch, bool CanUninstall, string? InstallPath, string? ErrorCode = null,
    string? UninstallErrorCode = null);

// A preparation is a short-lived, single-use confirmation for one verified install.
// The server retains the file manifest; the caller cannot supply a deletion list.
public sealed record StoreUninstallPreparation(StoreGameKey Key, long AccountRevision, long LibraryRevision,
    string? InstallPath, Guid? Nonce, DateTimeOffset ExpiresAt, bool CanUninstall, string? ErrorCode = null);

public interface IStoreInstalledGameService
{
    Task<StoreInstalledGameStatus> GetInstalledGameAsync(StoreGameKey key, CancellationToken cancellationToken = default);
    Task<StoreUninstallPreparation> PrepareUninstallAsync(StoreGameKey key, long expectedAccountRevision,
        long expectedLibraryRevision, CancellationToken cancellationToken = default);
    Task<StoreCommandResult> UninstallAsync(StoreUninstallPreparation preparation, Guid requestId,
        CancellationToken cancellationToken = default);
}
// Optional extension: newer clients bind the preparation to the exact path the
// user selected, while existing/fake installed-game services remain compatible.
public interface IStoreInstalledGamePathService
{
    Task<StoreUninstallPreparation> PrepareUninstallAtPathAsync(StoreGameKey key,long expectedAccountRevision,
        long expectedLibraryRevision,string expectedInstallPath,CancellationToken cancellationToken=default);
}
