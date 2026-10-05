using System.Text.Json.Nodes;

namespace Playhub.Integrations;

/// <summary>Exact trusted SteamGridDB author metadata; no URL, game-name or image-content inference.</summary>
public static class ArtworkLogoPolicy
{
    public static string? AuthorName(JsonNode? author) => author is JsonObject obj ? String(obj["name"]) : String(author);
    public static string? AuthorSteamId(JsonNode? author) => author is JsonObject obj ? String(obj["steam64"])??String(obj["steamid"])??String(obj["steam_id"]) : null;
    private static string? String(JsonNode? node) => node is JsonValue value&&value.TryGetValue<string>(out var text)&&text.Length<=256&&!text.Any(char.IsControl)?text:null;
    public static bool IsZazaHero(string type,string? provider,string? name,string? steamId) => type=="hero"
        && string.Equals(provider,"steamgriddb",StringComparison.OrdinalIgnoreCase)
        && (string.Equals(name?.Trim(),"zazamastro",StringComparison.OrdinalIgnoreCase)
            || string.Equals(name?.Trim(),"lozazamastro",StringComparison.OrdinalIgnoreCase)
            || steamId=="76561198128354791");
    public static bool? ManualOverride(JsonObject state)
    {
        if(state["steamLogoManualOverride"] is JsonValue value&&value.TryGetValue<bool>(out bool hidden))return hidden;
        // Older explicit visible preference must not be erased by author automation.
        if(state["perfect_hero_hideSteamLogo"] is JsonValue legacy&&legacy.TryGetValue<bool>(out bool old)&&!old)return false;
        return null;
    }
    public static bool HasVerifiedAuthorHero(JsonObject state,string actualSha256)
    {
        var source=state["hero_source"] as JsonObject;
        return ManualOverride(state)!=false && source is not null
            && IsZazaHero("hero",source["provider"]?.ToString(),source["authorName"]?.ToString(),source["authorSteamId"]?.ToString())
            && string.Equals(source["sha256"]?.ToString(),actualSha256,StringComparison.OrdinalIgnoreCase);
    }
}
