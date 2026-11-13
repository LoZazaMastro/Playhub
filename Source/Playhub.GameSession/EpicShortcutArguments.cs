using System.Text;

namespace Playhub.GameSession;

internal sealed record EpicShortcutRequest(string Namespace, string CatalogItemId, string AppName, string GameExecutable);
internal static class EpicShortcutArguments
{
    internal static bool Identifier(string value) => value.Length is > 0 and <= 256
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
    internal static bool HasCatalog(string space, string item, string app) => Identifier(space) && Identifier(item) && Identifier(app);
    internal static string Build(string space, string item, string app, string game)
    {
        if (!HasCatalog(space, item, app) || !Path.IsPathFullyQualified(game)) throw new ArgumentException("Installed Epic identity required.");
        return string.Join(" ", new[] { "--epic", space, item, app, Path.GetFullPath(game) }.Select(Quote));
    }
    internal static bool TryRead(IReadOnlyList<string> args, out EpicShortcutRequest? request)
    {
        request = null;
        if (args.Count != 5 || args[0] != "--epic" || !HasCatalog(args[1], args[2], args[3])
            || !Path.IsPathFullyQualified(args[4]) || !Path.GetExtension(args[4]).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        request = new(args[1], args[2], args[3], Path.GetFullPath(args[4])); return true;
    }
    internal static bool Matches(IReadOnlyList<string> args, string space, string item, string app) =>
        TryRead(args, out var request) && request!.Namespace == space && request.CatalogItemId == item && request.AppName == app;
    private static string Quote(string value)
    {
        var text = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}
