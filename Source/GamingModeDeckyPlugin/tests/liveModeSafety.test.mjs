import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = name => readFileSync(new URL(`../../GamingModeAgent/GamingMode.Services/${name}.cs`, import.meta.url), "utf8");
const manager = read("ModeManager");
const processes = read("ProcessTools");
const section = (text, start, end) => text.slice(text.indexOf(start), text.indexOf(end, text.indexOf(start)));

test("legacy session endpoints and safety recovery preserve the next login mode", () => {
  assert.match(manager, /ApplyModeAsync\([^\n]*bool updateShell = false\)/);
  const safety = readFileSync(new URL("../../Playhub/Assets/GamingMode/desktop-safety.ps1", import.meta.url), "utf8");
  assert.match(safety, /\$port = 47991/);
  assert.match(safety, /config\.safety\.apiPort/);
  assert.match(safety, /\$baseUri\/mode\/desktop\/switch" -Method POST/);
  assert.doesNotMatch(safety, /\/mode\/desktop\/restart|SetShellForMode|SaveConfig|Set-ItemProperty/);
  const boot = section(manager, "public async Task ApplyBootModeAsync", "public void ResumeGamingServices");
  assert.match(boot, /_shellTools.SetShellForMode\(modeKind\)/);
});

test("both login entry points cover Gaming startup before launching helpers", () => {
  const host = read("AgentHost");
  const boot = section(host, "bool flag = args.Any", "dashboard.Start();");
  assert.match(boot, /arg.Equals\("--boot"/);
  assert.doesNotMatch(boot, /bool showSplash = flag &&/);
  assert.match(boot, /bool showSplash = .*ModeKind.Gaming && !SafeModeGuard/);
  assert.ok(boot.indexOf("splashScreen.Show(") < boot.indexOf("manager.ApplyBootModeAsync("));
  const finish = boot.slice(boot.indexOf("finally"));
  assert.match(finish, /StartupForegroundGuard = null/);
  assert.match(finish, /steamRunning && themedStartup is not null\)[\s\S]*await themedStartup.WaitAsync\(\);\s*else\s*splashScreen.CancelImmediately\(\);/);
  assert.doesNotMatch(finish, /TryFocusSteamWindow|SetForegroundWindow/);
});

test("applying Gaming Mode does not rewrite any saved preference", () => {
  const gaming = section(manager, "private void ApplyGamingMode", "private void ApplyDesktopMode");
  assert.doesNotMatch(gaming, /SaveConfig|config\.\w+(?:\.\w+)*\s*=(?!=)/);
  // Ordinary mode still requires Explorer. Only the explicitly capability-
  // pinned trial may close it; neither branch may rewrite user preferences.
  assert.match(gaming, /bool shellFree = ShellFreeOverlayTrialPolicy\.MayRunWithoutExplorer\(config\.Gaming\)/);
  const trial = section(gaming, "if (shellFree)", "else");
  const ordinary = section(gaming, "else", "if (config.Gaming.EnsureSunshineCompatibilityInGamingMode)");
  assert.match(trial, /StartupForegroundGuard\?\.Invoke\(\) == false/);
  assert.match(trial, /if \(!_processTools\.StopExplorer\(\)\) throw new InvalidOperationException/);
  assert.match(ordinary, /if \(!_processTools\.StartExplorer\(background: true, canContinue: StartupForegroundGuard\)\)[\s\S]*throw new InvalidOperationException/);
  assert.doesNotMatch(ordinary, /StopExplorer\(/);
  const policy = read("ShellFreeOverlayTrialPolicy");
  assert.match(policy, /gaming\.CloseExplorerInGamingMode && gaming\.AllowExplorerCloseInGamingMode && Enabled/);
  assert.match(policy, /#else\s+return false;\s+#endif/);
  assert.match(policy, /components\.EnumerateObject\(\)\.Count\(\) != 3/);
  const resume = section(manager, "public void ResumeGamingServices()", "public Task<ApiResult> ApplyModeAsync");
  assert.doesNotMatch(resume, /StopExplorer\(/, "restarting the agent must never kill a running shell");
});
test("Explorer closure is session scoped, bounded, verified and does not alter the shell registry", () => {
  const stop = section(processes, "public bool StopExplorer()", "private static ServiceQueryState");
  assert.match(stop, /process.SessionId != sessionId/);
  assert.match(stop, /attempt < 20/);
  assert.match(stop, /quietTicks >= 5/);
  assert.match(stop, /return false/);
  assert.match(stop, /entireProcessTree: false/);
  assert.doesNotMatch(stop, /Registry|SetShell|shutdown|BeginLogoff/);
});
test("Explorer startup verifies the desktop shell rather than a folder process", () => {
  const start = section(processes, "public bool StartExplorer(", "public int RunUserStartupApps");
  assert.match(start, /ExplorerShellReadiness\.Ensure\(IsExplorerShellRunning,/);
  assert.doesNotMatch(start, /GetState\("explorer"\)/);
  const readiness = read("ExplorerShellReadiness");
  assert.match(readiness, /int timeoutMs = 12000/);
  assert.match(readiness, /polls <= 120/);
  assert.match(readiness, /elapsed >= timeoutMs/);
  assert.match(readiness, /elapsed < 0/);
  assert.match(start, /canContinue\?\.Invoke\(\) != false/);
  assert.match(processes, /nint shell = GetShellWindow\(\)/);
});
test("desktop exit and a later open share an acknowledged command generation", () => {
  const commands = read("SteamModeCommandState");
  assert.match(commands, /_exitDeadline = _clock\(\) \+ 4000/);
  assert.match(commands, /!_deliveredExit && _clock\(\) > _exitDeadline/);
  const change = section(manager, "public async Task<ApiResult> SwitchToModeAsync", "public async Task<ApiResult> RestartSteamAsync");
  const openAt = change.indexOf("_steamModeCommands.BeginOpen()"), desktopAt = change.indexOf("_steamModeCommands.RequestDesktop()");
  const applyAt = change.indexOf("ApplyModeAsync");
  assert.ok(openAt >= 0 && applyAt > openAt);
  assert.ok(desktopAt >= 0 && applyAt > desktopAt);
  assert.match(change, /!exit.Completed.IsCompleted/);
  assert.doesNotMatch(change, /BeginRestart|BeginLogoff|SaveConfig|SetShellForMode/);
});

test("live-switch curtain covers application and always hides before gate release", () => {
  const change = section(manager, "public async Task<ApiResult> SwitchToModeAsync", "public async Task<ApiResult> RestartSteamAsync");
  assert.ok(change.indexOf("_modeSwitch.WaitAsync(0)") < change.indexOf("using SplashScreenService"));
  const show = change.indexOf("transition.ShowTransition(");
  assert.ok(show >= 0 && show < change.indexOf("ApplyModeAsync"));
  const finish = change.slice(change.indexOf("finally"));
  assert.match(finish, /StartupForegroundGuard = null/);
  assert.match(finish, /if \(mode == ModeKind.Gaming\)\s*\{\s*themedStartup\?\.Dispose\(\);\s*transition.CancelImmediately\(\);\s*\}/);
  assert.match(finish, /else await transition.HideAsync\(minVisibleMs: 700, fade: true, fadeMs: 300\);/);
  assert.ok(finish.indexOf("transition.CancelImmediately") < finish.indexOf("_modeSwitch.Release()"));
  assert.ok(finish.indexOf("transition.HideAsync") < finish.indexOf("_modeSwitch.Release()"));
  assert.match(change, /attempt < 24/);
  const consume = section(manager, "public SteamModeCommand? ReadSteamModeCommand", "public ModeManager");
  assert.doesNotMatch(consume, /_modeSwitch.CurrentCount/);
});
