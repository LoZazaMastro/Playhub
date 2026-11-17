// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Playhub.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

/// <summary>Store-owned authentication only; the Steam portal remains the dashboard.</summary>
internal static class StoreSignInCommand
{
    private static Window? lifetimeWindow;

    internal static async Task<int> RunAsync(StoreKind kind)
    {
        // Keep the dispatcher alive after the browser closes while its code is
        // exchanged. This window is never activated or shown to the user.
        lifetimeWindow = new Window { Content = new Grid() };
        lifetimeWindow.AppWindow.Hide();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var service = StoreLibraryService.CreateDefault();
        using var controller = new StoreLibraryController(service);
        // Authentication is also opened as a separate app command from Steam.
        // Load the same saved language as the desktop before creating its UI.
        string? language = null;
        string Text(string key) => LocalizationService.Translate(language, key);
        try
        {
            language = (await new SettingsService().LoadAsync()).Language;
            await controller.ReadAsync();
            var account = controller.Snapshot.Accounts.FirstOrDefault(account => account.Store == kind);
            if (account?.State == StoreConnectionState.Connected) return 0;
            if (account?.Capabilities.CanConnect != true) return 2;
            if (kind == StoreKind.Xbox && service is IStoreXboxAccountService xbox)
            {
                var signIn = new StoreXboxSignInController(xbox);
                try
                {
                    var session = await signIn.BeginAsync(account.Revision, cancellation.Token);
                    if (session is null) return 2;
                    var result = await StoreXboxSignInWindow.ShowAsync(signIn, session, Text, cancellation.Token);
                    if (result?.State != StoreXboxSignInState.Connected)
                        return result?.State == StoreXboxSignInState.Canceled ? 1 : 2;
                    await controller.ReadAsync();
                    await controller.RefreshAsync(kind);
                    return 0;
                }
                finally { await signIn.CancelAsync(); }
            }
            while (!cancellation.IsCancellationRequested)
            {
                var session = await controller.BeginConnectAsync(kind);
                if (session is null) return 2;
                var result = await StoreSignInWindow.ShowAsync(session, Text, cancellation.Token);
                if (result.Outcome == StoreSignInOutcome.RetryRequested) { controller.CancelConnect(); continue; }
                if (result.Outcome != StoreSignInOutcome.Authorized || string.IsNullOrWhiteSpace(result.Code))
                { controller.CancelConnect(); return 1; }
                return await controller.ConnectAndLoadLibraryAsync(session, result.Code) ? 0 : 2;
            }
            return 1;
        }
        catch (OperationCanceledException) { return 1; }
        catch { return 2; } // No authorization URI, code or token enters diagnostics.
    }
}
