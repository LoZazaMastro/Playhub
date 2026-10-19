namespace Playhub.Importing;

/// <summary>Exact imported PC identity and host-owned artwork operations.</summary>
public sealed record GameInfoContext(
    string Title,
    uint ShortcutAppId,
    Func<CancellationToken, Task<IReadOnlyDictionary<string, string>>> ReadArtworkAsync,
    Func<string, string, string, CancellationToken, Task<IReadOnlyList<ImportArtworkResult>>> SearchStoreArtworkAsync,
    Func<string, ImportArtworkResult, CancellationToken, Task> ChooseArtworkAsync,
    string? StableIdentity = null,
    Func<CancellationToken,Task<IReadOnlyList<string>>>? GetSteamGridDirectoriesAsync = null,
    Func<string,CancellationToken,Task>? RemoveArtworkAsync = null,
    Action? DisposeResources = null,
    Func<string,CancellationToken,Task<ImportArtworkResult?>>? ReadArtworkSourceAsync = null,
    Func<string,string>? DefaultArtworkProvider = null,
    Func<string,string,CancellationToken,Task<IReadOnlyList<Playhub.Integrations.GameTitleIdentity>>>? SearchTitlesAsync = null,
    Func<Playhub.Integrations.GameTitleIdentity,CancellationToken,Task>? RefetchTitleAsync = null,
    Func<string,CancellationToken,Task<string>>? ReadTitleQueryAsync = null);
