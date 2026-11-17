// SPDX-License-Identifier: MIT
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

/// <summary>One official Microsoft sign-in session; credentials remain in StoreHost.</summary>
internal sealed class StoreXboxSignInController(IStoreXboxAccountService service,
    Func<DateTimeOffset>? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<TimeSpan, CancellationToken, Task> wait = delay ?? Task.Delay;
    private StoreXboxSignInSession? held;
    private long generation;
    private bool starting, polling, terminal;

    internal static bool ValidAuthorizationUri(Uri? uri)
    {
        if (uri is not { IsAbsoluteUri: true } || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.Host != "www.microsoft.com" || uri.AbsolutePath != "/link"
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        var fields = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 1 || !fields[0].StartsWith("otc=", StringComparison.Ordinal)) return false;
        string value = Uri.UnescapeDataString(fields[0][4..]);
        return value.Length is >= 4 and <= 32 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    internal async Task<StoreXboxSignInSession?> BeginAsync(long expectedRevision, CancellationToken token)
    {
        if (starting || held is not null || expectedRevision < 0 || token.IsCancellationRequested) return null;
        starting = true; long expected = ++generation;
        try
        {
            var session = await service.BeginXboxSignInAsync(expectedRevision, token).WaitAsync(TimeSpan.FromSeconds(30), token);
            if (expected != generation || token.IsCancellationRequested)
            {
                if (session.SessionId != Guid.Empty) await CancelRemoteAsync(session.SessionId);
                return null;
            }
            if (session.SessionId == Guid.Empty || !ValidAuthorizationUri(session.AuthorizationUri)
                || session.ExpiresAt <= now() || session.ExpiresAt > now().AddMinutes(30)
                || session.PollAfterSeconds is < 1 or > 30)
            {
                if (session.SessionId != Guid.Empty) await CancelRemoteAsync(session.SessionId);
                return null;
            }
            held = session; terminal = false; return session;
        }
        finally { starting = false; }
    }

    internal async Task<StoreXboxSignInResult?> PollAsync(StoreXboxSignInSession session, CancellationToken token)
    {
        if (!ReferenceEquals(held, session) || terminal || polling) return null;
        polling = true;
        try
        {
            long expected = generation; int seconds = session.PollAfterSeconds;
            while (!token.IsCancellationRequested && expected == generation && ReferenceEquals(held, session) && now() < session.ExpiresAt)
            {
                await wait(TimeSpan.FromSeconds(seconds), token);
                if (token.IsCancellationRequested || expected != generation || !ReferenceEquals(held, session) || now() >= session.ExpiresAt) return null;
                var result = await service.PollXboxSignInAsync(session.SessionId, token).WaitAsync(TimeSpan.FromSeconds(30), token);
                if (token.IsCancellationRequested || expected != generation || !ReferenceEquals(held, session) || now() >= session.ExpiresAt) return null;
                if (result.SessionId != session.SessionId || !Enum.IsDefined(result.State) || !StoreLibraryController.Valid(result.Snapshot)) return null;
                if (result.State == StoreXboxSignInState.Pending)
                {
                    if (result.PollAfterSeconds is < 1 or > 30) return null;
                    seconds = result.PollAfterSeconds; continue;
                }
                if (result.State == StoreXboxSignInState.Connected
                    && !result.Snapshot.Accounts.Any(a => a.Store == StoreKind.Xbox && a.State == StoreConnectionState.Connected)) return null;
                terminal = true; return result;
            }
            return null;
        }
        finally { polling = false; }
    }

    internal async Task CancelAsync()
    {
        ++generation;
        var session = held; held = null;
        if (session is not null && !terminal) await CancelRemoteAsync(session.SessionId);
    }
    private async Task CancelRemoteAsync(Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await service.CancelXboxSignInAsync(id, timeout.Token).WaitAsync(TimeSpan.FromSeconds(5), timeout.Token); }
        catch { /* Closing a view never grants or completes account authorization. */ }
    }
}
