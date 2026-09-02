namespace GamingMode.Services;

internal readonly record struct ControllerHapticStep(int Side, double Scale, int DurationMs, int GapMs);

internal static class ControllerHapticPatterns
{
	public static IReadOnlyList<ControllerHapticStep> ForAction(string? action, int requestedSide)
	{
		int side = requestedSide is >= 0 and <= 2 ? requestedSide : 2;
		return action?.Trim() switch
		{
			"moveLeft" or "moveRight" or "moveUp" or "moveDown" =>
			[
				new(side, 0.86, 16, 0)
			],
			"tabPrevious" or "tabNext" =>
			[
				new(side, 1.00, 18, 10),
				new(side, 0.48, 12, 0)
			],
			"sliderDecrease" or "sliderIncrease" =>
			[
				new(side, 0.68, 12, 6),
				new(side, 0.34, 10, 0)
			],
			"toggleOn" =>
			[
				new(0, 0.42, 13, 9),
				new(1, 1.00, 20, 0)
			],
			"toggleOff" =>
			[
				new(1, 0.42, 13, 9),
				new(0, 0.88, 18, 0)
			],
			"confirm" =>
			[
				new(2, 1.00, 21, 12),
				new(2, 0.38, 12, 0)
			],
			"back" =>
			[
				new(0, 0.88, 18, 8),
				new(0, 0.32, 10, 0)
			],
			"dropdown" =>
			[
				new(2, 0.40, 12, 8),
				new(2, 0.90, 18, 0)
			],
			"options" or "menu" =>
			[
				new(0, 0.48, 11, 7),
				new(1, 0.82, 15, 8),
				new(2, 0.30, 10, 0)
			],
			"letter" =>
			[
				new(2, 0.90, 19, 0)
			],
			_ =>
			[
				new(side, 0.82, 16, 0)
			]
		};
	}
}
