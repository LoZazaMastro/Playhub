// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
namespace Playhub.StoreLibrary;

public sealed partial class StoreSteamShortcutRegistry
{
    private static string MigrationVersion => (typeof(StoreSteamShortcutRegistry).Assembly.GetName().Version?.ToString(3) ?? "2.7.0") + ":store-collections-v4";
    private static bool ValidSteamAccount(string? value) => value is { Length: 17 } && value.All(char.IsAsciiDigit) &&
        ulong.TryParse(value, out var id) && id > 76561197960265728UL && id <= 76561197960265728UL + uint.MaxValue;
    private static string? ReadActiveSteamAccount()
    {
        try
        {
            using var valve = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            using var active = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (valve?.GetValue("SteamExe") is not string image || active?.GetValue("ActiveUser") is not int raw || raw == 0) return null;
            bool running = false;
            foreach (var process in Process.GetProcessesByName("steam"))
            {
                using (process) if (!process.HasExited && SamePath(process.MainModule?.FileName, image)) running = true;
            }
            return running ? (76561197960265728UL + unchecked((uint)raw)).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
    private StoreSteamCollectionMigration[] Migrations() => state.Migrations ?? [];
    private void SaveMigration(StoreSteamCollectionMigration value) => Commit(state with {
        Migrations = Migrations().Where(m => m.RequestId != value.RequestId).TakeLast(63).Append(value).ToArray() });
    private StoreSteamCollectionMigration RequestMigration()
    {
        string? account = activeSteamAccount();
        if (!ValidSteamAccount(account)) account = null;
        var existing = Migrations().LastOrDefault(m => m.Version == MigrationVersion && m.SteamAccountId == account);
        if (existing is not null && existing.State != "failed") return existing;
        var request = new StoreSteamCollectionMigration(Guid.NewGuid(), MigrationVersion, account, account is null ? "waitingSteam" : "pending", 0, 0);
        SaveMigration(request); return request;
    }
    private StoreSteamCollectionMigration BindMigration(StoreSteamCollectionMigration migration)
    {
        if (migration.State != "waitingSteam") return migration;
        string? account = activeSteamAccount(); if (!ValidSteamAccount(account)) return migration;
        var completed = Migrations().LastOrDefault(m => m.Version == migration.Version && m.SteamAccountId == account && m.State == "completed");
        var bound = migration with { SteamAccountId = account, State = completed is null ? "pending" : "completed",
            Confirmed = completed?.Confirmed ?? 0, Total = completed?.Total ?? 0 };
        SaveMigration(bound); return bound;
    }
    private StoreSteamCollectionMigration MigrationStatus(Guid id) => BindMigration(Migrations().SingleOrDefault(m => m.RequestId == id)
        ?? throw new InvalidOperationException("steam_migration_missing"));
    private IReadOnlyList<StoreSteamCollectionMigration> PendingMigrations()
    {
        foreach (var item in Migrations().Where(m => m.State == "waitingSteam").ToArray()) BindMigration(item);
        string? account = activeSteamAccount();
        return Migrations().Where(m => m.Version == MigrationVersion && m.SteamAccountId == account && account is not null && m.State == "pending").ToArray();
    }
    private StoreSteamCollectionMigration ReportMigration(JsonElement args)
    {
        var migration = MigrationStatus(args.GetProperty("requestId").GetGuid());
        string? account = args.GetProperty("steamAccountId").GetString();
        int confirmed = args.GetProperty("confirmed").GetInt32(), total = args.GetProperty("total").GetInt32();
        bool complete = args.GetProperty("complete").GetBoolean();
        if (migration.State == "completed" && complete && migration.SteamAccountId == account && activeSteamAccount() == account &&
            confirmed == migration.Confirmed && total == migration.Total) return migration;
        if (migration.State != "pending" || !ValidSteamAccount(account) || migration.SteamAccountId != account || activeSteamAccount() != account ||
            total < 0 || total > 50000 || confirmed < 0 || confirmed > total || complete && confirmed != total)
            throw new InvalidOperationException("steam_migration_unverified");
        string? error = args.TryGetProperty("errorCode", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (error is not null && (error.Length is < 1 or > 80 || error.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
            throw new InvalidOperationException("steam_migration_unverified");
        var updated = migration with { State = error is not null ? "failed" : complete ? "completed" : "pending", Confirmed = confirmed, Total = total, ErrorCode = error };
        SaveMigration(updated); return updated;
    }
}
