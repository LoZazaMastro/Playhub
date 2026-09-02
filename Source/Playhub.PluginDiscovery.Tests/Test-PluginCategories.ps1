# Run with PowerShell 7. Compiles only pure production members in memory, not the WinUI app.
$ErrorActionPreference = 'Stop'
$app = Join-Path $PSScriptRoot '../Playhub'
$main = Get-Content -Raw -LiteralPath (Join-Path $app 'MainWindow.xaml.cs')
$discovery = Get-Content -Raw -LiteralPath (Join-Path $app 'MainWindow.PluginDiscovery.cs')
$page = Get-Content -Raw -LiteralPath (Join-Path $app 'MainWindow.PluginPage.cs')
$navigation = Get-Content -Raw -LiteralPath (Join-Path $app 'MainWindow.Navigation.cs')
$service = Get-Content -Raw -LiteralPath (Join-Path $app 'Services/PluginCatalogService.cs')
$localization = Get-Content -Raw -LiteralPath (Join-Path $app 'Services/LocalizationService.cs')
$model = Get-Content -Raw -LiteralPath (Join-Path $app 'Models/DeckyPluginInfo.cs')
$catalog = Get-Content -Raw -LiteralPath (Join-Path $app 'Assets/PluginCatalog/external-plugins.json') | ConvertFrom-Json

function Get-ProductionMember([string] $Source, [string] $Declaration) {
    $pattern = '(?ms)^    ' + [regex]::Escape($Declaration) + '.*?^    \}'
    $match = [regex]::Match($Source, $pattern)
    if (!$match.Success) { throw "Missing production member: $Declaration" }
    return $match.Value -replace '^    private', '    public'
}

function Get-ProductionExpression([string] $Source, [string] $Declaration) {
    $match = [regex]::Match($Source, '(?ms)^    ' + [regex]::Escape($Declaration) + '[^;]+;')
    if (!$match.Success) { throw "Missing expression-bodied member: $Declaration" }
    return $match.Value -replace '^    private', '    public'
}

