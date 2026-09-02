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

	// ---------- PLAYHUB DASHBOARD ----------
	//
	// La Dashboard e' una schermata del plugin di Steam. Qui restano solo le
	// cose che il plugin non puo' sapere da solo.

	// Le sue scorciatoie (le "preferite" nella schermata Home).
	public List<GamingOverlayShortcut> DashboardShortcuts { get; set; } = new List<GamingOverlayShortcut>();

	// Interruttore principale condiviso fra Playhub, agente e plugin Decky.
	// Rimane attivo per compatibilita' con le installazioni precedenti.
	public bool DashboardEnabled { get; set; } = true;

	// Scorciatoia da tastiera per aprirla. Serve perche' mentre un gioco e' in
	// primo piano l'interfaccia di Steam non riceve nulla. Chi la vuole aprire
	// col pad lega questa stessa combinazione a un accordo DENTRO Steam: cosi'
	// e' Steam a premere i tasti e nessuno tocca il controller.
	public bool DashboardKeyboardShortcutEnabled { get; set; } = true;

	public string DashboardHotkey { get; set; } = "Ctrl+Alt+P";

	public bool NavigationHapticsEnabled { get; set; }

	public int NavigationHapticsIntensity { get; set; } = 55;

	public GamingSplashSettings Splash { get; set; } = new GamingSplashSettings();

	public bool ManageAudio { get; set; }
}
