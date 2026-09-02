using System.Text.RegularExpressions;

internal static class ReviewedContexts
{
    private static readonly HashSet<string> Brands = new(StringComparer.Ordinal)
    {
        "ASUS", "Lenovo", "MSI", "ROG", "PlayStation", "Nintendo", "Steam Deck", "SteamOS", "Xbox",
        "Steam", "DeckyLoader", "CSS Loader", "Playhub", "Gaming Mode", "Desktop Mode", "Big Picture",
        "Launch Curtain", "News", "Now Playing", "Playhub Artworks", "Playhub Metadata", "Playhub Notifications",
        "Playhub Surround", "Proton VPN", "Quick Settings", "ThemeDeck", "TrailerHero", "Weather", "Metadata",
        "Playhub Desktop Safety", "Playhub Focus Rescue", "Playhub Xbox Game Bar", "Playhub Gaming Mode",
        "SteamGridDB", "GitHub", "UWPHook", "Valve Corporation", "SharpSteam", "VDFParser", " · Steam"
    };
    private static readonly HashSet<string> TechnicalKeys = new(StringComparer.Ordinal)
    {
        "Assets", "Brand", "Extra", "PluginImages", "ServiceLogos", "assets", "Agent", "agent", "PluginLoader", "PluginLoader_noconsole",
        "BackgroundElement", "LayoutRoot", "DialogShowing", "DialogShowingStates", "DialogShowingWithoutSmokeLayer",
        "ButtonBackground", "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
        "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed", "ContentDialogMaxHeight", "ContentDialogMaxWidth",
        "ContentDialogMinHeight", "ContentDialogMinWidth", "ContentDialogPadding", "ContentDialogSeparatorThickness", "ContentDialogTopOverlay",
        "NavigationViewContentMargin", "PlayhubPagePadding", "PluginDescription", "Color", "Fraction", "Height", "Opacity", "Scale", "ScaleX", "Translation", "TrimEnd", "X",
        "progress", "progress.Fraction", "this.StartingValue", "AnimationsEnabledChanged", "Windows.UI.ViewManagement.UISettings",
        "Home + A", "Home + B", "PS + X", "CTRL + 2", "acrylic", "added", "asus", "banner", "cards", "category:", "cover", "custom",
        "discover", "en", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru", "it", "gaming", "gaming-mode", "hero", "home", "icon",
        "image", "installed", "lenovo", "list", "logo", "manage", "mica", "msi", "name", "noloc", "outside-store", "playstation",
        "plugin-artwork-scrim", "plugin-detail", "plugins", "plugins/", "rog", "settings", "solid", "steam-deck", "steamos", "styler", "support", "update-notification", "updated", "video", "welcome", "xbox",
        "Authorization", "Bearer {0}", "PLAYHUB_PLUGIN_REMOVE_PATH", "X-GitHub-Api-Version", "application/vnd.github+json",
        "SteamDeckHomebrew", "LoZazaMastro", "decky-loader", "local", "remote", "Cache write: ", "2022-11-28", ".invalid-", ".github/README.md", ".githubusercontent.com",
        "__root__", "_hero", "_logo", "cssloader", "latest", "installer", "source", "standalone", "sha256:", "tags/", "unknown", "steam", "Playhub-Setup",
        "N", "p", "x", "version", "build", "AUX", "CON", "NUL", "PRN", "GamingMode", "Shell", "Display",
        "Launch-Curtain", "Now-Playing", "Playhub-Artworks", "Playhub-Metadata", "Playhub-Notifications", "Playhub-Surround", "Proton-VPN", "Quick-Settings", "ThemeDeck-Windows",
        "alternate", "base64", "cache", "content", "dashboardEnabled", "decky-store", "entry", "games", "href", "html_url", "http", "https", "id",
        "image/*, video/*, application/octet-stream", "link", "playhub-gm-focus", "playhub-gm-gamebar", "playhub-gm-safety", "rel", "repository", "repositorySlug", "repositoryUrl", "text", "url", "value",
        "PluginLoader%20Win.zip", "{0}-{1}-*.zip", "{0}{1}.zip", " LoadAsync\n", " giochi a Steam.", "(File bloccato: ", ". Chiudi e riapri Steam per attivare DeckyLoader.",
        "DeckyLoader installato (", "Ho aggiunto", "Ho aggiunto ", "Non trovo i file installabili per ",
        "D", "P", "all", "decky", "github", "marker", "playhub", "Playhub {0}", "Playhub {0} · © 2026 Andrea Sgarro (LoZazaMastro)"
    };

