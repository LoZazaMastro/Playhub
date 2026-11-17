// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Playhub.StoreLibrary;

// Link this client file and StoreLibraryModels into WinUI. It never imports provider libraries.
public sealed partial class StoreLibraryService : IStoreLibraryService, IStoreDownloadHistoryService, IStoreCanceledDownloadHistoryService, IStoreHistoryRemovalService, IStoreCancelCleanupService
{
    private readonly string pipe;
    private readonly string hostPath;
    private string? verifiedManifest;
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static StoreLibraryService CreateDefault() => new(Path.Combine(AppContext.BaseDirectory, "StoreLibrary", "Playhub.StoreHost.exe"));
    public static string CurrentPipeName => "Playhub.StoreLibrary.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("user_missing"))))[..24];
    public StoreLibraryService(string hostPath, string? pipeName = null) { this.hostPath = Path.GetFullPath(hostPath); pipe = pipeName ?? CurrentPipeName; }
    public Task<StoreLibrarySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Call<StoreLibrarySnapshot>("snapshot", new { }, cancellationToken);
    public Task<StoreConnectSession> BeginConnectAsync(StoreKind store, CancellationToken cancellationToken = default) => Call<StoreConnectSession>("connect-begin", new { store }, cancellationToken);
    public Task<StoreCommandResult> CompleteConnectAsync(Guid sessionId, string authorizationCode, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("connect-complete", new { sessionId, authorizationCode }, cancellationToken);
    public Task<StoreCommandResult> DisconnectAsync(StoreKind store, long expectedAccountRevision, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("disconnect", new { store, expectedAccountRevision }, cancellationToken);
    public Task<StoreCommandResult> RefreshAsync(StoreKind store, long expectedAccountRevision, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("refresh", new { store, expectedAccountRevision }, cancellationToken);
    public Task<StoreInstallPreparation> PrepareInstallAsync(StoreGameKey key, long expectedAccountRevision, string? absoluteDestination = null, CancellationToken cancellationToken = default) => Call<StoreInstallPreparation>("prepare", new { key, expectedAccountRevision, absoluteDestination }, cancellationToken);
    public Task<StoreCommandResult> EnqueueAsync(StoreEnqueueRequest request, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("enqueue", request, cancellationToken);
    public Task<StoreCommandResult> ControlAsync(StoreJobCommand command, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("control", command, cancellationToken);
    public Task<StoreCommandResult> CancelCleanupAsync(StoreCancelCleanupRequest request, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("cancel-cleanup", request, cancellationToken);
    public Task<StoreCommandResult> ClearCompletedAsync(StoreClearCompletedRequest request, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("clear-completed", request, cancellationToken);
    public Task<StoreCommandResult> ClearCanceledAsync(StoreClearCanceledRequest request, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("clear-canceled", request, cancellationToken);
    public Task<StoreCommandResult> RemoveHistoryAsync(StoreRemoveHistoryRequest request, CancellationToken cancellationToken = default) => Call<StoreCommandResult>("remove-history", request, cancellationToken);
    public Task<StoreLaunchDescriptor> GetVerifiedLaunchDescriptorAsync(StoreGameKey key, CancellationToken cancellationToken = default) => Call<StoreLaunchDescriptor>("launch-descriptor", new { key }, cancellationToken);
    private async Task<T> Call<T>(string method, object args, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(method is "refresh" or "prepare-uninstall" or "uninstall" ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(60));
        VerifyHost();
        using var channel = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await channel.ConnectAsync(200, timeout.Token); }
        catch (TimeoutException)
        {
            VerifyHost();
            Process.Start(new ProcessStartInfo(hostPath) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--serve" } })?.Dispose();
            await channel.ConnectAsync(10000, timeout.Token);
        }
        if (!GetNamedPipeServerProcessId(channel.SafePipeHandle, out var serverPid)) throw new InvalidOperationException("host_identity_invalid");
        using (var process = Process.GetProcessById(checked((int)serverPid)))
            if (process.HasExited || !StringComparer.OrdinalIgnoreCase.Equals(process.MainModule?.FileName, hostPath)) throw new InvalidOperationException("host_identity_invalid");
        using var reader = new StreamReader(channel, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(channel, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var id = Guid.NewGuid();
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { protocol = 1, id, method, args }, JsonOptions).AsMemory(), timeout.Token);
        var line = await ReadResponse(reader, timeout.Token);
        using var json = JsonDocument.Parse(line); var root = json.RootElement;
        if (root.GetProperty("id").GetGuid() != id || root.GetProperty("protocol").GetInt32() != 1) throw new InvalidOperationException("host_response_invalid");
        if (!root.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(root.GetProperty("errorCode").GetString() ?? "host_error");
        return root.GetProperty("data").Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException("host_response_invalid");
    }
    private void VerifyHost()
    {
        StoreMaintenance.ThrowIfActive();
        var manifest = Path.Combine(Path.GetDirectoryName(hostPath)!, "store-runtime.json");
        var bytes = File.ReadAllBytes(manifest); var manifestHash = Convert.ToHexString(SHA256.HashData(bytes));
        if (verifiedManifest == manifestHash) return;
        using var doc = JsonDocument.Parse(bytes);
        if (doc.RootElement.GetProperty("protocol").GetInt32() != 1) throw new InvalidOperationException("runtime_manifest_invalid");
        if (doc.RootElement.TryGetProperty("finalized", out var finalized) && finalized.ValueKind == JsonValueKind.False) throw new InvalidOperationException("runtime_not_finalized");
        var hash = doc.RootElement.GetProperty("hostSha256").GetString();
        using var stream = File.OpenRead(hostPath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(hash, Convert.ToHexString(SHA256.HashData(stream)))) throw new InvalidOperationException("runtime_hash_mismatch");
        var directory = Path.GetDirectoryName(hostPath)!;
        if (!doc.RootElement.TryGetProperty("files", out var files) || files.GetArrayLength() is < 1 or > 512) throw new InvalidOperationException("runtime_manifest_invalid");
        foreach (var entry in files.EnumerateArray())
        {
            var relative = entry.GetProperty("path").GetString()!;
            var path = Path.GetFullPath(Path.Combine(directory, relative));
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("runtime_manifest_invalid");
            using var member = File.OpenRead(path);
            if (!StringComparer.OrdinalIgnoreCase.Equals(entry.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(member)))) throw new InvalidOperationException("runtime_hash_mismatch");
        }
        verifiedManifest = manifestHash;
    }
    private static async Task<string> ReadResponse(StreamReader reader, CancellationToken token)
    {
        var value = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        { var end = Array.IndexOf(buffer, '\n', 0, count); var length = end < 0 ? count : end; if (value.Length + length > 8 * 1024 * 1024) break; value.Append(buffer, 0, length); if (end >= 0) return value.ToString().TrimEnd('\r'); }
        throw new InvalidOperationException("host_response_invalid");
    }
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
}

internal static class StoreMaintenance
{
    internal sealed record Marker(int Protocol, Guid OwnerId, DateTimeOffset ExpiresAt);
    internal static string MarkerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "StoreLibrary", "maintenance-v1.json");
    internal static Marker? Current()
    {
        if (!File.Exists(MarkerPath)) return null;
        var value = JsonSerializer.Deserialize<Marker>(File.ReadAllText(MarkerPath), StoreLibraryService.JsonOptions);
        if (value is not { Protocol: 1 } || value.OwnerId == Guid.Empty) throw new InvalidOperationException("maintenance_marker_invalid");
        var remaining = value.ExpiresAt - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero && remaining <= TimeSpan.FromSeconds(330) ? value : null;
    }
    internal static void ThrowIfActive() { if (Current() != null) throw new InvalidOperationException("maintenance_in_progress"); }
    internal static Marker GetOrCreate()
    {
        if (Current() is { } existing) return existing;
        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
        var marker = new Marker(1, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        File.WriteAllText(MarkerPath + ".new", JsonSerializer.Serialize(marker, StoreLibraryService.JsonOptions)); File.Move(MarkerPath + ".new", MarkerPath, true); return marker;
    }
}
