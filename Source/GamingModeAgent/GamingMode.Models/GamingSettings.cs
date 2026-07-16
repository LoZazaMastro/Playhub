using System.Collections.Generic;

namespace GamingMode.Models;

public sealed class GamingSettings
{
	public string? SteamPath { get; set; }

	public string SteamArguments { get; set; } = "-gamepadui";

	public string? DeckyPath { get; set; }

	public string? SunshinePath { get; set; }

	public bool DeckyRequired { get; set; } = true;

	public bool SunshineRequired { get; set; } = true;

	public int DelaySteamAfterDeckyMs { get; set; } = 1500;

	public bool CloseExplorerInGamingMode { get; set; }

	public bool AllowExplorerCloseInGamingMode { get; set; }

	public bool RestoreExplorerOnDesktop { get; set; } = true;

	public bool RestoreStartupAppsOnDesktop { get; set; }

	public bool OpenSteamDesktopOnInteractiveDesktopMode { get; set; }

	public bool EnsureInputCompatibilityInGamingMode { get; set; } = true;

	public bool EnsureSunshineCompatibilityInGamingMode { get; set; } = true;

	public bool AutoHideMouseCursorInGamingMode { get; set; } = true;

	public int AutoHideMouseCursorAfterMs { get; set; } = 2200;

	public bool BorderlessFullscreenWindowsInGamingMode { get; set; } = true;

	public List<GamingStartupApp> CustomStartupApps { get; set; } = new List<GamingStartupApp>();

	public GamingSplashSettings Splash { get; set; } = new GamingSplashSettings();

	public bool ManageAudio { get; set; }
}
