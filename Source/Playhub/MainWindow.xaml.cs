using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Models;
using Playhub.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace Playhub;

public sealed partial class MainWindow : Window
{
    private static readonly Regex DescriptionInlinePattern = new(
        @"(?<link>\[(?<linkText>[^\]]+)\]\((?<linkUrl>https?://[^)\s]+)\))|" +
        @"(?<bold>\*\*(?<boldText>.+?)\*\*)|(?<boldAlt>__(?<boldAltText>.+?)__)|" +
        @"(?<italic>(?<!\*)\*(?<italicText>[^*\n]+)\*(?!\*))|" +
        @"(?<italicAlt>(?<!_)_(?<italicAltText>[^_\n]+)_(?!_))|" +
        @"(?<code>`(?<codeText>[^`\n]+)`)|(?<url>https?://[^\s]+)",
        RegexOptions.Compiled);
    private readonly SettingsService _settingsService = new();
    private readonly DeckyInstallerService _deckyInstaller = new();
    private readonly PluginCatalogService _catalog = new();
    private readonly DeckyPluginService _pluginService = new();
    private readonly GamingModeService _gamingMode = new();
    private readonly UwpXboxService _uwpXbox = new();
    private readonly ExecutableGameService _executableGameService = new();
    private readonly EpicGamesService _epicService = new();
    private readonly GogService _gogService = new();
    private readonly CssLoaderInstallService _cssLoaderInstaller = new();
    private readonly ExtraService _extra = new();
    private readonly SteamService _steam = new();
    private readonly PlayhubUpdateService _updateService = new();

    private readonly ObservableCollection<DeckyPluginInfo> _plugins = new();
    private readonly ObservableCollection<DeckyBuildRun> _deckyBuilds = new();
    private readonly ObservableCollection<UwpGameEntry> _uwpGames = new();
    private readonly ObservableCollection<UwpGameEntry> _executableGames = new();
    private readonly ObservableCollection<UwpGameEntry> _epicGames = new();
    private readonly ObservableCollection<UwpGameEntry> _gogGames = new();
    private readonly Dictionary<string, ToggleSwitch> _gamingToggles = new();
    private readonly List<WeakReference<Button>> _primaryButtons = new();
    // Weak keys so rebuilt UI elements (e.g. plugin cards) can be garbage-collected.
    private readonly NativeLocalizationKeys _localizationKeys = new();

    private PlayhubSettings _settings = new();
    private GamingModeConfig _gamingConfig = GamingModeService.CreateDefaultConfig();
    private bool _loadingSettings;
    private bool _loadingGaming = true; // guardia: nessun auto-save finché la config non è caricata
    private PointerRoutedEventArgs? _lastWheelArgs;
    private AppWindow? _appWindow;

    private Grid _titleBar = new();
    private Border _titleBarAccent = new();
    private TextBlock _titleBarText = new();
    private Grid _pageHost = new();
    private ScrollViewer _contentScroller = new();
    private InfoBar _status = new();
    private ComboBox _deckyBuildCombo = new();
    private Border _devTile = new();
    private FontIcon _devGlyph = new();
    private TextBlock _devStatus = new();
    private Border _installTile = new();
    private FontIcon _installGlyph = new();
    private TextBlock _installStatus = new();
    private Button _installButton = new();
    private Border _steamTile = new();
    private FontIcon _steamGlyph = new();
    private TextBlock _steamStatus = new();
    private Button _steamButton = new();
    private Border _gameBarTile = new();
    private FontIcon _gameBarGlyph = new();
    private TextBlock _gameBarStatus = new();
    private Button _gameBarButton = new();
    private StackPanel _pluginCards = new();
    private Grid _pluginFeaturedHost = new();
    private Grid _pluginFeaturedCarouselHost = new();
    private Button _pluginFeaturedPreviousButton = new();
    private Button _pluginFeaturedNextButton = new();
    private TextBox _pluginSearchBox = new();
    private Grid _pluginDiscoverTools = new();
    private Button _pluginShowAllButton = new();
    private StackPanel _pluginDiscoverView = new();
    private StackPanel _pluginManageView = new();
    private StackPanel _pluginManageContent = new();
    private StackPanel _pluginDetailsHost = new();
    private Button _pluginDiscoverButton = new();
    private Button _pluginManageButton = new();
    private ProgressBar _pluginManageProgress = new();
    private TextBlock _pluginManageProgressText = new();
    private string _pluginStoreMode = "discover";
    private bool _pluginBulkUpdateRunning;
    private bool _pluginManageCompact;
    private bool _pluginShowAll;
    private string _pluginAllSource = "all";
    private string _pluginAllSort = "name";
    private UIElement? _pluginAllCardsCache;
    private UIElement? _pluginAllListCache;
    private UIElement? _pluginManageCardsCache;
    private UIElement? _pluginManageListCache;
    private string _pluginManageQuery = string.Empty;
    private int _pluginManageColumnCount = 3;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, Panel> PluginViewOwners = new();
    private DataTemplate? _pluginRepeaterTemplate;
    private bool _pluginCardsDirty = true;
    private bool _pluginManagementDirty = true;
    private bool _suppressPluginSearchRender;
    private string? _expandedPluginKey;
    private int _pluginStoreColumnCount = 3;
    private int _featuredPluginIndex = -1;
    private bool _featuredPluginTransitioning;
    private bool _featuredPluginExpanded;
    private readonly List<string> _featuredPluginKeys = new();
    private readonly Dictionary<string, FrameworkElement> _featuredFrameCache = new(StringComparer.OrdinalIgnoreCase);
    private int _featuredFrameCacheVersion;
    private Storyboard? _featuredSlideStoryboard;
    private Grid _loadingOverlay = new();

    // Media gallery (lightbox) state.
    private Grid _mediaLightbox = new();
    private Border _lightboxStage = new();
    private TextBlock _lightboxCounter = new();
    private Button _lightboxPrev = new();
    private Button _lightboxNext = new();
    private List<PluginMediaInfo> _lightboxMedia = new();
    private int _lightboxIndex;
    private Action? _collapseOpenPluginCard; // accordion: collapses the currently open store card
    private Action? _cancelPluginMorphVisual;
    private int _pluginCardMorphVersion;
    private Grid _welcomeRoot = new();
    private NavigationView _navigation = new();
    private const int CurrentWelcomeVersion = 3;
    // Ri-traduce la slide di benvenuto attualmente mostrata. La prima slide è
    // costruita prima del caricamento della lingua, quindi va aggiornata dopo
    // il load (vedi ApplyLanguage), altrimenti resterebbe nella lingua di default.
    private Action? _refreshWelcomeSlide;
#if PLAYHUB_UI_REVIEW
    private Action<int>? _navigateWelcomeSlide;
    private Func<(int RequestedIndex, int RenderedIndex, bool IsAnimating, double Opacity,
        double OffsetX, int ArtworkSources, int ArtworkOpened)>? _readWelcomeMotionState;
    private Func<(int ActiveLayers, int LayerCount, int SizeChanges, int Transitions, int Failures,
        double LastSubmitMs, double LastCompletionMs, double MaxCompletionMs)>? _readWelcomeMotionDiagnostics;
#endif
    private Windows.Media.Playback.MediaPlayer? _lightboxPlayer;
    private readonly List<TutorialVideoSession> _tutorialVideos = new();
    private string _currentPageTag = "welcome";
    private bool _mediaPlaybackReady;
    private StackPanel _uwpGamesPanel = new();
    private StackPanel _executableGamesPanel = new();
    private StackPanel _executableSourcesPanel = new();
    private StackPanel _epicGamesPanel = new();
    private StackPanel _gogGamesPanel = new();
    private TextBlock _cssLoaderStatusText = new();
    private Button _cssLoaderInstallButton = new();
    private Button _cssLoaderRemoveButton = new();
    private Button _cssProfileInstallButton = new();
    private ProgressBar _cssLoaderInstallBar = new();
    private bool _cssLoaderInstallBusy;
    private bool _executableScanInProgress;
    private int _uwpCardColumnCount = 3;
    private int _executableCardColumnCount = 3;
    private int _epicCardColumnCount = 3;
    private int _gogCardColumnCount = 3;
    private Button _uwpChevron = new();
    private Button _executableChevron = new();
    private Button _epicChevron = new();
    private Button _gogChevron = new();
    private StackPanel _startupAppsPanel = new();
    private Border _deckyQuickAccessCard = new();
    private Border _deckyBigPictureCard = new();
    private Button _repairButton = new();
    private ProgressBar _repairBar = new();
    private TextBlock _repairStatusText = new();
    private bool _repairRunning;
    private Button _diagnosticsButton = new();
    private TextBlock _diagnosticsStatusText = new();
    private bool _diagnosticsRunning;
    private Button _playhubUpdateButton = new();
    private ProgressBar _playhubUpdateBar = new();
    private TextBlock _playhubUpdateStatus = new();
    private bool _playhubUpdateRunning;

    // Gaming Mode: visual mode selector + logo preview.
    private Border _desktopModeTile = new();
    private Border _gamingModeTile = new();
    private Action<bool>? _setDesktopSelected;
    private Action<bool>? _setGamingSelected;
    private Image _splashLogoPreview = new();
    private ComboBox _themeCombo = new();
    private ComboBox _languageCombo = new();
    private ComboBox _backdropCombo = new();
    private ComboBox _startupPageCombo = new();
    private StackPanel _accentColorPanel = new();
    private readonly List<Button> _accentSwatches = new();
    private readonly List<Button> _welcomeBackdropButtons = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, Dictionary<string, SolidColorBrush>>
        WelcomeBackdropBrushes = new();
    private string? _pluginViewLanguage;
    private TextBox _deckyPluginsBox = new();
    private PasswordBox _xboxSteamGridDbKeyBox = new();

    private ComboBox _defaultModeCombo = new();
    private TextBox _steamPathBox = new();
    private TextBox _steamArgsBox = new();
    private TextBox _deckyPathBox = new();
    private TextBox _sunshinePathBox = new();
    private TextBox _splashLogoBox = new();
    private ComboBox _splashLogoCombo = new();
    private NumberBox _delaySteamBox = new();
    private NumberBox _mouseDelayBox = new();
    private NumberBox _splashMinBox = new();
    private NumberBox _splashMaxBox = new();
    private NumberBox _apiPortBox = new();
    private sealed record ComboOption(string Key, string LabelKey);

    private sealed class TutorialVideoSession
    {
        public TutorialVideoSession(string pageTag, bool requiresDeckyInstalled, string videoPath, Grid host)
        {
            PageTag = pageTag;
            RequiresDeckyInstalled = requiresDeckyInstalled;
            VideoPath = videoPath;
            Host = host;
        }

        public string PageTag { get; }
        public bool RequiresDeckyInstalled { get; }
        public string VideoPath { get; }
        public Grid Host { get; }
        public Windows.Media.Playback.MediaPlayer? Player { get; set; }
        public bool IsInViewport { get; set; }
    }

    private sealed class ComboChoice
    {
        public ComboChoice(string key, string labelKey, string text)
        {
            Key = key;
            LabelKey = labelKey;
            Text = text;
        }

        public string Key { get; }
        public string LabelKey { get; }
        public string Text { get; set; }
        public override string ToString() => Text;
    }

    private static readonly ComboOption[] BackdropOptions =
    {
        new("mica", "Mica"),
        new("acrylic", "Acrylic"),
        new("solid", "Sfondo pieno")
    };

    private static readonly ComboOption[] StartupPageOptions =
    {
        new("decky", "DeckyLoader"),
        new("plugins", "Plugin Store"),
        new("gaming", "Gaming Mode"),
        new("xbox", "Importa Giochi"),
        new("styler", "Big Picture Styler"),
        new("settings", "Impostazioni")
    };

    private static readonly ComboOption[] ModeOptions =
    {
        new("Desktop", "Desktop"),
        new("Gaming", "Gaming")
    };

    private static readonly ComboOption[] SplashLogoOptions =
    {
        new("playhub", "Playhub"),
        new("asus", "ASUS"),
        new("lenovo", "Lenovo"),
        new("msi", "MSI"),
        new("playstation", "PlayStation"),
        new("rog", "ROG"),
        new("steam-deck", "Steam Deck"),
        new("steamos", "SteamOS"),
        new("xbox", "Xbox"),
        new("custom", "Personalizzato")
    };

    public MainWindow()
    {
        InitializeComponent();
        Title = "Playhub";
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();
        Closed += (_, _) => { CancelNavigationRestore(); CancelPluginCardMorph(); ReleaseMediaForShutdown(); };
        SetWindowShape();
        // Seed accent brushes BEFORE the navigation is built so its selection
        // indicator and item brushes resolve our instances (and update live).
        ApplyAccentResources(ParseColor(_settings.AccentColor));
        BuildShell();
#if PLAYHUB_UI_REVIEW
        Diag.Step("UI review: shell ready");
        _loadingOverlay.Visibility = Visibility.Collapsed;
#else
        // Al ritorno sull'app (es. dopo aver cambiato l'impostazione in Windows),
        // aggiorna lo stato della card Game Bar.
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
                try { RefreshGameBarStep(); } catch { }
        };
        _ = LoadAsync();
#endif
    }

    private async Task LoadAsync()
    {
        try
        {
            Diag.Step("LoadAsync begin");
            _settings = await _settingsService.LoadAsync();
            // Default the DeckyLoader plugins folder so the setting is invisible to users.
            if (string.IsNullOrWhiteSpace(_settings.DeckyPluginsPath))
            {
                _settings.DeckyPluginsPath = AppPaths.DefaultDeckyPluginsPath;
            }
            var needsCurrentWelcome = !_settings.WelcomeCompleted ||
                                      _settings.WelcomeVersion < CurrentWelcomeVersion;
            if (!needsCurrentWelcome)
            {
                var startupTag = string.IsNullOrWhiteSpace(_settings.StartupPage) ? "decky" : _settings.StartupPage;
                var startupItem = _navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, startupTag));
                if (startupItem is not null)
                {
                    _navigation.SelectedItem = startupItem;
                }
            }
            if (string.Equals(_settings.AccentColor, "#4CC2FF", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_settings.AccentColor, "#FFB454", StringComparison.OrdinalIgnoreCase))
            {
                _settings.AccentColor = "#FFCB0F";
                await _settingsService.SaveAsync();
            }

            // Il plugin Decky si aggiorna da solo, senza chiedere niente:
            // dopo l'installazione di Playhub e dopo ogni aggiornamento. Non
            // deve dipendere dal fatto che l'utente prema "Installa o
            // aggiorna". Se Decky non c'e', non fa nulla.
            // L'agente si aggiorna da solo, prima di ogni altra cosa: se resta
            // indietro, tutto quello che dipende da lui si comporta come la
            // versione precedente senza dirlo a nessuno.
            Diag.Step("LoadAsync: SyncAgent");
            try
            {
                if (await _gamingMode.SyncAgentAsync())
                {
                    Diag.Step("LoadAsync: agente Gaming Mode aggiornato");
                }
            }
            catch (Exception ex)
            {
                Diag.Step("LoadAsync: aggiornamento dell'agente non riuscito: " + ex.Message);
            }

            Diag.Step("LoadAsync: SyncDeckyPlugin");
            try
            {
                if (await _gamingMode.SyncDeckyPluginAsync(_settings.DeckyPluginsPath))
                {
                    Diag.Step("LoadAsync: plugin Decky aggiornato");
                }
            }
            catch (Exception ex)
            {
                Diag.Step("LoadAsync: sync plugin Decky non riuscita: " + ex.Message);
            }

            ApplyTheme();
            ApplyBackdrop();
            PopulateSettingsControls();
            RefreshCssLoaderState();
            ApplyLanguage();
            Diag.Step("LoadAsync: LoadDeckyBuildsSilently");
            _ = LoadDeckyBuildsSilentlyAsync();
            Diag.Step("LoadAsync: RefreshPlugins");
            _ = RefreshPluginsAsync();
            Diag.Step("LoadAsync: RefreshGamingMode");
            await RefreshGamingModeAsync();
            Diag.Step("LoadAsync: ResetXboxGameBarIfStuck");
            ResetXboxGameBarIfStuck();
            Diag.Step("LoadAsync: RefreshDeckyState");
            await RefreshDeckyStateAsync();
            Diag.Step("LoadAsync: steps done");
            ApplyLanguage();
            InitializeSupportReminder();

            // Controllo aggiornamenti non bloccante: se c'è una versione nuova
            // su GitHub, compare una notifica in-app con il link alla release.
            _ = CheckPlayhubUpdatesSilentlyAsync();
        }
        catch (Exception ex)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "playhub_crash.txt"),
                    DateTime.Now + " LoadAsync\n" + ex + "\n\n");
            }
            catch
            {
            }
        }
        finally
        {
            ShowPage(_currentPageTag);

            // The loading overlay must ALWAYS hide, even if a step above failed,
            // otherwise the app is stuck on the spinner forever.
            FadeOutThenHide(_loadingOverlay);
            _ = EnableMediaPlaybackAfterColdStartAsync();
        }

        // Re-check DeckyLoader state whenever the window regains focus
        // (e.g. after enabling Developer Mode in Windows Settings).
        Activated += async (_, _) =>
        {
            try { await RefreshDeckyStateAsync(); } catch { }
        };
    }

    private async Task EnableMediaPlaybackAfterColdStartAsync()
    {
        try
        {
            // Nessun ritardo: i video partono subito. Il crash all'avvio era la
            // scrittura di config.json (già risolta), non il media.
            await Task.CompletedTask;
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _mediaPlaybackReady = true;
                    ShowPage(_currentPageTag);
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }

    private void SetWindowShape()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            var dpi = Math.Max(96, GetDpiForWindow(hwnd));
            var scale = dpi / 96.0;
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var width = Math.Min((int)Math.Round(1280 * scale), Math.Max(960, workArea.Width - 80));
            var height = Math.Min((int)Math.Round(860 * scale), Math.Max(720, workArea.Height - 80));
            _appWindow.Resize(new SizeInt32(width, height));
            _appWindow.Move(new PointInt32(
                workArea.X + Math.Max(0, (workArea.Width - width) / 2),
                workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "Playhub.ico");
            if (File.Exists(icon))
            {
                _appWindow.SetIcon(icon);
            }
        }
        catch
        {
        }
    }

    private void BuildShell()
    {
        var navigation = new NavigationView
        {
            PaneTitle = "",
            PaneHeader = BuildPaneLogo(),
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsPaneToggleButtonVisible = false,
            IsTitleBarAutoPaddingEnabled = false,
            IsPaneOpen = true,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            OpenPaneLength = 260,
            CompactModeThresholdWidth = 0,
            ExpandedModeThresholdWidth = 0,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        navigation.Resources["NavigationViewContentMargin"] = new Thickness(0);
        _navigation = navigation;

        navigation.MenuItems.Add(NavItem("Benvenuto", "welcome", Symbol.Home));
        navigation.MenuItems.Add(NavItem("Decky", "decky", Symbol.Download));
        navigation.MenuItems.Add(NavItem("Plugin Store", "plugins", Symbol.Shop));
        navigation.MenuItems.Add(NavItem("Gaming Mode", "gaming", Symbol.Play));
        navigation.MenuItems.Add(NavItem("Importa Giochi", "xbox", VectorIcon(LayerDiagonalAddPath)));
        navigation.MenuItems.Add(NavItem("Big Picture Styler", "styler", ((char)0xE771).ToString()));
        navigation.MenuItems.Add(NavItem("Impostazioni", "settings", Symbol.Setting));
        navigation.MenuItems.Add(NavItem("Supporto", "support", VectorIcon(KoFiIconPath)));
        navigation.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                ShowPage(tag);
            }
        };
        navigation.ItemInvoked += (_, args) =>
        {
            if (_currentPageTag == "plugin-detail" && args.InvokedItemContainer?.Tag is string tag)
                ShowPage(tag);
        };

        _status = new InfoBar
        {
            IsOpen = false,
            Margin = new Thickness(28, 14, 28, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsClosable = true,
            Visibility = Visibility.Collapsed
        };
        _status.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (sender, _) =>
        {
            if (sender is InfoBar infoBar)
            {
                infoBar.Visibility = infoBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            }
        });

        _pageHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        Diag.Step("BuildShell: Decky");
        _pageHost.Children.Add(BuildDeckyPage());
        Diag.Step("BuildShell: Plugins");
        _pageHost.Children.Add(BuildPluginsPage());
        Diag.Step("BuildShell: Gaming");
        _pageHost.Children.Add(BuildGamingPage());
        Diag.Step("BuildShell: Xbox");
        _pageHost.Children.Add(BuildXboxPage());
        Diag.Step("BuildShell: BigPictureStyler");
        _pageHost.Children.Add(BuildBigPictureStylerPage());
        Diag.Step("BuildShell: Settings");
        _pageHost.Children.Add(BuildSettingsPage());
        Diag.Step("BuildShell: Support");
        _pageHost.Children.Add(BuildSupportPage());
        Diag.Step("BuildShell: pages done");

        // Auto-save di tutti i controlli Gaming Mode: agganciato DOPO che tutte le
        // pagine (inclusa Steam Controller) hanno registrato i loro toggle, così
        // anche quelli nuovi vengono salvati come gli altri.
        WireGamingAutoSave();

        _contentScroller = new ScrollViewer
        {
            Content = _pageHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Clicking on empty page area commits and leaves any open text/number field.
        _pageHost.IsTabStop = true;
        _pageHost.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, e) => CommitEditorsOnBackgroundPress(e)), true);


        var content = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent)
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        Grid.SetRow(_status, 0);
        content.Children.Add(_status);
        Grid.SetRow(_pluginStoreToolbar, 1);
        _pluginStoreToolbar.Margin = new Thickness(36, 26, 36, 0);
        content.Children.Add(_pluginStoreToolbar);
        Grid.SetRow(_contentScroller, 2);
        content.Children.Add(_contentScroller);

        navigation.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        content.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
        _contentScroller.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);

        navigation.Content = content;

        var root = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(BuildTitleBar());
        Grid.SetRow(navigation, 1);
        root.Children.Add(navigation);

        BuildWelcomePage(navigation); // sets _welcomeRoot
        _welcomeRoot.Margin = new Thickness(260, 0, 0, 0);
        Grid.SetRow(_welcomeRoot, 0);
        Grid.SetRowSpan(_welcomeRoot, 2);
        root.Children.Add(_welcomeRoot);

        _loadingOverlay = BuildLoadingOverlay();
        Grid.SetRowSpan(_loadingOverlay, 2);
        root.Children.Add(_loadingOverlay);

        var lightbox = BuildMediaLightbox();
        Grid.SetRowSpan(lightbox, 2);
        root.Children.Add(lightbox);

        Content = root;
        SetTitleBar(_titleBar);
        navigation.SelectedItem = navigation.MenuItems[0];
        ShowPage("welcome");
    }

    private Grid BuildLoadingOverlay()
    {
        var overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 22, 22, 26)) };

        var box = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 26
        };

        var logoPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "Brand", "base-logo.png");
        if (System.IO.File.Exists(logoPath))
        {
            box.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(logoPath)),
                Width = 190,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        box.Children.Add(new ProgressRing
        {
            IsActive = true,
            Width = 46,
            Height = 46,
            Foreground = new SolidColorBrush(ParseColor(_settings.AccentColor)),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        overlay.Children.Add(box);
        return overlay;
    }

    private Grid BuildTitleBar()
    {
        _titleBarAccent = new Border
        {
            Width = 3,
            Height = 20,
            CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(ParseColor(_settings.AccentColor)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new Image
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "Playhub Cube Icon.png"))),
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform
        };

        _titleBar = new Grid
        {
            Height = 48,
            Padding = new Thickness(18, 0, 148, 0),
            ColumnSpacing = 12,
            Background = new SolidColorBrush(Colors.Transparent)
        };
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition());
        _titleBar.Children.Add(icon);
        return _titleBar;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (ReferenceEquals(args, _lastWheelArgs))
        {
            return;
        }

        _lastWheelArgs = args;
        var delta = args.GetCurrentPoint(_contentScroller).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        CancelNavigationRestore();
        var target = Math.Max(0, _contentScroller.VerticalOffset - delta);
        _contentScroller.ChangeView(null, target, null, disableAnimation: false);
        args.Handled = true;
    }

    // If the user presses on empty page area (not on an interactive control),
    // move focus off the current editor so its value is committed.
    private void CommitEditorsOnBackgroundPress(PointerRoutedEventArgs args)
    {
        var node = args.OriginalSource as DependencyObject;
        while (node is not null)
        {
            if (node is Microsoft.UI.Xaml.Controls.TextBox
                or Microsoft.UI.Xaml.Controls.NumberBox
                or Microsoft.UI.Xaml.Controls.ComboBox
                or Microsoft.UI.Xaml.Controls.ToggleSwitch
                or Microsoft.UI.Xaml.Controls.Slider
                or Microsoft.UI.Xaml.Controls.Expander
                or Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
            {
                return;
            }

            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }

        try { _pageHost.Focus(FocusState.Programmatic); } catch { }
    }

    private static Image BuildPaneLogo()
    {
        return new Image
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "base-logo.png"))),
            Width = 122,
            Height = 42,
            Stretch = Stretch.Uniform,
            // Left margin aligns the logo with the nav item TEXT, not the icons.
            Margin = new Thickness(50, 18, 18, 14),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private static NavigationViewItem NavItem(string label, string tag, Symbol symbol)
        => MakeNavItem(label, tag, new SymbolIcon(symbol));

    private static NavigationViewItem NavItem(string label, string tag, string glyph)
        => MakeNavItem(label, tag, new FontIcon { Glyph = glyph });

    private static NavigationViewItem NavItem(string label, string tag, IconElement icon)
        => MakeNavItem(label, tag, icon);

    private const string LayerDiagonalAddPath =
        "M12.5 4.25194C12.5 3.73196 11.9837 3.36976 11.4948 3.54669L4.65454 6.02188C3.96161 6.27262 3.5 6.93056 3.5 7.66746V13.7491C3.5 14.2691 4.01625 14.6313 4.5052 14.4544L5 14.2753V15.8705C3.5378 16.388 2 15.3035 2 13.7491V7.66746C2 6.29894 2.85728 5.07704 4.14414 4.61138L10.9844 2.1362C12.4512 1.60541 14 2.69202 14 4.25194V4.42895L12.5 4.97174V4.25194ZM16.5 7.25194C16.5 6.73196 15.9837 6.36976 15.4948 6.54669L8.32467 9.14124C7.82972 9.32034 7.5 9.7903 7.5 10.3167V16.7491C7.5 17.2691 8.01625 17.6313 8.5052 17.4544L9 17.2753V18.8705C7.5378 19.388 6 18.3035 6 16.7491V10.3167C6 9.15868 6.72539 8.12477 7.81427 7.73075L14.9844 5.1362C16.4512 4.60541 18 5.69202 18 7.25194V7.42895L16.5 7.97174V7.25194ZM19.4948 9.54667C19.9837 9.36975 20.5 9.73195 20.5 10.2519V11.3135C21.0335 11.4858 21.5368 11.7253 22 12.0218V10.2519C22 8.692 20.4512 7.60539 18.9844 8.13618L11.4844 10.8501C10.5935 11.1725 10 12.0184 10 12.9658V19.7491C10 21.309 11.5488 22.3956 13.0156 21.8649L13.5231 21.6812C13.1928 21.2884 12.9081 20.856 12.6773 20.3921L12.5052 20.4544C12.0163 20.6313 11.5 20.2691 11.5 19.7491V12.9658C11.5 12.65 11.6978 12.368 11.9948 12.2606L19.4948 9.54667ZM24 17.5C24 14.4624 21.5376 12 18.5 12C15.4624 12 13 14.4624 13 17.5C13 20.5376 15.4624 23 18.5 23C21.5376 23 24 20.5376 24 17.5ZM19.0006 18L19.0011 20.5035C19.0011 20.7797 18.7773 21.0035 18.5011 21.0035C18.225 21.0035 18.0011 20.7797 18.0011 20.5035L18.0006 18H15.4956C15.2197 18 14.9961 17.7762 14.9961 17.5C14.9961 17.2239 15.2197 17 15.4956 17H18.0005L18 14.4993C18 14.2231 18.2239 13.9993 18.5 13.9993C18.7761 13.9993 19 14.2231 19 14.4993L19.0005 17H21.4966C21.7725 17 21.9961 17.2239 21.9961 17.5C21.9961 17.7762 21.7725 18 21.4966 18H19.0006Z";

    private const string KoFiIconPath =
        "M4 5H17V7H19C21.2091 7 23 8.79086 23 11C23 13.2091 21.2091 15 19 15H17.6C16.5 18.4 13.3 21 9.5 21C4.8 21 1 17.2 1 12.5V5H4ZM17 9V13H19C20.1046 13 21 12.1046 21 11C21 9.89543 20.1046 9 19 9H17ZM9 8.4C8.1 7.5 6.6 7.5 5.7 8.4C4.8 9.3 4.8 10.8 5.7 11.7L9 15L12.3 11.7C13.2 10.8 13.2 9.3 12.3 8.4C11.4 7.5 9.9 7.5 9 8.4Z";

    private static IconElement VectorIcon(string pathData)
        => (IconElement)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"" + pathData + "\"/>");

    private static NavigationViewItem MakeNavItem(string label, string tag, IconElement icon)
    {
        var item = new NavigationViewItem { Content = label, Tag = tag, Icon = icon };
        AttachSidebarIconMotion(item, icon);
        return item;
    }

    private void ShowPage(string tag, bool preserveMorph = false)
    {
        var changed = _currentPageTag != tag;
        if (changed)
        {
            CancelPluginSearch();
            SaveNavigationPosition();
            if (tag == "plugins") ResetPluginSourceFilter();
            if (!preserveMorph) CancelPluginCardMorph();
            if (_currentPageTag == "plugin-detail" && tag != "plugin-detail")
            {
                _pluginPagePluginKey = null;
                _pluginPageContent.Children.Clear();
                if (tag != "plugins") _pluginStoreHistory.Clear();
            }
        }
        _currentPageTag = tag;
        if (tag is "plugins" or "plugin-detail" && !Equals(_status.Tag, "update-notification"))
            _status.IsOpen = false;
        var welcome = tag == "welcome";
        _welcomeRoot.Visibility = welcome ? Visibility.Visible : Visibility.Collapsed;
        foreach (var child in _pageHost.Children.OfType<FrameworkElement>())
        {
            child.Visibility = (!welcome && Equals(child.Tag, tag)) ? Visibility.Visible : Visibility.Collapsed;
            if (child.Visibility == Visibility.Visible) LocalizeElement(child);
        }
        if (welcome) LocalizeElement(_welcomeRoot);
        RenderVisiblePluginView();
        if (changed) RestoreNavigationPosition();
        UpdateFeaturedAutoAdvanceState();
        UpdatePluginBackButton();

        if (!_mediaPlaybackReady)
        {
            return;
        }

        if (string.Equals(tag, "xbox", StringComparison.Ordinal) &&
            !_executableScanInProgress &&
            _executableGames.Count == 0 &&
            (_settings.ExecutableGameFolders.Count > 0 || _settings.ExecutableGameFiles.Count > 0))
        {
            _ = ScanExecutableGamesAsync();
        }

        UpdateTutorialPlayback(tag);
    }

    private void UpdateTutorialPlayback(string pageTag)
    {
        // RefreshDeckyStateAsync also calls this method while LoadAsync is still running.
        // Do not let that refresh bypass the startup media gate and create a player
        // underneath the loading overlay.
        if (!_mediaPlaybackReady)
        {
            return;
        }

        foreach (var tutorial in _tutorialVideos)
        {
            var canPlay = string.Equals(tutorial.PageTag, pageTag, StringComparison.Ordinal) &&
                          tutorial.IsInViewport && tutorial.Host.IsLoaded &&
                          (!tutorial.RequiresDeckyInstalled || _deckyQuickAccessCard.Visibility == Visibility.Visible);
            try
            {
                if (canPlay)
                {
                    StartTutorialVideo(tutorial);
                }
                else if (tutorial.Player is not null)
                {
                    tutorial.Player.Pause();
                }
            }
            catch
            {
            }
        }
    }

    private static void StartTutorialVideo(TutorialVideoSession tutorial)
    {
        if (tutorial.Player is null)
        {
            var player = new Windows.Media.Playback.MediaPlayer
            {
                IsMuted = true,
                Volume = 0,
                IsLoopingEnabled = true,
                AutoPlay = false
            };
            try { player.CommandManager.IsEnabled = false; } catch { }

            var element = new MediaPlayerElement
            {
                AreTransportControlsEnabled = false,
                IsHitTestVisible = false,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            element.SetMediaPlayer(player);
            tutorial.Host.Children.Add(element);
            tutorial.Player = player;
        }

        if (tutorial.Player.Source is null)
        {
            tutorial.Player.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(tutorial.VideoPath));
        }
        try { tutorial.Player.CommandManager.IsEnabled = false; } catch { }
        tutorial.Player.Play();
    }

    private void ReleaseMediaForShutdown()
    {
        foreach (var tutorial in _tutorialVideos)
        {
            if (tutorial.Player is null)
            {
                continue;
            }

            try { tutorial.Player.Pause(); } catch { }
            try { tutorial.Player.Source = null; } catch { }
            tutorial.Player = null;
        }
        _tutorialVideos.Clear();

        if (_lightboxPlayer is not null)
        {
            try { _lightboxPlayer.Pause(); } catch { }
            try { _lightboxPlayer.Source = null; } catch { }
            _lightboxPlayer = null;
        }
    }

    private UIElement BuildWelcomePage(NavigationView navigation)
    {
        var slides = new (string Asset, string Title, string Body, bool ShowColor)[]
        {
            ("welcome-onboarding.png", "Benvenuto in Playhub", "Il tuo PC da gioco, con l'anima di una console.", false),
            ("decky-installation-onboarding.png", "Installare Decky è semplice, come dovrebbe essere", "Playhub ti guida passo dopo passo e installa DeckyLoader in modo semplice.", false),
            ("plugin-store-grid-v2.png", "I migliori plugin sono tutti qui", "Scopri, installa, aggiorna, e disinstalla i plugin di Playhub, quelli del Decky Store e i progetti indipendenti pubblicati su GitHub.", false),
            ("gaming-mode-page-header.png", "Il tuo PC è la migliore console mai creata", "Con Playhub Gaming Mode puoi scegliere se avviare il PC in Desktop Mode, la classica esperienza Windows, oppure in Gaming Mode: un'esperienza da console che ottimizza i processi del PC, esclude i processi non necessari per giocare e mette Steam Big Picture al centro di tutto, così puoi dimenticare mouse e tastiera.", false),
            ("import-games-onboarding.png", "Tutti i tuoi giochi, una sola libreria", "Scansiona i giochi di Xbox, Epic e GOG, oppure le tue cartelle, e aggiungili a Steam con il nome e gli artwork corretti.", false),
            ("choose-color-onboarding.png", "Scegli il tuo stile", "Scegli un colore per la tua app Playhub.", true),
            ("final-onboarding.png", "È il momento di giocare come mai prima d'ora", "Scopri, prova, personalizza, gioca e divertiti. Questo è lo spirito di Playhub.", false)
        };
        var index = 0;
        var renderedIndex = 0;
#if PLAYHUB_UI_REVIEW
        var artworkOpened = 0;
#endif
        // Each source stays attached to its own Image for the lifetime of the page.
        var artworkCache = slides.Select(slide =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Welcome", "Mascots", slide.Asset);
            if (!File.Exists(path)) return null;
            var bitmap = new BitmapImage { DecodePixelWidth = 1240 };
#if PLAYHUB_UI_REVIEW
            bitmap.ImageOpened += (_, _) => artworkOpened++;
#endif
            bitmap.UriSource = new Uri(path);
            return bitmap;
        }).ToArray();

        var background = new Grid
        {
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 18, 19, 23), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(255, 29, 27, 22), Offset = 1 }
                }
            }
        };

        var welcomeAccent = BuildAccentPicker(welcome: true);
        welcomeAccent.HorizontalAlignment = HorizontalAlignment.Center;
        var welcomeAppearance = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center };
        welcomeAppearance.Children.Add(welcomeAccent);
        var backdropSelector = new Grid { ColumnSpacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var option in BackdropOptions)
        {
            var button = Button(option.LabelKey, async () =>
            {
                _settings.Backdrop = option.Key;
                ApplyBackdrop();
                ApplyChrome(ParseColor(_settings.AccentColor));
                SelectComboKey(_backdropCombo, option.Key);
                await SaveSettingsSilentlyAsync();
            });
            button.Tag = option.Key;
            button.Height = 40;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            backdropSelector.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(button, _welcomeBackdropButtons.Count);
            backdropSelector.Children.Add(button);
            _welcomeBackdropButtons.Add(button);
        }
        welcomeAppearance.Children.Add(backdropSelector);

        var startButton = Button("Iniziamo!", async () =>
        {
            _settings.WelcomeCompleted = true;
            _settings.WelcomeVersion = CurrentWelcomeVersion;
            await SaveSettingsSilentlyAsync();
            var target = navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, "decky"));
            if (target is not null)
            {
                navigation.SelectedItem = target;
            }
        }, primary: true);
        startButton.HorizontalAlignment = HorizontalAlignment.Center;
        startButton.Name = "WelcomeStartButton";
        startButton.MinWidth = 0;
        startButton.Width = CompactPrimaryActionWidth(startButton);
        startButton.Height = 40;

        var frames = new StackPanel[slides.Length];
        var titles = new TextBlock[slides.Length];
        var bodies = new TextBlock[slides.Length];
        var visuals = new Visual[slides.Length];
        var moving = new bool[slides.Length];
        var motionSettings = new Windows.UI.ViewManagement.UISettings();
        CompositionScopedBatch? transition = null;
        var transitionDeadline = DispatcherQueue.CreateTimer();
        transitionDeadline.IsRepeating = false;
        transitionDeadline.Interval = TimeSpan.FromMilliseconds(300);
        long transitionStarted = 0;
        var motionFailures = 0;
