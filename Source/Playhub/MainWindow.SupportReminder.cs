using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Playhub.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Playhub;

public sealed partial class MainWindow
{
    private SupportUsageClock? _supportUsageClock;
    private readonly Stopwatch _supportUsageMonotonic = new();
    private DispatcherQueueTimer? _supportUsageTimer;
    private XamlRoot? _supportUsageRoot;
    private FrameworkElement? _supportUsageContent;
    private nint _supportUsageWindowHandle;
    private bool _supportUsageActivated;
    private bool _supportUsageClosed;
    private Task _supportUsageSaveTask = Task.CompletedTask;
    private bool _supportUsageSaveRequested;
    private double _supportUsageLastSaved;
    private int _supportReminderOperationDepth;
    private ContentDialog? _supportReminderDialog;

    // Parent calls once after _settings = await _settingsService.LoadAsync().
    // Prefer the end of successful LoadAsync, after ApplyLanguage/startup work.
    private void InitializeSupportReminder()
    {
#if !PLAYHUB_UI_REVIEW
        if (_supportUsageClock is not null || _supportUsageClosed ||
            !ReferenceEquals(_settings, _settingsService.Current)) return;
        _supportUsageClock = new SupportUsageClock(_settings.SupportReminderUsageSeconds);
        _supportUsageLastSaved = _settings.SupportReminderUsageSeconds;
        _supportUsageWindowHandle = WindowNative.GetWindowHandle(this);
        _supportUsageActivated = SupportGetForegroundWindow() == _supportUsageWindowHandle;
        _supportUsageMonotonic.Start();
        _supportUsageTimer = DispatcherQueue.CreateTimer();
        _supportUsageTimer.Interval = TimeSpan.FromSeconds(30);
        _supportUsageTimer.Tick += SupportUsageTick;
        Activated += SupportUsageActivated;
        Closed += SupportUsageClosed;
        AppWindow.Changed += SupportUsageWindowChanged;
        _supportUsageContent = Content as FrameworkElement;
        if (_supportUsageContent is not null)
        {
            _supportUsageContent.Loaded += SupportUsageContentLoaded;
            _supportUsageContent.Unloaded += SupportUsageContentUnloaded;
        }
        ObserveSupportUsageRoot();
        RefreshSupportUsageActivity();
#endif
    }

    // Optional parent save hook: snapshot only, never recursively saves.
    private void CaptureSupportReminderUsageForSave()
    {
#if !PLAYHUB_UI_REVIEW
        if (_supportUsageClock is null || _supportUsageClosed) return;
        _supportUsageClock.Sample(_supportUsageMonotonic.Elapsed.TotalSeconds,
            IsSupportWindowForeground(), ReadSupportIdleSeconds());
        _settings.SupportReminderUsageSeconds = _supportUsageClock.UsageSeconds;
#endif
    }

    private void RefreshSupportUsageActivity()
    {
        if (_supportUsageClock is null || _supportUsageClosed) return;
        var wasActive = _supportUsageClock.IsActive;
        CaptureSupportReminderUsageForSave();
        // Keep checking input while foreground but idle; no background timer.
        if (IsSupportWindowForeground())
        {
            if (_supportUsageTimer?.IsRunning == false) _supportUsageTimer.Start();
        }
        else _supportUsageTimer?.Stop();
        if (wasActive && !_supportUsageClock.IsActive) _ = SaveSupportUsageAsync();
    }

    private void SupportUsageTick(DispatcherQueueTimer sender, object args)
    {
        RefreshSupportUsageActivity();
        _ = SaveSupportUsageAsync();
        _ = ShowSupportReminderAsync();
    }

    private void SupportUsageActivated(object sender, WindowActivatedEventArgs args)
    {
        _supportUsageActivated = args.WindowActivationState != WindowActivationState.Deactivated;
        RefreshSupportUsageActivity();
        if (_supportUsageActivated) _ = ShowSupportReminderAsync();
    }

