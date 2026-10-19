using Playhub.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class OriginalDescriptionTests
{
    public static async Task RunAsync()
    {
        var root = BundledManifest.Root;
        var catalog = RemotePluginCatalogService.Parse(File.ReadAllBytes(Path.Combine(root, "catalog/plugins.json")));
        using var provenance = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "catalog/plugin-description-provenance.json")));
        using var workbook = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "catalog/workbook-import-2026-10-19.json")));
        var additions = workbook.RootElement.GetProperty("added").EnumerateArray()
            .Select(entry => entry.GetProperty("repository").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = provenance.RootElement.GetProperty("sources").EnumerateArray().ToArray();
        if (sources.Length != 47 || !additions.SetEquals(sources.Select(source => source.GetProperty("repository").GetString()!)))
            throw new InvalidOperationException("Original-copy evidence must cover all 47 workbook additions.");

        var directory = Path.Combine(Path.GetTempPath(), "Playhub.OriginalCopy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var service = new PluginCatalogService();
            var loaded = await service.LoadAsync(Path.Combine(directory, "bundled"), Path.Combine(directory, "installed"), catalog);
            var languages = new[] { "it", "en", "de", "fr", "es", "pt", "ru", "zh", "ja", "ko", "pl" };
            foreach (var source in sources)
            {
                var repository = source.GetProperty("repository").GetString()!;
                var record = catalog.Plugins.Single(plugin => plugin.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase));
                var plugin = loaded.Single(plugin => plugin.RepositorySlug.Equals(repository, StringComparison.OrdinalIgnoreCase));
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.ShortDescription))).ToLowerInvariant();
                if (hash != source.GetProperty("descriptionSha256").GetString() || record.LongDescription != record.ShortDescription)
                    throw new InvalidOperationException("Publisher text changed: " + repository);
                if (plugin.ShortDescription != record.ShortDescription || plugin.LongDescription != record.LongDescription)
                    throw new InvalidOperationException("Catalog mapping replaced publisher text: " + repository);
                foreach (var language in languages)
                {
                    if (PluginCatalogService.LocalizedShortDescription(plugin, language) != record.ShortDescription ||
                        PluginCatalogService.LocalizedLongDescription(plugin, language) != record.LongDescription)
                        throw new InvalidOperationException($"External copy translated for {language}: {repository}");
                }
            }
            if (!catalog.Plugins.Single(plugin => plugin.Repository == "18798aa12/DeckyMods").ShortDescription.Contains("游戏") ||
                !catalog.Plugins.Single(plugin => plugin.Repository == "Kowalski-coder/SAM-for-Decky-Loader").ShortDescription.Contains("для"))
                throw new InvalidOperationException("Non-Latin publisher copy was replaced or damaged.");
            Console.WriteLine("PASS original publisher descriptions: 47 records mapped unchanged across 11 interface languages, including Chinese and Russian.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
