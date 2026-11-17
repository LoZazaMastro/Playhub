// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Playhub.StoreLibrary;

internal sealed record WindowsStoreInstallVolume(string VolumeName, string RootPath, long FreeBytes, bool IsReady);
internal interface IWindowsStoreInstallDestinationRuntime
{
    Task<IReadOnlyList<WindowsStoreInstallVolume>> GetInstallVolumesAsync(CancellationToken token);
    Task<int?> RunOnVolumeAsync(string productId, string family, string? volume, bool launch,
        Func<WindowsStoreManagedProgress, Task> progress, CancellationToken observation);
    Task CancelInstallAsync(string productId, CancellationToken token);
}

// The public Windows InstallControl API owns licensing, deployment and file
// cleanup. The helper uses the inbox WinRT projection, without a browser/client UI.
internal sealed class StoreWindowsInstallControl(string? helperPath = null, StoreToolQuery? query = null)
{
    readonly string helper = Path.GetFullPath(helperPath ?? Path.Combine(AppContext.BaseDirectory, "StoreInstallControl.ps1"));
    readonly string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    readonly ConcurrentDictionary<string, Process> sessions = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, WindowsStoreManagedOutcome> outcomes = new(StringComparer.Ordinal);
    internal bool Available => File.Exists(helper) && File.Exists(powershell);
    internal WindowsStoreManagedOutcome? TakeOutcome(string id) => outcomes.TryRemove(id, out var value) ? value : null;
    internal static bool ValidVolume(string? value) => value is { Length: >= 46 and <= 52 } &&
        value.StartsWith(@"\\?\Volume", StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParseExact(value[10..], "B", out _);
    List<string> Arguments(string action) => ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helper, "-Action", action];

