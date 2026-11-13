namespace GamingMode.Services;

// Canonical identity remains the installed wrapper's authenticated launch.
internal static partial class Win32SteamSessionAssociation
{
    private static readonly object OwnedSync = new();
    private static Win32OwnedAssociationCatalog? owned;
    private static bool ownedStopped;
    private static Win32OwnedAssociationCatalog? Owned
    {
        get { lock (OwnedSync) return ownedStopped ? null : owned ??= new(new WindowsOwnedAssociationRuntime()); }
    }
    internal static Task<Win32OwnedAssociationCatalog.Lease?> FindLeaseAsync(uint appId, CancellationToken cancellation)
        => Owned?.FindAsync(appId, cancellation) ?? Task.FromResult<Win32OwnedAssociationCatalog.Lease?>(null);
    internal static async Task<Identity?> FindAsync(uint appId, CancellationToken cancellation)
    {
        using var lease = await FindLeaseAsync(appId, cancellation).ConfigureAwait(false);
        return lease?.Current == true ? lease.Identity : null;
    }
    internal static Task<OwnedWin32Route?> DiscoverOwnedRouteAsync(uint observedAppId, CancellationToken cancellation)
        => Owned?.DiscoverAsync(observedAppId, cancellation) ?? Task.FromResult<OwnedWin32Route?>(null);
    internal static Task<bool> ProveOrdinaryForegroundAsync(uint? observedAppId, CancellationToken cancellation)
        => Owned?.ProveOrdinaryForegroundAsync(observedAppId, cancellation) ?? Task.FromResult(false);
    internal static Task<OwnedWin32Route?> DiscoverUiRouteAsync(uint? observed, uint? candidate, CancellationToken cancellation)
        => Owned?.DiscoverUiAsync(observed, candidate, cancellation) ?? Task.FromResult<OwnedWin32Route?>(null);
    internal static Task<bool> ProveSteamUiForegroundAsync(CancellationToken cancellation)
        => Owned?.ProveSteamUiForegroundAsync(cancellation) ?? Task.FromResult(false);
    internal static void DisposeOwned()
    {
        Win32OwnedAssociationCatalog? previous;
        lock (OwnedSync) { ownedStopped = true; previous = owned; owned = null; }
        previous?.Dispose();
    }
}
