// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;

namespace Playhub.StoreLibrary;

/// <summary>One explicit Steam shortcut launch opens one installation view.</summary>
internal sealed class StoreUiIntentProtocol(Func<CancellationToken, Task<StoreLibrarySnapshot>> readSnapshot, Func<DateTimeOffset>? clock = null) : IStoreProtocolExtension
{
    private readonly object sync = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private StoreUiInstallIntent? pending;

    public async Task<(bool Handled, object? Data)> TryHandleAsync(string method, JsonElement args, CancellationToken token)
    {
        if (method == "ui-install-take")
        {
            token.ThrowIfCancellationRequested();
            lock (sync)
            {
                var intent = pending;
                pending = null; // Consume before navigation; Back never reopens this view.
                return (true, new StoreUiIntentReadback(intent?.ExpiresAt > now() ? intent : null));
            }
        }
        if (method != "ui-install-request") return (false, null);
        var key = args.GetProperty("key").Deserialize<StoreGameKey>(StoreLibraryService.JsonOptions);
        if (key is null || !Enum.IsDefined(key.Store) || string.IsNullOrWhiteSpace(key.Id) || key.Id.Length > 512
            || key.Id.Trim() != key.Id || key.Id.Any(char.IsControl)) return (true, new StoreUiOpenResult(false, "invalid_game"));
        var snapshot = await readSnapshot(token);
        token.ThrowIfCancellationRequested();
        var game = snapshot.Games.FirstOrDefault(game => game.Key == key);
        // This grants navigation only. The install command still validates the
        // current account, catalogue, destination and explicit confirmation.
        var intentValue = new StoreUiInstallIntent(Guid.NewGuid(), key, game?.IsInstalled == true ? "manage" : "install", now().AddSeconds(30));
        lock (sync) pending = intentValue;
        return (true, new StoreUiOpenResult(true));
    }
}
