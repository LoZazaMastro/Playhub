using System.Collections.Generic;

namespace Playhub.Models;

public sealed class PlayhubSettings
{
    public string Theme { get; set; } = "Sistema";
    public string Backdrop { get; set; } = "Acrylic";
    public string Language { get; set; } = "auto";
    public string AccentColor { get; set; } = "#FFCB0F";
    public string PluginRoot { get; set; } = "";
    public string DeckyPluginsPath { get; set; } = "";
    public string PlayhubUpdateRepository { get; set; } = "LoZazaMastro/Playhub";
    public string SteamGridDbApiKey { get; set; } = "";
    public string XboxGamesView { get; set; } = "cards";
    public string CssLoaderProfileUrl { get; set; } = "https://www.mediafire.com/file/qml1pw9wve47xir/themes.zip/file";
    public List<string> RecentArtworkBackups { get; set; } = new();
    public bool WelcomeCompleted { get; set; }
    public string StartupPage { get; set; } = "decky";
}
