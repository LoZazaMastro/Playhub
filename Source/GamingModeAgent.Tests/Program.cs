using GamingMode.Services;
using System.Diagnostics;
using System.Reflection;

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
	("Display: Chromium process roles", TestDisplayProcessRoles),
	("Foreground cache bounds identity reads and disposes expired handles", TestForegroundCacheExpiry),
	("Foreground cache invalidates on PID changes and no foreground", TestForegroundCacheChanges),
	("Foreground cache rejects a dead handle even when PID is reused", TestForegroundCacheReuse),
	("Foreground identity queries the exact native process", TestNativeForegroundIdentity),
	("Empty Steam sessions do not inspect candidate processes", TestEmptySessionAvoidsProcessReads),
	("New Steam sessions are detected after an idle cache", TestSessionAfterIdle),
	("Active Steam sessions still exclude Steam client windows", TestActiveSessionExcludesSteam),
	("Fullscreen windows receive no style or resize writes", TestFullscreenIsUntouched),
	("Windowed games use physical monitor bounds and can recover again", TestPhysicalFullscreenRecovery),
	("Physical coordinates fail closed and restore after exceptions", TestPhysicalScopeFailure),
	("Native DPI scope restores the calling thread context", TestNativeDpiScope),
	("External overlay hosts never become fullscreen game windows", TestOverlayHostExclusion)
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

static void TestForegroundCacheExpiry()
{
	long now = 1000;
	var identities = new List<FakeForegroundIdentity>();
	var cache = new ForegroundProcessIdentityCache(pid => { var value = new FakeForegroundIdentity("steamwebhelper"); identities.Add(value); return value; }, () => now);
	for (int i = 0; i < 25; i++) { Equal("steamwebhelper", cache.Read(1)); now += 10; }
	Equal(1, identities.Count);
	Equal("steamwebhelper", cache.Read(1));
	Equal(2, identities.Count);
	Equal(1, identities[0].Disposals);
	cache.Read(0);
	Equal(1, identities[1].Disposals);
}

static void TestForegroundCacheChanges()
{
	var opened = new List<int>();
	var identities = new List<FakeForegroundIdentity>();
	var cache = new ForegroundProcessIdentityCache(pid => { opened.Add(pid); var identity = new FakeForegroundIdentity(pid == 10 ? "steam" : "game"); identities.Add(identity); return identity; }, () => 0);
	Equal("steam", cache.Read(10));
	Equal("game", cache.Read(20));
	Equal(1, identities[0].Disposals);
	Equal<string?>(null, cache.Read(0));
	Equal(1, identities[1].Disposals);
	Equal("steam", cache.Read(10));
	True(opened.SequenceEqual(new[] { 10, 20, 10 }));
}

static void TestForegroundCacheReuse()
{
	var first = new FakeForegroundIdentity("steam");
	var replacement = new FakeForegroundIdentity("game");
	int opens = 0;
	var cache = new ForegroundProcessIdentityCache(pid => ++opens == 1 ? first : replacement, () => 0);
	Equal("steam", cache.Read(10));
	first.IsAlive = false;
	Equal("game", cache.Read(10));
	Equal(2, opens);
	Equal(1, first.Disposals);
	var unavailable = new ForegroundProcessIdentityCache(pid => null, () => 0);
	Equal<string?>(null, unavailable.Read(1));
	var alreadyExited = new FakeForegroundIdentity("steam") { IsAlive = false };
	var exitedCache = new ForegroundProcessIdentityCache(pid => alreadyExited, () => 0);
	Equal<string?>(null, exitedCache.Read(1));
	Equal(1, alreadyExited.Disposals);
}

static void TestNativeForegroundIdentity()
{
	using Process current = Process.GetCurrentProcess();
	using IForegroundProcessIdentity? identity = NativeForegroundProcessIdentity.Open(current.Id);
	True(identity is not null && identity.IsAlive);
	Equal(current.ProcessName.ToLowerInvariant(), identity!.Name.ToLowerInvariant());
	Equal<IForegroundProcessIdentity?>(null, NativeForegroundProcessIdentity.Open(int.MaxValue));
}

static object? ReadActiveSession(string root, int pid, Func<int, string?> readName)
{
	MethodInfo method = typeof(OverlaySteamArtworkResolver).GetMethod("FindActiveGame", BindingFlags.Static | BindingFlags.NonPublic)!;
	return method.Invoke(null, new object?[] { root, pid, readName });
}

static string MakeSteamFixture()
{
	string root = Path.Combine(Path.GetTempPath(), "Playhub-native-perf-" + Guid.NewGuid().ToString("N"));
	Directory.CreateDirectory(Path.Combine(root, "logs"));
	return root;
}

