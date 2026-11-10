using Playhub.Models;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Services;

internal sealed partial class DeckyWindowsReleaseClient
{
    private const string WebReleasePath = "/SteamDeckHomebrew/decky-loader/releases";
    private const string WebRoot = "https://github.com" + WebReleasePath;
    private const string TagPath = WebReleasePath + "/tag/";

    private static readonly Regex CanonicalTag = HtmlPattern(
        """<meta\b[^>]*\bproperty="og:url"[^>]*\bcontent="(?<url>[^"]+)"[^>]*>""");
    private static readonly Regex PublishedTime = HtmlPattern(
        """\breleased this\s*<relative-time\b[^>]*\bdatetime="(?<date>[^"]+)"[^>]*>""");
    private static readonly Regex PreReleaseBadge = HtmlPattern(
        """<span\b[^>]*\bclass="[^"]*\bLabel--warning\b[^"]*"[^>]*>\s*(?:Pre-release|Draft)\s*</span>""");
    private static readonly Regex ExpandedAssetUrl = HtmlPattern(
        """<include-fragment\b[^>]*\bsrc="(?<url>[^"]+)"[^>]*>""");
    private static readonly Regex ReleaseSections = HtmlPattern(
        """<section\b[^>]*\bdata-release-anchor="release-(?<tag>[^"]+)"[^>]*>(?<body>.*?)</section>""");
    private static readonly Regex AssetRows = HtmlPattern("""<li\b[^>]*>.*?</li>""");
    private static readonly Regex AssetLink = HtmlPattern("""<a\b[^>]*\bhref="(?<path>[^"]+)"[^>]*>""");
    private static readonly Regex AssetDigest = HtmlPattern(
        """<clipboard-copy\b(?=[^>]*\baria-label="Copy to clipboard digest for (?<name>[^"]+)")(?=[^>]*\bvalue="sha256:(?<digest>[0-9a-f]{64})")[^>]*>""");

    private static Regex HtmlPattern(string pattern) => new(pattern,
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private async Task<Release> GetWebReleaseAsync(string? requestedTag)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var summary = requestedTag is not null
            ? ParseWebTagPage(await ReadWebPageAsync(WebRoot + "/tag/" + requestedTag, token).ConfigureAwait(false), requestedTag)
            : await ReadLatestWebSummaryAsync(token).ConfigureAwait(false);
        var release = await ReadWebAssetsAsync(summary, token).ConfigureAwait(false);
        if (release is not null) return release;
        if (requestedTag is null)
        {
            var compatible = await ListWebCompatibleAsync(token).ConfigureAwait(false);
            if (compatible.Length > 0) return compatible[0];
        }
        throw new InvalidDataException("La release ufficiale di Decky non contiene due eseguibili Windows verificabili per questo PC.");
    }

    private async Task<DeckyWindowsRelease> ReadLatestWebSummaryAsync(CancellationToken token)
    {
        using var request = WebRequest(WebRoot + "/latest");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
        {
            var location = response.Headers.Location;
            if (location is null || !TryGetWebTag(new Uri(request.RequestUri!, location), out var tag))
                throw new InvalidDataException("Il sito di Decky ha restituito un reindirizzamento non valido.");
            return ParseWebTagPage(await ReadWebPageAsync(WebRoot + "/tag/" + tag, token).ConfigureAwait(false), tag);
        }

        var html = await ReadWebHtmlAsync(response, token).ConfigureAwait(false);
        if (!TryGetCanonicalTag(html, out var canonical))
            throw new InvalidDataException("Il sito di Decky non indica una release stabile verificabile.");
        return ParseWebTagPage(html, canonical);
    }

