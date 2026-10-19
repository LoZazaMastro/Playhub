using System.Globalization;
using Playhub.Models;
using Playhub.Services;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
    Console.WriteLine("PASS " + message);
}
DeckyPluginInfo Plugin(string repository, string first = "", string updated = "", string release = "", string keywords = "", string? name = null) => new()
{
    Name = name ?? repository, RepositorySlug = repository, RepositoryName = repository.Split('/').Last(),
    FirstPublishedAt = first, UpdatedAt = updated, ReleasePublishedAt = release, Keywords = keywords
};
DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
string[] Keys(IEnumerable<DeckyPluginInfo> plugins) => plugins.Select(plugin => plugin.RepositorySlug).ToArray();

var sourceFirst = Plugin("owner/source-first", "2020-03-04T05:06:07.123+02:00", "2025-08-02T09:10:11Z",
    "03/08/2025", "catalog-added:2026-10-01 first-release:2010-01-01");
Check(PluginChronology.FirstPublished(sourceFirst) == Utc("2020-03-04T03:06:07.123Z"),
    "First publication retains the actual ISO instant including offset and fractions");
Check(PluginChronology.FirstPublished(Plugin("owner/imported", keywords: "catalog-added:2026-10-01")) is null,
    "A Playhub catalog import date never becomes the plugin's first publication");
Check(PluginChronology.FirstPublished(Plugin("owner/latest-only", updated: "2025-10-01T00:00:00Z", release: "02/10/2025")) is null,
    "Latest update and release dates never fabricate a first publication");
Check(PluginChronology.FirstPublished(Plugin("owner/legacy-first", "broken", keywords: "first-release:2020-04-03 catalog-added:2026-01-01")) == Utc("2020-04-03T00:00:00Z"),
    "A verified legacy first-release keyword remains a fallback for missing or invalid ISO first date");
Check(PluginChronology.FirstPublished(Plugin("owner/display-first", "03/04/2020")) is null,
    "Display dates are not parsed as ISO first-publication metadata");
Check(PluginChronology.LastUpdated(Plugin("owner/update-offset", "2020-01-01T00:00:00Z", "2025-10-19T11:12:13.456-03:00")) == Utc("2025-10-19T14:12:13.456Z"),
    "Latest update preserves UTC ordering across an explicit offset and fractional seconds");
Check(PluginChronology.LastUpdated(Plugin("owner/update-fallback", "2020-01-01T00:00:00Z", "broken", "03/04/2025")) == Utc("2025-04-03T00:00:00Z"),
    "Malformed nonblank update metadata falls back to unambiguous day/month legacy release display");
Check(PluginChronology.LastUpdated(Plugin("owner/max-date", "2020-01-01T00:00:00Z", "2024-01-01T00:00:00Z", "2025-01-01T00:00:00Z")) == Utc("2025-01-01T00:00:00Z"),
    "Latest update uses the newest valid source date rather than the first populated field");
Check(PluginChronology.LastUpdated(Plugin("owner/precise-update", "2020-01-01T00:00:00Z", "2025-10-19T23:30:00Z", "20/10/2025")) == Utc("2025-10-19T23:30:00Z"),
    "A local display date cannot replace or round a valid precise UTC update timestamp");
Check(PluginChronology.LastUpdated(Plugin("owner/first-only", "2020-01-01T00:00:00Z")) == Utc("2020-01-01T00:00:00Z"),
    "A dated first publication remains usable when update metadata is missing");
Check(PluginChronology.FirstPublished(Plugin("owner/future-first", "2099-01-01T00:00:00Z")) is null &&
    PluginChronology.LastUpdated(Plugin("owner/future-update", updated: "2099-01-01T00:00:00Z")) is null,
    "Future timestamps cannot be promoted into first publication or latest updates");
Check(PluginChronology.LastUpdated(Plugin("owner/future-fallback", "2020-01-01T00:00:00Z", "2099-01-01T00:00:00Z")) == Utc("2020-01-01T00:00:00Z"),
    "An invalid future update retains an earlier genuine publication fallback");
var clock = Utc("2025-10-19T12:00:00Z");
Check(PluginChronology.FirstPublished(Plugin("owner/at-now", clock.ToString("O")), clock) == clock &&
    PluginChronology.FirstPublished(Plugin("owner/after-now", clock.AddTicks(1).ToString("O")), clock) is null &&
    PluginChronology.News(new[] { Plugin("owner/future-news", first: clock.AddTicks(1).ToString("O")) }, clock).Count == 0,
    "An injected UTC clock accepts the exact present and rejects the next future tick consistently");
Check(PluginChronology.LastUpdated(Plugin("owner/import-only", keywords: "catalog-added:2025-01-01")) is null,
    "A Playhub catalog import date never fabricates an upstream update either");

var chronology = new[]
{
    Plugin("owner/early", "2020-01-01T00:00:00Z", "2025-10-19T00:30:00Z", name: "Z early"),
    Plugin("owner/late", "2021-01-01T00:00:00Z", "2025-10-19T23:30:00Z", name: "A late"),
    Plugin("owner/middle", "2022-01-01T00:00:00Z", "2025-10-20T00:15:00+02:00", name: "B middle"),
    Plugin("owner/undated", name: "0 undated")
};
Check(Keys(PluginChronology.Sort(chronology, "updated")).SequenceEqual(new[] { "owner/late", "owner/middle", "owner/early", "owner/undated" }),
    "Updated order is descending by actual instant, preserving times within one calendar day and missing dates last");
