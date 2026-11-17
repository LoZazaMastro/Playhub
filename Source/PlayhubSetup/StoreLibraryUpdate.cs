// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PlayhubSetup;

internal sealed class StoreLibraryUpdate : IDisposable
{
    private readonly string leasePath;
    private readonly Guid lease = Guid.NewGuid();
    private StoreLibraryUpdate()
    {
        leasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playhub", "StoreLibrary", "maintenance-v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
        File.WriteAllText(leasePath, JsonSerializer.Serialize(new { protocol = 1, ownerId = lease, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5) }));
    }
    internal static StoreLibraryUpdate Quiesce(string installDirectory)
    {
        var maintenance = new StoreLibraryUpdate();
        try { StopAsync(installDirectory, maintenance.lease).GetAwaiter().GetResult(); return maintenance; }
        catch { maintenance.Dispose(); throw; }
    }
    private static async Task StopAsync(string installDirectory, Guid lease)
    {
        string expected = Path.GetFullPath(Path.Combine(installDirectory, "StoreLibrary", "Playhub.StoreHost.exe"));
        var owners = new List<Process>();
        try
        {
            foreach (var candidate in Process.GetProcessesByName("Playhub.StoreHost"))
            {
                try
                {
                    if (!candidate.HasExited && StringComparer.OrdinalIgnoreCase.Equals(candidate.MainModule?.FileName, expected))
                    { _ = candidate.Handle; owners.Add(candidate); continue; }
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                candidate.Dispose();
            }
            if (owners.Count == 0) return;
            string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new IOException("Store download owner is unavailable.");
            string pipe = "Playhub.StoreLibrary.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..24];
            using var token = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            using var channel = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await channel.ConnectAsync(3000, token.Token); }
            catch (TimeoutException) when (owners.All(p => p.HasExited)) { return; }
            if (!GetNamedPipeServerProcessId(channel.SafePipeHandle, out var pid) || !owners.Any(p => p.Id == pid))
                throw new IOException("The store download host could not be confirmed.");
            var id = Guid.NewGuid();
            using var reader = new StreamReader(channel, new UTF8Encoding(false), false, 4096, true);
            using var writer = new StreamWriter(channel, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { protocol = 1, id, method = "quiesce", args = new { } }).AsMemory(), token.Token);
            var buffer = new StringBuilder();
            while (buffer.Length <= 4096)
            {
                var character = new char[1];
                if (await reader.ReadAsync(character.AsMemory(), token.Token) == 0) throw new IOException("Store downloads did not confirm their checkpoint.");
                if (character[0] == '\n') break;
                buffer.Append(character[0]);
            }
            using var reply = JsonDocument.Parse(buffer.ToString());
            if (reply.RootElement.GetProperty("protocol").GetInt32() != 1 ||
                reply.RootElement.GetProperty("id").GetGuid() != id || !reply.RootElement.GetProperty("ok").GetBoolean() ||
                !reply.RootElement.GetProperty("data").GetProperty("accepted").GetBoolean() ||
                reply.RootElement.GetProperty("data").GetProperty("ownerId").GetGuid() != lease)
                throw new IOException("Store downloads could not pause for this update.");
            foreach (var owner in owners) await owner.WaitForExitAsync(token.Token);
        }
        finally { foreach (var owner in owners) owner.Dispose(); }
    }
    public void Dispose()
    {
        try
        {
            if (!File.Exists(leasePath)) return;
            using var json = JsonDocument.Parse(File.ReadAllText(leasePath));
            if (json.RootElement.TryGetProperty("ownerId", out var current) && current.GetGuid() == lease) File.Delete(leasePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }
    [DllImport("kernel32", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
}
