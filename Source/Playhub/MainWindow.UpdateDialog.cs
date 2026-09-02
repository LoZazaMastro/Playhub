using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace Playhub;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _playhubUpdateNoticedVersions = new(StringComparer.OrdinalIgnoreCase);
    private ContentDialog? _playhubUpdateDialog;
    private Border? _playhubUpdateDialogContent;
    private Button? _playhubUpdateDialogCloseButton;
    private Button? _playhubUpdateDialogActionButton;
    private ProgressBar? _playhubUpdateDialogProgressBar;
    private TextBlock? _playhubUpdateDialogStatus;
    private ScrollViewer? _playhubUpdateDialogChangelog;
    private Func<Task>? _playhubUpdateDialogInvokeUpdate;
    private Action<double, double>? _playhubUpdateDialogResize;
    private StackPanel? _playhubUpdateDialogNotesBody;
    private CancellationTokenSource? _playhubUpdateDialogTranslationCancellation;
    private string _playhubUpdateDialogNotesLanguage = "";
    private ReleaseNotesTranslationService _playhubReleaseNotesTranslation = new();

    // Operation state outlives the native dialog. Only the existing updater owns
    // _playhubUpdateRunning; setting it here would suppress its download callback.
    private PlayhubUpdateService.UpdateInfo? _playhubUpdateDialogInfo;
    private bool _playhubUpdateDialogActionPending;
    private bool _playhubUpdateDialogFailed;
    private bool _playhubUpdateDialogHasProgress;
    private double? _playhubUpdateDialogFraction;
    private string _playhubUpdateDialogStatusText = "";

    private async void ShowPlayhubUpdateDialog(PlayhubUpdateService.UpdateInfo info, bool force = false)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowPlayhubUpdateDialog(info, force));
            return;
        }

        if (_playhubUpdateDialog is not null) return;
        var displayInfo = SelectPlayhubUpdateDialogInfo(info);
        if (!force && _playhubUpdateNoticedVersions.Contains(displayInfo.LatestVersion)) return;
        var xamlRoot = Content?.XamlRoot;
        if (xamlRoot is null) return;

        ContentDialog? dialog = null;
        Action<double, double>? resize = null;
        void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
            => resize?.Invoke(Math.Max(0, sender.Size.Width - 48), Math.Max(0, sender.Size.Height - 48));

        try
        {
            dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Background = new SolidColorBrush(Color.FromArgb(255, 57, 57, 57)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                DefaultButton = ContentDialogButton.None,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            AutomationProperties.SetName(dialog, $"Playhub {displayInfo.LatestVersion}");
            dialog.Resources["ContentDialogMinWidth"] = 0d;
            dialog.Resources["ContentDialogMaxWidth"] = 640d;
            dialog.Resources["ContentDialogMinHeight"] = 0d;
            dialog.Resources["ContentDialogMaxHeight"] = double.PositiveInfinity;
            dialog.Resources["ContentDialogPadding"] = new Thickness(0);
            dialog.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
            dialog.Resources["ContentDialogTopOverlay"] = new SolidColorBrush(Colors.Transparent);
            dialog.Content = BuildPlayhubUpdateDialogContent(displayInfo,
                () => DownloadAndInstallUpdateAsync(displayInfo), dialog.Hide,
                Math.Max(0, xamlRoot.Size.Width - 48), Math.Max(0, xamlRoot.Size.Height - 48));
            resize = _playhubUpdateDialogResize;
            dialog.Opened += (_, _) =>
            {
                _playhubUpdateNoticedVersions.Add(displayInfo.LatestVersion);
                _playhubUpdateDialogCloseButton?.Focus(FocusState.Programmatic);
#if !PLAYHUB_UI_REVIEW
                _ = TranslatePlayhubUpdateDialogNotesAsync(displayInfo, dialog);
#endif
            };
            dialog.Closing += (_, args) =>
            {
                if (_playhubUpdateRunning || _playhubUpdateDialogActionPending)
                {
                    args.Cancel = true;
                    return;
                }
                if (ReferenceEquals(_playhubUpdateDialog, dialog)) CancelPlayhubUpdateDialogTranslation();
            };
            _playhubUpdateDialog = dialog;
            xamlRoot.Changed += OnRootChanged;
            ConfigureDialogEntrance(dialog);
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // Another native dialog may own this XamlRoot. A failed show does not
            // consume the once-per-version notice or affect an active download.
            Diag.Crash(nameof(ShowPlayhubUpdateDialog), ex);
        }
        finally
        {
            xamlRoot.Changed -= OnRootChanged;
            if (ReferenceEquals(_playhubUpdateDialog, dialog))
            {
                CancelPlayhubUpdateDialogTranslation();
                _playhubUpdateDialog = null;
                _playhubUpdateDialogContent = null;
                _playhubUpdateDialogCloseButton = null;
                _playhubUpdateDialogActionButton = null;
                _playhubUpdateDialogProgressBar = null;
                _playhubUpdateDialogStatus = null;
                _playhubUpdateDialogChangelog = null;
                _playhubUpdateDialogInvokeUpdate = null;
                _playhubUpdateDialogResize = null;
                _playhubUpdateDialogNotesBody = null;
            }
        }
    }

    private PlayhubUpdateService.UpdateInfo SelectPlayhubUpdateDialogInfo(PlayhubUpdateService.UpdateInfo info)
    {
        if ((_playhubUpdateRunning || _playhubUpdateDialogActionPending) && _playhubUpdateDialogInfo is not null)
            return _playhubUpdateDialogInfo;

        if (!string.Equals(_playhubUpdateDialogInfo?.LatestVersion, info.LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            _playhubUpdateDialogFailed = false;
            _playhubUpdateDialogHasProgress = false;
            _playhubUpdateDialogFraction = null;
            _playhubUpdateDialogStatusText = "";
        }
        _playhubUpdateDialogInfo = info;
        return info;
    }

    private Border BuildPlayhubUpdateDialogContent(PlayhubUpdateService.UpdateInfo info,
        Func<Task> update, Action close, double width, double maxHeight)
    {
        CancelPlayhubUpdateDialogTranslation();
        var translationLifetime = new CancellationTokenSource();
        _playhubUpdateDialogTranslationCancellation = translationLifetime;
        _playhubUpdateDialogNotesLanguage = LocalizationService.ResolveLanguage(_settings.Language);
        var layout = new Grid { Padding = new Thickness(30, 40, 30, 24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 0 });
        var content = new Border
        {
            Name = "PlayhubUpdateDialogContent",
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Color.FromArgb(255, 57, 57, 57)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 91, 91, 91)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        var overlay = new Grid();
        overlay.Children.Add(layout);
        content.Child = overlay;
        var header = new StackPanel { MaxWidth = 280, HorizontalAlignment = HorizontalAlignment.Center };
        Image BrandImage(string file, double imageWidth, double imageHeight) => new()
        {
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", file))),
            Width = imageWidth,
            Height = imageHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false
        };
        var cube = BrandImage("cube.png", 108, 108);
        var wordmark = BrandImage("playhub-wordmark-white.png", 216, 57.024);
        wordmark.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetName(wordmark, "Playhub");
        header.Children.Add(cube);
        header.Children.Add(wordmark);
        header.Children.Add(new TextBlock
        {
            Name = "PlayhubUpdateDialogVersion",
            Text = info.LatestVersion,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.Light,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        layout.Children.Add(header);

        var actions = new StackPanel { Margin = new Thickness(0, 36, 0, 16) };
        var actionButton = new Button
        {
            Name = "PlayhubUpdateDialogAction",
            Content = T("Aggiorna ora"),
            Style = StyleResource("PlayhubPrimaryButtonStyle"),
            MinWidth = 0,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        RegisterButton(actionButton, primary: true);
        var preferredActionWidth = CompactPrimaryActionWidth(actionButton);
        actionButton.Width = preferredActionWidth;
        AutomationProperties.SetAutomationId(actionButton, "PlayhubUpdateDialogAction");
        var progress = new ProgressBar
        {
            Name = "PlayhubUpdateDialogProgress",
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = 4,
            MinHeight = 4,
            Width = preferredActionWidth,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ResourceBrush("AccentFillColorDefaultBrush", ParseColor(_settings.AccentColor)),
            Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            IsHitTestVisible = false
        };
        AutomationProperties.SetName(progress, T("Aggiornamento Playhub"));
        var status = new TextBlock
        {
            Name = "PlayhubUpdateDialogStatus",
            FontSize = 12,
            LineHeight = 17,
            Height = 34,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))
        };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        actions.Children.Add(actionButton);
        actions.Children.Add(progress);
        actions.Children.Add(status);
        Grid.SetRow(actions, 1);
        layout.Children.Add(actions);

        // Original notes render immediately through the shared Markdown reader.
        var notes = new StackPanel { Spacing = 16 };
        notes.Children.Add(new TextBlock
        {
            Text = T("Novità"),
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White)
        });
        var notesBody = new StackPanel { Spacing = 8 };
        notes.Children.Add(notesBody);
        if (!string.IsNullOrWhiteSpace(info.Notes)) notesBody.Children.Add(BuildDescription(info.Notes));
        else
        {
            notesBody.Children.Add(new TextBlock
            {
                Text = T("Note di rilascio non disponibili."),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.White)
            });
            if (Uri.TryCreate(info.ReleaseUrl, UriKind.Absolute, out var releaseUri) &&
                releaseUri.Scheme == Uri.UriSchemeHttps)
                notesBody.Children.Add(new HyperlinkButton { Content = T("Apri su GitHub"), NavigateUri = releaseUri });
        }
        var changelog = new ScrollViewer
        {
            Name = "PlayhubUpdateDialogChangelog",
            Content = notes,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            ZoomMode = ZoomMode.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 0, 8, 0),
            MinHeight = 0
        };
        AutomationProperties.SetName(changelog, T("Note di rilascio"));
        Grid.SetRow(changelog, 2);
        layout.Children.Add(changelog);
        var closeButton = new Button
        {
            Name = "PlayhubUpdateDialogClose",
            Content = new FontIcon { Glyph = "\uE8BB", FontSize = 14 },
            Width = 32,
            Height = 32,
            MinWidth = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(4),
            TabIndex = 0
        };
        SetLocalizedToolTip(closeButton, "Chiudi");
        AutomationProperties.SetName(closeButton, T("Chiudi"));
        AutomationProperties.SetAutomationId(closeButton, "PlayhubUpdateDialogClose");
        void CancelThisTranslation()
        {
            if (ReferenceEquals(_playhubUpdateDialogTranslationCancellation, translationLifetime))
                CancelPlayhubUpdateDialogTranslation();
        }
        closeButton.Click += (_, _) =>
        {
            if (_playhubUpdateRunning || _playhubUpdateDialogActionPending) return;
            CancelThisTranslation();
            close();
        };
        content.Unloaded += (_, _) => CancelThisTranslation();
        overlay.Children.Add(closeButton);
        actionButton.TabIndex = 1;
        changelog.TabIndex = 2;

        _playhubUpdateDialogContent = content;
        _playhubUpdateDialogCloseButton = closeButton;
        _playhubUpdateDialogActionButton = actionButton;
        _playhubUpdateDialogProgressBar = progress;
        _playhubUpdateDialogStatus = status;
        _playhubUpdateDialogChangelog = changelog;
        _playhubUpdateDialogNotesBody = notesBody;
        var invokeUpdate = new Func<Task>(() => InvokePlayhubUpdateDialogUpdateAsync(info, update));
        _playhubUpdateDialogInvokeUpdate = invokeUpdate;
        actionButton.Click += async (_, _) => await invokeUpdate();
        _playhubUpdateDialogResize = (availableWidth, availableHeight) =>
        {
            content.Width = Math.Min(640, Math.Max(0, availableWidth));
            content.MaxHeight = Math.Min(740, Math.Max(0, availableHeight));
            var compact = content.MaxHeight < 560;
            cube.Width = cube.Height = compact ? 72 : 108;
            wordmark.Width = Math.Min(compact ? 180 : 216, Math.Max(0, content.Width - 86));
            wordmark.Height = wordmark.Width * 132 / 500;
            var innerWidth = Math.Max(0, content.Width - 62);
            actionButton.MaxWidth = progress.MaxWidth = innerWidth;
            actionButton.Width = progress.Width = Math.Min(preferredActionWidth, innerWidth);
            header.Measure(new Size(innerWidth, double.PositiveInfinity));
            actions.Measure(new Size(innerWidth, double.PositiveInfinity));
            var verticalChrome = layout.Padding.Top + layout.Padding.Bottom + content.BorderThickness.Top + content.BorderThickness.Bottom;
            changelog.MaxHeight = Math.Max(0, content.MaxHeight - verticalChrome - header.DesiredSize.Height - actions.DesiredSize.Height);
        };
        _playhubUpdateDialogResize(width, maxHeight);
        RefreshPlayhubUpdateDialogState();
        return content;
    }

    private double CompactPrimaryActionWidth(Button button)
    {
        var label = new TextBlock
        {
            Text = T("Aggiorna ora"), FontFamily = button.FontFamily, FontSize = button.FontSize,
            FontWeight = button.FontWeight, FontStyle = button.FontStyle,
            FontStretch = button.FontStretch, CharacterSpacing = button.CharacterSpacing
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return (216 + label.DesiredSize.Width) / 2;
    }

    // An explicitly supplied provider is the only route to network translation.
    // The default service is unconfigured; review builds never invoke it.
    internal void ConfigurePlayhubReleaseNotesTranslation(ReleaseNotesTranslationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        CancelPlayhubUpdateDialogTranslation();
        _playhubReleaseNotesTranslation = service;
    }

    private void CancelPlayhubUpdateDialogTranslation()
    {
        var cancellation = _playhubUpdateDialogTranslationCancellation;
        _playhubUpdateDialogTranslationCancellation = null;
        if (cancellation is not null) _ = CancelPlayhubUpdateDialogTranslationAsync(cancellation);
    }

    private static async Task CancelPlayhubUpdateDialogTranslationAsync(CancellationTokenSource cancellation)
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception ex) { Diag.Crash(nameof(CancelPlayhubUpdateDialogTranslationAsync), ex); }
        finally { cancellation.Dispose(); }
    }

    private async Task TranslatePlayhubUpdateDialogNotesAsync(PlayhubUpdateService.UpdateInfo info, ContentDialog dialog)
    {
        if (!_playhubReleaseNotesTranslation.IsConfigured || string.IsNullOrWhiteSpace(info.Notes) ||
            _playhubUpdateDialogContent is not Border content || _playhubUpdateDialogNotesBody is not StackPanel notes ||
            _playhubUpdateDialogTranslationCancellation is not CancellationTokenSource lifetime) return;
        var token = lifetime.Token;
        var language = _playhubUpdateDialogNotesLanguage;
        try
        {
            var normalized = PluginCatalogService.PrepareDescriptionForDisplay(info.Notes);
            var result = await _playhubReleaseNotesTranslation.TranslateAsync(normalized, language, token).ConfigureAwait(false);
            if (!result.IsAutomatic || token.IsCancellationRequested) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_playhubUpdateDialog, dialog)) return;
                try { ApplyPlayhubUpdateDialogTranslation(content, notes, language, token, result, info.ReleaseUrl); }
                catch (Exception ex) { Diag.Crash(nameof(TranslatePlayhubUpdateDialogNotesAsync), ex); }
            });
        }
        catch (Exception ex)
        {
            Diag.Crash(nameof(TranslatePlayhubUpdateDialogNotesAsync), ex);
        }
    }

    private bool ApplyPlayhubUpdateDialogTranslation(Border content, StackPanel notes, string language,
        CancellationToken token, ReleaseNotesTranslationService.Translation result, string? releaseUrl)
    {
        if (!result.IsAutomatic || token.IsCancellationRequested ||
            !ReferenceEquals(_playhubUpdateDialogContent, content) || !ReferenceEquals(_playhubUpdateDialogNotesBody, notes) ||
            content.Visibility != Visibility.Visible || language != LocalizationService.ResolveLanguage(_settings.Language)) return false;

        var description = BuildDescription(result.Markdown);
        var automaticLabel = language switch
        {
            "it" => "Traduzione automatica",
            "es" => "Traducción automática",
            "fr" => "Traduction automatique",
            "de" => "Automatische Übersetzung",
            "pt" => "Tradução automática",
            "uk" => "Автоматичний переклад",
            "zh" => "自动翻译",
            "ja" => "自動翻訳",
            "ko" => "자동 번역",
            "hi" => "स्वचालित अनुवाद",
            "ru" => "Автоматический перевод",
            _ => "Automatic translation"
        };
        FrameworkElement attribution;
        if (Uri.TryCreate(releaseUrl, UriKind.Absolute, out var source) && source.Scheme == Uri.UriSchemeHttps &&
            string.Equals(source.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            attribution = new HyperlinkButton
            {
                Content = new TextBlock { Text = automaticLabel, TextWrapping = TextWrapping.Wrap },
                NavigateUri = source,
                FontSize = 12,
                MinWidth = 0,
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Opacity = 0.72
            };
            SetLocalizedToolTip(attribution, "Apri su GitHub");
        }
        else attribution = new TextBlock { Text = automaticLabel, FontSize = 12, Opacity = 0.72, TextWrapping = TextWrapping.Wrap };
        notes.Children.Clear();
        notes.Children.Add(description);
        notes.Children.Add(attribution);
        return true;
    }

    private async Task InvokePlayhubUpdateDialogUpdateAsync(PlayhubUpdateService.UpdateInfo info, Func<Task> update)
    {
        if (_playhubUpdateRunning || _playhubUpdateDialogActionPending ||
            string.IsNullOrWhiteSpace(info.DownloadUrl) ||
            (!_playhubUpdateDialogFailed && _playhubUpdateDialogFraction >= 1)) return;

        _playhubUpdateDialogActionPending = true;
        _playhubUpdateDialogFailed = false;
        _playhubUpdateDialogHasProgress = true;
        _playhubUpdateDialogFraction = null;
        _playhubUpdateDialogStatusText = "";
        RefreshPlayhubUpdateDialogState();
        try
        {
            await update();
        }
        catch (Exception ex)
        {
            Diag.Crash(nameof(InvokePlayhubUpdateDialogUpdateAsync), ex);
            UpdatePlayhubUpdateDialogProgress(null, T("Non riesco ad aggiornare Playhub. Riprova."), failed: true);
        }
        finally
        {
            _playhubUpdateDialogActionPending = false;
            RefreshPlayhubUpdateDialogState();
        }
    }

    // fraction is 0..1, or null for indeterminate. Status is displayed unchanged.
    // Parent reports failed after clearing _playhubUpdateRunning; progress is
    // retained even while closed. This method never starts/cancels an operation.
    private void UpdatePlayhubUpdateDialogProgress(double? fraction, string status, bool failed = false)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdatePlayhubUpdateDialogProgress(fraction, status, failed));
            return;
        }
        _playhubUpdateDialogHasProgress = true;
        _playhubUpdateDialogFraction = fraction is double value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : null;
        _playhubUpdateDialogStatusText = status ?? "";
        _playhubUpdateDialogFailed = failed;
        RefreshPlayhubUpdateDialogState();
    }

    private void RefreshPlayhubUpdateDialogState()
    {
        if (_playhubUpdateDialogActionButton is null || _playhubUpdateDialogProgressBar is null ||
            _playhubUpdateDialogStatus is null) return;
        var busy = _playhubUpdateRunning || _playhubUpdateDialogActionPending;
        if (_playhubUpdateDialogCloseButton is not null)
        {
            _playhubUpdateDialogCloseButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            _playhubUpdateDialogCloseButton.IsEnabled = !busy;
        }
        var complete = !busy && !_playhubUpdateDialogFailed && _playhubUpdateDialogFraction >= 1;
        // Keep the idle label while busy; progress/completion belongs below.
        var label = _playhubUpdateDialogFailed ? T("Riprova") : T("Aggiorna ora");
        _playhubUpdateDialogActionButton.Content = label;
        _playhubUpdateDialogActionButton.IsEnabled = !busy && !complete &&
            !string.IsNullOrWhiteSpace(_playhubUpdateDialogInfo?.DownloadUrl);
        AutomationProperties.SetName(_playhubUpdateDialogActionButton, label);
        _playhubUpdateDialogProgressBar.IsIndeterminate = busy && !_playhubUpdateDialogFailed && _playhubUpdateDialogFraction is null;
        _playhubUpdateDialogProgressBar.Value = _playhubUpdateDialogFraction ?? 0;
        _playhubUpdateDialogProgressBar.Opacity = _playhubUpdateDialogHasProgress || busy ? 1 : 0;
        var status = !string.IsNullOrWhiteSpace(_playhubUpdateDialogStatusText) ? _playhubUpdateDialogStatusText
            : busy ? T("Aggiornamento in corso") : complete ? T("Completato") : "";
        _playhubUpdateDialogStatus.Text = status;
        _playhubUpdateDialogStatus.Foreground = _playhubUpdateDialogFailed
            ? new SolidColorBrush(Color.FromArgb(255, 255, 155, 155))
            : new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
        AutomationProperties.SetHelpText(_playhubUpdateDialogActionButton, status);
        ToolTipService.SetToolTip(_playhubUpdateDialogStatus, status);
    }

