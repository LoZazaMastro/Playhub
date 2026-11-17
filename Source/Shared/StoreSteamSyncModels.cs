// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public sealed record StoreSteamSyncRequest(Guid RequestId, StoreKind Store, long AccountRevision,
    string State, int Confirmed, int Total, string? ErrorCode = null);
public sealed record StoreSteamShortcutPlan(Guid RequestId, Guid ClaimId, StoreGameKey Key,
    long AccountRevision, string Title, string Executable, string Arguments, string StartDirectory, uint? AppId,
    uint? CreatedAppId = null, string? NativeApplicationId = null, string? ManifestExecutable = null, string? LibraryKind = null);
public sealed record StoreSteamShortcutAck(Guid RequestId, Guid ClaimId, uint AppId,
    string Executable, string Arguments, string StartDirectory);
public sealed record StoreSteamOwnedGame(uint AppId, StoreGameKey Key, string Executable, string Arguments,
    string StartDirectory, string Title, bool AccountCurrent, StoreLibraryGame? Game, StoreDownloadJob? Job,
    string? NativeApplicationId = null, string? ManifestExecutable = null);
public sealed record StoreSteamSyncPending(IReadOnlyList<StoreSteamShortcutPlan> Plans);
public sealed record StoreSteamNativeIdentity(StoreGameKey Key, long AccountRevision, string? NativeApplicationId,
    string? ManifestExecutable, string InstallPath, string RuntimeExecutable, string StartDirectory,
    string? GameExecutablePath = null, string? EpicNamespace = null, string? EpicCatalogItemId = null, string? LibraryKind = null,
    string? GameArguments = null, string? NativeLaunchKind = null);
public sealed record StoreSteamImportAdoption(StoreGameKey Key, long ExpectedAccountRevision, uint AppId,
    string Title, string Executable, string Arguments, string StartDirectory, uint? ExpectedOwnedAppId = null);
public sealed record StoreSteamImportAdoptionResult(bool Accepted, StoreSteamOwnedGame Owned);
public sealed record StoreSteamCollectionMigration(Guid RequestId, string Version, string? SteamAccountId,
    string State, int Confirmed, int Total, string? ErrorCode = null);

// Windows command-line parsing shared by the registry's exact native identity
// checks. This never launches a command or normalizes a user's game arguments.
public static class StoreSteamShortcutIdentity
{
    public static string UwpArguments(string aumid, string executable) => "--uwp " + Quote(aumid) + " " + Quote(executable) + " \"\"";
    public static string Quote(string value)
    {
        var result = new System.Text.StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    public static IReadOnlyList<string>? Tokens(string command)
    {
        if (command.Length > 32767 || command.Any(c => char.IsControl(c) && c != '\t')) return null;
        var tokens = new List<string>(); int index = 0;
        while (index < command.Length)
        {
            while (index < command.Length && char.IsWhiteSpace(command[index])) index++;
            if (index == command.Length) break;
            var value = new System.Text.StringBuilder(); bool quoted = false;
            while (index < command.Length && (quoted || !char.IsWhiteSpace(command[index])))
            {
                int slashes = 0;
                while (index < command.Length && command[index] == '\\') { slashes++; index++; }
                if (index < command.Length && command[index] == '"')
                {
                    value.Append('\\', slashes / 2);
                    if (slashes % 2 != 0) value.Append('"'); else quoted = !quoted;
                    index++;
                }
                else
                {
                    value.Append('\\', slashes);
                    if (index < command.Length && (quoted || !char.IsWhiteSpace(command[index]))) value.Append(command[index++]);
                }
            }
            if (quoted || tokens.Count >= 128) return null;
            tokens.Add(value.ToString());
        }
        return tokens;
    }
}