function Assert-True([bool] $Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

$members = @(
    (Get-ProductionMember $main 'private static int PluginStoreCategoryOrder(')
    (Get-ProductionMember $main 'private static string NormalizePluginStoreCategory(')
    (Get-ProductionMember $main 'private IEnumerable<DeckyPluginInfo> FilterPluginAllBySource(')
    (Get-ProductionMember $main 'private IEnumerable<DeckyPluginInfo> SortPluginAll(')
    (Get-ProductionMember $main 'private static DateTime PluginCatalogDate(')
    (Get-ProductionMember $discovery 'private sealed class PluginDiscoveryCategoryState')
    (Get-ProductionMember $discovery 'private static List<DeckyPluginInfo> OrderPluginDiscoveryCategory(')
    (Get-ProductionMember $service 'private static string NormalizeExternalCategory(')
    (Get-ProductionMember $service 'private static string InferInstalledCategory(')
)
foreach ($entry in @(
    @{ Source = $main; Declaration = 'private static string PluginStoreKey(' }
    @{ Source = $discovery; Declaration = 'private static string PluginDiscoveryCategory(' }
    @{ Source = $discovery; Declaration = 'private static bool PluginBelongsToCategory(' }
)) {
    $members += Get-ProductionExpression $entry.Source $entry.Declaration
}
$preview = [regex]::Match($discovery, 'private const int PluginDiscoveryPreviewCount = (\d+);')
Assert-True $preview.Success 'Missing home preview count'
$members += $preview.Value.Replace('private', 'public')

$tests = @'
    public string _pluginAllSource = "all";
    public string _pluginAllSort = "name";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        var categories = new[] { "I plugin di Playhub", "Personalizzazione e media",
            "Libreria e giochi", "Social e community", "Strumenti e utilit\u00e0", "Sistema e hardware" };
        Check(PluginDiscoveryPreviewCount == 4, "Home must show four previews");
        for (var index = 0; index < categories.Length; index++)
        {
            var category = categories[index];
            Check(PluginStoreCategoryOrder(category) == index, "Unexpected category order: " + category);
            if (index == 0) continue;
            var variant = "  " + category.ToUpperInvariant() + "  ";
            Check(NormalizePluginStoreCategory(variant) == category, "UI category normalization");
            Check(NormalizeExternalCategory(variant) == category, "Catalog category normalization");
        }
        foreach (var alias in new[] { "Controller e hardware", "Rete e strumenti", "Sistema e connettivit\u00e0" })
        {
            Check(PluginStoreCategoryOrder(alias) == 5, "Legacy hardware category must stay last");
            Check(NormalizeExternalCategory(alias) == categories[5], "Legacy catalog category");
        }
        Check(NormalizePluginStoreCategory("Strumenti e utilita") == categories[4], "ASCII utility alias");
        Check(NormalizeExternalCategory("Strumenti e utilita") == categories[4], "Catalog utility alias");
        Check(NormalizePluginStoreCategory("Giochi e libreria") == categories[2], "Legacy library alias");
        Check(InferInstalledCategory("Discord", "Voice chat in Game Mode", Array.Empty<string>()) == categories[3], "Installed social plugin");
        Check(InferInstalledCategory("Timer", "Reminder during games", Array.Empty<string>()) == categories[4], "Installed utility plugin");
        Check(InferInstalledCategory("Metadata", "Community images for your game library", Array.Empty<string>()) == categories[2], "Community artwork is not social chat");
        Check(InferInstalledCategory("PowerTools", "CPU and battery settings", Array.Empty<string>()) == categories[5], "Installed hardware plugin");
        var playhubAssignments = new[] {
            ("Quick Settings", categories[4]),
            ("Launch Curtain", categories[4]), ("Playhub Notifications", categories[4]),
            ("Playhub Surround", categories[4]), ("Weather", categories[4]),
            ("Playhub Artworks", categories[1]), ("Now Playing", categories[1]),
            ("ThemeDeck", categories[1]), ("TrailerHero", categories[1]),
            ("Playhub Metadata", categories[2]), ("News", categories[3]), ("Proton VPN", categories[5])
        };
        var playhubPlugins = new List<DeckyPluginInfo>();
        foreach (var (name, category) in playhubAssignments)
        {
            var plugin = new DeckyPluginInfo { Name = name, IsPlayhubPlugin = true, Category = "Playhub" };
            playhubPlugins.Add(plugin);
            Check(PluginDiscoveryCategory(plugin) == category, "Functional Playhub category: " + name);
            Check(PluginBelongsToCategory(plugin, categories[0].ToUpperInvariant()), "Playhub membership: " + name);
            Check(PluginBelongsToCategory(plugin, category.ToUpperInvariant()), "Functional membership: " + name);
            Check(categories.Count(candidate => PluginBelongsToCategory(plugin, candidate)) == 2,
                "Playhub plugin must appear once in its source group and once in its functional group: " + name);
        }
        var futurePlayhub = new DeckyPluginInfo { Name = "Future Playhub plugin",
            IsPlayhubPlugin = true, Category = "  MEDIA E PERSONALIZZAZIONE  " };
        Check(PluginDiscoveryCategory(futurePlayhub) == categories[1], "Unknown Playhub names use normalized catalog category");
        Check(PluginBelongsToCategory(futurePlayhub, categories[0]) && PluginBelongsToCategory(futurePlayhub, categories[1]),
            "Unknown Playhub names retain both memberships");
        var externalNamesake = new DeckyPluginInfo { Name = "News", IsPlayhubPlugin = false,
            RepositoryName = "external-news", Category = "Controller e hardware", CatalogSource = "outside-store" };
        Check(PluginDiscoveryCategory(externalNamesake) == categories[5], "External names must not inherit Playhub overrides");
        Check(!PluginBelongsToCategory(externalNamesake, categories[0]) &&
            categories.Count(category => PluginBelongsToCategory(externalNamesake, category)) == 1,
            "External plugins belong only to their functional group");

        var plugins = Enumerable.Range(0, 12).Select(index => new DeckyPluginInfo {
            Name = "Plugin " + index.ToString("D2"), RepositoryName = "repo-" + index,
            Category = categories[3], IsPlayhubPlugin = false,
            CatalogSource = index % 2 == 0 ? "decky-store" : "outside-store",
            ReleasePublishedAt = new DateTime(2026, 1, 1).AddDays(index).ToString("yyyy-MM-dd"),
            UpdatedAt = new DateTime(2026, 1, 1).AddDays(11 - index).ToString("yyyy-MM-dd")
        }).ToList();
        var state = new PluginDiscoveryCategoryState();
        var ordered = OrderPluginDiscoveryCategory(state, plugins);
        var keys = ordered.Select(PluginStoreKey).ToArray();
        Check(keys.Distinct().Count() == plugins.Count, "Ordering must retain all identities");
        Check(ordered.Take(PluginDiscoveryPreviewCount).Count() == 4, "Preview slice");
        Check(OrderPluginDiscoveryCategory(state, plugins.AsEnumerable().Reverse().ToList()).Select(PluginStoreKey).SequenceEqual(keys), "Rebuild must preserve random order");
        var refreshed = plugins.Select(plugin => new DeckyPluginInfo { Name = plugin.Name,
            RepositoryName = plugin.RepositoryName, Version = "2.0.0" }).ToList();
        Check(OrderPluginDiscoveryCategory(state, refreshed).All(plugin => plugin.Version == "2.0.0"), "Use refreshed plugin objects");
        OrderPluginDiscoveryCategory(state, refreshed.Skip(3).ToList());
        Check(OrderPluginDiscoveryCategory(state, refreshed).Select(PluginStoreKey).SequenceEqual(keys), "Recovered entries keep their position");
        refreshed.Add(new DeckyPluginInfo { Name = "New plugin", RepositoryName = "new-repo" });
        Check(OrderPluginDiscoveryCategory(state, refreshed).Select(PluginStoreKey).Take(12).SequenceEqual(keys), "New entries must not shuffle previews");
        Check(OrderPluginDiscoveryCategory(new PluginDiscoveryCategoryState(), new List<DeckyPluginInfo>()).Count == 0, "Empty category");

        var samples = new HashSet<string>();
        for (var trial = 0; trial < 8; trial++)
            samples.Add(string.Join(",", OrderPluginDiscoveryCategory(new PluginDiscoveryCategoryState(), plugins).Take(4).Select(PluginStoreKey)));
        Check(samples.Count > 1, "New sessions must randomize previews");

        var view = new PluginCategoryProbe();
        Check(view.SortPluginAll(view.FilterPluginAllBySource(plugins)).Count() == 12, "Full category must not be preview-limited");
        foreach (var source in new[] { "decky", "github" })
        {
            view._pluginAllSource = source;
            var filtered = view.FilterPluginAllBySource(plugins).ToList();
            Check(filtered.Count == 6, "Source filtering must retain all matching category plugins");
            Check(filtered.All(plugin => (plugin.CatalogSource == "decky-store") == (source == "decky")), "Source partition");
            view._pluginAllSort = "added";
            Check(view.SortPluginAll(filtered).First().RepositoryName == (source == "decky" ? "repo-10" : "repo-11"), "Newest-added sort");
            view._pluginAllSort = "updated";
            Check(view.SortPluginAll(filtered).First().RepositoryName == (source == "decky" ? "repo-0" : "repo-1"), "Newest-updated sort");
            view._pluginAllSort = "name";
            Check(view.SortPluginAll(filtered).Select(plugin => plugin.Name).SequenceEqual(filtered.Select(plugin => plugin.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)), "Alphabetical sort");
        }
        view._pluginAllSource = "playhub";
        Check(!view.FilterPluginAllBySource(plugins).Any(), "Playhub source excludes external entries");

        var mixed = plugins.Concat(playhubPlugins).Append(externalNamesake).ToList();
        Check(mixed.Select(PluginStoreKey).Distinct().Count() == mixed.Count, "Shared membership must not duplicate catalog identities");
        Check(mixed.Count(plugin => PluginBelongsToCategory(plugin, categories[0])) == playhubPlugins.Count,
            "Playhub source group retains all Playhub plugins");
        Check(categories.Skip(1).Sum(category => mixed.Count(plugin => PluginBelongsToCategory(plugin, category))) == mixed.Count,
            "Every fixture appears in exactly one functional category");
        foreach (var category in categories.Skip(1))
        {
            var members = mixed.Where(plugin => PluginBelongsToCategory(plugin, category)).ToList();
            foreach (var source in new[] { "all", "playhub", "decky", "github" })
            {
                view._pluginAllSource = source;
                var expected = members.Where(plugin => source == "all" ||
                    (source == "playhub" && plugin.IsPlayhubPlugin) ||
                    (source == "decky" && plugin.CatalogSource == "decky-store") ||
                    (source == "github" && !plugin.IsPlayhubPlugin && plugin.CatalogSource != "decky-store"));
                Check(view.FilterPluginAllBySource(members).Select(PluginStoreKey).SequenceEqual(expected.Select(PluginStoreKey)),
                    "Functional category/source intersection: " + category + "/" + source);
            }
        }
    }
