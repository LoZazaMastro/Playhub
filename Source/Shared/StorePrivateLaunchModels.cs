// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

// Private GameSession-only IPC. These short-lived arguments/environment are never
// included in library snapshots, queue persistence, UI responses or diagnostics.
public sealed record StorePrivateLaunchPlan(Guid LaunchId, StoreGameKey Key, string GameExecutable,
    string WorkingDirectory, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment,
    DateTimeOffset ExpiresAt, string? XboxAumid = null,
    string? EpicNamespace = null, string? EpicCatalogItemId = null, string? EpicAppName = null);
