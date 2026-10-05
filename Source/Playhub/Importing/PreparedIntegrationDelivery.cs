using System.Text.Json.Nodes;

namespace Playhub.Importing;

internal static class PreparedIntegrationDelivery
{
    internal static JsonObject? Unresolved(string identity,string title,JsonObject draft) =>
        (draft["integrations"] as JsonObject)?.Any(category=>category.Value is JsonObject fields&&fields.Count>0)==true
        ? new JsonObject{["identity"]=identity,["title"]=title,["delivered"]=false,["detail"]="shortcut_unresolved"}:null;
    internal static bool HasIncomplete(JsonArray games) => games.OfType<JsonObject>().Any(game =>
        game["delivered"]?.GetValue<bool>() == false || (game["outcomes"] as JsonArray)?.OfType<JsonObject>().Any(outcome =>
            outcome["delivered"]?.GetValue<bool>() == false || outcome["status"]?.ToString() is "failed" or "prepared" or "unavailable") == true);
    internal static async Task<JsonArray> RunAsync<T>(IEnumerable<T> games, Func<T,CancellationToken,Task<JsonObject?>> deliver,
        Func<T,string> title, CancellationToken ct = default)
    {
        var outcomes = new JsonArray();
        foreach (var game in games)
        {
            ct.ThrowIfCancellationRequested();
            try { if (await deliver(game,ct) is { } result) outcomes.Add(result); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            { outcomes.Add(new JsonObject { ["title"] = title(game),["delivered"] = false,["detail"] = "prepared_delivery_failed",["error"] = error.GetType().Name }); }
        }
        return outcomes;
    }
}
