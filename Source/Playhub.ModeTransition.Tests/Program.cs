using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GamingMode.Models;
using GamingMode.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--preview-wide") || args.Contains("--preview-circles"))
        {
            ShowWidePreview(args.Contains("--preview-circles"), args.Contains("--until-escape"), args.Contains("--boot-logo"));
            return;
        }
        GamingModeConfigContract.Assert();
        string english = ModeTransitionVisual.Title(ModeKind.Gaming, "en");
        foreach (var (logo, scale) in new[] { ("xbox", 2d), ("playstation", 1.5), ("steam-deck", 1.5), ("rog", 1.5), ("base-logo", 1.2), ("steamos", 1.2), ("msi", 1.2), ("custom", 1d) })
            if (Playhub.Shared.SplashLogoScale.ForPath(logo + ".png") != scale)
                throw new Exception("Unexpected splash logo size: " + logo);
        int activeLaunches = 0, maxLaunches = 0;
        Task.WaitAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Playhub.Shared.DeckyStartupGuard.RunExclusive(() =>
        {
            int active = Interlocked.Increment(ref activeLaunches);
            maxLaunches = Math.Max(maxLaunches, active);
            Thread.Sleep(10);
            Interlocked.Decrement(ref activeLaunches);
            return true;
        }))).ToArray());
        if (maxLaunches != 1) throw new Exception("Concurrent Decky launches were not serialized");
        foreach (string locale in new[] { "en", "it", "es", "fr", "de", "pt", "uk", "zh", "ja", "ko", "hi", "ru" })
        {
            string gaming = ModeTransitionVisual.Title(ModeKind.Gaming, locale);
            string desktop = ModeTransitionVisual.Title(ModeKind.Desktop, locale);
            if (gaming == desktop || string.IsNullOrWhiteSpace(gaming) || (locale != "en" && gaming == english))
                throw new Exception("Missing transition translation: " + locale);
        }
        if (ModeTransitionVisual.Title(ModeKind.Gaming, "it-IT") != "Passaggio a Gaming Mode")
            throw new Exception("Regional language mismatch");
        var root = (Grid)ModeTransitionVisual.Create(ModeKind.Gaming, "it");
        var size = new Size(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
        var label = root.Children.OfType<TextBlock>().Single();
        if (root.Children.OfType<ReactiveCircleBackground>().Count() != 1)
            throw new Exception("Missing circle animation");
        if (root.Children.OfType<ReactiveCircleBackground>().Single().Opacity != 1)
            throw new Exception("Default animation opacity must be 100 percent");
        foreach (double percentage in new[] { 0d, 20d, 64d, 100d })
        {
            var visual = (Grid)ModeTransitionVisual.Create(ModeKind.Gaming, "en", new GamingSplashSettings { AnimationOpacity = percentage });
            if (Math.Abs(visual.Children.OfType<ReactiveCircleBackground>().Single().Opacity - percentage / 100) > .001)
                throw new Exception("Animation opacity mismatch");
        }
        var appSettings = new Playhub.Models.SplashOptions
        {
            AnimationColor = "#107C10", AnimationUseCustomColor = false,
            AnimationCustomHue = 274, AnimationCustomSaturation = 83,
            AnimationCustomBlackness = 27, AnimationOpacity = 53
        };
        var agentSettings = System.Text.Json.JsonSerializer.Deserialize<GamingSplashSettings>(System.Text.Json.JsonSerializer.Serialize(appSettings))!;
        var appRestored = System.Text.Json.JsonSerializer.Deserialize<Playhub.Models.SplashOptions>(System.Text.Json.JsonSerializer.Serialize(agentSettings))!;
        if (appRestored.AnimationColor != "#107C10" || appRestored.AnimationUseCustomColor ||
            appRestored.AnimationCustomHue != 274 || appRestored.AnimationCustomSaturation != 83 ||
            appRestored.AnimationCustomBlackness != 27 || appRestored.AnimationOpacity != 53)
            throw new Exception("App/agent must preserve custom settings while a preset is selected");
        var disabled = (Grid)ModeTransitionVisual.Create(ModeKind.Desktop, "en", new GamingSplashSettings { AnimationEnabled = false });
        if (disabled.Children.Count != 1 || disabled.Children[0] is not TextBlock)
            throw new Exception("Animation toggle must preserve the title only");
        var config = new GamingSplashSettings { AnimationEnabled = false, AnimationColor = "#73BCEB" };
        var restored = System.Text.Json.JsonSerializer.Deserialize<GamingSplashSettings>(System.Text.Json.JsonSerializer.Serialize(config))!;
        if (restored.AnimationEnabled || restored.AnimationColor != config.AnimationColor)
            throw new Exception("Animation preferences did not round-trip");
        if (label.FontWeight != FontWeights.Light || label.FontFamily.Source != "Segoe UI Light")
            throw new Exception("Transition font does not match Segoe UI Light");
        Point location = label.TranslatePoint(new Point(), root);
        if (Math.Abs(location.Y + label.ActualHeight / 2 - size.Height / 2) > 1)
            throw new Exception("Transition title is not vertically centered");
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string preview = Path.Combine(AppContext.BaseDirectory, "transition-preview.png");
        using (var stream = File.Create(preview)) encoder.Save(stream);
        Console.WriteLine("PASS: 12 locales, regional locale, centered title and offscreen render. " + preview);
    }

    private static void ShowWidePreview(bool circles, bool untilEscape, bool bootLogo)
    {
        var app = new Application();
        var root = (Grid)ModeTransitionVisual.Create(ModeKind.Gaming, "it");
        double width = SystemParameters.PrimaryScreenWidth;
        double height = SystemParameters.PrimaryScreenHeight;
        foreach (var label in root.Children.OfType<TextBlock>())
        {
            string title = label.Text;
            string? modeName = new[] { "Gaming Mode", "Desktop Mode" }.FirstOrDefault(title.Contains);
            if (modeName != null)
            {
                int start = title.IndexOf(modeName, StringComparison.Ordinal);
                label.Text = string.Empty;
                label.Inlines.Add(new System.Windows.Documents.Run(title[..start]));
                label.Inlines.Add(new System.Windows.Documents.Run(modeName)
                {
                    FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold
                });
                label.Inlines.Add(new System.Windows.Documents.Run(title[(start + modeName.Length)..]));
            }
            label.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, ShadowDepth = 0, BlurRadius = 12, Opacity = 1
            };
        }
        foreach (var glow in root.Children.Cast<UIElement>().Where(element => element is Border or ReactiveCircleBackground).ToArray()) root.Children.Remove(glow);
        var circleGrid = circles ? new ReactiveCircleGrid { IsHitTestVisible = false, Opacity = .64 } : null;
        if (circleGrid != null) root.Children.Insert(0, circleGrid);
        var transforms = new List<TranslateTransform>();
        var scales = new List<ScaleTransform>();
        var glowImages = new List<Image>();
        foreach (var color in circles ? Array.Empty<Color>() : new[] { Color.FromRgb(254,229,5) })
        {
            var transform = new TranslateTransform();
            transforms.Add(transform);
            var scale = new ScaleTransform(1, 1);
            scales.Add(scale);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(transform);
            var image = new Image
            {
                Source = GlowTexture(color), Width = width * .9, Height = height * 1.25,
                Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, RenderTransform = group,
                RenderTransformOrigin = new Point(.5, .5),
                IsHitTestVisible = false
            };
            glowImages.Add(image);
            root.Children.Insert(0, image);
        }
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        EventHandler render = (_, _) =>
        {
            if (circleGrid != null)
            {
                circleGrid.Seconds = elapsed.Elapsed.TotalSeconds * 2;
                circleGrid.InvalidateVisual();
            }
            for (int i = 0; i < transforms.Count; i++)
            {
                double angle = elapsed.Elapsed.TotalSeconds * Math.PI * 2 / 20 + i * Math.PI * 2 / 3;
                transforms[i].X = Math.Cos(angle) * width * .27;
                transforms[i].Y = Math.Sin(angle) * height * .28;
                double breath = .5 - .5 * Math.Cos(elapsed.Elapsed.TotalSeconds * Math.PI * 2 / 7);
                scales[i].ScaleX = scales[i].ScaleY = .9 + breath * .22;
                glowImages[i].Opacity = .72 + breath * .28;
            }
        };
        CompositionTarget.Rendering += render;
        if (bootLogo)
        {
            using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode", "config.json")));
            string path = config.RootElement.GetProperty("gaming").GetProperty("splash").GetProperty("logoPath").GetString()!;
            foreach (var label in root.Children.OfType<TextBlock>().ToArray()) root.Children.Remove(label);
            root.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri(path)), Stretch = Stretch.Uniform,
                Width = Math.Min(460, width * .28), Height = 180,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 12, Opacity = 1 }
            });
        }
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Left = 0, Top = 0, Width = width, Height = height,
            WindowState = WindowState.Maximized,
            Topmost = true, ShowInTaskbar = false, Background = Brushes.Black,
            Content = root
        };
        window.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) window.Close(); };
        window.Closed += (_, _) => { CompositionTarget.Rendering -= render; elapsed.Stop(); };
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
        window.ContentRendered += (_, _) => { if (!untilEscape) timer.Start(); };
        app.Run(window);
    }

    private static BitmapSource GlowTexture(Color color)
    {
        const int size = 2048;
        var pixels = new byte[size * size * 4];
        var random = new Random(71);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double dx = (x + .5) / size * 2 - 1, dy = (y + .5) / size * 2 - 1;
            double radius = Math.Sqrt(dx * dx + dy * dy);
            double fade = Math.Max(0, 1 - radius * radius);
            double alpha = Math.Exp(-5 * radius * radius) * fade * fade * .38;
            alpha = Math.Clamp(alpha * 255 + (random.NextDouble() - .5) * 14 * fade * fade, 0, 255);
            int offset = (y * size + x) * 4;
            pixels[offset] = (byte)Math.Round(color.B * alpha / 255);
            pixels[offset + 1] = (byte)Math.Round(color.G * alpha / 255);
            pixels[offset + 2] = (byte)Math.Round(color.R * alpha / 255);
            pixels[offset + 3] = (byte)Math.Round(alpha);
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }
}

