using System.IO;
using System.Runtime.InteropServices;
using GamingMode.Models;

namespace GamingMode.Services;

public static class SafeModeGuard
{
	private const int VirtualKeyShift = 16;

	public static bool ShouldForceDesktop(AppPaths paths)
	{
		if (IsShiftPressed())
		{
			return true;
		}
		return File.Exists(Path.Combine(paths.ConfigDirectory, "force-desktop.flag"));
	}

	public static void ApplySafeDefaults(ModeConfig config)
	{
		config.DefaultMode = ModeKind.Desktop;
		config.NextBootMode = null;
		config.Gaming.CloseExplorerInGamingMode = false;
	}

	private static bool IsShiftPressed()
	{
		return (GetAsyncKeyState(16) & 0x8000) != 0;
	}

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int virtualKeyCode);
}
