using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
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

	public static async Task<bool> WaitForFullscreenAsync(TimeSpan timeout, FileLogger logger, bool suppressDesktopWindows = false)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		TimeSpan graceForRelaxedDetection = TimeSpan.FromMilliseconds(Math.Min(timeout.TotalMilliseconds / 2.0, 15000.0));
		while (stopwatch.Elapsed < timeout)
		{
			bool relaxed = stopwatch.Elapsed >= graceForRelaxedDetection;
			if (IsSteamFullscreen(relaxed))
			{
				logger.Info(relaxed ? "Steam fullscreen window detected (relaxed match)." : "Steam fullscreen window detected.");
				return true;
			}
			if (suppressDesktopWindows)
			{
				SuppressSteamDesktopWindows(logger);
			}
			await Task.Delay(250);
		}
		logger.Info("Steam fullscreen window was not detected before the splash timeout.");
		return false;
	}

	// All'avvio in Gaming Mode la finestra desktop di Steam non deve mai essere
	// visibile: se compare prima della Big Picture viene ridotta a icona finche'
	// la UI gamepad non prende lo schermo.
	private static void SuppressSteamDesktopWindows(FileLogger logger)
	{
		try
		{
			EnumWindows(delegate(nint window, nint _)
			{
				if (window == 0 || !IsWindowVisible(window) || IsIconic(window))
				{
					return true;
				}
				GetWindowThreadProcessId(window, out var processId);
				if (processId == 0)
				{
					return true;
				}
				try
				{
					using Process process = Process.GetProcessById((int)processId);
					if (!SteamProcesses.Contains(process.ProcessName))
					{
						return true;
					}
				}
				catch
				{
					return true;
				}
				string title = GetWindowTitle(window);
				if (string.IsNullOrWhiteSpace(title) || title.Contains("Big Picture", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				ShowWindow(window, 6);
				logger.Info("Steam desktop window \"" + title + "\" was minimized while waiting for Big Picture.");
				return true;
			}, 0);
		}
		catch
		{
		}
	}

	public static void TryFocusSteamWindow(FileLogger logger)
	{
		try
		{
			nint best = 0;
			long bestArea = 0L;
			EnumWindows(delegate(nint window, nint _)
			{
				if (window == 0 || !IsWindowVisible(window))
				{
					return true;
				}
				GetWindowThreadProcessId(window, out var processId);
				if (processId == 0)
				{
					return true;
				}
				try
				{
					using Process process = Process.GetProcessById((int)processId);
					if (!SteamProcesses.Contains(process.ProcessName))
					{
						return true;
					}
				}
				catch
				{
					return true;
				}
				if (!GetWindowRect(window, out var lpRect))
				{
					return true;
				}
				long num = (long)Math.Max(0, lpRect.Right - lpRect.Left) * (long)Math.Max(0, lpRect.Bottom - lpRect.Top);
				if (IsBigPictureTitle(window))
				{
					num += 1000000000L;
				}
				if (num > bestArea)
				{
					bestArea = num;
					best = window;
				}
				return true;
			}, 0);
			if (best != 0)
			{
				if (IsIconic(best))
				{
					ShowWindow(best, 9);
				}
				SetForegroundWindow(best);
				SetWindowPos(best, 0, 0, 0, 0, 0, 83u);
				logger.Info("Steam window was brought to the foreground after the splash screen.");
			}
		}
		catch (Exception exception)
		{
			logger.Error("Could not bring the Steam window to the foreground.", exception);
		}
	}

	private static bool IsSteamFullscreen(bool relaxed)
	{
		bool detected = false;
		EnumWindows(delegate(nint window, nint _)
		{
			if (IsSteamFullscreenWindow(window, relaxed))
			{
				detected = true;
				return false;
			}
			return true;
		}, 0);
		return detected;
	}

	private static bool IsSteamFullscreenWindow(nint window, bool relaxed)
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
		bool isBigPictureTitle = IsBigPictureTitle(window);
		if (isBigPictureTitle)
		{
			return CoversMonitor(lpRect, lpmi.rcMonitor, relaxed);
		}
		if (!relaxed)
		{
			return false;
		}
		return CoversMonitor(lpRect, lpmi.rcMonitor, relaxed: false);
	}

	private static bool IsBigPictureTitle(nint window)
	{
		return GetWindowTitle(window).Contains("Big Picture", StringComparison.OrdinalIgnoreCase);
	}

	private static string GetWindowTitle(nint window)
	{
		int windowTextLength = GetWindowTextLength(window);
		if (windowTextLength <= 0)
		{
			return "";
		}
		StringBuilder stringBuilder = new StringBuilder(windowTextLength + 1);
		if (GetWindowText(window, stringBuilder, stringBuilder.Capacity) <= 0)
		{
			return "";
		}
		return stringBuilder.ToString();
	}

	private static bool CoversMonitor(Rect windowRect, Rect monitorRect, bool relaxed)
	{
		if (relaxed)
		{
			long monitorArea = (long)Math.Max(1, monitorRect.Right - monitorRect.Left) * (long)Math.Max(1, monitorRect.Bottom - monitorRect.Top);
			long windowArea = (long)Math.Max(0, windowRect.Right - windowRect.Left) * (long)Math.Max(0, windowRect.Bottom - windowRect.Top);
			if ((double)windowArea >= (double)monitorArea * 0.7)
			{
				return true;
			}
		}
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

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLength(nint hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);
}
