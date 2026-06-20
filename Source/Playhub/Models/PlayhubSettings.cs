using System.Collections.Generic;

namespace Playhub.Models;

public sealed class PlayhubSettings
{
    public string Theme { get; set; } = "Sistema";
    public string Backdrop { get; set; } = "Acrylic";
    public string Language { get; set; } = "en";
    public string AccentColor { get; set; } = "#FFCB0F";
    public string PluginRoot { get; set; } = "";
    public string DeckyPluginsPath { get; set; } = "";
    public string PlayhubUpdateRepository { get; set; } = "LoZazaMastro/Playhub";
    public string SteamGridDbApiKey { get; set; } = "";
    public string ExecutableGamesFolder { get; set; } = "";
    public List<string> ExecutableGameFolders { get; set; } = new();
    public List<string> ExecutableGameFiles { get; set; } = new();
    public Dictionary<string, int> SteamGridDbGameOverrides { get; set; } = new();
    public Dictionary<string, string> SteamGridDbTitleOverrides { get; set; } = new();
    public List<string> SteamGridDbArtworkDisabled { get; set; } = new();
    public string CssLoaderProfileUrl { get; set; } = "https://www.mediafire.com/file/qml1pw9wve47xir/themes.zip/file";
    public List<string> RecentArtworkBackups { get; set; } = new();
    public bool WelcomeCompleted { get; set; }
    public string StartupPage { get; set; } = "decky";
}
