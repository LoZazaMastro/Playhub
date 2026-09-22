using GamingMode.Services;

var tests = new (string Name, Action Run)[]
{
	("Hidden helper policy preserves interactive applications", TestHiddenHelpers),
	("Steam UI never inherits game identity", TestSteamClientIdentity),
	("Tracked game identity only flows to child processes", TestTrackedGameAncestry),
	("Historical tracking cannot match a recycled PID", TestRecycledGamePid),
	("Steam UI gate allows navigation", TestGateAllowsSteamUi),
	("Steam UI gate blocks active games", TestGateBlocksGames),
	("Steam UI gate blocks background Steam", TestGateBlocksBackgroundSteam),
	("Directional navigation pattern", TestDirectionalPattern),
	("Pattern timing bounds", TestPatternTiming),
	("Display: TV waking from standby is a hotplug", TestDisplayTvWake),
	("Display: monitors appearing or vanishing are hotplugs", TestDisplayCountChange),
	("Display: HDR change on the same monitor is colour-only", TestDisplayColorOnly),
	("Display: resolution change never triggers recovery", TestDisplayModeOnly),
	("Display: recovery decision", TestDisplayRecoveryDecision),
	("Display: black screen sampling", TestDisplayBlackSampling),
	("Display: Chromium process roles", TestDisplayProcessRoles)
};

var failures = new List<string>();
foreach ((string name, Action run) in tests)
{
	try
	{
		run();
		Console.WriteLine($"PASS {name}");
	}
	catch (Exception exception)
	{
		failures.Add($"FAIL {name}: {exception.Message}");
	}
}

foreach (string failure in failures) Console.Error.WriteLine(failure);
if (failures.Count > 0) Environment.ExitCode = 1;

