namespace GamingMode.Models;

public sealed class GamingStartupApp
{
	public string Name { get; set; } = "";

	public string? Path { get; set; }

	public string Arguments { get; set; } = "";

	public string? WorkingDirectory { get; set; }

	public string? ProcessName { get; set; }

	public bool Enabled { get; set; } = true;

	public bool StartMinimized { get; set; } = true;

	public int DelayAfterStartMs { get; set; }
}
