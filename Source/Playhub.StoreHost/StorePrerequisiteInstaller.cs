// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Playhub.StoreLibrary;

internal sealed record StorePrerequisiteStep(string Path, IReadOnlyList<string> Arguments, IReadOnlyList<int> ValidReturns);

// Store adapters supply declarations and checksums from their own retained
// manifests. File names and installation receipts never authorize execution.
internal static class StorePrerequisiteInstaller
{
    private sealed record CompletedStep(string Path, string Hash, IReadOnlyList<string> Arguments);
    private sealed record Receipt(int Version, Guid Owner, string Authorization, IReadOnlyList<CompletedStep> Steps);

    internal static void Apply(string root, Guid ownerJob, string authorization,
        IReadOnlyList<StoreOwnedInstallFile> files, IReadOnlyList<StorePrerequisiteStep> steps,
        string journal, CancellationToken token, Func<string, IReadOnlyList<string>, CancellationToken, int>? run = null)
    {
        root = new StoreInstallFileSystem().CanonicalDestination(root);
        using var rootPins = StoreOwnedInstallFiles.PinRoot(root);
        using var owner = StoreOwnedInstallFiles.OpenReadFile(Path.Combine(root, ".playhub-install-owner"));
        using var reader = new StreamReader(owner, leaveOpen: true);
        if (ownerJob == Guid.Empty || owner.Length > 64 || reader.ReadToEnd() != ownerJob.ToString("D"))
            throw new StoreOperationException("prerequisite_ownership_lost");
        if (steps.Count > 256 || string.IsNullOrWhiteSpace(authorization)) throw new StoreOperationException("prerequisite_metadata_invalid");
        var map = new Dictionary<string, StoreOwnedInstallFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var path = StoreOwnedInstallFiles.PathFor(root, file.RelativePath);
            if (!map.TryAdd(path, file)) throw new StoreOperationException("prerequisite_metadata_invalid");
        }
        Receipt? previous = null;
        if (File.Exists(journal))
        {
            using var old = StoreOwnedInstallFiles.OpenReadFile(journal);
            if (old.Length is > 0 and <= 1048576)
                try { previous = JsonSerializer.Deserialize<Receipt>(old); } catch (JsonException) { }
        }
        var complete = new List<CompletedStep>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Validate the entire declaration before running its first executable.
        foreach (var step in steps)
        {
            var path = StoreOwnedInstallFiles.PathFor(root, step.Path);
            if (!step.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !seen.Add(path) ||
                !map.ContainsKey(path) || step.Arguments.Count > 64 || step.Arguments.Any(a => a == null || a.IndexOfAny(['\0', '\r', '\n']) >= 0) ||
                step.Arguments.Sum(a => (long)a.Length + 3) > 8192 || step.ValidReturns.Count is < 1 or > 16)
                throw new StoreOperationException("prerequisite_metadata_invalid");
        }
        foreach (var step in steps)
        {
            token.ThrowIfCancellationRequested();
            var path = StoreOwnedInstallFiles.PathFor(root, step.Path);
            using var parents = StoreOwnedInstallFiles.PinRoot(Path.GetDirectoryName(path)!);
            using var retained = StoreOwnedInstallFiles.OpenReadFile(path);
            StoreOwnedInstallFiles.VerifyContents(retained, map[path], token);
            retained.Position = 0;
            var hash = Convert.ToHexString(SHA256.HashData(retained));
            var done = previous is { Version: 1 } && previous.Owner == ownerJob && previous.Authorization == authorization &&
                previous.Steps != null && previous.Steps.Any(s => s.Path == step.Path && s.Hash == hash &&
                    s.Arguments != null && s.Arguments.SequenceEqual(step.Arguments, StringComparer.Ordinal));
            if (!done)
            {
                var exit = (run ?? Run)(path, step.Arguments, token);
                if (!step.ValidReturns.Contains(exit))
                {
                    var failure = new StoreOperationException("prerequisite_installer_failed");
                    failure.Data["ExitCode"] = exit; throw failure;
                }
            }
            complete.Add(new(step.Path, hash, step.Arguments));
        }
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        var temporary = journal + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(output, new Receipt(1, ownerJob, authorization, complete)); output.Flush(true); }
            File.Move(temporary, journal, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static int Run(string path, IReadOnlyList<string> arguments, CancellationToken token)
    {
        // MSI/VC/DirectX installations share machine state across all stores.
        using var gate = new Mutex(false, "Local\\Playhub-StorePrerequisites");
        var acquired = false;
        try
        {
            while (!acquired)
            {
                token.ThrowIfCancellationRequested();
                try { acquired = gate.WaitOne(200); } catch (AbandonedMutexException) { acquired = true; }
            }
            var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path)! };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            Process? process;
            try { process = Process.Start(info); }
            catch (Win32Exception error) when (error.NativeErrorCode == 740)
            {
                // Let Windows show the genuine UAC prompt for this same verified
                // installer. No shell command, changed arguments or helper script.
                info.UseShellExecute = true; info.Verb = "runas"; process = Process.Start(info);
            }
            using (process)
            {
                if (process == null) throw new StoreOperationException("prerequisite_installer_failed");
                // Keep both the store lease and file pins until setup exits,
                // including when cancellation arrives while it is running.
                process.WaitForExit(); token.ThrowIfCancellationRequested(); return process.ExitCode;
            }
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }
}
