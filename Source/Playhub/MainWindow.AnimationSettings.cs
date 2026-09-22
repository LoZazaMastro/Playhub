using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Playhub.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;

namespace Playhub;

public sealed partial class MainWindow
{
    private Action? _refreshAnimationSettings;

    private static Button CreateAccentSwatch(string color)
    {
        var button = new Button
        {
            Tag = color, Width = 44, Height = 34, Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Content = new Border { Width = 26, Height = 18, Background = new SolidColorBrush(ParseColor(color)) }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, color);
        return button;
    }

    private FluentCard BuildAnimationCard()
    {
        var card = Card();
        card.Children.Add(IconHeader(((char)0xE790).ToString(), "Animazione Gaming Mode", "All'avvio e durante il cambio modalita."));
        AddExplainedToggle(card, "Attiva animazione", "Il colore e indipendente dall'accent di Playhub.", "splashAnimation");
        var palette = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var swatches = new List<Button>();
        var customButton = new Button
        {
            MinWidth = 64, MinHeight = 100, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Colors.Transparent)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(customButton, T("Colore personalizzato"));
        var hue = new Slider { Minimum = 0, Maximum = 360, StepFrequency = 1 };
        var saturation = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1 };
        var blackness = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1 };
        var hueTrack = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, .5), EndPoint = new Windows.Foundation.Point(1, .5) };
        var saturationTrack = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, .5), EndPoint = new Windows.Foundation.Point(1, .5) };
        var blacknessTrack = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, .5), EndPoint = new Windows.Foundation.Point(1, .5) };
        StyleAnimationSlider(hue, hueTrack);
        StyleAnimationSlider(saturation, saturationTrack);
        StyleAnimationSlider(blackness, blacknessTrack);
        bool refreshing = false;
        var saveTimer = DispatcherQueue.CreateTimer();
        saveTimer.Interval = TimeSpan.FromMilliseconds(200);
        saveTimer.IsRepeating = false;
        saveTimer.Tick += (_, _) => AutoSaveGaming();
        void QueueSave() { saveTimer.Stop(); saveTimer.Start(); }

        void Refresh()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                var settings = _gamingConfig.Gaming.Splash;
                hue.Value = ClampAnimationValue(settings.AnimationCustomHue, 360);
                saturation.Value = ClampAnimationValue(settings.AnimationCustomSaturation, 100);
                blackness.Value = ClampAnimationValue(settings.AnimationCustomBlackness, 100);
                var color = AnimationCustomColor(hue.Value, saturation.Value, blackness.Value);
                var customBrush = new SolidColorBrush(color);
                customButton.Background = customBrush;
                foreach (string state in new[] { "PointerOver", "Pressed" })
                    customButton.Resources["ButtonBackground" + state] = customBrush;
                hueTrack.GradientStops.Clear();
                saturationTrack.GradientStops.Clear();
                blacknessTrack.GradientStops.Clear();
                for (int i = 0; i <= 36; i++)
                {
                    double t = i / 36d;
                    hueTrack.GradientStops.Add(new GradientStop { Offset = t, Color = AnimationCustomColor(t * 360, saturation.Value, blackness.Value) });
                    saturationTrack.GradientStops.Add(new GradientStop { Offset = t, Color = AnimationCustomColor(hue.Value, t * 100, blackness.Value) });
                    blacknessTrack.GradientStops.Add(new GradientStop { Offset = t, Color = AnimationCustomColor(hue.Value, saturation.Value, t * 100) });
                }
                customButton.BorderThickness = new Thickness(settings.AnimationUseCustomColor ? 2 : 1);
                customButton.BorderBrush = settings.AnimationUseCustomColor
                    ? ResourceBrush("TextFillColorPrimaryBrush", Colors.White)
                    : ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(80, 128, 128, 128));
                foreach (var swatch in swatches)
                {
                    bool selected = !settings.AnimationUseCustomColor && string.Equals(swatch.Tag?.ToString(), settings.AnimationColor, StringComparison.OrdinalIgnoreCase);
                    swatch.BorderThickness = new Thickness(selected ? 2 : 1);
                    swatch.BorderBrush = selected ? new SolidColorBrush(ParseColor(swatch.Tag!.ToString()!))
                        : ResourceBrush("ControlStrokeColorDefaultBrush", Color.FromArgb(80, 128, 128, 128));
                }
            }
            finally { refreshing = false; }
        }

        foreach (string color in AccentPalette)
        {
            var swatch = CreateAccentSwatch(color);
            swatch.Click += (_, _) =>
            {
                _gamingConfig.Gaming.Splash.AnimationUseCustomColor = false;
                _gamingConfig.Gaming.Splash.AnimationColor = color;
                Refresh();
                AutoSaveGaming();
            };
            palette.Children.Add(swatch);
            swatches.Add(swatch);
        }
        card.Children.Add(palette);

        var variants = ChoiceCombo(new[]
        {
            new ComboOption("boot", "Avvio"),
            new ComboOption("desktop", "Passaggio alla Desktop Mode"),
            new ComboOption("gaming", "Passaggio alla Gaming Mode")
        });
        SelectComboKey(variants, "boot");
        var previewButton = Button("Provalo ora", async () => await PreviewAnimationAsync(GetComboKey(variants) ?? "boot"), primary: true);
        var previewRow = new Grid { ColumnSpacing = 12 };
        previewRow.ColumnDefinitions.Add(new ColumnDefinition());
        previewRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        variants.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(previewButton, 1);
        previewRow.Children.Add(variants);
        previewRow.Children.Add(previewButton);
        card.Children.Add(previewRow);
        var previewHint = Body("Premi ESC o fai clic per tornare a Playhub");
        previewHint.HorizontalAlignment = HorizontalAlignment.Right;
        previewHint.TextAlignment = TextAlignment.Right;
        card.Children.Add(previewHint);

        var controls = new StackPanel { Spacing = 8 };
        controls.Children.Add(AnimationSlider("H", "Tonalita", hue, true));
        controls.Children.Add(AnimationSlider("S", "Saturazione", saturation));
        controls.Children.Add(AnimationSlider("B", "Livello di nero", blackness));
        var editor = new Grid { ColumnSpacing = 20 };
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        Grid.SetColumn(controls, 1);
        editor.Children.Add(customButton);
        editor.Children.Add(controls);
        card.Children.Add(new Expander
        {
            Header = "Colore personalizzato", Content = editor,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        });

        void SelectCustom()
        {
            var settings = _gamingConfig.Gaming.Splash;
            settings.AnimationCustomHue = hue.Value;
            settings.AnimationCustomSaturation = saturation.Value;
            settings.AnimationCustomBlackness = blackness.Value;
            settings.AnimationUseCustomColor = true;
            var color = AnimationCustomColor(hue.Value, saturation.Value, blackness.Value);
            settings.AnimationColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            Refresh();
            QueueSave();
        }
        customButton.Click += (_, _) => SelectCustom();
        foreach (var slider in new[] { hue, saturation, blackness })
            slider.ValueChanged += (_, _) => { if (!refreshing && !_loadingGaming) SelectCustom(); };
        _refreshAnimationSettings = Refresh;
        palette.Loaded += (_, _) => Refresh();
        palette.Unloaded += (_, _) =>
        {
            if (!saveTimer.IsRunning) return;
            saveTimer.Stop();
            AutoSaveGaming();
        };
        return card;
    }

    private static void StyleAnimationSlider(Slider slider, LinearGradientBrush track)
    {
        slider.Background = track;
        slider.Foreground = new SolidColorBrush(Colors.Transparent);
        slider.Resources["SliderTrackThemeHeight"] = 8d;
        foreach (string state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            slider.Resources["SliderTrackFill" + state] = track;
            slider.Resources["SliderTrackValueFill" + state] = new SolidColorBrush(Colors.Transparent);
        }
    }

    private FrameworkElement AnimationSlider(string letter, string name, Slider slider, bool degrees = false)
    {
        var panel = new StackPanel { Spacing = 0 };
        var heading = new Grid();
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new TextBlock { Text = name, FontSize = 12 };
        var letterLabel = new TextBlock { Text = letter, FontSize = 12, Tag = "noloc", Width = 18 };
        var labels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        labels.Children.Add(letterLabel);
        labels.Children.Add(label);
        var value = new TextBlock { FontSize = 12, Opacity = .7, Tag = "noloc" };
        void RefreshValue() => value.Text = degrees ? $"{slider.Value:0}\u00b0" : $"{slider.Value:0}%";
        slider.ValueChanged += (_, _) => RefreshValue();
        RefreshValue();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, T(name));
        Grid.SetColumn(value, 1);
        heading.Children.Add(labels);
        heading.Children.Add(value);
        panel.Children.Add(heading);
        panel.Children.Add(slider);
        return panel;
    }

    private static double ClampAnimationValue(double value, double max) => double.IsFinite(value) ? Math.Clamp(value, 0, max) : max;

    internal static Color AnimationCustomColor(double hue, double saturation, double blackness)
    {
        double h = ClampAnimationValue(hue, 360) % 360 / 60;
        double s = ClampAnimationValue(saturation, 100) / 100;
        double v = 1 - ClampAnimationValue(blackness, 100) / 100;
        double c = v * s, x = c * (1 - Math.Abs(h % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x),
            < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x)
        };
        return Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private bool _animationPreviewRunning;
    private async Task PreviewAnimationAsync(string variant)
    {
        if (_animationPreviewRunning) return;
        _animationPreviewRunning = true;
        try
        {
            var settings = _gamingConfig.Gaming.Splash;
            settings.LogoPath = ResolveSplashLogo();
            settings.AnimationEnabled = _gamingToggles["splashAnimation"].IsOn;
            var start = new ProcessStartInfo(Path.Combine(AppPaths.GamingModePackage, "GamingMode.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = AppPaths.GamingModePackage
            };
            start.ArgumentList.Add("preview-animation");
            start.ArgumentList.Add(variant);
            start.ArgumentList.Add(Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(settings)));
            start.ArgumentList.Add(_settings.Language ?? "auto");
            using var process = Process.Start(start) ?? throw new InvalidOperationException();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException("Animation preview failed.");
        }
        catch (Exception ex)
        {
            Diag.Crash("PreviewAnimation", ex);
            SetStatus("Impossibile aprire l'anteprima. Riprova.", InfoBarSeverity.Error);
        }
        finally { _animationPreviewRunning = false; Activate(); }
    }
}
