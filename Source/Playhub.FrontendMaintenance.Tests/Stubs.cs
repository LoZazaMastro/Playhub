using System.Text.Json.Nodes;
namespace Playhub.Services {
public static class AppPaths {
public static string DefaultDeckyPluginsPath => throw new InvalidOperationException("OS entry forbidden in tests");
public static string SettingsFile => throw new InvalidOperationException("OS entry forbidden in tests");
public static string LocalDataRoot => throw new InvalidOperationException("OS entry forbidden in tests");
}}
namespace Playhub.Integrations { public static class SteamPluginBridge {
public static Task<JsonNode?> EvaluateAsync(string _,CancellationToken token) => throw new InvalidOperationException("live CDP forbidden in tests");
}}
