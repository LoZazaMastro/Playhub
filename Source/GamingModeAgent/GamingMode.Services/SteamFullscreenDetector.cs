using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

public static class SteamFullscreenDetector
{
	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	private const uint SpiGetForegroundLockTimeout = 0x2000;

	private const uint SpiSetForegroundLockTimeout = 0x2001;

	private const uint SpifSendChange = 0x0002;

	private const byte VkMenu = 0x12;

	private const uint KeyeventfKeyup = 0x0002;

	private static readonly nint HwndTopMostLocal = -1;

	private static readonly nint HwndNoTopMostLocal = -2;

	private const uint SwpNoSizeLocal = 0x0001;

	private const uint SwpNoMoveLocal = 0x0002;

	private const uint SwpNoActivateLocal = 0x0010;

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
				// Non minimizzare MAI una finestra a schermo intero: durante il
				// caricamento la Big Picture puo' avere un titolo provvisorio (es.
				// "Playhub") non ancora contenente "Big Picture". Minimizzarla
				// romperebbe l'avvio. Tocchiamo solo le finestre desktop piccole.
				if (GetWindowRect(window, out var lpRect))
				{
					nint hMonitor = MonitorFromWindow(window, 2u);
					MonitorInfo lpmi = MonitorInfo.Create();
					if (GetMonitorInfo(hMonitor, ref lpmi) && CoversMonitor(lpRect, lpmi.rcMonitor, relaxed: true))
					{
						return true;
					}
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
			// La Big Picture puo' impiegare qualche istante a creare la sua
			// finestra con input attivo: riproviamo per un breve periodo finche'
			// il foreground reale non e' Steam (non basta lo Z-order: senza focus
			// di input il controller e la tastiera non rispondono).
			// Senza "-silent" la Big Picture prende il focus da sola: nella
			// maggior parte dei casi al primo giro e' gia' in primo piano e non
			// facciamo nulla. Come rete di sicurezza, se non lo fosse, proviamo a
			// portarla in foreground SENZA input sintetici (niente click: non deve
			// saltare il video introduttivo della Big Picture).
			for (int attempt = 0; attempt < 12; attempt++)
			{
				if (IsForegroundSteam())
				{
					return;
				}
				nint best = FindBestSteamWindow();
				if (best != 0)
				{
					if (IsIconic(best))
					{
						ShowWindow(best, 9);
					}
					ForceForegroundWindow(best);
					if (IsForegroundSteam())
					{
						logger.Info("Steam window received input focus after the splash screen.");
						return;
					}
				}
				Thread.Sleep(150);
			}
			logger.Info("Steam window was not confirmed as foreground; no synthetic input was sent.");
		}
		catch (Exception exception)
		{
			logger.Error("Could not bring the Steam window to the foreground.", exception);
		}
	}

	private static nint FindBestSteamWindow()
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
		return best;
	}

	private static bool IsForegroundSteam()
	{
		nint foreground = GetForegroundWindow();
		if (foreground == 0)
		{
			return false;
		}
		GetWindowThreadProcessId(foreground, out var processId);
		if (processId == 0)
		{
			return false;
		}
		try
		{
			using Process process = Process.GetProcessById((int)processId);
			return SteamProcesses.Contains(process.ProcessName);
		}
		catch
		{
			return false;
		}
	}

	// Porta una finestra in primo piano AGGIRANDO il foreground lock di Windows.
	// SetForegroundWindow da solo, quando l'agente e' la shell e non c'e' input
	// recente, viene rifiutato: la finestra sale nello Z-order ma NON riceve il
	// focus di input (controller/tastiera morti finche' non si clicca col mouse).
	// Combiniamo i trucchi noti: azzeramento del timeout di foreground-lock, tap
	// virtuale di ALT per sbloccare la coda, AllowSetForegroundWindow e
	// AttachThreadInput sul thread attualmente in foreground.
	private static void ForceForegroundWindow(nint window)
	{
		try
		{
			uint targetThread = GetWindowThreadProcessId(window, out _);
			nint foreground = GetForegroundWindow();
			uint foregroundThread = (foreground != 0) ? GetWindowThreadProcessId(foreground, out _) : 0u;
			uint currentThread = GetCurrentThreadId();

			// 1) Disattiva temporaneamente il timeout di foreground lock.
			nint previousTimeout = 0;
			bool timeoutRead = SystemParametersInfo(SpiGetForegroundLockTimeout, 0, ref previousTimeout, 0);
			SystemParametersInfoSet(SpiSetForegroundLockTimeout, 0, 0, SpifSendChange);

			// 2) Tap virtuale di ALT: sblocca la restrizione di foreground.
			keybd_event(VkMenu, 0, 0, 0);
			keybd_event(VkMenu, 0, KeyeventfKeyup, 0);

			AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY

			// 3) Attacca l'input del thread in foreground per ottenere il diritto.
			bool attachedForeground = foregroundThread != 0 && foregroundThread != currentThread && AttachThreadInput(currentThread, foregroundThread, true);
			bool attachedTarget = targetThread != 0 && targetThread != currentThread && AttachThreadInput(currentThread, targetThread, true);

			ShowWindow(window, 9); // SW_RESTORE
			BringWindowToTop(window);
			SetForegroundWindow(window);
			SetActiveWindow(window);
			SetFocus(window);

			// Porta in cima (senza restare topmost) e poi rilascia il topmost.
			SetWindowPos(window, HwndTopMostLocal, 0, 0, 0, 0, SwpNoMoveLocal | SwpNoSizeLocal | SwpNoActivateLocal);
			SetWindowPos(window, HwndNoTopMostLocal, 0, 0, 0, 0, SwpNoMoveLocal | SwpNoSizeLocal | SwpNoActivateLocal);

			if (attachedTarget)
			{
				AttachThreadInput(currentThread, targetThread, false);
			}
			if (attachedForeground)
			{
				AttachThreadInput(currentThread, foregroundThread, false);
			}

			// 4) Ripristina il timeout originale.
			if (timeoutRead)
			{
				SystemParametersInfoSet(SpiSetForegroundLockTimeout, 0, previousTimeout, SpifSendChange);
			}
		}
		catch
		{
			SetForegroundWindow(window);
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

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll")]
	private static extern nint SetActiveWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern nint SetFocus(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll")]
	private static extern bool AllowSetForegroundWindow(uint dwProcessId);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nint dwExtraInfo);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref nint pvParam, uint fWinIni);

	[DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
	private static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);
}