    private void SupportUsageWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => RefreshSupportUsageActivity();
    private void SupportUsageRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => RefreshSupportUsageActivity();
    private void SupportUsageContentLoaded(object sender, RoutedEventArgs args) { ObserveSupportUsageRoot(); RefreshSupportUsageActivity(); }
    private void SupportUsageContentUnloaded(object sender, RoutedEventArgs args)
    {
        if (_supportUsageClock is null) return;
        _supportUsageClock.Sample(_supportUsageMonotonic.Elapsed.TotalSeconds, false, ReadSupportIdleSeconds());
        _settings.SupportReminderUsageSeconds = _supportUsageClock.UsageSeconds;
        _supportUsageTimer?.Stop();
        _ = SaveSupportUsageAsync();
    }

    private void ObserveSupportUsageRoot()
    {
        var root = Content?.XamlRoot;
        if (ReferenceEquals(root, _supportUsageRoot)) return;
        if (_supportUsageRoot is not null) _supportUsageRoot.Changed -= SupportUsageRootChanged;
        _supportUsageRoot = root;
        if (root is not null) root.Changed += SupportUsageRootChanged;
    }

    private void SupportUsageClosed(object sender, WindowEventArgs args)
    {
        if (_supportUsageClock is not null)
        {
            _supportUsageClock.Sample(_supportUsageMonotonic.Elapsed.TotalSeconds, false, ReadSupportIdleSeconds());
            _settings.SupportReminderUsageSeconds = _supportUsageClock.UsageSeconds;
        }
        _supportUsageClosed = true;
        _supportUsageTimer?.Stop();
        _supportUsageMonotonic.Stop();
        _ = SaveSupportUsageAsync();
        Activated -= SupportUsageActivated;
        Closed -= SupportUsageClosed;
        AppWindow.Changed -= SupportUsageWindowChanged;
        if (_supportUsageTimer is not null) _supportUsageTimer.Tick -= SupportUsageTick;
        if (_supportUsageRoot is not null) _supportUsageRoot.Changed -= SupportUsageRootChanged;
        if (_supportUsageContent is not null)
        {
            _supportUsageContent.Loaded -= SupportUsageContentLoaded;
            _supportUsageContent.Unloaded -= SupportUsageContentUnloaded;
        }
    }

    private Task SaveSupportUsageAsync()
    {
#if PLAYHUB_UI_REVIEW
        return Task.CompletedTask;
#else
        if (_supportUsageClock is null || (_supportUsageSaveTask.IsCompleted &&
            _supportUsageLastSaved == _settings.SupportReminderUsageSeconds)) return Task.CompletedTask;
        _supportUsageSaveRequested = true;
        if (_supportUsageSaveTask.IsCompleted) _supportUsageSaveTask = SaveSupportUsageCoreAsync();
        return _supportUsageSaveTask;
#endif
    }

    private async Task SaveSupportUsageCoreAsync()
    {
#if !PLAYHUB_UI_REVIEW
        try
        {
            while (_supportUsageSaveRequested)
            {
                _supportUsageSaveRequested = false;
                var value = _settings.SupportReminderUsageSeconds;
                await SaveSettingsSilentlyAsync();
                _supportUsageLastSaved = value;
            }
        }
        catch (Exception ex) { Diag.Crash(nameof(SaveSupportUsageCoreAsync), ex); }
#else
        await Task.CompletedTask;
#endif
    }

    private bool IsSupportWindowForeground()
        => !_supportUsageClosed && _supportUsageActivated &&
           SupportGetForegroundWindow() == _supportUsageWindowHandle && AppWindow.IsVisible &&
           Content is FrameworkElement { IsLoaded: true, Visibility: Visibility.Visible } &&
           Content.XamlRoot?.IsHostVisible == true &&
           AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private bool SupportReminderOperationIsBusy()
        => Volatile.Read(ref _supportReminderOperationDepth) > 0 ||
           _playhubUpdateDialog is not null || _playhubUpdateRunning || _playhubUpdateDialogActionPending ||
           _pluginBulkUpdateRunning || _pluginInstallOperations.Count > 0 || _pluginUninstalls.Count > 0 ||
           _executableScanInProgress || _cssLoaderInstallBusy || _repairRunning || _diagnosticsRunning ||
           _loadingOverlay.Visibility == Visibility.Visible ||
           _welcomeRoot.Visibility == Visibility.Visible;

    // Parent adds a using scope to its generic async Button/IconButton wrappers.
    // This covers Decky/import work lacking a dedicated busy flag. Support's
    // preview Test button is intentionally not itself a blocking operation.
    private IDisposable BeginSupportReminderOperation()
        => new SupportReminderOperationScope(_currentPageTag == "support" ? null : this);

