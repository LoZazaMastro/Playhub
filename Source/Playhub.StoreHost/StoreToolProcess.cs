// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playhub.StoreLibrary;

internal sealed record SupervisedToolRequest(string Executable, string[] Arguments, Dictionary<string, string> Environment, string WorkingDirectory);
internal static class StoreToolProcess
{
    internal static async Task<(int Code, string Json)> QueryAsync(string executable, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> environment, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        foreach (var e in environment) start.Environment[e.Key] = e.Value;
        using var p = Process.Start(start) ?? throw new StoreOperationException("tool_start_failed"); p.StandardInput.Close();
        // Legendary's owned-library JSON includes complete product metadata.
        // The real 212-game library is already larger than the ordinary 8 MiB
        // query cap. Only this exact metadata command receives the larger bound.
        var library = args.SequenceEqual(new[] { "list-games", "--json", "--include-non-installable", "--platform", "Windows" }, StringComparer.Ordinal);
        var maximumOutputBytes = library ? 64L * 1024 * 1024 : 8L * 1024 * 1024;
        var text = new StringBuilder();
        void StopOwnedQuery()
        {
            try { if (!p.HasExited) p.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        async Task Drain(StreamReader reader, StringBuilder? result, long maximumBytes, string failureCode)
        {
            // Bound bytes while reading fixed chunks, including a single huge
            // JSON/error line. No partial JSON or raw stderr leaves this method.
            var buffer = new char[8192];
            var encoder = Encoding.UTF8.GetEncoder();
            long bytes = 0;
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
                {
                    bytes += encoder.GetByteCount(buffer, 0, count, false);
                    if (bytes > maximumBytes) throw new StoreOperationException(failureCode);
                    result?.Append(buffer, 0, count);
                }
                bytes += encoder.GetByteCount(Array.Empty<char>(), 0, 0, true);
                if (bytes > maximumBytes) throw new StoreOperationException(failureCode);
            }
            catch { StopOwnedQuery(); throw; }
        }
        var output = Drain(p.StandardOutput, text, maximumOutputBytes, "tool_response_too_large");
        var errors = Drain(p.StandardError, null, 2L * 1024 * 1024, "tool_error_response_too_large");
        try { await Task.WhenAll(output, errors, p.WaitForExitAsync(token)); }
        catch { StopOwnedQuery(); throw; } // Exact newly owned query only; never a user launcher.
        return (p.ExitCode, text.ToString());
    }
    internal static bool ParseProgress(StoreKind store, string line, ref long downloaded, ref long? total, ref double? speed, ref double? percent)
    {
        var changed = false;
        if (store == StoreKind.Gog)
        {
            var progress = Regex.Match(line, @"= Progress: [0-9.]+ (\d+)/(\d+)");
            if (progress.Success && long.TryParse(progress.Groups[1].Value, out var done) && long.TryParse(progress.Groups[2].Value, out var size) && size > 0 && done <= size) { percent = 100.0 * done / size; changed = true; }
        }
        else
        {
            var progress = Regex.Match(line, @"Progress: ([0-9.]+)%");
            if (progress.Success && double.TryParse(progress.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var reportedPercent) && reportedPercent is >= 0 and <= 100) { percent = reportedPercent; changed = true; }
        }
        var announcedSize = Regex.Match(line, @"\bDownload size:\s+([0-9.]+) MiB(?!/s)");
        if(announcedSize.Success&&double.TryParse(announcedSize.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var totalMiB)&&double.IsFinite(totalMiB)&&totalMiB>0&&totalMiB<long.MaxValue/1048576.0){total=(long)(totalMiB*1048576);changed=true;}
        var bytes = Regex.Match(line, @"\bDownloaded:\s+([0-9.]+) MiB(?!/s)");
        if (bytes.Success && double.TryParse(bytes.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var downloadedMiB) && double.IsFinite(downloadedMiB) && downloadedMiB >= 0 && downloadedMiB < long.MaxValue / 1048576.0) { downloaded = Math.Max(downloaded,(long)(downloadedMiB * 1048576)); changed = true; }
        var rate = Regex.Match(line, @"Download\s*- ([0-9.]+) MiB/s");
        if (rate.Success && double.TryParse(rate.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { speed = value * 1024 * 1024; changed = true; }
        return changed;
    }
    internal static async Task RunSupervisorAsync()
    {
        var initial = await Console.In.ReadLineAsync() ?? throw new StoreOperationException("supervisor_request_missing");
        if (initial.Length > 65536) throw new StoreOperationException("supervisor_request_invalid");
        var request = JsonSerializer.Deserialize<SupervisedToolRequest>(initial, StoreLibraryService.JsonOptions) ?? throw new StoreOperationException("supervisor_request_invalid");
        var checkpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () => { var line = await Console.In.ReadLineAsync(); if (line == "checkpoint" || line == null) checkpoint.TrySetResult(); });
        var outputGate = new SemaphoreSlim(1, 1);
        async Task Write(object item) { await outputGate.WaitAsync(); try { Console.WriteLine(JsonSerializer.Serialize(item, StoreLibraryService.JsonOptions)); } finally { outputGate.Release(); } }
        try
        {
            var result = await PrivateConsoleSupervisor.RunAsync(request.Executable, request.Arguments, request.Environment, request.WorkingDirectory, line => Write(new { kind = "stdout", line }), line => Write(new { kind = "stderr", line }), checkpoint.Task, CancellationToken.None, () => Write(new { kind = "root-exit" }));
            await Write(new { kind = "exit", result.ExitCode, result.StopRequested, result.Drained, result.Forced });
        }
        catch (Exception e) { await Write(new { kind = "error", errorCode = "supervisor_failed", nativeError = e is System.ComponentModel.Win32Exception win ? win.NativeErrorCode : 0, failureKind = e is InvalidOperationException or System.ComponentModel.Win32Exception ? e.Message : e.GetType().Name }); Environment.ExitCode = 3; }
    }
}
