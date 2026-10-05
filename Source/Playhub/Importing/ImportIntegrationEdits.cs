using System.Text.Json.Nodes;
using Playhub.Emulation.Workbench;
namespace Playhub.Importing;

/// <summary>Only dirty fields are written; readiness is based on plugin readback, not transport success.</summary>
internal sealed class ImportIntegrationEdits(Func<string, string, JsonArray, CancellationToken, Task<JsonNode?>> call, bool sourceScoped = false)
{
    internal static JsonObject OverlayManualFields(JsonObject observed, JsonObject manual)
    {
        var merged = (JsonObject)observed.DeepClone();
        foreach (var field in manual) merged[field.Key] = field.Value?.DeepClone();
        return merged;
    }
    internal static void ClearConfirmedFields(JsonObject current, JsonObject confirmed)
    {
        foreach (var field in confirmed)
            if (JsonNode.DeepEquals(current[field.Key], field.Value)) current.Remove(field.Key);
    }
    internal async Task SaveAsync(uint appId, JsonObject metadata, JsonObject music, JsonObject curtain, CancellationToken ct)
    {
        var editor = new GameIntegrationEditorService(call, sourceScoped);
        if (metadata.Count > 0)
        {
            await editor.MetadataAsync(new(appId, metadata), ct);
            Verify(metadata, await call("Playhub Metadata", "get_metadata", new JsonArray(appId), ct));
        }
        if (music.Count > 0)
        {
            foreach (var field in music) await editor.MusicAsync(new(appId, field.Key, field.Value?.DeepClone()), ct);
            var actual = await editor.MusicAsync(new(appId), ct);
            if (music["remove"]?.GetValue<bool>() == true) { if (actual is not null) throw new InvalidOperationException("Track removal was not confirmed."); }
            else
            {
                var expected = new JsonObject();
                foreach (var field in music) expected[field.Key == "startOffset" ? "start_offset" : field.Key] = field.Value?.DeepClone();
                Verify(expected, actual);
            }
        }
        if (curtain.Count > 0)
        {
            var saved = await editor.CurtainAsync(new(appId, curtain), ct);
            if (saved?["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException("Launch settings were not saved.");
            var actual = await editor.CurtainAsync(new(appId), ct);
            Verify(curtain, actual?["settings"] ?? actual?["resolved"]);
        }
    }
    private static void Verify(JsonObject expected, JsonNode? actual)
    {
        if (actual is not JsonObject fields || expected.Any(field => !JsonNode.DeepEquals(field.Value, fields[field.Key])))
            throw new InvalidOperationException("Plugin settings readback did not match the draft.");
    }
}