'@

$navigationMembers = @(
    (Get-ProductionMember $page 'private void AttachPluginStoreToolbar(')
    (Get-ProductionMember $page 'private void UpdatePluginBackButton(')
    (Get-ProductionMember $page 'private void FinishPluginBackAnimation(')
    (Get-ProductionMember $main 'private void InvalidatePluginAllViews(')
    (Get-ProductionExpression $navigation 'private string NavigationPositionKey =>')
)
# Property-only doubles exercise production state logic without loading any WinUI types.
$navigationTests = @'
    public enum Visibility { Visible, Collapsed }
    public sealed class Element
    {
        public Visibility Visibility { get; set; } = Visibility.Collapsed;
        public double Opacity { get; set; }
        public double Width { get; set; } = 32;
        public Spacing Margin { get; } = new();
        public bool IsLoaded { get; set; }
        public bool IsHitTestVisible { get; set; }
        public bool IsTabStop { get; set; }
    }
    public sealed class Spacing { public double Right { get; set; } = 12; }
    public sealed class Offset { public double X { get; set; } }
    public sealed class Storyboard
    {
        public event EventHandler Completed { add { } remove { } }
        public void Begin() { }
        public void Stop() { }
    }
    private bool _pluginBackVisible;
    private readonly Offset _pluginBackSwitcherOffset = new();
    private Storyboard? _pluginBackAnimation;
    private static bool MotionEnabled() => false;
    private static void AddPluginSearchAnimation(Storyboard animation, object target, string property, double from, double to) { }
    private readonly Element _pluginStoreToolbar = new(), _pluginDiscoverTools = new(),
        _pluginShowAllButton = new(), _pluginBackButton = new();
    private readonly Stack<int> _pluginStoreHistory = new();
    private string _currentPageTag = "plugins", _pluginStoreMode = "discover";
    private string? _pluginCategoryFilter;
    private bool _pluginShowAll;
    private object? _pluginAllCardsCache = new(), _pluginAllListCache = new(),
        _pluginManageCardsCache = new(), _pluginManageListCache = new();
    private bool _pluginCardsDirty, _pluginManagementDirty;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        var probe = new PluginNavigationProbe();
        foreach (var page in new[] { "plugins", "plugin-detail", "settings" })
        foreach (var mode in new[] { "discover", "manage" })
        foreach (var hasHistory in new[] { false, true })
        {
            probe._currentPageTag = page;
            probe._pluginStoreMode = mode;
            probe._pluginStoreHistory.Clear();
            if (hasHistory) probe._pluginStoreHistory.Push(1);
            probe.UpdatePluginBackButton();
            var inStore = page == "plugins" || page == "plugin-detail";
            Check((probe._pluginStoreToolbar.Visibility == Visibility.Visible) == inStore, "Store toolbar visibility");
            Check((probe._pluginDiscoverTools.Visibility == Visibility.Visible) == (page == "plugins"),
                "Search must remain visible in both store modes and hidden on detail pages");
            Check((probe._pluginShowAllButton.Visibility == Visibility.Visible) == (page == "plugins" && mode == "discover"),
                "Show All is discover-only");
            Check((probe._pluginBackButton.Visibility == Visibility.Visible) == (inStore && hasHistory), "Back visibility");
            if (inStore && hasHistory) Check(probe._pluginBackButton.Opacity == 1, "Back appears immediately");
        }
        probe.InvalidatePluginAllViews();
        Check(probe._pluginAllCardsCache is null && probe._pluginAllListCache is null &&
            probe._pluginManageCardsCache is null && probe._pluginManageListCache is null,
            "Shared filter changes must invalidate both modes' list and card caches");
        Check(probe._pluginCardsDirty && probe._pluginManagementDirty, "Both modes must rerender after invalidation");
        probe._currentPageTag = "plugins";
        probe._pluginStoreMode = "manage";
        probe._pluginCategoryFilter = "Social e community";
        probe._pluginShowAll = true;
        Check(probe.NavigationPositionKey == "plugins/manage", "Manage offsets must ignore discovery category state");
        probe._pluginStoreMode = "discover";
        Check(probe.NavigationPositionKey == "plugins/discover/Social e community", "Category offsets must not collide with Manage");
        probe._pluginCategoryFilter = null;
        Check(probe.NavigationPositionKey == "plugins/discover/all", "All-plugins offset");
        probe._pluginShowAll = false;
        Check(probe.NavigationPositionKey == "plugins/discover/home", "Discovery home offset");
        probe._currentPageTag = "plugin-detail";
        Check(probe.NavigationPositionKey == "plugin-detail", "Detail offset must remain separate from store modes");
    }
