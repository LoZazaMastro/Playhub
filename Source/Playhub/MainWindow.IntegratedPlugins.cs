using Playhub.Models;
using System;
using System.IO;

namespace Playhub;

public sealed partial class MainWindow
{
    private static bool IsIntegratedGamingModePlugin(DeckyPluginInfo plugin)
        => string.Equals(plugin.Name, "Gaming Mode", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(plugin.Name, "Playhub Gaming Mode", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(Path.GetFileName((plugin.InstalledFolder ?? "").TrimEnd('\\', '/')),
               "gaming-mode", StringComparison.OrdinalIgnoreCase);
}
