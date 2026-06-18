namespace Playhub.Models;

public sealed class UwpGameEntry
{
    public bool Selected { get; set; }
    public string Name { get; set; } = "";
    public string Aumid { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Logo { get; set; } = "";
    public string PackageFamilyName { get; set; } = "";
    public bool InSteamLibrary { get; set; }
    public string SteamGridDbCoverPath { get; set; } = "";
    public string SteamGridDbBannerPath { get; set; } = "";
    public string SteamGridDbHeroPath { get; set; } = "";
    public string SteamGridDbLogoPath { get; set; } = "";
    public string SteamGridDbIconPath { get; set; } = "";
    public int SteamGridDbGameId { get; set; }
}
