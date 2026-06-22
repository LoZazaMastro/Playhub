using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace Playhub;

public sealed partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly DeckyInstallerService _deckyInstaller = new();
    private readonly PluginCatalogService _catalog = new();
    private readonly DeckyPluginService _pluginService = new();
    private readonly GamingModeService _gamingMode = new();
    private readonly UwpXboxService _uwpXbox = new();
    private readonly ExecutableGameService _executableGameService = new();
    private readonly EpicGamesService _epicService = new();
    private readonly GogService _gogService = new();
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
    private readonly List<Button> _primaryButtons = new();
    // Weak keys so rebuilt UI elements (e.g. plugin cards) can be garbage-collected.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DependencyObject, string> _localizationKeys = new();

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
    private StackPanel _pluginCards = new();
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
    private Grid _welcomeRoot = new();
    private NavigationView _navigation = new();
    private Windows.Media.Playback.MediaPlayer? _welcomePlayer;
    private int _welcomeSlideIndex;
    private Windows.Media.Playback.MediaPlayer? _lightboxPlayer;
    private readonly List<TutorialVideoSession> _tutorialVideos = new();
    private string _currentPageTag = "welcome";
    private bool _mediaPlaybackReady;
    private StackPanel _uwpGamesPanel = new();
    private StackPanel _executableGamesPanel = new();
    private StackPanel _executableSourcesPanel = new();
    private StackPanel _epicGamesPanel = new();
    private StackPanel _gogGamesPanel = new();
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
    private TextBox _deckyPluginsBox = new();
    private TextBox _xboxSteamGridDbKeyBox = new();

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
        new("plugins", "Playhub Plugin Store"),
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
        Closed += (_, _) => ReleaseMediaForShutdown();
        SetWindowShape();
        // Seed accent brushes BEFORE the navigation is built so its selection
        // indicator and item brushes resolve our instances (and update live).
        ApplyAccentResources(ParseColor(_settings.AccentColor));
        BuildShell();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _settings = await _settingsService.LoadAsync();
            // Default the DeckyLoader plugins folder so the setting is invisible to users.
            if (string.IsNullOrWhiteSpace(_settings.DeckyPluginsPath))
            {
                _settings.DeckyPluginsPath = AppPaths.DefaultDeckyPluginsPath;
            }

            // After the user completes the welcome once, open the preferred startup page.
            if (_settings.WelcomeCompleted)
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

            ApplyTheme();
            ApplyBackdrop();
            PopulateSettingsControls();
            ApplyLanguage();
            await LoadDeckyBuildsSilentlyAsync();
            await RefreshPluginsAsync();
            await RefreshGamingModeAsync();
            await RefreshDeckyStateAsync();
            ApplyLanguage();

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
            // BuildShell initially selects Welcome before the saved startup page is known.
            // Start media only now, once that navigation has settled, so startup never
            // opens a Welcome video and a tutorial video back-to-back under the spinner.
            _mediaPlaybackReady = true;
            ShowPage(_currentPageTag);

            // The loading overlay must ALWAYS hide, even if a step above failed,
            // otherwise the app is stuck on the spinner forever.
            FadeOutThenHide(_loadingOverlay);
        }

        // Re-check DeckyLoader state whenever the window regains focus
        // (e.g. after enabling Developer Mode in Windows Settings).
        Activated += async (_, _) =>
        {
            try { await RefreshDeckyStateAsync(); } catch { }
        };
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
        // Remove the NavigationView's default top content inset (title-bar auto padding +
        // content margin) so pages and the full-bleed Welcome video start at the very top.
        navigation.Resources["NavigationViewContentMargin"] = new Thickness(0);
        _navigation = navigation;

        navigation.MenuItems.Add(NavItem("Benvenuto", "welcome", Symbol.Home));
        navigation.MenuItems.Add(NavItem("DeckyLoader", "decky", Symbol.Download));
        navigation.MenuItems.Add(NavItem("Playhub Plugin Store", "plugins", Symbol.Shop));
        navigation.MenuItems.Add(NavItem("Gaming Mode", "gaming", Symbol.Play));
        navigation.MenuItems.Add(NavItem("Importa Giochi", "xbox", ((char)0xE7FC).ToString()));
        navigation.MenuItems.Add(NavItem("Big Picture Styler", "styler", ((char)0xE771).ToString()));
        navigation.MenuItems.Add(NavItem("Impostazioni", "settings", Symbol.Setting));
        navigation.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
            {
                ShowPage(tag);
            }
        };

        _status = new InfoBar
        {
            IsOpen = false,
            Margin = new Thickness(28, 14, 28, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsClosable = true
        };

        _pageHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        _pageHost.Children.Add(BuildDeckyPage());
        _pageHost.Children.Add(BuildPluginsPage());
        _pageHost.Children.Add(BuildGamingPage());
        _pageHost.Children.Add(BuildXboxPage());
        _pageHost.Children.Add(BuildBigPictureStylerPage());
        _pageHost.Children.Add(BuildSettingsPage());

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
        // Scroller fills the whole content area; the status InfoBar is an overlay at the top
        // (so it never reserves a row / empty strip above the full-bleed Welcome video).
        content.Children.Add(_contentScroller);
        _status.VerticalAlignment = VerticalAlignment.Top;
        content.Children.Add(_status);

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

        // Welcome is a root overlay over the content area (right of the 260px pane), spanning
        // both rows so its full-bleed video reaches the very top of the window.
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
        // (no accent line before the icon)
        Grid.SetColumn(icon, 0);
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

    private static NavigationViewItem MakeNavItem(string label, string tag, IconElement icon)
    {
        icon.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        icon.RenderTransform = new ScaleTransform();
        var item = new NavigationViewItem { Content = label, Tag = tag, Icon = icon };
        item.Tapped += (_, _) => BounceElement(icon);
        return item;
    }

    // A short "pop" bounce, used on the sidebar tab icons when clicked.
    private static void BounceElement(UIElement element)
    {
        try
        {
            if (element.RenderTransform is not ScaleTransform scale)
            {
                element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                scale = new ScaleTransform();
                element.RenderTransform = scale;
            }

            var storyboard = new Storyboard();
            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var anim = new DoubleAnimationUsingKeyFrames();
                Storyboard.SetTarget(anim, scale);
                Storyboard.SetTargetProperty(anim, property);
                anim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1 });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), Value = 1.3 });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360)),
                    Value = 1,
                    EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }
                });
                storyboard.Children.Add(anim);
            }

            storyboard.Begin();
        }
        catch
        {
        }
    }

    private void ShowPage(string tag)
    {
        _currentPageTag = tag;
        var welcome = tag == "welcome";
        _welcomeRoot.Visibility = welcome ? Visibility.Visible : Visibility.Collapsed;
        foreach (var child in _pageHost.Children.OfType<FrameworkElement>())
        {
            child.Visibility = (!welcome && Equals(child.Tag, tag)) ? Visibility.Visible : Visibility.Collapsed;
        }

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

        UpdateWelcomePlayback(welcome);
        UpdateTutorialPlayback(tag);
    }

    private void UpdateWelcomePlayback(bool visible)
    {
        if (_welcomePlayer is null)
        {
            return;
        }

        try
        {
            _welcomePlayer.CommandManager.IsEnabled = false;
            if (visible)
            {
                if (_welcomePlayer.Source is null)
                {
                    _welcomePlayer.Source = Windows.Media.Core.MediaSource.CreateFromUri(
                        new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Welcome", $"welcome-{_welcomeSlideIndex}.mp4")));
                }
                _welcomePlayer.Play();
            }
            else
            {
                _welcomePlayer.Pause();
                _welcomePlayer.Source = null;
            }
        }
        catch
        {
        }
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
                    tutorial.Player.Source = null;
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

        if (_welcomePlayer is not null)
        {
            try { _welcomePlayer.Pause(); } catch { }
            try { _welcomePlayer.Source = null; } catch { }
            _welcomePlayer = null;
        }

        if (_lightboxPlayer is not null)
        {
            try { _lightboxPlayer.Pause(); } catch { }
            try { _lightboxPlayer.Source = null; } catch { }
            _lightboxPlayer = null;
        }
    }

    private UIElement BuildWelcomePage(NavigationView navigation)
    {
        var slides = new (string Glyph, string Title, string Body, bool ShowColor)[]
        {
            ("", "Benvenuto in Playhub", "Un minuto per scoprire cosa puoi fare.", false),
            (((char)0xE790).ToString(), "Scegli il tuo colore", "Dai a Playhub il tuo tocco scegliendo il colore che preferisci.", true),
            (((char)0xE719).ToString(), "Plugin Store", "Installa DeckyLoader e i plugin di Playhub per accedere a musica, trailer, meteo, achievement e molto altro mentre giochi.", false),
            (((char)0xE945).ToString(), "Gaming Mode", "Avvia il tuo PC come se fosse una console, naviga con il controller e torna al desktop quando vuoi.", false),
            (((char)0xE896).ToString(), "Importa i tuoi giochi", "Porta i giochi Xbox Game Pass, Xbox Store, Microsoft Store e gli EXE del tuo PC nella tua libreria, completi di artwork e titolo corretti, pronti da avviare.", false),
            (((char)0xE7FC).ToString(), "Divertiti", "Ora tocca a te. Buon divertimento con Playhub!", false)
        };
        var index = 0;

        // ----- per-slide looping video background (full-bleed, 70% opacity) -----
        var bgPlayer = new Windows.Media.Playback.MediaPlayer
        {
            IsMuted = true,
            IsLoopingEnabled = true,
            AutoPlay = false
        };
        _welcomePlayer = bgPlayer;
        // It's just a background, not playable media: keep it out of Windows' System
        // Media Transport Controls (no progress bar / media controls in the OS).
        try { bgPlayer.CommandManager.IsEnabled = false; } catch { }
        var background = new MediaPlayerElement
        {
            Stretch = Stretch.UniformToFill,
            Opacity = 0.7,
            AreTransportControlsEnabled = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        background.SetMediaPlayer(bgPlayer);

        void SetSlideVideo()
        {
            _welcomeSlideIndex = index;
            try { bgPlayer.Source = null; } catch { }
            UpdateWelcomePlayback(_currentPageTag == "welcome");
        }

        // ----- hero content -----
        var logoImage = new Image
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "cube.png"))),
            Width = 156,
            Height = 156,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var icon = new FontIcon
        {
            FontSize = 140,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor))
        };
        var title = new TextBlock
        {
            FontSize = 34,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.White)
        };
        var body = new TextBlock
        {
            FontSize = 16,
            Opacity = 0.82,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 620,
            LineHeight = 25
        };
        var welcomeAccent = BuildAccentPicker();
        welcomeAccent.HorizontalAlignment = HorizontalAlignment.Center;

        var startButton = Button("Cominciamo", async () =>
        {
            _settings.WelcomeCompleted = true;
            await SaveSettingsSilentlyAsync();
            var target = navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Tag, "decky"));
            if (target is not null)
            {
                navigation.SelectedItem = target;
            }
        }, primary: true);
        startButton.HorizontalAlignment = HorizontalAlignment.Center;

        var hero = new StackPanel
        {
            Spacing = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 700
        };
        hero.Children.Add(logoImage);
        hero.Children.Add(icon);
        hero.Children.Add(title);
        hero.Children.Add(body);
        hero.Children.Add(welcomeAccent);
        hero.Children.Add(startButton);

        // hero slide/fade transition
        var heroTransform = new TranslateTransform();
        hero.RenderTransform = heroTransform;

        void SlideIn(int dir)
        {
            try
            {
                heroTransform.X = dir * 60;
                hero.Opacity = 0;
                var sb = new Storyboard();
                var ax = new DoubleAnimation
                {
                    To = 0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(ax, heroTransform);
                Storyboard.SetTargetProperty(ax, "X");
                var ao = new DoubleAnimation { To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(300)) };
                Storyboard.SetTarget(ao, hero);
                Storyboard.SetTargetProperty(ao, "Opacity");
                sb.Children.Add(ax);
                sb.Children.Add(ao);
                sb.Begin();
            }
            catch
            {
                hero.Opacity = 1;
                heroTransform.X = 0;
            }
        }

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

        // ----- circular arrows -----
        var left = GlyphCircleButton(((char)0xE76B).ToString(), 52);
        left.HorizontalAlignment = HorizontalAlignment.Left;
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(26, 0, 0, 0);
        var right = GlyphCircleButton(((char)0xE76C).ToString(), 52);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        right.VerticalAlignment = VerticalAlignment.Center;
        right.Margin = new Thickness(0, 0, 26, 0);

        void Render()
        {
            var s = slides[index];
            var isLogo = index == 0;
            var isFinish = index == slides.Length - 1;

            SetSlideVideo();

            logoImage.Visibility = isLogo ? Visibility.Visible : Visibility.Collapsed;
            icon.Visibility = isLogo ? Visibility.Collapsed : Visibility.Visible;
            if (!isLogo)
            {
                icon.Glyph = s.Glyph;
            }

            title.Text = s.Title;
            body.Text = s.Body;
            welcomeAccent.Visibility = s.ShowColor ? Visibility.Visible : Visibility.Collapsed;
            startButton.Visibility = isFinish ? Visibility.Visible : Visibility.Collapsed;

            for (var i = 0; i < dotList.Count; i++)
            {
                dotList[i].Background = i == index
                    ? ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor))
                    : new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            }

            left.Visibility = index > 0 ? Visibility.Visible : Visibility.Collapsed;
            right.Visibility = isFinish ? Visibility.Collapsed : Visibility.Visible;

            // Slide text is set in Italian above; re-translate it for the current language.
            LocalizeElement(title);
            LocalizeElement(body);
        }

        void GoTo(int target)
        {
            if (target < 0 || target >= slides.Length || target == index)
            {
                return;
            }

            var dir = target > index ? 1 : -1;
            index = target;
            Render();
            SlideIn(dir);
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

        left.Click += (_, _) =>
        {
            BounceElement(left);
            GoTo(index - 1);
        };
        right.Click += (_, _) =>
        {
            BounceElement(right);
            GoTo(index + 1);
        };

        // ----- assemble: full-bleed (no card), fills the right side of the window -----
        // Darken the video ~30% so the text and controls stay readable.
        var videoScrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)),
            IsHitTestVisible = false
        };

        _welcomeRoot = new Grid
        {
            Tag = "welcome",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _welcomeRoot.Children.Add(background);
        _welcomeRoot.Children.Add(videoScrim);
        _welcomeRoot.Children.Add(hero);
        _welcomeRoot.Children.Add(dots);
        _welcomeRoot.Children.Add(left);
        _welcomeRoot.Children.Add(right);

        Render();
        return _welcomeRoot;
    }

    private UIElement BuildDeckyPage()
    {
        var panel = Page("decky", "DeckyLoader", "Pochi passi e i plugin sono pronti in Steam. Ogni passo diventa verde quando è completato.");

        // Step 1 — Steam installed?
        _steamButton = Button("Scarica Steam", async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri("https://store.steampowered.com/about/")); });
        panel.Children.Add(BuildDeckyStep(
            "",
            "Steam installato",
            "DeckyLoader funziona dentro Steam: serve che Steam sia installato sul PC.",
            _steamButton,
            out _steamTile, out _steamGlyph, out _steamStatus));

        // Step 2 — Windows Developer Mode
        panel.Children.Add(BuildDeckyStep(
            "",
            "Modalità sviluppatore di Windows",
            "Si attiva una volta sola: permette a DeckyLoader di installare i plugin.",
            Button("Apri impostazioni", async () => { await _deckyInstaller.OpenDeveloperSettingsAsync(); }),
            out _devTile, out _devGlyph, out _devStatus));

        // Step 2 — Install DeckyLoader
        _installButton = Button("Installa", async () => { await InstallLatestDeckyBuildAsync(); await RefreshDeckyStateAsync(); }, primary: true);
        panel.Children.Add(BuildDeckyStep(
            "",
            "Installa DeckyLoader",
            "Scarico e configuro l'ultima versione di DeckyLoader.",
            ActionRow(
                _installButton,
                Button("Rimuovi", async () => { SetStatus(await _deckyInstaller.RemoveAsync(), InfoBarSeverity.Warning); await RefreshDeckyStateAsync(); })),
            out _installTile, out _installGlyph, out _installStatus));

        var bigPicture = BuildBigPictureTutorialCard();
        _deckyBigPictureCard = bigPicture.Root;
        _deckyBigPictureCard.Visibility = Visibility.Collapsed;
        panel.Children.Add(bigPicture);

        panel.Children.Add(BuildGameBarWarningCard());

        var quickAccess = BuildQuickAccessTutorialCard(
            "decky",
            "Esplora Decky",
            "Aprilo dal Quick Access Menu con il controller o la tastiera.",
            "");
        _deckyQuickAccessCard = quickAccess.Root;
        _deckyQuickAccessCard.Visibility = Visibility.Collapsed;
        panel.Children.Add(quickAccess);

        // Advanced — pick a specific build
        var update = Card();
        update.Children.Add(IconHeader(((char)0xE896).ToString(), "Installa una versione specifica di DeckyLoader",
            "Di solito non serve: l'installazione qui sopra usa già l'ultima versione. Scegli una data solo se ti occorre una versione precisa."));
        _deckyBuildCombo = new ComboBox { PlaceholderText = "Scegli una versione", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 0) };
        update.Children.Add(_deckyBuildCombo);
        update.Children.Add(ActionRow(Button("Installa questa versione", async () => { await InstallSelectedDeckyBuildAsync(); await RefreshDeckyStateAsync(); })));
        panel.Children.Add(update);

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
        var ready = steamInstalled && devOn && installed;
        _deckyBigPictureCard.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        _deckyQuickAccessCard.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        UpdateTutorialPlayback(_currentPageTag);
        var installLabel = installed ? "Reinstalla" : "Installa";
        _localizationKeys.AddOrUpdate(_installButton, installLabel);
        _installButton.Content = T(installLabel);
        await Task.CompletedTask;
    }

    private void SetStepState(Border tile, FontIcon glyph, TextBlock status, bool done, string pendingGlyph, string label)
    {
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
        var panel = Page("plugins", "Playhub Plugin Store", "I plugin della suite Playhub: installali, aggiornali e gestiscili da qui.");
        panel.Children.Add(BuildPluginRestartCard());
        _pluginCards = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(_pluginCards);
        panel.Children.Add(BuildQuickAccessTutorialCard(
            "plugins",
            "Decky Store",
            "Apri lo store di Decky dal Quick Access Menu e scopri altri plugin.",
            "",
            "I plugin dello store di Decky sono sviluppati per Linux, a volte potrebbero non funzionare come previsto su Windows.",
            "Decky-Store.mp4"));
        return panel;
    }

    private UIElement BuildGamingPage()
    {
        var panel = Page("gaming", "Gaming Mode", "Usa il tuo PC come una console senza bisogno di mouse e tastiera.");

        // Backing value for the default mode (driven by the two tiles below).
        _defaultModeCombo = ChoiceCombo(ModeOptions);

        // ---------- 1. What it is + install ----------
        var manage = Card();
        manage.Children.Add(IconHeader(((char)0xE896).ToString(), "Installa Gaming Mode",
            "Installa Gaming Mode e il plugin companion per DeckyLoader in un solo passaggio."));
        manage.Children.Add(ActionRow(
            Button("Installa o aggiorna", async () =>
            {
                var result = await _gamingMode.InstallAsync(_settings.DeckyPluginsPath);
                SetStatus(result.Message, result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            }, primary: true),
            Button("Disinstalla", async () =>
            {
                var result = await _gamingMode.UninstallAsync(_settings.DeckyPluginsPath);
                SetStatus(result.Message, result.Success ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
            })));
        manage.Children.Add(AdvancedGamingTools());
        panel.Children.Add(manage);
        panel.Children.Add(BuildQuickAccessTutorialCard(
            "gaming",
            "Apri il plugin Gaming Mode",
            "Gaming Mode vive nel Quick Access Menu di Decky, sempre a portata di controller.",
            "",
            warning: "Per qualche motivo non riesci ad accedere al plugin Gaming Mode? Nessun problema: mentre sei in Gaming Mode ti basta chiudere Steam e il PC torna da solo in Desktop Mode. In alternativa, tieni premuto Shift mentre accedi a Windows per avviarlo direttamente sul desktop. Da lì riapri Playhub quando vuoi.",
            videoFile: "Gaming-Mode-Plugin.mp4"));

        // ---------- 2. Default mode: two big tiles + one-time switch ----------
        var modeCard = Card();
        modeCard.Children.Add(IconHeader(((char)0xE7FC).ToString(), "Modalità predefinita",
            "Scegli come si accende il PC. La scheda illuminata è quella attiva ad ogni avvio."));

        var desktopIcons = new List<FontIcon>();
        var desktopIconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        var keyboardIcon = new FontIcon { Glyph = ((char)0xE765).ToString(), FontSize = 42, VerticalAlignment = VerticalAlignment.Center };
        var mouseIcon = new FontIcon { Glyph = ((char)0xE962).ToString(), FontSize = 34, VerticalAlignment = VerticalAlignment.Center };
        desktopIcons.Add(keyboardIcon);
        desktopIcons.Add(mouseIcon);
        desktopIconRow.Children.Add(keyboardIcon);
        desktopIconRow.Children.Add(mouseIcon);
        _desktopModeTile = ModeTileShell(desktopIconRow, "Desktop", "Mouse, tastiera e finestre, come un PC normale.");
        _desktopModeTile.Tapped += async (_, _) => await SelectDefaultModeAsync("Desktop");

        var gamingIcons = new List<FontIcon>();
        var gamingIconRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var padIcon = new FontIcon { Glyph = ((char)0xE7FC).ToString(), FontSize = 48, VerticalAlignment = VerticalAlignment.Center };
        gamingIcons.Add(padIcon);
        gamingIconRow.Children.Add(padIcon);
        _gamingModeTile = ModeTileShell(gamingIconRow, "Gaming", "Il tuo PC in modalità gaming, da divano e controller.");
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
                SetStatus("Gaming Mode non risponde. Installa o avvia l'agente e riprova.", InfoBarSeverity.Warning);
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
                SetStatus("Gaming Mode non risponde. Installa o avvia l'agente e riprova.", InfoBarSeverity.Warning);
            }
        });
        gamingSwitch.HorizontalAlignment = HorizontalAlignment.Stretch;
        gamingCol.Children.Add(gamingSwitch);
        Grid.SetColumn(gamingCol, 1);

        modeGrid.Children.Add(desktopCol);
        modeGrid.Children.Add(gamingCol);
        modeCard.Children.Add(modeGrid);
        panel.Children.Add(modeCard);

        // Shared fields (placed into the concept cards below).
        _steamPathBox = TextBox("Cartella di Steam");
        _steamArgsBox = TextBox("Argomenti di Steam");
        _deckyPathBox = TextBox("Percorso di PluginLoader_noconsole.exe");
        _sunshinePathBox = TextBox("Cartella dello strumento di streaming");
        _delaySteamBox = Number("Attesa prima di Steam (ms)", 0, 60000);
        _mouseDelayBox = Number("Nascondi il cursore dopo (ms)", 0, 30000);
        _apiPortBox = Number("Porta agente", 1, 65535);

        // ---------- 3. Avvio ----------
        var startCard = Card();
        startCard.Children.Add(IconHeader(((char)0xE945).ToString(), "Avvio",
            "Cosa parte quando entri in Gaming Mode."));
        AddExplainedToggle(startCard, "Avvia DeckyLoader prima di Steam",
            "Carica i plugin Decky prima di Steam, così sono pronti quando si apre la libreria.", "deckyRequired");
        AddExplainedToggle(startCard, "Avvia lo streaming",
            "Apre Sunshine, Apollo o Vibepollo per giocare in streaming da un altro dispositivo.", "sunshineRequired");

        var advancedStart = new StackPanel { Spacing = 12 };
        advancedStart.Children.Add(TwoColumn(Labeled("Cartella di Steam", BrowseRow(_steamPathBox, folder: true)), Labeled("Argomenti di Steam", _steamArgsBox)));
        advancedStart.Children.Add(TwoColumn(
            Labeled("PluginLoader_noconsole.exe", BrowseRow(_deckyPathBox, folder: false, exts: new[] { ".exe" })),
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
            "L'aspetto dell'esperienza a tutto schermo."));
        AddExplainedToggle(screenCard, "Nascondi il desktop in Gaming Mode",
            "In Gaming Mode il desktop di Windows non viene avviato, per un'esperienza pulita da console. Al ritorno in Desktop Mode viene sempre ripristinato.", "closeExplorer");
        AddExplainedToggle(screenCard, "Finestre senza bordi",
            "Tiene i giochi a schermo intero senza bordi, per la massima immersività.", "borderless");
        AddExplainedToggle(screenCard, "Nascondi il cursore",
            "Fa sparire il puntatore del mouse quando giochi con il controller.", "hideMouse");
        screenCard.Children.Add(NumberWithHint(_mouseDelayBox, "Inattività prima di nascondere il cursore."));

        // ---------- 5. Controller, streaming e audio ----------
        var inputCard = Card();
        inputCard.Children.Add(IconHeader(((char)0xE7FC).ToString(), "Controller e streaming",
            "Input e gioco in streaming."));
        AddExplainedToggle(inputCard, "Prepara i controller",
            "Applica le impostazioni dei controller quando entri in Gaming Mode.", "inputCompatibility");
        AddExplainedToggle(inputCard, "Prepara lo streaming locale",
            "Configura il sistema per lo streaming dei giochi sulla rete di casa.", "sunshineCompatibility");
        inputCard.Children.Add(Labeled("Strumento di streaming (Sunshine, Apollo o Vibepollo)", BrowseRow(_sunshinePathBox, folder: true)));

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
        _splashMaxBox = Number("Timeout massimo (ms)", 1000, 300000);
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
            NumberWithHint(_splashMaxBox, "Dopo questo tempo la schermata si chiude comunque, per sicurezza.")));

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
        apps.Children.Add(IconHeader(((char)0xE710).ToString(), "Processi personalizzati",
            "App da avviare prima di Steam in Gaming Mode."));
        apps.Children.Add(ActionRow(Button("Aggiungi processo", async () =>
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

        // Auto-save: ogni modifica viene salvata all'istante (niente "Applica modifiche").
        WireGamingAutoSave();

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
        row.Children.Add(new TextBlock { Text = title, Style = StyleResource("PlayhubSectionTitleStyle"), VerticalAlignment = VerticalAlignment.Center });
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
    private StackPanel ImageHeader(string logoFile, string title, string subtitle)
    {
        var header = new StackPanel { Spacing = 8 };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var logo = new Image
        {
            Height = 22,
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
        row.Children.Add(logo);
        row.Children.Add(new TextBlock { Text = title, Style = StyleResource("PlayhubSectionTitleStyle"), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(row);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            header.Children.Add(Body(subtitle));
        }
        return header;
    }

    private FluentCard BuildQuickAccessTutorialCard(
        string pageTag,
        string title,
        string subtitle,
        string finalStep,
        string warning = "",
        string videoFile = "DeckyLoader-QAM.mp4")
    {
        var card = Card();
        var text = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        var headerGlyph = pageTag == "plugins" ? ((char)0xE719).ToString() : ((char)0xE7FC).ToString();
        text.Children.Add(IconHeader(headerGlyph, title, subtitle));
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

    private Border BuildGameBarWarningCard()
    {
        var content = new StackPanel { Spacing = 14 };

        var headerGrid = new Grid { ColumnSpacing = 12 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = new FontIcon
        {
            Glyph = ((char)0xE7BA).ToString(),
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 203, 15)),
            VerticalAlignment = VerticalAlignment.Top
        };
        var textStack = new StackPanel { Spacing = 4 };
        textStack.Children.Add(new TextBlock
        {
            Text = "Disattiva l'apertura della Game Bar da controller",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        textStack.Children.Add(new TextBlock
        {
            Text = "Per evitare conflitti e avere un'esperienza migliore in Modalità Gaming, disattiva l'opzione "
                + ((char)0x201C).ToString() + "Consenti al controller di aprire Game Bar" + ((char)0x201D).ToString()
                + " nelle impostazioni di Windows.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 19,
            Opacity = 0.9
        });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(textStack, 1);
        headerGrid.Children.Add(icon);
        headerGrid.Children.Add(textStack);
        content.Children.Add(headerGrid);

        content.Children.Add(ActionRow(Button("Fallo subito", async () =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:gaming-gamebar")), primary: true)));

        return new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 203, 15)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(72, 255, 203, 15)),
            BorderThickness = new Thickness(1),
            Child = content
        };
    }

    private FluentCard BuildBigPictureTutorialCard()
    {
        var card = Card();
        var grid = new Grid
        {
            ColumnSpacing = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

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

        Grid.SetColumn(text, 0);
        Grid.SetColumn(imageStage, 1);
        grid.Children.Add(text);
        grid.Children.Add(imageStage);
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
        _tutorialVideos.Add(new TutorialVideoSession(pageTag, requiresDeckyInstalled, videoPath, stage));

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
            Foreground = new SolidColorBrush(Colors.White)
        });
        content.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12.5,
            Opacity = 0.72,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 250
        });

        return new Border
        {
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
        tools.Children.Add(Body("Strumenti utili solo per diagnosi o sviluppo."));
        tools.Children.Add(ActionRow(
            Button("Avvia agente", () => { _gamingMode.StartAgent(); SetStatus("Agente avviato.", InfoBarSeverity.Informational); }),
            Button("Controlla agente", async () => SetStatus(await _gamingMode.IsAgentHealthyAsync(_gamingConfig.Safety.ApiPort) ? "Agente attivo." : "Agente non raggiungibile.", InfoBarSeverity.Informational))));

        return new Expander
        {
            Header = "Avanzate",
            Content = tools,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private UIElement BuildXboxPage()
    {
        var panel = Page("xbox", "Importa Giochi", "Porta i tuoi giochi nella libreria di Steam e completa automaticamente gli artwork.");

        var import = Card();
        _uwpChevron = AddCollapsibleHeader(import, ImageHeader("Xbox.png", "Importa giochi Xbox e Microsoft Store",
            "Trova i giochi Xbox, Game Pass e Microsoft Store installati e li aggiunge a Steam, pronti da avviare."), () => _uwpGamesPanel);
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
            "Trova i giochi installati con l'Epic Games Launcher e li aggiunge a Steam."), () => _epicGamesPanel);
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
            "Trova i giochi GOG installati (Galaxy o installer offline) e li aggiunge a Steam."), () => _gogGamesPanel);
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
        _executableChevron = AddCollapsibleHeader(executableImport, IconHeader(((char)0xE8B7).ToString(), "Importa giochi da cartelle e file",
            "Scegli le tue cartelle preferite o aggiungi singoli file e scansiona gli EXE presenti nelle cartelle e nelle sottocartelle. Playhub riconoscerà automaticamente i giochi e li preparerà per la tua libreria."), () => _executableGamesPanel);
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
            "Inserisci qui la tua chiave API di SteamGridDB che serve a scaricare automaticamente copertine, sfondi e loghi quando importi i giochi."));
        _xboxSteamGridDbKeyBox = TextBox("SteamGridDB API key");
        _xboxSteamGridDbKeyBox.TextChanged += async (_, _) =>
        {
            if (_loadingSettings) return;
            _settings.SteamGridDbApiKey = _xboxSteamGridDbKeyBox.Text;
            await SaveSettingsSilentlyAsync();
        };

        var apiRow = new Grid { ColumnSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        apiRow.ColumnDefinitions.Add(new ColumnDefinition());
        apiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        apiRow.Children.Add(_xboxSteamGridDbKeyBox);
        var apiButton = Button("La tua API", async () =>
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
        card.Children.Add(new TextBlock
        {
            Text = "Hai installato tutto?",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        card.Children.Add(ActionRow(Button("Riavvia DeckyLoader e Steam", async () =>
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
        var panel = Page("styler", "Big Picture Styler", "Personalizza Big Picture e prenditi cura della tua libreria Steam.");

        var css = Card();
        var cssText = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        cssText.Children.Add(IconHeader(((char)0xE790).ToString(), "Tema Playhub per CSS Loader",
            "Installa il profilo Playhub in CSS Loader senza sostituire le tue opzioni attuali: puoi provarlo in sicurezza, senza rischiare di azzerare le impostazioni che hai già. È consigliato per la migliore esperienza con i plugin di Playhub."));
        cssText.Children.Add(BuildYellowWarning(
            "Per poter installare il tema è prima necessario installare il plugin CSS Loader dal Decky Store."));
        cssText.Children.Add(ActionRow(
            Button("Installa", async () => SetStatus(await _extra.ApplyCssLoaderProfileAsync(_settings.CssLoaderProfileUrl), InfoBarSeverity.Success)),
            Button("Rimuovi", async () => SetStatus(await _extra.RemoveCssLoaderProfileAsync(), InfoBarSeverity.Warning))));

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

        var steam = Card();
        steam.Children.Add(IconHeader(((char)0xE72E).ToString(), "Aggiornamenti di Steam",
            "Blocca gli aggiornamenti del client di Steam copiando steam.cfg nella sua cartella. Puoi rimuoverlo quando vuoi."));
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

    private UIElement BuildSettingsPage()
    {
        var panel = Page("settings", "Impostazioni", "Aspetto, avvio e informazioni di Playhub.");

        // ---------- Aspetto ----------
        var appearance = Card();
        appearance.Children.Add(IconHeader(((char)0xE713).ToString(), "Aspetto",
            "Personalizza lo sfondo e il colore di Playhub."));
        _languageCombo = LanguageCombo();
        _languageCombo.SelectionChanged += async (_, _) =>
        {
            if (_loadingSettings) return;
            var selectedLanguage = GetComboKey(_languageCombo) ?? "en";
            if (string.Equals(
                    LocalizationService.NormalizeLanguageKey(_settings.Language),
                    LocalizationService.NormalizeLanguageKey(selectedLanguage),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.Language = selectedLanguage;
            await SaveSettingsSilentlyAsync();
            RestartPlayhub();
        };

        _backdropCombo = ChoiceCombo(BackdropOptions);
        _backdropCombo.SelectionChanged += async (_, _) =>
        {
            if (_loadingSettings) return;
            _settings.Backdrop = GetComboKey(_backdropCombo) ?? "mica";
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
            Text = "Questo riavvierà Playhub",
            Opacity = 0.68,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        appearance.Children.Add(TwoColumn(Labeled("Lingua", languageRow), Labeled("Sfondo", _backdropCombo)));
        appearance.Children.Add(Labeled("Colore accent", _accentColorPanel));
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
            "Controlla se c'è una nuova versione pronta da installare."));
        updates.Children.Add(ActionRow(Button("Controlla aggiornamenti", async () => await CheckPlayhubUpdatesAsync(), primary: true)));
        panel.Children.Add(updates);

        // ---------- Informazioni ----------
        var about = Card();
        about.Children.Add(IconHeader(((char)0xE946).ToString(), "Informazioni",
            $"Playhub {GetAppVersion()} · © 2026 Andrea Sgarro (ZazaMastro)"));
        about.Children.Add(Body("Componenti di terze parti (licenza MIT): UWPHook © 2016 Brian Lima · VDFParser © 2016 Victor Gama · SharpSteam © 2020 Brian Lima."));
        about.Children.Add(ActionRow(
            Button("UWPHook", async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/BrianLima/UWPHook"))),
            Button("VDFParser", async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/BrianLima/VDFParser"))),
            Button("SharpSteam", async () => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/BrianLima/SharpSteam")))));
        about.Children.Add(new Expander
        {
            Header = "Testo delle licenze (MIT)",
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

    private const string ThirdPartyLicensesText =
@"Playhub includes the following open-source components, each under the MIT License.

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
SOFTWARE.";


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
        // Installs the latest official build directly (no dependency on the
        // GitHub listing API, which is rate-limited without a token).
        SetStatus(await _deckyInstaller.InstallLatestAsync(), InfoBarSeverity.Success);
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

    private async Task RefreshPluginsAsync()
    {
        _plugins.Clear();
        try
        {
            foreach (var plugin in await _catalog.LoadAsync(_settings.PluginRoot, _settings.DeckyPluginsPath))
            {
                _plugins.Add(plugin);
            }
            RenderPluginCards();
        }
        catch
        {
            SetStatus("Plugin Store non disponibile. Riprova tra poco.", InfoBarSeverity.Warning);
        }
    }

    private void RenderPluginCards()
    {
        _pluginCards.Children.Clear();
        foreach (var plugin in _plugins)
        {
            _pluginCards.Children.Add(PluginBannerCard(plugin));
        }

        LocalizeElement(_pluginCards);
    }

    private UIElement PluginBannerCard(DeckyPluginInfo plugin)
    {
        const double compressedHeight = 188;
        const double expandedAspect = 9.0 / 16.0; // banner is always 16:9 when expanded
        var expanded = false;
        Border card = null!; // declared early so ToggleDetails can scroll it into view

        var banner = new Grid { Height = compressedHeight };

        var imagePath = PluginImagePath(plugin);
        if (imagePath is not null)
        {
            var bitmap = new BitmapImage { DecodePixelWidth = 1500 };
            bitmap.UriSource = new Uri(imagePath);
            banner.Children.Add(new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center // center the crop when compressed
            });
        }
        else
        {
            banner.Background = new SolidColorBrush(WithAlpha(ParseColor(_settings.AccentColor), 70));
        }
        banner.Children.Add(new Border
        {
            Height = 210,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = CardScrim()
        });

        // status pill — top-left
        var pill = PluginStatusPill(plugin);
        pill.HorizontalAlignment = HorizontalAlignment.Left;
        pill.VerticalAlignment = VerticalAlignment.Top;
        pill.Margin = new Thickness(20, 18, 0, 0);
        banner.Children.Add(pill);

        // close (X) — top-right, only when expanded
        var closeButton = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0),
            Visibility = Visibility.Collapsed,
            Content = new FontIcon { Glyph = "", FontSize = 14 }
        };
        // (no X button — Dettagli toggles open/closed)

        // ---------- details (continuation) — declared before it's captured below ----------
        var details = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            Padding = new Thickness(24, 8, 24, 22),
            Spacing = 14
        };
        // Screenshots/video first (like every app store), then the changelog, then the text.
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
            // Testo già nella lingua giusta (blocco unico): il walker NON deve
            // ritradurlo riga per riga, altrimenti tornerebbe il "misto".
            description.Tag = "noloc";
            details.Children.Add(description);
        }

        // Dettagli toggle (expands/collapses; the chevron flips)
        var chevron = new FontIcon { Glyph = ((char)0xE70D).ToString(), FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        var detailsLabel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        detailsLabel.Children.Add(chevron);
        detailsLabel.Children.Add(new TextBlock { Text = "Dettagli", VerticalAlignment = VerticalAlignment.Center });
        var detailsButton = new Button { Content = detailsLabel, Style = StyleResource("PlayhubSecondaryButtonStyle") };
        void ToggleDetails()
        {
            if (expanded)
            {
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
        // Clicking the banner/image (not the action buttons) also expands/collapses.
        banner.Tapped += (_, _) => ToggleDetails();

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
        bottom.Children.Add(PluginActions(plugin, detailsButton));
        banner.Children.Add(bottom);

        // ---------- assemble ----------
        var stack = new StackPanel();
        stack.Children.Add(banner);
        stack.Children.Add(details);

        card = new Border
        {
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 34)),
            Child = stack
        };

        void Expand()
        {
            if (expanded) return;
            expanded = true;
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

        return card;
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
        var panel = new StackPanel { Spacing = 9 };
        var lines = text.Replace("\r\n", "\n").Split('\n');
        StackPanel? bullets = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                bullets = null;
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                bullets = null;
                panel.Children.Add(new TextBlock
                {
                    Text = line.Substring(3).Trim(),
                    FontSize = 15,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = ResourceBrush("TextFillColorPrimaryBrush", Colors.White)
                });
                continue;
            }

            if (line.StartsWith("• ", StringComparison.Ordinal) || line.StartsWith("- ", StringComparison.Ordinal))
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
                    Text = "•",
                    FontSize = 16,
                    Margin = new Thickness(2, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Foreground = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor))
                };
                Grid.SetColumn(dot, 0);

                var content = new TextBlock
                {
                    Text = line.Substring(2).Trim(),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 21,
                    Opacity = 0.9
                };
                Grid.SetColumn(content, 1);

                row.Children.Add(dot);
                row.Children.Add(content);
                bullets.Children.Add(row);
                continue;
            }

            bullets = null;
            panel.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Opacity = 0.92
            });
        }

        return panel;
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
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch
        {
            return true;
        }
    }

    private StackPanel PluginActions(DeckyPluginInfo plugin, Button? detailsButton = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        // Accent (primary) only for a real confirm action: install or update.
        if (!plugin.IsInstalled)
        {
            row.Children.Add(IconButton(((char)0xE896).ToString(), "Installa", async () =>
            {
                await _pluginService.InstallOrUpdateAsync(plugin, _settings.DeckyPluginsPath);
                await RefreshPluginsAsync();
                SetStatus($"{plugin.Name}: {T("Installato")}.", InfoBarSeverity.Success);
            }, primary: true));
        }
        else if (plugin.HasUpdate)
        {
            row.Children.Add(IconButton(((char)0xE895).ToString(), "Aggiorna", async () =>
            {
                await _pluginService.InstallOrUpdateAsync(plugin, _settings.DeckyPluginsPath);
                await RefreshPluginsAsync();
                SetStatus($"{plugin.Name}: {T("Aggiornato")}.", InfoBarSeverity.Success);
            }, primary: true));
        }

        if (plugin.IsInstalled)
        {
            row.Children.Add(IconButton(((char)0xE74D).ToString(), "Disinstalla", async () =>
            {
                if (!await ConfirmAsync("Disinstallare il plugin?", "Il plugin verrà rimosso da DeckyLoader. Potrai reinstallarlo quando vuoi."))
                {
                    return;
                }

                await _pluginService.UninstallAsync(plugin);
                await RefreshPluginsAsync();
                SetStatus($"{plugin.Name}: {T("Rimosso")}.", InfoBarSeverity.Warning);
            }));
        }

        if (detailsButton is not null)
        {
            row.Children.Add(detailsButton);
        }

        // GitHub is always last.
        row.Children.Add(GitHubButton(async () =>
        {
            if (!string.IsNullOrWhiteSpace(plugin.RepositoryUrl))
            {
                await Launcher.LaunchUriAsync(new Uri(plugin.RepositoryUrl));
            }
        }));

        return row;
    }

    private static string? PluginImagePath(DeckyPluginInfo plugin)
    {
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "PluginImages", plugin.Name + ".jpg");
        return System.IO.File.Exists(path) ? path : null;
    }

    private static Image? PluginImageElement(DeckyPluginInfo plugin, int decodeWidth)
    {
        var path = PluginImagePath(plugin);
        if (path is null)
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage { DecodePixelWidth = decodeWidth };
            bitmap.UriSource = new Uri(path);
            return new Image
            {
                Source = bitmap,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
        }
        catch
        {
            return null;
        }
    }

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
        var version = plugin.IsInstalled ? plugin.InstalledVersion : plugin.Version;
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
        var grid = new Grid { ColumnSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        for (var i = 0; i < items.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var tile = PluginMediaTile(items, i);
            Grid.SetColumn(tile, i);
            grid.Children.Add(tile);
        }

        return grid;
    }

    private FrameworkElement PluginMediaTile(List<PluginMediaInfo> all, int index)
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
            border.Child = new Image { Source = new BitmapImage(uri), Stretch = Stretch.Uniform };
        }
        else
        {
            var stack = new Grid();
            if (hasUri)
            {
                stack.Children.Add(BuildVideoPoster(uri!));
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
            border.Tapped += (_, _) => OpenLightbox(all, index);
        }

        return border;
    }

    // A muted, paused MediaPlayerElement showing the video's first frame as a poster.
    private FrameworkElement BuildVideoPoster(Uri uri)
    {
        try
        {
            var player = new Windows.Media.Playback.MediaPlayer { IsMuted = true, AutoPlay = false };
            try { player.CommandManager.IsEnabled = false; } catch { }
            player.Source = Windows.Media.Core.MediaSource.CreateFromUri(uri);
            player.MediaOpened += (s, _) => DispatcherQueue.TryEnqueue(() =>
            {
                try { s.StepForwardOneFrame(); } catch { }
            });

            var element = new MediaPlayerElement { AreTransportControlsEnabled = false, Stretch = Stretch.Uniform };
            element.SetMediaPlayer(player);
            return element;
        }
        catch
        {
            return new Border { Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 22)) };
        }
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
        PopulateGamingConfigControls();
        RenderStartupApps();
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
        await _gamingMode.SaveConfigAsync(_gamingConfig);
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
        if (result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var game in _uwpGames.Where(game => game.InSteamLibrary))
            {
                game.Selected = false;
            }
        }
        RenderUwpGames();
        SetStatus(result, result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase)
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

        if (result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase))
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
        if (result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var game in _executableGames.Where(game => game.InSteamLibrary))
            {
                game.Selected = false;
            }
        }
        RenderExecutableGames();
        SetStatus(result, result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase)
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
        ToolTipService.SetToolTip(remove, T("Rimuovi"));
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
        if (result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var game in _epicGames.Where(game => game.InSteamLibrary))
            {
                game.Selected = false;
            }
        }

        RenderEpicGames();
        SetStatus(result, result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase)
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
        if (result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var game in _gogGames.Where(game => game.InSteamLibrary))
            {
                game.Selected = false;
            }
        }

        RenderGogGames();
        SetStatus(result, result.StartsWith("Ho importato", StringComparison.OrdinalIgnoreCase)
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
        var refetchButton = Button("Refetch", async () => await ShowSteamGridDbRefetchDialogAsync(game));
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
            Title = string.Format(T("Refetch - {0}"), game.Name),
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
                    Text = option.ReleaseYear?.ToString() ?? "—",
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
            SetStatus("Il risultato SteamGridDB è stato rimosso. Il gioco verrà mostrato senza artwork.", InfoBarSeverity.Success);
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
        SetStatus(
            coverLoaded
                ? string.Format(T("Risultato aggiornato: {0}."), selected.Name)
                : string.Format(T("Risultato aggiornato: {0}. Nessuna cover disponibile."), selected.Name),
            coverLoaded ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async Task ShowUwpArtworkDialogAsync(UwpGameEntry game)
    {
        if (string.IsNullOrWhiteSpace(_settings.SteamGridDbApiKey))
        {
            SetStatus("Inserisci prima la chiave API SteamGridDB nella sezione Artwork dei giochi.", InfoBarSeverity.Warning);
            return;
        }

        var selector = new SelectorBar { HorizontalAlignment = HorizontalAlignment.Stretch };
        var categories = new[]
        {
            (Type: "cover", Text: "Cover", Symbol: Symbol.Library),
            (Type: "banner", Text: "Banner", Symbol: Symbol.Pictures),
            (Type: "hero", Text: "Hero", Symbol: Symbol.FullScreen),
            (Type: "logo", Text: "Logo", Symbol: Symbol.Font),
            (Type: "icon", Text: "Icon", Symbol: Symbol.Emoji)
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

        var content = new Grid { RowSpacing = 14, Width = 1000, Height = 600 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        content.Children.Add(selector);
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
        dialog.Resources["ContentDialogMinWidth"] = 1040d;
        dialog.Resources["ContentDialogMaxWidth"] = 1040d;

        var selectedArtworks = new Dictionary<string, SteamGridArtworkOption>(StringComparer.OrdinalIgnoreCase);
        var activeArtworkType = "cover";
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
                artworks = await _uwpXbox.GetSteamGridDbArtworkAsync(game, artworkType, _settings.SteamGridDbApiKey);
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
                preview.Children.Add(new TextBlock
                {
                    Text = $"{artwork.Width}x{artwork.Height}",
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
        selector.SelectionChanged += async (_, _) =>
        {
            if (selector.SelectedItem?.Tag is string artworkType)
            {
                await LoadCategoryAsync(artworkType);
            }
        };
        dialog.Opened += async (_, _) => await LoadCategoryAsync("cover");

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
        _xboxSteamGridDbKeyBox.Text = _settings.SteamGridDbApiKey;
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
            // Il watcher di sicurezza è gestito da Playhub: non mostrarlo né
            // renderlo modificabile/rimovibile dall'utente.
            if (string.Equals(app.Name, "Playhub Desktop Safety", StringComparison.OrdinalIgnoreCase))
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
            var minimized = new ToggleSwitch { Header = "Avvia minimizzato", IsOn = app.StartMinimized };
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
        _gamingToggles[key] = toggle;
        panel.Children.Add(toggle);
    }

    // A toggle row with a title + plain-language explanation on the left and the switch on the right.
    private void AddExplainedToggle(FluentCard card, string title, string description, string key)
    {
        var toggle = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
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
        _gamingConfig.Gaming.ManageAudio = GetToggle("manageAudio");
        _gamingConfig.Safety.AllowRemoteApi = GetToggle("remoteApi");
        // No confirmation dialog when switching modes — always on.
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
        var content = new StackPanel
        {
            Spacing = 18,
            Padding = (Thickness)(Application.Current.Resources.TryGetValue("PlayhubPagePadding", out var value) && value is Thickness thickness
                ? thickness
                : new Thickness(36, 24, 36, 64)),
            Tag = tag,
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        content.Children.Add(new TextBlock { Text = title, Style = StyleResource("PlayhubPageTitleStyle") });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            content.Children.Add(new TextBlock { Text = subtitle, Style = StyleResource("PlayhubBodyTextStyle") });
        }
        return content;
    }

    private static FluentCard Card()
    {
        return new FluentCard();
    }

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        Style = StyleResource("PlayhubSectionTitleStyle")
    };

    private static TextBlock GroupTitle(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 0),
        Foreground = ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(210, 255, 255, 255))
    };

    private static TextBlock Body(string text) => new()
    {
        Text = text,
        Style = StyleResource("PlayhubBodyTextStyle")
    };

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
        button.Click += (_, _) => action();
        return button;
    }

    private Button Button(string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = text, Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle") };
        RegisterButton(button, primary);
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

    private static UIElement IconContent(string glyph, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private Button IconButton(string glyph, string text, Action action, bool primary = false)
    {
        var button = new Button { Content = IconContent(glyph, text), Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle") };
        RegisterButton(button, primary);
        button.Click += (_, _) => action();
        return button;
    }

    private Button IconButton(string glyph, string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = IconContent(glyph, text), Style = StyleResource(primary ? "PlayhubPrimaryButtonStyle" : "PlayhubSecondaryButtonStyle") };
        RegisterButton(button, primary);
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
        if (!primary)
        {
            return;
        }

        _primaryButtons.Add(button);
        ApplyAccentToButton(button);
    }

    private static FrameworkElement Labeled(string label, FrameworkElement element)
    {
        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = label, Opacity = 0.72 },
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

    private StackPanel BuildAccentPicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var color in new[] { "#FFCB0F", "#0F6CBD", "#107C10", "#C50F1F", "#8764B8" })
        {
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
            button.Click += async (_, _) =>
            {
                _settings.AccentColor = color;
                ApplyTheme();
                RefreshAccentPicker();
                await SaveSettingsSilentlyAsync();
            };
            panel.Children.Add(button);
        }

        return panel;
    }

    private void RefreshAccentPicker()
    {
        foreach (var button in _accentColorPanel.Children.OfType<Button>())
        {
            var selected = string.Equals(button.Tag?.ToString(), _settings.AccentColor, StringComparison.OrdinalIgnoreCase);
            button.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            button.BorderBrush = selected
                ? new SolidColorBrush(ParseColor(_settings.AccentColor))
                : ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(80, 128, 128, 128));
        }
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
        SetStatus("Controllo aggiornamenti in corso…", InfoBarSeverity.Informational);
        var info = await _updateService.CheckAsync(_settings.PlayhubUpdateRepository, GetAppVersion());

        if (info is null)
        {
            SetStatus("Non riesco a contattare GitHub per gli aggiornamenti. Riprova tra poco.", InfoBarSeverity.Warning);
            return;
        }

        if (info.IsNewer)
        {
            ShowUpdateNotification(info);
        }
        else
        {
            SetStatus("Playhub è aggiornato.", InfoBarSeverity.Success);
        }
    }

    // Controllo silenzioso all'avvio: notifica SOLO se c'è una versione nuova,
    // senza disturbare con messaggi di "tutto a posto" o errori di rete.
    private async Task CheckPlayhubUpdatesSilentlyAsync()
    {
        try
        {
            var info = await _updateService.CheckAsync(_settings.PlayhubUpdateRepository, GetAppVersion());
            if (info is { IsNewer: true })
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
            _status.IsOpen = false;
        };
        _status.ActionButton = openStore;
        _status.IsOpen = true;
    }

    private void ShowUpdateNotification(PlayhubUpdateService.UpdateInfo info)
    {
        WindowsToastService.ShowPlayhubUpdate(info.LatestVersion);
        _localizationKeys.AddOrUpdate(_status, "È disponibile una nuova versione di Playhub.");
        _status.Title = string.Format(T("Playhub {0} disponibile"), info.LatestVersion);
        _status.Message = T("È disponibile una nuova versione di Playhub.");
        _status.Severity = InfoBarSeverity.Success;

        if (!string.IsNullOrWhiteSpace(info.ReleaseUrl) &&
            Uri.TryCreate(info.ReleaseUrl, UriKind.Absolute, out var releaseUri))
        {
            var goButton = new Button
            {
                Content = T("Vai alla release"),
                Style = StyleResource("PlayhubPrimaryButtonStyle")
            };
            goButton.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(releaseUri);
            _status.ActionButton = goButton;
        }
        else
        {
            _status.ActionButton = null;
        }

        _status.IsOpen = true;
    }

    private async Task SaveSettingsSilentlyAsync()
    {
        await _settingsService.SaveAsync();
    }

    private void SetStatus(string message, InfoBarSeverity severity)
    {
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

    private void RestartPlayhub()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                SetStatus("Non riesco a riavviare Playhub. Chiudilo e riaprilo manualmente.", InfoBarSeverity.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
            Close();
        }
        catch
        {
            SetStatus("Non riesco a riavviare Playhub. Chiudilo e riaprilo manualmente.", InfoBarSeverity.Warning);
        }
    }

    private string TranslateMessage(string message)
    {
        if (message.StartsWith("Ho importato ", StringComparison.Ordinal) &&
            message.Contains(" giochi in Steam.", StringComparison.Ordinal))
        {
            var countText = message["Ho importato ".Length..].Split(' ', 2)[0];
            return string.Format(T("Ho importato {0} giochi in Steam. Ho creato anche un backup degli shortcut. Riavvia Steam per vederli."), countText);
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

        return TranslatePrefix(message, "Rimozione non riuscita: ") ??
               TranslatePrefix(message, "Installazione del plugin non riuscita: ") ??
               TranslatePrefix(message, "Rimozione del plugin non riuscita: ") ??
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

        // Le descrizioni dei plugin sono risolte per lingua al momento della
        // costruzione della card (e marcate "noloc"), quindi ricostruiamo le
        // card per riflettere subito la nuova lingua.
        if (_plugins.Count > 0)
        {
            RenderPluginCards();
        }
    }

    private void LocalizeElement(DependencyObject element)
    {
        LocalizeElement(element, new HashSet<DependencyObject>());
    }

    private void LocalizeElement(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element))
        {
            return;
        }

        // I sottoalberi marcati "noloc" (es. descrizioni dei plugin già tradotte
        // come blocco unico) non vanno ritradotti riga per riga.
        if (element is FrameworkElement { Tag: "noloc" })
        {
            return;
        }

        switch (element)
        {
            case TextBlock textBlock:
                textBlock.Text = T(GetLocalizationKey(textBlock, textBlock.Text));
                break;
            case Button { Content: string buttonText } button:
                button.Content = T(GetLocalizationKey(button, buttonText));
                break;
            case NavigationViewItem { Content: string itemText } item:
                item.Content = T(GetLocalizationKey(item, itemText));
                break;
            case Expander { Header: string header } expander:
                expander.Header = T(GetLocalizationKey(expander, header));
                break;
            case ToggleSwitch { Header: string header } toggle:
                toggle.Header = T(GetLocalizationKey(toggle, header));
                break;
            case TextBox textBox:
                textBox.PlaceholderText = T(GetLocalizationKey(textBox, textBox.PlaceholderText));
                break;
            case NumberBox numberBox when numberBox.Header is string header:
                numberBox.Header = T(GetLocalizationKey(numberBox, header));
                break;
            case InfoBar infoBar:
                infoBar.Message = _localizationKeys.TryGetValue(infoBar, out var infoKey)
                    ? TranslateMessage(infoKey)
                    : TranslateMessage(GetLocalizationKey(infoBar, infoBar.Message));
                // NON scendere nel template dell'InfoBar: impostare il Text del
                // TextBlock interno romperebbe il TemplateBinding alla proprietà
                // Message e i messaggi resterebbero vuoti. Message è già gestito qui.
                return;
        }

        if (element is ContentControl { Content: DependencyObject contentObject })
        {
            LocalizeElement(contentObject, visited);
        }

        if (element is Border { Child: DependencyObject borderChild })
        {
            LocalizeElement(borderChild, visited);
        }

        if (element is Panel panel)
        {
            foreach (var panelChild in panel.Children.OfType<DependencyObject>())
            {
                LocalizeElement(panelChild, visited);
            }
        }

        if (element is Expander { Content: DependencyObject expanderContent })
        {
            LocalizeElement(expanderContent, visited);
        }

        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++)
        {
            LocalizeElement(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i), visited);
        }
    }

    private string GetLocalizationKey(DependencyObject element, string currentText)
    {
        if (_localizationKeys.TryGetValue(element, out var key))
        {
            // Keep the original Italian key across every language change.
            // Replacing it with the currently translated text made the second
            // change irreversible (for example Italian -> English -> Italian).
            return key;
        }

        _localizationKeys.AddOrUpdate(element, currentText);
        return currentText;
    }

    private string FriendlyError(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return T("Windows ha bloccato l'accesso a un file. Riprova.");
        }

        return ex.Message;
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

        foreach (var button in _primaryButtons)
        {
            ApplyAccentToButton(button);
        }

        ApplyChrome(accent);
        RefreshAccentPicker();
        // Re-tint the Gaming Mode mode tiles (border/background/icons) with the new accent.
        UpdateModeTiles();
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
        // Selected tab text stays white (readable) like the hover state — not the accent colour.
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
       
