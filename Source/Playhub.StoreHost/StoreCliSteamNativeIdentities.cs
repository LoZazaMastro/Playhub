// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Text.Json;
namespace Playhub.StoreLibrary;

public sealed partial class StoreCliProvider
{
    // The private provider's installed record and official metadata establish
    // the product/executable identity. Titles never participate in adoption.
    public Task<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>> ReadSteamNativeIdentitiesAsync(
        IReadOnlyList<StoreLibraryGame> games, string wrapper, CancellationToken token)
    {
        var result = new Dictionary<StoreGameKey, StoreSteamNativeIdentity>();
        StoreOfficialInventory? official = null;
        foreach (var game in games.Where(g => g.Key.Store == Store && g.IsInstalled))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                ValidateId(game.Key.Id);
                var installed = Store == StoreKind.Epic ? ReadEpicInstall(game.Key.Id) : ReadGogInstall(game.Key.Id);
                if (installed is null)
                {
                    var native = (official ??= OfficialInstallations.Read(Store, token)).Find(game.Key.Id);
                    if (native is null || game.InstallPath is null || !SameOfficialPath(native.InstallPath, game.InstallPath)) continue;
                    result.Add(game.Key, new(game.Key, game.AccountRevision, null, null, native.InstallPath,
                        Path.GetFullPath(wrapper), native.WorkingDirectory, native.Executable, native.EpicNamespace,
                        native.EpicCatalogItemId, GameArguments: native.GameArguments, NativeLaunchKind: "official"));
                    continue;
                }
                if (installed is null || game.InstallPath is null || !Path.IsPathFullyQualified(game.InstallPath) ||
                    !StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.InstallPath)),
                        Path.TrimEndingDirectorySeparator(installed.Path))) continue;
                string? space = null, item = null;
                string working = Path.GetDirectoryName(installed.Executable)!;
                if (Store == StoreKind.Epic)
                {
                    string metadataPath = Path.Combine(Path.GetDirectoryName(nativeAuth)!, "metadata", game.Key.Id + ".json");
                    if (File.Exists(metadataPath))
                    {
                        if (new FileInfo(metadataPath).Length > 4 * 1024 * 1024) continue;
                        using var document = JsonDocument.Parse(File.ReadAllBytes(metadataPath)); var data = document.RootElement;
                        if (data.GetProperty("app_name").GetString() != game.Key.Id) continue;
                        var metadata = data.GetProperty("metadata");
                        space = metadata.GetProperty("namespace").GetString(); item = metadata.GetProperty("id").GetString();
                        static bool Identifier(string? value) => value is { Length: > 0 and <= 256 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
                        if (!Identifier(space) || !Identifier(item)) continue;
                    }
                }
                else
                {
                    using var info = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(installed.Path, "goggame-" + game.Key.Id + ".info")));
                    var primary = info.RootElement.GetProperty("playTasks").EnumerateArray().Single(t => t.TryGetProperty("isPrimary", out var p) && p.ValueKind == JsonValueKind.True);
                    string relative = primary.TryGetProperty("workingDir", out var directory) && directory.ValueKind == JsonValueKind.String ? directory.GetString() ?? "" : "";
                    working = string.IsNullOrWhiteSpace(relative) ? installed.Path : Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar), installed.Path);
                    if (!(working.Equals(installed.Path, StringComparison.OrdinalIgnoreCase) || working.StartsWith(Path.TrimEndingDirectorySeparator(installed.Path) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) || !Directory.Exists(working)) continue;
                }
                result.Add(game.Key, new(game.Key, game.AccountRevision, null, null, installed.Path, Path.GetFullPath(wrapper), working,
                    installed.Executable, space, item));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException) { }
        }
        return Task.FromResult<IReadOnlyDictionary<StoreGameKey, StoreSteamNativeIdentity>>(result);
    }
}
