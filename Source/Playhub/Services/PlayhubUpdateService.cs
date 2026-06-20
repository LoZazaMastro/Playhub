using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Playhub.Services;

/// <summary>
/// Controlla se su GitHub esiste una release di Playhub più recente di quella
/// installata. Non scarica nulla: restituisce solo le informazioni così che
/// l'app possa mostrare una notifica (InfoBar) con il link alla release.
/// </summary>
public sealed class PlayhubUpdateService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Playhub/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public sealed record UpdateInfo(
        bool IsNewer,
        string LatestVersion,
        string CurrentVersion,
        string? ReleaseUrl,
        string? Notes);

    /// <summary>
    /// Interroga releases/latest del repository indicato (es. "LoZazaMastro/Playhub")
    /// e confronta il tag con la versione corrente. Restituisce null se la rete
    /// non risponde o non esiste alcuna release pubblicata.
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(string repository, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return null;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var json = await Http.GetStringAsync(
                $"https://api.github.com/repos/{repository.Trim('/')}/releases/latest", cts.Token);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return await CheckFromAtomAsync(repository, currentVersion);
            }

            var url = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;

            var latest = ParseVersion(tag);
            var current = ParseVersion(currentVersion);
            var isNewer = latest is not null && current is not null && latest > current;

            return new UpdateInfo(isNewer, NormalizeTag(tag), currentVersion, url, notes);
        }
        catch
        {
            // GitHub's anonymous API is heavily rate-limited. The public
            // release feed contains the same latest tag and is not tied to
            // that quota, so update checks keep working without a token.
            return await CheckFromAtomAsync(repository, currentVersion);
        }
    }

    private static async Task<UpdateInfo?> CheckFromAtomAsync(string repository, string currentVersion)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var xml = await Http.GetStringAsync(
                $"https://github.com/{repository.Trim('/')}/releases.atom", cts.Token);
            var document = XDocument.Parse(xml);
            XNamespace atom = "http://www.w3.org/2005/Atom";
            var entry = document.Root?.Elements(atom + "entry").FirstOrDefault();
            if (entry is null)
            {
                return null;
            }

            var releaseUrl = entry.Elements(atom + "link")
                .FirstOrDefault(link => string.Equals(
                    (string?)link.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("href")?.Value;
            if (string.IsNullOrWhiteSpace(releaseUrl))
            {
                return null;
            }

            var tag = Uri.UnescapeDataString(releaseUrl[(releaseUrl.LastIndexOf('/') + 1)..]);
            var latest = ParseVersion(tag);
            var current = ParseVersion(currentVersion);
            var isNewer = latest is not null && current is not null && latest > current;
            return new UpdateInfo(isNewer, NormalizeTag(tag), currentVersion, releaseUrl, null);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeTag(string tag) => tag.Trim().TrimStart('v', 'V');

    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = raw.Trim().TrimStart('v', 'V');

        // Tieni solo la parte numerica iniziale (es. "1.2.0-beta" -> "1.2.0").
        var end = 0;
        while (end < cleaned.Length && (char.IsDigit(cleaned[end]) || cleaned[end] == '.'))
        {
            end++;
        }

        cleaned = cleaned[..end].Trim('.');
        if (cleaned.Length == 0)
        {
            return null;
        }

        // Version richiede almeno major.minor.
        if (!cleaned.Contains('.'))
        {
            cleaned += ".0";
        }

        return Version.TryParse(cleaned, out var version) ? version : null;
    }
}
