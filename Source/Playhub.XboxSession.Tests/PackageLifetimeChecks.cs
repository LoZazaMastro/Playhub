using System.Diagnostics;
using System.Text.Json;

internal static class PackageLifetimeChecks
{
    internal static void Run(string root, Action<bool, string> check, Action<Action, string> denied)
    {
        const string package = "Fixture.Cuphead_1.0.0.0_x64__abcd";
        const string boot = "fixture-boot";
        var owner = new LeaseProcess(100, 1000);
        var game = new LeaseProcess(200, 2000);
        var oldOwner = new LeaseProcess(300, 3000);
        var oldGame = new LeaseProcess(400, 4000);
        var states = new Dictionary<LeaseProcess, LeaseProcessState>
        {
            [owner] = LeaseProcessState.Running, [game] = LeaseProcessState.Running,
            [oldOwner] = LeaseProcessState.Gone, [oldGame] = LeaseProcessState.Gone
        };
        var folder = Directory.CreateDirectory(Path.Combine(root, "lease"));
        string journal = Path.Combine(folder.FullName, "cuphead.json");
        PackageLeaseRecord Read() => JsonSerializer.Deserialize<PackageLeaseRecord>(File.ReadAllText(journal))!;
        void Seed(PackageLeasePhase phase, string currentBoot = boot) =>
            File.WriteAllText(journal, JsonSerializer.Serialize(new PackageLeaseRecord(1, package, currentBoot, oldOwner, oldGame, phase)));

        var backend = new Fixture(() => Read().Phase, package);
        var messages = new List<string>();
        PackageLifetimeLease Acquire() => PackageLifetimeLease.Acquire(folder.FullName, package, boot, owner, game,
            backend, process => states[process], messages.Add);
        void Refuse(string name) => denied(() => { using var lease = Acquire(); }, name);

        try
        {
            using (var lease = Acquire())
            {
                check(backend.References == 3 && Read().Phase == PackageLeasePhase.Acquired
                    && backend.Calls.SequenceEqual(["enable:Acquiring"]),
                    "Cuphead records intent before acquiring one ref and commits only confirmed success");
                Refuse("A live Cuphead lease holds an exclusive lock through the game");
                states[game] = LeaseProcessState.Gone;
            }
            check(backend.References == 2 && !File.Exists(journal)
                && backend.Calls.SequenceEqual(["enable:Acquiring", "disable:Releasing"]),
                "Exact exit releases once after durable intent and preserves the debugger's existing refs");

            backend = new Fixture(() => Read().Phase, package) { References = 3 };
            Seed(PackageLeasePhase.Acquired);
            states[game] = LeaseProcessState.Running;
            using (var lease = Acquire())
            {
                check(backend.References == 3 && backend.Calls.SequenceEqual(["disable:Releasing", "enable:Acquiring"])
                    && Read().Owner == owner && Read().Game == game,
                    "Recovery balances exactly one confirmed stale ref before the new acquisition");
                states[game] = LeaseProcessState.Gone;
            }
            check(backend.References == 2 && !File.Exists(journal), "Recovery and current trial retain the other two references");

            backend = new Fixture(() => Read().Phase, package);
            states[game] = LeaseProcessState.Running;
            Seed(PackageLeasePhase.Acquired);
            states[oldGame] = LeaseProcessState.Unknown;
            Refuse("Recovery refuses an inaccessible old target identity");
            states[oldGame] = LeaseProcessState.Gone;
            Seed(PackageLeasePhase.Acquired, "other-boot");
            Refuse("Recovery never decrements a reference recorded across a reboot");
            Seed(PackageLeasePhase.Acquiring);
            Refuse("Recovery never balances an ambiguous acquisition");
            Seed(PackageLeasePhase.Releasing);
            Refuse("Recovery never retries an ambiguous decrement");
            check(backend.Calls.Count == 0, "All unverified records leave external debugger references untouched");

            File.Delete(journal);
            backend = new Fixture(() => Read().Phase, package) { EnableResult = unchecked((int)0x80004005) };
            Refuse("A failed Windows enable skips overlay and keeps a journaled uncertain result");
            check(Read().Phase == PackageLeasePhase.Acquiring && backend.Calls.SequenceEqual(["enable:Acquiring"]),
                "A failed enable never causes an unowned disable");

            File.Delete(journal);
            backend = new Fixture(() => Read().Phase, package) { DisableResult = unchecked((int)0x80004005) };
            using (var lease = Acquire()) states[game] = LeaseProcessState.Gone;
            check(Read().Phase == PackageLeasePhase.Releasing && backend.Calls.SequenceEqual(["enable:Acquiring", "disable:Releasing"]),
                "Failed disable leaves the ambiguous transition durably recorded");
            states[game] = LeaseProcessState.Running;
            Refuse("A later trial cannot repeat a failed disable and decrement someone else's ref");
            check(backend.Calls.Count == 2, "A failed decrement is attempted only once");

            File.Delete(journal);
            backend = new Fixture(() => Read().Phase, package);
            states[game] = LeaseProcessState.Running;
            using (var lease = Acquire()) { }
            check(Read().Phase == PackageLeasePhase.Acquired && backend.Calls.SequenceEqual(["enable:Acquiring"]),
                "Disposal while the exact game is still alive preserves its confirmed ref");

            using var own = Process.GetCurrentProcess();
            check(WindowsPackageLifetime.Observe(new LeaseProcess((uint)own.Id, own.StartTime.ToFileTimeUtc())) == LeaseProcessState.Running
                && WindowsPackageLifetime.Observe(new LeaseProcess((uint)own.Id, 1)) == LeaseProcessState.Gone
                && WindowsPackageLifetime.BootMarker() == WindowsPackageLifetime.BootMarker(),
                "Actual Windows birth and boot evidence is stable without modifying package state");
        }
        finally
        {
            File.Delete(journal);
            folder.Delete();
        }
    }

    private sealed class Fixture(Func<PackageLeasePhase> phase, string expected) : IPackageDebugReference
    {
        internal int References = 2;
        internal int EnableResult;
        internal int DisableResult;
        internal List<string> Calls { get; } = [];
        public int Enable(string package)
        {
            if (package != expected) throw new InvalidOperationException("Wrong package");
            Calls.Add("enable:" + phase());
            if (EnableResult == 0) References++;
            return EnableResult;
        }
        public int Disable(string package)
        {
            if (package != expected) throw new InvalidOperationException("Wrong package");
            Calls.Add("disable:" + phase());
            if (DisableResult == 0) References--;
            return DisableResult;
        }
    }
}
