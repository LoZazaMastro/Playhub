namespace GamingMode.Models;

public sealed class SafetySettings
{
	public int ApiPort { get; set; } = 47991;

	public bool AllowRemoteApi { get; set; }

	public bool RestartWithoutPrompt { get; set; } = true;
}
