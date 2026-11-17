// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

/// <summary>The store owns its sign-in form. Only its final authorization callback is read.</summary>
internal sealed class StoreSignInWindow : Window
{
    private readonly WebView2 browser = new();
    private readonly TextBlock address = new() { TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Height = 3 };
    private readonly StoreConnectSession session;
    private readonly TaskCompletionSource<StoreSignInResult> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer expiry = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StoreSignInRecovery recovery;
    private readonly Func<string, string> text;
    private readonly StackPanel failure = new() { Spacing = 16, MaxWidth = 520, Margin = new Thickness(32), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock failureMessage = new() { FontSize = 16, TextWrapping = TextWrapping.Wrap };
    private bool finished => recovery.Result is not null;

    private StoreSignInWindow(StoreConnectSession connection, Func<string, string> text)
    {
        session = connection;
        this.text = text;
        recovery = new(connection.ExpiresAt);
        Title = text("StoreLibrary.Connect") + " · " + StoreLibraryPresentation.Name(connection.Store);
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new());
        var heading = new StackPanel { Spacing = 6, Padding = new Thickness(20, 14, 20, 14) };
        heading.Children.Add(new TextBlock { Text = StoreLibraryPresentation.Name(connection.Store), FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        heading.Children.Add(address);
        layout.Children.Add(heading);
        Grid.SetRow(loading, 1); layout.Children.Add(loading);
        Grid.SetRow(browser, 2); layout.Children.Add(browser);
        failure.Children.Add(failureMessage);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var retry = new Button { Content = text("Riprova") };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent) && accent is Style style) retry.Style = style;
        retry.Click += (_, _) => { if (recovery.Retry()) Finish(); };
        var cancel = new Button { Content = text("StoreLibrary.Cancel") };
        cancel.Click += (_, _) => Cancel();
        actions.Children.Add(retry); actions.Children.Add(cancel); failure.Children.Add(actions);
        Grid.SetRow(failure, 2); layout.Children.Add(failure);
        Content = layout;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min(900, area.Width), Math.Min(760, area.Height)));
        Closed += (_, _) => Cancel(close: false);
        expiry.Tick += (_, _) => { recovery.Tick(); RenderFailure(); };
    }

    internal static async Task<StoreSignInResult> ShowAsync(StoreConnectSession session, Func<string, string> text, CancellationToken token)
    {
        if (token.IsCancellationRequested) return new(StoreSignInOutcome.Canceled, null);
        var window = new StoreSignInWindow(session, text);
        using var registration = token.Register(() => window.DispatcherQueue.TryEnqueue(() => window.Cancel()));
        window.Activate();
        window.expiry.Start();
        if (window.recovery.State == StoreSignInPageState.Expired) window.RenderFailure();
        else _ = window.InitializeAsync();
        try { return await window.result.Task; }
        finally { window.lifetime.Dispose(); }
    }

    private async Task InitializeAsync()
    {
        try
        {
            // A private profile makes Sign out effective on the next sign-in too,
            // without changing browser/launcher sessions belonging to the user.
            var directory = Path.Combine(Playhub.Services.AppPaths.LocalDataRoot, "store-sign-in");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, directory, null).AsTask().WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            if (finished) return;
            recovery.Tick();
            if (recovery.State is StoreSignInPageState.Failed or StoreSignInPageState.Expired) { RenderFailure(); return; }
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = session.Store.ToString(); options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options).AsTask().WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            if (finished) return;
            recovery.Tick();
            if (recovery.State is StoreSignInPageState.Failed or StoreSignInPageState.Expired) { RenderFailure(); return; }
            var core = browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NavigationStarting += (_, e) =>
            {
                if (finished) { e.Cancel = true; return; }
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https") { e.Cancel = true; recovery.Fail(); RenderFailure(); return; }
                if (!recovery.StartNavigation()) { e.Cancel = true; RenderFailure(); return; }
                address.Text = uri.Host; loading.Visibility = Visibility.Visible;
                if (StoreAuthorizationCallback.TryReadRedirect(session.Store, uri, out var code))
                { e.Cancel = true; Authorize(code); }
            };
            core.NavigationCompleted += (_, e) => { recovery.NavigationCompleted(e.IsSuccess, e.HttpStatusCode); loading.Visibility = Visibility.Collapsed; RenderFailure(); };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!finished && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https") core.Navigate(uri.AbsoluteUri);
            };
            core.WebResourceResponseReceived += async (_, e) =>
            {
                if (finished || session.Store != StoreKind.Epic || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)
                    || !StoreAuthorizationCallback.IsEpicResponse(uri) || e.Response.StatusCode != 200) return;
                try
                {
                    var stream = await e.Response.GetContentAsync();
                    if (finished || stream is null) return;
                    using var input = stream.AsStreamForRead();
                    using var output = new MemoryStream();
                    var bytes = new byte[4096]; int read;
                    while ((read = await input.ReadAsync(bytes, lifetime.Token)) > 0)
                    { if (output.Length + read > 32768) return; output.Write(bytes, 0, read); }
                    if (!finished && StoreAuthorizationCallback.TryReadEpicResponse(uri, Encoding.UTF8.GetString(output.ToArray()), out var code)) Authorize(code);
                }
                catch { /* A failed response never becomes a successful sign-in. */ }
            };
            address.Text = session.AuthorizationUri.Host;
            core.Navigate(session.AuthorizationUri.AbsoluteUri);
        }
        catch { if (!finished) { recovery.Fail(); RenderFailure(); } }
    }

    private void RenderFailure()
    {
        if (finished || recovery.State is not (StoreSignInPageState.Failed or StoreSignInPageState.Expired)) return;
        browser.Visibility = Visibility.Collapsed; loading.Visibility = Visibility.Collapsed;
        failureMessage.Text = recovery.State == StoreSignInPageState.Expired
            ? text("StoreLibrary.Expired") + ". " + text("StoreLibrary.ConnectFailed") : text("StoreLibrary.ConnectFailed");
        failure.Visibility = Visibility.Visible;
    }
    private void Authorize(string code) { if (recovery.Authorize(code)) Finish(); else RenderFailure(); }
    private void Cancel(bool close = true) { if (recovery.Cancel()) Finish(close); }
    private void Finish(bool close = true)
    {
        expiry.Stop(); lifetime.Cancel();
        try { browser.Close(); } catch { }
        result.TrySetResult(recovery.Result!);
        if (close) Close();
    }
}

