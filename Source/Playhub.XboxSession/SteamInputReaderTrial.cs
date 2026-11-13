// Playhub Xbox Session. Distributed under GPL-3.0-only.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using WSGM.PackagedLaunch;

internal static class SteamInputReaderTrial
{
    private const string ReaderName = "PlayhubSteamInputReader.exe";

    private static string? ReaderDigest => typeof(SteamInputReaderTrial).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .SingleOrDefault(attribute => attribute.Key == "PlayhubSteamInputReaderSha256")?.Value;

    internal static bool Configured => ReaderDigest is { Length: 64 } expected && expected.All(Uri.IsHexDigit);

    internal static bool TryRun(TrustedSteamFiles steam, ulong gameId, nint window, uint thread, uint owner,
        Func<bool> ownsInput, CancellationToken cancellation, SteamRawInputRelayTrial? relay, TritonSource? source,
        IRetainedSteamGameTarget target, long focusEpoch, Action queueExitRestore,
        out SteamRawInputRelayTrial.ReaderOutcome outcome, Func<bool>? retainedSessionCurrent = null, Func<RelayExit?>? readPause = null,
        Func<string?>? readRetainedFailure = null, SteamTrackedWrapperChain? readerBootstrap = null,
        Func<RelayExit?>? readCapturedPause = null)
    {
        outcome = default;
        string? expected = ReaderDigest;
        if (expected?.Length != 64 || !expected.All(Uri.IsHexDigit)) return false;

        try
        {
            SteamControllerSourceLease? controller = null;
            string renderer = steam.ComponentPaths.Last();
            string inventory = Path.Combine(Path.GetDirectoryName(renderer)!, "config", "virtualgamepadinfo.txt");
            string? firstSessionFailure = null;
            SteamTrackedWrapperChain.ReaderProof? bootstrapReader = null;
            SteamRawInputRelayTrial.SessionBinding? bootstrapBinding = null;
            bool SessionFailed(string reason)
            {
                firstSessionFailure ??= reason;
                return false;
            }
            string RetainedFailure()
            {
                try { return readRetainedFailure?.Invoke() ?? "retained-delegate-false"; }
                catch { return "retained-delegate-false; diagnostic-unavailable"; }
            }
            bool SessionCurrent()
            {
                if (cancellation.IsCancellationRequested) return SessionFailed("reader-cancelled");
                if (!(retainedSessionCurrent?.Invoke() ?? ownsInput())) return SessionFailed(RetainedFailure());
                if (readerBootstrap is not null && !readerBootstrap.Current) return SessionFailed("tracked-wrapper-chain-not-current");
                if (bootstrapReader is not null && (bootstrapBinding is not { } exact || !bootstrapReader.Current(exact)))
                    return SessionFailed("tracked-wrapper-reader-not-current");
                if (relay is null && controller is not null && !controller.Current(SteamInputEvidence.Read(inventory)))
                    return SessionFailed("reader-source-lease: " + controller.FailureText);
                return true;
            }
            bool Current() => SessionCurrent() && ownsInput();
            if (!Current()) return true;
            if (SteamControllerSourceProbe.ReadInventory(inventory) is not [var slot] ||
                relay is not null && new TritonSource(slot.Index, slot.Product, slot.Handle) != source)
            {
                PackagedLaunchLog.Info("Native Steam input reader skipped: single exact Triton unavailable.");
                return true;
            }
            controller = new SteamControllerSourceLease(new TritonSource(slot.Index, slot.Product, slot.Handle));
            if (!Current()) return true;

            string path = Path.Combine(AppContext.BaseDirectory, ReaderName);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                PackagedLaunchLog.Info("Native Steam input reader skipped: executable digest mismatch.");
                return true;
            }

            using var self = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(file.Name)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            string Decimal(ulong value) => value.ToString(CultureInfo.InvariantCulture);
            string[] arguments =
            [
                relay is null ? "--probe" : "--relay-session", Decimal(gameId), Decimal((uint)slot.Index),
                slot.Product.ToString("X4", CultureInfo.InvariantCulture),
                slot.Handle.ToString("X", CultureInfo.InvariantCulture), renderer,
                Decimal((uint)self.Id), Decimal((ulong)self.StartTime.ToFileTimeUtc()),
                Decimal((nuint)window), Decimal(thread), Decimal(owner)
            ];
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            if (!Current()) return true;

            using var child = Process.Start(start) ?? throw new InvalidOperationException("Native input reader did not start.");
            var stderr = child.StandardError.ReadToEndAsync();
            try
            {
                if (relay is not null)
                {
                    var binding = new SteamRawInputRelayTrial.SessionBinding(gameId, self.Id, self.StartTime.ToFileTimeUtc(),
                        target.Pid, target.Creation, new TritonSource(slot.Index, slot.Product, slot.Handle), steam, window, thread, owner);
                    using var proof = readerBootstrap?.BindReader(child, file, expected, binding);
                    if (readerBootstrap is not null && proof is null)
                    {
                        PackagedLaunchLog.Info("Steam reader bootstrap refused: exact retained wrapper/sidecar/reader chain unavailable.");
                        return true;
                    }
                    bootstrapBinding = binding; bootstrapReader = proof;
                    outcome = relay.Run(child, steam, inventory, binding, focusEpoch, Current, cancellation, SessionCurrent, readPause, proof, readCapturedPause);
                    if (outcome.EndReason == RelayExit.SessionEnded)
                    {
                        // Only facts captured by the original failing predicates;
                        // no process, inventory, foreground or token re-query.
                        try { PackagedLaunchLog.Info($"Steam input retained-session first failure: reader={outcome.ReaderPid}; birth={outcome.ReaderBirth}; epoch={focusEpoch}; cause={firstSessionFailure ?? "not-recorded"}."); }
                        catch { }
                    }
                    return true;
                }
                var stdout = child.StandardOutput.ReadToEndAsync();
                bool tracked = false;
                var deadline = Stopwatch.StartNew();
                while (!child.WaitForExit(40))
                {
                    if (!Current() || deadline.ElapsedMilliseconds >= 7300
                        || SteamControllerSourceProbe.ReadInventory(inventory) is not [var current] || current != slot)
                        return true;
                    if (!tracked)
                    {
                        try { tracked = TrialSteamTracking.IsTracked(steam.TrackingLog, child, gameId); }
                        catch (Exception error) when (error is InvalidOperationException or IOException
                            or System.ComponentModel.Win32Exception) { }
                    }
                }

                if (!stdout.Wait(250) || !stderr.Wait(250)) return true;
                string sample = stdout.Result.Trim();
                if (sample.Length is > 0 and <= 2048 && Current() && tracked)
                {
                    using var json = JsonDocument.Parse(sample);
                    PackagedLaunchLog.Info($"Native Steam input reader: tracked=True; exit={child.ExitCode}; {json.RootElement.GetRawText()}");
                }
                else
                {
                    PackagedLaunchLog.Info($"Native Steam input reader: tracked={tracked}; exit={child.ExitCode}; sample-accepted=False.");
                }
            }
            finally
            {
                // The reader has retired forwarding and neutralized its own session output.
                // An explicitly paused output remains connected until game teardown.
                // Queue the game's terminal layout reset before reaping its reader.
                try { queueExitRestore(); }
                finally
                {
                    long reapAt = Stopwatch.GetTimestamp();
                    if (!child.HasExited)
                    {
                        try { child.Kill(); }
                        catch (InvalidOperationException) when (child.HasExited) { }
                        child.WaitForExit(750);
                    }
                    // A fresh forwarding lease is forbidden while the exact
                    // previous retained reader process remains alive/unknown.
                    outcome = outcome with { ReaderStopped = child.HasExited };
                    try { PackagedLaunchLog.Info($"Steam input reader final outcome: reader={outcome.ReaderPid}; epoch={focusEpoch}; reason={outcome.EndReason}; can-resume={outcome.CanResume}; explicit-scope={outcome.ExplicitScopeEnded}; parent-scope={outcome.ParentScopeRevoked}; stopped={outcome.ReaderStopped}; neutral-held={outcome.NeutralHeld}; terminal-cleanup={outcome.CleanupConfirmed}."); } catch { }
                    if (target.HasExited)
                        PackagedLaunchLog.Info($"Steam input reader teardown: reap-ms={Stopwatch.GetElapsedTime(reapAt).TotalMilliseconds:F0}; exited={child.HasExited}.");
                }
            }
        }
        catch (Exception error)
        {
            PackagedLaunchLog.Info($"Native Steam input reader stopped: {error.GetType().Name}.");
        }
        return true;
    }
}
