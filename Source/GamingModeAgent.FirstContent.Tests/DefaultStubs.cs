// Only unused platform default factories are stubbed. Tests call the actual
// controller's inert constructor and actual endpoint handler, never Windows.
using Playhub.FirstContent;
namespace GamingMode.Services;
internal sealed class GamingWindowFocusService{}
internal sealed class LivePerformanceSourceResolver{internal LivePerformanceSourceResolver(GamingWindowFocusService focus){_ =focus;}}
internal static class LaunchCurtainRetainedSource{internal static Task<ILaunchCurtainSource?> ResolveAsync(CurtainCoverRequest request,LivePerformanceSourceResolver resolver,CancellationToken stop){_ =request;_ =resolver;_ =stop;return Task.FromResult<ILaunchCurtainSource?>(null);}}
internal sealed class XboxShellBroker{internal bool IsAuthorized(System.Net.IPAddress? address,string token){_ =address;_ =token;return false;}}
