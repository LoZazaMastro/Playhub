// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.IO;
namespace Playhub.Shared;

// Only installed catalog identity crosses the broker. No URI, command line,
// handler executable, environment, working directory or elevation override.
internal sealed record EpicProtocolLaunchRequest(int Version, string RequestId, string Namespace,
    string CatalogItemId, string AppName, string GameExecutable, string ManifestIdentitySha256)
{
    internal static EpicProtocolLaunchRequest Create(EpicInstalledLaunch launch) => new(1,
        Guid.NewGuid().ToString("N"), launch.Namespace, launch.CatalogItemId, launch.AppName,
        launch.GameExecutable, launch.ManifestIdentitySha256);

    internal bool Valid => Version == 1 && RequestId is { Length: 32 } && Guid.TryParseExact(RequestId, "N", out var id) && id.ToString("N") == RequestId
        && Namespace is not null && CatalogItemId is not null && AppName is not null
        && EpicLaunchCatalog.Identifier(Namespace) && EpicLaunchCatalog.Identifier(CatalogItemId)
        && EpicLaunchCatalog.Identifier(AppName) && GameExecutable is not null && !GameExecutable.Any(char.IsControl) && EpicLaunchCatalog.LocalAbsolute(GameExecutable)
        && Path.GetExtension(GameExecutable).Equals(".exe", StringComparison.OrdinalIgnoreCase)
        && ManifestIdentitySha256 is { Length: 64 } && ManifestIdentitySha256.All(Uri.IsHexDigit);

    internal bool Matches(EpicInstalledLaunch plan) => Valid && plan.Namespace == Namespace && plan.CatalogItemId == CatalogItemId
        && plan.AppName == AppName && plan.GameExecutable.Equals(GameExecutable, StringComparison.OrdinalIgnoreCase)
        && plan.ManifestIdentitySha256.Equals(ManifestIdentitySha256, StringComparison.OrdinalIgnoreCase)
        && plan.LaunchUri == "com.epicgames.launcher://apps/" + string.Join("%3A",
            new[] { Namespace, CatalogItemId, AppName }.Select(Uri.EscapeDataString)) + "?action=launch&silent=true";
}

internal sealed record EpicProtocolLaunchResult(bool Ok, string Code);
