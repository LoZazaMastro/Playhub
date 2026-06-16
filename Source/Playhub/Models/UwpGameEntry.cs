namespace Playhub.Models;

public sealed class UwpGameEntry
{
    public bool Selected { get; set; }
    public string Name { get; set; } = "";
    public string Aumid { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Logo { get; set; } = "";
    public string PackageFamilyName { get; set; } = "";
}