internal enum StoreSignInOutcome { Authorized, Canceled, RetryRequested }
internal sealed record StoreSignInResult(StoreSignInOutcome Outcome, string? Code);
internal enum StoreSignInPageState { Initializing, Loading, Ready, Failed, Expired, Finished }

/// <summary>Original authorization expiry and one explicit recovery decision; no store commands.</summary>
internal sealed class StoreSignInRecovery
{
    private readonly DateTimeOffset expiresAt;
    private readonly Func<DateTimeOffset> now;
    private DateTimeOffset pageDeadline;
    internal StoreSignInPageState State { get; private set; } = StoreSignInPageState.Initializing;
    internal StoreSignInResult? Result { get; private set; }
    internal StoreSignInRecovery(DateTimeOffset expiresAt, Func<DateTimeOffset>? clock = null)
    {
        this.expiresAt = expiresAt; now = clock ?? (() => DateTimeOffset.UtcNow);
        pageDeadline = now().AddSeconds(30); Tick();
    }
    internal void Tick()
    {
        if (Result is not null) return;
        if (now() >= expiresAt) State = StoreSignInPageState.Expired;
        else if (State is StoreSignInPageState.Initializing or StoreSignInPageState.Loading && now() >= pageDeadline)
            State = StoreSignInPageState.Failed;
    }
    internal bool StartNavigation()
    {
        Tick();
        if (Result is not null || State is StoreSignInPageState.Failed or StoreSignInPageState.Expired) return false;
        if (State == StoreSignInPageState.Ready) pageDeadline = now().AddSeconds(30);
        State = StoreSignInPageState.Loading; return true;
    }
    internal void NavigationCompleted(bool success, int httpStatus)
    {
        Tick();
        if (Result is not null || State is StoreSignInPageState.Failed or StoreSignInPageState.Expired) return;
        State = success && httpStatus < 400 ? StoreSignInPageState.Ready : StoreSignInPageState.Failed;
    }
    internal void Fail() { Tick(); if (Result is null && State != StoreSignInPageState.Expired) State = StoreSignInPageState.Failed; }
    internal bool Authorize(string code)
    {
        Tick();
        if (Result is not null || State is StoreSignInPageState.Failed or StoreSignInPageState.Expired || string.IsNullOrWhiteSpace(code)) return false;
        Result = new(StoreSignInOutcome.Authorized, code); State = StoreSignInPageState.Finished; return true;
    }
    internal bool Retry()
    {
        Tick();
        if (Result is not null || State is not (StoreSignInPageState.Failed or StoreSignInPageState.Expired)) return false;
        Result = new(StoreSignInOutcome.RetryRequested, null); State = StoreSignInPageState.Finished; return true;
    }
    internal bool Cancel()
    {
        if (Result is not null) return false;
        Result = new(StoreSignInOutcome.Canceled, null); State = StoreSignInPageState.Finished; return true;
    }
}
