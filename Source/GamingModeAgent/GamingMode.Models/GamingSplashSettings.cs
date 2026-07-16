namespace GamingMode.Models;

public sealed class GamingSplashSettings
{
	public bool Enabled { get; set; } = true;

	public string? LogoPath { get; set; }

	public int MinVisibleMs { get; set; } = 1200;

	public int MaxVisibleMs { get; set; } = 120000;
}
