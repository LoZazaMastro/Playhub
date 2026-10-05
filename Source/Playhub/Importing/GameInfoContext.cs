namespace Playhub.Importing;

/// <summary>Exact imported identity and host-owned artwork operations shared by native PC and emulated-game Info.</summary>
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
    Func<string,string>? DefaultArtworkProvider = null);
