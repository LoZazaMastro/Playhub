// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
// Original Amazon adapter over Playhub's retained-handle, manifest-only remover.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playhub.StoreLibrary;

public sealed partial class StoreAmazonProvider : IStoreInstalledGameProvider
{
    private sealed class AmazonUninstallState(StoreOwnedUninstallPlan plan, JsonArray records,
        int installedIndex, IReadOnlyList<IDisposable> pins) : IDisposable
    {
        internal StoreOwnedUninstallPlan Plan { get; } = plan;
        internal JsonArray Records { get; } = records;
        internal int InstalledIndex { get; } = installedIndex;
        public void Dispose() { foreach (var pin in pins.Reverse()) pin.Dispose(); }
    }

    public Task<bool> CanUninstallAsync(string id, string installPath, Guid ownerJob, CancellationToken token)
    {
        StoreCliProvider.ValidateId(id); token.ThrowIfCancellationRequested();
        if (!gate.TryWait()) return Task.FromResult(false);
        try
        {
            if (ownerJob == Guid.Empty) return Task.FromResult(false);
            var path = new StoreInstallFileSystem().CanonicalDestination(installPath);
            using var owner = PinDestination(path, ownerJob);
            // This is only capability/status. Keep account, signed fuel, exact
            // executable identity/length and owner checks; no bulk payload reads.
            return Task.FromResult(InstalledPath(id) is {} installed && StringComparer.OrdinalIgnoreCase.Equals(installed,path)
                && File.Exists(AmazonManifest.Contained(native,"manifests/"+id+".raw")));
        }
        catch (Exception error) when (LaunchReadFailure(error)) { return Task.FromResult(false); }
        finally { gate.Release(); }
    }

