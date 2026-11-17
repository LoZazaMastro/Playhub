// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using Playhub.Shared;
using Playhub.StoreLibrary;

namespace Playhub.GameSession;

internal static class StoreOfficialEpicLaunch
{
    internal static int? TryRun(StorePrivateLaunchPlan plan, Func<EpicInstalledLaunch, int> activate,
        Func<string, string, string, string, EpicInstalledLaunch?>? resolve = null)
    {
        if (plan.EpicNamespace is null && plan.EpicCatalogItemId is null && plan.EpicAppName is null) return null;
        return activate(Resolve(plan, resolve));
    }

    internal static EpicInstalledLaunch Resolve(StorePrivateLaunchPlan plan,
        Func<string, string, string, string, EpicInstalledLaunch?>? resolve = null)
    {
        if (plan.Key.Store != StoreKind.Epic || plan.EpicNamespace is not { } space ||
            plan.EpicCatalogItemId is not { } item || plan.EpicAppName is not { } app || app != plan.Key.Id ||
            !EpicShortcutArguments.HasCatalog(space, item, app) ||
            plan.Arguments.Count != 0 || plan.Environment.Count != 0 || plan.XboxAumid is not null)
            throw new InvalidDataException("The official Epic launch identity was not verified.");
        var installed = (resolve ?? EpicLaunchCatalog.ResolveInstalled)(space, item, app, plan.GameExecutable);
        if (installed is null || installed.Namespace != space || installed.CatalogItemId != item || installed.AppName != plan.Key.Id ||
            !Path.GetFullPath(installed.GameExecutable).Equals(Path.GetFullPath(plan.GameExecutable), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installed Epic manifest is no longer current.");
        return installed;
    }
}
