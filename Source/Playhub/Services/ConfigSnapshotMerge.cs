using System.Text.Json.Nodes;

namespace Playhub.Services;

internal static class ConfigSnapshotMerge
{
    // Fields untouched since the UI loaded belong to the current disk snapshot.
    // Explicit changes (including null, false and removed array items) belong to
    // the UI. Unknown keys added by the agent are retained as well.
    internal static JsonObject Merge(JsonObject baseline, JsonObject proposed, JsonObject current)
    {
        var result = (JsonObject)current.DeepClone();
        foreach (var field in proposed)
        {
            baseline.TryGetPropertyValue(field.Key, out var previous);
            current.TryGetPropertyValue(field.Key, out var disk);
            if (JsonNode.DeepEquals(previous, field.Value)) continue;
            result[field.Key] = previous is JsonObject a && field.Value is JsonObject b && disk is JsonObject c
                ? Merge(a, b, c) : field.Value?.DeepClone();
        }
        foreach (var field in baseline)
            if (!proposed.ContainsKey(field.Key)) result.Remove(field.Key);
        return result;
    }
}
