// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 LoZazaMastro.
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Playhub.StoreLibrary;

internal sealed record WindowsStoreManagedProgress(string Phase, double Percent, long? DownloadedBytes = null, long? TotalBytes = null, double? BytesPerSecond = null,bool FinalDeploymentConfirmed=false);
internal sealed record WindowsStoreManagedOutcome(int? ExitCode, string? HResult, bool ReportedProgress, string? ErrorCode = null);
internal interface IWindowsStoreManagedOutcomeSource
{
    WindowsStoreManagedOutcome? TakeOutcome(string productId);
}
internal interface IWindowsStoreManagedRuntime
{
    bool Available { get; }
    Task<WindowsXboxPackage?> FindPackageAsync(string family, CancellationToken token);
    Task<int?> RunInstallAsync(string productId, Func<WindowsStoreManagedProgress, Task> progress, CancellationToken observation);
}

internal sealed class WindowsStoreManagedRuntime : IWindowsStoreManagedRuntime, IWindowsStoreManagedOutcomeSource, IWindowsStoreInstallDestinationRuntime
{
    readonly StoreWindowsInstallControl installControl = new();
    public Task<IReadOnlyList<WindowsStoreInstallVolume>> GetInstallVolumesAsync(CancellationToken token) => installControl.GetVolumesAsync(token);
    public Task<int?> RunOnVolumeAsync(string productId, string family, string? volume, bool launch, Func<WindowsStoreManagedProgress, Task> progress, CancellationToken observation)
        => installControl.RunAsync(productId, family, volume, launch, progress, observation);
    public Task CancelInstallAsync(string productId, CancellationToken token) => installControl.CancelAsync(productId, token);
    readonly StoreWindowsStoreCli cli = new();
    readonly ConcurrentDictionary<string, WindowsStoreManagedOutcome> outcomes = new(StringComparer.Ordinal);
    public WindowsStoreManagedOutcome? TakeOutcome(string productId) => installControl.TakeOutcome(productId) ?? (outcomes.TryRemove(productId, out var value) ? value : null);
    internal sealed class Diagnostics
    {
        readonly object sync = new();
        string stdout = "", stderr = "";
        string? result;
        bool reportedProgress;
        internal void Add(string value, bool error, bool progress = false)
        {
            lock (sync)
            {
                string tail = (error ? stderr : stdout) + value;
                if (tail.Length > 4096) tail = tail[^4096..];
                if (error) stderr = tail; else stdout = tail;
                reportedProgress |= progress;
                // Only canonical failed HRESULTs with an explicit error label
                // survive. No output text, paths, account data or URLs persist.
                var matches = Regex.Matches(tail, @"\b(?:HRESULT|Error|Failed)[^\r\n]{0,120}?\b(0x[89A-Fa-f][0-9A-Fa-f]{7})\b", RegexOptions.IgnoreCase);
                if (matches.Count != 0) result = "0x" + matches[^1].Groups[1].Value[2..].ToUpperInvariant();
            }
        }
        internal WindowsStoreManagedOutcome Snapshot(int? exitCode) { lock (sync) return new(exitCode, result, reportedProgress); }
    }
    public bool Available => installControl.Available;
    public Task<WindowsXboxPackage?> FindPackageAsync(string family, CancellationToken token) => WindowsXboxPackageCatalog.FindAsync(family, token);
    internal static WindowsStoreManagedProgress? ParseProgress(string value)
    {
        value = Regex.Replace(value, "\u001b\\[[0-?]*[ -/]*[@-~]", "");
        var matches = Regex.Matches(value, @"\b(Starting|Downloading|Installing|Verifying)\s*:?\s*([0-9]{1,3}(?:[.,][0-9]+)?)\s*%", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return null;
        var last = matches[^1];
        if (!double.TryParse(last.Groups[2].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double percent) || percent is < 0 or > 100) return null;
        return new(last.Groups[1].Value.ToLowerInvariant(), percent);
    }
    public async Task<int?> RunInstallAsync(string productId, Func<WindowsStoreManagedProgress, Task> progress, CancellationToken observation)
    {
        outcomes.TryRemove(productId, out _);
        var diagnostics = new Diagnostics();
        int? exitCode = null;
        Process process;
        try { process = Process.Start(cli.CreateInstallStartInfo(productId)) ?? throw new StoreOperationException("windows_store_start_failed"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { throw new StoreOperationException("windows_store_start_failed"); }
        using (process)
        {
            process.StandardInput.Close();
            async Task Drain(StreamReader source, bool observe)
            {
                char[] buffer = new char[1024]; string tail = ""; WindowsStoreManagedProgress? previous = null;
                int count;
                while ((count = await source.ReadAsync(buffer.AsMemory(), observation)) != 0)
                {
                    string chunk = new(buffer, 0, count);
                    if (!observe) { diagnostics.Add(chunk, error: true); continue; }
                    tail += chunk;
                    if (tail.Length > 4096) tail = tail[^4096..];
                    var current = ParseProgress(tail);
                    diagnostics.Add(chunk, error: false, progress: current is not null);
                    if (current is not null && current != previous) { previous = current; await progress(current); }
                }
            }
            try
            {
                await Task.WhenAll(Drain(process.StandardOutput, true), Drain(process.StandardError, false), process.WaitForExitAsync(observation));
                exitCode = process.ExitCode;
                return exitCode;
            }
            catch (OperationCanceledException) when (observation.IsCancellationRequested)
            {
                // Observation ends with the host; Microsoft Store owns the
                // operation. Never Kill(), promise cancellation or replay it.
                return null;
            }
            finally { outcomes[productId] = diagnostics.Snapshot(exitCode); }
        }
    }
}
