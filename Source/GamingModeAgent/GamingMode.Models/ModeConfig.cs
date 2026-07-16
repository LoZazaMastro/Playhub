namespace GamingMode.Models;

public sealed class ModeConfig
{
	public string? Language { get; set; }

	public ModeKind DefaultMode { get; set; }

	public ModeKind? NextBootMode { get; set; }

	public GamingSettings Gaming { get; set; } = new GamingSettings();

	public SafetySettings Safety { get; set; } = new SafetySettings();
}