internal sealed class ReactiveCircleGrid : FrameworkElement
{
    public double Seconds { get; set; }
    private readonly Pen[] pens = Enumerable.Range(0, 128).Select(index =>
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(8 + index * 1.75), 254, 229, 5)), 1.1);
        pen.Freeze();
        return pen;
    }).ToArray();

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        double angle = Seconds * Math.PI * 2 / 20;
        double breath = .5 - .5 * Math.Cos(Seconds * Math.PI * 2 / 7);
        double cx = width * (.5 + .27 * Math.Cos(angle));
        double cy = height * (.5 + .28 * Math.Sin(angle));
        double spread = .9 + .22 * breath;
        const double pitch = 24;
        for (double y = pitch / 2; y < height; y += pitch)
        for (double x = pitch / 2; x < width; x += pitch)
        {
            double dx = (x - cx) / (width * .36 * spread);
            double dy = (y - cy) / (height * .55 * spread);
            double strength = Math.Exp(-3 * (dx * dx + dy * dy)) * (.72 + .28 * breath);
            double radius = .65 + strength * (pitch * .43 - .65);
            context.DrawEllipse(null, pens[Math.Clamp((int)(strength * 127), 0, 127)], new Point(x, y), radius, radius);
        }
    }
}


// IL CONTRATTO FRA APP E AGENTE, VERIFICATO.
//
// config.json lo scrivono in due. Se l'app aggiunge un'opzione che l'agente non
// conosce, quell'opzione non fa niente e sparisce alla prima riscrittura
// dell'agente (GamingSettings non ha [JsonExtensionData]); se i due lati partono
// da default diversi, la stessa installazione si comporta in modo diverso a
// seconda di chi ha creato il file per primo. Erano entrambi difetti invisibili.
//
// Le liste qui sotto sono le UNICHE divergenze accettate oggi. Aggiungerne una
// nuova richiede di scriverla qui, cioe' di accorgersene.
internal static class GamingModeConfigContract
{
    // Scritte dall'app, ignorate dall'agente. Ogni voce e' un difetto noto.
    private static readonly string[] KnownAppOnlyGamingKeys =
    {
        // L'agente non la conosce: la verita' durevole vive in PlayhubSettings
        // (XboxGameBarEnabled) e l'effetto passa da CustomStartupApps.
        "EnableXboxGameBar"
    };

