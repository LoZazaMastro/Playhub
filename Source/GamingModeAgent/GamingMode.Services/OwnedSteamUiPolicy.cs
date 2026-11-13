using System.IO;
using Playhub.DeckyHost;
namespace GamingMode.Services;
internal static class OwnedSteamUiPolicy
{
    internal static bool Matches(OwnedProcessFacts ui, OwnedProcessFacts steam, string root, string windowClass,
        IReadOnlyList<DeckyProcessTree.Identity> chain, bool valve)
    {
        bool Same(DeckyProcessTree.Identity p, OwnedProcessFacts f) => p.Pid == f.Pid && p.Birth == f.Birth
            && p.Path.Equals(f.Image, StringComparison.OrdinalIgnoreCase) && p.Sid == f.Sid && p.Session == f.Session;
        bool Helper(string path) => path.Equals(Path.Combine(root,"bin","cef","cef.win64","steamwebhelper.exe"),StringComparison.OrdinalIgnoreCase)
            || path.Equals(Path.Combine(root,"bin","cef","cef.win7x64","steamwebhelper.exe"),StringComparison.OrdinalIgnoreCase);
        if (!valve || windowClass != "SDL_app" || ui.Sid != steam.Sid || ui.Session != steam.Session || ui.Birth < steam.Birth
            || chain.Count is 0 or > 5 || !Same(chain[0],ui) || !Same(chain[^1],steam)) return false;
        if (chain.Count == 1) return ui.Pid == steam.Pid && ui.Birth == steam.Birth;
        if (!Helper(ui.Image)) return false;
        for (int i=0;i<chain.Count-1;i++)
            if (!Helper(chain[i].Path) || chain[i].Sid != steam.Sid || chain[i].Session != steam.Session
                || chain[i].ParentPid != chain[i+1].Pid || chain[i].Birth < chain[i+1].Birth) return false;
        return chain.Select(p=>p.Pid).Distinct().Count() == chain.Count;
    }
}
