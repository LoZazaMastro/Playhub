using System.Runtime.CompilerServices;

namespace GamingMode;

public sealed record LogoChoice(string Name, string? Path)
{
	public static LogoChoice Playhub { get; } = new LogoChoice("Playhub", null);

	[CompilerGenerated]
	private LogoChoice(LogoChoice original)
	{
		Name = original.Name;
		Path = original.Path;
	}
}
