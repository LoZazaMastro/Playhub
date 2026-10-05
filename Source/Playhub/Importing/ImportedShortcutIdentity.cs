using VDFParser.Models;
namespace Playhub.Importing;

internal static class ImportedShortcutIdentity
{
    internal static uint? Resolve(IEnumerable<VDFEntry[]> profiles, Func<VDFEntry, bool> matches)
    {
        uint? identity = null;
        foreach (var profile in profiles)
        {
            var found = profile.Where(matches).ToArray();
            if (found.Length > 1) return null;
            if (found.Length == 0) continue;
            var id = unchecked((uint)found[0].appid);
            if (id == 0 || identity.HasValue && identity.Value != id) return null;
            identity = id;
        }
        return identity;
    }
}