    private sealed class SupportReminderOperationScope : IDisposable
    {
        private MainWindow? _owner;
        public SupportReminderOperationScope(MainWindow? owner)
        {
            _owner = owner;
            if (owner is not null) Interlocked.Increment(ref owner._supportReminderOperationDepth);
        }
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null) Interlocked.Decrement(ref owner._supportReminderOperationDepth);
        }
    }

    private async Task ShowSupportReminderAsync(bool force = false)
    {
#if PLAYHUB_UI_REVIEW
        if (!force) return;
#endif
        if (_supportUsageClosed || _supportReminderDialog is not null || SupportReminderOperationIsBusy()) return;
        if (!force && (_supportUsageClock?.IsDue != true || !IsSupportWindowForeground() ||
            ReadSupportIdleSeconds() >= SupportUsageClock.IdleLimitSeconds)) return;
        var root = Content?.XamlRoot;
        if (root is null || VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0) return;

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Color.FromArgb(255, 57, 57, 57)),
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            DefaultButton = ContentDialogButton.None,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        AutomationProperties.SetName(dialog, T("Ti piace Playhub?"));
        dialog.Resources["ContentDialogMinWidth"] = 0d;
        dialog.Resources["ContentDialogMaxWidth"] = 560d;
        dialog.Resources["ContentDialogMinHeight"] = 0d;
        dialog.Resources["ContentDialogMaxHeight"] = double.PositiveInfinity;
        dialog.Resources["ContentDialogPadding"] = new Thickness(0);
        dialog.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
        dialog.Resources["ContentDialogTopOverlay"] = new SolidColorBrush(Colors.Transparent);
        async Task Donate()
        {
#if !PLAYHUB_UI_REVIEW
            await Launcher.LaunchUriAsync(new Uri("https://ko-fi.com/lozazamastro"));
#else
            await Task.CompletedTask;
#endif
        }
        var view = BuildSupportReminderContent(Donate, dialog.Hide, root.Size.Width - 48, root.Size.Height - 48);
        dialog.Content = view.Content;
        void Resize(XamlRoot sender, XamlRootChangedEventArgs args) => view.Resize(sender.Size.Width - 48, sender.Size.Height - 48);
        dialog.KeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { args.Handled = true; dialog.Hide(); } };
        dialog.Opened += (_, _) =>
        {
            if (_supportUsageClosed || SupportReminderOperationIsBusy() ||
                (!force && (!IsSupportWindowForeground() || ReadSupportIdleSeconds() >= SupportUsageClock.IdleLimitSeconds)))
            {
                dialog.Hide();
                return;
            }
            view.Close.Focus(FocusState.Programmatic);
#if !PLAYHUB_UI_REVIEW
            if (!force && _supportUsageClock is not null)
            {
                _supportUsageClock.MarkReminderOpened(_supportUsageMonotonic.Elapsed.TotalSeconds,
                    IsSupportWindowForeground(), ReadSupportIdleSeconds());
                _settings.SupportReminderUsageSeconds = _supportUsageClock.UsageSeconds;
                _ = SaveSupportUsageAsync();
            }
#endif
        };
        try
        {
            _supportReminderDialog = dialog;
            root.Changed += Resize;
            ConfigureDialogEntrance(dialog);
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // A competing ContentDialog can win between the check and ShowAsync.
            // Leave accumulated usage due for the next normal tick.
            Diag.Step("Support reminder deferred: " + ex.GetType().Name);
        }
        finally
        {
            root.Changed -= Resize;
            if (ReferenceEquals(_supportReminderDialog, dialog)) _supportReminderDialog = null;
        }
    }

    private (Border Content, Button Close, Action<double, double> Resize) BuildSupportReminderContent(
        Func<Task> donate, Action close, double width, double maxHeight)
    {
        var content = new Border
        {
            Name = "SupportReminderDialogContent",
            RequestedTheme = ElementTheme.Dark,
            Background = new SolidColorBrush(Color.FromArgb(255, 57, 57, 57)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 91, 91, 91)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        var layout = new StackPanel();
        var illustration = new Image
        {
            Name = "SupportReminderImage",
            Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Support", "Donation.png"))),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false
        };
        layout.Children.Add(illustration);
        layout.Children.Add(new TextBlock
        {
            Text = T("Ti piace Playhub?"), FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.White), Margin = new Thickness(0, 20, 0, 12)
        });
        layout.Children.Add(new TextBlock
        {
            Text = T("Playhub è gratuito e open source. Lo sviluppo, i test e la manutenzione sono sostenuti da una sola persona."),
            FontSize = 14, LineHeight = 21, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255))
        });
        layout.Children.Add(new TextBlock
        {
            Text = T("Se Playhub ti è utile e vuoi aiutare il progetto a continuare a crescere, una donazione è sempre apprezzata. Nessun contenuto è bloccato: è semplicemente un modo gentile per sostenere il lavoro che c'è dietro."),
            FontSize = 14, LineHeight = 21, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)), Margin = new Thickness(0, 12, 0, 0)
        });
        var donationButton = new Button
        {
            Name = "SupportReminderDonate",
            Content = new TextBlock { Text = T("Fai una donazione"), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            Style = StyleResource("PlayhubPrimaryButtonStyle"), MinWidth = 0, MinHeight = 40,
            Padding = new Thickness(20, 10, 20, 10), Margin = new Thickness(0, 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
            TabIndex = 1
        };
        RegisterButton(donationButton, primary: true);
        AutomationProperties.SetName(donationButton, T("Fai una donazione"));
        AutomationProperties.SetAutomationId(donationButton, "SupportReminderDonate");
        donationButton.Click += async (_, _) =>
        {
            if (!donationButton.IsEnabled) return;
            donationButton.IsEnabled = false;
            try { await donate(); }
            catch (Exception ex) { Diag.Crash(nameof(BuildSupportReminderContent), ex); }
            finally { donationButton.IsEnabled = true; }
        };
        layout.Children.Add(donationButton);
        var scroll = new ScrollViewer
        {
            Content = layout, Padding = new Thickness(30, 40, 30, 28),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var overlay = new Grid();
        overlay.Children.Add(scroll);
        var closeButton = new Button
        {
            Name = "SupportReminderClose", Content = new FontIcon { Glyph = "\uE8BB", FontSize = 14 },
            Width = 32, Height = 32, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Transparent), Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4), TabIndex = 0
        };
        AutomationProperties.SetName(closeButton, T("Chiudi"));
        AutomationProperties.SetAutomationId(closeButton, "SupportReminderClose");
        SetLocalizedToolTip(closeButton, "Chiudi");
        closeButton.Click += (_, _) => close();
        content.KeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { args.Handled = true; close(); } };
        overlay.Children.Add(closeButton);
        content.Child = overlay;
        void Resize(double availableWidth, double availableHeight)
        {
            content.Width = Math.Clamp(availableWidth, 0, 560);
            content.MaxHeight = Math.Clamp(availableHeight, 0, 700);
            scroll.MaxHeight = Math.Max(0, content.MaxHeight - 2);
            var innerWidth = Math.Max(0, content.Width - 62);
            illustration.Width = Math.Min(430, innerWidth);
            illustration.Height = illustration.Width * 747 / 1357;
            donationButton.MaxWidth = innerWidth;
        }
        Resize(width, maxHeight);
        return (content, closeButton, Resize);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SupportLastInputInfo { public uint Size; public uint Tick; }
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint SupportGetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetLastInputInfo")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SupportGetLastInputInfo(ref SupportLastInputInfo info);
    private static double ReadSupportIdleSeconds()
    {
        var info = new SupportLastInputInfo { Size = (uint)Marshal.SizeOf<SupportLastInputInfo>() };
        return SupportGetLastInputInfo(ref info)
            ? unchecked((uint)System.Environment.TickCount - info.Tick) / 1000d : double.PositiveInfinity;
    }

#if PLAYHUB_UI_REVIEW
    internal FrameworkElement BuildSupportReminderForReview(Func<Task>? fakeDonation = null, Action? onClose = null,
        double width = 560, double maxHeight = 640)
    {
        Border? content = null;
        var view = BuildSupportReminderContent(fakeDonation ?? (() => Task.CompletedTask),
            onClose ?? (() => { if (content is not null) content.Visibility = Visibility.Collapsed; }), width, maxHeight);
        content = view.Content;
        return content;
    }
#endif
}
