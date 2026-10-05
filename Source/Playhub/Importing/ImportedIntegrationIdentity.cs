using System.Security.Cryptography;
using System.Text;
using Playhub.Integrations;

namespace Playhub.Importing;

internal static class ImportedIntegrationIdentity
{
    internal static string? ResultKey(uint appId,string? stableIdentity) => stableIdentity is not null
        ? ApplicationIntegrationIdentity.Validate(stableIdentity) : appId > 0 ? "shortcut:" + appId : null;
    internal static string Pc(string aumid, string executable, string launchArguments)
    {
        if (!string.IsNullOrWhiteSpace(aumid)) return ApplicationIntegrationIdentity.Create("pc", "source:" + aumid);
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("An exact game source is required.");
        string key = Path.GetFullPath(executable).ToUpperInvariant() + "\0" + launchArguments;
        return ApplicationIntegrationIdentity.Create("pc", "local:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant());
    }
}
