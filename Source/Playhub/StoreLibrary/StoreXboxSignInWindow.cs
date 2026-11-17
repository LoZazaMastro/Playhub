// SPDX-License-Identifier: MIT
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

/// <summary>Displays Microsoft's normal sign-in page; no verification code is rendered by Playhub.</summary>
internal sealed class StoreXboxSignInWindow : Window
{
    private readonly WebView2 browser = new();
    private readonly ProgressBar loading = new() { IsIndeterminate = true, Height = 3 };
    private readonly TaskCompletionSource<StoreXboxSignInResult?> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource lifetime;
    private readonly StoreXboxSignInController controller;
    private readonly StoreXboxSignInSession session;
    private bool finished;

    private StoreXboxSignInWindow(StoreXboxSignInController owner, StoreXboxSignInSession current, Func<string, string> text, CancellationToken token)
    {
        controller = owner; session = current; lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Title = text("StoreLibrary.Connect") + " · Xbox";
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new());
        var heading = new StackPanel { Spacing = 6, Padding = new Thickness(20, 14, 20, 14) };
        heading.Children.Add(new TextBlock { Text = "Microsoft · Xbox", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = text("StoreLibrary.XboxSignInHelp"), TextWrapping = TextWrapping.Wrap, Opacity = .75 });
        layout.Children.Add(heading); Grid.SetRow(loading, 1); layout.Children.Add(loading);
        Grid.SetRow(browser, 2); layout.Children.Add(browser); Content = layout;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min(900, area.Width), Math.Min(760, area.Height)));
        Closed += (_, _) => Finish(null, false);
    }

    internal static async Task<StoreXboxSignInResult?> ShowAsync(StoreXboxSignInController owner, StoreXboxSignInSession session, Func<string, string> text, CancellationToken token)
    {
        if (token.IsCancellationRequested || !StoreXboxSignInController.ValidAuthorizationUri(session.AuthorizationUri)) { await owner.CancelAsync(); return null; }
        var window = new StoreXboxSignInWindow(owner, session, text, token);
        using var registration = token.Register(() => window.DispatcherQueue.TryEnqueue(() => window.Finish(null)));
        try
        {
            window.Activate();
            // Closing the window must release the auth-only process even when
            // WebView2 has not finished initializing. Initialize owns its errors.
            _ = window.InitializeAsync();
            return await window.done.Task;
        }
        finally { await owner.CancelAsync(); window.lifetime.Dispose(); }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var directory = Path.Combine(Playhub.Services.AppPaths.LocalDataRoot, "store-sign-in");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, directory, null)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            if (finished) return;
            var options = environment.CreateCoreWebView2ControllerOptions(); options.ProfileName = "Xbox"; options.IsInPrivateModeEnabled = true;
            await browser.EnsureCoreWebView2Async(environment, options)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            if (finished) return;
            var core = browser.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NavigationStarting += (_, e) => { if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => { e.Handled = true; if (!finished && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0) core.Navigate(uri.AbsoluteUri); };
            core.NavigationCompleted += (_, _) => loading.Visibility = Visibility.Collapsed;
            core.Navigate(session.AuthorizationUri.AbsoluteUri);
            Finish(await controller.PollAsync(session, lifetime.Token));
        }
        catch { Finish(null); }
    }
    private void Finish(StoreXboxSignInResult? result, bool close = true)
    {
        if (finished) return;
        finished = true; lifetime.Cancel();
        try { browser.Close(); } catch { }
        done.TrySetResult(result); if (close) Close();
    }
}