Check(Keys(PluginChronology.Sort(chronology, "added")).SequenceEqual(new[] { "owner/middle", "owner/late", "owner/early", "owner/undated" }),
    "Added order uses genuine first publication descending and places unknown dates last");
Check(PluginChronology.Sort(chronology, "name").First().RepositorySlug == "owner/undated",
    "Alphabetical mode remains independent of date availability");
var oldCulture = CultureInfo.CurrentCulture;
try
{
    foreach (var culture in new[] { "it-IT", "en-US", "tr-TR" })
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        Check(Keys(PluginChronology.Sort(chronology, "updated")).SequenceEqual(new[] { "owner/late", "owner/middle", "owner/early", "owner/undated" }) &&
            PluginChronology.LastUpdated(Plugin("owner/day-month", release: "03/04/2025")) == Utc("2025-04-03T00:00:00Z"),
            "Date sorting and day/month fallback are independent of process culture: " + culture);
    }
}
finally { CultureInfo.CurrentCulture = oldCulture; }

Check(PluginChronology.NewsLimit == 100, "News has an explicit maximum of 100 plugins");
var seed = Utc("2025-01-01T00:00:00Z");
var candidates = Enumerable.Range(0, 125).Select(index => Plugin("owner/item-" + index,
    first: seed.AddMinutes(index).ToString("O", CultureInfo.InvariantCulture))).ToList();
candidates.AddRange(Enumerable.Range(0, 20).Select(index => Plugin("Owner/Duplicate",
    first: seed.AddDays(1).AddMinutes(index).ToString("O", CultureInfo.InvariantCulture))));
candidates.Add(Plugin("owner/DUPLICATE", first: seed.AddDays(2).ToString("O", CultureInfo.InvariantCulture)));
candidates.Add(Plugin("owner/missing"));
candidates.Add(Plugin("owner/updated-only", updated: "2025-10-19T23:30:00Z"));
candidates.Add(Plugin("owner/future", first: "2099-01-01T00:00:00Z", updated: "2025-10-19T23:30:00Z"));
var news = PluginChronology.News(candidates);
Check(news.Count == 100 && news.Select(plugin => plugin.RepositorySlug).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 100,
    "News deduplicates case-insensitive full repository identity before applying the 100 item cap");
Check(news[0].RepositorySlug.Equals("owner/duplicate", StringComparison.OrdinalIgnoreCase) &&
    PluginChronology.FirstPublished(news[0]) == seed.AddDays(2), "Duplicate resolution follows first-publication order before the cap");
Check(news.All(plugin => plugin.RepositorySlug is not ("owner/missing" or "owner/future" or "owner/updated-only")),
    "Unknown or future first publications cannot occupy a News slot even when an update is known");
Check(news.Zip(news.Skip(1)).All(pair => PluginChronology.FirstPublished(pair.First) >= PluginChronology.FirstPublished(pair.Second)),
    "Every adjacent News entry follows descending first-publication chronology");
Check(PluginChronology.News(new[]
    { Plugin("first/plugin", first: "2020-01-01T00:00:00Z"), Plugin("second/plugin", first: "2021-01-01T00:00:00Z") }).Count == 2,
    "Different repository owners with the same basename remain distinct");
var github = Plugin("owner/shared", first: "2020-01-01T00:00:00Z"); github.RepositoryUrl = "https://github.com/owner/shared";
var gitlab = Plugin("owner/shared", first: "2020-01-01T00:00:00Z"); gitlab.RepositoryUrl = "https://gitlab.com/owner/shared";
Check(PluginChronology.News(new[] { github, gitlab }).Count == 2,
    "Identical repository paths on different upstream hosts retain distinct News identities");
var tied = new[] { Plugin("owner/z", first: "2025-01-01T02:00:00+02:00"), Plugin("owner/a", first: "2025-01-01T00:00:00Z") };
Check(Keys(PluginChronology.News(tied)).SequenceEqual(Keys(PluginChronology.News(tied.Reverse()))) &&
    PluginChronology.News(tied).First().RepositorySlug == "owner/a",
    "Equal instants use a deterministic identity tie break regardless of input order or UTC offset spelling");
var oldUpdatedToday = Plugin("owner/old-updated-today", first: "2001-01-01T00:00:00Z", updated: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
var newPublication = Plugin("owner/new-publication", first: "2025-01-01T00:00:00Z", updated: "2025-01-01T00:00:00Z");
Check(Keys(PluginChronology.News(new[] { oldUpdatedToday, newPublication })).SequenceEqual(new[] { "owner/new-publication", "owner/old-updated-today" }),
    "Updating an old plugin today cannot move it ahead of a newly published plugin in News");
Check(Keys(PluginChronology.Sort(new[] { oldUpdatedToday, newPublication }, "updated")).SequenceEqual(new[] { "owner/old-updated-today", "owner/new-publication" }),
    "Updated sorting still promotes the recent update outside the News collection");
Check(PluginChronology.News(new[] { Plugin("owner/update-without-first", updated: "2025-01-01T00:00:00Z") }).Count == 0,
    "A known latest update never makes an unknown first publication eligible for News");
Check(PluginChronology.News(new[] { Plugin("owner/first-without-updates", first: "2025-01-01T00:00:00Z") }).Count == 1,
    "A known first publication is eligible without needing an update timestamp");
Check(PluginChronology.News(Array.Empty<DeckyPluginInfo>()).Count == 0 && PluginChronology.News(new[] { Plugin("owner/no-dates") }).Count == 0,
    "Empty or wholly undated catalogs produce an empty News list");
Console.WriteLine($"{passed} plugin chronology checks passed.");
