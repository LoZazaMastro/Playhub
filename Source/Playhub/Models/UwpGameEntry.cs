namespace Playhub.Models;

public sealed class UwpGameEntry
{
    public bool Selected { get; set; }
    public string Name { get; set; } = "";
    public string Aumid { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Logo { get; set; } = "";
    public string PackageFamilyName { get; set; } = "";
    public bool IsLocalExecutable { get; set; }
    public string LocalExecutablePath { get; set; } = "";
    public string Publisher { get; set; } = "";
    public long FileSize { get; set; }
    public bool InSteamLibrary { get; set; }
    public string SteamGridDbCoverPath { get; set; } = "";
    public string SteamGridDbBannerPath { get; set; } = "";
    public string SteamGridDbHeroPath { get; set; } = "";
    public string SteamGridDbLogoPath { get; set; } = "";
    public string SteamGridDbIconPath { get; set; } = "";
    public int SteamGridDbGameId { get; set; }

    /// <summary>
    /// AppID Steam del gioco corrispondente, quando esiste anche su Steam.
    /// Serve a proporre gli artwork ufficiali di Valve quando SteamGridDB non ha nulla.
    /// -1 = cercato e non trovato, 0 = mai cercato.
    /// </summary>
    public int SteamAppId { get; set; }
    public bool SteamGridDbArtworkDisabled { get; set; }
}