#if PLAYHUB_UI_REVIEW
        var sizeChanges = 0;
        var transitions = 0;
        var lastSubmitMs = 0d;
        var lastCompletionMs = 0d;
        var maxCompletionMs = 0d;
#endif
        // Keep layout and image surfaces intact. Only compositor properties change on navigation.
        for (var i = 0; i < slides.Length; i++)
        {
            var slide = slides[i];
            var frame = frames[i] = new StackPanel
            {
                Spacing = 22,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 760,
                Margin = new Thickness(60, 36, 60, 54),
                IsHitTestVisible = i == index
            };
            frame.Children.Add(new Border
            {
                Width = 680,
                Height = 340,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new Image
                {
                    Source = artworkCache[i],
                    Width = 620,
                    Height = 310,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1),
                    RenderTransform = new ScaleTransform
                    {
                        ScaleX = i == 1 ? 1.3 : i == slides.Length - 1 ? 1.2 : 1,
                        ScaleY = i == 1 ? 1.3 : i == slides.Length - 1 ? 1.2 : 1
                    }
                }
            });
            titles[i] = new TextBlock
            {
                Tag = "noloc", Text = T(slide.Title), FontSize = 34,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White)
            };
            bodies[i] = new TextBlock
            {
                Tag = "noloc", Text = T(slide.Body), FontSize = 16, Opacity = 0.82,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 620, LineHeight = 25,
                Foreground = new SolidColorBrush(Colors.White)
            };
            frame.Children.Add(titles[i]);
            frame.Children.Add(bodies[i]);
            if (slide.ShowColor) frame.Children.Add(welcomeAppearance);
            if (i == slides.Length - 1) frame.Children.Add(startButton);
            ElementCompositionPreview.SetIsTranslationEnabled(frame, true);
            visuals[i] = ElementCompositionPreview.GetElementVisual(frame);
            visuals[i].Opacity = i == index ? 1 : 0;
#if PLAYHUB_UI_REVIEW
            frame.SizeChanged += (_, _) => sizeChanges++;
#endif
        }

        var backgroundVisual = ElementCompositionPreview.GetElementVisual(background);
        var compositor = backgroundVisual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        ScalarKeyFrameAnimation Fade(float target)
        {
            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            animation.InsertKeyFrame(1, target);
            animation.Duration = TimeSpan.FromMilliseconds(240);
            return animation;
        }
        Vector3KeyFrameAnimation Move(float target)
        {
            var animation = compositor.CreateVector3KeyFrameAnimation();
            animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            animation.InsertKeyFrame(1, new Vector3(target, 0, 0), ease);
            animation.Duration = TimeSpan.FromMilliseconds(240);
            return animation;
        }
        var fadeIn = Fade(1);
        var fadeOut = Fade(0);
        var moveIn = Move(0);
        var moveLeft = Move(-24);
        var moveRight = Move(24);

        // ----- dots (clickable) -----
        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 26)
        };
        var dotList = new List<Border>();
        var inactiveDotBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

        // ----- circular arrows -----
        var left = GlyphCircleButton(((char)0xE76B).ToString(), 52);
        left.HorizontalAlignment = HorizontalAlignment.Left;
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(26, 0, 0, 0);
        var right = GlyphCircleButton(((char)0xE76C).ToString(), 52);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        right.VerticalAlignment = VerticalAlignment.Center;
        right.Margin = new Thickness(0, 0, 26, 0);

        void RefreshNavigation()
        {
            for (var i = 0; i < dotList.Count; i++)
            {
                dotList[i].Background = i == index
                    ? ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor))
                    : inactiveDotBrush;
            }

            left.Visibility = index > 0 ? Visibility.Visible : Visibility.Collapsed;
            right.Visibility = index == slides.Length - 1 ? Visibility.Collapsed : Visibility.Visible;
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i].IsHitTestVisible = i == index;
                var view = i == index ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content
                    : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(titles[i], view);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(bodies[i], view);
            }
            foreach (var row in welcomeAccent.Children.OfType<StackPanel>())
            foreach (var button in row.Children.OfType<Button>()) button.IsTabStop = slides[index].ShowColor;
            foreach (var button in _welcomeBackdropButtons) button.IsTabStop = slides[index].ShowColor;
            startButton.IsTabStop = index == slides.Length - 1;
        }

        // Translate cached text only when the language changes, never during navigation.
        _refreshWelcomeSlide = () =>
        {
            for (var i = 0; i < slides.Length; i++)
            {
                titles[i].Text = T(slides[i].Title);
                bodies[i].Text = T(slides[i].Body);
            }
            startButton.Width = CompactPrimaryActionWidth(startButton);
        };

        void SettleWelcome()
        {
            transitionDeadline.Stop();
#if PLAYHUB_UI_REVIEW
            if (transition is not null)
            {
                lastCompletionMs = Stopwatch.GetElapsedTime(transitionStarted).TotalMilliseconds;
                maxCompletionMs = Math.Max(maxCompletionMs, lastCompletionMs);
            }
#endif
            var previous = transition;
            transition = null;
            previous?.Dispose();
            for (var i = 0; i < visuals.Length; i++)
            {
                visuals[i].StopAnimation("Opacity");
                visuals[i].StopAnimation("Translation");
                visuals[i].Opacity = i == index ? 1 : 0;
                visuals[i].Properties.InsertVector3("Translation", Vector3.Zero);
                moving[i] = false;
            }
            backgroundVisual.StopAnimation("Opacity");
            backgroundVisual.Opacity = 1;
            renderedIndex = index;
        }
        // The compositor can delay its completion event after the visual has finished.
        // Bound cleanup independently so a new navigation never inherits stale layers.
        transitionDeadline.Tick += (_, _) => { if (transition is not null) SettleWelcome(); };

        void GoTo(int target)
        {
            if (target < 0 || target >= slides.Length || target == index)
            {
                return;
            }

#if PLAYHUB_UI_REVIEW
            var started = Stopwatch.GetTimestamp();
#endif
            var previousIndex = index;
            var direction = Math.Sign(target - index);
            index = target;
            renderedIndex = target;
            RefreshNavigation();
            if (!_welcomeRoot.IsLoaded || _welcomeRoot.Visibility != Visibility.Visible || !motionSettings.AnimationsEnabled)
            {
                SettleWelcome();
                return;
            }
            try
            {
#if PLAYHUB_UI_REVIEW
                transitions++;
#endif
                var previous = transition;
                transition = null;
                previous?.Dispose();
                transitionDeadline.Stop();
                // Do not stop active properties here: StartingValue samples their current compositor pose.
                if (!moving[index])
                    visuals[index].Properties.InsertVector3("Translation", new Vector3(direction * 60, 0, 0));
                var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                transition = batch;
                transitionStarted = Stopwatch.GetTimestamp();
                batch.Completed += (_, _) =>
                {
                    if (!ReferenceEquals(transition, batch)) return;
                    SettleWelcome();
#if PLAYHUB_UI_REVIEW
                    lastCompletionMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    maxCompletionMs = Math.Max(maxCompletionMs, lastCompletionMs);
#endif
                };
                for (var i = 0; i < visuals.Length; i++)
                {
                    if (i != index && i != previousIndex && !moving[i]) continue;
                    moving[i] = true;
                    visuals[i].StartAnimation("Opacity", i == index ? fadeIn : fadeOut);
                    visuals[i].StartAnimation("Translation", i == index ? moveIn : direction > 0 ? moveLeft : moveRight);
                }
                backgroundVisual.StartAnimation("Opacity", fadeIn);
                batch.End();
                transitionDeadline.Start();
#if PLAYHUB_UI_REVIEW
                lastSubmitMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
            }
            catch
            {
                motionFailures++;
                SettleWelcome();
            }
        }

        for (var i = 0; i < slides.Length; i++)
        {
            var dotIndex = i;
            var inner = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var hit = new Border
            {
                Width = 24,
                Height = 22,
                Background = new SolidColorBrush(Colors.Transparent),
                Child = inner
            };
            hit.Tapped += (_, _) => GoTo(dotIndex);
            dotList.Add(inner);
            dots.Children.Add(hit);
        }

        left.Click += (_, _) => GoTo(index - 1);
        right.Click += (_, _) => GoTo(index + 1);
#if PLAYHUB_UI_REVIEW
        _navigateWelcomeSlide = GoTo;
        _readWelcomeMotionState = () =>
        {
            visuals[index].Properties.TryGetVector3("Translation", out var offset);
            // Composition getters expose base values; these two fields verify the settled endpoint only.
            return (index, renderedIndex, transition is not null, visuals[index].Opacity,
                offset.X, artworkCache.Count(source => source is not null), artworkOpened);
        };
        _readWelcomeMotionDiagnostics = () => (moving.Count(value => value), frames.Length, sizeChanges,
            transitions, motionFailures, lastSubmitMs, lastCompletionMs, maxCompletionMs);
