// SPDX-License-Identifier: MIT. Original Playhub public catalogue view lifetime.
using System.Text.RegularExpressions;

namespace Playhub.StoreLibrary;

internal sealed class XboxGamePassController(IStoreXboxCatalogService service) : IDisposable
{
    private CancellationTokenSource? operation;
    private long epoch;
    private bool disposed;
    internal StoreXboxCatalogSnapshot? Snapshot { get; private set; }
    internal bool Busy { get; private set; }
    internal string? ErrorKey { get; private set; }
    internal string? NoticeKey { get; private set; }
    internal event EventHandler? Changed;
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    private bool Current(long expected) => !disposed && epoch == expected;

    internal static bool Valid(StoreXboxCatalogSnapshot? value) => value is not null
        && value.Revision >= 0 && value.Platform == "pc" && value.Plan == "pc"
        && value.Source == "xbox-web-gamepass-pc-sigl-v3:609d944c-d395-4c0a-9ea4-e9f39b52c1ad"
        && value.Market is not null && Regex.IsMatch(value.Market, "^[A-Z]{2}$")
        && value.Language is not null && Regex.IsMatch(value.Language, "^[a-z]{2}-[A-Z]{2}$")
        && value.Games is not null && value.Games.Count <= 10000
        && value.Games.All(game => game is not null && game.ProductId is not null
            && Regex.IsMatch(game.ProductId, "^[A-Z0-9]{12}$")
            && !string.IsNullOrWhiteSpace(game.Title) && game.Title.Length <= 2048
            && game.EntitlementState == "not-checked" && game.InstalledState == "not-checked"
            && game.PcAvailability is "desktop-confirmed" or "not-desktop" or "not-confirmed")
        && value.Games.Select(game => game.ProductId).Distinct().Count() == value.Games.Count;

    internal IEnumerable<StoreXboxCatalogGame> VisibleGames(string? query) =>
        (Snapshot?.Games ?? []).Where(game => game.IsCurrentMember && game.PcAvailability != "not-desktop"
            && game.Title.Contains(query ?? "", StringComparison.CurrentCultureIgnoreCase))
        .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase);

    internal Task ReadAsync() => ReadOwnedAsync(false, false);
    internal Task RefreshAsync() => ReadOwnedAsync(true, true);
    internal Task RefreshIfStaleAsync() => ReadOwnedAsync(true, false);
    private async Task ReadOwnedAsync(bool refresh, bool force)
    {
        if (disposed || Busy) return;
        var ticket = ++epoch;
        using var attempt = new CancellationTokenSource(); operation = attempt;
        Busy = true; ErrorKey = NoticeKey = null; Notify();
        try
        {
            var next = refresh ? await service.RefreshXboxCatalogAsync(force, attempt.Token)
                : await service.GetXboxCatalogAsync(attempt.Token);
            if (!Current(ticket)) return;
            if (!Valid(next) || Snapshot is { } previous && next.Revision < previous.Revision)
            { ErrorKey = "GamePass.RefreshFailed"; return; }
            Snapshot = next;
            if (next.ErrorCode is not null && next.ErrorCode != "catalogue_not_loaded")
                ErrorKey = "GamePass.RefreshFailed";
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (Current(ticket)) ErrorKey = "GamePass.RefreshFailed"; }
        finally
        {
            if (Current(ticket)) { Busy = false; operation = null; Notify(); }
        }
    }
    internal async Task<bool> OpenAsync(string productId, long expectedRevision)
    {
        if (disposed || Busy || Snapshot is not { } held || held.Revision != expectedRevision
            || !held.Games.Any(game => game.ProductId == productId && game.IsCurrentMember
                && game.PcAvailability != "not-desktop" && game.CanOpenOfficialPage)) return false;
        var ticket = ++epoch; var requestId = Guid.NewGuid();
        using var attempt = new CancellationTokenSource(); operation = attempt;
        Busy = true; ErrorKey = NoticeKey = null; Notify();
        try
        {
            var result = await service.OpenXboxInstallPageAsync(new(productId, expectedRevision, requestId), attempt.Token);
            if (!Current(ticket)) return false;
            if (result.ProductId != productId || result.RequestId != requestId
                || result.InstallState != "not-observed" || result.EntitlementState != "not-checked"
                || result.LocationChoice != "official-client")
            { ErrorKey = "GamePass.OpenUnknown"; return false; }
            if (!result.Accepted || result.State != "official-page-requested")
            { ErrorKey = result.State == "outcome-unknown" ? "GamePass.OpenUnknown" : "GamePass.OpenFailed"; return false; }
            NoticeKey = "GamePass.OpenRequested"; return true;
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested) { return false; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (Current(ticket)) ErrorKey = "GamePass.OpenUnknown"; return false; }
        finally { if (Current(ticket)) { Busy = false; operation = null; Notify(); } }
    }
    internal void CancelPending()
    {
        if (disposed) return;
        ++epoch; operation?.Cancel(); operation = null; Busy = false; Notify();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; ++epoch; operation?.Cancel(); operation = null; Busy = false; Changed = null;
        // This never cancels a Store download or asserts that an official page closed.
    }
}
