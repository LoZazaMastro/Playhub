using System.Text.Json.Nodes;
using Playhub.Emulation.Workbench;

namespace Playhub.Importing;

/// <summary>A plugin draft for an exact imported shortcut, independent of store/emulator launch logic.</summary>
internal sealed class ImportIntegrationPlan
{
    internal static readonly string[] Categories = ["artworks", "metadata", "themedeck", "trailerhero", "launch-curtain"];
    internal uint AppId { get; }
    internal string? StableIdentity { get; }
    internal JsonObject Game { get; }
    internal JsonObject Preview { get; }
    internal ImportIntegrationPlan(uint appId, string title, IReadOnlyDictionary<string, string> artwork, IEnumerable<string> enabled, string? stableIdentity = null)
    {
        if (stableIdentity is not null) Playhub.Integrations.ApplicationIntegrationIdentity.Validate(stableIdentity);
        if (appId == 0 && stableIdentity is null || string.IsNullOrWhiteSpace(title)) throw new ArgumentException("An exact source or imported shortcut is required.");
        StableIdentity = stableIdentity;
        AppId = appId;
        Game = new JsonObject { ["title"] = title, ["steamArtwork"] = new JsonObject() };
        foreach (var item in artwork) Game["steamArtwork"]![item.Key] = item.Value;
        Preview = new JsonObject
        {
            ["integrations"] = new JsonArray(enabled.Where(Categories.Contains).Distinct(StringComparer.Ordinal).Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()),
            ["artwork"] = new JsonObject { ["perfectHero"] = true },
            ["themedeck"] = new JsonObject { ["onlyMissing"] = true },
            ["trailers"] = new JsonObject { ["offline"] = false }
        };
    }
    internal PostImportIntegrationRequest Request() => new(AppId, StableIdentity ?? "shortcut:" + AppId, (JsonObject)Game.DeepClone(), (JsonObject)Preview.DeepClone());
    internal static JsonArray Candidates(JsonNode? outcome) => (outcome?["data"]?["results"] as JsonArray)?.DeepClone().AsArray() ?? new();
    internal static string State(string? status, bool italian) => status switch
    {
        "completed" => italian ? "Pronto" : "Ready",
        "selection_required" => italian ? "Scegli un risultato in Info" : "Choose a result in Info",
        "not_found" => italian ? "Nessun risultato" : "No result found",
        "unavailable" => italian ? "Apri Steam e controlla il plugin" : "Open Steam and check the plugin",
        "running" or "queued" => italian ? "In corso" : "In progress",
        "cancelled" => italian ? "Annullato" : "Cancelled",
        _ => italian ? "Operazione non completata" : "Operation incomplete"
    };
}
