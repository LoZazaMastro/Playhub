using System.Text.Json.Nodes;

namespace Playhub.Integrations;

/// <summary>Validated application preferences shared by manual and automatic artwork acquisition.</summary>
public sealed class ApplicationArtworkPreferences(JsonObject? artwork = null)
{
    public static readonly string[] AssetTypes = ["cover", "banner", "hero", "logo", "icon"];
    public static readonly string[] Providers = ["steamgriddb", "ign", "igdb", "playstation", "nintendo", "xbox", "alphacoders", "iidb"];
    public static string ProviderKey(string type) => type switch { "cover" => "grid_p", "banner" => "grid_l", _ => type };
    public static bool Supports(string provider,string type)
    {
        string key=ProviderKey(type);
        if(key is not ("grid_p" or "grid_l" or "hero" or "logo" or "icon"))return false;
        if(provider=="steamgriddb")return true;
        if(!Providers.Contains(provider))return false;
        return !((provider=="alphacoders"&&key is not ("grid_l" or "hero"))||
            (provider=="ign"&&key!="grid_p")||
            (provider is "playstation" or "nintendo" or "igdb"&&key=="logo")||
            (provider=="iidb"&&key=="grid_p"));
    }
    public static string[] AllowedDimensions(string type, string shape) => type switch
    {
        "cover" => shape == "square" ? ["512x512", "1024x1024"] : ["600x900", "342x482", "660x930"],
        "banner" => ["460x215", "920x430"],
        "hero" => ["1920x620", "3840x1240", "1600x650"],
        // Logos have no enumerated API sizes. Preserve their native proportions.
        "logo" => [],
        "icon" => [],
        _ => throw new ArgumentException("Unsupported artwork type.")
    };
    public static string[] AllowedMimes(string type) => type is "logo" or "icon" ? ["image/png"] : ["image/png", "image/jpeg"];
    private JsonNode? Filter(string type, string shape) => artwork?["filters"]?[type == "cover" ? "cover_" + shape : type];
    public string DefaultProvider(string type)
    {
        string? selected = artwork?["defaultProviders"]?[type]?.ToString();
        if (selected is not null&&Supports(selected,type)) return selected;
        return Strings(artwork?["providerOrder"]?[ProviderKey(type)]).FirstOrDefault(provider=>Supports(provider,type)) ?? "steamgriddb";
    }
    public string[] Order(string type, IEnumerable<string>? requested = null)
    {
        var order = (requested ?? Strings(artwork?["providerOrder"]?[ProviderKey(type)])).Where(provider=>Supports(provider,type)).Distinct().ToList();
        if (order.Count == 0 && requested is null) order.AddRange(new[]{"steamgriddb", "ign", "igdb", "playstation", "nintendo", "xbox"}.Where(provider=>Supports(provider,type)));
        string preferred = DefaultProvider(type);
        if (order.Remove(preferred)) order.Insert(0, preferred);
        return order.ToArray();
    }
    public string[] Dimensions(string type, string shape) => ValidSelection(Filter(type, shape)?["dimensions"], AllowedDimensions(type, shape));
    public string[] Mimes(string type, string shape) => ValidSelection(Filter(type, shape)?["mimes"], AllowedMimes(type));
    public string Query(string type, string shape)
    {
        string dimensions = string.Join(',', Dimensions(type, shape));
        return (dimensions.Length > 0 ? "dimensions=" + dimensions + "&" : "") +
            (type is "logo" or "icon" ? "mimes=" : "types=static&nsfw=false&humor=false&mimes=") + string.Join(',', Mimes(type, shape));
    }
    public bool Accepts(JsonObject asset, string type, string shape)
    {
        if (asset["animated"]?.ToString().Equals("true", StringComparison.OrdinalIgnoreCase) == true || asset["type"]?.ToString() == "animated") return false;
        string? mime = asset["mime"]?.ToString();
        if (mime is not null && !Mimes(type, shape).Contains(mime)) return false;
        var sizes = Dimensions(type, shape);
        if (sizes.Length == 0) return true;
        string? width = asset["width"]?.ToString(), height = asset["height"]?.ToString();
        return type == "icon" ? width == height && sizes.Contains(width) : sizes.Contains(width + "x" + height);
    }
    public string CacheKey(string type, string shape) => Query(type, shape);
    private static string[] ValidSelection(JsonNode? value, string[] allowed)
    {
        var selected = Strings(value).Where(allowed.Contains).Distinct().ToArray();
        return selected.Length > 0 ? selected : allowed;
    }
    private static IEnumerable<string> Strings(JsonNode? value) => (value as JsonArray ?? []).OfType<JsonValue>().Select(x => x.TryGetValue<string>(out var text) ? text : "");
    public static string[] SteamUsers(JsonObject preview, IEnumerable<string> available)
    {
        var actual = available.Where(x => x.Length > 0 && x.All(char.IsAsciiDigit)).Distinct().ToArray();
        // All available accounts are imported. Historical hidden subsets are intentionally ignored.
        return actual;
    }
}
