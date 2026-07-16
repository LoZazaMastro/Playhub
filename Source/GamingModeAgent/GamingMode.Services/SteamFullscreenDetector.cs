using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GamingMode.Services;

public static class SteamFullscreenDetector
{
	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	private struct Rect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MonitorInfo
	{
		public uint cbSize;

		public Rect rcMonitor;

		public Rect rcWork;

		public uint dwFlags;

		public static MonitorInfo Create()
		{
			return new MonitorInfo
			{
				cbSize = (uint)Marshal.SizeOf<MonitorInfo>()
			};
		}
	}

	private static readonly HashSet<string> SteamProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "steam", "steamwebhelper" };

	private const uint MonitorDefaultToNearest = 2u;

	public static async Task<bool> WaitForFullscreenAsync(TimeSpan timeout, FileLogger logger)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.Elapsed < timeout)
		{
			if (IsSteamFullscreen())
			{
				logger.Info("Steam fullscreen window detected.");
				return true;
			}
			await Task.Delay(250);
		}
		logger.Info("Steam fullscreen window was not detected before the splash timeout.");
		return false;
	}

	private static bool IsSteamFullscreen()
	{
		bool detected = false;
		EnumWindows(delegate(nint window, nint _)
		{
			if (IsSteamFullscreenWindow(window))
			{
				detected = true;
				return false;
			}
			return true;
		}, 0);
		return detected;
	}

	private static bool IsSteamFullscreenWindow(nint window)
	{
		if (window == 0 || !IsWindowVisible(window) || IsIconic(window))
		{
			return false;
		}
		GetWindowThreadProcessId(window, out var processId);
		if (processId == 0)
		{
			return false;
		}
		try
		{
			using Process process = Process.GetProcessById((int)processId);
			if (!SteamProcesses.Contains(process.ProcessName))
			{
				return false;
			}
		}
		catch
		{
			return false;
		}
		if (!GetWindowRect(window, out var lpRect))
		{
			return false;
		}
		nint hMonitor = MonitorFromWindow(window, 2u);
		MonitorInfo lpmi = MonitorInfo.Create();
		if (!GetMonitorInfo(hMonitor, ref lpmi))
		{
			return false;
		}
		return CoversMonitor(lpRect, lpmi.rcMonitor);
	}

	private static bool CoversMonitor(Rect windowRect, Rect monitorRect)
	{
		int num = 32;
		int num2 = monitorRect.Right - monitorRect.Left;
		int num3 = monitorRect.Bottom - monitorRect.Top;
		int num4 = windowRect.Right - windowRect.Left;
		int num5 = windowRect.Bottom - windowRect.Top;
		bool num6 = (double)num4 >= (double)num2 * 0.9 && (double)num5 >= (double)num3 * 0.9;
		bool flag = windowRect.Left <= monitorRect.Left + num && windowRect.Top <= monitorRect.Top + num && windowRect.Right >= monitorRect.Right - num && windowRect.Bottom >= monitorRect.Bottom - num;
		return num6 && flag;
	}

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint hWnd, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);
}
