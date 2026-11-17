// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;
public sealed partial class StoreLibraryService : IStoreXboxCatalogService, IStoreXboxInstalledService
{
    public Task<StoreXboxCatalogSnapshot> GetXboxCatalogAsync(CancellationToken cancellationToken = default) => Call<StoreXboxCatalogSnapshot>("xbox-catalog", new { }, cancellationToken);
    public Task<StoreXboxCatalogSnapshot> RefreshXboxCatalogAsync(bool force = false, CancellationToken cancellationToken = default) => Call<StoreXboxCatalogSnapshot>("xbox-refresh", new { force }, cancellationToken);
    public Task<StoreXboxHandoffResult> OpenXboxInstallPageAsync(StoreXboxHandoffRequest request, CancellationToken cancellationToken = default) => Call<StoreXboxHandoffResult>("xbox-install-handoff", request, cancellationToken);
    public Task<StoreXboxInstalledSnapshot> GetXboxInstalledLibraryAsync(CancellationToken cancellationToken = default) => Call<StoreXboxInstalledSnapshot>("xbox-installed", new { }, cancellationToken);
    public Task<StoreXboxInstalledSnapshot> RefreshXboxInstalledLibraryAsync(CancellationToken cancellationToken = default) => Call<StoreXboxInstalledSnapshot>("xbox-installed-refresh", new { }, cancellationToken);
    public Task<StoreXboxAppResult> OpenXboxAppAsync(StoreXboxAppRequest request, CancellationToken cancellationToken = default) => Call<StoreXboxAppResult>("xbox-open-official", request, cancellationToken);
}
