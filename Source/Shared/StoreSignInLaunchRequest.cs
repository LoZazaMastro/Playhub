// SPDX-License-Identifier: MIT.
using System.Collections.Generic;

namespace Playhub.StoreLibrary;

public static class StoreSignInLaunchRequest
{
    public static bool TryParse(IReadOnlyList<string> arguments, out StoreKind store)
    {
        store = default;
        if (arguments.Count != 2 || arguments[0] != "--store-sign-in") return false;
        switch (arguments[1])
        {
            case "epic": store = StoreKind.Epic; return true;
            case "gog": store = StoreKind.Gog; return true;
            case "xbox": store = StoreKind.Xbox; return true;
            case "amazon": store = StoreKind.Amazon; return true;
            default: return false;
        }
    }
}
