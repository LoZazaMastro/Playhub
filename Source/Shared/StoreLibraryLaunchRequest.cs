// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

public static class StoreLibraryLaunchRequest
{
    public static bool TryParse(IReadOnlyList<string> args, string command, out StoreGameKey? key)
    {
        key = null;
        if (args.Count != 3 || args[0] != command || args[2].Length is < 1 or > 250 ||
            !args[2].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) return false;
        var store = args[1] switch { "epic" => StoreKind.Epic, "gog" => StoreKind.Gog, "xbox" => StoreKind.Xbox, "amazon" => StoreKind.Amazon, _ => (StoreKind?)null };
        if (store is null) return false;
        key = new(store.Value, args[2]); return true;
    }
}
