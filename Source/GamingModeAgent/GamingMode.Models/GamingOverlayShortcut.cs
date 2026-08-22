namespace GamingMode.Models;

public enum GamingOverlayShortcutKind
{
	DesktopProgram,
	WindowsApp
}

public sealed class GamingOverlayShortcut
{
	public string Id { get; set; } = System.Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "";

	public GamingOverlayShortcutKind Kind { get; set; }

	public string Target { get; set; } = "";

	public string Arguments { get; set; } = "";

	public string WorkingDirectory { get; set; } = "";
}