    private async Task WaitForRemovalGateAsync(CancellationToken token)
    {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(token);budget.CancelAfter(TimeSpan.FromSeconds(20));
        try{await gate.WaitAsync(budget.Token);}
        catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new StoreOperationException("provider_busy");}
    }
    public async Task<StoreOwnedUninstallPlan> PrepareUninstallAsync(string id, string installPath, Guid ownerJob, CancellationToken token)
    {
        StoreCliProvider.ValidateId(id); token.ThrowIfCancellationRequested();
        await WaitForRemovalGateAsync(token);
        try { using var state = ReadAmazonUninstall(id, installPath, ownerJob, token); return state.Plan; }
        finally { gate.Release(); }
    }

    private AmazonUninstallState ReadAmazonUninstall(string id, string installPath, Guid ownerJob, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); StoreCliProvider.ValidateId(id);
        if (ownerJob == Guid.Empty) throw new StoreOperationException("installation_ownership_lost");
        var path = new StoreInstallFileSystem().CanonicalDestination(installPath);
        var pins = new List<IDisposable>();
        try
        {
            pins.Add(PinDestination(path, ownerJob));
            FileStream Pin(string file, long maximum) { var pin = OpenLaunchFile(file, maximum); pins.Add(pin); return pin; }
            var installed = Pin(AmazonManifest.Contained(native, "installed.json"), 4 * 1024 * 1024);
            var raw = Pin(AmazonManifest.Contained(native, "manifests/" + id + ".raw"), AmazonManifest.MaximumBytes);
            var libraryPath=AmazonManifest.Contained(native,"library.json");
            var library = File.Exists(libraryPath)?Pin(libraryPath,16*1024*1024):null;
            StoreOwnedInstallFiles.RefuseRunning(path);
            string? entitlement=null;
            if(library is not null){using var catalog=ParseLaunchJson(ReadLaunchFile(library));var owned=catalog.RootElement.EnumerateArray().Where(g=>g.GetProperty("product").GetProperty("id").GetString()==id).ToArray();if(owned.Length==1)entitlement=owned[0].GetProperty("id").GetString();}
            var records = JsonNode.Parse(ReadLaunchFile(installed))!.AsArray();
            var matches = records.Select((record, index) => (record, index))
                .Where(item => item.record?["id"]?.GetValue<string>() is { } value && (value == id || value == entitlement)
                    &&item.record?["path"]?.GetValue<string>() is {} location&&Path.IsPathFullyQualified(location)&&StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),path)).ToArray();
            if (matches.Length != 1) throw new StoreOperationException("installed_metadata_invalid");
            foreach (var item in records.Select((record, index) => (record, index)).Where(item => item.index != matches[0].index))
            {
                var other = item.record?["path"]?.GetValue<string>();
                if (other == null || !Path.IsPathFullyQualified(other)) throw new StoreOperationException("installed_metadata_invalid");
                other = Path.TrimEndingDirectorySeparator(Path.GetFullPath(other));
                if (StringComparer.OrdinalIgnoreCase.Equals(other, path) ||
                    other.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new StoreOperationException("shared_installation_refused");
            }
            var manifest = parseManifest(ReadLaunchFile(raw));
            var files=StoreOwnedInstallFiles.BindRemovalFiles(path,manifest.Files.Select(f=>new StoreOwnedInstallFile(f.RelativePath,f.Size,null,null)),token);
            string executable=StoreOwnedInstallFiles.PathFor(path,files.FirstOrDefault(f=>f.RelativePath.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))?.RelativePath??files.First().RelativePath);
            var ordered = files.OrderBy(file => StringComparer.OrdinalIgnoreCase.Equals(
                    StoreOwnedInstallFiles.PathFor(path, file.RelativePath), executable) ? 1 : 0)
                .ThenBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
            string Hash(FileStream file) => Convert.ToHexString(SHA256.HashData(ReadLaunchFile(file)));
            // Other products are rechecked for path overlap above, but their
            // harmless metadata changes do not alter this installation's nonce.
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                id, path, ownerJob, executable, raw = Hash(raw), installed = matches[0].record!.ToJsonString(),
                entitlement = matches[0].record!["id"]!.GetValue<string>()==id?null:entitlement, files = ordered }))));
            var plan = new StoreOwnedUninstallPlan(new(Store, id), path, executable, ownerJob, fingerprint, Array.AsReadOnly(ordered));
            StoreOwnedInstallFiles.ValidatePlan(plan); token.ThrowIfCancellationRequested();
            return new(plan, records, matches[0].index, pins);
        }
        catch { foreach (var pin in pins.AsEnumerable().Reverse()) pin.Dispose(); throw; }
    }

    public async Task UninstallAsync(StoreOwnedUninstallPlan plan, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (plan.Key.Store != Store) throw new StoreOperationException("store_mismatch");
        await WaitForRemovalGateAsync(token);
        try
        {
            var installedFile = AmazonManifest.Contained(native, "installed.json");
            var state = ReadAmazonUninstall(plan.Key.Id, plan.InstallPath, plan.OwnerJob, token);
            JsonArray remaining;
            try
            {
                var current = state.Plan;
                if (current.ManifestHash != plan.ManifestHash || current.GameExecutable != plan.GameExecutable ||
                    !current.Files.SequenceEqual(plan.Files)) throw new StoreOperationException("installation_changed");
                remaining = state.Records; remaining.RemoveAt(state.InstalledIndex);
                token.ThrowIfCancellationRequested();
                // Cancellation is honored through the complete verification phase.
                // Once deletion commits, finish registration even if the UI closes.
                StoreOwnedInstallFiles.Delete(current, CancellationToken.None);
                if (current.Files.Any(file => File.Exists(StoreOwnedInstallFiles.PathFor(current.InstallPath, file.RelativePath))))
                    throw new StoreOperationException("uninstall_incomplete");
            }
            finally { state.Dispose(); }
            var temporary = installedFile + ".uninstall-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, remaining); stream.Flush(true); }
                File.Move(temporary, installedFile, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            // Only remove this operation's marker. Saves and unknown files remain.
            var path = new StoreInstallFileSystem().CanonicalDestination(plan.InstallPath);
            var marker = Path.Combine(path, ".playhub-install-owner");
            if (File.ReadAllText(marker) != plan.OwnerJob.ToString("D")) throw new StoreOperationException("installation_ownership_lost");
            File.Delete(marker);
            try { Directory.Delete(path, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return;
        }
        finally { gate.Release(); }
    }
}