#endif

        _welcomeRoot = new Grid
        {
            Tag = "welcome",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _welcomeRoot.Children.Add(background);
        foreach (var frame in frames) _welcomeRoot.Children.Add(frame);
        _welcomeRoot.Children.Add(dots);
        _welcomeRoot.Children.Add(left);
        _welcomeRoot.Children.Add(right);
        _welcomeRoot.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (_welcomeRoot.Visibility != Visibility.Visible) SettleWelcome();
        });
        _welcomeRoot.Unloaded += (_, _) => SettleWelcome();
        _welcomeRoot.Loaded += (_, _) => SettleWelcome();
        Closed += (_, _) =>
        {
            SettleWelcome();
            fadeIn.Dispose();
            fadeOut.Dispose();
            moveIn.Dispose();
            moveLeft.Dispose();
            moveRight.Dispose();
            ease.Dispose();
        };

        SettleWelcome();
        RefreshNavigation();
        RefreshAccentPicker();
        return _welcomeRoot;
    }

    private UIElement BuildDeckyPage()
    {
        var panel = Page("decky", "Decky", "Pochi passi e i plugin sono pronti in Steam. Ogni passo diventa verde quando è completato.");

        _steamButton = Button("Scarica Steam", async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri("https://store.steampowered.com/about/")); });
        panel.Children.Add(BuildDeckyStep(
            "",
            "Steam",
            "DeckyLoader funziona dentro Steam: serve che Steam sia installato sul PC.",
            _steamButton,
            out _steamTile, out _steamGlyph, out _steamStatus));

        panel.Children.Add(BuildDeckyStep(
            "",
            "Modalità sviluppatore di Windows",
            "Si attiva una volta sola: permette a DeckyLoader di installare i plugin.",
            Button("Apri impostazioni", async () => { await _deckyInstaller.OpenDeveloperSettingsAsync(); }),
            out _devTile, out _devGlyph, out _devStatus));

        _installButton = DeckyOperationButton(_deckyInstaller.IsInstalled() ? "Aggiorna" : "Installa", InstallLatestDeckyBuildAsync, primary: true);
        panel.Children.Add(BuildDeckyStep(
            "",
            "Installa DeckyLoader",
            "Scarico e configuro l'ultima versione di DeckyLoader.",
            ActionRow(
                _installButton,
                DeckyOperationButton("Rimuovi", async () => { SetStatus(await Task.Run(() => _deckyInstaller.RemoveAsync()), InfoBarSeverity.Warning); await RefreshDeckyStateAsync(); })),
            out _installTile, out _installGlyph, out _installStatus));

        var bigPicture = BuildBigPictureTutorialCard();
        _deckyBigPictureCard = bigPicture.Root;
        _deckyBigPictureCard.Visibility = Visibility.Collapsed;
        panel.Children.Add(bigPicture);

        panel.Children.Add(BuildGameBarWarningCard());

        var quickAccess = BuildQuickAccessTutorialCard(
            "decky",
            "Esplora Decky",
            "Aprilo dal menu rapido di Steam con il controller o la tastiera.",
            "");
        _deckyQuickAccessCard = quickAccess.Root;
        _deckyQuickAccessCard.Visibility = Visibility.Collapsed;
        panel.Children.Add(quickAccess);

        var update = Card();
        update.Children.Add(IconHeader(((char)0xE896).ToString(), "Scegli una versione di DeckyLoader",
            "Usa questa opzione solo se ti serve una versione precisa."));
        _deckyBuildCombo = new ComboBox { PlaceholderText = "Scegli una versione", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 0) };
        update.Children.Add(_deckyBuildCombo);
        update.Children.Add(ActionRow(DeckyOperationButton("Installa questa versione", async () => { await InstallSelectedDeckyBuildAsync(); await RefreshDeckyStateAsync(); })));
        panel.Children.Add(update);

        // Variante avanzata: DeckyLoader con console visibile (log in tempo reale).
        var consoleCard = Card();
        consoleCard.Children.Add(IconHeader(((char)0xE756).ToString(), "DeckyLoader con console",
            "Mostra una finestra con il registro in tempo reale. Utile per diagnosi e sviluppo."));
        consoleCard.Children.Add(ActionRow(DeckyOperationButton("Installa la versione con console", async () => { SetStatus(await _deckyInstaller.InstallLatestConsoleAsync(), InfoBarSeverity.Success); await RefreshDeckyStateAsync(); })));
        panel.Children.Add(consoleCard);

        return panel;
    }

    // A horizontal step card: accent icon tile · title + subtitle · status text over the action.
    private UIElement BuildDeckyStep(string glyph, string title, string subtitle, UIElement action,
        out Border iconTile, out FontIcon glyphIcon, out TextBlock statusText)
    {
        var accent = ParseColor(_settings.AccentColor);

        glyphIcon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 22,
            Foreground = new SolidColorBrush(accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        iconTile = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(WithAlpha(accent, 38)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = glyphIcon
        };

        var texts = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(new TextBlock { Text = subtitle, Style = StyleResource("PlayhubBodyTextStyle"), TextWrapping = TextWrapping.Wrap });

        statusText = new TextBlock
        {
            Text = "Da fare",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(190, 255, 255, 255))
        };

        // La targhetta di stato ("Da fare"/"Installato"/"Attiva"…) è stata rimossa:
        // lo stato è già evidente dal colore della tile e dal segno di spunta.
        var right = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(action);

        var grid = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(iconTile, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(iconTile);
        grid.Children.Add(texts);
        grid.Children.Add(right);

        var card = Card();
        card.Children.Add(grid);
        return card;
    }

    private async Task RefreshDeckyStateAsync()
    {
        var steamInstalled = UwpHookSteamManager.GetSteamFolder() is not null;
        SetStepState(_steamTile, _steamGlyph, _steamStatus, steamInstalled, "", steamInstalled ? "Installato" : "Non trovato");
        _steamButton.Visibility = steamInstalled ? Visibility.Collapsed : Visibility.Visible;

        var devOn = _deckyInstaller.IsDeveloperModeEnabled();
        var installed = _deckyInstaller.IsInstalled();
        SetStepState(_devTile, _devGlyph, _devStatus, devOn, "", devOn ? "Attiva" : "Da attivare");
        SetStepState(_installTile, _installGlyph, _installStatus, installed, "", installed ? "Installato" : "Non installato");
        RefreshGameBarStep();
        var ready = steamInstalled && devOn && installed;
        _deckyBigPictureCard.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        _deckyQuickAccessCard.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        UpdateTutorialPlayback(_currentPageTag);
        var installLabel = installed ? "Aggiorna" : "Installa";
        _localizationKeys.AddOrUpdate(_installButton, installLabel);
        if (!_deckyOperationRunning) _installButton.Content = T(installLabel);
        await Task.CompletedTask;
    }

    private void SetStepState(Border tile, FontIcon glyph, TextBlock status, bool done, string pendingGlyph, string label)
    {
        _localizationKeys.AddOrUpdate(status, label);
        var accent = ParseColor(_settings.AccentColor);
        var green = Color.FromArgb(255, 56, 176, 96);
        if (done)
        {
            tile.Background = new SolidColorBrush(WithAlpha(green, 42));
            glyph.Glyph = ""; // checkmark
            glyph.Foreground = new SolidColorBrush(green);
            status.Text = T(label);
            status.Foreground = new SolidColorBrush(green);
        }
        else
        {
            tile.Background = new SolidColorBrush(WithAlpha(accent, 38));
            glyph.Glyph = pendingGlyph;
            glyph.Foreground = new SolidColorBrush(accent);
            status.Text = T(label);
            status.Foreground = ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(190, 255, 255, 255));
        }
    }

    private UIElement BuildPluginsPage()
    {
        var panel = PageWithoutHeader("plugins");
        _pluginStoreHomeHost = panel;

        var modeBar = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        modeBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modeBar.ColumnDefinitions.Add(new ColumnDefinition());
        _pluginStoreToolbar = modeBar;
        var localNavigation = new Grid { VerticalAlignment = VerticalAlignment.Top };
        localNavigation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        localNavigation.ColumnDefinitions.Add(new ColumnDefinition());
        localNavigation.Children.Add(BuildPluginBackButton());
        _pluginStoreSwitcher = BuildPluginStoreModeSelector();
        _pluginStoreSwitcher.RenderTransform = _pluginBackSwitcherOffset;
        Grid.SetColumn(_pluginStoreSwitcher, 1);
        localNavigation.Children.Add(_pluginStoreSwitcher);
        modeBar.Children.Add(localNavigation);

        _pluginDiscoverView = new StackPanel
        {
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _pluginFeaturedHost = new Grid
        {
            Height = 380,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _pluginFeaturedHost.SizeChanged += (_, args) =>
        {
            if (_featuredPluginExpanded || args.NewSize.Width <= 0)
            {
                return;
            }

            var height = Math.Clamp(args.NewSize.Width * 0.26, 340, 470);
            _pluginFeaturedHost.Height = height;
            _pluginFeaturedHost.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, height)
            };
        };
        _pluginDiscoverView.Children.Add(_pluginFeaturedHost);

        var storeTools = new Grid
        {
            ColumnSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        _pluginDiscoverTools = storeTools;
        storeTools.ColumnDefinitions.Add(new ColumnDefinition());
        storeTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        storeTools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        storeTools.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        storeTools.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var showAllPlugins = IconButton(((char)0xE8A9).ToString(), "Vedi tutti i plugin", () =>
        {
            if (!_pluginShowAll || _pluginCategoryFilter is not null || !string.IsNullOrWhiteSpace(_pluginSearchBox.Text))
                PushPluginStoreHistory();
            CancelPluginSearch();
            _pluginCategoryFilter = null;
            _pluginShowAll = true;
            _pluginAllSource = "all";
            InvalidatePluginAllViews();
            _suppressPluginSearchRender = true;
            try
            {
                _pluginSearchBox.Text = string.Empty;
            }
            finally
            {
                _suppressPluginSearchRender = false;
            }
            _pluginFeaturedHost.Visibility = Visibility.Collapsed;
            _pluginCardsDirty = true;
            RenderPluginCards();
            UpdatePluginBackButton();
            UpdateFeaturedAutoAdvanceState();
            RestoreNavigationPosition(reset: true);
        });
        _pluginShowAllButton = showAllPlugins;
        showAllPlugins.Height = 40;
        showAllPlugins.MinHeight = 40;
        showAllPlugins.Padding = new Thickness(14, 0, 14, 0);
        showAllPlugins.HorizontalAlignment = HorizontalAlignment.Right;
        showAllPlugins.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(showAllPlugins, 1);
        storeTools.Children.Add(showAllPlugins);
        _pluginSearchBox = new TextBox
        {
            MinWidth = 0,
            MinHeight = 40,
            Height = 40,
            Padding = new Thickness(40, 9, 12, 3),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(58, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(130, 38, 38, 42)),
            CornerRadius = new CornerRadius(6),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 14,
            PlaceholderText = "Cerca plugin e funzioni",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            IsSpellCheckEnabled = false
        };
        _localizationKeys.AddOrUpdate(_pluginSearchBox, "Cerca plugin e funzioni");
        _pluginSearchBox.TextChanged += (_, _) =>
        {
            if (_suppressPluginSearchRender)
            {
                return;
            }

            if (_pluginStoreMode == "manage")
            {
                _pluginManageQuery = _pluginSearchBox.Text;
                _pluginManageCardsCache = null;
                _pluginManageListCache = null;
                _pluginManagementDirty = true;
                SchedulePluginSearch();
                return;
            }

            var hasQuery = !string.IsNullOrWhiteSpace(_pluginSearchBox.Text);
            if (hasQuery)
            {
                _pluginShowAll = false;
            }
            else if (_pluginCategoryFilter is not null)
            {
                _pluginShowAll = true;
            }
            _pluginFeaturedHost.Visibility = hasQuery || _pluginShowAll || _pluginCategoryFilter is not null
                ? Visibility.Collapsed
                : Visibility.Visible;
            _pluginCardsDirty = true;
            SchedulePluginSearch();
        };
        var searchHost = BuildCollapsiblePluginSearch(showAllPlugins);
        Grid.SetColumn(searchHost, 2);
        storeTools.Children.Add(searchHost);
        storeTools.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width > 0 && args.NewSize.Width < 650;
            Grid.SetColumn(showAllPlugins, compact ? 0 : 1);
            Grid.SetColumnSpan(showAllPlugins, compact ? 3 : 1);
            Grid.SetRow(showAllPlugins, 0);
            showAllPlugins.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(searchHost, compact ? 0 : 2);
            Grid.SetColumnSpan(searchHost, compact ? 3 : 1);
            Grid.SetRow(searchHost, compact ? 1 : 0);
            searchHost.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(0);
            UpdatePluginSearchWidth(compact ? args.NewSize.Width : args.NewSize.Width - showAllPlugins.ActualWidth - 10);
        };
        Grid.SetColumn(storeTools, 1);
        modeBar.Children.Add(storeTools);

        _pluginDetailsHost = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };
        _pluginDiscoverView.Children.Add(_pluginDetailsHost);

        _pluginCards = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        _pluginCards.SizeChanged += (_, args) =>
        {
            var columns = GetPluginStoreColumnCount(args.NewSize.Width);
            if (columns == _pluginStoreColumnCount)
            {
                return;
            }

            _pluginStoreColumnCount = columns;
            _pluginAllCardsCache = null;
            _pluginCardsDirty = true;
            if (string.Equals(_pluginStoreMode, "discover", StringComparison.Ordinal))
            {
                DispatcherQueue.TryEnqueue(RenderPluginCardsIfNeeded);
            }
        };
        _pluginDiscoverView.Children.Add(_pluginCards);
        panel.Children.Add(_pluginDiscoverView);

        _pluginManageView = new StackPanel
        {
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        _pluginManageContent = new StackPanel
        {
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _pluginManageContent.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width > 0 && args.NewSize.Width < 820;
            var columns = GetPluginStoreColumnCount(args.NewSize.Width);
            if (compact == _pluginManageCompact && columns == _pluginManageColumnCount)
            {
                return;
            }

            _pluginManageCompact = compact;
            _pluginManageColumnCount = columns;
            _pluginManageCardsCache = null;
            _pluginManagementDirty = true;
            if (string.Equals(_pluginStoreMode, "manage", StringComparison.Ordinal))
            {
                DispatcherQueue.TryEnqueue(RenderPluginManagementIfNeeded);
            }
        };
        _pluginManageView.Children.Add(_pluginManageContent);
        _pluginManageView.Children.Add(BuildPluginRestartCard());
        panel.Children.Add(_pluginManageView);

        SwitchPluginStoreMode("discover", animate: false);
        return panel;
    }

    private Border BuildPluginStoreModeSelector()
    {
        var grid = new Grid { ColumnSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        _pluginDiscoverButton = BuildPluginStoreModeButton(
            ((char)0xE721).ToString(), "Scopri", "discover");
        grid.Children.Add(_pluginDiscoverButton);

        _pluginManageButton = BuildPluginStoreModeButton(
            ((char)0xE8F1).ToString(), "Gestisci", "manage");
        Grid.SetColumn(_pluginManageButton, 1);
        grid.Children.Add(_pluginManageButton);

        return new Border
        {
            Height = 54,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(8),
            Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(220, 28, 28, 32)),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    private Button BuildPluginStoreModeButton(string glyph, string label, string mode)
    {
        var button = new Button
        {
            MinWidth = 150,
            Height = 44,
            Padding = new Thickness(16, 0, 16, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = IconContent(glyph, label)
        };
        button.Click += (_, _) => SwitchPluginStoreMode(mode);
        return button;
    }

    private void SwitchPluginStoreMode(string mode, bool animate = true)
    {
        CancelPluginSearch();
        ResetPluginSourceFilter();
        CancelPluginCardMorph();
        _pluginStoreHistory.Clear();
        var managing = string.Equals(mode, "manage", StringComparison.OrdinalIgnoreCase);
        var modeChanged = !string.Equals(
            _pluginStoreMode,
            managing ? "manage" : "discover",
            StringComparison.Ordinal);
        if (modeChanged) SaveNavigationPosition();
        _pluginStoreMode = managing ? "manage" : "discover";
        _pluginDiscoverView.Visibility = managing ? Visibility.Collapsed : Visibility.Visible;
        _pluginManageView.Visibility = managing ? Visibility.Visible : Visibility.Collapsed;
        _pluginDiscoverTools.Visibility = Visibility.Visible;
        _pluginShowAllButton.Visibility = managing ? Visibility.Collapsed : Visibility.Visible;
        UpdatePluginStoreModeButtons();

        if (managing)
        {
            _suppressPluginSearchRender = true;
            try { _pluginSearchBox.Text = _pluginManageQuery; }
            finally { _suppressPluginSearchRender = false; }
            RenderPluginManagementIfNeeded();
        }
        else
        {
            var resetHome = _pluginShowAll || _pluginCategoryFilter is not null || !string.IsNullOrWhiteSpace(_pluginSearchBox.Text);
            _pluginCategoryFilter = null;
            _pluginShowAll = false;
            if (resetHome) InvalidatePluginAllViews();
            _suppressPluginSearchRender = true;
            try
            {
                _pluginSearchBox.Text = string.Empty;
            }
            finally
            {
                _suppressPluginSearchRender = false;
            }
            _pluginFeaturedHost.Visibility = Visibility.Visible;
            if (resetHome)
            {
                _pluginCardsDirty = true;
            }
            RenderPluginCardsIfNeeded();
        }

        if (animate && modeChanged)
        {
            AnimateStoreEntrance(managing ? _pluginManageView : _pluginDiscoverView, managing ? 18 : -18);
        }
        if (_currentPageTag == "plugins")
            RestoreNavigationPosition(reset: !managing);
        UpdateFeaturedAutoAdvanceState();
        if (_currentPageTag == "plugin-detail") ShowPage("plugins");
        UpdatePluginBackButton();
    }

    private void UpdatePluginStoreModeButtons()
    {
        UpdatePluginStoreModeButton(_pluginDiscoverButton, _pluginStoreMode == "discover");
        UpdatePluginStoreModeButton(_pluginManageButton, _pluginStoreMode == "manage");
    }

    private void UpdatePluginStoreModeButton(Button button, bool selected)
    {
        if (button is null)
        {
            return;
        }

        var accent = ParseColor(_settings.AccentColor);
        var foreground = selected
            ? (NeedsLightForeground(accent) ? Colors.White : Colors.Black)
            : Color.FromArgb(220, 255, 255, 255);
        var background = selected ? accent : Colors.Transparent;
        button.Background = new SolidColorBrush(background);
        button.Foreground = new SolidColorBrush(foreground);
        SetLocalBrush(button, "ButtonBackground", background);
        SetLocalBrush(button, "ButtonBackgroundPointerOver", selected ? Mix(accent, Colors.White, 0.12) : Color.FromArgb(28, 255, 255, 255));
        SetLocalBrush(button, "ButtonBackgroundPressed", selected ? Mix(accent, Colors.Black, 0.12) : Color.FromArgb(42, 255, 255, 255));
        SetLocalBrush(button, "ButtonForeground", foreground);
        SetLocalBrush(button, "ButtonForegroundPointerOver", foreground);
        SetLocalBrush(button, "ButtonForegroundPressed", foreground);
    }

    private static void AnimateStoreEntrance(UIElement element, double fromX)
    {
        if (!MotionEnabled()) { element.Opacity = 1; return; }
        try
        {
            var transform = new TranslateTransform { X = fromX };
            element.RenderTransform = transform;
            element.Opacity = 0;

            var storyboard = new Storyboard();
            var movement = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(movement, transform);
            Storyboard.SetTargetProperty(movement, "X");
            storyboard.Children.Add(movement);

            var fade = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(170))
            };
            Storyboard.SetTarget(fade, element);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
            storyboard.Begin();
        }
        catch
        {
            element.Opacity = 1;
        }
    }

    private void AddStoreCardInteractions(Border card)
    {
        var originalBorder = card.BorderBrush;
        card.PointerEntered += (_, _) =>
        {
            card.BorderBrush = new SolidColorBrush(WithAlpha(ParseColor(_settings.AccentColor), 150));
        };
        card.PointerExited += (_, _) =>
        {
            card.BorderBrush = originalBorder;
        };
    }

    private UIElement BuildDeckyStoreCard()
    {
        return BuildQuickAccessTutorialCard(
            "plugins",
            "Decky Store",
            "Apri lo store di Decky dal Quick Access Menu e scopri altri plugin.",
            "",
            "I plugin dello store di Decky sono sviluppati per Linux, a volte potrebbero non funzionare come previsto su Windows.",
            "Decky-Store.mp4");
    }

    private UIElement BuildGamingPage()
    {
        var panel = Page("gaming", "Gaming Mode", "Apri direttamente Big Picture e controlla il PC dal divano.");

        // Backing value for the default mode (driven by the two tiles below).
        _defaultModeCombo = ChoiceCombo(ModeOptions);

        // ---------- 1. What it is + install ----------
        var manage = Card();
        var installHeading = new Grid { ColumnSpacing = 10 };
        installHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        installHeading.ColumnDefinitions.Add(new ColumnDefinition());
        installHeading.Children.Add(new FontIcon
        {
            Glyph = ((char)0xE896).ToString(), FontSize = 18, VerticalAlignment = VerticalAlignment.Center,
            Foreground = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor))
        });
        var installTitle = new TextBlock
        {
            Text = "Installa Gaming Mode", Style = StyleResource("PlayhubSectionTitleStyle"),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(installTitle, 1);
        installHeading.Children.Add(installTitle);
        var installHeader = new StackPanel { Spacing = 8 };
        installHeader.Children.Add(installHeading);
        installHeader.Children.Add(Body("Installa Gaming Mode e il plugin per DeckyLoader."));
        manage.Children.Add(installHeader);
        var installActions = ActionRow(
            Button("Installa o aggiorna", async () =>
            {
                var result = await _gamingMode.InstallAsync(_settings.DeckyPluginsPath);
                SetStatus(result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }, primary: true),
            Button("Disinstalla", async () =>
            {
                var result = await _gamingMode.UninstallAsync(_settings.DeckyPluginsPath);
                SetStatus(result.Message, result.Success ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
            }));
        installActions.Orientation = Orientation.Vertical;
        foreach (var action in installActions.Children.OfType<Button>())
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
        manage.Children.Add(installActions);
        manage.Children.Add(AdvancedGamingTools());
        var quickAccess = BuildQuickAccessTutorialCard(
            "gaming",
            "Apri il plugin Gaming Mode",
            "Apri Gaming Mode dal menu rapido di Decky, senza lasciare il controller.",
            "",
            warning: "Se il menu rapido non risponde, chiudi Steam per tornare al desktop. Puoi anche tenere premuto Shift durante l'accesso a Windows.",
            videoFile: "Gaming-Mode-Plugin.mp4",
            compact: true);
        manage.Root.VerticalAlignment = VerticalAlignment.Stretch;
        quickAccess.Root.VerticalAlignment = VerticalAlignment.Stretch;
        var gamingTopCards = CardsRow(manage, quickAccess);
        gamingTopCards.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        gamingTopCards.ColumnDefinitions[1].Width = new GridLength(3, GridUnitType.Star);
        panel.Children.Add(gamingTopCards);

        var dashboardCard = Card();
        dashboardCard.Children.Add(IconHeader(((char)0xE80F).ToString(), "Playhub Dashboard",
            "Passa fra giochi e app, controlla il PC e raggiungi gli strumenti essenziali senza lasciare il controller."));
        AddExplainedToggle(dashboardCard, "Attiva Playhub Dashboard",
            "Aprila con una doppia pressione del tasto Home del controller oppure con Ctrl + Alt + P.", "dashboardEnabled");
        var tryDashboard = Button("Prova Playhub Dashboard", async () =>
        {
            await SaveGamingConfigAsync();
            var opened = await _gamingMode.OpenDashboardAsync(_gamingConfig.Safety.ApiPort);
            SetStatus(opened
                ? "Apro la Dashboard in Gaming Mode…"
                : "Non riesco ad aprire la Dashboard. Assicurati che Gaming Mode sia attivo e riprova.",
                opened ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }, primary: true);
        tryDashboard.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (_gamingToggles.TryGetValue("dashboardEnabled", out var dashboardToggle))
        {
            tryDashboard.IsEnabled = dashboardToggle.IsOn;
            dashboardToggle.Toggled += (_, _) => tryDashboard.IsEnabled = dashboardToggle.IsOn;
        }
        dashboardCard.Children.Add(tryDashboard);

        // ---------- 2. Default mode: two big tiles + one-time switch ----------
        var modeCard = Card();
        modeCard.Children.Add(IconHeader(((char)0xE7FC).ToString(), "Modalità predefinita",
            "Scegli cosa vedere quando accendi il PC."));

        var desktopIcons = new List<FontIcon>();
        var desktopIconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        var keyboardIcon = new FontIcon { Glyph = ((char)0xE765).ToString(), FontSize = 42, VerticalAlignment = VerticalAlignment.Center };
        var mouseIcon = new FontIcon { Glyph = ((char)0xE962).ToString(), FontSize = 34, VerticalAlignment = VerticalAlignment.Center };
        desktopIcons.Add(keyboardIcon);
        desktopIcons.Add(mouseIcon);
        desktopIconRow.Children.Add(keyboardIcon);
        desktopIconRow.Children.Add(mouseIcon);
        _desktopModeTile = ModeTileShell(desktopIconRow, "Desktop", "Il desktop di Windows, con mouse e tastiera.");
        _desktopModeTile.Tapped += async (_, _) => await SelectDefaultModeAsync("Desktop");

        var gamingIcons = new List<FontIcon>();
        var gamingIconRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var padIcon = new FontIcon { Glyph = ((char)0xE7FC).ToString(), FontSize = 48, VerticalAlignment = VerticalAlignment.Center };
        gamingIcons.Add(padIcon);
        gamingIconRow.Children.Add(padIcon);
        _gamingModeTile = ModeTileShell(gamingIconRow, "Gaming", "Big Picture a tutto schermo, pronto per il controller.");
        _gamingModeTile.Tapped += async (_, _) => await SelectDefaultModeAsync("Gaming");

        _setDesktopSelected = sel => ApplyModeTileState(_desktopModeTile, desktopIcons, sel);
        _setGamingSelected = sel => ApplyModeTileState(_gamingModeTile, gamingIcons, sel);

        var modeGrid = new Grid { ColumnSpacing = 16, Margin = new Thickness(0, 6, 0, 0) };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition());
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var desktopCol = new StackPanel { Spacing = 12 };
        desktopCol.Children.Add(_desktopModeTile);
        var desktopSwitch = Button("Avvia ora in Desktop", async () =>
        {
            if (!await _gamingMode.SwitchModeAsync("Desktop", _gamingConfig.Safety.ApiPort))
            {
                SetStatus("Gaming Mode non risponde. Avvialo e riprova.", InfoBarSeverity.Warning);
            }
        });
        desktopSwitch.HorizontalAlignment = HorizontalAlignment.Stretch;
        desktopCol.Children.Add(desktopSwitch);
        Grid.SetColumn(desktopCol, 0);

        var gamingCol = new StackPanel { Spacing = 12 };
        gamingCol.Children.Add(_gamingModeTile);
        var gamingSwitch = Button("Avvia ora in Gaming", async () =>
        {
            if (!await _gamingMode.SwitchModeAsync("Gaming", _gamingConfig.Safety.ApiPort))
            {
                SetStatus("Gaming Mode non risponde. Avvialo e riprova.", InfoBarSeverity.Warning);
            }
        });
        gamingSwitch.HorizontalAlignment = HorizontalAlignment.Stretch;
        gamingCol.Children.Add(gamingSwitch);
        Grid.SetColumn(gamingCol, 1);

        modeGrid.Children.Add(desktopCol);
        modeGrid.Children.Add(gamingCol);
        modeCard.Children.Add(modeGrid);
        panel.Children.Add(modeCard);
        panel.Children.Add(dashboardCard);

        // Shared fields (placed into the concept cards below).
        _steamPathBox = TextBox("Cartella di Steam");
        _steamArgsBox = TextBox("Opzioni di avvio di Steam");
        _deckyPathBox = TextBox("Eseguibile di DeckyLoader");
        _sunshinePathBox = TextBox("Cartella dello strumento di streaming");
        _delaySteamBox = Number("Attesa prima di Steam (ms)", 0, 60000);
        _mouseDelayBox = Number("Nascondi il cursore dopo (ms)", 0, 30000);
        _apiPortBox = Number("Porta di comunicazione", 1, 65535);

        // ---------- 3. Avvio ----------
        var startCard = Card();
        startCard.Children.Add(IconHeader(((char)0xE945).ToString(), "Avvio",
            "Scegli cosa deve essere pronto prima di Big Picture."));
        AddExplainedToggle(startCard, "Avvia DeckyLoader prima di Steam",
            "Rende disponibili i plugin appena si apre la libreria.", "deckyRequired");
        AddExplainedToggle(startCard, "Avvia lo streaming",
            "Avvia l'host scelto quando entri in Gaming Mode, così puoi collegarti subito da un altro dispositivo.", "sunshineRequired");

        var advancedStart = new StackPanel { Spacing = 12 };
        advancedStart.Children.Add(TwoColumn(Labeled("Cartella di Steam", BrowseRow(_steamPathBox, folder: true)), Labeled("Opzioni di avvio di Steam", _steamArgsBox)));
        advancedStart.Children.Add(TwoColumn(
            Labeled("Eseguibile di DeckyLoader", BrowseRow(_deckyPathBox, folder: false, exts: new[] { ".exe" })),
            NumberWithHint(_delaySteamBox, "Pausa prima di aprire Steam, per dare tempo a DeckyLoader di caricarsi.")));
        startCard.Children.Add(new Expander
        {
            Header = "Impostazioni avanzate",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = advancedStart
        });

        // ---------- 4. Schermo e desktop ----------
        var screenCard = Card();
        screenCard.Children.Add(IconHeader(((char)0xE7F4).ToString(), "Schermo e desktop",
            "Scegli come si presenta Gaming Mode."));
        AddExplainedToggle(screenCard, "Nascondi il desktop in Gaming Mode",
            "In Gaming Mode il desktop di Windows non viene avviato, per un'esperienza pulita da console. Al ritorno in Desktop Mode viene sempre ripristinato.", "closeExplorer");
        AddExplainedToggle(screenCard, "Finestre senza bordi",
            "Apre i giochi a tutto schermo senza cornici di Windows.", "borderless");
        AddExplainedToggle(screenCard, "Nascondi il cursore",
            "Fa sparire il puntatore del mouse quando giochi con il controller.", "hideMouse");
        screenCard.Children.Add(NumberWithHint(_mouseDelayBox, "Inattività prima di nascondere il cursore."));

        // ---------- 5. Controller, streaming e audio ----------
        var inputCard = Card();
        inputCard.Children.Add(IconHeader(((char)0xE7FC).ToString(), "Controller e streaming",
            "Controller, Game Bar e streaming sulla rete di casa."));
        AddExplainedToggle(inputCard, "Prepara i controller",
            "Applica le impostazioni dei controller quando entri in Gaming Mode.", "inputCompatibility");
        AddExplainedToggle(inputCard, "Prepara lo streaming locale",
            "Configura il sistema per lo streaming dei giochi sulla rete di casa.", "sunshineCompatibility");
        AddExplainedToggle(inputCard, "Xbox Game Bar automatica",
            "Attiva la Xbox Game Bar dal controller nei giochi Xbox e Microsoft Store, poi la rispegne quando esci.", "xboxGameBar");
        inputCard.Children.Add(Labeled(
            "Host per il gioco in remoto",
            BrowseRow(_sunshinePathBox, folder: false, exts: new[] { ".exe" })));

        // ---------- 6. Avanzate ----------
        var advancedCard = Card();
        advancedCard.Children.Add(IconHeader(((char)0xE713).ToString(), "Avanzate",
            "Rete locale, per chi vuole il controllo completo."));
        advancedCard.Children.Add(NumberWithHint(_apiPortBox, "Porta di rete locale con cui Playhub comunica con la Gaming Mode. Cambiala solo se è già occupata."));
        AddExplainedToggle(advancedCard, "Consenti API remote",
            "Permette ad altri dispositivi di comandare la modalità sulla rete locale.", "remoteApi");

        panel.Children.Add(startCard);
        panel.Children.Add(CardsRow(screenCard, inputCard));

        // ---------- Splash logo with live preview ----------
        _splashLogoCombo = ChoiceCombo(SplashLogoOptions);
        _splashLogoBox = TextBox("Percorso logo personalizzato");
        _splashMinBox = Number("Durata minima (ms)", 0, 30000);
        _splashMaxBox = Number("Chiusura automatica dopo (ms)", 1000, 300000);
        _splashLogoCombo.SelectionChanged += (_, _) => UpdateLogoPreview();
        _splashLogoBox.TextChanged += (_, _) => UpdateLogoPreview();

        var splash = Card();

        var splashOptions = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        splashOptions.Children.Add(IconHeader(((char)0xE91B).ToString(), "Schermata di avvio",
            "Il logo mostrato a tutto schermo mentre il PC entra in Gaming Mode."));
        var chooseLogo = new StackPanel { Spacing = 6 };
        chooseLogo.Children.Add(Button("Scegli file…", async () =>
        {
            var file = await PickFileAsync(new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" });
            if (!string.IsNullOrWhiteSpace(file))
            {
                _splashLogoBox.Text = file;
                SelectComboKey(_splashLogoCombo, "custom");
            }
        }));
        chooseLogo.Children.Add(new TextBlock { Text = "PNG, JPG, WebP o BMP", FontSize = 12, Opacity = 0.62 });
        splashOptions.Children.Add(TwoColumn(Labeled("Logo di avvio", _splashLogoCombo), Labeled("Logo personalizzato", chooseLogo)));
        splashOptions.Children.Add(TwoColumn(
            NumberWithHint(_splashMinBox, "Tempo minimo per cui la schermata resta visibile, anche se il gioco è già pronto."),
            NumberWithHint(_splashMaxBox, "Chiude la schermata anche se il gioco non risponde.")));

        _splashLogoPreview = new Image
        {
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform { ScaleX = 0.7, ScaleY = 0.7 } // 30% smaller logo
        };
        var splashPreview = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(255, 15, 15, 19)),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(44, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = _splashLogoPreview
        };
        // Keep the preview frame at 16:9.
        splashPreview.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width > 0) splashPreview.Height = e.NewSize.Width * 9.0 / 16.0;
        };

        var splashGrid = new Grid { ColumnSpacing = 20 };
        splashGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        splashGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(splashOptions, 0);
        Grid.SetColumn(splashPreview, 1);
        splashGrid.Children.Add(splashOptions);
        splashGrid.Children.Add(splashPreview);
        splash.Children.Add(splashGrid);
        panel.Children.Add(splash);

        // ---------- Avanzate (just before custom processes) ----------
        panel.Children.Add(advancedCard);

        // ---------- Custom processes ----------
        var apps = Card();
        apps.Children.Add(IconHeader(((char)0xE710).ToString(), "App all'avvio",
            "Scegli le app da aprire insieme a Gaming Mode."));
        apps.Children.Add(ActionRow(Button("Aggiungi app", async () =>
        {
            var exe = await PickFileAsync(new[] { ".exe" });
            if (string.IsNullOrWhiteSpace(exe))
            {
                return;
            }

            var appName = System.IO.Path.GetFileNameWithoutExtension(exe);
            _gamingConfig.Gaming.CustomStartupApps.Add(new StartupAppConfig
            {
                Name = appName,
                Path = exe,
                ProcessName = appName,
                Enabled = true,
                StartMinimized = true
            });
            RenderStartupApps();
            AutoSaveGaming();
        })));
        _startupAppsPanel = new StackPanel { Spacing = 10 };
        apps.Children.Add(_startupAppsPanel);
        panel.Children.Add(apps);

        UpdateModeTiles();
        UpdateLogoPreview();
        return panel;
    }

    // A text box paired with a "browse" button that opens a folder/file picker.
    private FrameworkElement BrowseRow(TextBox box, bool folder, string[]? exts = null, Action? afterPick = null)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(box, 0);

        var browse = new Button
        {
            Content = new FontIcon { Glyph = ((char)0xE8B7).ToString(), FontSize = 15 },
            Style = StyleResource("PlayhubSecondaryButtonStyle"),
            VerticalAlignment = VerticalAlignment.Bottom
        };
        browse.Click += async (_, _) =>
        {
            var path = folder ? await PickFolderAsync() : await PickFileAsync(exts);
            if (!string.IsNullOrWhiteSpace(path))
            {
                box.Text = path;
                afterPick?.Invoke();
            }
        };
        Grid.SetColumn(browse, 1);

        grid.Children.Add(box);
        grid.Children.Add(browse);
        return grid;
    }

    private async Task<string?> PickFolderAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> PickFileAsync(string[]? exts)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            if (exts is { Length: > 0 })
            {
                foreach (var ext in exts)
                {
                    picker.FileTypeFilter.Add(ext);
                }
            }
            else
            {
                picker.FileTypeFilter.Add("*");
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch
        {
            return null;
        }
    }

    private StackPanel IconHeader(string glyph, string title, string subtitle)
    {
        var header = new StackPanel { Spacing = 8 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Center,
            // Shared accent brush (mutates in place) so icons follow the accent live.
            Foreground = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor))
        });
        row.Children.Add(LocalizedText(new TextBlock { Text = title, Style = StyleResource("PlayhubSectionTitleStyle"), VerticalAlignment = VerticalAlignment.Center }, title));
        header.Children.Add(row);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            header.Children.Add(Body(subtitle));
        }
        return header;
    }

    // Intestazione con una freccia in alto a destra per comprimere/espandere il
    // pannello dei giochi della card (utile dopo la scansione).
    private Button AddCollapsibleHeader(FluentCard card, FrameworkElement header, Func<StackPanel?> panel)
    {
        var chevron = new FontIcon { Glyph = ((char)0xE70E).ToString(), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var toggle = new Button
        {
            Content = chevron,
            Style = StyleResource("PlayhubSecondaryButtonStyle"),
            MinWidth = 0,
            Width = 40,
            Height = 34,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed // compare solo dopo la scansione
        };
        toggle.Click += (_, _) =>
        {
            var p = panel();
            if (p is null)
            {
                return;
            }

            if (p.Visibility == Visibility.Visible)
            {
                p.Visibility = Visibility.Collapsed;
                chevron.Glyph = ((char)0xE70D).ToString();
            }
            else
            {
                p.Visibility = Visibility.Visible;
                chevron.Glyph = ((char)0xE70E).ToString();
            }
        };

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(header, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(header);
        grid.Children.Add(toggle);
        card.Children.Add(grid);
        return toggle;
    }

    // Come IconHeader, ma con un logo PNG (Assets\ServiceLogos\<file>) al posto della glifo.
    private Grid ImageHeader(string logoFile, string title, string subtitle)
    {
        var header = new Grid { ColumnSpacing = 14, Tag = "import-store-header" };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var copy = new StackPanel { Spacing = 8 };
        var logo = new Image
        {
            Height = 54,
            MaxWidth = 100,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5)
        };
        try
        {
            logo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "ServiceLogos", logoFile)));
        }
        catch
        {
        }
        header.Children.Add(logo);
        copy.Children.Add(new TextBlock { Text = title, Style = StyleResource("PlayhubSectionTitleStyle"), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            copy.Children.Add(Body(subtitle));
        }
        copy.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Height > 0) logo.Height = args.NewSize.Height;
        };
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        return header;
    }

    private FluentCard BuildQuickAccessTutorialCard(
        string pageTag,
        string title,
        string subtitle,
        string finalStep,
        string warning = "",
        string videoFile = "DeckyLoader-QAM.mp4",
        bool compact = false)
    {
        var card = Card();
        var text = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        var headerGlyph = pageTag == "plugins" ? ((char)0xE719).ToString() : ((char)0xE7FC).ToString();
        var header = IconHeader(headerGlyph, title, subtitle);
        if (compact) card.Children.Add(header);
        else text.Children.Add(header);
        text.Children.Add(BuildQuickAccessShortcuts());
        if (!string.IsNullOrWhiteSpace(finalStep))
        {
            text.Children.Add(Body(finalStep));
        }

        if (!string.IsNullOrWhiteSpace(warning))
        {
            text.Children.Add(BuildYellowWarning(warning));
        }

        var grid = new Grid { ColumnSpacing = 20, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        var video = BuildLoopingTutorialVideo(pageTag, videoFile, requiresDeckyInstalled: pageTag == "decky");
        Grid.SetColumn(video, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(video);
        grid.Children.Add(text);
        if (compact)
        {
            grid.RowSpacing = 14;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.SizeChanged += (_, args) =>
            {
                var stacked = args.NewSize.Width < 560;
                grid.ColumnDefinitions[0].Width = new GridLength(stacked ? 1 : 2, GridUnitType.Star);
                grid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
                Grid.SetRow(text, stacked ? 1 : 0);
                Grid.SetColumn(text, stacked ? 0 : 1);
            };
        }
        card.Children.Add(grid);
        return card;
    }

    private static Border BuildYellowWarning(string warning)
    {
        var noticeContent = new Grid { ColumnSpacing = 10 };
        noticeContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        noticeContent.ColumnDefinitions.Add(new ColumnDefinition());
        var noticeIcon = new FontIcon
        {
            Glyph = ((char)0xE7BA).ToString(),
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 203, 15)),
            VerticalAlignment = VerticalAlignment.Top
        };
        var noticeText = new TextBlock
        {
            Text = warning,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 19,
            Opacity = 0.9
        };
        Grid.SetColumn(noticeIcon, 0);
        Grid.SetColumn(noticeText, 1);
        noticeContent.Children.Add(noticeIcon);
        noticeContent.Children.Add(noticeText);
        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 203, 15)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(72, 255, 203, 15)),
            BorderThickness = new Thickness(1),
            Child = noticeContent
        };
    }

    private UIElement BuildGameBarWarningCard()
    {
        // Stessa struttura degli step DeckyLoader: quando l'apertura della Game Bar
        // dal controller è disattivata (lo stato che vogliamo), la card diventa
        // "completata" con la spunta verde. RefreshGameBarStep() aggiorna lo stato.
        _gameBarButton = Button("Apri impostazioni", async () =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:gaming-gamebar")), primary: false);

        var card = BuildDeckyStep(
            ((char)0xE7FC).ToString(),
            "Apertura della Game Bar dal controller disattivata",
            "Il tasto del controller non deve aprire la Game Bar: in Big Picture crea problemi di navigazione. Ci pensa Playhub ad attivarla solo per i giochi Xbox.",
            ActionRow(_gameBarButton),
            out _gameBarTile, out _gameBarGlyph, out _gameBarStatus);

        RefreshGameBarStep();
        return card;
    }

    // Legge l'impostazione Windows "apri Game Bar dal controller".
    private static bool IsGameBarControllerEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            return key?.GetValue("UseNexusForGameBarEnabled") is int value && value != 0;
        }
        catch
        {
            return false;
        }
    }

    // Aggiorna la card Game Bar: "completata" (spunta verde) quando l'apertura dal
    // controller è disattivata; altrimenti mostra il pulsante per aprirne le impostazioni.
    private void RefreshGameBarStep()
    {
        var disabled = !IsGameBarControllerEnabled();
        SetStepState(_gameBarTile, _gameBarGlyph, _gameBarStatus, disabled, ((char)0xE7FC).ToString(),
            disabled ? "Disattivata" : "Attiva");
        _gameBarButton.Visibility = disabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private FluentCard BuildBigPictureTutorialCard()
    {
        var card = Card();
        var grid = new Grid
        {
            ColumnSpacing = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var text = new StackPanel
        {
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Children.Add(IconHeader(
            ((char)0xE7F4).ToString(),
            "Iniziamo!",
            "Apri Steam e clicca su Modalità Big Picture."));
        text.Children.Add(ActionRow(Button("Apri Steam", async () =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri("steam://open/main")), primary: true)));

        var imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Tutorials", "Big Picture Mode tutorial.png");
        var imageStage = new Border
        {
            Width = 260,
            MaxHeight = 150,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(255, 14, 14, 16)),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (File.Exists(imagePath))
        {
            imageStage.Child = new Image
            {
                Source = new BitmapImage(new Uri(imagePath)),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        Grid.SetColumn(imageStage, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(imageStage);
        grid.Children.Add(text);
        card.Children.Add(grid);
        return card;
    }

    private Grid BuildQuickAccessShortcuts()
    {
        var grid = new Grid { ColumnSpacing = 18, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var shortcuts = new[]
        {
            ("Xbox", "Home + A", ((char)0xE7FC).ToString()),
            ("PlayStation", "PS + X", ((char)0xE7FC).ToString()),
            ("Nintendo", "Home + B", ((char)0xE7FC).ToString()),
            ("Tastiera", "CTRL + 2", ((char)0xE765).ToString())
        };
        for (var i = 0; i < shortcuts.Length; i++)
        {
            var shortcut = shortcuts[i];
            var row = new Grid { ColumnSpacing = 9 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new FontIcon
            {
                Glyph = shortcut.Item3,
                FontSize = 15,
                Foreground = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor)),
                VerticalAlignment = VerticalAlignment.Center
            });

            var labels = new StackPanel { Spacing = 1 };
            labels.Children.Add(new TextBlock { Text = shortcut.Item1, FontSize = 11.5, Opacity = 0.62 });
            labels.Children.Add(new TextBlock
            {
                Text = shortcut.Item2,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);
            Grid.SetColumn(row, i % 2);
            Grid.SetRow(row, i / 2);
            grid.Children.Add(row);
        }

        return grid;
    }

    private FrameworkElement BuildLoopingTutorialVideo(string pageTag, string videoFile, bool requiresDeckyInstalled)
    {
        var stage = new Grid
        {
            Height = 270,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(255, 14, 14, 16))
        };
        stage.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width <= 0)
            {
                return;
            }

            var targetHeight = args.NewSize.Width * 9.0 / 16.0;
            if (Math.Abs(stage.Height - targetHeight) > 0.5)
            {
                stage.Height = targetHeight;
            }
        };
        var videoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Tutorials", videoFile);
        var tutorial = new TutorialVideoSession(pageTag, requiresDeckyInstalled, videoPath, stage);
        _tutorialVideos.Add(tutorial);
        stage.EffectiveViewportChanged += (_, args) =>
        {
            var viewport = args.EffectiveViewport;
            var visible = Math.Min(stage.ActualWidth, viewport.Right) > Math.Max(0, viewport.Left) &&
                          Math.Min(stage.ActualHeight, viewport.Bottom) > Math.Max(0, viewport.Top);
            if (tutorial.IsInViewport == visible) return;
            tutorial.IsInViewport = visible;
            UpdateTutorialPlayback(_currentPageTag);
        };
        stage.Unloaded += (_, _) =>
        {
            tutorial.IsInViewport = false;
            try { tutorial.Player?.Pause(); } catch { }
        };

        return new Border
        {
            CornerRadius = new CornerRadius(0),
            Background = new SolidColorBrush(Color.FromArgb(255, 14, 14, 16)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stage
        };
    }

    private Border ModeTileShell(UIElement icon, string title, string subtitle)
    {
        var content = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        var iconHost = new Grid { Height = 56 };
        iconHost.Children.Add(icon);
        content.Children.Add(iconHost);
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 19,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Colors.White),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            MinHeight = 46
        });
        content.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12.5,
            Opacity = 0.72,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 250,
            MinHeight = 38
        });

        return new Border
        {
            Height = 220,
            Padding = new Thickness(20, 22, 20, 22),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content
        };
    }

    private void ApplyModeTileState(Border tile, List<FontIcon> icons, bool selected)
    {
        var accent = ParseColor(_settings.AccentColor);
        if (selected)
        {
            tile.BorderBrush = new SolidColorBrush(accent);
            tile.Background = new SolidColorBrush(WithAlpha(accent, 38));
            foreach (var icon in icons)
            {
                icon.Foreground = new SolidColorBrush(accent);
            }
        }
        else
        {
            tile.BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
            tile.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            foreach (var icon in icons)
            {
                icon.Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));
            }
        }
    }

    private void UpdateModeTiles()
    {
        var gaming = string.Equals(GetComboKey(_defaultModeCombo), "Gaming", StringComparison.OrdinalIgnoreCase);
        _setDesktopSelected?.Invoke(!gaming);
        _setGamingSelected?.Invoke(gaming);
    }

    private async Task SelectDefaultModeAsync(string mode)
    {
        SelectComboKey(_defaultModeCombo, mode);
        UpdateModeTiles();
        // Come il plugin: comunica la predefinita all'agente; salva anche in
        // config così resta coerente anche se l'agente non è in esecuzione.
        await _gamingMode.SetDefaultModeViaAgentAsync(mode, _gamingConfig.Safety.ApiPort);
        await SaveGamingConfigAsync();
    }

    private void UpdateLogoPreview()
    {
        try
        {
            var path = ResolveSplashLogo();
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                _splashLogoPreview.Source = new BitmapImage(new Uri(path));
            }
            else
            {
                _splashLogoPreview.Source = null;
            }
        }
        catch
        {
            _splashLogoPreview.Source = null;
        }
    }

    private Expander AdvancedGamingTools()
    {
        var tools = new StackPanel { Spacing = 10 };
        tools.Children.Add(Body("Strumenti per diagnosi e sviluppo."));
        var actions = ActionRow(
            Button("Avvia servizio", () => { _gamingMode.StartAgent(); SetStatus("Servizio avviato.", InfoBarSeverity.Informational); }),
            Button("Controlla servizio", async () => SetStatus(await _gamingMode.IsAgentHealthyAsync(_gamingConfig.Safety.ApiPort) ? "Servizio attivo." : "Servizio non raggiungibile.", InfoBarSeverity.Informational)));
        actions.Orientation = Orientation.Vertical;
        foreach (var action in actions.Children.OfType<Button>())
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
        tools.Children.Add(actions);

        return new Expander
        {
            Header = "Avanzate",
            Content = tools,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }


    private UIElement BuildXboxPage()
    {
        var panel = Page("xbox", "Importa Giochi", "Riunisci i tuoi giochi in Steam e completa automaticamente copertine, sfondi e loghi.");

        var import = Card();
        _uwpChevron = AddCollapsibleHeader(import, ImageHeader("Xbox.png", "Importa giochi Xbox e Microsoft Store",
            "Trova i giochi Xbox, Game Pass e Microsoft Store installati e aggiungili a Steam."), () => _uwpGamesPanel);
        import.Children.Add(ActionRow(
            Button("Scansiona", async () => await ScanUwpGamesAsync()),
            Button("Importa in Steam", async () => await ExportUwpGamesAsync(), primary: true),
            Button("Ricollega giochi", async () => await RelinkUwpGamesAsync()),
            Button("Riavvia Steam", async () => { await _steam.RestartSteamAsync(); SetStatus("Steam riavviato.", InfoBarSeverity.Success); })));

        _uwpGamesPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        _uwpGamesPanel.SizeChanged += (_, args) =>
        {
            if (_uwpGames.Count == 0)
            {
                return;
            }

            var columns = GetUwpCardColumnCount(args.NewSize.Width);
            if (columns != _uwpCardColumnCount)
            {
                _uwpCardColumnCount = columns;
                DispatcherQueue.TryEnqueue(RenderUwpGames);
            }
        };
        import.Children.Add(_uwpGamesPanel);
        panel.Children.Add(import);

        // ---------- Epic Games Store ----------
        var epicImport = Card();
        _epicChevron = AddCollapsibleHeader(epicImport, ImageHeader("Epic.png", "Importa giochi da Epic Games Store",
            "Trova i giochi installati con Epic Games Launcher e aggiungili a Steam."), () => _epicGamesPanel);
        epicImport.Children.Add(ActionRow(
            Button("Scansiona", async () => await ScanEpicGamesAsync()),
            Button("Importa in Steam", async () => await ExportEpicGamesAsync(), primary: true),
            Button("Riavvia Steam", async () => { await _steam.RestartSteamAsync(); SetStatus("Steam riavviato.", InfoBarSeverity.Success); })));
        _epicGamesPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        _epicGamesPanel.SizeChanged += (_, args) =>
        {
            if (_epicGames.Count == 0)
            {
                return;
            }

            var columns = GetUwpCardColumnCount(args.NewSize.Width);
            if (columns != _epicCardColumnCount)
            {
                _epicCardColumnCount = columns;
                DispatcherQueue.TryEnqueue(RenderEpicGames);
            }
        };
        epicImport.Children.Add(_epicGamesPanel);
        panel.Children.Add(epicImport);

        // ---------- GOG ----------
        var gogImport = Card();
        _gogChevron = AddCollapsibleHeader(gogImport, ImageHeader("Gog.png", "Importa giochi da GOG",
            "Trova i giochi GOG installati con Galaxy o da un installer offline e aggiungili a Steam."), () => _gogGamesPanel);
        gogImport.Children.Add(ActionRow(
            Button("Scansiona", async () => await ScanGogGamesAsync()),
            Button("Importa in Steam", async () => await ExportGogGamesAsync(), primary: true),
            Button("Riavvia Steam", async () => { await _steam.RestartSteamAsync(); SetStatus("Steam riavviato.", InfoBarSeverity.Success); })));
        _gogGamesPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        _gogGamesPanel.SizeChanged += (_, args) =>
        {
            if (_gogGames.Count == 0)
            {
                return;
            }

            var columns = GetUwpCardColumnCount(args.NewSize.Width);
            if (columns != _gogCardColumnCount)
            {
                _gogCardColumnCount = columns;
                DispatcherQueue.TryEnqueue(RenderGogGames);
            }
        };
        gogImport.Children.Add(_gogGamesPanel);
        panel.Children.Add(gogImport);

        var executableImport = Card();
        _executableChevron = AddCollapsibleHeader(executableImport, IconHeader(((char)0xE8B7).ToString(), "Aggiungi giochi e app dal PC",
            "Scegli una cartella o un file .exe. Playhub trova i giochi nelle sottocartelle e li prepara per Steam."), () => _executableGamesPanel);
        executableImport.Children.Add(ActionRow(
            Button("Aggiungi cartella", async () => await ChooseExecutableFolderAsync()),
            Button("Aggiungi file", async () => await ChooseExecutableFileAsync()),
            Button("Scansiona", async () => await ScanExecutableGamesAsync()),
            Button("Importa in Steam", async () => await ExportExecutableGamesAsync(), primary: true),
            Button("Riavvia Steam", async () => { await _steam.RestartSteamAsync(); SetStatus("Steam riavviato.", InfoBarSeverity.Success); })));
        executableImport.Children.Add(Body("Non trovi il gioco che cerchi? Aggiungilo direttamente con il pulsante \"Aggiungi file\"."));

        _executableSourcesPanel = new StackPanel { Spacing = 6 };
        executableImport.Children.Add(_executableSourcesPanel);

        _executableGamesPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        _executableGamesPanel.SizeChanged += (_, args) =>
        {
            if (_executableGames.Count == 0)
            {
                return;
            }

            var columns = GetUwpCardColumnCount(args.NewSize.Width);
            if (columns != _executableCardColumnCount)
            {
                _executableCardColumnCount = columns;
                DispatcherQueue.TryEnqueue(RenderExecutableGames);
            }
        };
        executableImport.Children.Add(_executableGamesPanel);
        panel.Children.Add(executableImport);

        var artwork = Card();
        artwork.Children.Add(IconHeader(((char)0xE91B).ToString(), "Artwork dei giochi",
            "Aggiungi la chiave API di SteamGridDB per scaricare automaticamente copertine, sfondi e loghi."));
        _xboxSteamGridDbKeyBox = new PasswordBox
        {
            PlaceholderText = "Chiave API SteamGridDB",
            MinWidth = 220,
            PasswordRevealMode = PasswordRevealMode.Hidden
        };
        _xboxSteamGridDbKeyBox.PasswordChanged += async (_, _) =>
        {
            if (_loadingSettings) return;
            _settings.SteamGridDbApiKey = _xboxSteamGridDbKeyBox.Password;
            await SaveSettingsSilentlyAsync();
        };

        var apiRow = new Grid { ColumnSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        apiRow.ColumnDefinitions.Add(new ColumnDefinition());
        apiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        apiRow.Children.Add(_xboxSteamGridDbKeyBox);
        var apiButton = Button("Ottieni la chiave", async () =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.steamgriddb.com/profile/preferences/api")), primary: true);
        apiButton.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(apiButton, 1);
        apiRow.Children.Add(apiButton);
        artwork.Children.Add(apiRow);

        artwork.Children.Add(new TextBlock
        {
            Text = "Non hai ancora un account SteamGridDB?",
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = 0.72
        });
        artwork.Children.Add(ActionRow(Button("Crea account", async () =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.steamgriddb.com/register")))));
        panel.Children.Add(artwork);
        return panel;
    }

    private UIElement BuildPluginRestartCard()
    {
        var accent = ParseColor(_settings.AccentColor);
        var card = Card();
        card.Root.Background = new SolidColorBrush(WithAlpha(accent, 38));
        card.Root.BorderBrush = new SolidColorBrush(WithAlpha(accent, 145));
        card.Children.Add(IconHeader(
            ((char)0xE7B8).ToString(),
            "Applica le modifiche",
            "Riavvia Steam e DeckyLoader per rendere subito disponibili i plugin."));
        card.Children.Add(ActionRow(Button("Riavvia ora", async () =>
        {
            var success = await _deckyInstaller.RestartWithSteamAsync(_steam);
            SetStatus(
                success
                    ? "DeckyLoader e Steam sono stati riavviati."
                    : "Non riesco a riavviare DeckyLoader e Steam. Riprova.",
                success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }, primary: true)));
        return card;
    }

    private UIElement BuildBigPictureStylerPage()
    {
        var panel = Page("styler", "Big Picture Styler", "Dai a Big Picture lo stile di Playhub e proteggi gli artwork della libreria.");

        var css = Card();
        var cssText = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        cssText.Children.Add(IconHeader(((char)0xE790).ToString(), "Tema Playhub per CSS Loader",
            "Installa il profilo Playhub e porta lo stesso stile in tutta Big Picture."));
        var cssLoaderRow = new Grid { ColumnSpacing = 12 };
        cssLoaderRow.ColumnDefinitions.Add(new ColumnDefinition());
        cssLoaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _cssLoaderStatusText = new TextBlock
        {
            Tag = "noloc",
            Text = "Controllo CSS Loader…",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        _cssLoaderInstallButton = Button("Installa CSS Loader", InstallCssLoaderAsync, primary: true);
        _cssLoaderRemoveButton = Button("Disinstalla CSS Loader", UninstallCssLoaderAsync);
        var cssLoaderActions = ActionRow(_cssLoaderInstallButton, _cssLoaderRemoveButton);
        cssLoaderRow.Children.Add(_cssLoaderStatusText);
        Grid.SetColumn(cssLoaderActions, 1);
        cssLoaderRow.Children.Add(cssLoaderActions);
        cssText.Children.Add(new Border
        {
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(36, 255, 255, 255)),
            Background = ResourceBrush("SubtleFillColorSecondaryBrush", Color.FromArgb(20, 255, 255, 255)),
            Child = cssLoaderRow
        });
        _cssLoaderInstallBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        cssText.Children.Add(_cssLoaderInstallBar);
        _cssProfileInstallButton = Button("Installa profilo", async () =>
            SetStatus(await _extra.ApplyCssLoaderProfileAsync(_settings.CssLoaderProfileUrl), InfoBarSeverity.Success), primary: true);
        cssText.Children.Add(ActionRow(
            _cssProfileInstallButton,
            Button("Rimuovi profilo", async () => SetStatus(await _extra.RemoveCssLoaderProfileAsync(), InfoBarSeverity.Warning))));

        var cssPreviewPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "Extra", "css-theme-preview.png");
        if (System.IO.File.Exists(cssPreviewPath))
        {
            var cssGrid = new Grid { ColumnSpacing = 20 };
            cssGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            cssGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            var preview = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(255, 14, 14, 16)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Image
                {
                    Source = new BitmapImage(new Uri(cssPreviewPath)),
                    Stretch = Stretch.Uniform
                }
            };
            Grid.SetColumn(preview, 0);
            Grid.SetColumn(cssText, 1);
            cssGrid.Children.Add(preview);
            cssGrid.Children.Add(cssText);
            css.Children.Add(cssGrid);
        }
        else
        {
            css.Children.Add(cssText);
        }
        panel.Children.Add(css);
        RefreshCssLoaderState();

        var steam = Card();
        steam.Children.Add(IconHeader(((char)0xE72E).ToString(), "Aggiornamenti di Steam",
            "Mantieni la versione attuale di Steam. Puoi riattivare gli aggiornamenti in qualsiasi momento."));
        steam.Children.Add(ActionRow(
            Button("Blocca aggiornamenti", async () => SetStatus(await _extra.ApplySteamCfgAsync(), InfoBarSeverity.Success)),
            Button("Rimuovi blocco", async () => SetStatus(await _extra.RemoveSteamCfgAsync(), InfoBarSeverity.Warning))));
        panel.Children.Add(steam);

        var artworkBackup = Card();
        artworkBackup.Children.Add(IconHeader(((char)0xE74E).ToString(), "Backup degli artwork di Steam",
            "Salva o ripristina le immagini della tua libreria Steam."));
        artworkBackup.Children.Add(ActionRow(
            Button("Crea backup", async () => SetStatus(await _extra.BackupSteamArtworkAsync(), InfoBarSeverity.Success)),
            Button("Ripristina backup", async () => SetStatus(await _extra.RestoreLatestSteamArtworkAsync(), InfoBarSeverity.Warning))));
        panel.Children.Add(artworkBackup);

        return panel;
    }

    private void RefreshCssLoaderState()
    {
        if (_cssLoaderStatusText is null || _cssLoaderInstallButton is null || _cssLoaderRemoveButton is null) return;
        var status = _cssLoaderInstaller.GetStatus(_settings.DeckyPluginsPath);
        _cssLoaderStatusText.Text = status.Installed
            ? $"CSS Loader {status.Version} è pronto. Ora puoi applicare il profilo Playhub."
            : "Installa CSS Loader per usare il profilo Playhub.";
        _cssLoaderInstallButton.Visibility = status.Installed ? Visibility.Collapsed : Visibility.Visible;
        _cssLoaderRemoveButton.Visibility = status.Installed ? Visibility.Visible : Visibility.Collapsed;
        _cssLoaderInstallButton.IsEnabled = !_cssLoaderInstallBusy && !status.Installed;
        _cssLoaderRemoveButton.IsEnabled = !_cssLoaderInstallBusy && status.Installed;
        _cssProfileInstallButton.IsEnabled = !_cssLoaderInstallBusy && status.Installed;
    }

    private async Task InstallCssLoaderAsync()
    {
        if (_cssLoaderInstallBusy) return;
        _cssLoaderInstallBusy = true;
        _cssLoaderInstallButton.IsEnabled = false;
        _cssProfileInstallButton.IsEnabled = false;
        _cssLoaderInstallBar.Visibility = Visibility.Visible;
        _cssLoaderInstallBar.IsIndeterminate = false;
        _cssLoaderInstallBar.Value = 0;
        try
        {
            var progress = new Progress<(double Percent, string Status)>(value =>
            {
                _cssLoaderInstallBar.Value = Math.Clamp(value.Percent, 0, 1);
                _cssLoaderStatusText.Text = value.Status;
            });
            var result = await _cssLoaderInstaller.InstallLatestAsync(_settings.DeckyPluginsPath, progress);
            SetStatus(result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        finally
        {
            _cssLoaderInstallBusy = false;
            _cssLoaderInstallBar.Visibility = Visibility.Collapsed;
            RefreshCssLoaderState();
        }
    }

    private async Task UninstallCssLoaderAsync()
    {
        if (_cssLoaderInstallBusy) return;
        if (!await ConfirmAsync("Disinstallare CSS Loader?", "CSS Loader verrà rimosso. Il profilo Playhub resterà disponibile per una futura reinstallazione."))
        {
            return;
        }

        _cssLoaderInstallBusy = true;
        try
        {
            var result = await _cssLoaderInstaller.UninstallAsync(_settings.DeckyPluginsPath);
            SetStatus(result.Message, result.Success ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
        }
        finally
        {
            _cssLoaderInstallBusy = false;
            RefreshCssLoaderState();
        }
    }

    private UIElement BuildSupportPage()
    {
        var panel = PageWithoutHeader("support");

        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Support", "Donation.png"))),
            MaxWidth = 760,
            Height = 330,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        });

        var support = Card();
        support.Root.MaxWidth = 820;
        support.Root.HorizontalAlignment = HorizontalAlignment.Center;
        support.Children.Add(IconHeader(((char)0xEB51).ToString(), "Grazie per essere parte di Playhub",
            "Playhub è gratuito e open source. Lo sviluppo, i test e la manutenzione sono sostenuti da una sola persona."));
        support.Children.Add(Body(
            "Se Playhub ti è utile e vuoi aiutare il progetto a continuare a crescere, una donazione è sempre apprezzata. Nessun contenuto è bloccato: è semplicemente un modo gentile per sostenere il lavoro che c'è dietro."));
        support.Children.Add(ActionRow(Button("Fai una donazione", async () =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://ko-fi.com/lozazamastro")), primary: true)));
#if PLAYHUB_UPDATE_PREVIEW
        var testReminder = Button("Test", () => ShowSupportReminderAsync(force: true));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(testReminder, "SupportReminderTest");
        support.Children.Add(ActionRow(testReminder));
#endif
        panel.Children.Add(support);
        return panel;
    }

    private UIElement BuildSettingsPage()
    {
        var panel = Page("settings", "Impostazioni", "Aspetto, avvio e informazioni di Playhub.");

        // ---------- Aspetto ----------
        var appearance = Card();
        appearance.Children.Add(IconHeader(((char)0xE713).ToString(), "Aspetto",
            "Personalizza lo sfondo e il colore di Playhub."));
        _languageCombo = LanguageCombo();
        _languageCombo.SelectionChanged += async (_, _) => await ChangeLanguageAsync();

        _backdropCombo = ChoiceCombo(BackdropOptions);
        _backdropCombo.SelectionChanged += async (_, _) =>
        {
            if (_loadingSettings) return;
            var backdrop = GetComboKey(_backdropCombo) ?? "mica";
            if (backdrop == NormalizeBackdropKey(_settings.Backdrop)) return;
            _settings.Backdrop = backdrop;
            ApplyBackdrop();
            ApplyChrome(ParseColor(_settings.AccentColor));
            await SaveSettingsSilentlyAsync();
        };

        _deckyPluginsBox = TextBox("Cartella plugin DeckyLoader");
        _deckyPluginsBox.TextChanged += async (_, _) =>
        {
            if (_loadingSettings) return;
            _settings.DeckyPluginsPath = _deckyPluginsBox.Text;
            await SaveSettingsSilentlyAsync();
            await RefreshPluginsAsync();
        };

        _accentColorPanel = BuildAccentPicker();
        var languageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        languageRow.Children.Add(_languageCombo);
        languageRow.Children.Add(new TextBlock
        {
            Text = "Playhub verrà riavviato.",
            Opacity = 0.68,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        appearance.Children.Add(TwoColumn(Labeled("Lingua", languageRow), Labeled("Sfondo", _backdropCombo)));
        appearance.Children.Add(Labeled("Colore principale", _accentColorPanel));
        panel.Children.Add(appearance);

        // ---------- Avvio ----------
        var startup = Card();
        startup.Children.Add(IconHeader(((char)0xE80F).ToString(), "Avvio",
            "La pagina su cui si apre Playhub ogni volta che lo avvii."));
        _startupPageCombo = ChoiceCombo(StartupPageOptions);
        _startupPageCombo.SelectionChanged += async (_, _) =>
        {
            if (_loadingSettings) return;
            _settings.StartupPage = GetComboKey(_startupPageCombo) ?? "decky";
            await SaveSettingsSilentlyAsync();
        };
        startup.Children.Add(Labeled("Pagina di avvio", _startupPageCombo));
        panel.Children.Add(startup);

        // ---------- Aggiornamenti Playhub ----------
        var updates = Card();
        updates.Children.Add(IconHeader(((char)0xE895).ToString(), "Aggiorna Playhub",
            "Installa l'ultima versione senza uscire dall'app."));
        _playhubUpdateButton = Button("Cerca aggiornamenti", CheckPlayhubUpdatesAsync, primary: true);
        _playhubUpdateBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _playhubUpdateStatus = new TextBlock
        {
            Tag = "noloc",
            Style = StyleResource("PlayhubBodyTextStyle"),
            Visibility = Visibility.Collapsed
        };
        updates.Children.Add(ActionRow(_playhubUpdateButton));
        updates.Children.Add(_playhubUpdateBar);
        updates.Children.Add(_playhubUpdateStatus);
        panel.Children.Add(updates);

        // ---------- Risoluzione problemi ----------
        var repair = Card();
        repair.Children.Add(IconHeader(((char)0xE90F).ToString(), "Risoluzione problemi",
            "Controlla Gaming Mode, DeckyLoader e l'importazione dei giochi e ripristina ciò che non funziona."));
        _repairButton = Button("Controlla e ripara", RunRepairAsync, primary: true);
        _repairBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed
        };
        _repairStatusText = new TextBlock
        {
            Style = StyleResource("PlayhubBodyTextStyle"),
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            // Testo di stato impostato a runtime, già tradotto con T():
            // non deve essere ritradotto dal LocalizeElement.
            Tag = "noloc"
        };
        repair.Children.Add(ActionRow(_repairButton));
        repair.Children.Add(_repairBar);
        repair.Children.Add(_repairStatusText);
        panel.Children.Add(repair);

        // ---------- Serve aiuto? (report diagnostico) ----------
        var diagnostics = Card();
        diagnostics.Children.Add(IconHeader(((char)0xE9D9).ToString(), "Serve aiuto?",
            "Se qualcosa non funziona come ti aspetti, crea un report diagnostico. Raccoglie le informazioni utili a capire il problema e lo salva sul desktop, pronto da allegare alla tua segnalazione."));
        _diagnosticsButton = Button("Crea report diagnostico", RunDiagnosticsAsync, primary: true);
        _diagnosticsStatusText = new TextBlock
        {
            Style = StyleResource("PlayhubBodyTextStyle"),
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            // Testo di stato impostato a runtime, già tradotto con T().
            Tag = "noloc"
        };
        diagnostics.Children.Add(ActionRow(_diagnosticsButton));
        diagnostics.Children.Add(_diagnosticsStatusText);
        panel.Children.Add(diagnostics);

        // ---------- Informazioni ----------
        var about = Card();
        about.Children.Add(IconHeader(((char)0xE946).ToString(), "Informazioni",
            $"Playhub {GetAppVersion()} · © 2026 Andrea Sgarro (LoZazaMastro)"));
        about.Children.Add(Body("Componenti di terze parti (licenza MIT): UWPHook © 2016 Brian Lima · VDFParser © 2016 Victor Gama · SharpSteam © 2020 Brian Lima."));
        about.Children.Add(Body("Playhub è un progetto indipendente e non è affiliato né approvato da Valve. Steam e Steam Controller sono marchi di Valve Corporation."));
        about.Children.Add(ActionRow(
            Button("UWPHook", async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/BrianLima/UWPHook"))),
            Button("VDFParser", async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/BrianLima/VDFParser"))),
            Button("SharpSteam", async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/BrianLima/SharpSteam")))));
        about.Children.Add(new Expander
        {
            Header = "Note sui componenti",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new ScrollViewer
            {
                MaxHeight = 280,
                Content = new TextBlock
                {
                    Text = ThirdPartyLicensesText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontSize = 12,
                    Opacity = 0.85
                }
            }
        });
        panel.Children.Add(about);
        return panel;
    }

    // "Ripara tutto": esegue la RepairService mostrando barra di avanzamento
    // WinUI e testo di stato ("Controllo…", "Sistemo…", "Verifico…").
    private async Task RunRepairAsync()
    {
        if (_repairRunning) return;
        _repairRunning = true;
        _repairButton.IsEnabled = false;
        _repairBar.Value = 0;
        _repairBar.Visibility = Visibility.Visible;
        _repairStatusText.Visibility = Visibility.Visible;
        _repairStatusText.Text = T("Preparazione del controllo…");
        try
        {
            // La RepairService riporta le chiavi italiane: la traduzione nella
            // lingua attiva avviene qui con T(), come per il resto della UI.
            var progress = new Progress<(double Percent, string Status)>(update =>
            {
                _repairBar.Value = update.Percent;
                _repairStatusText.Text = T(update.Status);
            });

            var repairService = new RepairService(_gamingMode);
            var report = await repairService.RunAsync(_settings.DeckyPluginsPath, progress);

            var summary = report.IssuesFound == 0
                ? T("Tutto a posto: nessun problema trovato.")
                : string.Format(T("{0} problemi trovati, {1} risolti."), report.IssuesFound, report.IssuesFixed);
            if (report.Notes.Count > 0)
            {
                summary += "\n• " + string.Join("\n• ", report.Notes.Select(T));
            }
            _repairStatusText.Text = summary;
        }
        catch (Exception ex)
        {
            Diag.Crash("RunRepairAsync", ex);
            _repairStatusText.Text = T("Non riesco a completare il ripristino. Riprova.");
        }
        finally
        {
            _repairRunning = false;
            _repairButton.IsEnabled = true;
            _repairBar.Visibility = Visibility.Collapsed;
        }
    }

    // "Crea report diagnostico": genera il report completo sul desktop e lo
    // evidenzia in Esplora file, così l'utente lo trova subito.
    private async Task RunDiagnosticsAsync()
    {
        if (_diagnosticsRunning) return;
        _diagnosticsRunning = true;
        _diagnosticsButton.IsEnabled = false;
        _diagnosticsStatusText.Visibility = Visibility.Visible;
        _diagnosticsStatusText.Text = T("Creazione del report in corso…");
        try
        {
            var diagnostics = new DiagnosticsService(_gamingMode);
            var reportPath = await diagnostics.CreateReportAsync(AppPaths.SettingsFile);
            _diagnosticsStatusText.Text = string.Format(
                T("Report salvato sul desktop: {0}"), Path.GetFileName(reportPath));
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + reportPath + "\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Se Esplora file non si apre, il report è comunque sul desktop.
            }
        }
        catch (Exception ex)
        {
            Diag.Crash("RunDiagnosticsAsync", ex);
            _diagnosticsStatusText.Text = T("Non riesco a creare il report. Riprova.");
        }
        finally
        {
            _diagnosticsRunning = false;
            _diagnosticsButton.IsEnabled = true;
        }
    }

    private const string ThirdPartyLicensesText =
@"Playhub includes the following open-source components under the MIT License.

UWPHook    - Copyright (c) 2016 Brian Lima - https://github.com/BrianLima/UWPHook
VDFParser  - Copyright (c) 2016 Victor Gama - https://github.com/BrianLima/VDFParser
SharpSteam - Copyright (c) 2020 Brian Lima - https://github.com/BrianLima/SharpSteam

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

Trademarks

Steam, the Steam Controller and related logos and images are trademarks of
Valve Corporation.

All trademarks, logos and product images are the property of their respective
owners and are used here for identification (nominative) purposes only. Playhub
is an independent project and is not affiliated with, sponsored by, or endorsed
by Valve.";


    private async Task CheckDeveloperModeAsync()
    {
        var enabled = _deckyInstaller.IsDeveloperModeEnabled();
        SetStatus(
            enabled ? "Modalità sviluppatore attiva." : "Modalità sviluppatore non attiva.",
            enabled ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        await Task.CompletedTask;
    }

    private async Task LoadDeckyBuildsSilentlyAsync()
    {
        try
        {
            _deckyBuilds.Clear();
            foreach (var run in await _deckyInstaller.GetMainBuildsAsync())
            {
                _deckyBuilds.Add(run);
            }

            _deckyBuildCombo.ItemsSource = _deckyBuilds;
            _deckyBuildCombo.DisplayMemberPath = "Display";
            if (_deckyBuilds.Count > 0)
            {
                _deckyBuildCombo.SelectedIndex = 0;
            }
        }
        catch
        {
        }
    }

    private async Task InstallLatestDeckyBuildAsync()
    {
        using var context = BeginNotificationContext("decky");
        try
        {
            SetStatus(await _deckyInstaller.InstallOrUpdateLatestAsync(), InfoBarSeverity.Success);
        }
        finally
        {
            await RefreshDeckyStateAsync();
        }
    }

    private async Task InstallSelectedDeckyBuildAsync()
    {
        if (_deckyBuildCombo.SelectedItem is not DeckyBuildRun run)
        {
            SetStatus("Scegli prima una versione dall'elenco.", InfoBarSeverity.Warning);
            return;
        }

        SetStatus(await _deckyInstaller.InstallBuildAsync(run), InfoBarSeverity.Success);
    }

    private void RenderPluginCardsIfNeeded()
    {
        if (_pluginCardsDirty)
        {
            RenderPluginCards();
        }
    }

    private void RenderPluginManagementIfNeeded()
    {
        if (_pluginManagementDirty)
        {
            RenderPluginManagement();
        }
    }

    private void RenderVisiblePluginView()
    {
        if (_currentPageTag != "plugins") return;
        if (_pluginStoreMode == "manage") RenderPluginManagementIfNeeded();
        else RenderPluginCardsIfNeeded();
    }

    private Task RefreshPluginsAsync() => RefreshRemoteAwarePluginsAsync();

    private void RenderPluginManagement()
    {
        CancelPluginCardMorph();
        _pluginManagementDirty = false;
        _pluginManageContent.Children.Clear();

        var installed = _plugins
            .Where(plugin => plugin.IsInstalled && !IsIntegratedGamingModePlugin(plugin))
            .OrderByDescending(plugin => plugin.HasUpdate)
            .ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var updates = installed.Where(plugin => plugin.HasUpdate).ToList();
        var visible = SortPluginAll(FilterPluginAllBySource(installed
            .Where(plugin => string.IsNullOrWhiteSpace(_pluginManageQuery) ||
                MatchesPluginSearch(plugin, _pluginManageQuery.Trim())))).ToList();

        var heading = new Grid { ColumnSpacing = 16, Tag = "plugin-management-heading" };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        heading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var headingCopy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Bottom };
        headingCopy.Children.Add(new TextBlock
        {
            Text = T("Plugin installati"),
            FontSize = 25,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        heading.Children.Add(headingCopy);

        if (updates.Count > 1 && !_pluginBulkUpdateRunning)
        {
            var updateAll = IconButton(((char)0xE895).ToString(), "Aggiorna tutti", UpdateAllPluginsAsync, primary: true);
            updateAll.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(updateAll, 1);
            heading.Children.Add(updateAll);
        }
        var selectors = BuildPluginViewSelectors();
        selectors.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(selectors, 2);
        heading.Children.Add(selectors);
        heading.SizeChanged += (_, args) =>
        {
            selectors.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            headingCopy.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var actionWidth = heading.Children.OfType<Button>().Sum(button => button.ActualWidth + 16);
            var compact = args.NewSize.Width < headingCopy.DesiredSize.Width + selectors.DesiredSize.Width + actionWidth + 16;
            Grid.SetColumn(selectors, compact ? 0 : 2);
            Grid.SetColumnSpan(selectors, compact ? 3 : 1);
            Grid.SetRow(headingCopy, compact ? 1 : 0);
            headingCopy.Margin = new Thickness(0, compact ? 12 : 0, 0, 0);
            foreach (var button in heading.Children.OfType<Button>()) Grid.SetRow(button, compact ? 1 : 0);
        };
        _pluginManageContent.Children.Add(heading);

        _pluginManageProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = Math.Max(1, updates.Count),
            Height = 4,
            Visibility = _pluginBulkUpdateRunning ? Visibility.Visible : Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _pluginManageProgressText = new TextBlock
        {
            FontSize = 13,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(200, 255, 255, 255)),
            Visibility = _pluginBulkUpdateRunning ? Visibility.Visible : Visibility.Collapsed,
            Tag = "noloc"
        };
        var progress = new StackPanel { Spacing = 6 };
        progress.Children.Add(_pluginManageProgressText);
        progress.Children.Add(_pluginManageProgress);
        _pluginManageContent.Children.Add(progress);

        if (installed.Count == 0)
        {
            var empty = new StackPanel
            {
                Spacing = 10,
                Padding = new Thickness(24, 44, 24, 44),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            empty.Children.Add(new FontIcon
            {
                Glyph = ((char)0xE7B8).ToString(),
                FontSize = 30,
                Opacity = 0.58,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            empty.Children.Add(new TextBlock
            {
                Text = T("Nessun plugin installato"),
                FontSize = 16,
                Opacity = 0.74,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            _pluginManageContent.Children.Add(empty);
        }
        else if (visible.Count == 0)
            _pluginManageContent.Children.Add(Body(T("Nessun plugin trovato.")));
        else if (_pluginAllLayout == "list")
        {
            _pluginManageListCache ??= BuildPluginStoreListRepeater(visible, managed: true);
            AttachPluginView(_pluginManageContent, _pluginManageListCache);
        }
        else
        {
            _pluginManageCardsCache ??= BuildPluginStoreGrid(visible, managed: true);
            AttachPluginView(_pluginManageContent, _pluginManageCardsCache);
        }

        LocalizeElement(_pluginManageContent);
    }

    private UIElement BuildManagedPluginRow(DeckyPluginInfo plugin)
    {
        var compactHost = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var expandedHost = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };

        async Task ToggleDetails()
        {
            OpenPluginPage(plugin, compactHost);
            await Task.CompletedTask;
        }

        var visual = new Grid();
        var image = PluginImageElement(plugin, 320);
        if (image is not null)
        {
            visual.Children.Add(image);
        }
        else
        {
            visual.Background = new SolidColorBrush(WithAlpha(ParseColor(_settings.AccentColor), 54));
            visual.Children.Add(new FontIcon
            {
                Glyph = plugin.IconGlyph,
                FontSize = 28,
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var thumbnail = new Border
        {
            Width = _pluginManageCompact ? 76 : 96,
            Height = _pluginManageCompact ? 52 : 64,
            CornerRadius = new CornerRadius(7),
            Child = visual,
            VerticalAlignment = VerticalAlignment.Center
        };

        var metadata = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        metadata.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        var installedVersion = new[] { plugin.InstalledVersion, plugin.Version }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var versionText = string.IsNullOrWhiteSpace(installedVersion)
            ? T("Versione non rilevata")
            : plugin.HasUpdate && !string.IsNullOrWhiteSpace(plugin.Version)
                ? $"{T("Versione installata")} {installedVersion}  ·  {T("Ultima versione")} {plugin.Version}"
                : $"{T("Versione installata")} {installedVersion}";
        metadata.Children.Add(new TextBlock
        {
            Text = versionText,
            FontSize = 13,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(205, 255, 255, 255)),
            Tag = "noloc"
        });
        var secondary = string.Join("  ·  ", new[] { plugin.Author, plugin.Category }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            metadata.Children.Add(new TextBlock
            {
                Text = secondary,
                FontSize = 12,
                Opacity = 0.58,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                Tag = "noloc"
            });
        }

        var actions = BuildManagedPluginActions(plugin, ToggleDetails);
        UIElement content;
        if (_pluginManageCompact)
        {
            var compact = new StackPanel { Spacing = 14 };
            var summary = new Grid { ColumnSpacing = 14 };
            summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            summary.ColumnDefinitions.Add(new ColumnDefinition());
            summary.Children.Add(thumbnail);
            Grid.SetColumn(metadata, 1);
            summary.Children.Add(metadata);
            compact.Children.Add(summary);
            compact.Children.Add(actions);
            content = compact;
        }
        else
        {
            var row = new Grid { ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(thumbnail);
            Grid.SetColumn(metadata, 1);
            row.Children.Add(metadata);
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            content = row;
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
            Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(232, 32, 32, 36)),
            Child = content
        };
        card.Tapped += async (_, args) =>
        {
            if (ComesFromButton(args.OriginalSource))
            {
                return;
            }

            args.Handled = true;
            await ToggleDetails();
        };
        AddStoreCardInteractions(card);
        compactHost.Children.Add(card);
        var updateNotes = BuildManagedUpdateNotesCard(plugin);
        if (updateNotes is not null)
        {
            compactHost.Children.Add(updateNotes);
        }

        var wrapper = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        wrapper.Children.Add(compactHost);
        wrapper.Children.Add(expandedHost);
        return wrapper;
    }

    private StackPanel BuildManagedPluginActions(DeckyPluginInfo plugin, Func<Task> detailsAction)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = _pluginManageCompact ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Children.Add(BuildCatalogStatusBadge(plugin, expanded: false));

        if (plugin.HasUpdate)
        {
            var update = CreatePluginInstallButton(plugin, compact: false);
            update.IsEnabled = !_pluginBulkUpdateRunning && !_pluginInstallOperations.ContainsKey(PluginStoreKey(plugin));
            row.Children.Add(update);
        }

        var uninstall = IconButton(((char)0xE74D).ToString(), "Disinstalla",
            () => UninstallStorePluginAsync(plugin));
        uninstall.Height = 42;
        uninstall.MinHeight = 42;
        uninstall.IsEnabled = !_pluginBulkUpdateRunning;
        row.Children.Add(BindPluginUninstallButton(uninstall, plugin, compact: false));

        return row;
    }

    private UIElement? BuildManagedUpdateNotesCard(DeckyPluginInfo plugin)
    {
        if (!plugin.HasUpdate || string.IsNullOrWhiteSpace(plugin.ReleaseNotes))
        {
            return null;
        }

        var gold = Color.FromArgb(255, 255, 205, 28);
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(new FontIcon
        {
            Glyph = ((char)0xE895).ToString(),
            FontSize = 14,
            Foreground = new SolidColorBrush(gold),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = T("Novità"),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(gold),
            VerticalAlignment = VerticalAlignment.Center
        });
        var targetVersion = new[] { plugin.ReleaseNotesVersion, plugin.Version }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(targetVersion))
        {
            header.Children.Add(new TextBlock
            {
                Text = targetVersion,
                FontSize = 12,
                Opacity = 0.78,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "noloc"
            });
        }

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(header);
        var notes = BuildDescription(plugin.ReleaseNotes);
        notes.Tag = "noloc";
        content.Children.Add(notes);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(26, 255, 205, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(112, 255, 205, 28)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Child = content
        };
    }

    private static void MarkPluginInstalled(DeckyPluginInfo plugin)
    {
        plugin.IsInstalled = true;
        plugin.HasUpdate = false;
        var installedVersion = new[]
        {
            plugin.Version,
            plugin.ReleaseNotesVersion,
            plugin.InstalledVersion
        }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(installedVersion))
        {
            plugin.InstalledVersion = installedVersion;
        }
    }

    private async Task CommitPluginInstallStateAsync(DeckyPluginInfo plugin)
    {
        MarkPluginInstalled(plugin);
        InvalidatePluginAllViews();
        InvalidateFeaturedFrames();
        _pluginCardsDirty = true;
        _pluginManagementDirty = true;
        RenderVisiblePluginView();
        RefreshOpenPluginPage();
        await Task.Yield();
        await RefreshPluginsAsync();
    }

    private async Task UpdateAllPluginsAsync()
    {
        using var context = BeginNotificationContext("plugins");
        if (_pluginBulkUpdateRunning || _pluginInstallOperations.Count > 0 || _pluginUninstalls.Count > 0)
        {
            return;
        }

        var updates = _plugins
            .Where(plugin => plugin.IsInstalled && plugin.HasUpdate)
            .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (updates.Count < 2)
        {
            return;
        }

        _pluginBulkUpdateRunning = true;
        RenderPluginManagement();
        var failures = new List<string>();
        try
        {
            for (var index = 0; index < updates.Count; index++)
            {
                var plugin = updates[index];
                _pluginManageProgress.Value = index;
                _pluginManageProgressText.Text = string.Format(
                    T("Aggiornamento di {0} su {1}"), index + 1, updates.Count) + $": {plugin.Name}";
                try
                {
                    if (!await InstallPluginWithProgressAsync(plugin, bulkOperation: true)) failures.Add(plugin.Name);
                    RenderPluginManagement();
                    _pluginManageProgress.Value = index + 1;
                    _pluginManageProgressText.Text = string.Format(
                        T("Aggiornamento di {0} su {1}"), index + 1, updates.Count) + $": {plugin.Name}";
                }
                catch
                {
                    failures.Add(plugin.Name);
                }
                _pluginManageProgress.Value = index + 1;
            }
        }
        finally
        {
            _pluginBulkUpdateRunning = false;
            await RefreshPluginsAsync();
        }

        if (failures.Count == 0)
        {
            SetStatus(T("Tutti i plugin sono aggiornati."), InfoBarSeverity.Success);
        }
        else
        {
            SetStatus($"{T("Aggiornamento non riuscito")}: {string.Join(", ", failures)}", InfoBarSeverity.Warning);
        }
    }

    private void RenderPluginCards()
    {
        var refreshFeatured = _pluginFeaturedHost.Visibility == Visibility.Visible &&
            (_pluginCardsDirty || _pluginFeaturedHost.Children.Count == 0);
        _pluginCardsDirty = false;
        _collapseOpenPluginCard?.Invoke();
        _collapseOpenPluginCard = null;
        CancelPluginCardMorph();
        if (refreshFeatured)
        {
            RenderFeaturedPlugin();
        }
        _pluginCards.Children.Clear();

        var query = _pluginSearchBox.Text?.Trim() ?? string.Empty;
        IEnumerable<DeckyPluginInfo> visibleQuery = _plugins
            .Where(plugin => _pluginCategoryFilter is null ||
                PluginBelongsToCategory(plugin, _pluginCategoryFilter))
            .Where(plugin => string.IsNullOrWhiteSpace(query) || MatchesPluginSearch(plugin, query));
        var visible = _pluginShowAll || !string.IsNullOrWhiteSpace(query) || _pluginCategoryFilter is not null
            ? SortPluginAll(FilterPluginAllBySource(visibleQuery)).ToList()
            : visibleQuery.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        if (visible.Count == 0 && !_pluginShowAll && string.IsNullOrWhiteSpace(query) && _pluginCategoryFilter is null)
        {
            _pluginCards.Children.Add(new TextBlock
            {
                Text = T("Nessun plugin trovato."),
                Style = StyleResource("PlayhubBodyTextStyle"),
                Margin = new Thickness(0, 28, 0, 28),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            _pluginCards.Children.Add(BuildPluginStoreCategory("Risultati", visible, showLayoutToggle: true));
        }
        else if (_pluginShowAll)
        {
            _pluginCards.Children.Add(BuildPluginStoreCategory(_pluginCategoryFilter ?? "Tutti i plugin", visible, showLayoutToggle: true));
        }
        else
        {
            foreach (var category in new[] { "I plugin di Playhub", "Personalizzazione e media", "Libreria e giochi",
                "Social e community", "Strumenti e utilità", "Sistema e hardware" })
            {
                var group = visible.Where(plugin => PluginBelongsToCategory(plugin, category)).ToList();
                if (group.Count == 0) continue;
                _pluginCards.Children.Add(BuildPluginDiscoveryCategory(
                    category,
                    group.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase).ToList()));
            }
        }

        LocalizeElement(_pluginCards);
    }

    private static int PluginStoreCategoryOrder(string category)
    {
        return NormalizePluginStoreCategory(category).ToLowerInvariant() switch
        {
            "i plugin di playhub" => 0,
            "personalizzazione e media" => 1,
            "libreria e giochi" => 2,
            "social e community" => 3,
            "strumenti e utilità" => 4,
            "sistema e hardware" => 5,
            _ => 6
        };
    }

    private static string NormalizePluginStoreCategory(string category)
    {
        return category.Trim().ToLowerInvariant() switch
        {
            "media e personalizzazione" => "Personalizzazione e media",
            "personalizzazione e media" => "Personalizzazione e media",
            "sistema e connettività" => "Sistema e hardware",
            "sistema e hardware" => "Sistema e hardware",
            "rete e strumenti" => "Sistema e hardware",
            "controller e hardware" => "Sistema e hardware",
            "giochi e libreria" => "Libreria e giochi",
            "libreria e giochi" => "Libreria e giochi",
            "social e community" => "Social e community",
            "strumenti e utilita" or "strumenti e utilità" => "Strumenti e utilità",
            _ => category
        };
    }

    private void RenderFeaturedPlugin()
    {
        StopFeaturedAutoAdvance();
        CompleteFeaturedSlideTransition();
        _featuredSlideStoryboard?.Stop();
        _featuredSlideStoryboard = null;
        _featuredPluginTransitioning = false;
        _pluginFeaturedCarouselHost.Children.Clear();
        _pluginFeaturedHost.Children.Clear();
        _pluginFeaturedHost.Background = new SolidColorBrush(Colors.Transparent);
        _featuredPluginExpanded = false;
        SetFeaturedPluginCollapsedSize(_pluginFeaturedHost.ActualWidth);
        var featured = GetFeaturedPlugins();
        if (featured.Count == 0)
        {
            _pluginFeaturedHost.Children.Add(new ProgressRing
            {
                IsActive = true,
                Width = 36,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }

        if (_featuredPluginIndex < 0 || _featuredPluginIndex >= featured.Count)
        {
            _featuredPluginIndex = 0;
        }

        var frame = new Grid
        {
            Margin = new Thickness(4, 4, 4, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        _pluginFeaturedCarouselHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _pluginFeaturedCarouselHost.SizeChanged += (sender, args) =>
        {
            if (ReferenceEquals(sender, _pluginFeaturedCarouselHost)) CompleteFeaturedSlideTransition();
            ((Grid)sender).Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, args.NewSize.Width, args.NewSize.Height)
            };
        };
        _pluginFeaturedCarouselHost.Children.Add(GetFeaturedFrame(featured[_featuredPluginIndex]));
        frame.Children.Add(_pluginFeaturedCarouselHost);

        _pluginFeaturedPreviousButton = StoreArrowButton(
            ((char)0xE76B).ToString(), "Plugin precedente", -1, HorizontalAlignment.Left);
        _pluginFeaturedNextButton = StoreArrowButton(
            ((char)0xE76C).ToString(), "Plugin successivo", 1, HorizontalAlignment.Right);
        _pluginFeaturedPreviousButton.Margin = new Thickness(12, 0, 0, 0);
        _pluginFeaturedNextButton.Margin = new Thickness(0, 0, 12, 0);
        var navigationVisibility = featured.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _pluginFeaturedPreviousButton.Visibility = navigationVisibility;
        _pluginFeaturedNextButton.Visibility = navigationVisibility;
        frame.Children.Add(_pluginFeaturedPreviousButton);
        frame.Children.Add(_pluginFeaturedNextButton);

        _pluginFeaturedHost.Children.Add(frame);
        WarmFeaturedFrames(featured);
        ResetFeaturedAutoAdvance();
    }

    private void InvalidateFeaturedFrames()
    {
        _featuredFrameCacheVersion++;
        _featuredFrameCache.Clear();
    }

    private FrameworkElement GetFeaturedFrame(DeckyPluginInfo plugin)
    {
        var key = PluginStoreKey(plugin);
        if (!_featuredFrameCache.TryGetValue(key, out var frame))
        {
            frame = BuildFeaturedPluginFrame(plugin);
            LocalizeElement(frame);
            _featuredFrameCache[key] = frame;
        }
        if (frame.Parent is Panel previousHost) previousHost.Children.Remove(frame);
        return frame;
    }

    private void WarmFeaturedFrames(IReadOnlyList<DeckyPluginInfo> featured)
    {
        var version = _featuredFrameCacheVersion;
        foreach (var plugin in featured)
        {
            var key = PluginStoreKey(plugin);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (version != _featuredFrameCacheVersion || _featuredFrameCache.ContainsKey(key)) return;
                GetFeaturedFrame(plugin);
            });
        }
    }

    private IReadOnlyList<DeckyPluginInfo> GetFeaturedPlugins()
    {
        var playhubPlugins = _plugins
            .Where(plugin => plugin.IsPlayhubPlugin && !IsIntegratedGamingModePlugin(plugin))
            .ToList();
        var orderedKeys = playhubPlugins
            .OrderBy(plugin => plugin.HasUpdate ? 0 : !plugin.IsInstalled ? 1 : 2)
            .ThenBy(plugin =>
            {
                var index = _featuredPluginKeys.FindIndex(key =>
                    string.Equals(key, PluginStoreKey(plugin), StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(_ => Random.Shared.Next())
            .Select(PluginStoreKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        if (!_featuredPluginKeys.SequenceEqual(orderedKeys, StringComparer.OrdinalIgnoreCase))
        {
            _featuredPluginIndex = 0;
            InvalidateFeaturedFrames();
        }
        _featuredPluginKeys.Clear();
        _featuredPluginKeys.AddRange(orderedKeys);

        return _featuredPluginKeys
            .Select(key => playhubPlugins.FirstOrDefault(plugin =>
                string.Equals(PluginStoreKey(plugin), key, StringComparison.OrdinalIgnoreCase)))
            .Where(plugin => plugin is not null)
            .Cast<DeckyPluginInfo>()
            .Take(5)
            .ToList();
    }

    private static string PluginStoreKey(DeckyPluginInfo plugin)
        => string.IsNullOrWhiteSpace(plugin.RepositoryName) ? plugin.Name : plugin.RepositoryName;

    private FrameworkElement BuildFeaturedPluginFrame(DeckyPluginInfo plugin)
    {
        var stage = new Grid();
        var imagePath = PluginImagePath(plugin);
        if (imagePath is not null)
        {
            stage.Background = new ImageBrush
            {
                ImageSource = CachedPluginBitmap(imagePath, 1600),
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
        }
        else
        {
            stage.Background = new SolidColorBrush(WithAlpha(ParseColor(_settings.AccentColor), 88));
        }

        stage.Children.Add(new Border
        {
            Background = Scrim(0.92, 0.18),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        });

        var copy = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(72, 32, 72, 58),
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        copy.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontSize = 34,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap
        });
        copy.Children.Add(new TextBlock
        {
            Text = PluginCatalogService.LocalizedShortDescription(
                plugin, LocalizationService.ResolveLanguage(_settings.Language)),
            FontSize = 16,
            Foreground = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Tag = "noloc"
        });
        var featuredActions = (FrameworkElement)BuildPluginStoreActions(
            plugin,
            compact: true,
            includeUninstall: false);
        featuredActions.Margin = new Thickness(0, 12, 0, 0);
        copy.Children.Add(featuredActions);
        stage.Children.Add(copy);

        if (plugin.HasUpdate)
        {
            var updatePill = PluginStatusPill(plugin);
            updatePill.Margin = new Thickness(18);
            updatePill.HorizontalAlignment = HorizontalAlignment.Left;
            updatePill.VerticalAlignment = VerticalAlignment.Top;
            stage.Children.Add(updatePill);
        }

        var featuredBadge = BuildCatalogStatusBadge(plugin, expanded: true);
        featuredBadge.Margin = new Thickness(18);
        featuredBadge.HorizontalAlignment = HorizontalAlignment.Right;
        featuredBadge.VerticalAlignment = VerticalAlignment.Top;
        stage.Children.Add(featuredBadge);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(255, 32, 32, 36)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = stage
        };
        stage.Children.Add(BuildFeaturedAutoAdvanceControl(card));
        card.Tapped += (_, args) =>
        {
            if (ComesFromButton(args.OriginalSource))
            {
                return;
            }

            args.Handled = true;
            ExpandFeaturedPlugin(plugin);
        };
        return card;
    }

    private void SlideFeaturedPlugin(int direction)
    {
        CompleteFeaturedSlideIfElapsed();
        if (_featuredPluginTransitioning || _featuredPluginExpanded || direction == 0) return;
        var featured = GetFeaturedPlugins();
        if (featured.Count < 2)
        {
            return;
        }

        direction = Math.Sign(direction);
        var previous = _pluginFeaturedCarouselHost.Children.OfType<FrameworkElement>().LastOrDefault();
        _featuredPluginIndex = (_featuredPluginIndex + direction + featured.Count) % featured.Count;
        var next = GetFeaturedFrame(featured[_featuredPluginIndex]);
        if (!MotionEnabled() || previous == null || _pluginFeaturedCarouselHost.ActualWidth <= 0)
        {
            _pluginFeaturedCarouselHost.Children.Clear();
            _pluginFeaturedCarouselHost.Children.Add(next);
            ResetFeaturedAutoAdvance();
            return;
        }
        StartFeaturedSlideTransition(previous, next, direction);
        ResetFeaturedAutoAdvance();
    }

    private void ExpandFeaturedPlugin(DeckyPluginInfo plugin)
    {
        if (_featuredPluginExpanded || _featuredPluginTransitioning)
        {
            return;
        }

        OpenPluginPage(plugin, _pluginFeaturedCarouselHost.Children.OfType<FrameworkElement>().Last());
    }

    private void CloseFeaturedPluginDetails()
    {
        var source = _pluginFeaturedCarouselHost.Children.OfType<FrameworkElement>().Last();
        MorphPluginCard(source, () =>
        {
            _featuredPluginExpanded = false;
            RenderFeaturedPlugin();
            return _pluginFeaturedCarouselHost.Children.OfType<FrameworkElement>().Last();
        });
    }

    private void CancelPluginCardMorph()
    {
        _pluginCardMorphVersion++;
        var cancel = _cancelPluginMorphVisual;
        _cancelPluginMorphVisual = null;
        try { cancel?.Invoke(); } catch { }
    }

    private void MorphPluginCard(FrameworkElement source, Func<FrameworkElement> switchView)
    {
        CancelPluginCardMorph();
        var destination = switchView();
        destination.Opacity = 1;
    }

    private void SetFeaturedPluginCollapsedSize(double width)
    {
        if (width <= 0)
        {
            width = 1280;
        }

        var height = Math.Clamp(width * 0.26, 340, 470);
        _pluginFeaturedHost.Height = height;
        _pluginFeaturedHost.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, width, height)
        };
    }

    private Button StoreArrowButton(string glyph, string tooltip, int direction, HorizontalAlignment alignment)
    {
        var button = new Button
        {
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            CornerRadius = new CornerRadius(20),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(164, 18, 18, 22)),
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = glyph, FontSize = 18 }
        };
        SetLocalizedToolTip(button, tooltip);
        button.Click += (_, _) => SlideFeaturedPlugin(direction);
        return button;
    }

    private UIElement BuildPluginStoreCategory(
        string title,
        IReadOnlyList<DeckyPluginInfo> plugins,
        bool showLayoutToggle = false,
        bool clickableHeading = false)
    {
        var section = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(0, 0, 0, 24),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        heading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        heading.Children.Add(clickableHeading ? BuildPluginCategoryHeading(title) : new TextBlock
        {
            Text = T(title),
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        if (showLayoutToggle)
        {
            var selectors = BuildPluginViewSelectors();
            Grid.SetColumn(selectors, 1);
            heading.Children.Add(selectors);
            heading.SizeChanged += (_, args) =>
            {
                var compact = args.NewSize.Width > 0 && args.NewSize.Width < selectors.ActualWidth + 260;
                Grid.SetColumn(selectors, compact ? 0 : 1);
                Grid.SetColumnSpan(selectors, compact ? 2 : 1);
                Grid.SetRow(selectors, compact ? 1 : 0);
                selectors.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(0);
            };
        }
        section.Children.Add(heading);

        if (plugins.Count == 0)
        {
            section.Children.Add(new TextBlock
            {
                Text = T("Nessun plugin trovato."), Style = StyleResource("PlayhubBodyTextStyle"),
                Margin = new Thickness(0, 28, 0, 28), HorizontalAlignment = HorizontalAlignment.Center
            });
            return section;
        }

        if (showLayoutToggle)
        {
            if (string.Equals(_pluginAllLayout, "list", StringComparison.Ordinal))
            {
                _pluginAllListCache ??= BuildPluginStoreListRepeater(plugins);
                AttachPluginView(section, _pluginAllListCache);
            }
            else
            {
                _pluginAllCardsCache ??= BuildPluginStoreGrid(plugins);
                AttachPluginView(section, _pluginAllCardsCache);
            }
            return section;
        }

        section.Children.Add(BuildPluginStoreGrid(plugins));
        return section;
    }

    private UIElement BuildPluginStoreGrid(IReadOnlyList<DeckyPluginInfo> plugins, bool managed = false)
    {
        var columns = Math.Max(1, managed ? _pluginManageColumnCount : _pluginStoreColumnCount);
        var rowGroups = new List<IReadOnlyList<DeckyPluginInfo>>();
        for (var rowStart = 0; rowStart < plugins.Count; rowStart += columns)
        {
            rowGroups.Add(plugins.Skip(rowStart).Take(columns).ToList());
        }

        var repeater = new ItemsRepeater
        {
            ItemsSource = rowGroups,
            ItemTemplate = PluginRepeaterTemplate(),
            Layout = new StackLayout { Spacing = 14 },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        repeater.ElementPrepared += (_, args) =>
        {
            if (args.Element is ContentPresenter presenter &&
                args.Index >= 0 && args.Index < rowGroups.Count)
            {
                var index = args.Index;
                var width = managed ? _pluginManageContent.ActualWidth : _pluginCards.ActualWidth;
                var tileWidth = Math.Max(260, (width - 14 * (columns - 1)) / columns);
                QueuePluginRow(repeater, presenter, index, Math.Max(160, tileWidth * 0.5) + 150,
                    () => BuildPluginStoreGridRow(rowGroups[index], columns, managed));
            }
        };
        repeater.ElementClearing += ClearPluginRow;
        return repeater;
    }

    private UIElement BuildPluginStoreGridRow(
        IReadOnlyList<DeckyPluginInfo> plugins,
        int columns,
        bool managed = false)
    {
        var compactRow = new Grid
        {
            ColumnSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        for (var column = 0; column < columns; column++)
        {
            compactRow.ColumnDefinitions.Add(new ColumnDefinition());
        }

        var expandedHost = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };
        var tiles = new Dictionary<string, FrameworkElement>(StringComparer.OrdinalIgnoreCase);

        async Task OpenInRow(DeckyPluginInfo selected)
        {
            OpenPluginPage(selected, tiles[PluginStoreKey(selected)]);
            await Task.CompletedTask;
        }

        for (var column = 0; column < plugins.Count; column++)
        {
            var tile = BuildPluginStoreTile(plugins[column], OpenInRow);
            tiles[PluginStoreKey(plugins[column])] = tile;
            var item = managed ? WithManagedUpdateNotes(tile, plugins[column]) : tile;
            Grid.SetColumn(item, column);
            compactRow.Children.Add(item);
        }

        var rowShell = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        rowShell.Children.Add(compactRow);
        rowShell.Children.Add(expandedHost);
        return rowShell;
    }

    private FrameworkElement WithManagedUpdateNotes(FrameworkElement content, DeckyPluginInfo plugin)
    {
        if (!plugin.HasUpdate || string.IsNullOrWhiteSpace(plugin.ReleaseNotes)) return content;
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(content);
        if (BuildManagedUpdateNotesCard(plugin) is { } notes) stack.Children.Add(notes);
        return stack;
    }

    private FrameworkElement BuildPluginViewSelectors()
    {
        var selectors = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "plugin-view-selectors"
        };
        selectors.Children.Add(BuildPluginAllSourceSelector());
        selectors.Children.Add(BuildPluginAllSortSelector());
        selectors.Children.Add(BuildPluginAllLayoutSelector());
        return selectors;
    }

    private FrameworkElement BuildPluginAllLayoutSelector()
    {
        var accent = ParseColor(_settings.AccentColor);
        var selectedForeground = NeedsLightForeground(accent) ? Colors.White : Colors.Black;
        var buttons = new Grid { ColumnSpacing = 2 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition());

        Button LayoutButton(string layout, string glyph, string tooltip)
        {
            var selected = string.Equals(_pluginAllLayout, layout, StringComparison.Ordinal);
            var button = new Button
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                Background = selected
                    ? new SolidColorBrush(accent)
                    : new SolidColorBrush(Colors.Transparent),
                Foreground = selected
                    ? new SolidColorBrush(selectedForeground)
                    : ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(210, 255, 255, 255)),
                Content = new FontIcon
                {
                    Glyph = glyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 16
                }
            };
            SetLocalizedToolTip(button, tooltip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(tooltip));
            button.Click += (_, _) =>
            {
                if (string.Equals(_pluginAllLayout, layout, StringComparison.Ordinal))
                {
                    return;
                }

                _pluginAllLayout = layout;
                _pluginCardsDirty = true;
                _pluginManagementDirty = true;
                RenderVisiblePluginView();
                _ = PersistPluginLayoutAsync();
            };
            return button;
        }

        var cards = LayoutButton("cards", ((char)0xE8A9).ToString(), "Visualizzazione a schede");
        var list = LayoutButton("list", ((char)0xE8FD).ToString(), "Visualizzazione elenco");
        buttons.Children.Add(list);
        Grid.SetColumn(cards, 1);
        buttons.Children.Add(cards);

        return new Border
        {
            Height = 44,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(74, 255, 255, 255)),
            Child = buttons
        };
    }

    private FrameworkElement BuildPluginAllSourceSelector()
    {
        var accent = ParseColor(_settings.AccentColor);
        var selectedForeground = NeedsLightForeground(accent) ? Colors.White : Colors.Black;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2
        };

        FrameworkElement SourceIcon(string source)
        {
            if (string.Equals(source, "all", StringComparison.Ordinal))
            {
                return new TextBlock
                {
                    Text = T("Tutti"),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            var sourcePlugin = source switch
            {
                "playhub" => new DeckyPluginInfo { IsPlayhubPlugin = true },
                "decky" => new DeckyPluginInfo
                {
                    IsPlayhubPlugin = false,
                    CatalogStatus = "decky",
                    CatalogSource = "decky-store"
                },
                _ => new DeckyPluginInfo
                {
                    IsPlayhubPlugin = false,
                    CatalogStatus = "github",
                    CatalogSource = "outside-store"
                }
            };
            return new Viewbox
            {
                Width = 28,
                Height = 28,
                Child = BuildCatalogStatusBadge(sourcePlugin, expanded: false)
            };
        }

        Button SourceButton(string source, string label)
        {
            var selected = string.Equals(_pluginAllSource, source, StringComparison.Ordinal);
            var isAll = string.Equals(source, "all", StringComparison.Ordinal);
            var width = isAll ? 64d : 40d;
            var button = new Button
            {
                Width = width,
                Height = 40,
                MinWidth = width,
                Padding = isAll ? new Thickness(10, 0, 10, 0) : new Thickness(0),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(0),
                Background = selected
                    ? new SolidColorBrush(accent)
                    : new SolidColorBrush(Colors.Transparent),
                Foreground = selected
                    ? new SolidColorBrush(selectedForeground)
                    : ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(210, 255, 255, 255)),
                Content = SourceIcon(source)
            };
            SetLocalizedToolTip(button, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(label));
            button.Click += (_, _) =>
            {
                if (string.Equals(_pluginAllSource, source, StringComparison.Ordinal))
                {
                    return;
                }

                _pluginAllSource = source;
                InvalidatePluginAllViews();
                RenderVisiblePluginView();
            };
            return button;
        }

        buttons.Children.Add(SourceButton("all", "Tutti"));
        buttons.Children.Add(SourceButton("playhub", "Playhub"));
        buttons.Children.Add(SourceButton("decky", "Decky"));
        buttons.Children.Add(SourceButton("github", "GitHub"));

        return new Border
        {
            Height = 44,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(74, 255, 255, 255)),
            Child = buttons
        };
    }

    private FrameworkElement BuildPluginAllSortSelector()
    {
        var selectedLabel = _pluginAllSort switch
        {
            "added" => "Data di aggiunta",
            "updated" => "Data di aggiornamento",
            _ => "Nome"
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new FontIcon
        {
            Glyph = ((char)0xE8CB).ToString(),
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 15
        });
        content.Children.Add(new TextBlock
        {
            Text = T(selectedLabel),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new FontIcon
        {
            Glyph = ((char)0xE70D).ToString(),
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 11,
            Opacity = 0.72
        });

        var button = new Button
        {
            Height = 40,
            MinHeight = 40,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Content = content
        };
        var flyout = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
        };

        void AddSortOption(string sort, string label)
        {
            var item = new MenuFlyoutItem { Text = T(label) };
            if (string.Equals(_pluginAllSort, sort, StringComparison.Ordinal))
            {
                item.Icon = new FontIcon
                {
                    Glyph = ((char)0xE73E).ToString(),
                    FontFamily = new FontFamily("Segoe Fluent Icons")
                };
            }
            item.Click += (_, _) =>
            {
                if (string.Equals(_pluginAllSort, sort, StringComparison.Ordinal))
                {
                    return;
                }

                _pluginAllSort = sort;
                InvalidatePluginAllViews();
                RenderVisiblePluginView();
            };
            flyout.Items.Add(item);
        }

        AddSortOption("name", "Nome");
        AddSortOption("added", "Data di aggiunta");
        AddSortOption("updated", "Data di aggiornamento");
        button.Flyout = flyout;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T("Ordina per"));
        return new Border
        {
            Height = 44,
            Padding = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(74, 255, 255, 255)),
            Child = button
        };
    }

    private IEnumerable<DeckyPluginInfo> FilterPluginAllBySource(IEnumerable<DeckyPluginInfo> plugins)
    {
        return _pluginAllSource switch
        {
            "playhub" => plugins.Where(plugin => plugin.IsPlayhubPlugin),
            "decky" => plugins.Where(plugin =>
                string.Equals(plugin.CatalogSource, "decky-store", StringComparison.OrdinalIgnoreCase)),
            "github" => plugins.Where(plugin =>
                !plugin.IsPlayhubPlugin &&
                !string.Equals(plugin.CatalogSource, "decky-store", StringComparison.OrdinalIgnoreCase)),
            _ => plugins
        };
    }

    private IEnumerable<DeckyPluginInfo> SortPluginAll(IEnumerable<DeckyPluginInfo> plugins)
    {
        return _pluginAllSort switch
        {
            "added" => plugins
                .OrderByDescending(plugin => PluginCatalogDate(plugin.ReleasePublishedAt))
                .ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase),
            "updated" => plugins
                .OrderByDescending(plugin => PluginCatalogDate(
                    string.IsNullOrWhiteSpace(plugin.UpdatedAt)
                        ? plugin.ReleasePublishedAt
                        : plugin.UpdatedAt))
                .ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => plugins.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private static DateTime PluginCatalogDate(string value)
    {
        if (DateTime.TryParseExact(
                value,
                new[] { "dd/MM/yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ssZ" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return DateTime.MinValue;
    }

    private void InvalidatePluginAllViews()
    {
        _pluginAllCardsCache = null;
        _pluginAllListCache = null;
        _pluginManageCardsCache = null;
        _pluginManageListCache = null;
        _pluginCardsDirty = true;
        _pluginManagementDirty = true;
    }

    private static void AttachPluginView(Panel target, UIElement view)
    {
        if (PluginViewOwners.TryGetValue(view, out var currentParent)) currentParent.Children.Remove(view);
        PluginViewOwners.Remove(view);
        target.Children.Add(view);
        PluginViewOwners.Add(view, target);
    }

    private DataTemplate PluginRepeaterTemplate()
    {
        return _pluginRepeaterTemplate ??= (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<ContentPresenter HorizontalContentAlignment=\"Stretch\" />" +
            "</DataTemplate>");
    }

    private UIElement BuildPluginStoreListRepeater(IReadOnlyList<DeckyPluginInfo> plugins, bool managed = false)
    {
        var items = plugins.ToList();
        var repeater = new ItemsRepeater
        {
            ItemsSource = items,
            ItemTemplate = PluginRepeaterTemplate(),
            Layout = new StackLayout { Spacing = 10 },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        repeater.ElementPrepared += (_, args) =>
        {
            if (args.Element is ContentPresenter presenter &&
                args.Index >= 0 && args.Index < items.Count)
            {
                var index = args.Index;
                QueuePluginRow(repeater, presenter, index, 121, () =>
                {
                    var row = (FrameworkElement)BuildPluginStoreListRow(items[index]);
                    return managed ? WithManagedUpdateNotes(row, items[index]) : row;
                });
            }
        };
        repeater.ElementClearing += ClearPluginRow;
        return repeater;
    }

    private UIElement BuildPluginStoreListRow(DeckyPluginInfo plugin)
    {
        var compactHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        var expandedHost = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        async Task ToggleRow()
        {
            OpenPluginPage(plugin, compactHost);
            await Task.CompletedTask;
        }

        var row = new Grid
        {
            ColumnSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var imageStage = new Border
        {
            Width = 176,
            Height = 99,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(255, 22, 22, 26))
        };
        var image = PluginImageElement(plugin, 520);
        if (image is not null)
        {
            image.Stretch = Stretch.UniformToFill;
            image.HorizontalAlignment = HorizontalAlignment.Stretch;
            image.VerticalAlignment = VerticalAlignment.Stretch;
            imageStage.Child = image;
        }
        else
        {
            imageStage.Child = new FontIcon
            {
                Glyph = plugin.IconGlyph,
                FontSize = 34,
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        row.Children.Add(imageStage);

        var copy = new StackPanel
        {
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        copy.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);

        var badge = BuildCatalogStatusBadge(plugin, expanded: false);
        badge.Margin = new Thickness(4, 0, 4, 0);
        Grid.SetColumn(badge, 2);
        row.Children.Add(badge);

        var actions = (FrameworkElement)BuildPluginStoreActions(plugin, compact: true, showRepository: false);
        Grid.SetColumn(actions, 3);
        row.Children.Add(actions);

        var card = new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
            Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(232, 32, 32, 36)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = row
        };
        card.Tapped += async (_, args) =>
        {
            if (ComesFromButton(args.OriginalSource))
            {
                return;
            }

            args.Handled = true;
            await ToggleRow();
        };
        AddStoreCardInteractions(card);
        compactHost.Children.Add(card);

        var shell = new StackPanel
        {
            Spacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        shell.Children.Add(compactHost);
        shell.Children.Add(expandedHost);
        return shell;
    }

    private Border BuildPluginStoreTile(
        DeckyPluginInfo plugin,
        Func<DeckyPluginInfo, Task> openDetails)
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        var imageStage = new Grid
        {
            Height = 190,
            Background = new SolidColorBrush(Color.FromArgb(255, 22, 22, 26))
        };
        var image = PluginImageElement(plugin, 760);
        if (image is not null)
        {
            image.Stretch = Stretch.UniformToFill;
            image.HorizontalAlignment = HorizontalAlignment.Stretch;
            image.VerticalAlignment = VerticalAlignment.Stretch;
            imageStage.Children.Add(image);
        }
        else
        {
            imageStage.Background = new SolidColorBrush(WithAlpha(ParseColor(_settings.AccentColor), 74));
            imageStage.Children.Add(new FontIcon
            {
                Glyph = plugin.IconGlyph,
                FontSize = 46,
                Opacity = 0.8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        imageStage.Children.Add(new Border
        {
            Height = 74,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = CardScrim()
        });
        content.Children.Add(imageStage);

        var body = new Grid
        {
            RowSpacing = 8,
            Padding = new Thickness(16, 14, 16, 12)
        };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Tag = "noloc"
        });
        var actions = (FrameworkElement)BuildPluginStoreActions(plugin, compact: true);
        actions.VerticalAlignment = VerticalAlignment.Top;
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(actions, 1);
        heading.Children.Add(actions);
        body.Children.Add(heading);
        var description = new TextBlock
        {
            Text = PluginCatalogService.LocalizedShortDescription(plugin,
                LocalizationService.ResolveLanguage(_settings.Language)),
            FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
            LineHeight = 20, MinHeight = 40,
            TextTrimming = TextTrimming.CharacterEllipsis, Tag = "noloc",
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(description, 1);
        body.Children.Add(description);
        var badge = BuildCatalogStatusBadge(plugin, expanded: false);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.VerticalAlignment = VerticalAlignment.Bottom;
        badge.Tag = "plugin-card-source-badge";
        Grid.SetRow(badge, 2);
        body.Children.Add(badge);
        Grid.SetRow(body, 1);
        content.Children.Add(body);

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
            Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(232, 32, 32, 36)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content
        };
        card.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width <= 0)
            {
                return;
            }

            var imageHeight = Math.Max(160, args.NewSize.Width * 0.5);
            if (Math.Abs(imageStage.Height - imageHeight) > 0.5) imageStage.Height = imageHeight;
        };
        card.Tapped += async (_, args) =>
        {
            if (ComesFromButton(args.OriginalSource))
            {
                return;
            }

            args.Handled = true;
            await openDetails(plugin);
        };
        AddStoreCardInteractions(card);
        card.IsTabStop = true;
        card.KeyDown += async (_, args) =>
        {
            if (ComesFromButton(args.OriginalSource) ||
                (args.Key != VirtualKey.Enter && args.Key != VirtualKey.Space)) return;
            args.Handled = true;
            await openDetails(plugin);
        };
        return card;
    }

    private UIElement BuildPluginStoreActions(
        DeckyPluginInfo plugin,
        bool compact,
        Func<Task>? detailsAction = null,
        bool showRepository = true,
        bool includeInstall = true,
        bool includeUninstall = true)
    {
        if (!compact)
        {
            return PluginActions(plugin, includeUninstall);
        }

        var grid = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        void Add(Button button)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            button.Width = button.MinWidth = button.MaxWidth = 32;
            button.Height = button.MinHeight = button.MaxHeight = 32;
            button.Padding = new Thickness(0);
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(button, grid.Children.Count);
            grid.Children.Add(button);
        }

        if (includeInstall && (!plugin.IsInstalled || plugin.HasUpdate))
        {
            Add(CreatePluginInstallButton(plugin, compact: true));
        }

        if (includeUninstall && plugin.IsInstalled)
        {
            Add(BindPluginUninstallButton(StoreIconButton(((char)0xE74D).ToString(), "Disinstalla",
                () => UninstallStorePluginAsync(plugin)), plugin, compact: true));
        }

        if (showRepository && !string.IsNullOrWhiteSpace(plugin.RepositoryUrl))
        {
            Add(StoreGitHubIconButton(async () =>
                await Launcher.LaunchUriAsync(new Uri(plugin.RepositoryUrl))));
        }
        return grid;
    }

    private Button StoreIconButton(string glyph, string tooltip, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            MinWidth = 42,
            Height = 42,
            Padding = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 16 },
            Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle")
        };
        RegisterButton(button, primary);
        SetLocalizedToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(tooltip));
        button.Click += async (_, _) =>
        {
            using var context = BeginNotificationContext("plugins");
            using var reminderOperation = BeginSupportReminderOperation();
            try
            {
                button.IsEnabled = false;
                await action();
            }
            catch (Exception ex)
            {
                SetStatus(FriendlyError(ex), InfoBarSeverity.Error);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private Button StoreGitHubIconButton(Func<Task> action)
    {
        var mark = (UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"" + GitHubMarkPath + "\"/>");
        var button = new Button
        {
            MinWidth = 42,
            Height = 42,
            Padding = new Thickness(0),
            Content = new Viewbox { Width = 16, Height = 16, Child = mark },
            Style = StyleResource("PlayhubSecondaryButtonStyle")
        };
        ToolTipService.SetToolTip(button, "GitHub");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "GitHub");
        button.Click += async (_, _) =>
        {
            try
            {
                button.IsEnabled = false;
                await action();
            }
            catch (Exception ex)
            {
                SetStatus(FriendlyError(ex), InfoBarSeverity.Error);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private async Task ShowPluginDetailsAsync(DeckyPluginInfo plugin)
    {
        var key = PluginStoreKey(plugin);
        if (_pluginDetailsHost.Visibility == Visibility.Visible &&
            string.Equals(_expandedPluginKey, key, StringComparison.OrdinalIgnoreCase))
        {
            ClosePluginDetails();
            return;
        }

        _expandedPluginKey = key;
        _pluginDetailsHost.Children.Clear();
        _pluginDetailsHost.Children.Add(PluginBannerCard(
            plugin,
            initiallyExpanded: true,
            closeRequested: ClosePluginDetails));
        _pluginDetailsHost.Visibility = Visibility.Visible;
        _pluginDetailsHost.Opacity = 0;
        FadeIn(_pluginDetailsHost);
        DispatcherQueue.TryEnqueue(() => ScrollCardIntoView(_pluginDetailsHost));
        await Task.CompletedTask;
    }

    private void ClosePluginDetails()
    {
        _expandedPluginKey = null;
        FadeOutThenHide(_pluginDetailsHost);
    }

    private static bool MatchesPluginSearch(DeckyPluginInfo plugin, string query)
    {
        var haystack = string.Join(' ', new[]
        {
            plugin.Name,
            plugin.Author,
            plugin.ShortDescription,
            plugin.LongDescription,
            plugin.Category,
            plugin.Keywords,
            plugin.CatalogStatus,
            plugin.RepositoryName
        });
        return haystack.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private Border BuildCatalogStatusBadge(DeckyPluginInfo plugin, bool expanded)
    {
        var catalogStatus = plugin.CatalogStatus.Trim().ToLowerInvariant();
        var status = plugin.IsPlayhubPlugin
            ? "playhub"
            : catalogStatus.Contains("decky", StringComparison.Ordinal) ||
              plugin.CatalogSource.Contains("decky", StringComparison.OrdinalIgnoreCase)
                ? "decky"
                : "github";
        var label = status switch
        {
            "playhub" => "Playhub",
            "decky" => "Decky Store",
            _ => "GitHub"
        };
        var start = status switch
        {
            "playhub" => Color.FromArgb(255, 188, 132, 0),
            "decky" => Color.FromArgb(255, 57, 211, 197),
            _ => Color.FromArgb(255, 102, 56, 157)
        };
        var end = status switch
        {
            "playhub" => Color.FromArgb(255, 112, 72, 0),
            "decky" => Color.FromArgb(255, 31, 52, 91),
            _ => Color.FromArgb(255, 102, 56, 157)
        };
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = start, Offset = 0 },
                new GradientStop { Color = end, Offset = 1 }
            }
        };

        var iconHost = new Grid
        {
            Width = 36,
            Height = 36,
            UseLayoutRounding = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (status == "playhub")
        {
            iconHost.Children.Add(new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(0),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(255, 255, 241, 157), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(255, 236, 185, 49), Offset = 0.2 },
                        new GradientStop { Color = Color.FromArgb(255, 170, 101, 0), Offset = 0.5 },
                        new GradientStop { Color = Color.FromArgb(255, 219, 155, 22), Offset = 0.73 },
                        new GradientStop { Color = Color.FromArgb(255, 103, 54, 0), Offset = 1 }
                    }
                }
            });
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "PlayhubTag.png");
            if (File.Exists(logoPath))
            {
                iconHost.Children.Add(new Image
                {
                    Source = new BitmapImage(new Uri(logoPath)),
                    Width = 17.1,
                    Height = 17.1,
                    Stretch = Stretch.Uniform,
                    RenderTransform = new CompositeTransform { TranslateX = 0.5 },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                iconHost.Children.Add(new TextBlock
                {
                    Text = "P",
                    FontSize = 19,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 223, 76)),
                    Margin = new Thickness(0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
        }
        else if (status == "decky")
        {
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "DeckyStoreBadge.png");
            if (File.Exists(logoPath))
            {
                iconHost.Children.Add(new Image
                {
                    Source = new BitmapImage(new Uri(logoPath)),
                    Width = 36,
                    Height = 36,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                iconHost.Children.Add(new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(17),
                    Background = brush,
                    Child = new TextBlock
                    {
                        Text = "D",
                        FontSize = 17,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }
        }
        else
        {
            var githubCircle = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = new SolidColorBrush(Color.FromArgb(255, 102, 56, 157))
            };
            var mark = (PathIcon)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"" + GitHubMarkPath + "\"/>");
            mark.Foreground = new SolidColorBrush(Colors.White);
            githubCircle.Child = new Viewbox
            {
                Width = 19,
                Height = 19,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = mark
            };
            iconHost.Children.Add(githubCircle);
        }

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = expanded ? 8 : 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(iconHost);
        if (expanded)
        {
            row.Children.Add(new TextBlock
            {
                Text = T(label),
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        if (!expanded)
        {
            return new Border
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(2),
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Colors.Transparent),
                Child = row
            };
        }

        return new Border
        {
            Height = 42,
            MinWidth = 42,
            Padding = new Thickness(3, 0, 13, 0),
            CornerRadius = new CornerRadius(21),
            BorderThickness = new Thickness(0),
            Background = brush,
            Child = row
        };
    }

    private static int GetPluginStoreColumnCount(double width)
    {
        if (width >= 2200) return 6;
        if (width >= 1760) return 5;
        if (width >= 1320) return 4;
        if (width >= 900) return 3;
        if (width >= 600) return 2;
        return 1;
    }

    private UIElement PluginBannerCard(
        DeckyPluginInfo plugin,
        bool initiallyExpanded = false,
        Action? closeRequested = null,
        bool pageMode = false)
    {
        const double compressedHeight = 188;
        var expandedAspect = 9.0 / 16.0;
        var expanded = initiallyExpanded;
        Border card = null!; // declared early so ToggleDetails can scroll it into view

        var banner = new Grid { Height = initiallyExpanded ? 360 : compressedHeight };
        void SizeExpandedArtwork()
        {
            if (pageMode || !expanded || banner.ActualWidth <= 0) return;
            var height = banner.ActualWidth * expandedAspect;
            if (Math.Abs(banner.Height - height) > 0.5) banner.Height = height;
        }
        banner.SizeChanged += (_, args) =>
        {
            if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 0.5) SizeExpandedArtwork();
        };

        var imagePath = PluginImagePath(plugin);
        if (imagePath is not null)
        {
            var bitmap = CachedPluginBitmap(imagePath, 1500);
            var artwork = new Image
            {
                Source = bitmap,
                Stretch = initiallyExpanded ? Stretch.Uniform : Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            void UpdateArtworkAspect()
            {
                if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                    expandedAspect = (double)bitmap.PixelHeight / bitmap.PixelWidth;
                SizeExpandedArtwork();
            }
            artwork.ImageOpened += (_, _) => UpdateArtworkAspect();
            UpdateArtworkAspect();
            banner.Children.Add(artwork);
        }
        else
        {
            banner.Background = new SolidColorBrush(WithAlpha(ParseColor(_settings.AccentColor), 70));
        }
        var artworkScrim = new Border
        {
            Height = 210,
            Tag = "plugin-artwork-scrim",
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = CardScrim()
        };
        banner.Children.Add(artworkScrim);

        var pill = PluginStatusPill(plugin);
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        pill.VerticalAlignment = VerticalAlignment.Top;
        pill.Margin = new Thickness(20, 18, 0, 0);
        banner.Children.Add(pill);

        var catalogBadge = BuildCatalogStatusBadge(plugin, expanded: true);
        catalogBadge.HorizontalAlignment = HorizontalAlignment.Right;
        catalogBadge.VerticalAlignment = VerticalAlignment.Top;
        catalogBadge.Margin = new Thickness(0, 16, 18, 0);
        banner.Children.Add(catalogBadge);

        var details = new StackPanel
        {
            Visibility = initiallyExpanded ? Visibility.Visible : Visibility.Collapsed,
            Opacity = initiallyExpanded ? 1 : 0,
            Padding = new Thickness(24, 8, 24, 22),
            Spacing = 14
        };
        void RenderDetailsContent()
        {
            details.Children.Clear();
            // Keep screenshots immediately below the actions, before the page description.
            if (plugin.Media.Count > 0)
            {
                details.Children.Add(PluginMediaStrip(plugin));
            }
            var noveltyCard = PluginNoveltyCard(plugin);
            if (noveltyCard is not null)
            {
                details.Children.Add(noveltyCard);
            }
            var localizedLong = PluginCatalogService.LocalizedLongDescription(
                plugin, LocalizationService.ResolveLanguage(_settings.Language));
            if (!string.IsNullOrWhiteSpace(localizedLong))
            {
                var description = BuildDescription(localizedLong);
                description.Name = "PluginDescription";
                // Testo già nella lingua giusta (blocco unico): il walker NON deve
                // ritradurlo riga per riga, altrimenti tornerebbe il "misto".
                description.Tag = "noloc";
                details.Children.Insert(pageMode ? (plugin.Media.Count > 0 ? 1 : 0) : details.Children.Count, description);
            }
            if (!plugin.IsPlayhubPlugin) details.Children.Add(BuildExternalPluginWarning(plugin));
        }

        var enrichmentRequested = false;
        void RequestExternalDetails()
        {
            if (enrichmentRequested || plugin.IsPlayhubPlugin)
            {
                return;
            }

            enrichmentRequested = true;
            _ = EnrichExternalDetailsAsync();
        }

        async Task EnrichExternalDetailsAsync()
        {
            await _catalog.EnsurePluginDetailsAsync(plugin);
            DispatcherQueue.TryEnqueue(() =>
            {
                if (card?.IsLoaded == true && details.Visibility == Visibility.Visible)
                    RenderDetailsContent();
            });
        }

        RenderDetailsContent();

        // Dettagli toggle (expands/collapses; the chevron flips)
        var chevron = new FontIcon
        {
            Glyph = (initiallyExpanded ? (char)0xE70E : (char)0xE70D).ToString(),
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center
        };
        var detailsLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        detailsLabel.Children.Add(chevron);
        detailsLabel.Children.Add(new TextBlock { Text = "Dettagli", VerticalAlignment = VerticalAlignment.Center });
        var detailsButton = new Button { Content = detailsLabel, Style = StyleResource("PlayhubSecondaryButtonStyle") };
        void ToggleDetails()
        {
            if (expanded)
            {
                if (closeRequested is not null)
                {
                    closeRequested();
                    return;
                }
                Collapse();
                chevron.Glyph = ((char)0xE70D).ToString();
                _collapseOpenPluginCard = null;
            }
            else
            {
                // Accordion: instantly collapse whichever card was already open,
                // then expand this one and scroll its image to the top.
                _collapseOpenPluginCard?.Invoke();
                Expand();
                chevron.Glyph = ((char)0xE70E).ToString();
                _collapseOpenPluginCard = CollapseInstant;
                DispatcherQueue.TryEnqueue(() => ScrollCardIntoView(card));
            }
        }
        detailsButton.Click += (_, _) => ToggleDetails();
        banner.Tapped += (_, args) =>
        {
            if (pageMode) return;
            if (ComesFromButton(args.OriginalSource))
            {
                return;
            }

            ToggleDetails();
            args.Handled = true;
        };

        // bottom-left: name + short description + actions
        var bottom = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(24, 0, 24, 20),
            Spacing = 8,
            MaxWidth = 780
        };
        bottom.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontSize = 30,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap
        });
        bottom.Children.Add(new TextBlock
        {
            Text = PluginCatalogService.LocalizedShortDescription(
                plugin, LocalizationService.ResolveLanguage(_settings.Language)),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(222, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            // Già localizzata sopra: il walker non deve ritoccarla.
            Tag = "noloc"
        });
        bottom.Children.Add(PluginActions(plugin));
        banner.Children.Add(bottom);

        // ---------- assemble ----------
        var stack = new StackPanel();
        stack.Children.Add(banner);
        stack.Children.Add(details);

        card = new Border
        {
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 34)),
            Child = stack
        };
        if (pageMode)
            ConfigurePluginDetailHero(banner, stack, bottom, details, pill, catalogBadge);
        if (initiallyExpanded)
        {
            card.Loaded += (_, _) =>
            {
                SizeExpandedArtwork();
                RequestExternalDetails();
            };
        }

        void Expand()
        {
            if (expanded) return;
            expanded = true;
            RequestExternalDetails();
            details.Visibility = Visibility.Visible;
            var width = banner.ActualWidth;
            var target = width > 0 ? width * expandedAspect : 360;
            AnimateHeight(banner, target);
            FadeIn(details);
        }

        void Collapse()
        {
            if (!expanded) return;
            expanded = false;
            AnimateHeight(banner, compressedHeight);
            FadeOutThenHide(details);
        }

        // Instant collapse (no animation) used by the accordion so layout is final
        // before we scroll the newly opened card to the top.
        void CollapseInstant()
        {
            if (!expanded) return;
            expanded = false;
            banner.Height = compressedHeight;
            details.Opacity = 0;
            details.Visibility = Visibility.Collapsed;
            chevron.Glyph = "";
        }

        LocalizeElement(card);
        return card;
    }

    private static bool ComesFromButton(object? source)
    {
        var node = source as DependencyObject;
        while (node is not null)
        {
            if (node is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private void ScrollCardIntoView(FrameworkElement card)
    {
        try
        {
            _pageHost.UpdateLayout();
            if (_contentScroller.Content is not UIElement content)
            {
                return;
            }

            var y = card.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            _contentScroller.ChangeView(null, Math.Max(0, y - 12), null, disableAnimation: false);
        }
        catch
        {
        }
    }

    // Vertical scrim: light at the top, fading to the card's grey at the bottom for legibility.
    private static LinearGradientBrush CardScrim()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(80, 0, 0, 0), Offset = 0.42 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(205, 24, 24, 28), Offset = 0.80 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 30, 30, 34), Offset = 1 });
        return brush;
    }

    private UIElement? PluginNoveltyCard(DeckyPluginInfo plugin)
    {
        // No real changelog/release notes → don't show the novelty card at all.
        if (string.IsNullOrWhiteSpace(plugin.ReleaseNotes))
        {
            return null;
        }

        var accent = ParseColor(_settings.AccentColor);
        var content = new StackPanel { Spacing = 8 };

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        headerRow.Children.Add(new TextBlock { Text = "Novità", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(accent), VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrWhiteSpace(plugin.ReleaseNotesVersion))
        {
            headerRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(WithAlpha(accent, 50)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = plugin.ReleaseNotesVersion, FontSize = 12, Foreground = new SolidColorBrush(accent) }
            });
        }
        if (!string.IsNullOrWhiteSpace(plugin.ReleaseNotesPublishedAt))
        {
            headerRow.Children.Add(new TextBlock { Text = plugin.ReleaseNotesPublishedAt, FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center });
        }
        content.Children.Add(headerRow);
        var releaseDescription = BuildDescription(plugin.ReleaseNotes);
        releaseDescription.Tag = "noloc";
        content.Children.Add(releaseDescription);

        return new Border
        {
            Background = new SolidColorBrush(WithAlpha(accent, 28)),
            BorderBrush = new SolidColorBrush(WithAlpha(accent, 95)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = content
        };
    }

    private static string FirstParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var normalized = text.Replace("\r\n", "\n").Trim();
        var breakIndex = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        var paragraph = (breakIndex > 0 ? normalized.Substring(0, breakIndex) : normalized).Replace("\n", " ").Trim();
        return paragraph.Length > 320 ? paragraph.Substring(0, 320).TrimEnd() + "…" : paragraph;
    }

    private static void AnimateHeight(FrameworkElement element, double to)
    {
        try
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(240)),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Height");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
        catch
        {
            element.Height = to;
        }
    }

    private static void FadeIn(UIElement element)
    {
        try
        {
            var animation = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(260)) };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
        catch
        {
            element.Opacity = 1;
        }
    }

    private static void FadeOutThenHide(FrameworkElement element)
    {
        try
        {
            var animation = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(170)) };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            storyboard.Completed += (_, _) => element.Visibility = Visibility.Collapsed;
            storyboard.Begin();
        }
        catch
        {
            element.Visibility = Visibility.Collapsed;
        }
    }

    // Renders a rich plugin description: plain paragraphs, "## " subheadings,
    // and "• "/"- " bullet lists with an accent-coloured marker. Per-line Trim()
    // lets the source strings be written as indented verbatim text.
    private FrameworkElement BuildDescription(string text)
    {
        text = PluginCatalogService.PrepareDescriptionForDisplay(text);
        var panel = new StackPanel { Spacing = 9 };
        var lines = text.Replace("\r\n", "\n").Split('\n');
        StackPanel? bullets = null;
        StackPanel? quoteLines = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                bullets = null;
                quoteLines = null;
                continue;
            }

            if (line.StartsWith(">", StringComparison.Ordinal))
            {
                bullets = null;
                line = line.TrimStart('>', ' ');
                if (line.Length == 0)
                {
                    continue;
                }

                if (quoteLines is null)
                {
                    quoteLines = new StackPanel { Spacing = 6 };
                    panel.Children.Add(new Border
                    {
                        BorderBrush = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor)),
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(13, 4, 0, 4),
                        Child = quoteLines
                    });
                }

                var quoteHeadingMatch = Regex.Match(line, @"^#{1,6}\s+(.+?)(?:\s+#+)?$");
                var quoteHeading = quoteHeadingMatch.Success;
                quoteLines.Children.Add(DescriptionTextBlock(
                    quoteHeading ? quoteHeadingMatch.Groups[1].Value.Trim() : line,
                    quoteHeading ? 15 : 14,
                    quoteHeading,
                    0.86,
                    21));
                continue;
            }

            quoteLines = null;

            var headingMatch = Regex.Match(line, @"^#{1,6}\s+(.+?)(?:\s+#+)?$");
            if (headingMatch.Success)
            {
                bullets = null;
                var heading = DescriptionTextBlock(headingMatch.Groups[1].Value.Trim(), 15, true, 1, 21);
                heading.Margin = new Thickness(0, 4, 0, 0);
                heading.Foreground = ResourceBrush("TextFillColorPrimaryBrush", Colors.White);
                panel.Children.Add(heading);
                continue;
            }

            var numberedItem = Regex.Match(line, @"^(?<marker>\d+[.)])\s+(?<text>.+)$");
            if (line.StartsWith("• ", StringComparison.Ordinal) || line.StartsWith("- ", StringComparison.Ordinal) || numberedItem.Success)
            {
                if (bullets is null)
                {
                    bullets = new StackPanel { Spacing = 6 };
                    panel.Children.Add(bullets);
                }

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var dot = new TextBlock
                {
                    Text = numberedItem.Success ? numberedItem.Groups["marker"].Value : "•",
                    FontSize = 16,
                    Margin = new Thickness(2, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Foreground = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor))
                };
                Grid.SetColumn(dot, 0);

                var content = DescriptionTextBlock(numberedItem.Success ? numberedItem.Groups["text"].Value : line[2..].Trim(), 14, false, 0.9, 21);
                Grid.SetColumn(content, 1);

                row.Children.Add(dot);
                row.Children.Add(content);
                bullets.Children.Add(row);
                continue;
            }

            bullets = null;
            panel.Children.Add(DescriptionTextBlock(line, 14, false, 0.92, 22));
        }

        return panel;
    }

    private TextBlock DescriptionTextBlock(
        string text,
        double fontSize,
        bool semiBold,
        double opacity,
        double lineHeight)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = semiBold
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal,
            LineHeight = lineHeight,
            Opacity = opacity
        };
        AppendDescriptionInlines(block.Inlines, text);
        return block;
    }

    private static void AppendDescriptionInlines(InlineCollection target, string text)
    {
        var cursor = 0;
        foreach (Match match in DescriptionInlinePattern.Matches(text))
        {
            if (match.Index > cursor)
            {
                target.Add(new Run { Text = text[cursor..match.Index] });
            }

            if (match.Groups["link"].Success)
            {
                AppendDescriptionLink(target, match.Groups["linkText"].Value, match.Groups["linkUrl"].Value);
            }
            else if (match.Groups["bold"].Success || match.Groups["boldAlt"].Success)
            {
                var value = match.Groups["bold"].Success
                    ? match.Groups["boldText"].Value
                    : match.Groups["boldAltText"].Value;
                var bold = new Bold();
                AppendDescriptionInlines(bold.Inlines, value);
                target.Add(bold);
            }
            else if (match.Groups["italic"].Success || match.Groups["italicAlt"].Success)
            {
                var value = match.Groups["italic"].Success
                    ? match.Groups["italicText"].Value
                    : match.Groups["italicAltText"].Value;
                var italic = new Italic();
                AppendDescriptionInlines(italic.Inlines, value);
                target.Add(italic);
            }
            else if (match.Groups["code"].Success)
            {
                target.Add(new Run
                {
                    Text = match.Groups["codeText"].Value,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas")
                });
            }
            else
            {
                var rawUrl = match.Groups["url"].Value;
                var url = rawUrl.TrimEnd('.', ',', ';');
                AppendDescriptionLink(target, url, url);
                if (url.Length < rawUrl.Length)
                {
                    target.Add(new Run { Text = rawUrl[url.Length..] });
                }
            }

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
        {
            target.Add(new Run { Text = text[cursor..] });
        }
    }

    private static void AppendDescriptionLink(InlineCollection target, string label, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            target.Add(new Run { Text = label });
            return;
        }

        var link = new Hyperlink { NavigateUri = uri };
        // A bare URL uses the URL itself as its label. Feeding that label back into
        // the inline parser would match the same URL forever and overflow the stack.
        link.Inlines.Add(new Run { Text = label });
        target.Add(link);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = T(title),
                Content = T(message),
                PrimaryButtonText = T("Sì"),
                CloseButtonText = T("No"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            ConfigureDialogEntrance(dialog);
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            return false;
        }
    }

    private StackPanel PluginActions(DeckyPluginInfo plugin, bool includeUninstall = true)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        // Accent (primary) only for a real confirm action: install or update.
        if (!plugin.IsInstalled || plugin.HasUpdate)
        {
            row.Children.Add(CreatePluginInstallButton(plugin, compact: false));
        }

        if (includeUninstall && plugin.IsInstalled)
        {
            row.Children.Add(BindPluginUninstallButton(IconButton(((char)0xE74D).ToString(), "Disinstalla",
                () => UninstallStorePluginAsync(plugin)), plugin, compact: false));
        }

        // GitHub is always last.
        row.Children.Add(GitHubButton(async () =>
        {
            if (!string.IsNullOrWhiteSpace(plugin.RepositoryUrl))
            {
                await Launcher.LaunchUriAsync(new Uri(plugin.RepositoryUrl));
            }
        }));

        foreach (var action in row.Children.OfType<FrameworkElement>())
        {
            action.Height = 42;
            action.MinHeight = 42;
        }
        return row;
    }

    private static string? PluginImagePath(DeckyPluginInfo plugin)
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "PluginImages", plugin.Name + ".jpg");
        if (System.IO.File.Exists(path))
        {
            return path;
        }

        foreach (var candidate in new[] { plugin.CoverImage, plugin.Image })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (System.IO.File.Exists(candidate) ||
                (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                 (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)))
            {
                return candidate;
            }
        }

        return null;
    }

    private Image? PluginImageElement(DeckyPluginInfo plugin, int decodeWidth)
        => CreatePluginPreviewImage(plugin, decodeWidth);

    // Dark gradient from the bottom-left (more opaque) to the top-right, for text legibility over images.
    private static Microsoft.UI.Xaml.Media.LinearGradientBrush Scrim(double bottomLeftAlpha, double topRightAlpha)
    {
        var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 1),
            EndPoint = new Windows.Foundation.Point(1, 0)
        };
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = Color.FromArgb((byte)(255 * bottomLeftAlpha), 0, 0, 0), Offset = 0 });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = Color.FromArgb((byte)(255 * (bottomLeftAlpha + topRightAlpha) / 2), 0, 0, 0), Offset = 0.5 });
        brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = Color.FromArgb((byte)(255 * topRightAlpha), 0, 0, 0), Offset = 1 });
        return brush;
    }

    private FrameworkElement PluginStatusPill(DeckyPluginInfo plugin)
    {
        var text = T(plugin.HasUpdate ? "Aggiornamento disponibile" : plugin.IsInstalled ? "Installato" : "Non installato");
        var version = plugin.HasUpdate
            ? plugin.Version
            : plugin.IsInstalled
                ? plugin.InstalledVersion
                : plugin.Version;
        if (!string.IsNullOrWhiteSpace(version))
        {
            text += " - " + version;
        }

        var foreground = plugin.HasUpdate
            ? ParseColor(_settings.AccentColor)
            : plugin.IsInstalled
                ? Color.FromArgb(255, 16, 124, 16)
                : Color.FromArgb(255, 185, 185, 185);
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 3),
            // Dark, semi-opaque chip so the label is readable over any (bright) banner.
            Background = new SolidColorBrush(Color.FromArgb(175, 12, 12, 16)),
            BorderBrush = new SolidColorBrush(WithAlpha(foreground, 150)),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = new SolidColorBrush(foreground)
            }
        };
    }

    private FrameworkElement PluginMediaStrip(DeckyPluginInfo plugin)
    {
        var items = plugin.Media.Take(4).ToList();
        var grid = new Grid { Name = "PluginScreenshots", ColumnSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        void RemoveFailedMedia(FrameworkElement tile, PluginMediaInfo media)
        {
            items.Remove(media);
            grid.Children.Remove(tile);
            grid.ColumnDefinitions.Clear();
            for (var column = 0; column < grid.Children.Count; column++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn((FrameworkElement)grid.Children[column], column);
            }
            grid.Visibility = grid.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        for (var i = 0; i < items.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var tile = PluginMediaTile(items, i, RemoveFailedMedia);
            Grid.SetColumn(tile, i);
            grid.Children.Add(tile);
        }

        return grid;
    }

    private FrameworkElement PluginMediaTile(List<PluginMediaInfo> all, int index, Action<FrameworkElement, PluginMediaInfo> failed)
    {
        var media = all[index];
        var border = new Border
        {
            Height = 150,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(255, 14, 14, 16)),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(44, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };

        var hasUri = Uri.TryCreate(media.Url, UriKind.Absolute, out var uri);

        if (media.Kind == "image" && hasUri)
        {
            // Whole image, letterboxed inside the tile (never cropped).
            var image = new Image { Stretch = Stretch.Uniform };
            image.ImageFailed += (_, _) => failed(border, media);
            border.Child = image;
            image.Source = new BitmapImage(uri);
        }
        else
        {
            var stack = new Grid();
            if (hasUri)
            {
                stack.Children.Add(BuildVideoPoster(uri!, () => failed(border, media)));
                // dim the poster a touch so the play badge reads well
                stack.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) });
            }
            var badge = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Children.Add(new FontIcon
            {
                Glyph = ((char)0xE768).ToString(),
                FontSize = 26,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White)
            });
            badge.Children.Add(new TextBlock
            {
                Text = "Video",
                FontSize = 12,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Colors.White)
            });
            stack.Children.Add(badge);
            border.Child = stack;
        }

        if (hasUri)
        {
            border.Tapped += (_, _) =>
            {
                var currentIndex = all.IndexOf(media);
                if (currentIndex >= 0) OpenLightbox(all, currentIndex);
            };
        }
        else border.Loaded += (_, _) => failed(border, media);

        return border;
    }

    // A muted, paused MediaPlayerElement showing the video's first frame as a poster.
    private FrameworkElement BuildVideoPoster(Uri uri, Action? failed = null)
    {
        var element = new MediaPlayerElement { AreTransportControlsEnabled = false, Stretch = Stretch.Uniform };
        Windows.Media.Playback.MediaPlayer? player = null;
        void ReleasePlayer()
        {
            element.SetMediaPlayer(null);
            player?.Dispose();
            player = null;
        }
        element.Loaded += (_, _) =>
        {
            if (player is not null) return;
            try
            {
                player = new Windows.Media.Playback.MediaPlayer { IsMuted = true, AutoPlay = false };
                player.CommandManager.IsEnabled = false;
                var current = player;
                player.MediaOpened += (sender, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ReferenceEquals(current, player)) return;
                    try { sender.StepForwardOneFrame(); } catch { }
                });
                player.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (!ReferenceEquals(current, player)) return;
                    ReleasePlayer();
                    failed?.Invoke();
                });
                element.SetMediaPlayer(player);
                player.Source = Windows.Media.Core.MediaSource.CreateFromUri(uri);
            }
            catch { ReleasePlayer(); failed?.Invoke(); }
        };
        element.Unloaded += (_, _) => ReleasePlayer();
        return element;
    }

    private Button GlyphCircleButton(string glyph, double size = 44)
    {
        return new Button
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(165, 28, 28, 32)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Content = new FontIcon { Glyph = glyph, FontSize = 16, Foreground = new SolidColorBrush(Colors.White) }
        };
    }

    private Grid BuildMediaLightbox()
    {
        _mediaLightbox = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 6, 6, 8)),
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };

        _lightboxStage = new Border
        {
            Margin = new Thickness(110, 84, 110, 84),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _mediaLightbox.Children.Add(_lightboxStage);

        _lightboxPrev = GlyphCircleButton(((char)0xE76B).ToString(), 48);
        _lightboxPrev.HorizontalAlignment = HorizontalAlignment.Left;
        _lightboxPrev.VerticalAlignment = VerticalAlignment.Center;
        _lightboxPrev.Margin = new Thickness(24, 0, 0, 0);
        _lightboxPrev.Click += (_, _) => LightboxStep(-1);
        _mediaLightbox.Children.Add(_lightboxPrev);

        _lightboxNext = GlyphCircleButton(((char)0xE76C).ToString(), 48);
        _lightboxNext.HorizontalAlignment = HorizontalAlignment.Right;
        _lightboxNext.VerticalAlignment = VerticalAlignment.Center;
        _lightboxNext.Margin = new Thickness(0, 0, 24, 0);
        _lightboxNext.Click += (_, _) => LightboxStep(1);
        _mediaLightbox.Children.Add(_lightboxNext);

        var close = GlyphCircleButton(((char)0xE711).ToString(), 40);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        // Pushed down so it doesn't collide with the window's caption buttons.
        close.Margin = new Thickness(0, 64, 26, 0);
        close.Click += (_, _) => CloseLightbox();
        _mediaLightbox.Children.Add(close);

        _lightboxCounter = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 30, 0, 0),
            FontSize = 13,
            Opacity = 0.85,
            Foreground = new SolidColorBrush(Colors.White)
        };
        _mediaLightbox.Children.Add(_lightboxCounter);

        return _mediaLightbox;
    }

    private void OpenLightbox(List<PluginMediaInfo> media, int index)
    {
        _lightboxMedia = media;
        _lightboxIndex = index;
        RenderLightbox();
        _mediaLightbox.Visibility = Visibility.Visible;
        _mediaLightbox.Opacity = 1;
    }

    private void RenderLightbox()
    {
        StopLightboxPlayer();

        if (_lightboxMedia.Count == 0)
        {
            CloseLightbox();
            return;
        }

        _lightboxIndex = ((_lightboxIndex % _lightboxMedia.Count) + _lightboxMedia.Count) % _lightboxMedia.Count;
        var media = _lightboxMedia[_lightboxIndex];

        var multiple = _lightboxMedia.Count > 1;
        _lightboxPrev.Visibility = multiple ? Visibility.Visible : Visibility.Collapsed;
        _lightboxNext.Visibility = multiple ? Visibility.Visible : Visibility.Collapsed;
        _lightboxCounter.Text = multiple ? $"{_lightboxIndex + 1} / {_lightboxMedia.Count}" : "";

        if (!Uri.TryCreate(media.Url, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (media.Kind == "video")
        {
            var player = new Windows.Media.Playback.MediaPlayer { AutoPlay = true };
            player.Source = Windows.Media.Core.MediaSource.CreateFromUri(uri);
            _lightboxPlayer = player;

            var element = new MediaPlayerElement { AreTransportControlsEnabled = true, Stretch = Stretch.Uniform };
            element.SetMediaPlayer(player);
            _lightboxStage.Child = element;
        }
        else
        {
            _lightboxStage.Child = new Image { Source = new BitmapImage(uri), Stretch = Stretch.Uniform };
        }
    }

    private void LightboxStep(int delta)
    {
        _lightboxIndex += delta;
        RenderLightbox();
    }

    private void StopLightboxPlayer()
    {
        if (_lightboxPlayer is not null)
        {
            try { _lightboxPlayer.Pause(); } catch { }
            try { _lightboxPlayer.Dispose(); } catch { }
            _lightboxPlayer = null;
        }
    }

    private void CloseLightbox()
    {
        StopLightboxPlayer();
        _lightboxStage.Child = null;
        _mediaLightbox.Opacity = 0;
        _mediaLightbox.Visibility = Visibility.Collapsed;
    }

    private static UIElement PluginTextSection(string title, string text)
    {
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Opacity = 0.78
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(text) ? "Nessuna descrizione disponibile." : text,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 22,
                    Opacity = 0.9
                }
            }
        };
    }

    private UIElement PluginDetails(DeckyPluginInfo plugin)
    {
        var panel = new StackPanel { Spacing = 16, Padding = new Thickness(4, 4, 6, 4) };
        panel.Children.Add(PluginTextSection("Descrizione", plugin.Readme));

        if (!string.IsNullOrWhiteSpace(plugin.ReleaseNotes) ||
            !string.IsNullOrWhiteSpace(plugin.Version) ||
            !string.IsNullOrWhiteSpace(plugin.ReleasePublishedAt))
        {
            var title = string.IsNullOrWhiteSpace(plugin.Version)
                ? "Novità"
                : plugin.HasUpdate ? string.Format(T("Novità disponibili {0}"), plugin.Version) : string.Format(T("Novità {0}"), plugin.Version);
            var date = string.IsNullOrWhiteSpace(plugin.ReleasePublishedAt) ? "" : string.Format(T("Disponibile dal {0}"), plugin.ReleasePublishedAt);
            var notes = string.IsNullOrWhiteSpace(plugin.ReleaseNotes)
                ? "Questa versione è disponibile su GitHub."
                : plugin.ReleaseNotes;
            panel.Children.Add(PluginTextSection(title, string.IsNullOrWhiteSpace(date) ? notes : $"{date}\n\n{notes}"));
        }

        return panel;
    }

    private async Task RefreshGamingModeAsync()
    {
        _gamingConfig = await _gamingMode.LoadConfigAsync();
        var changed = false;
        // Xbox Game Bar: verità durevole nelle impostazioni di Playhub (l'agente
        // scarta EnableXboxGameBar dal config.json e lo riporta al default true).
        if (_gamingConfig.Gaming.EnableXboxGameBar != _settings.XboxGameBarEnabled)
        {
            _gamingConfig.Gaming.EnableXboxGameBar = _settings.XboxGameBarEnabled;
            changed = true;
        }
        if (changed)
        {
            await _gamingMode.SaveConfigAsync(_gamingConfig);
        }
        PopulateGamingConfigControls();
        RenderStartupApps();
    }

    // Sicurezza: se un gioco Xbox aveva acceso "apri Game Bar dal controller" e
    // qualcosa è andato storto lasciandola accesa, la rispegniamo all'avvio di
    // Playhub quando nessun gioco Xbox (UWPHook) è in esecuzione. Solo se la
    // feature è attiva: se l'utente l'ha disattivata NON tocchiamo la sua scelta.
    private void ResetXboxGameBarIfStuck()
    {
        try
        {
            if (_gamingConfig?.Gaming is null || !_gamingConfig.Gaming.EnableXboxGameBar)
            {
                return;
            }

            if (Process.GetProcessesByName("UWPHook").Length > 0)
            {
                return; // un gioco Xbox è in corso: non toccare l'impostazione.
            }

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\GameBar", writable: true);
            if (key?.GetValue("UseNexusForGameBarEnabled") is int value && value != 0)
            {
                key.SetValue("UseNexusForGameBarEnabled", 0, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }
        catch
        {
        }
    }

    private async Task SaveGamingConfigAsync()
    {
        _gamingConfig.DefaultMode = GetComboKey(_defaultModeCombo) ?? "Desktop";
        _gamingConfig.Gaming.SteamPath = EmptyToNull(_steamPathBox.Text);
        _gamingConfig.Gaming.SteamArguments = _steamArgsBox.Text;
        _gamingConfig.Gaming.DeckyPath = EmptyToNull(_deckyPathBox.Text);
        _gamingConfig.Gaming.SunshinePath = EmptyToNull(_sunshinePathBox.Text);
        _gamingConfig.Gaming.DelaySteamAfterDeckyMs = (int)_delaySteamBox.Value;
        _gamingConfig.Gaming.AutoHideMouseCursorAfterMs = (int)_mouseDelayBox.Value;
        _gamingConfig.Safety.ApiPort = (int)_apiPortBox.Value;
        _gamingConfig.Gaming.Splash.LogoPath = ResolveSplashLogo();
        _gamingConfig.Gaming.Splash.MinVisibleMs = (int)_splashMinBox.Value;
        _gamingConfig.Gaming.Splash.MaxVisibleMs = (int)_splashMaxBox.Value;
        ReadTogglesIntoConfig();
        try
        {
            await _gamingMode.SaveConfigAsync(_gamingConfig);
            // Salva anche le impostazioni proprie di Playhub (Xbox Game Bar ecc.).
            await _settingsService.SaveAsync();
        }
        catch (Exception ex)
        {
            // Un salvataggio fallito (es. file momentaneamente bloccato) NON deve mai
            // diventare un'eccezione non osservata che chiude l'app.
            Diag.Crash("SaveGamingConfigAsync", ex);
        }
    }

    // Salvataggio istantaneo: chiamato a ogni modifica dei controlli Gaming Mode.
    private void AutoSaveGaming()
    {
        if (_loadingGaming) return;
        _ = SaveGamingConfigAsync();
    }

    // Aggancia il salvataggio automatico a tutti i controlli della Gaming Mode.
    private void WireGamingAutoSave()
    {
        foreach (var toggle in _gamingToggles.Values)
        {
            toggle.Toggled += (_, _) => AutoSaveGaming();
        }
        foreach (var box in new[] { _steamPathBox, _steamArgsBox, _deckyPathBox, _sunshinePathBox, _splashLogoBox })
        {
            box.LostFocus += (_, _) => AutoSaveGaming();
        }
        foreach (var num in new[] { _delaySteamBox, _mouseDelayBox, _apiPortBox, _splashMinBox, _splashMaxBox })
        {
            num.ValueChanged += (_, _) => AutoSaveGaming();
        }
        _splashLogoCombo.SelectionChanged += (_, _) => AutoSaveGaming();
    }


    private async Task ScanUwpGamesAsync()
    {
        _uwpGames.Clear();
        SetStatus("Cerco i giochi Xbox...", InfoBarSeverity.Informational);
        var scannedGames = await _uwpXbox.ScanAsync();
        _uwpXbox.RefreshLibraryState(scannedGames);
        ApplySteamGridDbPreferences(scannedGames);
        foreach (var game in scannedGames)
        {
            _uwpGames.Add(game);
        }

        RenderUwpGames();
        var inLibrary = _uwpGames.Count(game => game.InSteamLibrary);
        SetStatus(string.Format(T("Ho trovato {0} giochi. {1} sono già in libreria."), _uwpGames.Count, inLibrary), InfoBarSeverity.Success);
        _ = LoadUwpCoversAsync(_uwpGames.ToList());
    }

    private async Task ExportUwpGamesAsync()
    {
        var result = await _uwpXbox.ExportSelectedToSteamAsync(_uwpGames, _settings.SteamGridDbApiKey);
        _uwpXbox.RefreshLibraryState(_uwpGames);
        if (result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var game in _uwpGames.Where(game => game.InSteamLibrary))
            {
                game.Selected = false;
            }
        }
        RenderUwpGames();
        SetStatus(result, result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase)
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Warning);
    }

    private async Task RelinkUwpGamesAsync()
    {
        var confirmed = await ConfirmAsync(
            "Ricollegare tutti i giochi Xbox?",
            "Questa operazione potrebbe far riapparire i giochi fuori da eventuali collezioni create o reimpostare alcuni parametri dei plugin legati al gioco, come Launch Curtain, Playhub Metadata e ThemeDeck. Questi parametri dovranno essere configurati nuovamente.");
        if (!confirmed)
        {
            return;
        }

        SetStatus("Cerco i giochi Xbox...", InfoBarSeverity.Informational);
        var scannedGames = await _uwpXbox.ScanAsync();
        _uwpXbox.RefreshLibraryState(scannedGames);
        ApplySteamGridDbPreferences(scannedGames);

        var linkedGames = scannedGames.Where(game => game.InSteamLibrary).ToList();
        if (linkedGames.Count == 0)
        {
            SetStatus("Non ci sono giochi Xbox da ricollegare.", InfoBarSeverity.Warning);
            return;
        }

        foreach (var game in linkedGames)
        {
            game.Selected = true;
        }

        SetStatus("Sto aggiornando i collegamenti e applicando gli artwork mancanti…", InfoBarSeverity.Informational);
        await Task.Yield();
        var result = await _uwpXbox.ExportSelectedToSteamAsync(linkedGames, _settings.SteamGridDbApiKey);
        _uwpXbox.RefreshLibraryState(scannedGames);
        foreach (var game in linkedGames)
        {
            game.Selected = false;
        }

        _uwpGames.Clear();
        foreach (var game in scannedGames)
        {
            _uwpGames.Add(game);
        }

        RenderUwpGames();
        _ = LoadUwpCoversAsync(_uwpGames.ToList());

        if (result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus(
                string.Format(T("Ho ricollegato {0} giochi Xbox. Riavvia Steam per applicare le modifiche."), linkedGames.Count),
                InfoBarSeverity.Success);
            return;
        }

        SetStatus(result, InfoBarSeverity.Warning);
    }

    private async Task ChooseExecutableFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        if (!_settings.ExecutableGameFolders.Contains(folder.Path, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ExecutableGameFolders.Add(folder.Path);
        }
        _settings.ExecutableGamesFolder = "";
        RenderExecutableSources();
        await SaveSettingsSilentlyAsync();
        await ScanExecutableGamesAsync();
    }

    private async Task ScanExecutableGamesAsync()
    {
        if (_executableScanInProgress)
        {
            return;
        }

        var folders = _settings.ExecutableGameFolders
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var files = _settings.ExecutableGameFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (folders.Count == 0 && files.Count == 0)
        {
            SetStatus("Aggiungi prima una cartella o un file da scansionare.", InfoBarSeverity.Warning);
            return;
        }

        _executableScanInProgress = true;
        try
        {
            _executableGames.Clear();
            SetStatus("Cerco i giochi nelle cartelle, nelle sottocartelle e nei file aggiunti...", InfoBarSeverity.Informational);
            var folderResults = await Task.WhenAll(folders.Select(_executableGameService.ScanAsync));
            var fileResults = await Task.WhenAll(files.Select(_executableGameService.CreateEntryAsync));
            var scannedGames = folderResults
                .SelectMany(result => result)
                .Concat(fileResults.Where(game => game is not null).Cast<UwpGameEntry>())
                .GroupBy(game => game.LocalExecutablePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _uwpXbox.RefreshLibraryState(scannedGames);
            ApplySteamGridDbPreferences(scannedGames);
            foreach (var game in scannedGames)
            {
                _executableGames.Add(game);
            }

            RenderExecutableGames();
            var inLibrary = _executableGames.Count(game => game.InSteamLibrary);
            SetStatus(string.Format(T("Ho trovato {0} giochi. {1} sono già in libreria."), _executableGames.Count, inLibrary), InfoBarSeverity.Success);
            _ = LoadGameCoversAsync(_executableGames.ToList(), _executableGames, RenderExecutableGames);
        }
        finally
        {
            _executableScanInProgress = false;
        }
    }

    private async Task ChooseExecutableFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add(".exe");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var game = await _executableGameService.CreateEntryAsync(file.Path);
        if (game is null)
        {
            SetStatus("Non riesco a leggere il file selezionato.", InfoBarSeverity.Warning);
            return;
        }

        if (!_settings.ExecutableGameFiles.Contains(file.Path, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ExecutableGameFiles.Add(file.Path);
        }
        RenderExecutableSources();
        await SaveSettingsSilentlyAsync();
        await ScanExecutableGamesAsync();
    }

    private async Task ExportExecutableGamesAsync()
    {
        var result = await _uwpXbox.ExportSelectedToSteamAsync(_executableGames, _settings.SteamGridDbApiKey);
        _uwpXbox.RefreshLibraryState(_executableGames);
        if (result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var game in _executableGames.Where(game => game.InSteamLibrary))
            {
                game.Selected = false;
            }
        }
        RenderExecutableGames();
        SetStatus(result, result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase)
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Warning);
    }

    private void RenderExecutableSources()
    {
        _executableSourcesPanel.Children.Clear();
        foreach (var folder in _settings.ExecutableGameFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _executableSourcesPanel.Children.Add(BuildExecutableSourceRow(folder, isFolder: true));
        }
        foreach (var file in _settings.ExecutableGameFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _executableSourcesPanel.Children.Add(BuildExecutableSourceRow(file, isFolder: false));
        }
        _executableSourcesPanel.Visibility = _executableSourcesPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private UIElement BuildExecutableSourceRow(string path, bool isFolder)
    {
        var remove = new Button
        {
            Content = new FontIcon { Glyph = ((char)0xE711).ToString(), FontSize = 12 },
            Style = StyleResource("PlayhubSecondaryButtonStyle"),
            Width = 28,
            Height = 28,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6)
        };
        SetLocalizedToolTip(remove, "Rimuovi");
        remove.Click += async (_, _) => await RemoveExecutableSourceAsync(path, isFolder);

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(remove);
        var icon = new FontIcon
        {
            Glyph = ((char)(isFolder ? 0xE8B7 : 0xE8A5)).ToString(),
            FontSize = 15,
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 1);
        row.Children.Add(icon);
        var label = new TextBlock
        {
            Text = path,
            Opacity = (isFolder ? Directory.Exists(path) : File.Exists(path)) ? 0.72 : 0.42,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 2);
        row.Children.Add(label);
        return new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(7),
            Background = ResourceBrush("SubtleFillColorSecondaryBrush", Color.FromArgb(28, 255, 255, 255)),
            Child = row
        };
    }

    private async Task RemoveExecutableSourceAsync(string path, bool isFolder)
    {
        var sources = isFolder ? _settings.ExecutableGameFolders : _settings.ExecutableGameFiles;
        sources.RemoveAll(value => string.Equals(value, path, StringComparison.OrdinalIgnoreCase));
        RenderExecutableSources();
        await SaveSettingsSilentlyAsync();
        if (_settings.ExecutableGameFolders.Count == 0 && _settings.ExecutableGameFiles.Count == 0)
        {
            _executableGames.Clear();
            RenderExecutableGames();
            return;
        }
        await ScanExecutableGamesAsync();
    }

    private void ApplySteamGridDbPreferences(IEnumerable<UwpGameEntry> games)
    {
        foreach (var game in games)
        {
            var key = game.Aumid;
            game.SteamGridDbArtworkDisabled = _settings.SteamGridDbArtworkDisabled
                .Any(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase));
            if (game.SteamGridDbArtworkDisabled)
            {
                game.SteamGridDbGameId = 0;
                ClearSteamGridDbArtwork(game);
                continue;
            }

            foreach (var item in _settings.SteamGridDbGameOverrides)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    game.SteamGridDbGameId = item.Value;
                    break;
                }
            }
            foreach (var item in _settings.SteamGridDbTitleOverrides)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    game.Name = item.Value;
                    break;
                }
            }
        }
    }

    private static void ClearSteamGridDbArtwork(UwpGameEntry game)
    {
        game.SteamGridDbCoverPath = "";
        game.SteamGridDbBannerPath = "";
        game.SteamGridDbHeroPath = "";
        game.SteamGridDbLogoPath = "";
        game.SteamGridDbIconPath = "";
    }

    private static void RemoveSteamGridDbPreferenceKey<T>(Dictionary<string, T> dictionary, string key)
    {
        foreach (var existingKey in dictionary.Keys
                     .Where(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            dictionary.Remove(existingKey);
        }
    }

    private void RenderUwpGames()
    {
        RenderGameCollection(_uwpGames, _uwpGamesPanel);
    }

    private void RenderExecutableGames()
    {
        RenderGameCollection(_executableGames, _executableGamesPanel);
    }

    private void RenderEpicGames() => RenderGameCollection(_epicGames, _epicGamesPanel);

    private void RenderGogGames() => RenderGameCollection(_gogGames, _gogGamesPanel);

    private async Task ScanEpicGamesAsync()
    {
        _epicGames.Clear();
        SetStatus("Cerco i giochi dell'Epic Games Store...", InfoBarSeverity.Informational);
        var scanned = (await _epicService.ScanAsync()).ToList();
        _uwpXbox.RefreshLibraryState(scanned);
        ApplySteamGridDbPreferences(scanned);
        foreach (var game in scanned)
        {
            _epicGames.Add(game);
        }

        RenderEpicGames();
        var inLibrary = _epicGames.Count(game => game.InSteamLibrary);
        SetStatus(string.Format(T("Ho trovato {0} giochi. {1} sono già in libreria."), _epicGames.Count, inLibrary), InfoBarSeverity.Success);
        _ = LoadGameCoversAsync(_epicGames.ToList(), _epicGames, RenderEpicGames);
    }

    private async Task ExportEpicGamesAsync()
    {
        var result = await _uwpXbox.ExportSelectedToSteamAsync(_epicGames, _settings.SteamGridDbApiKey);
        _uwpXbox.RefreshLibraryState(_epicGames);
        if (result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var game in _epicGames.Where(game => game.InSteamLibrary))
            {
                game.Selected = false;
            }
        }

        RenderEpicGames();
        SetStatus(result, result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase)
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Warning);
    }

    private async Task ScanGogGamesAsync()
    {
        _gogGames.Clear();
        SetStatus("Cerco i giochi di GOG...", InfoBarSeverity.Informational);
        var scanned = (await _gogService.ScanAsync()).ToList();
        _uwpXbox.RefreshLibraryState(scanned);
        ApplySteamGridDbPreferences(scanned);
        foreach (var game in scanned)
        {
            _gogGames.Add(game);
        }

        RenderGogGames();
        var inLibrary = _gogGames.Count(game => game.InSteamLibrary);
        SetStatus(string.Format(T("Ho trovato {0} giochi. {1} sono già in libreria."), _gogGames.Count, inLibrary), InfoBarSeverity.Success);
        _ = LoadGameCoversAsync(_gogGames.ToList(), _gogGames, RenderGogGames);
    }

    private async Task ExportGogGamesAsync()
    {
        var result = await _uwpXbox.ExportSelectedToSteamAsync(_gogGames, _settings.SteamGridDbApiKey);
        _uwpXbox.RefreshLibraryState(_gogGames);
        if (result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var game in _gogGames.Where(game => game.InSteamLibrary))
            {
                game.Selected = false;
            }
        }

        RenderGogGames();
        SetStatus(result, result.StartsWith("Ho aggiunto", StringComparison.OrdinalIgnoreCase)
            ? InfoBarSeverity.Success
            : InfoBarSeverity.Warning);
    }

    private void RenderGameCollection(IReadOnlyList<UwpGameEntry> games, StackPanel panel)
    {
        panel.Children.Clear();
        RenderGameCards(games, panel);

        LocalizeElement(panel);

        // La freccia comprimi/espandi compare solo quando ci sono giochi scansionati.
        var chevron = ReferenceEquals(panel, _uwpGamesPanel) ? _uwpChevron
            : ReferenceEquals(panel, _executableGamesPanel) ? _executableChevron
            : ReferenceEquals(panel, _epicGamesPanel) ? _epicChevron
            : ReferenceEquals(panel, _gogGamesPanel) ? _gogChevron
            : null;
        if (chevron is not null)
        {
            chevron.Visibility = games.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RenderGameCards(IReadOnlyList<UwpGameEntry> games, StackPanel panel)
    {
        var columns = GetUwpCardColumnCount(panel.ActualWidth);
        if (ReferenceEquals(panel, _uwpGamesPanel))
        {
            _uwpCardColumnCount = columns;
        }
        else
        {
            _executableCardColumnCount = columns;
        }
        var grid = new Grid
        {
            ColumnSpacing = 14,
            RowSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        for (var column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (var index = 0; index < games.Count; index++)
        {
            var rowIndex = index / columns;
            while (grid.RowDefinitions.Count <= rowIndex)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            var card = BuildUwpGameCard(games[index]);
            Grid.SetColumn(card, index % columns);
            Grid.SetRow(card, rowIndex);
            grid.Children.Add(card);
        }

        panel.Children.Add(grid);
    }

    private Border BuildUwpGameCard(UwpGameEntry game)
    {
        var content = new StackPanel { Spacing = 10 };
        var coverStage = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 48, 48, 52))
        };

        if (!game.SteamGridDbArtworkDisabled && File.Exists(game.SteamGridDbCoverPath))
        {
            coverStage.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(game.SteamGridDbCoverPath)),
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            });
        }
        else if (File.Exists(game.Logo))
        {
            coverStage.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(game.Logo)),
                Width = 112,
                Height = 112,
                Opacity = 1,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            coverStage.Children.Add(new FontIcon
            {
                Glyph = ((char)0xE7FC).ToString(),
                FontSize = 60,
                Opacity = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var check = new CheckBox
        {
            IsChecked = game.Selected,
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        check.Checked += (_, _) => game.Selected = true;
        check.Unchecked += (_, _) => game.Selected = false;
        coverStage.Children.Add(check);

        if (game.InSteamLibrary)
        {
            var badge = BuildInLibraryBadge();
            badge.Margin = new Thickness(10);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Top;
            coverStage.Children.Add(badge);
        }

        var coverFrame = new Border
        {
            CornerRadius = new CornerRadius(9),
            Height = 360,
            Child = coverStage
        };
        coverFrame.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width > 0)
            {
                var targetHeight = args.NewSize.Width * 1.5;
                if (Math.Abs(coverFrame.Height - targetHeight) > 0.5)
                {
                    coverFrame.Height = targetHeight;
                }
            }
        };
        content.Children.Add(coverFrame);
        var pathText = new TextBlock
        {
            Text = game.Aumid,
            FontSize = 12,
            Opacity = 0.64,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        ToolTipService.SetToolTip(pathText, game.Aumid);
        content.Children.Add(pathText);
        content.Children.Add(CreateUwpNameEditor(game));
        var actions = new Grid { ColumnSpacing = 8 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var artworkButton = Button("Artwork", async () => await ShowUwpArtworkDialogAsync(game));
        artworkButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        artworkButton.MinWidth = 0;
        artworkButton.IsEnabled = !game.SteamGridDbArtworkDisabled;
        actions.Children.Add(artworkButton);
        var refetchButton = Button("Cerca di nuovo", async () => await ShowSteamGridDbRefetchDialogAsync(game));
        refetchButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        refetchButton.MinWidth = 0;
        Grid.SetColumn(refetchButton, 1);
        actions.Children.Add(refetchButton);
        content.Children.Add(actions);

        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(235, 35, 35, 39)),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = content
        };
    }

    private static TextBox CreateUwpNameEditor(UwpGameEntry game)
    {
        var editor = new TextBox
        {
            Text = game.Name,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        editor.TextChanged += (_, _) => game.Name = editor.Text;
        return editor;
    }

    private static Border BuildInLibraryBadge()
    {
        var green = Color.FromArgb(255, 16, 124, 16);
        return new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 3),
            Background = new SolidColorBrush(Color.FromArgb(175, 12, 12, 16)),
            BorderBrush = new SolidColorBrush(WithAlpha(green, 150)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "In libreria",
                FontSize = 12,
                Foreground = new SolidColorBrush(green)
            }
        };
    }

    private async Task ShowSteamGridDbRefetchDialogAsync(UwpGameEntry game)
    {
        if (string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey))
        {
            SetStatus("Inserisci prima la chiave API SteamGridDB nella sezione Artwork dei giochi.", InfoBarSeverity.Warning);
            return;
        }

        var searchBox = new TextBox
        {
            Text = game.Name,
            PlaceholderText = T("Cerca titolo"),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var searchButton = Button(T("Cerca"), () => { });
        var removeButton = Button(T("Rimuovi risultato"), () => { });
        searchButton.MinWidth = 0;
        removeButton.MinWidth = 0;
        var removeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        removeRow.Children.Add(removeButton);
        var searchRow = new Grid { ColumnSpacing = 8 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition());
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(searchBox);
        Grid.SetColumn(searchButton, 1);
        searchRow.Children.Add(searchButton);

        var header = new Grid { ColumnSpacing = 12, Margin = new Thickness(12, 4, 12, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        header.Children.Add(new TextBlock { Text = T("Titolo"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var yearHeader = new TextBlock
        {
            Text = T("Anno"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(yearHeader, 1);
        header.Children.Add(yearHeader);

        var results = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        ScrollViewer.SetVerticalScrollBarVisibility(results, ScrollBarVisibility.Hidden);
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        var loading = new ProgressRing
        {
            Width = 40,
            Height = 40,
            IsActive = false,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var empty = new TextBlock
        {
            Text = T("Nessun risultato trovato."),
            Opacity = 0.68,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var resultStage = new Grid { MinHeight = 300 };
        resultStage.Children.Add(results);
        resultStage.Children.Add(empty);
        resultStage.Children.Add(loading);

        var content = new Grid { RowSpacing = 10, Width = 640, Height = 480 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        content.Children.Add(removeRow);
        Grid.SetRow(searchRow, 1);
        content.Children.Add(searchRow);
        Grid.SetRow(header, 2);
        content.Children.Add(header);
        Grid.SetRow(resultStage, 3);
        content.Children.Add(resultStage);

        var dialog = new ContentDialog
        {
            Title = string.Format(T("Cerca di nuovo — {0}"), game.Name),
            Content = content,
            PrimaryButtonText = T("Usa risultato"),
            CloseButtonText = T("Chiudi"),
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        dialog.Resources["ContentDialogMinWidth"] = 720d;
        dialog.Resources["ContentDialogMaxWidth"] = 720d;

        var removeRequested = false;
        var loadVersion = 0;
        async Task LoadResultsAsync()
        {
            var version = ++loadVersion;
            results.Items.Clear();
            dialog.IsPrimaryButtonEnabled = false;
            empty.Visibility = Visibility.Collapsed;
            loading.Visibility = Visibility.Visible;
            loading.IsActive = true;
            IReadOnlyList<SteamGridGameOption> options;
            try
            {
                options = await _uwpXbox.SearchSteamGridDbGamesAsync(searchBox.Text, _settings.SteamGridDbApiKey);
            }
            catch
            {
                options = Array.Empty<SteamGridGameOption>();
            }

            if (version != loadVersion)
            {
                return;
            }

            loading.IsActive = false;
            loading.Visibility = Visibility.Collapsed;
            empty.Visibility = options.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var option in options)
            {
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 8, 4, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.Children.Add(new TextBlock
                {
                    Text = option.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var year = new TextBlock
                {
                    Text = option.ReleaseYear?.ToString() ?? "-",
                    Opacity = option.ReleaseYear.HasValue ? 1 : 0.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(year, 1);
                row.Children.Add(year);
                results.Items.Add(new ListViewItem
                {
                    Content = row,
                    Tag = option,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                });
            }
        }

        results.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = (results.SelectedItem as ListViewItem)?.Tag is SteamGridGameOption;
        searchButton.Click += async (_, _) => await LoadResultsAsync();
        searchBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                await LoadResultsAsync();
            }
        };
        removeButton.Click += (_, _) =>
        {
            removeRequested = true;
            dialog.Hide();
        };
        dialog.Opened += async (_, _) => await LoadResultsAsync();

        ConfigureDialogEntrance(dialog);
        var result = await dialog.ShowAsync();
        if (removeRequested)
        {
            RemoveSteamGridDbPreferenceKey(_settings.SteamGridDbGameOverrides, game.Aumid);
            RemoveSteamGridDbPreferenceKey(_settings.SteamGridDbTitleOverrides, game.Aumid);
            _settings.SteamGridDbArtworkDisabled.RemoveAll(value =>
                string.Equals(value, game.Aumid, StringComparison.OrdinalIgnoreCase));
            _settings.SteamGridDbArtworkDisabled.Add(game.Aumid);
            game.SteamGridDbArtworkDisabled = true;
            game.SteamGridDbGameId = 0;
            ClearSteamGridDbArtwork(game);
            await SaveSettingsSilentlyAsync();
            await _uwpXbox.PopulateApplicationIconsAsync(new[] { game });
            RenderUwpGames();
            RenderExecutableGames();
            RenderEpicGames();
            RenderGogGames();
            SetStatus("Risultato rimosso. Il gioco resterà senza artwork.", InfoBarSeverity.Success);
            return;
        }

        if (result != ContentDialogResult.Primary ||
            (results.SelectedItem as ListViewItem)?.Tag is not SteamGridGameOption selected)
        {
            return;
        }

        RemoveSteamGridDbPreferenceKey(_settings.SteamGridDbGameOverrides, game.Aumid);
        RemoveSteamGridDbPreferenceKey(_settings.SteamGridDbTitleOverrides, game.Aumid);
        _settings.SteamGridDbGameOverrides[game.Aumid] = selected.Id;
        _settings.SteamGridDbTitleOverrides[game.Aumid] = selected.Name;
        _settings.SteamGridDbArtworkDisabled.RemoveAll(value =>
            string.Equals(value, game.Aumid, StringComparison.OrdinalIgnoreCase));
        game.Name = selected.Name;
        game.SteamGridDbGameId = selected.Id;
        game.SteamGridDbArtworkDisabled = false;
        ClearSteamGridDbArtwork(game);
        await SaveSettingsSilentlyAsync();
        var coverLoaded = await _uwpXbox.RefreshSteamGridDbCoverAsync(game, _settings.SteamGridDbApiKey);
        RenderUwpGames();
        RenderExecutableGames();
        RenderEpicGames();
        RenderGogGames();
        SetStatus(
            coverLoaded
                ? string.Format(T("Risultato aggiornato: {0}."), selected.Name)
                : string.Format(T("Risultato aggiornato: {0}. Nessuna copertina disponibile."), selected.Name),
            coverLoaded ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async Task ShowUwpArtworkDialogAsync(UwpGameEntry game)
    {
        if (string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey))
        {
            SetStatus(
                "Aggiungi una chiave SteamGridDB oppure scegli gli artwork ufficiali di Steam.",
                InfoBarSeverity.Informational);
        }

        var selector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
        var categories = new[]
        {
            (Type: "cover", Text: "Copertina", Symbol: Symbol.Library),
            (Type: "banner", Text: "Banner", Symbol: Symbol.Pictures),
            (Type: "hero", Text: "Sfondo", Symbol: Symbol.FullScreen),
            (Type: "logo", Text: "Logo", Symbol: Symbol.Font),
            (Type: "icon", Text: "Icona", Symbol: Symbol.Emoji)
        };
        foreach (var category in categories)
        {
            selector.Items.Add(new SelectorBarItem
            {
                Text = category.Text,
                Icon = new SymbolIcon(category.Symbol),
                Tag = category.Type
            });
        }
        selector.SelectedItem = selector.Items[0];

        var artworkGrid = new GridView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var loading = new ProgressRing
        {
            Width = 42,
            Height = 42,
            IsActive = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var empty = new TextBlock
        {
            Text = T("Nessun artwork disponibile per questa categoria."),
            Opacity = 0.68,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var stage = new Grid { MinHeight = 0 };
        stage.Children.Add(artworkGrid);
        stage.Children.Add(empty);
        stage.Children.Add(loading);

        var sourceButton = new Button
        {
            Style = StyleResource("PlayhubSecondaryButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var header = new Grid { Margin = new Thickness(0, 0, 6, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(selector);
        Grid.SetColumn(sourceButton, 1);
        header.Children.Add(sourceButton);

        var content = new Grid { RowSpacing = 14, Width = 1000, Height = 600 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        content.Children.Add(header);
        Grid.SetRow(stage, 1);
        content.Children.Add(stage);

        var dialog = new ContentDialog
        {
            Title = string.Format(T("Artwork - {0}"), game.Name),
            Content = content,
            PrimaryButtonText = T("Applica"),
            CloseButtonText = T("Chiudi"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = Content.XamlRoot
        };
        dialog.Resources["ContentDialogMinWidth"] = 1100d;
        dialog.Resources["ContentDialogMaxWidth"] = 1100d;

        var selectedArtworks = new Dictionary<string, SteamGridArtworkOption>(StringComparer.OrdinalIgnoreCase);
        var activeArtworkType = "cover";
        var showOfficial = false;

        string CategoryLabel(string artworkType)
            => categories.FirstOrDefault(category => category.Type == artworkType).Text ?? "Cover";

        void UpdateSourceButton()
        {
            var supportsOfficial = !string.Equals(activeArtworkType, "icon", StringComparison.OrdinalIgnoreCase);
            sourceButton.Visibility = supportsOfficial ? Visibility.Visible : Visibility.Collapsed;
            if (!supportsOfficial)
            {
                showOfficial = false;
                return;
            }

            sourceButton.Content = showOfficial
                ? T("Artwork da SteamGridDB")
                : string.Format(T("{0} ufficiale di Steam"), T(CategoryLabel(activeArtworkType)));
        }
        var suppressSelectionChanged = false;
        var loadVersion = 0;

        async Task LoadCategoryAsync(string artworkType)
        {
            var version = ++loadVersion;
            activeArtworkType = artworkType;
            suppressSelectionChanged = true;
            artworkGrid.SelectedItem = null;
            artworkGrid.Items.Clear();
            suppressSelectionChanged = false;
            empty.Visibility = Visibility.Collapsed;
            loading.IsActive = true;
            loading.Visibility = Visibility.Visible;

            IReadOnlyList<SteamGridArtworkOption> artworks;
            try
            {
                artworks = showOfficial
                    ? await _uwpXbox.GetOfficialSteamArtworkAsync(game, artworkType)
                    : await _uwpXbox.GetSteamGridDbArtworkAsync(game, artworkType, _settings.SteamGridDbApiKey);
            }
            catch
            {
                artworks = Array.Empty<SteamGridArtworkOption>();
            }

            if (version != loadVersion)
            {
                return;
            }

            loading.IsActive = false;
            loading.Visibility = Visibility.Collapsed;
            empty.Text = showOfficial
                ? T("Steam non pubblica un artwork ufficiale di questo tipo per questo gioco.")
                : T("Nessun artwork disponibile per questa categoria.");
            empty.Visibility = artworks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var (previewWidth, previewHeight) = ArtworkPreviewSize(artworkType);
            foreach (var artwork in artworks)
            {
                var preview = new StackPanel
                {
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                preview.Children.Add(new Border
                {
                    Width = previewWidth,
                    Height = previewHeight,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromArgb(255, 42, 42, 46)),
                    Child = new Image
                    {
                        Source = new BitmapImage(new Uri(artwork.PreviewUrl)),
                        Stretch = artworkType == "cover" ? Stretch.UniformToFill : Stretch.Uniform
                    }
                });
                var sizeText = artwork.Width > 0 && artwork.Height > 0
                    ? $"{artwork.Width}x{artwork.Height}"
                    : T("Dimensione originale");
                preview.Children.Add(new TextBlock
                {
                    Text = showOfficial ? sizeText + " · Steam" : sizeText,
                    FontSize = 12,
                    Opacity = 0.72,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var item = new GridViewItem
                {
                    Content = preview,
                    Tag = artwork,
                    Padding = new Thickness(0),
                    Margin = new Thickness(3)
                };
                artworkGrid.Items.Add(item);
                if (selectedArtworks.TryGetValue(artworkType, out var selected) &&
                    string.Equals(selected.Url, artwork.Url, StringComparison.OrdinalIgnoreCase))
                {
                    artworkGrid.SelectedItem = item;
                }
            }

            dialog.IsPrimaryButtonEnabled = selectedArtworks.Count > 0;
        }

        artworkGrid.SelectionChanged += (_, _) =>
        {
            if (suppressSelectionChanged)
            {
                return;
            }

            if ((artworkGrid.SelectedItem as GridViewItem)?.Tag is SteamGridArtworkOption selectedArtwork)
            {
                selectedArtworks[activeArtworkType] = selectedArtwork;
            }

            dialog.IsPrimaryButtonEnabled = selectedArtworks.Count > 0;
        };
        sourceButton.Click += async (_, _) =>
        {
            showOfficial = !showOfficial;
            UpdateSourceButton();
            await LoadCategoryAsync(activeArtworkType);
        };
        selector.SelectionChanged += async (_, _) =>
        {
            if (selector.SelectedItem?.Tag is string artworkType)
            {
                showOfficial = false;
                activeArtworkType = artworkType;
                UpdateSourceButton();
                await LoadCategoryAsync(artworkType);
            }
        };
        UpdateSourceButton();
        dialog.Opened += async (_, _) => await LoadCategoryAsync("cover");

        ConfigureDialogEntrance(dialog);
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || selectedArtworks.Count == 0)
        {
            return;
        }

        var appliedImmediately = false;
        foreach (var category in categories)
        {
            if (selectedArtworks.TryGetValue(category.Type, out var selectedArtwork))
            {
                appliedImmediately |= await _uwpXbox.DownloadAndApplySteamGridDbArtworkAsync(game, category.Type, selectedArtwork);
            }
        }
        RenderUwpGames();
        RenderExecutableGames();
        RenderEpicGames();
        RenderGogGames();
        SetStatus(
            appliedImmediately
                ? "Artwork aggiornati. Riavvia Steam per vederli ovunque."
                : "Artwork selezionati. Verranno applicati quando importi il gioco.",
            InfoBarSeverity.Success);
    }

    private static (double Width, double Height) ArtworkPreviewSize(string artworkType)
    {
        return artworkType switch
        {
            "cover" => (150, 225),
            "banner" => (230, 108),
            "hero" => (230, 129),
            "logo" => (210, 110),
            "icon" => (130, 130),
            _ => (150, 225)
        };
    }

    private async Task LoadUwpCoversAsync(IReadOnlyList<UwpGameEntry> scannedGames)
    {
        await LoadGameCoversAsync(scannedGames, _uwpGames, RenderUwpGames);
    }

    private async Task LoadGameCoversAsync(
        IReadOnlyList<UwpGameEntry> scannedGames,
        IEnumerable<UwpGameEntry> currentGames,
        Action render)
    {
        await _uwpXbox.PopulateApplicationIconsAsync(scannedGames);
        if (!string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey))
        {
            await _uwpXbox.PopulateSteamGridDbCoversAsync(scannedGames, _settings.SteamGridDbApiKey);
        }
        var current = currentGames.ToHashSet();
        if (scannedGames.All(current.Contains))
        {
            render();
        }
    }

    private static int GetUwpCardColumnCount(double availableWidth)
    {
        if (availableWidth >= 820) return 4;
        if (availableWidth >= 650) return 4;
        if (availableWidth >= 480) return 3;
        if (availableWidth >= 320) return 2;
        return 1;
    }

    private void PopulateSettingsControls()
    {
        _loadingSettings = true;
        SelectCombo(_themeCombo, _settings.Theme);
        SelectComboKey(_languageCombo, _settings.Language);
        SelectComboKey(_backdropCombo, NormalizeBackdropKey(_settings.Backdrop));
        SelectComboKey(_startupPageCombo, NormalizeStartupPageKey(_settings.StartupPage));
        RenderExecutableSources();
        _deckyPluginsBox.Text = _settings.DeckyPluginsPath;
        _xboxSteamGridDbKeyBox.Password = _settings.SteamGridDbApiKey;
        RefreshAccentPicker();
        _loadingSettings = false;
    }

    private void PopulateGamingConfigControls()
    {
        _loadingGaming = true;
        SelectComboKey(_defaultModeCombo, NormalizeModeKey(_gamingConfig.DefaultMode));
        _steamPathBox.Text = _gamingConfig.Gaming.SteamPath ?? "";
        _steamArgsBox.Text = _gamingConfig.Gaming.SteamArguments;
        _deckyPathBox.Text = _gamingConfig.Gaming.DeckyPath ?? "";
        _sunshinePathBox.Text = _gamingConfig.Gaming.SunshinePath ?? "";
        _delaySteamBox.Value = _gamingConfig.Gaming.DelaySteamAfterDeckyMs;
        _mouseDelayBox.Value = _gamingConfig.Gaming.AutoHideMouseCursorAfterMs;
        _apiPortBox.Value = _gamingConfig.Safety.ApiPort;
        _splashLogoBox.Text = _gamingConfig.Gaming.Splash.LogoPath ?? "";
        _splashMinBox.Value = _gamingConfig.Gaming.Splash.MinVisibleMs;
        _splashMaxBox.Value = _gamingConfig.Gaming.Splash.MaxVisibleMs;
        WriteConfigIntoToggles();
        UpdateModeTiles();
        UpdateLogoPreview();
        _loadingGaming = false;
    }

    private void RenderStartupApps()
    {
        _startupAppsPanel.Children.Clear();
        foreach (var app in _gamingConfig.Gaming.CustomStartupApps.ToList())
        {
            // Componenti interni di Playhub: l'agente li avvia tecnicamente come
            // startup app, ma non sono processi custom dell'utente.
            if (IsPlayhubManagedStartupApp(app.Name))
            {
                continue;
            }

            var row = new StackPanel
            {
                Spacing = 10,
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(10),
                Background = ResourceBrush("SubtleFillColorSecondaryBrush", Color.FromArgb(28, 255, 255, 255))
            };

            // Process name comes from the chosen exe and is not editable.
            row.Children.Add(new TextBlock
            {
                Text = app.Name,
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            row.Children.Add(new TextBlock
            {
                Text = app.Path,
                FontSize = 12,
                Opacity = 0.62,
                TextWrapping = TextWrapping.Wrap
            });

            var args = TextBox("Argomenti (facoltativi)");
            args.Text = app.Arguments;
            args.TextChanged += (_, _) => app.Arguments = args.Text;
            args.LostFocus += (_, _) => AutoSaveGaming();
            row.Children.Add(Labeled("Argomenti", args));

            var enabled = new ToggleSwitch { Header = "Attivo", IsOn = app.Enabled };
            ApplyToggleStateText(enabled);
            var minimized = new ToggleSwitch { Header = "Avvia minimizzato", IsOn = app.StartMinimized };
            ApplyToggleStateText(minimized);
            enabled.Toggled += (_, _) => { app.Enabled = enabled.IsOn; AutoSaveGaming(); };
            minimized.Toggled += (_, _) => { app.StartMinimized = minimized.IsOn; AutoSaveGaming(); };

            row.Children.Add(ActionRow(enabled, minimized, Button("Rimuovi", () =>
            {
                _gamingConfig.Gaming.CustomStartupApps.Remove(app);
                RenderStartupApps();
                AutoSaveGaming();
            })));
            _startupAppsPanel.Children.Add(row);
        }

        LocalizeElement(_startupAppsPanel);
    }

    private static bool IsPlayhubManagedStartupApp(string? name)
    {
        return string.Equals(name, "Playhub Desktop Safety", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Playhub Focus Rescue", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Playhub Xbox Game Bar", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveSplashLogo()
    {
        var selected = GetComboKey(_splashLogoCombo) ?? "custom";
        if (selected == "custom")
        {
            return EmptyToNull(_splashLogoBox.Text);
        }

        var file = selected switch
        {
            "playhub" => Path.Combine(AppPaths.GamingModePackage, "assets", "base-logo.png"),
            "steam-deck" => Path.Combine(AppPaths.GamingModePackage, "assets", "logos", "steam-deck.png"),
            "steamos" => Path.Combine(AppPaths.GamingModePackage, "assets", "logos", "steamos.png"),
            _ => Path.Combine(AppPaths.GamingModePackage, "assets", "logos", selected + ".png")
        };
        return File.Exists(file) ? file : EmptyToNull(_splashLogoBox.Text);
    }

    private void AddToggle(FluentCard panel, string label, string key)
    {
        var toggle = new ToggleSwitch { Header = label };
        ApplyToggleStateText(toggle);
        _gamingToggles[key] = toggle;
        panel.Children.Add(toggle);
    }

    // A toggle row with a title + plain-language explanation on the left and the switch on the right.
    private void AddExplainedToggle(FluentCard card, string title, string description, string key)
    {
        var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        ApplyToggleStateText(toggle);
        _gamingToggles[key] = toggle;

        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        texts.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12.5,
            Opacity = 0.68,
            TextWrapping = TextWrapping.Wrap
        });

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texts, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(texts);
        grid.Children.Add(toggle);
        card.Children.Add(grid);
    }

    private void ApplyToggleStateText(ToggleSwitch toggle)
    {
        toggle.OnContent = T("Attivato");
        toggle.OffContent = T("Disattivato");
    }

    // Lays out cards in equal-width columns, side by side.
    private static Grid CardsRow(params FluentCard[] cards)
    {
        var grid = new Grid { ColumnSpacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var i = 0; i < cards.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(cards[i].Root, i);
            grid.Children.Add(cards[i].Root);
        }
        return grid;
    }

    private void WriteConfigIntoToggles()
    {
        SetToggle("deckyRequired", _gamingConfig.Gaming.DeckyRequired);
        SetToggle("sunshineRequired", _gamingConfig.Gaming.SunshineRequired);
        SetToggle("closeExplorer", _gamingConfig.Gaming.CloseExplorerInGamingMode);
        SetToggle("restoreExplorer", _gamingConfig.Gaming.RestoreExplorerOnDesktop);
        SetToggle("inputCompatibility", _gamingConfig.Gaming.EnsureInputCompatibilityInGamingMode);
        SetToggle("sunshineCompatibility", _gamingConfig.Gaming.EnsureSunshineCompatibilityInGamingMode);
        SetToggle("hideMouse", _gamingConfig.Gaming.AutoHideMouseCursorInGamingMode);
        SetToggle("borderless", _gamingConfig.Gaming.BorderlessFullscreenWindowsInGamingMode);
        _gamingConfig.Gaming.EnableXboxGameBar = _settings.XboxGameBarEnabled;
        SetToggle("xboxGameBar", _settings.XboxGameBarEnabled);
        SetToggle("dashboardEnabled", _gamingConfig.Gaming.DashboardEnabled);
        SetToggle("manageAudio", _gamingConfig.Gaming.ManageAudio);
        SetToggle("remoteApi", _gamingConfig.Safety.AllowRemoteApi);
        SetToggle("restartWithoutPrompt", _gamingConfig.Safety.RestartWithoutPrompt);
    }

    private void ReadTogglesIntoConfig()
    {
        _gamingConfig.Gaming.DeckyRequired = GetToggle("deckyRequired");
        _gamingConfig.Gaming.SunshineRequired = GetToggle("sunshineRequired");
        _gamingConfig.Gaming.CloseExplorerInGamingMode = GetToggle("closeExplorer");
        // Always restore the desktop in Desktop Mode (no toggle: prevents users
        // getting stuck without Explorer).
        _gamingConfig.Gaming.RestoreExplorerOnDesktop = true;
        _gamingConfig.Gaming.EnsureInputCompatibilityInGamingMode = GetToggle("inputCompatibility");
        _gamingConfig.Gaming.EnsureSunshineCompatibilityInGamingMode = GetToggle("sunshineCompatibility");
        _gamingConfig.Gaming.AutoHideMouseCursorInGamingMode = GetToggle("hideMouse");
        _gamingConfig.Gaming.BorderlessFullscreenWindowsInGamingMode = GetToggle("borderless");
        _gamingConfig.Gaming.EnableXboxGameBar = GetToggle("xboxGameBar");
        _settings.XboxGameBarEnabled = GetToggle("xboxGameBar");
        _gamingConfig.Gaming.DashboardEnabled = GetToggle("dashboardEnabled");
        _gamingConfig.Gaming.ManageAudio = GetToggle("manageAudio");
        _gamingConfig.Safety.AllowRemoteApi = GetToggle("remoteApi");
        _gamingConfig.Safety.RestartWithoutPrompt = true;
    }

    private void SetToggle(string key, bool value)
    {
        if (_gamingToggles.TryGetValue(key, out var toggle))
        {
            toggle.IsOn = value;
        }
    }

    private bool GetToggle(string key) => _gamingToggles.TryGetValue(key, out var toggle) && toggle.IsOn;

    private StackPanel Page(string tag, string title, string subtitle = "")
    {
        var content = PageWithoutHeader(tag);
        content.Children.Add(BuildPageHeader(tag, title, subtitle));
        return content;
    }

    private static StackPanel PageWithoutHeader(string tag)
    {
        return new StackPanel
        {
            Spacing = 18,
            Padding = (Thickness)(Application.Current.Resources.TryGetValue("PlayhubPagePadding", out var value) && value is Thickness thickness
                ? thickness
                : new Thickness(36, 24, 36, 64)),
            Tag = tag,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private FrameworkElement BuildPageHeader(string tag, string title, string subtitle)
    {
        var asset = tag switch
        {
            "decky" => "decky-installation-onboarding.png",
            "gaming" => "gaming-mode-page-header.png",
            "xbox" => "import-games-onboarding.png",
            "styler" => "big-picture-styler-page-header.png",
            "settings" => "settings-page-header.png",
            _ => string.Empty
        };

        var text = new StackPanel
        {
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = 610
        };
        text.Children.Add(new TextBlock
        {
            Text = title,
            Style = StyleResource("PlayhubPageTitleStyle"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                Style = StyleResource("PlayhubBodyTextStyle"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var grid = new Grid
        {
            ColumnSpacing = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 300,
            Tag = "page-mascot-header"
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width > 0)
                grid.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(args.NewSize.Width * 0.3, 200, 260));
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Welcome", "Mascots", asset);
        if (File.Exists(path))
        {
            var image = new Image
            {
                Source = new BitmapImage(new Uri(path)),
                MaxWidth = 300,
                Height = 300,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(-36, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            Grid.SetColumn(image, 0);
            grid.Children.Add(image);
        }

        return grid;
    }

    private static FluentCard Card()
    {
        return new FluentCard();
    }

    private TextBlock SectionTitle(string text) => LocalizedText(new TextBlock
    {
        Text = text,
        Style = StyleResource("PlayhubSectionTitleStyle")
    }, text);

    private TextBlock GroupTitle(string text) => LocalizedText(new TextBlock
    {
        Text = text,
        FontSize = 14,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 0),
        Foreground = ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(210, 255, 255, 255))
    }, text);

    private TextBlock Body(string text) => LocalizedText(new TextBlock
    {
        Text = text,
        Style = StyleResource("PlayhubBodyTextStyle")
    }, text);

    private static StackPanel ActionRow(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var child in children)
        {
            row.Children.Add(child);
        }
        return row;
    }

    private Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle") };
        RegisterButton(button, primary);
        button.Click += (_, _) => { using var context = BeginNotificationContext(); action(); };
        return button;
    }

    private Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = text, Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle") };
        RegisterButton(button, primary);
        button.Click += async (_, _) =>
        {
            using var context = BeginNotificationContext();
            using var reminderOperation = BeginSupportReminderOperation();
            try
            {
                button.IsEnabled = false;
                await action();
            }
            catch (Exception ex)
            {
                SetStatus(FriendlyError(ex), InfoBarSeverity.Error);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private UIElement IconContent(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(LocalizedText(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }, text));
        return row;
    }

    private Button IconButton(string glyph, string text, Action action, bool primary = false)
    {
        var button = new Button { Content = IconContent(glyph, text), Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle") };
        RegisterButton(button, primary);
        button.Click += (_, _) => { using var context = BeginNotificationContext(); action(); };
        return button;
    }

    private Button IconButton(string glyph, string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = IconContent(glyph, text), Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle") };
        RegisterButton(button, primary);
        button.Click += async (_, _) =>
        {
            using var context = BeginNotificationContext();
            using var reminderOperation = BeginSupportReminderOperation();
            try
            {
                button.IsEnabled = false;
                await action();
            }
            catch (Exception ex)
            {
                SetStatus(FriendlyError(ex), InfoBarSeverity.Error);
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    // The GitHub "mark" (octocat) as a 16x16 vector path.
    private const string GitHubMarkPath =
        "M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66.07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56.82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z";

    private Button GitHubButton(Func<Task> action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };

        var mark = (UIElement)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<PathIcon xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\"" + GitHubMarkPath + "\"/>");
        row.Children.Add(new Viewbox
        {
            Width = 15,
            Height = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Child = mark
        });
        row.Children.Add(new TextBlock { Text = "GitHub", VerticalAlignment = VerticalAlignment.Center });

        var button = new Button { Content = row, Style = StyleResource("PlayhubSecondaryButtonStyle") };
        RegisterButton(button, false);
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                SetStatus(FriendlyError(ex), InfoBarSeverity.Error);
            }
        };
        return button;
    }

    private void RegisterButton(Button button, bool primary)
    {
        LocalizeElement(button);
        if (!primary)
        {
            return;
        }

        _primaryButtons.Add(new WeakReference<Button>(button));
        ApplyAccentToButton(button);
    }

    private FrameworkElement Labeled(string label, FrameworkElement element)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                LocalizedText(new TextBlock { Text = label, Opacity = 0.72 }, label),
                element
            }
        };
    }

    private static Grid TwoColumn(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Grid ThreeColumn(FrameworkElement one, FrameworkElement two, FrameworkElement three)
    {
        var grid = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(two, 1);
        Grid.SetColumn(three, 2);
        grid.Children.Add(one);
        grid.Children.Add(two);
        grid.Children.Add(three);
        return grid;
    }

    private static TextBox TextBox(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        MinWidth = 220
    };

    private static ComboBox Combo(params string[] items)
    {
        var combo = new ComboBox { MinWidth = 220 };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }
        combo.SelectedIndex = 0;
        return combo;
    }

    private ComboBox LanguageCombo()
    {
        var combo = new ComboBox { MinWidth = 220 };
        RefreshLanguageCombo(combo, _settings.Language);
        return combo;
    }

    private ComboBox ChoiceCombo(params ComboOption[] options)
    {
        var combo = new ComboBox { MinWidth = 220, Tag = options };
        RefreshChoiceCombo(combo, options.Length > 0 ? options[0].Key : "");
        return combo;
    }

    private void RefreshLanguageCombo(ComboBox combo, string? selectedKey = null)
    {
        var wanted = LocalizationService.NormalizeLanguageKey(selectedKey ?? GetComboKey(combo) ?? "en");
        combo.Items.Clear();
        foreach (var language in LocalizationService.Languages)
        {
            combo.Items.Add(new ComboChoice(language.Key, language.NativeName,
                LocalizationService.LanguageDisplayName(language.Key, _settings.Language)));
        }

        SelectComboKey(combo, wanted);
    }

    private void RefreshChoiceCombo(ComboBox combo, string? selectedKey = null)
    {
        if (combo.Tag is not ComboOption[] options)
        {
            return;
        }

        var wanted = selectedKey ?? GetComboKey(combo) ?? (options.Length > 0 ? options[0].Key : "");
        combo.Items.Clear();
        foreach (var option in options)
        {
            combo.Items.Add(new ComboChoice(option.Key, option.LabelKey, T(option.LabelKey)));
        }

        SelectComboKey(combo, wanted);
    }

    private static string? GetComboKey(ComboBox combo)
    {
        return combo.SelectedItem switch
        {
            ComboChoice choice => choice.Key,
            string text => text,
            _ => combo.SelectedItem?.ToString()
        };
    }

    private static void SelectComboKey(ComboBox combo, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
            return;
        }

        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboChoice choice &&
                string.Equals(choice.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }

            if (string.Equals(combo.Items[i]?.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static NumberBox Number(string header, double min, double max)
    {
        return new NumberBox
        {
            Header = header,
            Minimum = min,
            Maximum = max,
            SmallChange = 100,
            LargeChange = 1000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            MinWidth = 180
        };
    }

    private static FrameworkElement NumberWithHint(NumberBox box, string hint)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(box);
        stack.Children.Add(new TextBlock { Text = hint, FontSize = 12, Opacity = 0.66, TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    private StackPanel BuildAccentPicker(bool welcome = false)
    {
        var panel = new StackPanel { Spacing = 10 };
        StackPanel? row = null;
        var index = 0;
        foreach (var color in new[]
        {
            "#FFCB0F", "#0F6CBD", "#107C10", "#C50F1F", "#8764B8",
            "#E97A9D", "#7DDCB5", "#73BCEB", "#B79AE8", "#F2A36F"
        })
        {
            if (welcome && index == 10) break;
            if (index++ % 10 == 0)
            {
                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                panel.Children.Add(row);
            }
            var button = new Button
            {
                Tag = color,
                Width = 44,
                Height = 34,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                Content = new Border
                {
                    Width = 26,
                    Height = 18,
                    Background = new SolidColorBrush(ParseColor(color))
                }
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, color);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, color);
            button.Click += async (_, _) =>
            {
                _settings.AccentColor = color;
                ApplyTheme();
                await SaveSettingsSilentlyAsync();
            };
            row!.Children.Add(button);
            _accentSwatches.Add(button);
        }

        return panel;
    }

    private void RefreshAccentPicker()
    {
        foreach (var button in _accentSwatches)
        {
            var selected = string.Equals(button.Tag?.ToString(), _settings.AccentColor, StringComparison.OrdinalIgnoreCase);
            button.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            button.BorderBrush = selected
                ? new SolidColorBrush(ParseColor(_settings.AccentColor))
                : ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(80, 128, 128, 128));
        }
        RefreshWelcomeBackdrop();
    }

    private void RefreshWelcomeBackdrop()
    {
        var selected = NormalizeBackdropKey(_settings.Backdrop);
        var accent = ParseColor(_settings.AccentColor);
        foreach (var button in _welcomeBackdropButtons)
        {
            var active = Equals(button.Tag, selected);
            var foreground = active
                ? (NeedsLightForeground(accent) ? Colors.White : Colors.Black)
                : Color.FromArgb(220, 255, 255, 255);
            button.Background = WelcomeBackdropBrush(button, "ButtonBackground", active ? accent : Colors.Transparent);
            button.Foreground = WelcomeBackdropBrush(button, "ButtonForeground", foreground);
            WelcomeBackdropBrush(button, "ButtonBackgroundPointerOver", active ? Mix(accent, Colors.White, 0.12) : Color.FromArgb(28, 255, 255, 255));
            WelcomeBackdropBrush(button, "ButtonBackgroundPressed", active ? Mix(accent, Colors.Black, 0.12) : Color.FromArgb(42, 255, 255, 255));
            WelcomeBackdropBrush(button, "ButtonForegroundPointerOver", foreground);
            WelcomeBackdropBrush(button, "ButtonForegroundPressed", foreground);
        }
    }

    private static SolidColorBrush WelcomeBackdropBrush(Button button, string key, Color color)
    {
        // Resource lookup can return a shared theme brush. Animate only brushes owned by this button.
        var brushes = WelcomeBackdropBrushes.GetOrCreateValue(button);
        if (brushes.TryGetValue(key, out var brush))
        {
            if (brush.Color != color) AnimateBrushColor(brush, color);
            return brush;
        }

        var created = new SolidColorBrush(color);
        brushes.Add(key, created);
        button.Resources[key] = created;
        return created;
    }

    private FrameworkElement PluginImage(DeckyPluginInfo plugin, double width, double height)
    {
        var source = string.IsNullOrWhiteSpace(plugin.CoverImage) ? plugin.Image : plugin.CoverImage;
        if (!string.IsNullOrWhiteSpace(source) && Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            try
            {
                return new Image { Source = new BitmapImage(uri), Width = width, Height = height, Stretch = Stretch.UniformToFill };
            }
            catch
            {
            }
        }

        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(8),
            Background = ResourceBrush("SubtleFillColorSecondaryBrush", Color.FromArgb(32, 255, 255, 255))
        };
    }

    private static string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private async Task CheckPlayhubUpdatesAsync()
    {
        if (_playhubUpdateRunning)
        {
            if (_playhubUpdateDialogInfo is not null) ShowPlayhubUpdateDialog(_playhubUpdateDialogInfo, force: true);
            return;
        }
        _playhubUpdateButton.IsEnabled = false;
        _playhubUpdateStatus.Visibility = Visibility.Visible;
        _playhubUpdateStatus.Text = "Cerco aggiornamenti…";
        try
        {
            var info = await _updateService.CheckAsync(PlayhubUpdatePolicy.Repository(_settings.PlayhubUpdateRepository), GetAppVersion(), PlayhubUpdatePolicy.ReleaseTag);

            if (info is null)
            {
                _playhubUpdateStatus.Text = "Non riesco a contattare GitHub. Riprova tra poco.";
                SetStatus("Non riesco a contattare GitHub per gli aggiornamenti. Riprova tra poco.", InfoBarSeverity.Warning);
                return;
            }

            if (PlayhubUpdatePolicy.ShouldOffer(info))
            {
                _playhubUpdateStatus.Text = $"Playhub {info.LatestVersion} disponibile";
                ShowPlayhubUpdateDialog(info, force: true);
            }
            else
            {
                _playhubUpdateStatus.Text = "Playhub è già aggiornato.";
                SetStatus("Playhub è aggiornato.", InfoBarSeverity.Success);
            }
        }
        finally
        {
            if (!_playhubUpdateRunning) _playhubUpdateButton.IsEnabled = true;
        }
    }

    private async Task DownloadAndInstallUpdateAsync(PlayhubUpdateService.UpdateInfo info)
    {
        if (_playhubUpdateRunning) return;
        _playhubUpdateRunning = true;
        _playhubUpdateButton.IsEnabled = true;
        _playhubUpdateBar.Visibility = Visibility.Collapsed;
        _playhubUpdateStatus.Visibility = Visibility.Collapsed;
        UpdatePlayhubUpdateDialogProgress(info.DownloadSize > 0 ? 0 : null, $"Scarico Playhub {info.LatestVersion}…");

        try
        {
            var progress = new Progress<PlayhubUpdateService.DownloadProgress>(value =>
            {
                var receivedMb = value.BytesReceived / 1024d / 1024d;
                var status = value.TotalBytes > 0
                    ? $"Scaricati {receivedMb:0.0} di {value.TotalBytes / 1024d / 1024d:0.0} MB ({value.Fraction:P0})"
                    : $"Scaricati {receivedMb:0.0} MB";
                UpdatePlayhubUpdateDialogProgress(value.TotalBytes > 0 ? value.Fraction : null, status);
            });
            var installer = await _updateService.DownloadInstallerAsync(info, progress);
            UpdatePlayhubUpdateDialogProgress(1, "Download completato. Apro l'installer…");

            Process.Start(new ProcessStartInfo(installer, "--update")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installer) ?? ""
            });
            await Task.Delay(500);
            Close();
        }
        catch (Exception ex)
        {
            _playhubUpdateRunning = false;
            _playhubUpdateButton.IsEnabled = true;
            Diag.Crash("DownloadAndInstallUpdateAsync", ex);
            UpdatePlayhubUpdateDialogProgress(null, "Non riesco ad aggiornare Playhub. Riprova.", failed: true);
        }
    }

    // Controllo silenzioso all'avvio: notifica SOLO se c'è una versione nuova,
    // senza disturbare con messaggi di "tutto a posto" o errori di rete.
    private async Task CheckPlayhubUpdatesSilentlyAsync()
    {
        try
        {
            var info = await _updateService.CheckAsync(PlayhubUpdatePolicy.Repository(_settings.PlayhubUpdateRepository), GetAppVersion(), PlayhubUpdatePolicy.ReleaseTag);
            if (info is not null && PlayhubUpdatePolicy.ShouldOffer(info))
            {
                ShowUpdateNotification(info);
                return;
            }
        }
        catch
        {
            // L'avvio non deve mai fallire per il controllo aggiornamenti.
        }

        ShowPluginUpdatesNotification();
    }

    private void ShowPluginUpdatesNotification()
    {
        var updates = _plugins
            .Where(plugin => plugin.IsInstalled && plugin.HasUpdate)
            .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (updates.Count == 0)
        {
            return;
        }

        _status.Tag = "update-notification";
        _status.Title = T(updates.Count == 1
            ? "Aggiornamento plugin disponibile"
            : "Aggiornamenti plugin disponibili");
        _status.Message = string.Join(" · ", updates.Select(plugin =>
            string.IsNullOrWhiteSpace(plugin.Version) ? plugin.Name : $"{plugin.Name} {plugin.Version}"));
        _status.Severity = InfoBarSeverity.Success;

        var openStore = new Button
        {
            Content = T("Apri Plugin Store"),
            Style = StyleResource("PlayhubPrimaryButtonStyle")
        };
        openStore.Click += (_, _) =>
        {
            var storeItem = _navigation.MenuItems.OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, "plugins", StringComparison.Ordinal));
            if (storeItem is not null)
            {
                _navigation.SelectedItem = storeItem;
            }
            SwitchPluginStoreMode("manage");
            _status.IsOpen = false;
        };
        _status.ActionButton = openStore;
        _status.IsOpen = true;
    }

    private void ShowUpdateNotification(PlayhubUpdateService.UpdateInfo info)
    {
        ShowPlayhubUpdateDialog(info);
    }

    private async Task SaveSettingsSilentlyAsync()
    {
        CaptureSupportReminderUsageForSave();
        await _settingsService.SaveAsync();
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
        if (IsStoreNotificationContext())
        {
            Diag.Step("Plugin Store: " + message);
            return;
        }
        _status.Tag = null;
        _localizationKeys.AddOrUpdate(_status, message);
        // Ripulisci eventuali titolo/pulsante lasciati da una notifica precedente
        // (es. quella di aggiornamento), così i messaggi normali restano puliti.
        _status.Title = "";
        _status.ActionButton = null;
        _status.Message = TranslateMessage(message);

        // Success already has a clear Fluent green surface. The other default
        // InfoBar surfaces are translucent and can blend into page headings when
        // this control is shown as an overlay, so give them an opaque dark tint.
        if (severity == InfoBarSeverity.Success)
        {
            _status.ClearValue(Control.BackgroundProperty);
            _status.ClearValue(Control.BorderBrushProperty);
        }
        else
        {
            var surface = severity switch
            {
                InfoBarSeverity.Warning => Color.FromArgb(255, 55, 48, 29),
                InfoBarSeverity.Error => Color.FromArgb(255, 58, 34, 37),
                _ => Color.FromArgb(255, 45, 45, 49)
            };
            var outline = severity switch
            {
                InfoBarSeverity.Warning => Color.FromArgb(255, 126, 105, 45),
                InfoBarSeverity.Error => Color.FromArgb(255, 126, 61, 68),
                _ => Color.FromArgb(255, 75, 75, 81)
            };
            _status.Background = new SolidColorBrush(surface);
            _status.BorderBrush = new SolidColorBrush(outline);
        }

        _status.Severity = severity;
        _status.IsOpen = true;
    }

    private string T(string text) => LocalizationService.Translate(_settings.Language, text);

    private bool RestartPlayhub()
    {
        try
        {
            // The SDK agent waits for this process to exit before relaunching,
            // so single-instance activation cannot redirect back to this window.
            var failure = Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
            if (failure == Windows.ApplicationModel.Core.AppRestartFailureReason.RestartPending)
                return true;
            Diag.Step("Language restart failed: " + failure);
        }
        catch (Exception ex)
        {
            Diag.Crash("Language restart", ex);
        }
        SetStatus("Non riesco a riavviare Playhub. Chiudilo e riaprilo manualmente.", InfoBarSeverity.Warning);
        return false;
    }

    private string TranslateMessage(string message)
    {
        if (message.StartsWith("Ho aggiunto ", StringComparison.Ordinal) &&
            message.Contains(" giochi a Steam.", StringComparison.Ordinal))
        {
            var countText = message["Ho aggiunto ".Length..].Split(' ', 2)[0];
            return string.Format(T("Ho aggiunto {0} giochi a Steam. Riavvia Steam per vederli."), countText);
        }

        const string blockedPrefix = "Windows ha impedito la scrittura del file shortcuts di Steam. Non dipende dal fatto che Steam sia aperto: è la protezione \"Accesso alle cartelle controllato\" di Sicurezza di Windows che blocca questa app (UWPHook funziona perché è già tra le app consentite). Per risolvere: Sicurezza di Windows → Protezione da virus e minacce → Gestisci protezione ransomware → Accesso alle cartelle controllato → Consenti app tramite Accesso alle cartelle controllato → Aggiungi Playhub.exe. Poi riprova.";
        if (message.StartsWith(blockedPrefix, StringComparison.Ordinal))
        {
            var translated = T(blockedPrefix);
            const string marker = "(File bloccato: ";
            var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var blockedFile = message[(markerIndex + marker.Length)..].TrimEnd(')');
                translated += " " + string.Format(T("File bloccato: {0}"), blockedFile);
            }
            return translated;
        }

        const string installablePrefix = "Non trovo i file installabili per ";
        if (message.StartsWith(installablePrefix, StringComparison.Ordinal) && message.EndsWith(".", StringComparison.Ordinal))
        {
            var pluginName = message[installablePrefix.Length..^1];
            return string.Format(T("Non trovo i file installabili per {0}."), pluginName);
        }

        // Messaggio di installazione DeckyLoader composto a runtime: "DeckyLoader
        // installato ({label}): {nota, nota}. Chiudi e riapri Steam...". Va tradotto
        // a pezzi (template + etichetta + singole note).
        const string deckyInstalledPrefix = "DeckyLoader installato (";
        const string deckyInstalledTail = ". Chiudi e riapri Steam per attivare DeckyLoader.";
        if (message.StartsWith(deckyInstalledPrefix, StringComparison.Ordinal) &&
            message.EndsWith(deckyInstalledTail, StringComparison.Ordinal))
        {
            var labelEnd = message.IndexOf("): ", StringComparison.Ordinal);
            if (labelEnd > 0)
            {
                var label = message[deckyInstalledPrefix.Length..labelEnd];
                var notesPart = message[(labelEnd + 3)..^deckyInstalledTail.Length];
                var notes = notesPart.Split(", ", StringSplitOptions.None).Select(n => T(n));
                return string.Format(
                    T("DeckyLoader installato ({0}): {1}. Chiudi e riapri Steam per attivare DeckyLoader."),
                    T(label), string.Join(", ", notes));
            }
        }

        return TranslatePrefix(message, "Rimozione non riuscita: ") ??
               TranslatePrefix(message, "Installazione del plugin non riuscita: ") ??
               TranslatePrefix(message, "Rimozione del plugin non riuscita: ") ??
               TranslatePrefix(message, "Windows ha impedito la scrittura del file shortcuts di Steam. Consenti Playhub in Accesso alle cartelle controllato e riprova. ") ??
               T(message);
    }

    private string? TranslatePrefix(string message, string prefix)
    {
        return message.StartsWith(prefix, StringComparison.Ordinal)
            ? T(prefix) + message[prefix.Length..]
            : null;
    }

    private void ApplyLanguage()
    {
        _loadingSettings = true;
        RefreshLanguageCombo(_languageCombo, _settings.Language);
        RefreshChoiceCombo(_backdropCombo, NormalizeBackdropKey(_settings.Backdrop));
        RefreshChoiceCombo(_startupPageCombo, NormalizeStartupPageKey(_settings.StartupPage));
        RefreshChoiceCombo(_defaultModeCombo, NormalizeModeKey(_gamingConfig.DefaultMode));
        RefreshChoiceCombo(_splashLogoCombo, GetComboKey(_splashLogoCombo) ?? "playhub");
        _loadingSettings = false;

        if (Content is DependencyObject root)
        {
            LocalizeElement(root);
        }

        foreach (var item in _navigation.MenuItems.OfType<DependencyObject>())
        {
            LocalizeElement(item);
        }

        if (!string.IsNullOrWhiteSpace(_status.Message))
        {
            _status.Message = _localizationKeys.TryGetValue(_status, out var statusKey)
                ? TranslateMessage(statusKey)
                : TranslateMessage(_status.Message);
        }

        // Invalidate localized descriptions only when the language changes;
        // hidden store views are built on demand when their page is opened.
        if (!string.Equals(_pluginViewLanguage, _settings.Language, StringComparison.Ordinal))
        {
            _pluginViewLanguage = _settings.Language;
            InvalidatePluginAllViews();
            InvalidateFeaturedFrames();
            _pluginCardsDirty = true;
            _pluginManagementDirty = true;
            RenderVisiblePluginView();
        }

        // Aggiorna la slide di benvenuto: la prima è costruita prima del load della
        // lingua, quindi senza questo resterebbe nella lingua di default.
        _refreshWelcomeSlide?.Invoke();
    }

    private string FriendlyError(Exception ex)
    {
        Diag.Crash("Azione dell'interfaccia non riuscita", ex);

        if (ex is UnauthorizedAccessException)
        {
            return T("Windows ha bloccato l'accesso a un file. Riprova.");
        }

        return T("Qualcosa non ha funzionato. Riprova.");
    }

    private static string NormalizeBackdropKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "acrylic";
        }

        var key = value.Trim().ToLowerInvariant();
        return key switch
        {
            "mica" => "mica",
            "acrylic" => "acrylic",
            "sfondo pieno" or "sfondopieno" or "solid" or "solidbackground" => "solid",
            _ => "acrylic"
        };
    }

    private static string NormalizeStartupPageKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "decky";
        }

        var key = value.Trim().ToLowerInvariant();
        return key switch
        {
            "decky" or "deckyloader" => "decky",
            "plugins" or "playhub plugin store" => "plugins",
            "gaming" or "gaming mode" => "gaming",
            "xbox" or "importa giochi xbox" => "xbox",
            "styler" or "big picture styler" => "styler",
            "settings" or "impostazioni" => "settings",
            _ => "decky"
        };
    }

    private static string NormalizeModeKey(string? value)
    {
        return string.Equals(value, "Gaming", StringComparison.OrdinalIgnoreCase) ? "Gaming" : "Desktop";
    }

    private static void SelectCombo(ComboBox combo, string value)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Style? StyleResource(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is Style style ? style : null;
    }

    private static Brush ResourceBrush(string key, Color fallback)
    {
        try
        {
            return Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : new SolidColorBrush(fallback);
        }
        catch
        {
            return new SolidColorBrush(fallback);
        }
    }

    private static Color ParseColor(string? hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return Colors.DeepSkyBlue;
            }

            var value = hex.TrimStart('#');
            var r = Convert.ToByte(value[0..2], 16);
            var g = Convert.ToByte(value[2..4], 16);
            var b = Convert.ToByte(value[4..6], 16);
            return Color.FromArgb(255, r, g, b);
        }
        catch
        {
            return Colors.DeepSkyBlue;
        }
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color source, Color target, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(source.R + ((target.R - source.R) * amount)),
            (byte)Math.Round(source.G + ((target.G - source.G) * amount)),
            (byte)Math.Round(source.B + ((target.B - source.B) * amount)));
    }

    private static bool NeedsLightForeground(Color color)
    {
        var luminance = (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);
        return luminance < 150;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void ApplyTheme()
    {
        var accent = ParseColor(_settings.AccentColor);
        ApplyAccentResources(accent);

        if (Content is FrameworkElement element)
        {
            element.RequestedTheme = ElementTheme.Dark;
        }

        _primaryButtons.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in _primaryButtons)
        {
            if (reference.TryGetTarget(out var button)) ApplyAccentToButton(button);
        }

        ApplyChrome(accent);
        RefreshAccentPicker();
        // Re-tint the Gaming Mode mode tiles (border/background/icons) with the new accent.
        UpdateModeTiles();
        UpdatePluginStoreModeButtons();
    }

    private static void AnimateStopColor(GradientStop stop, Color to)
    {
        try
        {
            var animation = new ColorAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(320)),
                EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, stop);
            Storyboard.SetTargetProperty(animation, "Color");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
        catch
        {
            stop.Color = to;
        }
    }

    private void ApplyBackdrop()
    {
        RefreshWelcomeBackdrop();
        try
        {
            SystemBackdrop = _settings.Backdrop switch
            {
                "acrylic" or "Acrylic" => new DesktopAcrylicBackdrop(),
                "solid" or "Sfondo pieno" => null,
                _ => new MicaBackdrop()
            };
        }
        catch
        {
        }
    }

    private void ApplyChrome(Color accent)
    {
        var text = Colors.White;
        // Always transparent so it matches the page/backdrop behind it (any "Sfondo").
        _titleBar.Background = new SolidColorBrush(Colors.Transparent);
        _titleBarText.Foreground = new SolidColorBrush(text);
        _titleBarAccent.Background = new SolidColorBrush(accent);
        ApplySystemTitleBarColors(text, Color.FromArgb(30, 255, 255, 255), Color.FromArgb(46, 255, 255, 255));
    }

    private void ApplySystemTitleBarColors(Color foreground, Color hover, Color pressed)
    {
        if (_appWindow is null)
        {
            return;
        }

        try
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.InactiveBackgroundColor = Colors.Transparent;
            titleBar.ForegroundColor = foreground;
            titleBar.InactiveForegroundColor = WithAlpha(foreground, 128);
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = WithAlpha(foreground, 128);
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonPressedForegroundColor = foreground;
        }
        catch
        {
        }
    }

    private void ApplyAccentResources(Color accent)
    {
        var hover = Mix(accent, Colors.White, 0.16);
        var pressed = Mix(accent, Colors.Black, 0.18);
        var subtle = WithAlpha(accent, 44);
        var subtleHover = WithAlpha(accent, 62);
        var subtlePressed = WithAlpha(accent, 80);
        var onAccent = NeedsLightForeground(accent) ? Colors.White : Colors.Black;
        var disabledAccent = WithAlpha(accent, 92);

        SetResource("SystemAccentColor", accent);
        SetResource("SystemAccentColorLight1", Mix(accent, Colors.White, 0.22));
        SetResource("SystemAccentColorLight2", Mix(accent, Colors.White, 0.38));
        SetResource("SystemAccentColorLight3", Mix(accent, Colors.White, 0.54));
        SetResource("SystemAccentColorDark1", Mix(accent, Colors.Black, 0.18));
        SetResource("SystemAccentColorDark2", Mix(accent, Colors.Black, 0.32));
        SetResource("SystemAccentColorDark3", Mix(accent, Colors.Black, 0.46));

        SetBrush("AccentFillColorDefaultBrush", accent);
        SetBrush("AccentFillColorSecondaryBrush", hover);
        SetBrush("AccentFillColorTertiaryBrush", pressed);
        SetBrush("AccentFillColorDisabledBrush", disabledAccent);
        SetBrush("TextOnAccentFillColorPrimaryBrush", onAccent);
        SetBrush("TextOnAccentFillColorSecondaryBrush", WithAlpha(onAccent, 210));
        SetBrush("TextOnAccentFillColorDisabledBrush", WithAlpha(onAccent, 130));
        SetResource("TextOnAccentFillColorPrimary", onAccent);

        SetBrush("NavigationViewSelectionIndicatorForeground", accent);
        SetBrush("NavigationViewItemForegroundSelected", Colors.White);
        SetBrush("NavigationViewItemForegroundSelectedPointerOver", Colors.White);
        SetBrush("NavigationViewItemForegroundSelectedPressed", Colors.White);
        SetBrush("NavigationViewItemBackgroundSelected", subtle);
        SetBrush("NavigationViewItemBackgroundSelectedPointerOver", subtleHover);
        SetBrush("NavigationViewItemBackgroundSelectedPressed", subtlePressed);

        SetBrush("ToggleSwitchFillOn", accent);
        SetBrush("ToggleSwitchFillOnPointerOver", hover);
        SetBrush("ToggleSwitchFillOnPressed", pressed);
        SetBrush("ToggleSwitchStrokeOn", accent);
        SetBrush("ToggleSwitchKnobFillOn", onAccent);

        SetBrush("CheckBoxCheckBackgroundFillChecked", accent);
        SetBrush("CheckBoxCheckBackgroundFillCheckedPointerOver", hover);
        SetBrush("CheckBoxCheckBackgroundFillCheckedPressed", pressed);
        SetBrush("CheckBoxCheckBackgroundStrokeChecked", accent);
        SetBrush("CheckBoxCheckGlyphForegroundChecked", onAccent);
    }

    private void ApplyAccentToButton(Button button)
    {
        var accent = ParseColor(_settings.AccentColor);
        var hover = Mix(accent, Colors.White, 0.16);
        var pressed = Mix(accent, Colors.Black, 0.18);
        var onAccent = NeedsLightForeground(accent) ? Colors.White : Colors.Black;

        SetLocalBrush(button, "ButtonBackground", accent);
        SetLocalBrush(button, "ButtonBackgroundPointerOver", hover);
        SetLocalBrush(button, "ButtonBackgroundPressed", pressed);
        SetLocalBrush(button, "ButtonBorderBrush", accent);
        SetLocalBrush(button, "ButtonForeground", onAccent);
        SetLocalBrush(button, "ButtonForegroundPointerOver", onAccent);
        SetLocalBrush(button, "ButtonForegroundPressed", onAccent);

        button.Background = new SolidColorBrush(accent);
        button.Foreground = new SolidColorBrush(onAccent);
        button.BorderBrush = new SolidColorBrush(accent);
        button.BorderThickness = new Thickness(0);
    }

    private static void SetResource(string key, object value)
    {
        try
        {
            Application.Current.Resources[key] = value;
        }
        catch
        {
        }
    }

    // Brushes we created ourselves, so we can safely mutate their Color for live
    // accent updates. We must NOT mutate framework/system brushes (that throws
    // UnauthorizedAccessException), so we only ever touch the ones in this map.
    private static readonly Dictionary<string, SolidColorBrush> OwnedBrushes = new();

    private static void SetBrush(string key, Color color)
    {
        if (OwnedBrushes.TryGetValue(key, out var brush))
        {
            AnimateBrushColor(brush, color);
            return;
        }

        var created = new SolidColorBrush(color);
        OwnedBrushes[key] = created;
        SetResource(key, created);
    }

    // Smoothly fades a brush to a new colour (used so accent changes are live, not abrupt).
    private static void AnimateBrushColor(SolidColorBrush brush, Color to)
    {
        try
        {
            var animation = new ColorAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(320)),
                EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(animation, brush);
            Storyboard.SetTargetProperty(animation, "Color");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
        catch
        {
            brush.Color = to;
        }
    }

    private static void SetLocalBrush(FrameworkElement element, string key, Color color)
    {
        try
        {
            element.Resources[key] = new SolidColorBrush(color);
        }
        catch
        {
        }
    }

    private sealed class FluentCard
    {
        private readonly StackPanel _content = new() { Spacing = 12 };

        public FluentCard()
        {
            Root = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18),
                BorderThickness = new Thickness(1),
                BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(48, 255, 255, 255)),
                Background = ResourceBrush("CardBackgroundFillColorDefaultBrush", Color.FromArgb(218, 32, 32, 36)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Child = _content
            };

            Root.Style = StyleResource("PlayhubCardBorderStyle");
        }

        public Border Root { get; }

        public UIElementCollection Children => _content.Children;

        public static implicit operator UIElement(FluentCard card) => card.Root;
    }
}
       
