using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Windows.Media.Effects;
using GamingMode.Models;

namespace GamingMode.Services;

internal static class ModeTransitionVisual
{
    internal static string ResolveLanguage(string? fallback)
    {
        // Playhub's language is authoritative, including changes made since agent startup.
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playhub", "settings.json");
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(candidate));
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name.Equals("Language", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        return Normalize(property.Value.GetString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        return Normalize(fallback);
    }

    private static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return language.Trim().ToLowerInvariant().Split('-', '_')[0];
    }

    internal static string Title(ModeKind mode, string language)
    {
        bool gaming = mode == ModeKind.Gaming;
        return Normalize(language) switch
        {
            "it" => gaming ? "Passaggio a Gaming Mode" : "Passaggio a Desktop Mode",
            "es" => gaming ? "Cambiando a Gaming Mode" : "Cambiando a Desktop Mode",
            "fr" => gaming ? "Passage en Gaming Mode" : "Passage en Desktop Mode",
            "de" => gaming ? "Wechsel zu Gaming Mode" : "Wechsel zu Desktop Mode",
            "pt" => gaming ? "Mudando para Gaming Mode" : "Mudando para Desktop Mode",
            "uk" or "ua" => gaming ? "Перехід до Gaming Mode" : "Перехід до Desktop Mode",
            "zh" => gaming ? "正在切换到游戏模式" : "正在切换到桌面模式",
            "ja" => gaming ? "ゲーミングモードに切り替え中" : "デスクトップモードに切り替え中",
            "ko" => gaming ? "게이밍 모드로 전환 중" : "데스크톱 모드로 전환 중",
            "hi" => gaming ? "Gaming Mode में जा रहे हैं" : "Desktop Mode में जा रहे हैं",
            "ru" => gaming ? "Переход в Gaming Mode" : "Переход в Desktop Mode",
            _ => gaming ? "Switching to Gaming Mode" : "Switching to Desktop Mode"
        };
    }

    internal static FrameworkElement Create(ModeKind mode, string? language, GamingSplashSettings? settings = null)
    {
        var root = new Grid { Background = Brushes.Black, ClipToBounds = true, IsHitTestVisible = false };
        AddGlows(root, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight, settings);
        var title = new TextBlock
        {
            Text = Title(mode, ResolveLanguage(language)), Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"), FontSize = 72, FontWeight = FontWeights.Light,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(48, 0, 48, 0), Effect = TextShadow()
        };
        string text = title.Text;
        string modeName = mode == ModeKind.Gaming ? "Gaming Mode" : "Desktop Mode";
        int position = text.IndexOf(modeName, StringComparison.Ordinal);
        if (position >= 0)
        {
            title.Inlines.Clear();
            title.Inlines.Add(new Run(text[..position]));
            title.Inlines.Add(new Run(modeName) { FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.SemiBold });
            title.Inlines.Add(new Run(text[(position + modeName.Length)..]));
        }
        root.Children.Add(title);
        return root;
    }

    internal static DropShadowEffect TextShadow() => new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 12, Opacity = 1 };

    internal static void AddGlows(Grid root, double width, double height, GamingSplashSettings? settings = null)
    {
        if (settings?.AnimationEnabled == false) return;
        Color color = Color.FromRgb(255, 203, 15);
        try { color = (Color)ColorConverter.ConvertFromString(settings?.AnimationColor ?? "#FFCB0F"); }
        catch (FormatException) { }
        catch (ArgumentException) { }
        double opacity = settings?.AnimationOpacity ?? 100;
        if (!double.IsFinite(opacity)) opacity = 100;
        root.Children.Add(new ReactiveCircleBackground(color) { IsHitTestVisible = false, Opacity = Math.Clamp(opacity, 0, 100) / 100 });
    }

    private static void AddLegacyGlows(Grid root, double width, double height)
    {
        // Diffuse layers and alternate drift follow Now Playing's Glow effect.
        AddGlow(root, Color.FromRgb(254, 229, 5), HorizontalAlignment.Left, 18.5, width, height);
        AddGlow(root, Color.FromRgb(238, 156, 65), HorizontalAlignment.Right, 23.8, width, height);
        AddGlow(root, Color.FromRgb(221, 182, 1), HorizontalAlignment.Center, 21.2, width, height);
    }

    private static void AddGlow(Grid root, Color color, HorizontalAlignment alignment, double seconds, double width, double height)
    {
        bool reverse = alignment == HorizontalAlignment.Right;
        var brush = new RadialGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(92, color.R, color.G, color.B), 0),
                new(Color.FromArgb(40, color.R, color.G, color.B), .28),
                new(Color.FromArgb(0, color.R, color.G, color.B), 1)
            }
        };
        var translation = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        var transforms = new TransformGroup();
        transforms.Children.Add(scale);
        transforms.Children.Add(translation);
        var glow = new Border
        {
            Width = width * .85, Height = height * .95, Background = brush,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -height * .38),
            RenderTransform = transforms, RenderTransformOrigin = new Point(.5, .5),
            Opacity = alignment == HorizontalAlignment.Center ? .6 : .85
        };
        root.Children.Add(glow);
        if (!SystemParameters.ClientAreaAnimation) return;
        DoubleAnimation Drift(double from, double to) => new(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        translation.BeginAnimation(TranslateTransform.XProperty, Drift(width * (reverse ? .06 : -.06), width * (reverse ? -.07 : .07)));
        translation.BeginAnimation(TranslateTransform.YProperty, Drift(-height * .03, height * .05));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Drift(.92, 1.08));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Drift(.92, 1.08));
    }
}
