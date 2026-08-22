using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playhub.Models;

// LA CONFIGURAZIONE E' CONDIVISA, E QUESTO NE E' SOLO META'.
//
// Lo stesso config.json lo scrivono in due: l'agente Gaming Mode e questa app.
// L'agente ne conosce ogni campo; qui invece c'e' soltanto quello che serve
// all'interfaccia. Fino a poco fa la conseguenza era grave e silenziosa:
// bastava che Playhub leggesse e riscrivesse il file - cosa che fa da sola,
// all'avvio e a ogni riparazione - perche' tutti i campi che questo modello non
// conosce sparissero dal disco.
//
// Si vedeva cosi': le app preferite della Dashboard sparivano al riavvio, e la
// combinazione da tastiera tornava sempre a quella predefinita per quanto la si
// cambiasse. Non e' che non venissero salvate: venivano cancellate subito dopo.
//
// La proprieta' qui sotto raccoglie tutto cio' che il modello non riconosce e
// lo riscrive tale e quale. Vale anche per i campi che verranno aggiunti in
// futuro sul lato agente, senza che nessuno debba ricordarsi di rispecchiarli
// qui.
public sealed class GamingModeConfig
{
    public string DefaultMode { get; set; } = "Desktop";
    public string? NextBootMode { get; set; }
    public GamingOptions Gaming { get; set; } = new();
    public SafetyOptions Safety { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class GamingOptions
{
    public string? SteamPath { get; set; }
    public string SteamArguments { get; set; } = "-gamepadui";
    public string? DeckyPath { get; set; }
    public string? SunshinePath { get; set; }
    public bool DeckyRequired { get; set; } = true;
    public bool SunshineRequired { get; set; } = true;
    public int DelaySteamAfterDeckyMs { get; set; } = 1500;
    public bool CloseExplorerInGamingMode { get; set; } = true;
    public bool AllowExplorerCloseInGamingMode { get; set; } = true;
    public bool RestoreExplorerOnDesktop { get; set; } = true;
    public bool RestoreStartupAppsOnDesktop { get; set; }
    public bool OpenSteamDesktopOnInteractiveDesktopMode { get; set; }
    public bool EnsureInputCompatibilityInGamingMode { get; set; } = true;
    public bool EnsureSunshineCompatibilityInGamingMode { get; set; } = true;
    public bool AutoHideMouseCursorInGamingMode { get; set; } = true;
    public int AutoHideMouseCursorAfterMs { get; set; } = 500;
    public bool BorderlessFullscreenWindowsInGamingMode { get; set; } = true;
    // Attiva automaticamente "Apri Game Bar dal controller" SOLO mentre gira un
    // gioco Xbox/MS Store (avviato via UWPHook), poi la rispegne. Serve perché il
    // QAM di Steam non si disegna sopra le app UWP: lì si usa la Xbox Game Bar.
    public bool EnableXboxGameBar { get; set; } = true;
    public bool DashboardEnabled { get; set; } = true;
    public bool ManageAudio { get; set; }
    public SplashOptions Splash { get; set; } = new();
    public List<StartupAppConfig> CustomStartupApps { get; set; } = new();

    // Qui finiscono le opzioni che vivono solo lato agente - fra cui le app
    // preferite della Dashboard e la sua combinazione da tastiera. Vanno
    // riscritte tali e quali.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SplashOptions
{
    public bool Enabled { get; set; } = true;
    public string? LogoPath { get; set; }
    public int MinVisibleMs { get; set; } = 1200;
    public int MaxVisibleMs { get; set; } = 120000;
}

public sealed class StartupAppConfig
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string? WorkingDirectory { get; set; }
    public string ProcessName { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public int DelayAfterStartMs { get; set; }
}

public sealed class SafetyOptions
{
    public int ApiPort { get; set; } = 47991;
    // Loopback basta a Playhub: niente accesso remoto (LAN) di default, per sicurezza.
    public bool AllowRemoteApi { get; set; } = false;
    public bool RestartWithoutPrompt { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