    internal async Task<IReadOnlyList<WindowsStoreInstallVolume>> GetVolumesAsync(CancellationToken token)
    {
        if (!Available) throw new StoreOperationException("windows_store_cli_unavailable");
        var result = await (query ?? StoreToolProcess.QueryAsync)(powershell, Arguments("locations"), new Dictionary<string, string>(), token);
        if (result.Code != 0 || result.Json.Length > 65536) throw new StoreOperationException("windows_store_destination_unavailable");
        using var document = JsonDocument.Parse(result.Json);
        var root = document.RootElement;
        if (root.GetProperty("protocol").GetInt32() != 1 || root.GetProperty("kind").GetString() != "locations")
            throw new StoreOperationException("windows_store_destination_unavailable");
        var volumes = root.GetProperty("locations").Deserialize<WindowsStoreInstallVolume[]>(StoreLibraryService.JsonOptions);
        if (volumes is null || volumes.Length > 26 || volumes.Any(v => v is null || !ValidVolume(v.VolumeName) ||
                v.RootPath is not { Length: 3 } || !char.IsAsciiLetter(v.RootPath[0]) || v.RootPath[1..] != @":\" || v.FreeBytes < 0 || !v.IsReady) ||
            volumes.Select(v => v.VolumeName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != volumes.Length ||
            volumes.Select(v => v.RootPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != volumes.Length)
            throw new StoreOperationException("windows_store_destination_unavailable");
        return volumes;
    }
    internal async Task CancelAsync(string productId, CancellationToken token)
    {
        Process? process=null;
        for(int attempt=0;attempt<100&&!sessions.TryGetValue(productId,out process);attempt++)
            await Task.Delay(50,token);
        if(process is null)throw new StoreOperationException("windows_store_cancel_unavailable");
        try { await process.StandardInput.WriteLineAsync("cancel".AsMemory(), token); await process.StandardInput.FlushAsync(token); }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException) { throw new StoreOperationException("windows_store_cancel_failed"); }
    }
    internal async Task<int?> RunAsync(string productId, string family, string? volume, bool launch,
        Func<WindowsStoreManagedProgress, Task> progress, CancellationToken token)
    {
        if (!StoreXboxAccountCollections.ValidId(productId) || !WindowsXboxPackageCatalog.ValidFamily(family) || volume is not null && !ValidVolume(volume))
            throw new StoreOperationException("windows_store_product_invalid");
        if (!Available) throw new StoreOperationException("windows_store_cli_unavailable");
        var arguments = Arguments(launch ? "install" : "observe");
        arguments.AddRange(["-ProductId", productId, "-FamilyName", family]);
        if (volume is not null) arguments.AddRange(["-VolumeName", volume]);
        var start = new ProcessStartInfo(powershell)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(helper)! };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        Process process;
        lock (sessions)
        {
            if (sessions.ContainsKey(productId)) throw new StoreOperationException("provider_busy");
            outcomes.TryRemove(productId, out _);
            process = Process.Start(start) ?? throw new StoreOperationException("windows_store_start_failed");
            sessions[productId] = process;
        }
        using var ownedProcess = process;
        string? hresult = null, errorCode = null; bool observed = false; int? exit = null;
        try
        {
            async Task ReadOutput()
            {
                while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                {
                    if (line.Length > 65536) throw new StoreOperationException("windows_store_response_invalid");
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.GetProperty("protocol").GetInt32() != 1) throw new StoreOperationException("windows_store_response_invalid");
                    string? kind = root.GetProperty("kind").GetString();
                    if (kind == "progress")
                    {
                        var phase = root.GetProperty("phase").GetString(); double percent = root.GetProperty("percent").GetDouble();
                        long bytes = root.GetProperty("downloadedBytes").GetInt64(), total = root.GetProperty("totalBytes").GetInt64();
                        double? speed = root.TryGetProperty("bytesPerSecond", out var rate) && rate.ValueKind == JsonValueKind.Number ? rate.GetDouble() : null;
                        if (phase is not ("starting" or "downloading" or "installing" or "paused") || !double.IsFinite(percent) || percent is < 0 or > 100 ||
                            bytes < 0 || total < 0 || speed is { } value && (!double.IsFinite(value) || value < 0)) throw new StoreOperationException("windows_store_response_invalid");
                        observed = true; await progress(new(phase, percent, bytes, total == 0 ? null : total, speed,root.TryGetProperty("finalDeploymentConfirmed",out var final)&&final.ValueKind==JsonValueKind.True&&phase=="installing"&&percent==100));
                    }
                    else if(kind=="completed"&&root.TryGetProperty("finalDeploymentConfirmed",out var confirmed)&&confirmed.ValueKind==JsonValueKind.True)
                    {await progress(new("installing",100,null,null,null,true));}
                    else if (kind == "error")
                    {
                        var code = root.GetProperty("code").GetString();
                        errorCode = code is "windows_store_install_failed" or "windows_store_destination_unavailable" or "windows_store_install_not_started" ? code : "windows_store_install_failed";
                        if (root.TryGetProperty("hresult", out var hr) && hr.GetString() is { } text &&
                            System.Text.RegularExpressions.Regex.IsMatch(text, "^0x[89A-Fa-f][0-9A-Fa-f]{7}$")) hresult = text;
                    }
                }
            }
            async Task DrainErrors()
            {
                // Native helper errors are codes. Never persist raw PowerShell output.
                char[] buffer = new char[1024]; long count = 0; int read;
                while ((read = await process.StandardError.ReadAsync(buffer.AsMemory(), token)) > 0)
                    if ((count += read) > 65536) throw new StoreOperationException("windows_store_response_invalid");
            }
            await Task.WhenAll(ReadOutput(), DrainErrors(), process.WaitForExitAsync(token));
            exit = process.ExitCode;
            return exit;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Host maintenance detaches observation; explicit Cancel uses the
            // native item.Cancel command instead. The OS operation is retained.
            try { await process.StandardInput.WriteLineAsync("detach"); await process.StandardInput.FlushAsync(); } catch { }
            return null;
        }
        finally
        {
            sessions.TryRemove(new KeyValuePair<string, Process>(productId, process));
            outcomes[productId] = new(exit, hresult, observed, errorCode);
        }
    }
}