    public static (string Category, string Reason)? Classify(string file, string member, string key)
    {
        if (Brands.Contains(key)) return ("excluded", "Product/brand name preserved verbatim");
        if (file == "PluginCatalogService.cs" && member == "CleanReleaseHtml" && key == "No content.")
            return ("excluded", "Verified parser sentinel discarded before display, not interface text");
        if (file == "MainWindow.UpdateDialog.cs" && member == "ApplyPlayhubUpdateDialogTranslation" &&
            key is "Automatic translation" or "Automatische Übersetzung" or "Traducción automática" or "Traduction automatique" or "Traduzione automatica" or "Tradução automática" or "Автоматический перевод" or "Автоматичний переклад" or "स्वचालित अनुवाद" or "自动翻译" or "自動翻訳" or "자동 번역")
            return ("inline", "Complete existing language switch; reuse native inline translation, do not duplicate");
        if (TechnicalKeys.Contains(key)) return ("excluded", "Reviewed technical identifier, path segment, API token, keyboard chord or diagnostic token");
        if (file == "DiagnosticsService.cs") return ("excluded", "Non-UI support diagnostic export, intentionally invariant English; preserves logs, process identifiers and technical data");
        if (file == "Diag.cs") return ("excluded", "Diagnostic log formatting, never rendered in UI");
        if (member == "ThirdPartyLicensesText") return ("excluded", "Original MIT license text preserved per user instruction");
        if (file == "PluginCatalogService.cs" && member is "DescriptionTranslations" or "Definitions") return ("excluded", "Original plugin description/README content and plugin identity metadata; preserved per user instruction");
        if (file == "PluginCatalogService.cs" && member is "PlayhubKeywords" or "ReadInstalledPluginMetadata" or "ReadInstalledVersion" or "ExtractMedia" or "IsMediaMime" or "DetectMediaKind" or "IsRemoteUri" or "IsMediaLink" or "ExtractGithubRepositorySlug" or "TryParseSemanticVersion" or "IsBlockedPluginIdentity" or "MakeInstallFolderName") return ("excluded", "Reviewed plugin metadata, search index or parser protocol token");
        if (file == "ExecutableGameService.cs" && member is "ExcludedFileTerms" or "ExcludedPathTerms" or "GenericFolders" or "LibraryFolders" or "IsUsefulTitle" or "ScoreCandidate") return ("excluded", "Executable discovery filename/path/title filter, not rendered copy");
        if (file == "RemotePluginCatalogService.cs" && member is "FileName" or "IsBlockedIdentity" or "ValidateEntry") return ("excluded", "Catalog path/domain/identity validation constant");
        if (member is "AppendDescriptionInlines" or "PluginStoreCategoryOrder") return ("excluded", "Markdown capture or category sorting token; not rendered copy");
        if (member == "UnsafeTranslation") return ("excluded", "Regular-expression validation syntax");
        if (file == "MainWindow.UpdateDialog.cs" && member == "ApplyPlayhubUpdateDialogTranslation" && key != "github.com" && !key.Contains(' ')) return null;
        if (Regex.IsMatch(key, @"^(?:[A-Za-z0-9-]+\.)+(?:com|org|io|moe)$") || key.StartsWith("Playhub/1.4 (+https://")) return ("excluded", "Host name or HTTP user-agent");
        if (key.StartsWith("Software\\", StringComparison.OrdinalIgnoreCase) || key.StartsWith("SOFTWARE\\")) return ("excluded", "Registry path");
        if (Regex.IsMatch(key, @"^(?:yyyy|dd/|d\\\.).*")) return ("excluded", "Date/time format specifier");
        if (Regex.IsMatch(key, @"^v\d+(?:\.\d+)+$") || key == "Playhub Setup.exe") return ("excluded", "Version or executable filename");
        if (key.StartsWith("# System / Store apps")) return ("excluded", "Embedded PowerShell discovery script");
        if (Regex.IsMatch(key, @"^(?:grids|heroes|logos|icons)/game/")) return ("excluded", "Artwork API route template");
        if (Regex.Replace(key, @"\{\d+(?:[^}]*)?\}", "").All(character => !char.IsLetter(character)) || key == "{0}x{1}") return ("excluded", "Language-invariant value composition");
        return null;
    }
}
