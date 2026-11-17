// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Security.Cryptography;
using System.Text.Json;

namespace Playhub.StoreLibrary;

internal sealed record StoreRuntimeClient(string Kind, string Path, string Hash);
internal static class StoreRuntimeClientPolicy
{
    internal static StoreRuntimeClient[] Read(JsonElement clients, string app, string deckyServices)
    {
        if (clients.ValueKind != JsonValueKind.Array || clients.GetArrayLength() is < 1 or > 8)
            throw new StoreOperationException("runtime_client_invalid");
        var result = new List<StoreRuntimeClient>();
        foreach (var client in clients.EnumerateArray())
        {
            var kind = client.GetProperty("kind").GetString();
            var hash = client.GetProperty("sha256").GetString();
            if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit)) throw new StoreOperationException("runtime_client_invalid");
            var name = client.GetProperty(kind == "decky" ? "fileName" : "path").GetString();
            var valid = kind switch
            {
                "app" => name is "Playhub.exe" or "Playhub.GameSession.exe",
                "decky" => name is "PluginLoader.exe" or "PluginLoader_noconsole.exe",
                "xbox-companion" => name == "Playhub.XboxSession.exe" && client.TryGetProperty("methods", out var methods)
                    && methods.ValueKind == JsonValueKind.Array && methods.GetArrayLength() == 1
                    && methods[0].ValueKind == JsonValueKind.String && methods[0].GetString() == "launch-descriptor",
                _ => false
            };
            if (!valid) throw new StoreOperationException("runtime_client_invalid");
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(kind == "decky" ? deckyServices : app, name!));
            if (result.Any(value => value.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) throw new StoreOperationException("runtime_client_invalid");
            result.Add(new(kind!, path, hash));
        }
        return result.ToArray();
    }
    internal static bool IsAuthorizedImage(IReadOnlyList<StoreRuntimeClient> clients, string image)
    {
        var path = System.IO.Path.GetFullPath(image);
        var identity = clients.SingleOrDefault(client => client.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (identity is null) return false;
        using var stream = File.OpenRead(path);
        return identity.Hash.Equals(Convert.ToHexString(SHA256.HashData(stream)), StringComparison.OrdinalIgnoreCase);
    }
    internal static bool AllowsMethod(IReadOnlyList<StoreRuntimeClient> clients, string image, string method)
    {
        var path = System.IO.Path.GetFullPath(image);
        var identity = clients.SingleOrDefault(client => client.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        // Unknown future same-user installers retain revocation-only quiescence.
        // Every other method still goes through pinned-image admission.
        if (identity?.Kind == "xbox-companion") return method == "launch-descriptor";
        if (identity?.Kind == "app" && System.IO.Path.GetFileName(identity.Path).Equals("Playhub.exe", StringComparison.OrdinalIgnoreCase))
            return method is "snapshot" or "connect-begin" or "connect-complete" or "disconnect" or "refresh" or
                "xbox-account-status" or "xbox-account-begin" or "xbox-account-poll" or "xbox-account-cancel" or "xbox-account-disconnect" or
                "xbox-account-owned" or "steam-sync-request" or "steam-sync-status" or "steam-migration-request" or
                "steam-migration-status" or "quiesce";
        return true;
    }
}
