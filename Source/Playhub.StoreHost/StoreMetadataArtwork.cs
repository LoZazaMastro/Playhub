// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playhub.StoreLibrary;

internal sealed record StoreMetadataImage(string? Url, IReadOnlyList<string> Alternatives);
internal static class StoreMetadataArtwork
{
    internal static StoreMetadataImage Amazon(JsonElement product)
    {
        var urls = new List<string>();
        if (product.ValueKind == JsonValueKind.Object && product.TryGetProperty("productDetail", out var detail) && detail.ValueKind == JsonValueKind.Object)
        {
            if (Url(detail, "iconUrl", StoreKind.Amazon) is { } cover) { urls.Add(AmazonPoster(cover)); urls.Add(cover); }
            if (detail.TryGetProperty("details", out var data) && data.ValueKind == JsonValueKind.Object)
                foreach (var field in new[] { "boxArtUrl", "coverUrl", "pgCrownImageUrl" }) if (Url(data, field, StoreKind.Amazon) is { } image) urls.Add(image);
        }
        // logoUrl is a transparent wordmark; background URLs are screenshots.
        // Neither can be represented as a product cover.
        var distinct = urls.Distinct(StringComparer.Ordinal).Take(4).ToArray(); return new(distinct.FirstOrDefault(), distinct.Skip(1).ToArray());
    }
    internal static string AmazonPoster(string url)
    {
        var uri = new Uri(url);
        if (uri.Host == "m.media-amazon.com" && uri.Query.Length == 0 && Regex.IsMatch(uri.AbsolutePath, @"^/images/I/[A-Za-z0-9+_-]+\.(jpg|png)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase))
        { var dot = url.LastIndexOf('.'); return url[..dot] + "._SX480_" + url[dot..]; }
        return url;
    }
    internal static IEnumerable<string> CoverVariants(StoreKind store, string url)
    {
        var uri = new Uri(url);
        if (uri.Query.Length == 0 && store is StoreKind.Epic or StoreKind.Xbox)
        {
            var builder = new UriBuilder(uri) { Query = store == StoreKind.Epic ? "resize=1&w=480&h=640&quality=medium" : "w=480&h=640&mode=scale&q=85" };
            yield return builder.Uri.AbsoluteUri;
        }
        yield return url;
    }
    internal static bool XboxCoverPurpose(JsonElement image)
    {
        if (!image.TryGetProperty("ImagePurpose", out var purpose)) return false;
        if (purpose.GetString() is "Poster" or "BoxArt") return true;
        // Legacy Microsoft Store products label their tall marketing poster as
        // Hero. Require portrait geometry; app logos/tiles/screenshots never pass.
        return purpose.GetString() == "Hero" && image.TryGetProperty("Width", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var width) &&
            image.TryGetProperty("Height", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var height) && width >= 256 && height >= 352 && (double)width / height is >= .60 and <= .85;
    }
    internal static int XboxCoverPriority(JsonElement image) => image.GetProperty("ImagePurpose").GetString() switch { "Poster" => 0, "BoxArt" => 1, _ => 2 };
    internal static string? GogAccountCover(JsonElement product)
    {
        if (product.ValueKind != JsonValueKind.Object) return null;
        foreach (var field in new[] { "boxArtImage", "image" })
        {
            if (Url(product, field, StoreKind.Gog) is not { } image) continue;
            var uri = new Uri(image); var name = uri.AbsolutePath.TrimStart('/');
            if (uri.Query.Length == 0 && name.Length is >= 32 and <= 128 && name.All(Uri.IsHexDigit)) return image + "_product_card_v2_mobile_slider_639.jpg";
            return image;
        }
        return null;
    }
    internal static string? GogProductCover(JsonElement document, string id)
    {
        if (document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("_embedded", out var embedded) || embedded.ValueKind != JsonValueKind.Object ||
            !embedded.TryGetProperty("product", out var product) || product.ValueKind != JsonValueKind.Object || !product.TryGetProperty("id", out var productId) || productId.ToString() != id ||
            !document.TryGetProperty("_links", out var links) || links.ValueKind != JsonValueKind.Object || !links.TryGetProperty("boxArtImage", out var art) || art.ValueKind != JsonValueKind.Object) return null;
        return Url(art, "href", StoreKind.Gog);
    }
    private static string? Url(JsonElement value, string field, StoreKind store) => value.TryGetProperty(field, out var url) && url.ValueKind == JsonValueKind.String ? StoreCliProvider.OfficialArtwork(store, url.GetString()) : null;
}