    // Default diversi fra app e agente: chi crea config.json per primo vince.
    private static readonly string[] KnownDefaultDivergences =
    {
        "Gaming.CloseExplorerInGamingMode",
        "Gaming.AllowExplorerCloseInGamingMode"
    };

    internal static void Assert()
    {
        ComparePair("Gaming", new Playhub.Models.GamingOptions(), new GamingSettings(), KnownAppOnlyGamingKeys);
        ComparePair("Safety", new Playhub.Models.SafetyOptions(), new SafetySettings(), Array.Empty<string>());
        ComparePair("Gaming.Splash", new Playhub.Models.SplashOptions(), new GamingSplashSettings(), Array.Empty<string>());
        ComparePair("", new Playhub.Models.GamingModeConfig(), new ModeConfig(), Array.Empty<string>(),
            skip: new[] { "DefaultMode", "NextBootMode", "Gaming", "Safety" });
        CompareNames("Gaming.CustomStartupApps[]", typeof(Playhub.Models.StartupAppConfig), typeof(GamingStartupApp), Array.Empty<string>());
    }

    private static void ComparePair(string prefix, object app, object agent, string[] knownAppOnly, string[]? skip = null)
    {
        var agentType = agent.GetType();
        foreach (var property in app.GetType().GetProperties())
        {
            // JsonExtensionData: e' il contenitore delle chiavi altrui, non una chiave.
            if (property.Name == "Extra") continue;
            if (skip is not null && skip.Contains(property.Name)) continue;

            var mirror = agentType.GetProperty(property.Name);
            if (mirror is null)
            {
                if (knownAppOnly.Contains(property.Name)) continue;
                throw new Exception($"L'app scrive {Join(prefix, property.Name)} ma l'agente non la legge (e la cancella al primo salvataggio).");
            }

            if (knownAppOnly.Contains(property.Name))
                throw new Exception($"{Join(prefix, property.Name)} risulta ora letta dall'agente: toglila da KnownAppOnlyGamingKeys.");

            // I valori composti (Splash, liste) si confrontano a parte.
            if (!property.PropertyType.IsPrimitive && property.PropertyType != typeof(string) &&
                property.PropertyType != typeof(decimal)) continue;

            object? appDefault = property.GetValue(app);
            object? agentDefault = mirror.GetValue(agent);
            bool same = Equals(appDefault?.ToString(), agentDefault?.ToString());
            bool known = KnownDefaultDivergences.Contains(Join(prefix, property.Name));
            if (!same && !known)
                throw new Exception($"Default diverso per {Join(prefix, property.Name)}: app={appDefault ?? "null"}, agente={agentDefault ?? "null"}.");
            if (same && known)
                throw new Exception($"{Join(prefix, property.Name)} ora coincide: toglila da KnownDefaultDivergences.");
        }
    }

    private static void CompareNames(string prefix, Type app, Type agent, string[] knownAppOnly)
    {
        foreach (var property in app.GetProperties())
        {
            if (property.Name == "Extra") continue;
            if (agent.GetProperty(property.Name) is null && !knownAppOnly.Contains(property.Name))
                throw new Exception($"L'app scrive {Join(prefix, property.Name)} ma l'agente non la legge.");
        }
    }

    private static string Join(string prefix, string name) => prefix.Length == 0 ? name : prefix + "." + name;
}
