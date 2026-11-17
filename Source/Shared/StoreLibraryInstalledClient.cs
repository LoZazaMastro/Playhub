// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed partial class StoreLibraryService : IStoreInstalledGameService,IStoreInstalledGamePathService
{
    public Task<StoreInstalledGameStatus> GetInstalledGameAsync(StoreGameKey key, CancellationToken cancellationToken = default) =>
        Call<StoreInstalledGameStatus>("installed-game", new { key }, cancellationToken);
    public Task<StoreUninstallPreparation> PrepareUninstallAsync(StoreGameKey key, long expectedAccountRevision,
        long expectedLibraryRevision, CancellationToken cancellationToken = default) =>
        Call<StoreUninstallPreparation>("prepare-uninstall", new { key, expectedAccountRevision, expectedLibraryRevision }, cancellationToken);
    public Task<StoreUninstallPreparation> PrepareUninstallAtPathAsync(StoreGameKey key,long expectedAccountRevision,
        long expectedLibraryRevision,string expectedInstallPath,CancellationToken cancellationToken=default)=>
        Call<StoreUninstallPreparation>("prepare-uninstall",new{key,expectedAccountRevision,expectedLibraryRevision,expectedInstallPath},cancellationToken);
    public Task<StoreCommandResult> UninstallAsync(StoreUninstallPreparation preparation, Guid requestId,
        CancellationToken cancellationToken = default) =>
        Call<StoreCommandResult>("uninstall", new { preparation, requestId }, cancellationToken);
}
