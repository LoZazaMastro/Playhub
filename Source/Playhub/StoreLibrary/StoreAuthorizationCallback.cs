// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Playhub.StoreLibrary;

internal static class StoreAuthorizationCallback
{
    private static bool Endpoint(Uri uri, string host, string path) => uri.IsAbsoluteUri
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0 && uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath == path;

    internal static bool IsEpicResponse(Uri uri) => Endpoint(uri, "www.epicgames.com", "/id/api/redirect")
        || Endpoint(uri, "epicgames.com", "/id/api/redirect");

    internal static bool TryReadRedirect(StoreKind store, Uri uri, out string code)
    {
        code = "";
        string key;
        if (store == StoreKind.Gog && Endpoint(uri, "embed.gog.com", "/on_login_success")) key = "code";
        else if (store == StoreKind.Amazon && (Endpoint(uri, "www.amazon.com", "/") || Endpoint(uri, "amazon.com", "/")
            || Endpoint(uri, "www.amazon.com", "/ap/maplanding") || Endpoint(uri, "amazon.com", "/ap/maplanding")))
            key = "openid.oa2.authorization_code";
        else return false;
        string? found = null;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = pair.Split('=', 2);
            if (Uri.UnescapeDataString(pieces[0]) != key) continue;
            if (found is not null || pieces.Length != 2) return false;
            found = Uri.UnescapeDataString(pieces[1]);
        }
        if (!ValidCode(found)) return false;
        code = found!; return true;
    }

    internal static bool TryReadEpicResponse(Uri uri, string body, out string code)
    {
        code = "";
        if (!IsEpicResponse(uri) || body.Length > 32768) return false;
        try
        {
            using var parsed = JsonDocument.Parse(body);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty("authorizationCode", out var value)
                || value.ValueKind != JsonValueKind.String || !ValidCode(value.GetString())) return false;
            code = value.GetString()!; return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool ValidCode(string? code) => code is { Length: > 0 and <= 4096 }
        && !System.Linq.Enumerable.Any(code, c => char.IsControl(c) || char.IsWhiteSpace(c));
}
