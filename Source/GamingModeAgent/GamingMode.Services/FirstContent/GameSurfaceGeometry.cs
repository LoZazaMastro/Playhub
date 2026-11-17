// SPDX-License-Identifier: GPL-3.0-only
namespace Playhub.FirstContent;
internal static class GameSurfaceGeometry
{
 internal static bool ClientCoversMonitor(string windowClass,int[] client,int[] monitor)
  =>windowClass.Length>0&&windowClass is not ("BootSplashClass" or "GAMINGSERVICESUI_HOSTING_WINDOW_CLASS")
    &&!windowClass.StartsWith("HwndWrapper[GamingServicesTcui;",StringComparison.Ordinal)
    &&client.Length==4&&monitor.Length==4&&client[2]>client[0]&&client[3]>client[1]
    &&monitor[2]>monitor[0]&&monitor[3]>monitor[1]
    &&client[0]<=monitor[0]+8&&client[1]<=monitor[1]+8&&client[2]>=monitor[2]-8&&client[3]>=monitor[3]-8;
}
