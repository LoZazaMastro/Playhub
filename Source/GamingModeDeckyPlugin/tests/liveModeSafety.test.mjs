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
  assert.match(safety, /47991\/mode\/desktop\/switch/);
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
  assert.match(boot, /finally[\s\S]*splashScreen.HideAsync/);
});

test("applying Gaming Mode does not rewrite any saved preference", () => {
  const gaming = section(manager, "private void ApplyGamingMode", "private void ApplyDesktopMode");
  assert.doesNotMatch(gaming, /SaveConfig|config\.\w+(?:\.\w+)*\s*=(?!=)/);
  assert.match(gaming, /if \(!_processTools.StopExplorer\(\)\)[\s\S]*throw new InvalidOperationException/);
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
  const start = section(processes, "public bool StartExplorer()", "public int RunUserStartupApps");
  assert.match(start, /IsExplorerShellRunning\(\)/);
  assert.doesNotMatch(start, /GetState\("explorer"\)/);
  assert.match(start, /attempt < 40/);
  assert.match(processes, /nint shell = GetShellWindow\(\)/);
});
test("desktop-exit signal expires and is cleared before another mode switch starts", () => {
  const consume = section(manager, "public bool ConsumeDesktopSwitchRequest", "public ModeManager");
  assert.match(consume, /Environment.TickCount64 - requested <= 4000/);
  assert.match(consume, /CurrentMode == ModeKind.Desktop/);
  const change = section(manager, "public async Task<ApiResult> SwitchToModeAsync", "public async Task<ApiResult> RestartSteamAsync");
  assert.ok(change.indexOf("Interlocked.Exchange(ref _desktopSwitchRequested, 0)") < change.indexOf("ApplyModeAsync"));
  assert.match(change, /result.Ok && mode == ModeKind.Desktop/);
  assert.doesNotMatch(change, /BeginRestart|BeginLogoff|SaveConfig|SetShellForMode/);
});

test("live-switch curtain covers application and always hides before gate release", () => {
  const change = section(manager, "public async Task<ApiResult> SwitchToModeAsync", "public async Task<ApiResult> RestartSteamAsync");
  assert.ok(change.indexOf("_modeSwitch.WaitAsync(0)") < change.indexOf("using SplashScreenService"));
  const show = change.indexOf("transition.ShowTransition(");
  assert.ok(show >= 0 && show < change.indexOf("ApplyModeAsync"));
  assert.match(change, /finally\s*\{\s*await transition.HideAsync\(minVisibleMs: 700, fade: true, fadeMs: 300\);/);
  assert.ok(change.indexOf("transition.HideAsync") < change.indexOf("_modeSwitch.Release()"));
  assert.match(change, /attempt < 24/);
  const consume = section(manager, "public bool ConsumeDesktopSwitchRequest", "public ModeManager");
  assert.doesNotMatch(consume, /_modeSwitch.CurrentCount/);
});
