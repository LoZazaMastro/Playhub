using GamingMode.Services;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
var policy = new DesktopRecoveryPolicy();
Check(!policy.ShouldRestore(true, false, false, false), "One transient Steam bootstrap gap does not restore");
Check(policy.ShouldRestore(true, false, false, false), "Steam exit with only a folder process restores desktop");
Check(!policy.ShouldRestore(true, false, false, false), "Recovery resets and does not spin");
Check(!policy.ShouldRestore(true, true, false, false), "Live Steam without shell remains Gaming Mode");
Check(!policy.ShouldRestore(true, false, false, false), "Steam returning resets the gap count");
Check(!policy.ShouldRestore(true, false, true, false), "Actual desktop shell prevents recovery");
Check(!policy.ShouldRestore(true, false, false, false), "Shell recovery resets previous failure");
Check(!policy.ShouldRestore(true, false, false, true), "No desktop relaunch during shutdown");
Check(!policy.ShouldRestore(true, false, false, false), "Shutdown resets previous absence");
Check(policy.ShouldRestore(true, false, false, false), "Persistent Steam exit recovered after two samples");
Check(!policy.ShouldRestore(false, true, false, false), "Desktop mode missing shell waits one sample");
Check(policy.ShouldRestore(false, true, false, false), "Desktop mode restores missing shell even with Steam open");
policy.Reset();
Check(!policy.ShouldRestore(false, false, false, false), "Explicit reset clears observations");
Check(policy.ShouldRestore(false, false, false, false), "Desktop shell recovered independent of plugin and config flags");

List<string> probes = new();
bool Observe(bool shellReady = false, bool gaming = true, bool steamRunning = false,
    bool lateShellReady = false, bool shuttingDown = false)
{
    probes.Clear();
    int shellChecks = 0;
    bool Probe(string name, bool result)
    {
        probes.Add(name);
        return result;
    }

    return policy.ShouldRestore(
        () => Probe("shell", shellChecks++ == 0 ? shellReady : lateShellReady),
        () => Probe("state", gaming),
        () => Probe("steam", steamRunning),
        () => Probe("shutdown", shuttingDown));
}

Check(!Observe(), "One absent sample is recorded on the watchdog path");
Check(!Observe(shellReady: true) && probes.SequenceEqual(new[] { "shell" }),
    "Healthy shell skips state, Steam and late checks");
Check(!Observe(), "Healthy shell resets the prior absence");
Check(!Observe(lateShellReady: true) && probes.SequenceEqual(new[] { "shell", "state", "steam", "shell", "shutdown" }),
    "Shell appearing during state or Steam checks vetoes recovery");
Check(!Observe(), "Late shell readiness resets the prior absence");
Check(!Observe(shuttingDown: true) && probes.Last() == "shutdown", "Late shutdown vetoes recovery");
Check(!Observe(), "Late shutdown resets the prior absence");
Check(!Observe(steamRunning: true), "Running Steam resets the missing Gaming shell count");
Check(!Observe() && Observe(), "Gaming recovery still requires two successive absences");
Check(!Observe(gaming: false, steamRunning: true) && probes.SequenceEqual(new[] { "shell", "state", "shell", "shutdown" }),
    "Missing Desktop shell skips Steam and retains the late checks");
Check(Observe(gaming: false, steamRunning: true), "Desktop recovery still ignores Steam after two observations");
Console.WriteLine($"Desktop recovery: {passed} checks passed; no process, shell, registry or UI mutations.");