static void DeleteSteamFixture(string root)
{
	string expectedPrefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "Playhub-native-perf-";
	if (!Path.GetFullPath(root).StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid fixture cleanup path");
	Directory.Delete(root, true);
}

static void TestEmptySessionAvoidsProcessReads()
{
	string root = MakeSteamFixture();
	try
	{
		int reads = 0;
		Func<int, string?> readName = pid => { reads++; throw new InvalidOperationException("Idle must not inspect processes"); };
		Equal<object?>(null, ReadActiveSession(root, int.MaxValue, readName)); // Missing log.
		File.WriteAllText(Path.Combine(root, "logs", "gameprocess_log.txt"), "Client version: fixture\n");
		for (int i = 0; i < 50; i++) Equal<object?>(null, ReadActiveSession(root, i + 1, readName));
		Equal(0, reads);
	}
	finally { DeleteSteamFixture(root); }
}

static void WriteTrackedFixture(string root)
{
	using Process current = Process.GetCurrentProcess();
	File.WriteAllText(Path.Combine(root, "logs", "gameprocess_log.txt"), $"[{current.StartTime:yyyy-MM-dd HH:mm:ss}] AppID 123 adding PID {current.Id}\n");
}

static void TestSessionAfterIdle()
{
	string root = MakeSteamFixture();
	try
	{
		using Process current = Process.GetCurrentProcess();
		int reads = 0;
		Func<int, string?> readName = pid => { reads++; return "testgame"; };
		File.WriteAllText(Path.Combine(root, "logs", "gameprocess_log.txt"), "Client version: fixture\n");
		Equal<object?>(null, ReadActiveSession(root, current.Id, readName));
		Equal(0, reads);
		WriteTrackedFixture(root);
		Thread.Sleep(550); // Production log stat cache is bounded to 500 ms.
		True(ReadActiveSession(root, current.Id, readName) is not null);
		Equal(1, reads);
	}
	finally { DeleteSteamFixture(root); }
}

static void TestActiveSessionExcludesSteam()
{
	string root = MakeSteamFixture();
	try
	{
		WriteTrackedFixture(root);
		using Process current = Process.GetCurrentProcess();
		int reads = 0;
		Equal<object?>(null, ReadActiveSession(root, current.Id, pid => { reads++; return "steamwebhelper"; }));
		Equal(1, reads);
	}
	finally { DeleteSteamFixture(root); }
}

static void True(bool condition)
{
	if (!condition) throw new InvalidOperationException("Condition was false.");
}

static void TestOverlayHostExclusion()
{
	foreach (string name in new[] { "GlosSITarget", "glossitarget.exe", @"F:\proof\GlosSITarget.exe" })
		True(FullscreenWindowGeometry.IsOverlayHostProcess(name));
	foreach (string? name in new[] { "Cuphead", "Cuphead.exe", "DOOM64_x64", "GlosSITargetGame", "steamwebhelper", "", null })
		Equal(false, FullscreenWindowGeometry.IsOverlayHostProcess(name));
}

static void TestFullscreenIsUntouched()
{
	int reads = 0, writes = 0, restores = 0;
	bool physical = false;
	var screen = new PhysicalWindowRect(0, 0, 3840, 2160);
	for (int i = 0; i < 25; i++)
	{
		Equal(false, FullscreenWindowGeometry.Apply(() =>
		{
			True(physical); reads++;
			return new FullscreenWindowBounds(screen, screen);
		}, _ => writes++, () => { physical = true; return new ActionScope(() => { physical = false; restores++; }); }));
	}
	Equal(25, reads); Equal(0, writes); Equal(25, restores); Equal(false, physical);
	True(new PhysicalWindowRect(-3840, 0, 0, 2160).Matches(new(-3839, 0, 0, 2160)));
	Equal(false, new PhysicalWindowRect(-3840, 0, 0, 2160).Matches(screen));
}

static void TestPhysicalFullscreenRecovery()
{
	var monitor = new PhysicalWindowRect(-3840, 0, 0, 2160);
	var window = new PhysicalWindowRect(-3600, 200, -1600, 1400);
	int writes = 0;
	bool physical = false;
	bool Apply() => FullscreenWindowGeometry.Apply(() =>
	{
		True(physical);
		return new FullscreenWindowBounds(window, monitor);
	}, bounds => { True(physical); Equal(monitor, bounds.Monitor); window = bounds.Monitor; writes++; },
		() => { physical = true; return new ActionScope(() => physical = false); });
	Equal(true, Apply()); Equal(false, Apply()); Equal(1, writes); Equal(false, physical);
	window = new PhysicalWindowRect(-3500, 300, -1700, 1300); // Same styles, game returns to windowed mode.
	Equal(true, Apply()); Equal(2, writes);
}

static void TestPhysicalScopeFailure()
{
	int reads = 0, writes = 0, restores = 0;
	Equal(false, FullscreenWindowGeometry.Apply(() => { reads++; return null; }, _ => writes++, () => null));
	Equal(0, reads); Equal(0, writes);
	Equal(false, FullscreenWindowGeometry.Apply(() => null, _ => writes++, () => new ActionScope(() => restores++)));
	Equal(1, restores);
	try
	{
		FullscreenWindowGeometry.Apply(() => new FullscreenWindowBounds(new(10, 10, 600, 400), new(0, 0, 3840, 2160)),
			_ => throw new InvalidOperationException("fixture"), () => new ActionScope(() => restores++));
		throw new Exception("Expected resize exception");
	}
	catch (InvalidOperationException exception) when (exception.Message == "fixture") { }
	Equal(2, restores);
}

static void TestNativeDpiScope()
{
	nint original = DpiTestNative.GetThreadDpiAwarenessContext();
	using (IDisposable? scope = PhysicalDpiScope.Enter())
	{
		True(scope != null);
		Equal(2, DpiTestNative.GetAwarenessFromDpiAwarenessContext(DpiTestNative.GetThreadDpiAwarenessContext()));
	}
	True(DpiTestNative.AreDpiAwarenessContextsEqual(original, DpiTestNative.GetThreadDpiAwarenessContext()));
}

sealed class ActionScope(Action restore) : IDisposable
{
	public void Dispose() => restore();
}

static class DpiTestNative
{
	[System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern nint GetThreadDpiAwarenessContext();
	[System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern int GetAwarenessFromDpiAwarenessContext(nint context);
	[System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);
}

sealed class FakeForegroundIdentity(string name) : IForegroundProcessIdentity
{
	public string Name { get; } = name;
	public bool IsAlive { get; set; } = true;
	public int Disposals { get; private set; }
	public void Dispose() => Disposals++;
}
