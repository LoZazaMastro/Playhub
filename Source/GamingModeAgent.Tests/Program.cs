using GamingMode.Services;

var tests = new (string Name, Action Run)[]
{
	("Steam UI gate allows navigation", TestGateAllowsSteamUi),
	("Steam UI gate blocks active games", TestGateBlocksGames),
	("Steam UI gate blocks background Steam", TestGateBlocksBackgroundSteam),
	("Directional navigation pattern", TestDirectionalPattern),
	("Pattern timing bounds", TestPatternTiming)
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