'@

$code = "#nullable enable`nusing System;`nusing System.Linq;`nusing System.Globalization;`nusing System.Text.RegularExpressions;`nusing Playhub.Models;`n" +
    $model.Replace('namespace Playhub.Models;', 'namespace Playhub.Models {') + "`n}`n" +
    "public sealed class PluginCategoryProbe {`n" + ($members -join "`n") + "`n" + $tests + "`n}`n" +
    "public sealed class PluginNavigationProbe {`n" + ($navigationMembers -join "`n") + "`n" + $navigationTests + "`n}"
Add-Type -TypeDefinition $code
[PluginCategoryProbe]::Run()
Write-Output 'PASS pure production functional categories, Playhub multiple membership, preview stability, source filters and sorting'
[PluginNavigationProbe]::Run()
Write-Output 'PASS pure production toolbar visibility, separate cache invalidation and navigation key tests'

Assert-True ($discovery.Contains('orderedPlugins.Take(PluginDiscoveryPreviewCount)')) 'Preview limit must remain home-only'
Assert-True ($discovery.Contains('_pluginCategoryFilter = category;') -and $discovery.Contains('_pluginShowAll = true;')) 'Category click must open its complete list'
$render = Get-ProductionMember $main 'private void RenderPluginCards('
Assert-True ($render.Contains('SortPluginAll(FilterPluginAllBySource(visibleQuery))')) 'Full category must retain source filtering and sorting'
Assert-True ($render.Contains('BuildPluginStoreCategory(_pluginCategoryFilter ?? "Tutti i plugin", visible, showLayoutToggle: true)')) 'Full category must render all visible plugins'
Assert-True ($render.Contains('PluginBelongsToCategory(plugin, _pluginCategoryFilter)')) 'Full categories must include Playhub functional membership'
Assert-True ($render.Contains('visible.Where(plugin => PluginBelongsToCategory(plugin, category))')) 'Home must use non-exclusive category membership'
$homeCategoryList = [regex]::Match($render, '(?s)foreach\s*\(var category in new\[\]\s*\{(?<items>.*?)\}\s*\)')
Assert-True $homeCategoryList.Success 'Missing explicit home category list'
$homeCategories = @('[' + $homeCategoryList.Groups['items'].Value + ']' | ConvertFrom-Json)
Assert-True ($homeCategories.Count -eq 6) 'Home must render all six categories'
for ($index = 0; $index -lt $homeCategories.Count; $index++) {
    Assert-True ([PluginCategoryProbe]::PluginStoreCategoryOrder($homeCategories[$index]) -eq $index) 'Home category order'
}
Write-Output 'PASS home and complete-category rendering contracts'