    private async Task<Release[]> ListWebCompatibleAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await ListWebCompatibleAsync(deadline.Token).ConfigureAwait(false);
    }

    private async Task<Release[]> ListWebCompatibleAsync(CancellationToken token)
    {
        var html = await ReadWebPageAsync(WebRoot, token).ConfigureAwait(false);
        var candidates = ReleaseSections.Matches(html)
            .Select(section => ParseWebSummary(section.Groups["body"].Value, section.Groups["tag"].Value))
            .OfType<DeckyWindowsRelease>().DistinctBy(summary => summary.Tag)
            .OrderByDescending(summary => Version.Parse(summary.Tag[1..])).Take(8).ToArray();
        if (candidates.Length == 0)
            throw new InvalidDataException("Il sito di Decky non contiene un elenco di release stabili verificabili.");

        using var concurrency = new SemaphoreSlim(3);
        var inspected = await Task.WhenAll(candidates.Select(async summary =>
        {
            await concurrency.WaitAsync(token).ConfigureAwait(false);
            try { return await ReadWebAssetsAsync(summary, token).ConfigureAwait(false); }
            finally { concurrency.Release(); }
        })).ConfigureAwait(false);
        return inspected.OfType<Release>().ToArray();
    }

    private async Task<Release?> ReadWebAssetsAsync(DeckyWindowsRelease summary, CancellationToken token)
    {
        var html = await ReadWebPageAsync(WebRoot + "/expanded_assets/" + summary.Tag, token).ConfigureAwait(false);
        var rows = AssetRows.Matches(html);
        var console = ParseWebAsset(rows, summary.Tag, WindowsAssetPrefix + ".exe", DeckyFileUpgrade.ConsoleName);
        var background = ParseWebAsset(rows, summary.Tag, WindowsAssetPrefix + "-noconsole.exe", DeckyFileUpgrade.BackgroundName);
        return console is null || background is null ? null : new(summary, console, background);
    }

    private static Asset? ParseWebAsset(MatchCollection rows, string tag, string filename, string installName)
    {
        string relativePath = WebReleasePath + "/download/" + tag + "/" + filename;
        var candidates = rows.Where(row => AssetLink.Matches(row.Value)
            .Any(link => string.Equals(link.Groups["path"].Value, relativePath, StringComparison.Ordinal))).ToArray();
        if (candidates.Length != 1) return null;
        var digests = AssetDigest.Matches(candidates[0].Value)
            .Where(digest => string.Equals(digest.Groups["name"].Value, filename, StringComparison.Ordinal)).ToArray();
        if (digests.Length != 1) return null;
        // The public page displays rounded sizes. Exact Content-Length or the
        // bounded download stream supplies the size, checked against the digest.
        return new(installName, new Uri(AssetRoot + tag + "/" + filename), 0, digests[0].Groups["digest"].Value);
    }

    private static DeckyWindowsRelease ParseWebTagPage(string html, string expectedTag) =>
        TryGetCanonicalTag(html, out var tag) && tag == expectedTag && ParseWebSummary(html, tag) is { } summary
            ? summary : throw new InvalidDataException("Il sito di Decky non corrisponde alla release stabile richiesta.");

    private static DeckyWindowsRelease? ParseWebSummary(string html, string tag)
    {
        if (!IsStableTag(tag) || PreReleaseBadge.IsMatch(html) || !ExpandedAssetUrl.Matches(html)
            .Any(link => string.Equals(link.Groups["url"].Value, WebRoot + "/expanded_assets/" + tag, StringComparison.Ordinal)))
            return null;
        var date = PublishedTime.Match(html).Groups["date"].Value;
        if (!DateTimeOffset.TryParseExact(date, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var published)) return null;
        return new(tag, published);
    }

    private static bool TryGetCanonicalTag(string html, out string tag)
    {
        string address = CanonicalTag.Match(html).Groups["url"].Value;
        if (address.StartsWith('/')) address = "https://github.com" + address;
        Uri.TryCreate(address, UriKind.Absolute, out var uri);
        return TryGetWebTag(uri, out tag);
    }

    private static bool TryGetWebTag(Uri? uri, out string tag)
    {
        tag = string.Empty;
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !uri.AbsolutePath.StartsWith(TagPath, StringComparison.Ordinal)) return false;
        tag = uri.AbsolutePath[TagPath.Length..];
        return IsStableTag(tag);
    }

    private async Task<string> ReadWebPageAsync(string url, CancellationToken token)
    {
        using var request = WebRequest(url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        return await ReadWebHtmlAsync(response, token).ConfigureAwait(false);
    }

    private static HttpRequestMessage WebRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.AcceptLanguage.ParseAdd("en-US");
        return request;
    }

    private static async Task<string> ReadWebHtmlAsync(HttpResponseMessage response, CancellationToken token)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxMetadataSize)
            throw new InvalidDataException("I dati della release di Decky sono troppo grandi.");
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return Encoding.UTF8.GetString(await ReadBoundedAsync(stream, MaxMetadataSize, token).ConfigureAwait(false));
    }
}
