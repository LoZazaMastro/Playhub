using PlayhubSetup;
using Playhub.Shared;

int checks = 0;
void Check(bool condition, string name)
{ if (!condition) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); checks++; }
string root = Path.Combine(Path.GetTempPath(), "Playhub-Agent-Update-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string package = Path.Combine(root, "package.exe"), installed = Path.Combine(root, "installed.exe"), script = Path.Combine(root, "install.ps1");
string config = Path.Combine(root, "config.json"), state = Path.Combine(root, "state.json"), disabled = Path.Combine(root, "disabled-by-user");
byte[] configBytes = "{\"defaultMode\":\"Gaming\",\"userSetting\":17}"u8.ToArray();
byte[] stateBytes = "{\"currentMode\":\"Desktop\"}"u8.ToArray();
File.WriteAllBytes(config, configBytes); File.WriteAllBytes(state, stateBytes);
File.WriteAllText(package, "new payload"); File.WriteAllText(script, "fixture only; never executed");
void Reset() => File.WriteAllText(installed, "old payload");
void Fails(GamingModeUpdateFailure expected, Func<IGamingModeInstallerProcess?> start, string name)
{
    try { GamingModeAgentUpdate.Ensure(package, installed, script, start, 17); throw new InvalidOperationException("Missing failure: " + name); }
    catch (GamingModeAgentUpdateException error) { Check(error.Failure == expected, name); }
}
try
{
    Reset(); int starts = 0;
    var good = new FakeProcess(true, 0, () => File.Copy(package, installed, true));
    GamingModeAgentUpdate.Ensure(package, installed, script, () => { starts++; return good; }, 17);
    Check(starts == 1 && good.WaitBudget == 17 && good.ExitReads == 1 && good.Disposed && !good.Aborted,
        "Successful update waits for the owned installer, checks exit status and disposes its handle");
    Check(GamingModeInstallationPolicy.HasSameAgentPayload(package, installed), "Successful update has exact payload bytes after the process exits");
    GamingModeAgentUpdate.Ensure(package, installed, script, () => throw new InvalidOperationException("Identical bytes must skip"));
    Check(starts == 1, "Identical bytes skip the installer regardless of timestamps or version");
    Reset(); var timeout = new FakeProcess(false, 0, null);
    Fails(GamingModeUpdateFailure.TimedOut, () => timeout, "An installer timeout is a setup failure");
    Check(timeout.Aborted && timeout.ExitReads == 0 && timeout.Disposed,
        "Timeout stops only the owned script and never reads a running process ExitCode");
    Reset(); var failed = new FakeProcess(true, 5, () => File.Copy(package, installed, true));
    Fails(GamingModeUpdateFailure.InstallerFailed, () => failed, "Nonzero exit fails even when the file happened to be copied");
    Check(failed.Disposed && !failed.Aborted, "An already-exited failed installer needs no termination");
    Reset(); var mismatch = new FakeProcess(true, 0, null);
    Fails(GamingModeUpdateFailure.PayloadMismatch, () => mismatch, "ExitCode zero cannot conceal an unchanged old installed payload");
    Check(mismatch.Disposed && mismatch.ExitReads == 1, "Readback failure releases the completed installer process");
    Reset(); Fails(GamingModeUpdateFailure.StartFailed, () => null, "A null Process.Start result fails setup instead of reporting success");
    Fails(GamingModeUpdateFailure.StartFailed, () => throw new IOException("Fixture start failure"), "Installer launch errors surface through the dedicated setup failure");
    File.Delete(script);
    Fails(GamingModeUpdateFailure.MissingPayload, () => throw new InvalidOperationException("Must not start"), "Missing packaged install script fails an enabled agent update");
    File.WriteAllText(script, "fixture only"); File.Delete(package);
    Fails(GamingModeUpdateFailure.MissingPayload, () => throw new InvalidOperationException("Must not start"), "Missing bundled agent fails before running a script");
    File.WriteAllText(package, "new payload"); Reset();
    using (File.Open(installed, FileMode.Open, FileAccess.Read, FileShare.None))
        Fails(GamingModeUpdateFailure.PayloadMismatch, () => new FakeProcess(true, 0, null), "Unreadable installed content never passes post-install verification");
    Check(GamingModeInstallationPolicy.ShouldMaintainInstallation(installed, disabled), "An installed enabled component remains eligible for maintenance");
    GamingModeInstallationPolicy.Disable(disabled);
    Check(!GamingModeInstallationPolicy.ShouldMaintainInstallation(installed, disabled), "The existing explicit opt-out still skips automatic maintenance");
    File.Delete(installed);
    Check(!GamingModeInstallationPolicy.ShouldMaintainInstallation(installed, Path.Combine(root, "no-marker")), "An absent optional component retains the existing skip policy");
    Check(File.ReadAllBytes(config).SequenceEqual(configBytes) && File.ReadAllBytes(state).SequenceEqual(stateBytes),
        "All success and failure paths preserve mode, startup preference and unrelated user settings byte for byte");
    foreach (var language in Loc.Languages)
    {
        Loc.Lang = language.Code;
        string message = Loc.T("GamingModeUpdateIncomplete");
        Check(message != "GamingModeUpdateIncomplete" && message.Contains("Playhub") && message.Contains("Gaming Mode"),
            "The setup failure has an actionable localized message: " + language.Code);
    }
    Console.WriteLine($"{checks}/{checks} agent installation readback checks passed. No agent, setup or Steam process was executed.");
}
finally
{
    string safe = Path.GetFullPath(Path.GetTempPath()) + "Playhub-Agent-Update-Checks-";
    if (!Path.GetFullPath(root).StartsWith(safe, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe fixture directory");
    Directory.Delete(root, recursive: true);
}

sealed class FakeProcess(bool exited, int exit, Action? onExit) : IGamingModeInstallerProcess
{
    internal int WaitBudget, ExitReads;
    internal bool Disposed, Aborted;
    public bool WaitForExit(int milliseconds) { WaitBudget = milliseconds; if (exited) onExit?.Invoke(); return exited; }
    public int ExitCode { get { if (!exited) throw new InvalidOperationException("Process is still running"); ExitReads++; return exit; } }
    public void Abort() => Aborted = true;
    public void Dispose() => Disposed = true;
}