$manage = Get-ProductionMember $main 'private void RenderPluginManagement('
Assert-True ($manage.Contains('.Where(plugin => plugin.IsInstalled &&') -and
    $manage.Contains('SortPluginAll(FilterPluginAllBySource(installed')) 'Manage source All must remain scoped to installed plugins'
Assert-True ($manage.Contains('_pluginManageCardsCache') -and $manage.Contains('_pluginManageListCache') -and
    $manage -notmatch '_pluginAll(?:Cards|List)Cache') 'Manage must never reuse discovery list/card caches'
$restore = Get-ProductionMember $page 'private void RestorePluginStoreView('
Assert-True ($restore.Contains('_pluginManageQuery = state.ManageQuery;') -and
    $restore.Contains('_pluginManageQuery != state.ManageQuery') -and
    $restore.Contains('if (changed) InvalidatePluginAllViews();')) 'History must restore the Manage query and invalidate stale views'
Assert-True ($restore.Contains('_pluginFeaturedHost.Visibility = state.Mode == "discover"')) 'Restoring Manage must not reveal discovery featured content'
Write-Output 'PASS installed-only Manage rendering, cache isolation and history restoration contracts'

$hero = Get-ProductionMember $page 'private void ConfigurePluginDetailHero('
$captureScrim = $hero.IndexOf('Equals(child.Tag, "plugin-artwork-scrim")')
$clearImage = $hero.IndexOf('imagePanel.Children.Clear();')
$addCanvas = $hero.IndexOf('imagePanel.Children.Add(imageCanvas);')
$addScrim = $hero.IndexOf('imagePanel.Children.Add(artworkScrim);')
Assert-True ($captureScrim -ge 0 -and $captureScrim -lt $clearImage) 'Capture the original gradient before clearing the image panel'
Assert-True ($addCanvas -gt $clearImage -and $addScrim -gt $addCanvas) 'Restore the original gradient above the image canvas'
Assert-True ($hero.Contains('imagePanel.Clip = imageClip;') -and $hero.Contains('imageClip.Rect = new Rect(0, 0, width, height);')) 'Clip both artwork and gradient to the current responsive viewport'
Assert-True ($hero.Contains('catalogBadge.Tag = "plugin-detail-source-badge";') -and
    $hero.Contains('badge.HorizontalAlignment = HorizontalAlignment.Right;') -and
    $hero.Contains('badge.VerticalAlignment = VerticalAlignment.Top;')) 'Detail source badges must remain identifiable and top-right'