static void TestHiddenHelpers()
{
	Equal(true, BackgroundProcessPolicy.IsHiddenPowerShell(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", "-NoProfile -WindowStyle Hidden -File safety.ps1"));
	Equal(true, BackgroundProcessPolicy.IsHiddenPowerShell("pwsh.exe", "-windowstyle hidden"));
	Equal(false, BackgroundProcessPolicy.IsHiddenPowerShell("powershell.exe", "-WindowStyle Normal -File setup.ps1"));
	Equal(false, BackgroundProcessPolicy.IsHiddenPowerShell("powershell.exe", null));
	Equal(false, BackgroundProcessPolicy.IsHiddenPowerShell("game.exe", "-WindowStyle Hidden"));
	Equal(false, BackgroundProcessPolicy.IsHiddenPowerShell("powershell.exe", "-WindowStyle HiddenOther"));
}

static void TestSteamClientIdentity()
{
	foreach (string name in new[] { "steam", "STEAM.EXE", "steamwebhelper", "steamservice", @"C:\Steam\GameOverlayUI.exe" })
		Equal(true, SteamGameIdentityPolicy.IsSteamClient(name));
	Equal(false, SteamGameIdentityPolicy.IsSteamClient("NFS14.exe"));
	Equal(false, SteamGameIdentityPolicy.IsSteamClient("steamworlddig.exe"));
}

static void TestTrackedGameAncestry()
{
	// Steam -> launcher -> game; browser is a sibling of the launcher.
	var parents = new Dictionary<int, int> { [20] = 10, [30] = 20, [40] = 10 };
	Equal(true, SteamGameIdentityPolicy.IsTrackedWindow(20, 20, parents));
	Equal(true, SteamGameIdentityPolicy.IsTrackedWindow(20, 30, parents));
	Equal(false, SteamGameIdentityPolicy.IsTrackedWindow(30, 20, parents));
	Equal(false, SteamGameIdentityPolicy.IsTrackedWindow(20, 10, parents));
	Equal(false, SteamGameIdentityPolicy.IsTrackedWindow(20, 40, parents));
	Equal(false, SteamGameIdentityPolicy.IsTrackedWindow(99, 40, new Dictionary<int, int> { [40] = 50, [50] = 40 }));
}

static void TestRecycledGamePid()
{
	var tracked = new DateTime(2026, 8, 22, 17, 41, 26);
	Equal(false, SteamGameIdentityPolicy.IsCurrentProcess(new DateTime(2026, 9, 12, 16, 20, 43), tracked));
	Equal(true, SteamGameIdentityPolicy.IsCurrentProcess(tracked.AddMilliseconds(-800), tracked));
	Equal(true, SteamGameIdentityPolicy.IsCurrentProcess(tracked.AddMilliseconds(500), tracked));
}

static void TestGateAllowsSteamUi()
{
	Equal(SteamUiHapticsState.Allowed, SteamUiHapticsGate.Evaluate(true, 0, true));
}

static void TestGateBlocksGames()
{
	Equal(SteamUiHapticsState.GameActive, SteamUiHapticsGate.Evaluate(true, 620, true));
}

static void TestGateBlocksBackgroundSteam()
{
	Equal(SteamUiHapticsState.SteamNotForeground, SteamUiHapticsGate.Evaluate(true, 0, false));
	Equal(SteamUiHapticsState.SteamUnavailable, SteamUiHapticsGate.Evaluate(false, 0, false));
}

static void TestDirectionalPattern()
{
	ControllerHapticStep left = ControllerHapticPatterns.ForAction("moveLeft", 0).Single();
	ControllerHapticStep right = ControllerHapticPatterns.ForAction("moveRight", 1).Single();
	Equal(0, left.Side);
	Equal(1, right.Side);
	True(left.DurationMs <= 24 && right.DurationMs <= 24);
}

static void TestPatternTiming()
{
	string[] actions =
	[
		"moveLeft", "moveRight", "moveUp", "moveDown", "tabPrevious", "tabNext",
		"sliderDecrease", "sliderIncrease", "toggleOn", "toggleOff", "confirm", "back",
		"dropdown", "options", "menu", "letter"
	];
	foreach (string action in actions)
	{
		IReadOnlyList<ControllerHapticStep> pattern = ControllerHapticPatterns.ForAction(action, 2);
		True(pattern.Count is >= 1 and <= 3);
		True(pattern.All(step => step.DurationMs is >= 8 and <= 24));
		True(pattern.All(step => step.GapMs is >= 0 and <= 16));
	}
}

static DisplaySnapshot Snap(params DisplayMonitorEntry[] monitors) => new(monitors);

// Report 21/09/2026: stesso \\.\DISPLAY1 3840x2160, ma a TV spenta SDR 8 bit e
// a TV accesa HDR 10 bit con un monitor ricreato da Windows.
static void TestDisplayTvWake()
{
	var standby = Snap(new DisplayMonitorEntry(@"\\.\DISPLAY1", 0x10001, 3840, 2160, false, 8));
	var awake = Snap(new DisplayMonitorEntry(@"\\.\DISPLAY1", 0x20003, 3840, 2160, true, 10));
	Equal(DisplayChangeKind.Hotplug, DisplayChangePolicy.Classify(standby, awake));
	Equal(true, DisplayChangePolicy.ShouldRecover(DisplayChangePolicy.Classify(standby, awake), false));
	Equal(true, standby.SameAs(Snap(new DisplayMonitorEntry(@"\\.\DISPLAY1", 0x10001, 3840, 2160, false, 8))));
}

static void TestDisplayCountChange()
{
	var tv = new DisplayMonitorEntry(@"\\.\DISPLAY1", 0x10001, 3840, 2160, true, 10);
	Equal(DisplayChangeKind.Hotplug, DisplayChangePolicy.Classify(DisplaySnapshot.Empty, Snap(tv)));
	Equal(DisplayChangeKind.Hotplug, DisplayChangePolicy.Classify(Snap(tv), DisplaySnapshot.Empty));
	Equal(DisplayChangeKind.Hotplug, DisplayChangePolicy.Classify(Snap(tv), Snap(tv with { Device = @"\\.\DISPLAY2" })));
	// L'ordine di enumerazione non conta.
	var second = new DisplayMonitorEntry(@"\\.\DISPLAY2", 0x10005, 1920, 1080, false, 8);
	Equal(DisplayChangeKind.None, DisplayChangePolicy.Classify(Snap(tv, second), Snap(second, tv)));
}

static void TestDisplayColorOnly()
{
	var sdr = new DisplayMonitorEntry(@"\\.\DISPLAY1", 0x10001, 3840, 2160, false, 8);
	Equal(DisplayChangeKind.ColorOnly, DisplayChangePolicy.Classify(Snap(sdr), Snap(sdr with { AdvancedColor = true, BitsPerColor = 10 })));
	Equal(DisplayChangeKind.ColorOnly, DisplayChangePolicy.Classify(Snap(sdr), Snap(sdr with { AdvancedColor = null })));
	Equal(DisplayChangeKind.ColorOnly, DisplayChangePolicy.Classify(Snap(sdr), Snap(sdr with { Width = 2560, Height = 1440, AdvancedColor = true })));
}

static void TestDisplayModeOnly()
{
	var tv = new DisplayMonitorEntry(@"\\.\DISPLAY1", 0x10001, 3840, 2160, true, 10);
	var changed = DisplayChangePolicy.Classify(Snap(tv), Snap(tv with { Width = 1920, Height = 1080 }));
	Equal(DisplayChangeKind.ModeOnly, changed);
	Equal(false, DisplayChangePolicy.NeedsRecovery(changed));
	Equal(false, DisplayChangePolicy.ShouldRecover(changed, true));
	Equal(DisplayChangeKind.Hotplug, DisplayChangePolicy.Strongest(DisplayChangeKind.Hotplug, DisplayChangeKind.ModeOnly));
	Equal(DisplayChangeKind.ColorOnly, DisplayChangePolicy.Strongest(DisplayChangeKind.ModeOnly, DisplayChangeKind.ColorOnly));
}

static void TestDisplayRecoveryDecision()
{
	Equal(true, DisplayChangePolicy.ShouldRecover(DisplayChangeKind.Hotplug, null));
	Equal(true, DisplayChangePolicy.ShouldRecover(DisplayChangeKind.Hotplug, false));
	// HDR cambiato dal pannello rapido con Steam che disegna: nessun intervento.
	Equal(false, DisplayChangePolicy.ShouldRecover(DisplayChangeKind.ColorOnly, false));
	Equal(true, DisplayChangePolicy.ShouldRecover(DisplayChangeKind.ColorOnly, true));
	Equal(true, DisplayChangePolicy.ShouldRecover(DisplayChangeKind.ColorOnly, null));
	Equal(false, DisplayChangePolicy.ShouldRecover(DisplayChangeKind.None, true));
}

static void TestDisplayBlackSampling()
{
	Equal<bool?>(true, DisplayChangePolicy.IsBlack(new uint[] { 0x000000, 0x0A0A0A, 0x000C00 }));
	Equal<bool?>(false, DisplayChangePolicy.IsBlack(new uint[] { 0x000000, 0x000000, 0x1E1E1E }));
	Equal<bool?>(false, DisplayChangePolicy.IsBlack(new uint[] { 0x0000FF }));
	Equal<bool?>(null, DisplayChangePolicy.IsBlack(new uint[] { 0x000000, DisplayChangePolicy.InvalidPixel }));
	Equal<bool?>(null, DisplayChangePolicy.IsBlack(Array.Empty<uint>()));
}

static void TestDisplayProcessRoles()
{
	const string browser = "\"C:\\Program Files (x86)\\Steam\\bin\\cef\\cef.win64\\steamwebhelper.exe\" -nocrashdialog -lang=it_IT -cachedir=\"C:\\Users\\Andrea\\AppData\\Local\\Steam\\htmlcache\"";
	const string gpu = "\"C:\\Program Files (x86)\\Steam\\bin\\cef\\cef.win64\\steamwebhelper.exe\" --type=gpu-process --disable-breakpad";
	const string renderer = "\"C:\\Program Files (x86)\\Steam\\bin\\cef\\cef.win64\\steamwebhelper.exe\" --type=renderer --enable-chrome-runtime";
	Equal(true, DisplayChangePolicy.IsBrowserProcess(browser));
	Equal(false, DisplayChangePolicy.IsGpuProcess(browser));
	Equal(true, DisplayChangePolicy.IsGpuProcess(gpu));
	Equal(false, DisplayChangePolicy.IsBrowserProcess(gpu));
	Equal(false, DisplayChangePolicy.IsGpuProcess(renderer));
	Equal(false, DisplayChangePolicy.IsBrowserProcess(renderer));
	// Riga di comando illeggibile: mai toccare il processo.
	Equal(false, DisplayChangePolicy.IsGpuProcess(null));
	Equal(false, DisplayChangePolicy.IsBrowserProcess(null));
	Equal(false, DisplayChangePolicy.IsBrowserProcess(""));
}

static void Equal<T>(T expected, T actual)
{
	if (!EqualityComparer<T>.Default.Equals(expected, actual))
	{
		throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
	}
}

static void True(bool condition)
{
	if (!condition) throw new InvalidOperationException("Condition was false.");
}
