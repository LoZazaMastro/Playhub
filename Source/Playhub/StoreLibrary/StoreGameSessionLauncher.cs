// SPDX-License-Identifier: MIT
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.StoreLibrary;

/// <summary>Starts the installed GameSession wrapper; provider arguments never enter the UI.</summary>
internal sealed class StoreGameSessionLauncher(string appDirectory, Func<ProcessStartInfo, bool>? start = null)
{
    private readonly Func<ProcessStartInfo, bool> startProcess = start ?? Start;
    private static bool Start(ProcessStartInfo value) { using var process = Process.Start(value); return process is not null; }
    public Task<bool> LaunchAsync(StoreLaunchDescriptor descriptor, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (descriptor.Key.Store is not (StoreKind.Epic or StoreKind.Gog or StoreKind.Amazon or StoreKind.Xbox) || !StoreInstalledGameController.SafeKey(descriptor.Key)
                || !descriptor.IsInstalled || !descriptor.IsLaunchable || descriptor.ErrorCode is not null) return Task.FromResult(false);
            string root = Path.GetFullPath(appDirectory), executable = Path.Combine(root, "Playhub.GameSession.exe");
            string manifest = Path.Combine(root, "StoreLibrary", "store-runtime.json");
            if (!SafePath(executable) || !SafePath(manifest) || new FileInfo(manifest).Length > 1024 * 1024) return Task.FromResult(false);
            using var manifestPin = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var document = JsonDocument.Parse(manifestPin);
            var json = document.RootElement;
            if (json.GetProperty("protocol").GetInt32() != 1 || !json.GetProperty("finalized").GetBoolean()) return Task.FromResult(false);
            var clients = json.GetProperty("allowedClients").EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "app"
                && c.TryGetProperty("path", out var path) && string.Equals(path.GetString(), "Playhub.GameSession.exe", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (clients.Length != 1) return Task.FromResult(false);
            string? expected = clients[0].GetProperty("sha256").GetString();
            if (expected is not { Length: 64 } || !expected.All(char.IsAsciiHexDigit)) return Task.FromResult(false);
            using var executablePin = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!StringComparer.OrdinalIgnoreCase.Equals(expected, Convert.ToHexString(SHA256.HashData(executablePin)))) return Task.FromResult(false);
            token.ThrowIfCancellationRequested();
            var process = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
            process.ArgumentList.Add("--store-library");
            process.ArgumentList.Add(descriptor.Key.Store.ToString().ToLowerInvariant());
            process.ArgumentList.Add(descriptor.Key.Id);
            return Task.FromResult(startProcess(process));
        }
        catch (Exception e) when (e is not OutOfMemoryException) { return Task.FromResult(false); }
    }
    private static bool SafePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return false;
        for (string? p = path; p is not null; p = Path.GetDirectoryName(p))
            if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
}
