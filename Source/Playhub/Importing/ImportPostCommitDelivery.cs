// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Importing;

internal static class ImportPostCommitDelivery
{
    // Shortcuts are already committed. A failed artwork handoff remains pending
    // for that game and cannot undo the import or prevent later games applying.
    internal static async Task<int> RunAsync<T>(IEnumerable<T> games, Func<T, Task> deliver,
        Action<Exception> record, CancellationToken token = default)
    {
        int pending = 0;
        foreach (var game in games)
        {
            token.ThrowIfCancellationRequested();
            try { await deliver(game); }
            catch (Exception error) when (!token.IsCancellationRequested && error is IOException or InvalidOperationException
                or HttpRequestException or TimeoutException or JsonException or COMException or OperationCanceledException)
            { pending++; record(error); }
        }
        return pending;
    }
}