#if PLAYHUB_UI_REVIEW
    internal FrameworkElement BuildPlayhubUpdateDialogForReview(PlayhubUpdateService.UpdateInfo info,
        Func<Task> fakeUpdate, Action? onClose = null, double width = 640, double maxHeight = 720)
    {
        ArgumentNullException.ThrowIfNull(fakeUpdate);
        var displayInfo = SelectPlayhubUpdateDialogInfo(info);
        return BuildPlayhubUpdateDialogContent(displayInfo, fakeUpdate,
            onClose ?? (() => { if (_playhubUpdateDialogContent is not null) _playhubUpdateDialogContent.Visibility = Visibility.Collapsed; }),
            width, maxHeight);
    }

    internal Task InvokePlayhubUpdateDialogUpdateForReviewAsync()
        => _playhubUpdateDialogInvokeUpdate?.Invoke() ?? Task.CompletedTask;

    // Literal fixture injection only: no provider/network calls in UI review.
    // Retain the old content reference to verify that replaced dialogs reject it.
    internal bool ApplyPlayhubUpdateDialogTranslationForReview(FrameworkElement expectedContent, string translatedMarkdown)
        => expectedContent is Border content && _playhubUpdateDialogNotesBody is StackPanel notes &&
           _playhubUpdateDialogTranslationCancellation is CancellationTokenSource lifetime &&
           ApplyPlayhubUpdateDialogTranslation(content, notes, _playhubUpdateDialogNotesLanguage, lifetime.Token,
               new ReleaseNotesTranslationService.Translation(translatedMarkdown, true), _playhubUpdateDialogInfo?.ReleaseUrl);
#endif
}
