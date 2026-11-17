// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Playhub.StoreLibrary;

public interface IStoreProtocolExtension
{
    Task<(bool Handled, object? Data)> TryHandleAsync(string method, JsonElement args, CancellationToken cancellationToken);
}

public sealed class StorePipeServer(StoreQueueEngine engine, Func<uint, bool> authorize, IReadOnlyList<IStoreProtocolExtension>? extensions = null, string? pipeName = null, SteamDownloadAuthority? steamAuthority = null, Func<uint, bool>? authorizePrivateLaunch = null, Action? exitAfterQuiesce = null, Func<(Guid OwnerId, DateTimeOffset ExpiresAt)>? maintenanceFactory = null, TimeSpan? quiesceBudget = null, Func<uint, string, bool>? authorizeMethod = null)
{
    private sealed record QuiesceAck(bool Accepted, Guid OwnerId, DateTimeOffset ExpiresAt, string? ErrorCode);
    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(pipeName ?? StoreLibraryService.CurrentPipeName, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(token); }
            catch { await pipe.DisposeAsync(); throw; }
            _ = HandleOwnedAsync(pipe, token);
        }
    }
    private async Task HandleOwnedAsync(NamedPipeServerStream pipe, CancellationToken shutdown)
    {
        await using var owned = pipe;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var id = Guid.Empty;
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid)) throw new StoreOperationException("client_not_authorized");
            var line = await ReadBoundedAsync(reader, 65536, timeout.Token);
            using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
            id = root.GetProperty("id").GetGuid();
            if (id == Guid.Empty || root.GetProperty("protocol").GetInt32() != 1) throw new StoreOperationException("protocol_invalid");
            var method = root.GetProperty("method").GetString() ?? ""; var args = root.GetProperty("args");
            if (method is "refresh" or "prepare-uninstall" or "uninstall") timeout.CancelAfter(TimeSpan.FromMinutes(2));
            // Same-user quiescence is revocation ONLY; a future installer cannot
            // be pinned in an older release. Every data/grant method stays pinned.
            if (authorizeMethod?.Invoke(pid, method) == false || method != "quiesce" && !authorize(pid)) throw new StoreOperationException("client_not_authorized");
            object data = method switch
            {
                "snapshot" => await engine.GetSnapshotAsync(timeout.Token),
                "downloads" => (await engine.GetSnapshotAsync(timeout.Token)).Jobs,
                "connect-begin" => await engine.BeginConnectAsync(Read<StoreKind>(args, "store"), timeout.Token),
                "connect-complete" => await engine.CompleteConnectAsync(args.GetProperty("sessionId").GetGuid(), args.GetProperty("authorizationCode").GetString() ?? "", timeout.Token),
                "disconnect" => await engine.DisconnectAsync(Read<StoreKind>(args, "store"), args.GetProperty("expectedAccountRevision").GetInt64(), timeout.Token),
                "refresh" => await engine.RefreshAsync(Read<StoreKind>(args, "store"), args.GetProperty("expectedAccountRevision").GetInt64(), timeout.Token),
                "prepare" => await engine.PrepareInstallWithLanguageAsync(Read<StoreGameKey>(args, "key"), args.GetProperty("expectedAccountRevision").GetInt64(), args.TryGetProperty("absoluteDestination", out var dest) && dest.ValueKind == JsonValueKind.String ? dest.GetString() : null, timeout.Token,args.TryGetProperty("language",out var lang)&&lang.ValueKind==JsonValueKind.String?lang.GetString():null),
                "prepare-parent" => await engine.PrepareInstallParentAsync(Read<StoreGameKey>(args, "key"), args.GetProperty("expectedAccountRevision").GetInt64(), args.GetProperty("parentPath").GetString() ?? throw new StoreOperationException("invalid_destination"), timeout.Token,args.TryGetProperty("language",out var parentLang)&&parentLang.ValueKind==JsonValueKind.String?parentLang.GetString():null),
                "directories" => engine.GetInstallDirectories(args.TryGetProperty("path", out var directory) && directory.ValueKind == JsonValueKind.String ? directory.GetString() : null),
                "enqueue" => await engine.EnqueueAsync(args.Deserialize<StoreEnqueueRequest>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid"), timeout.Token),
                "control" => await engine.ControlAsync(args.Deserialize<StoreJobCommand>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid"), timeout.Token),
                "cancel-cleanup" => await engine.CancelCleanupAsync(args.Deserialize<StoreCancelCleanupRequest>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid"), timeout.Token),
                "clear-completed" => await engine.ClearCompletedAsync(args.Deserialize<StoreClearCompletedRequest>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid"), timeout.Token),
                "clear-canceled" => await engine.ClearCanceledAsync(args.Deserialize<StoreClearCanceledRequest>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid"), timeout.Token),
                "remove-history" => await engine.RemoveHistoryAsync(args.Deserialize<StoreRemoveHistoryRequest>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid"), timeout.Token),
                "launch-descriptor" => await engine.GetVerifiedLaunchDescriptorAsync(Read<StoreGameKey>(args, "key"), timeout.Token),
                "prepare-launch" => authorizePrivateLaunch?.Invoke(pid) == true ? await engine.PreparePrivateLaunchAsync(Read<StoreGameKey>(args, "key"), args.GetProperty("requestId").GetGuid(), timeout.Token) : throw new StoreOperationException("private_role_required"),
                "steam-register" => steamAuthority?.Register(pid) ?? throw new StoreOperationException("steam_observer_unavailable"),
                "quiesce" => await Quiesce(timeout.Token),
                "steam-unregister" => await RevokeSteam(pid, args, timeout.Token),
                "steam-activity" => await ObserveSteam(pid, args, timeout.Token),
                _ => await ExtensionAsync(method, args, timeout.Token)
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { protocol = 1, id, ok = true, data }, StoreLibraryService.JsonOptions).AsMemory(), timeout.Token);
            if (data is QuiesceAck { Accepted: true }) exitAfterQuiesce?.Invoke();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            var code = e is StoreOperationException operation ? operation.Code : e is OperationCanceledException ? "request_timeout" : "request_failed";
            try { await writer.WriteLineAsync(JsonSerializer.Serialize(new { protocol = 1, id, ok = false, errorCode = code }, StoreLibraryService.JsonOptions)); } catch (IOException) { }
        }
    }
    private async Task<QuiesceAck> Quiesce(CancellationToken token)
    {
        (Guid OwnerId, DateTimeOffset ExpiresAt) marker;
        if (maintenanceFactory != null) marker = maintenanceFactory();
        else { var held = StoreMaintenance.GetOrCreate(); marker = (held.OwnerId, held.ExpiresAt); }
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token); bounded.CancelAfter(quiesceBudget ?? TimeSpan.FromSeconds(30));
        var accepted = await engine.QuiesceAsync(bounded.Token);
        return new(accepted, marker.OwnerId, marker.ExpiresAt, accepted ? null : "downloads_not_quiescent");
    }
    private async Task<object> ObserveSteam(uint pid, JsonElement args, CancellationToken token)
    {
        var packet = args.Deserialize<StoreSteamActivity>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid");
        var accepted = steamAuthority != null && steamAuthority.Observe(pid, packet) && await engine.ObserveSteamAsync(packet, true, token);
        return new { accepted };
    }
    private async Task<object> RevokeSteam(uint pid, JsonElement args, CancellationToken token)
    {
        var id = args.GetProperty("ownerId").GetGuid(); var accepted = steamAuthority != null && steamAuthority.Revoke(pid, id);
        if (accepted) await engine.InvalidateSteamAsync(id, token); return new { accepted };
    }
    private async Task<object> ExtensionAsync(string method, JsonElement args, CancellationToken token)
    {
        if (extensions != null) foreach (var extension in extensions) { var result = await extension.TryHandleAsync(method, args, token); if (result.Handled) return result.Data ?? new { }; }
        throw new StoreOperationException("method_unavailable");
    }
    private static T Read<T>(JsonElement args, string name) => args.GetProperty(name).Deserialize<T>(StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("request_invalid");
    internal static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token); if (count == 0) throw new StoreOperationException("request_incomplete");
            var end = Array.IndexOf(buffer, '\n', 0, count); var length = end < 0 ? count : end;
            if (result.Length + length > limit) throw new StoreOperationException("request_too_large");
            result.Append(buffer, 0, length); if (end >= 0) return result.ToString().TrimEnd('\r');
        }
    }
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
}