Write-Output 'PASS detail hero gradient preservation, viewport clipping and source badge contracts'

$utility = 'Strumenti e utilit' + [char]0x00e0
$expected = @('Personalizzazione e media', 'Libreria e giochi', 'Social e community', $utility, 'Sistema e hardware')
$active = @($catalog.plugins | Where-Object active)
$groups = @($active | Group-Object { [PluginCategoryProbe]::NormalizeExternalCategory($_.category) })
Assert-True ($groups.Count -eq 5) 'Bundled external catalog must populate exactly five non-Playhub categories'
foreach ($group in $groups) {
    Assert-True ($group.Name -in $expected -and $group.Count -ge 4) "Invalid or underpopulated category: $($group.Name)"
    Write-Output ("{0}: {1}" -f $group.Name, $group.Count)
}
foreach ($category in @('Social e community', $utility)) {
    $entry = [regex]::Match($localization, '(?m)^\s*\["' + [regex]::Escape($category) + '"\] = V\(([^\r\n]+)\),')
    Assert-True ($entry.Success -and [regex]::Matches($entry.Groups[1].Value, '"(?:[^"\\]|\\.)*"').Count -eq 11) "Missing category translations: $category"
}
foreach ($repository in @('Necrosiak/Steamcord', 'ILadis/ts3-qs4sd', 'andrewburgess/steamdeck-discord-status', 'itsOwen/playcount-decky', 'stevensnoeijen/decky-insignia')) {
    $plugin = @($active | Where-Object repository -EQ $repository)
    Assert-True ($plugin.Count -eq 1 -and $plugin[0].category -eq 'Social e community') "Social assignment: $repository"
}
foreach ($repository in @('wynn1212/SDH-Notebook', 'cat-in-a-box/Decky-Translator', 'Teppichseite/DeckPass', 'panyiwei-home/Friendeck', 'jessebofill/DeckWebBrowser')) {
    $plugin = @($active | Where-Object repository -EQ $repository)
    Assert-True ($plugin.Count -eq 1 -and $plugin[0].category -eq $utility) "Utility assignment: $repository"
}
Write-Output 'PASS bundled catalog membership and all 11 category translations'
