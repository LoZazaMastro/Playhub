// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Playhub.Shared;

internal static class StoreGdkLaunchDefinition
{
    // GDK permits Id to be omitted: packaging derives the registered Id from
    // Name. With one registered app, its unique retail PC executable is enough
    // to bind the two manifests without guessing that derived spelling.
    internal static string? Executable(XDocument config, string applicationId, int registeredApplications)
    {
        var entries = config.Root?.Elements().Where(e => e.Name.LocalName == "ExecutableList")
            .SelectMany(e => e.Elements()).Where(e => e.Name.LocalName == "Executable").ToArray() ?? [];
        bool RetailPc(XElement e)
        {
            string? dev = (string?)e.Attribute("IsDevOnly"), device = (string?)e.Attribute("TargetDeviceFamily"), arch = (string?)e.Attribute("Architecture");
            return (dev is null || string.Equals(dev, "false", StringComparison.OrdinalIgnoreCase)) &&
                (device is null || string.Equals(device, "PC", StringComparison.OrdinalIgnoreCase)) &&
                (arch is null || string.Equals(arch, RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "ARM64" : "x64", StringComparison.OrdinalIgnoreCase));
        }
        var exact = entries.Where(e => (string?)e.Attribute("Id") == applicationId).ToArray();
        if (exact.Length != 0)
            return exact.Length == 1 && RetailPc(exact[0]) ? (string?)exact[0].Attribute("Name") : null;
        var retail = entries.Where(RetailPc).ToArray();
        return registeredApplications == 1 && retail.Length == 1 && retail[0].Attribute("Id") is null
            ? (string?)retail[0].Attribute("Name") : null;
    }
}
