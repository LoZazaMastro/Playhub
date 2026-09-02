using System.Diagnostics;
using Microsoft.Win32;

namespace GamingMode.Services;

internal enum SteamUiHapticsState
{
	Allowed,
	SteamUnavailable,
	GameActive,
	SteamNotForeground
}

internal sealed class SteamUiHapticsGate
{
	public bool CanPlay() => ReadState() == SteamUiHapticsState.Allowed;

	public SteamUiHapticsState ReadState()
	{
		bool steamRunning;
		try
		{
			Process[] processes = Process.GetProcessesByName("steam");
			steamRunning = processes.Length > 0;
			foreach (Process process in processes) process.Dispose();
		}
		catch
		{
			steamRunning = false;
		}

		return Evaluate(steamRunning, ReadRunningAppId(), OverlayWindowTools.IsSteamForeground());
	}

	internal static SteamUiHapticsState Evaluate(bool steamRunning, uint runningAppId, bool steamForeground)
	{
		if (!steamRunning) return SteamUiHapticsState.SteamUnavailable;
		if (runningAppId != 0) return SteamUiHapticsState.GameActive;
		return steamForeground ? SteamUiHapticsState.Allowed : SteamUiHapticsState.SteamNotForeground;
	}

	private static uint ReadRunningAppId()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			object? value = key?.GetValue("RunningAppID");
			return value switch
			{
				int number when number > 0 => (uint)number,
				long number when number > 0 && number <= uint.MaxValue => (uint)number,
				string text when uint.TryParse(text, out uint parsed) => parsed,
				_ => 0
			};
		}
		catch
		{
			return 0;
		}
	}
}
