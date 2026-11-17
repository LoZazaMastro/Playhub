// SPDX-License-Identifier: MIT. Synthetic persisted queue and owned TEMP roots only.
using System.Security.Cryptography;
using System.Text.Json;
using Playhub.StoreLibrary;

static class FinalizationRetryChecks
{
    internal static async Task<int> Run(string parent)
    {
        int checks = 0;
        void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
        foreach (string mode in new[] { "complete-setup", "incomplete", "network-error", "undrained", "account-changed" })
        {
            string path = Path.Combine(parent, "retry-" + mode); Directory.CreateDirectory(path);
            var id = Guid.NewGuid(); var key = new StoreGameKey(StoreKind.Amazon, "Fixture"); byte[] payload = [1,2,3,4,5,6,7,8];
            File.WriteAllText(Path.Combine(path, ".playhub-install-owner"), id.ToString("D"));
            var manifest = new StorePartialManifest(new string('A', 64), [new("game.exe", 8, "SHA256", Convert.ToHexString(SHA256.HashData(payload)))], []);
            var job = new StoreDownloadJob(id, key, "Inert", StoreJobState.Interrupted, 2, 1, path,
                mode == "incomplete" ? 4 : 8, 8, null, DateTimeOffset.UtcNow, false, false, true,
                ErrorCode: mode == "network-error" ? "worker_failed" : "amazon_setup_unsupported");
            var receipt = StoreOwnedPartialFiles.Capture(job, "inert-account")! with { Manifest = manifest, WorkerStarted = true, DrainConfirmed = mode != "undrained" };
            File.WriteAllBytes(Path.Combine(path, "game.exe"), payload);
            var provider = new Provider(manifest); var caps = provider.Capabilities;
            string disk = Path.Combine(parent, "retry-" + mode + ".json");
            File.WriteAllText(disk, JsonSerializer.Serialize(new { Version = 1, Revision = 5L,
                Accounts = new[] { new StoreAccount(StoreKind.Amazon, StoreConnectionState.Connected, mode == "account-changed" ? 2 : 1, "Inert", caps) },
                Games = new[] { new StoreLibraryGame(key, "Inert", 1, caps, false, false, null) }, Jobs = new[] { job },
                AccountIdentities = new Dictionary<StoreKind,string> { [StoreKind.Amazon] = "inert-account" },
                JobAccountIdentities = new Dictionary<Guid,string> { [id] = "inert-account" }, PartialDownloads = new[] { receipt } }));
            await using var queue = new StoreQueueEngine([provider], new StoreInstallFileSystem(), disk);
            await queue.InitializeAsync();
            await queue.ObserveSteamAsync(new(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, false), true);
            var snapshot = await queue.GetSnapshotAsync(); var current = snapshot.Jobs.Single();
            bool allowed = mode == "complete-setup";
            Check(current.CanResume == allowed && current.CanDownloadNow == allowed, "only fully downloaded, drained, current-account setup failure offers retry:" + mode);
            var response = await queue.ControlAsync(new(id, current.Revision, StoreJobAction.Resume, Guid.NewGuid()));
            Check(response.Accepted == allowed, "actual command applies same bounded finalization authority:" + mode);
            if (allowed)
            {
                for (int i = 0; i < 100 && provider.Starts == 0; i++) await Task.Delay(10);
                Check(provider.Starts == 1 && provider.Request?.JobId == id && provider.Request.InstallPath == path, "retry retains exact job and downloaded root, without claiming another directory");
                provider.Worker.Done.TrySetResult(new(false, false, false, "amazon_setup_installer_failed", DrainConfirmed: true));
                for (int i = 0; i < 100 && (await queue.GetSnapshotAsync()).Jobs.Single().State != StoreJobState.Interrupted; i++) await Task.Delay(10);
                Check(File.ReadAllBytes(Path.Combine(path, "game.exe")).SequenceEqual(payload), "retry and failed prerequisite preserve already downloaded files");
            }
            else Check(provider.Starts == 0, "refused retry never starts a provider:" + mode);
        }
        return checks;
    }
    sealed class Worker : IStoreTransfer
    {
        internal TaskCompletionSource<TransferCompletion> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<TransferCompletion> Completion => Done.Task;
        public Task RequestCheckpointAsync(CancellationToken token) { Done.TrySetResult(new(false, false, false, Stopped: true, DrainConfirmed: true)); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    sealed class Provider(StorePartialManifest manifest) : IStoreProvider, IStorePartialDownloadSource
    {
        internal int Starts; internal TransferRequest? Request; internal Worker Worker = new();
        public StoreKind Store => StoreKind.Amazon;
        public StoreCapabilities Capabilities => new(StoreInstallKind.Direct, true, true, false, false, true);
        public bool HasOperationLease => false;
        public Task<StorePartialManifest> ReadPartialManifestAsync(string id, CancellationToken token) => Task.FromResult(manifest);
        public Uri AuthorizationUri => throw new Exception("No auth");
        public Task<ProviderAccount?> GetAccountAsync(CancellationToken token) => Task.FromResult<ProviderAccount?>(new("inert-account", "Inert"));
        public Task<ProviderAccount> ConnectAsync(string code, CancellationToken token) => throw new Exception("No account");
        public Task DisconnectAsync(CancellationToken token) => throw new Exception("No account");
        public Task<IReadOnlyList<ProviderGame>> ListAsync(CancellationToken token) => throw new Exception("No network");
        public Task<ProviderInstallSize> GetInstallSizeAsync(string id, CancellationToken token) => throw new Exception("No network");
        public Task<ProviderLaunch?> ResolveLaunchAsync(string id, string path, CancellationToken token) => Task.FromResult<ProviderLaunch?>(null);
        public Task<StorePrivateLaunchPlan> PrepareLaunchAsync(string id, string path, CancellationToken token) => throw new Exception("No launch");
        public Task<IStoreTransfer> StartAsync(TransferRequest request, Action<TransferProgress> progress, CancellationToken token)
        { Request = request; Interlocked.Increment(ref Starts); return Task.FromResult<IStoreTransfer>(Worker); }
    }
}
