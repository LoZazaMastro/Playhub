import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

const read = path => fs.readFileSync(new URL(path, import.meta.url), "utf8");
const manager = read("../../GamingModeAgent/GamingMode.Services/ModeManager.cs");
const entry = read("../src/index.tsx");

test("desktop app waits for mode completion and does not offer Dashboard disable or test controls", () => {
  const service = read("../../Playhub/Services/GamingModeService.cs");
  const ui = read("../../Playhub/MainWindow.xaml.cs");
  const change = service.split("public async Task<bool> SwitchModeAsync")[1].split("public async Task<bool> SetDefaultModeViaAgentAsync")[0];
  assert.match(change, /Timeout = TimeSpan.FromSeconds\(90\)/);
  assert.match(change, /TryGetProperty\("ok"/);
  assert.doesNotMatch(change, /PostAgentAsync/);
  assert.doesNotMatch(ui, /"Attiva Playhub Dashboard"|"Prova Playhub Dashboard"|GetToggle\("dashboardEnabled"\)/);
});

test("animation color previews three channels in a full swatch and hint follows the accent button", () => {
  const ui = read("../../Playhub/MainWindow.AnimationSettings.cs");
  assert.match(ui, /customButton.Background = customBrush/);
  assert.doesNotMatch(ui, /customFill|opacityTrack|AnimationSlider\("O"/);
  assert.match(ui, /hueTrack.GradientStops.Add/);
  assert.match(ui, /saturationTrack.GradientStops.Add/);
  assert.match(ui, /blacknessTrack.GradientStops.Add/);
  assert.match(ui, /SliderTrackValueFill[\s\S]*Colors.Transparent/);
  assert.match(ui, /Button\("Provalo ora"[^\n]*primary: true/);
  assert.match(ui, /previewHint.TextAlignment = TextAlignment.Right/);
  assert.match(ui, /Body\("Premi ESC o fai clic per tornare a Playhub"\)/);
});

test("Explorer autostart and agent launches share a Decky startup lock", () => {
  const guard = read("../../Shared/DeckyStartupGuard.cs");
  const processes = read("../../GamingModeAgent/GamingMode.Services/ProcessTools.cs");
  const installer = read("../../Playhub/Services/DeckyInstallerService.cs");
  const explorer = processes.split("public bool StartExplorer()")[1].split("public int RunUserStartupApps()")[0];
  assert.ok(explorer.indexOf("UpgradeExistingAutostart()") < explorer.indexOf("Process.Start("));
  assert.match(processes, /DeckyStartupGuard.RunExclusive\(\(\) => EnsureProcessUnlocked/);
  assert.match(installer, /SetValue\("DeckyLoader", Playhub.Shared.DeckyStartupGuard.CreateCommand\(loader\)\)/);
  assert.match(guard, /Get-Process -Name PluginLoader,PluginLoader_noconsole/);
  assert.equal(guard.split("Local\\Playhub.Decky.Start.").length - 1, 2);
  assert.ok(guard.indexOf("$mutex.WaitOne") < guard.indexOf("Get-Process -Name"));
  assert.ok(guard.indexOf("Get-Process -Name") < guard.indexOf("[Diagnostics.Process]::Start"));
});

test("desktop transition stops Decky before Explorer and only ensures one replacement", () => {
  const desktop = manager.split("private void ApplyDesktopMode(")[1];
  assert.ok(desktop.indexOf("StopDeckyForDesktopTransition()") < desktop.indexOf("StartExplorer()"));
  assert.match(desktop, /CurrentMode == ModeKind.Gaming/);
  assert.match(desktop, /finally[\s\S]*if \(restoreDecky\)[\s\S]*EnsureProcessWithEnvironment/);
  const processes = read("../../GamingModeAgent/GamingMode.Services/ProcessTools.cs");
  assert.match(processes, /process.SessionId == session/);
  assert.match(processes, /WaitForExit\(5000\)/);
  assert.match(processes, /if \(GetState\(processNames\).Running\)[\s\S]*return true/);
});

test("mode confirmation asks a localized question with Ok and Cancel", () => {
  assert.match(entry, /strTitle=\{getModeQuestion\(currentLocale, mode\)\}/);
  assert.match(entry, /strOKButtonText="Ok"/);
  assert.match(entry, /strCancelButtonText=\{local.cancel\}/);
  assert.doesNotMatch(entry, /PanelSection title=\{local.dashboard\}/);
});

test("live mode transition reuses configured mode application without changing login defaults", () => {
  const method = manager.split("public async Task<ApiResult> SwitchToModeAsync")[1].split("public async Task<ApiResult> RestartSteamAsync")[0];
  assert.match(method, /ApplyModeAsync\(mode,[\s\S]*updateShell: false/);
  assert.doesNotMatch(method, /RestartInMode\(|BeginRestart\(|BeginLogoff\(|SetShellForMode\(|SaveConfig\(/);
  assert.match(method, /_modeSwitch.WaitAsync\(0\)/);
  assert.match(method, /finally \{ _modeSwitch.Release\(\)/);
  assert.match(manager, /CloseExplorerInGamingMode && config.Gaming.AllowExplorerCloseInGamingMode/);
  assert.match(manager, /if \(config.Gaming.RestoreExplorerOnDesktop\)/);
});

test("QAM and power menu switch commands use translated live labels and never restart", () => {
  const binding = entry.split("const uninstallPowerMenuPatch =")[1].split("const hapticRevision")[0];
  assert.match(binding, /t\(\).switchGaming/);
  assert.match(binding, /t\(\).switchDesktop/);
  assert.match(binding, /\/mode\/\$\{mode\}\/switch/);
  assert.doesNotMatch(binding, /\/restart/);
  assert.match(entry, /signal.exitBigPicture[\s\S]*ExitBigPictureMode/);
});
