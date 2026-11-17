// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Playhub.StoreLibrary;

/// <summary>Desktop connections only. Game installation and management belong to Steam.</summary>
internal sealed class StoreAccountsPage : UserControl, IDisposable
{
    private readonly StoreLibraryController controller;
    private readonly StoreAccountControllers accountControllers;
    private readonly IStoreXboxAccountService? xboxService;
    private readonly Func<string, string> text;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Grid accounts = new() { ColumnSpacing = 16, RowSpacing = 16 };
    private readonly TextBlock hint = Body();
    private readonly TextBlock steamHint = Body();
    private readonly TextBlock guideTitle = new() { FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock guideSubtitle = Body();
    private readonly Grid guideSteps = new() { ColumnSpacing = 12, RowSpacing = 12 };
    private readonly TextBlock[] guideCopy = new TextBlock[4];
    private readonly InfoBar status = new() { IsClosable = true };
    private readonly Button refresh = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock migrationStatus = Body();
    private readonly Button migrationAction = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border migrationCard;
    private readonly Dictionary<StoreKind, AccountRow> rows = new();
    private readonly Dictionary<StoreKind, CancellationTokenSource> accountLifetimes = new();
    private readonly Dictionary<StoreKind, StoreAccountIntent> accountIntents = new();
    private readonly Dictionary<StoreKind, StoreAccount> lastAccounts = new();
    private readonly Dictionary<StoreKind, string> localMessages = new();
    private string? language;
    private bool active;
    private bool disposed;

    internal StoreAccountsPage(StoreLibraryController controller, IStoreXboxAccountService? xboxService,
        Func<string, string> text, string? language)
    {
        this.controller = controller; this.xboxService = xboxService; this.text = text; this.language = language;
        accountControllers = new(controller);
        Tag = "noloc"; RequestedTheme = ElementTheme.Dark;
        var content = new StackPanel { Spacing = 20, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(BuildGuide());
        content.Children.Add(hint);
        var migrationBody = new StackPanel { Spacing = 12 };
        migrationBody.Children.Add(migrationStatus); migrationBody.Children.Add(migrationAction);
        migrationCard = Card(migrationBody); migrationCard.Visibility = Visibility.Collapsed;
        ApplyStyle(migrationAction, "PlayhubSecondaryButtonStyle");
        content.Children.Add(migrationCard); content.Children.Add(status); content.Children.Add(accounts); content.Children.Add(refresh);
        Content = content;
        foreach (var kind in Enum.GetValues<StoreKind>())
        {
            var body = new Grid { RowSpacing = 12, MinHeight = 152, VerticalAlignment = VerticalAlignment.Stretch };
            body.RowDefinitions.Add(new() { Height = GridLength.Auto });
            body.RowDefinitions.Add(new() { Height = GridLength.Auto });
            body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 20 });
            body.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            string? logo = StoreLibraryPresentation.ServiceLogo(kind);
            if (logo is not null) heading.Children.Add(new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/ServiceLogos/" + logo + ".png")),
                Width = 36, Height = 36, Stretch = Stretch.Uniform });
            heading.Children.Add(new TextBlock { Text = StoreLibraryPresentation.Name(kind), FontSize = 20,
                FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var state = Body(); var name = Body();
            var action = new Button { MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
            ApplyStyle(action, "PlayhubSecondaryButtonStyle");
            action.Click += async (_, _) => await AccountActionAsync(kind, action.Tag is StoreAccountIntent intent ? intent : StoreAccountIntent.SignIn);
            Grid.SetRow(state, 1); Grid.SetRow(name, 2); Grid.SetRow(action, 3);
            body.Children.Add(heading); body.Children.Add(state); body.Children.Add(name); body.Children.Add(action);
            var card = Card(body); accounts.Children.Add(card);
            AutomationProperties.SetAutomationId(card, "StoreAccount." + kind);
            AutomationProperties.SetAutomationId(action, "StoreAccountAction." + kind);
            AutomationProperties.SetAutomationId(state, "StoreAccountState." + kind);
            rows.Add(kind, new(card, state, name, action));
        }
        accounts.SizeChanged += (_, _) => Arrange();
        ApplyStyle(refresh, "PlayhubSecondaryButtonStyle");
        refresh.Click += async (_, _) => { localMessages.Clear(); await RefreshAccountsAsync(); };
        controller.Changed += ControllerChanged;
        accountControllers.Changed += ControllerChanged;
        timer.Tick += async (_, _) => { if (active) await controller.ReadAsync(); };
        Loaded += (_, _) => { if (active) timer.Start(); Arrange(); };
        Unloaded += (_, _) => { timer.Stop(); CancelAccountActions(); };
        Relocalize(language);
    }

    private static TextBlock Body() => new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private static void ApplyStyle(FrameworkElement element, string key)
    { if (Application.Current.Resources.TryGetValue(key, out var value) && value is Style style) element.Style = style; }
    private static Border Card(UIElement child)
    {
        var card = new Border { Child = child, Padding = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Stretch };
        ApplyStyle(card, "PlayhubCardBorderStyle"); return card;
    }
    private string Copy(string key) => StoreDesktopCopy.Get(language, key);
    private Border BuildGuide()
    {
        var accent = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 211, 0));
        var muted = new SolidColorBrush(Windows.UI.Color.FromArgb(210, 222, 224, 230));
        var body = new StackPanel { Spacing = 18 };
        var heading = new StackPanel { Spacing = 6 };
        guideTitle.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        guideSubtitle.Foreground = muted; heading.Children.Add(guideTitle); heading.Children.Add(guideSubtitle);
        body.Children.Add(heading); body.Children.Add(guideSteps);
        Symbol[] icons = [Symbol.Contact, Symbol.AllApps, Symbol.Download, Symbol.Library];
        for (int i = 0; i < 4; i++)
        {
            var step = new StackPanel { Spacing = 12 };
            var top = new Grid(); top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            top.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
                HorizontalAlignment = HorizontalAlignment.Left, Background = accent,
                Child = new TextBlock { Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FontSize = 15, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            var icon = new SymbolIcon(icons[i]) { Foreground = accent, Opacity = .8, Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(icon, 1); top.Children.Add(icon); step.Children.Add(top);
            guideCopy[i] = Body(); guideCopy[i].LineHeight = 20; step.Children.Add(guideCopy[i]);
            guideSteps.Children.Add(new Border { Child = step, Padding = new Thickness(16), CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(12, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255)), BorderThickness = new Thickness(1) });
        }
        var card = Card(body); card.Padding = new Thickness(24); card.CornerRadius = new CornerRadius(16);
        card.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(110, 146, 126, 50)); card.BorderThickness = new Thickness(1);
        card.Background = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops = { new GradientStop { Color = Windows.UI.Color.FromArgb(255, 44, 41, 29), Offset = 0 },
                new GradientStop { Color = Windows.UI.Color.FromArgb(255, 26, 28, 34), Offset = 1 } } };
        AutomationProperties.SetAutomationId(card, "DigitalStoresGuide");
        guideSteps.SizeChanged += (_, _) => ArrangeGuide();
        return card;
    }
    private void ArrangeGuide()
    {
        int columns = guideSteps.ActualWidth >= 980 ? 4 : guideSteps.ActualWidth >= 440 ? 2 : 1;
        guideSteps.ColumnDefinitions.Clear(); guideSteps.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) guideSteps.ColumnDefinitions.Add(new());
        for (int i = 0; i < (4 + columns - 1) / columns; i++) guideSteps.RowDefinitions.Add(new() { Height = GridLength.Auto });
        for (int i = 0; i < 4; i++)
        {
            var step = (Border)guideSteps.Children[i]; step.MinHeight = columns == 1 ? 86 : 120;
            Grid.SetRow(step, i / columns); Grid.SetColumn(step, i % columns);
        }
    }
    private void Arrange()
    {
        int columns = accounts.ActualWidth >= 1000 ? 4 : accounts.ActualWidth >= 560 ? 2 : 1;
        accounts.ColumnDefinitions.Clear(); accounts.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) accounts.ColumnDefinitions.Add(new());
        for (int i = 0; i < (rows.Count + columns - 1) / columns; i++) accounts.RowDefinitions.Add(new() { Height = GridLength.Auto });
        int index = 0;
        foreach (var row in rows.Values) { Grid.SetColumn(row.Card, index % columns); Grid.SetRow(row.Card, index / columns); index++; }
    }
    internal void SetActive(bool value)
    {
        if (disposed) return;
        active = value;
        if (!value) { timer.Stop(); CancelAccountActions(); return; }
        timer.Start(); Render(); _ = controller.ReadAsync();
    }
    internal void Relocalize(string? value)
    {
        language = value;
        hint.Text = Copy("accountsHint"); steamHint.Text = Copy("steamHint");
        guideTitle.Text = Copy("guideTitle"); guideSubtitle.Text = Copy("guideSubtitle");
        for (int i = 0; i < guideCopy.Length; i++) guideCopy[i].Text = Copy("guideStep" + (i + 1));
        refresh.Content = Copy("refreshStatus"); Render();
    }
    internal void SetMigrationStatus(string? message, string action, Action open)
    {
        migrationStatus.Text = message ?? ""; migrationAction.Content = action;
        migrationCard.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        migrationAction.Click -= MigrationClicked; migrationOpen = open; migrationAction.Click += MigrationClicked;
    }
    private Action? migrationOpen;
    private void MigrationClicked(object sender, RoutedEventArgs e) => migrationOpen?.Invoke();
    private void ControllerChanged(object? sender, EventArgs e)
    { if (DispatcherQueue.HasThreadAccess) Render(); else DispatcherQueue.TryEnqueue(Render); }
    private void CancelAccountActions()
    { foreach (var attempt in accountLifetimes.Values.ToArray()) attempt.Cancel(); }
    private StoreAccount? DisplayAccount(StoreKind kind)
    {
        var local = accountControllers.ForStore(kind).Controller.Snapshot;
        var snapshot = local.Revision > controller.Snapshot.Revision ? local : controller.Snapshot;
        var account = snapshot.Accounts.FirstOrDefault(a => a.Store == kind);
        if (account is not null && account.State != StoreConnectionState.Unavailable)
        { lastAccounts[kind] = account; return account; }
        return lastAccounts.GetValueOrDefault(kind);
    }
    private void Render()
    {
        if (disposed) return;
        foreach (var (kind, row) in rows)
        {
            var account = DisplayAccount(kind);
            row.State.Text = accountIntents.TryGetValue(kind, out var intent)
                ? Copy(intent == StoreAccountIntent.SignOut ? "signingOut" : "signingIn")
                : account is null ? Copy("checkingAccount") : text("StoreLibrary.Account." + account.State);
            row.DisplayName.Text = account?.DisplayName ?? "";
            bool connected = account?.State == StoreConnectionState.Connected;
            row.Action.Tag = connected ? StoreAccountIntent.SignOut : StoreAccountIntent.SignIn;
            row.Action.Content = accountLifetimes.ContainsKey(kind) ? text("StoreLibrary.Cancel")
                : text(connected ? "StoreLibrary.Disconnect" : "StoreLibrary.Connect");
            row.Action.IsEnabled = true;
            AutomationProperties.SetName(row.Action, row.Action.Content + " · " + StoreLibraryPresentation.Name(kind));
        }
        refresh.IsEnabled = true;
        string? message = localMessages.Count > 0
            ? string.Join(Environment.NewLine, localMessages.Select(pair => StoreLibraryPresentation.Name(pair.Key) + ": " + Copy(pair.Value)))
            : controller.Snapshot.Revision >= 0 && controller.ErrorKey is { } key ? text(key) : null;
        status.Message = message ?? ""; status.IsOpen = message is not null;
        status.Severity = InfoBarSeverity.Warning;
    }

    private async Task RefreshAccountsAsync()
    {
        if (disposed) return;
        await Task.WhenAll(Enum.GetValues<StoreKind>().Select(kind =>
            accountControllers.ForStore(kind).EnsureLoadedAsync(lifetime.Token)).Append(ReadSharedAsync()));
        if (!disposed) Render();
    }
    private async Task<bool> ReadSharedAsync()
    {
        await controller.ReadAsync();
        return !disposed && controller.Snapshot.Revision >= 0;
    }
    private async Task AccountActionAsync(StoreKind kind, StoreAccountIntent intent)
    {
        if (disposed) return;
        if (accountLifetimes.TryGetValue(kind, out var current)) { current.Cancel(); return; }
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        accountLifetimes[kind] = attempt; accountIntents[kind] = intent;
        localMessages.Remove(kind); Render();
        bool Current() => !disposed && accountLifetimes.TryGetValue(kind, out var held) && ReferenceEquals(held, attempt);
        try
        {
            var account = accountControllers.ForStore(kind);
            var result = await account.RunAsync(intent, async (local, token) =>
            {
                if (intent == StoreAccountIntent.SignOut)
                {
                    if (!await local.DisconnectAsync(kind)) throw new InvalidOperationException("Account sign-out was not confirmed.");
                    return;
                }
                await SignInAccountAsync(kind, local, token);
            }, attempt.Token);
            if (!Current()) return;
            switch (result)
            {
                case StoreAccountActionResult.Completed:
                case StoreAccountActionResult.AlreadyInState:
                    localMessages.Remove(kind);
                    if (intent == StoreAccountIntent.SignIn && result == StoreAccountActionResult.Completed)
                        _ = RefreshSignedInLibraryAsync(kind);
                    else _ = ReadSharedAsync();
                    break;
                case StoreAccountActionResult.Canceled:
                    if (intent == StoreAccountIntent.SignIn) localMessages[kind] = "authCanceled";
                    break;
                default:
                    localMessages[kind] = "accountFailed";
                    break;
            }
        }
        catch (OperationCanceledException) when (attempt.IsCancellationRequested)
        { if (Current() && intent == StoreAccountIntent.SignIn) localMessages[kind] = "authCanceled"; }
        catch { if (Current()) localMessages[kind] = "accountFailed"; }
        finally
        {
            if (Current())
            {
                accountLifetimes.Remove(kind); accountIntents.Remove(kind); Render();
            }
        }
    }
    private async Task SignInAccountAsync(StoreKind kind, StoreLibraryController local, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (kind == StoreKind.Xbox)
        {
            if (xboxService is null) throw new InvalidOperationException("Xbox sign-in is unavailable.");
            var account = local.Snapshot.Accounts.FirstOrDefault(value => value.Store == kind)
                ?? throw new InvalidOperationException("Account status is unavailable.");
            var signIn = new StoreXboxSignInController(xboxService);
            try
            {
                var session = await signIn.BeginAsync(account.Revision, token);
                token.ThrowIfCancellationRequested();
                if (session is null) throw new InvalidOperationException("Xbox sign-in was not started.");
                var result = await StoreXboxSignInWindow.ShowAsync(signIn, session, text, token);
                token.ThrowIfCancellationRequested();
                if (result?.State == StoreXboxSignInState.Canceled) throw new OperationCanceledException();
                if (result?.State != StoreXboxSignInState.Connected) throw new InvalidOperationException("Xbox sign-in was not confirmed.");
            }
            finally { await signIn.CancelAsync(); }
            return;
        }
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var session = await local.BeginConnectAsync(kind);
            token.ThrowIfCancellationRequested();
            if (session is null) throw new InvalidOperationException("Sign-in was not started.");
            var result = await StoreSignInWindow.ShowAsync(session, text, token);
            token.ThrowIfCancellationRequested();
            if (result.Outcome == StoreSignInOutcome.RetryRequested) { local.CancelConnect(); continue; }
            if (result.Outcome != StoreSignInOutcome.Authorized || string.IsNullOrWhiteSpace(result.Code))
            { local.CancelConnect(); throw new OperationCanceledException(); }
            // Authentication is confirmed independently from potentially large
            // library hydration. The coordinator then verifies a fresh snapshot.
            if (!await local.CompleteConnectAsync(session, result.Code)) throw new InvalidOperationException("Sign-in was not confirmed.");
            return;
        }
    }
    private async Task RefreshSignedInLibraryAsync(StoreKind kind)
    {
        using var refreshController = controller.CreateIndependent();
        using var revoke = lifetime.Token.Register(refreshController.Dispose);
        try
        {
            await refreshController.ReadAsync();
            if (!disposed) await refreshController.RefreshAsync(kind);
        }
        catch { /* A library refresh failure does not undo a confirmed account sign-in. */ }
        if (!disposed) await controller.ReadAsync();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; active = false; timer.Stop(); CancelAccountActions(); lifetime.Cancel();
        controller.Changed -= ControllerChanged; accountControllers.Changed -= ControllerChanged;
        accountControllers.Dispose(); controller.Dispose(); lifetime.Dispose();
        accountLifetimes.Clear(); accountIntents.Clear();
    }
    private sealed record AccountRow(Border Card, TextBlock State, TextBlock DisplayName, Button Action);
}
