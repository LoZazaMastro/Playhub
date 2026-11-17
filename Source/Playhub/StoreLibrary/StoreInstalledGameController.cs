// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

internal enum StoreInstalledActionResult { Requested, Completed, Canceled, Unavailable, Stale, Busy, Failed }

/// <summary>One view-owned action. The host owns installation identity and deletion.</summary>
internal sealed class StoreInstalledGameController(
    IStoreInstalledGameService service,
    IStoreLibraryService library,
    Func<StoreLibraryGame, bool> current,
    Func<StoreLaunchDescriptor, CancellationToken, Task<bool>> launch,
    Func<DateTimeOffset>? clock = null) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private int pending;
    private bool disposed;
    public bool Busy => Volatile.Read(ref pending) != 0;
    public event EventHandler? Changed;
    private bool Current(StoreLibraryGame game) => !disposed && !lifetime.IsCancellationRequested && current(game);
    internal static bool Supported(StoreLibraryGame game) => game.IsInstalled
        && game.Key.Store is StoreKind.Epic or StoreKind.Gog or StoreKind.Amazon or StoreKind.Xbox && SafeKey(game.Key) && !string.IsNullOrWhiteSpace(game.InstallPath);
    internal static bool SafeKey(StoreGameKey key) => key.Id is { Length: > 0 and <= 250 }
        && key.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    internal static bool SamePath(string? a, string? b)
    {
        try { return a is not null && b is not null && Path.IsPathFullyQualified(a) && Path.IsPathFullyQualified(b)
            && StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b))); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    private static bool Matches(StoreLibraryGame game, StoreInstalledGameStatus status) => status.Key == game.Key
        && status.AccountRevision == game.AccountRevision && status.LibraryRevision >= 0 && status.IsInstalled
        && status.ErrorCode is null && SamePath(status.InstallPath, game.InstallPath);

    public async Task<StoreInstalledGameStatus?> ReadStatusAsync(StoreLibraryGame game)
    {
        if (!Supported(game) || !Current(game)) return null;
        try
        {
            var value = await service.GetInstalledGameAsync(game.Key, lifetime.Token);
            return Current(game) && Matches(game, value) ? value : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return null; }
    }
    private bool Begin(StoreLibraryGame game)
    {
        if (!Supported(game) || !Current(game) || Interlocked.CompareExchange(ref pending, 1, 0) != 0) return false;
        Changed?.Invoke(this, EventArgs.Empty); return true;
    }
    private void End() { Interlocked.Exchange(ref pending, 0); if (!disposed) Changed?.Invoke(this, EventArgs.Empty); }

    public async Task<StoreInstalledActionResult> PlayAsync(StoreLibraryGame game)
    {
        if (!Begin(game)) return Busy ? StoreInstalledActionResult.Busy : StoreInstalledActionResult.Stale;
        try
        {
            var status = await ReadStatusAsync(game);
            if (status is null || !status.CanLaunch) return StoreInstalledActionResult.Unavailable;
            var descriptor = await library.GetVerifiedLaunchDescriptorAsync(game.Key, lifetime.Token);
            if (!Current(game) || descriptor.Key != game.Key || descriptor.AccountRevision != game.AccountRevision
                || descriptor.LibraryRevision < status.LibraryRevision || !descriptor.IsInstalled || !descriptor.IsLaunchable || descriptor.ErrorCode is not null)
                return StoreInstalledActionResult.Stale;
            return await launch(descriptor, lifetime.Token) ? StoreInstalledActionResult.Requested : StoreInstalledActionResult.Failed;
        }
        catch (OperationCanceledException) { return StoreInstalledActionResult.Canceled; }
        catch (Exception e) when (e is not OutOfMemoryException) { return StoreInstalledActionResult.Failed; }
        finally { End(); }
    }
    public async Task<StoreInstalledActionResult> UninstallAsync(StoreLibraryGame game,
        Func<StoreUninstallPreparation, CancellationToken, Task<bool>> confirm)
    {
        if (!Begin(game)) return Busy ? StoreInstalledActionResult.Busy : StoreInstalledActionResult.Stale;
        try
        {
            // Removal uses a fresh explicit server preparation. A failed launch
            // or advisory status read must not prevent recovering an install.
            var snapshot = await library.GetSnapshotAsync(lifetime.Token);
            if (!Current(game) || !StoreLibraryController.Valid(snapshot)) return StoreInstalledActionResult.Stale;
            var preparation = service is IStoreInstalledGamePathService scoped
                ?await scoped.PrepareUninstallAtPathAsync(game.Key,game.AccountRevision,snapshot.Revision,game.InstallPath!,lifetime.Token)
                :await service.PrepareUninstallAsync(game.Key, game.AccountRevision, snapshot.Revision, lifetime.Token);
            bool Valid() => Current(game) && preparation.Key == game.Key && preparation.AccountRevision == game.AccountRevision
                && preparation.LibraryRevision >= snapshot.Revision && preparation.CanUninstall && preparation.Nonce is Guid nonce && nonce != Guid.Empty
                && preparation.ErrorCode is null && preparation.ExpiresAt > now() && SamePath(preparation.InstallPath, game.InstallPath);
            if (!Valid()) return StoreInstalledActionResult.Stale;
            if (!await confirm(preparation, lifetime.Token)) return StoreInstalledActionResult.Canceled;
            // The dialog can reenter, change account, replace the game or outlive its nonce.
            if (!Valid()) return StoreInstalledActionResult.Stale;
            var result = await service.UninstallAsync(preparation, Guid.NewGuid(), lifetime.Token);
            if (!result.Accepted || !StoreLibraryController.Valid(result.Snapshot)
                || result.Snapshot.Revision < preparation.LibraryRevision
                || result.Snapshot.Games.Any(g => g.Key == game.Key && g.IsInstalled)) return StoreInstalledActionResult.Failed;
            return StoreInstalledActionResult.Completed;
        }
        catch (OperationCanceledException) { return StoreInstalledActionResult.Canceled; }
        catch (Exception e) when (e is not OutOfMemoryException) { return StoreInstalledActionResult.Failed; }
        finally { End(); }
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); }
}

