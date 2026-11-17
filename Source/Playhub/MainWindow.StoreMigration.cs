// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using Microsoft.UI.Xaml;
using Playhub.StoreLibrary;
using System.Threading;

namespace Playhub;

public sealed partial class MainWindow
{
    private StoreMigrationController? _storeMigration;
    private readonly CancellationTokenSource _storeMigrationLifetime = new();
    private DispatcherTimer? _storeMigrationTimer;
    private DateTimeOffset _storeMigrationNextRetry;

    private async Task InitializeStoreMigrationAsync()
    {
        if (_storeMigration is not null || _storeMigrationLifetime.IsCancellationRequested) return;
        var service = StoreLibraryService.CreateDefault();
        _storeMigration = new(service.RequestSteamMigrationAsync, service.GetSteamMigrationStatusAsync);
        _storeMigrationTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        _storeMigrationTimer.Tick += async (_, _) =>
        {
            if (_storeMigration.Busy || _storeMigrationLifetime.IsCancellationRequested) return;
            bool retry = _storeMigration.ReadFailed || _storeMigration.Snapshot?.State == "failed";
            if (!_storeMigration.NeedsPolling && (!retry || DateTimeOffset.UtcNow < _storeMigrationNextRetry)) return;
            _storeMigrationNextRetry = DateTimeOffset.UtcNow.AddSeconds(30);
            await _storeMigration.RefreshAsync();
        };
        Closed += (_, _) =>
        {
            _storeMigrationLifetime.Cancel(); _storeMigrationTimer.Stop(); _storeMigration.Dispose();
        };
        await _storeMigration.StartAsync();
        if (_storeMigrationLifetime.IsCancellationRequested) return;
        _storeMigrationNextRetry = DateTimeOffset.UtcNow.AddSeconds(30);
        _storeMigrationTimer.Start();
        RenderStoreMigration();
    }

    // Collection organization is automatic background work, with no dialog,
    // announcement, progress banner or Retry/Later controls in the app.
    private void RenderStoreMigration() => _storeLibraryPage?.SetMigrationStatus(null, "", static () => { });
}
