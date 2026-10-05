using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

public sealed class GamingWindowFocusService : IDisposable
{
	private readonly record struct LaunchCurtainWindow(nint Handle, bool IsPrimaryOverlay);

	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	private struct Rect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private readonly record struct AppliedWindowState(Rect Rect, long Style, long ExStyle, bool IsSteamGame);

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

	private static readonly HashSet<string> IgnoredProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"GamingMode", "GamingModeSetup", "explorer", "steam", "steamwebhelper", "sunshine", "apollo", "vibepollo", "vibeshine", "PluginLoader", "PluginLoader_noconsole",
		"powershell", "pwsh", "cmd", "conhost", "WindowsTerminal", "yt-dlp", "ffmpeg", "ffprobe", "curl", "wget"
	};

	private static readonly ConcurrentDictionary<uint, (string Name, long Timestamp)> ProcessNameCache = new ConcurrentDictionary<uint, (string, long)>();

	private const long ProcessNameCacheTtlMs = 5000L;

	private static readonly string[] IgnoredTitleFragments = new string[1] { "Launch Curtain" };

	private readonly FileLogger _logger;

	private readonly Func<int, bool> _isSteamGame;

	private readonly object _sync = new object();

	private readonly ConcurrentDictionary<nint, AppliedWindowState> _appliedWindows = new ConcurrentDictionary<nint, AppliedWindowState>();

	private CancellationTokenSource? _cancellation;

	private Task? _worker;

	private volatile bool _applyBorderlessFullscreen = true;

	private volatile bool _launchCurtainPriorityActive;

	private int _steamFocusRecoveryGeneration;

	private nint _lastForegroundSteamGameWindow;

	private readonly ConcurrentDictionary<nint, uint> _gameWindows = new();
	private readonly HashSet<nint> _pendingLaunchFocus = new();
	private bool _initialScan;

	/// <summary>
	/// True mentre a schermo c'e' una schermata di avvio di Launch Curtain.
	/// Chi porta avanti finestre deve rispettarla e non intromettersi.
	/// </summary>
	public bool IsLaunchCurtainOnScreen => _launchCurtainPriorityActive;

	public int SteamFocusRecoveryVersion => Volatile.Read(ref _steamFocusRecoveryGeneration);

	private const int GwlStyle = -16;

	private const int GwlExStyle = -20;

	private const long WsVisible = 268435456L;

	private const long WsDisabled = 134217728L;

	private const long WsChild = 1073741824L;

	private const long WsCaption = 12582912L;

	private const long WsThickFrame = 262144L;

	private const long WsMinimizeBox = 131072L;

	private const long WsMaximizeBox = 65536L;

	private const long WsSysMenu = 524288L;

	private const long WsExDlgModalFrame = 1L;

	private const long WsExClientEdge = 512L;

	private const long WsExStaticEdge = 131072L;

	private const uint SwpNoSize = 1u;

	private const uint SwpNoMove = 2u;

	private const uint SwpNoZOrder = 4u;

	private const uint SwpNoActivate = 16u;

	private const uint SwpNoOwnerZOrder = 512u;

	private const uint SwpFrameChanged = 32u;

	private const uint SwpShowWindow = 64u;

	private const uint MonitorDefaultToNearest = 2u;

	private static readonly nint HwndTopMost = -1;

	private static readonly nint HwndTop = 0;

	public bool Running
	{
		get
		{
			lock (_sync)
			{
				Task worker = _worker;
				return worker != null && !worker.IsCompleted;
			}
		}
	}

	public GamingWindowFocusService(FileLogger logger) : this(logger, pid => OverlaySteamArtworkResolver.IsSteamGameProcess(pid)
		|| UwpSteamSessionAssociation.IsTrackedGame(pid)) { }

	internal GamingWindowFocusService(FileLogger logger, Func<int, bool> isSteamGame)
	{
		_logger = logger;
		_isSteamGame = isSteamGame;
	}

	public void Start(bool applyBorderlessFullscreen = true)
	{
		lock (_sync)
		{
			_applyBorderlessFullscreen = applyBorderlessFullscreen;
			Task worker = _worker;
			if (worker == null || worker.IsCompleted)
			{
				_cancellation = new CancellationTokenSource();
				_initialScan = true;
				var token = _cancellation.Token;
				_worker = Task.Run(() => RunAsync(token));
				_logger.Info("Gaming window focus service started.");
			}
		}
	}

	public void Stop()
	{
		CancellationTokenSource cancellation;
		Task worker;
		lock (_sync)
		{
			cancellation = _cancellation;
			worker = _worker;
			_cancellation = null;
			_worker = null;
			_appliedWindows.Clear();
			_gameWindows.Clear();
			ProcessNameCache.Clear();
			_launchCurtainPriorityActive = false;
			_lastForegroundSteamGameWindow = 0;
		}
		if (cancellation == null)
		{
			return;
		}
		try
		{
			cancellation.Cancel();
			worker?.Wait(TimeSpan.FromMilliseconds(500.0));
		}
		catch
		{
		}
		finally
		{
			cancellation.Dispose();
			_logger.Info("Gaming window focus service stopped.");
		}
	}

	public void Dispose()
	{
		Stop();
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		using var changes = new SemaphoreSlim(0, 1);
		using var observer = new WindowLifecycleObserver(() =>
		{
			try { if (changes.CurrentCount == 0) changes.Release(); }
			catch (SemaphoreFullException) { }
			catch (ObjectDisposedException) { }
		});
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				try { ApplyToCandidateWindows(); }
				catch (Exception exception) { _logger.Error("Could not reconcile game windows.", exception); }
				// Events handle launches/closures immediately; the slow fallback covers
				// a late Steam tracking-log update or an unavailable accessibility hook.
				await changes.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
				await Task.Delay(150, cancellationToken); // coalesce one window's create/show/foreground burst
				while (changes.Wait(0)) { }
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
		finally { _pendingLaunchFocus.Clear(); }
	}

	private bool ApplyToCandidateWindows()
	{
		IReadOnlyList<nint> readOnlyList = EnumerateWindows();
		HashSet<nint> seen = new HashSet<nint>();
		List<LaunchCurtainWindow> list = new List<LaunchCurtainWindow>();
		foreach (nint item in readOnlyList)
		{
			seen.Add(item);
			if (TryGetLaunchCurtainWindow(item, out var launchCurtainWindow))
			{
				list.Add(launchCurtainWindow);
			}
		}
		bool flag = list.Count > 0;
		if (flag)
		{
			foreach (LaunchCurtainWindow item2 in list.Where((LaunchCurtainWindow window) => !window.IsPrimaryOverlay))
			{
				PrioritizeLaunchCurtainWindow(item2.Handle);
			}
			foreach (LaunchCurtainWindow item3 in list.Where((LaunchCurtainWindow window) => window.IsPrimaryOverlay))
			{
				PrioritizeLaunchCurtainWindow(item3.Handle);
			}
		}
		if (flag != _launchCurtainPriorityActive)
		{
			_launchCurtainPriorityActive = flag;
			// Chi porta avanti altre finestre deve poter sapere che in questo
			// momento c'e' una schermata di avvio a video, e stare fermo.
			_logger.Info(flag ? "Launch Curtain priority mode active." : "Launch Curtain priority mode released.");
		}
		var processIdentity = new Dictionary<uint, bool>();
		foreach (nint window in readOnlyList)
		{
			try
			{
				if (!IsCandidateWindow(window, out uint processId, out string processName)) continue;
				if (!processIdentity.TryGetValue(processId, out bool isGame))
					processIdentity[processId] = isGame = _isSteamGame((int)processId);
				if (!isGame) continue;
				bool firstSeen = _gameWindows.TryAdd(window, processId);
				if (firstSeen && !_initialScan && (flag || OverlayWindowTools.IsSteamForeground()))
					_pendingLaunchFocus.Add(window);
				if (!flag && _applyBorderlessFullscreen) ApplyToWindow(window);
			}
			catch (Exception exception) { _logger.Error($"Could not track game window {window}.", exception); }
		}
		_initialScan = false;
		if (!flag && _pendingLaunchFocus.Count > 0)
		{
			// One foreground handoff on launch. Never force a game back over an
			// overlay or over an application the user deliberately switched to.
			if (OverlayWindowTools.IsSteamForeground())
			{
				nint candidate = readOnlyList.FirstOrDefault(window => _pendingLaunchFocus.Contains(window)
					&& _gameWindows.ContainsKey(window) && IsWindowVisible(window) && !IsIconic(window));
				if (candidate != 0) OverlayWindowTools.Activate(candidate);
			}
			_pendingLaunchFocus.Clear();
		}
		nint foregroundWindow = GetForegroundWindow();
		if (_gameWindows.ContainsKey(foregroundWindow)) _lastForegroundSteamGameWindow = foregroundWindow;
		// Hidden UWP frames can survive process exit; an iconic window, however,
		// is only minimized and must not be treated as a finished game.
		nint[] removed = _gameWindows.Keys.Where(window => !seen.Contains(window)
			|| !IsWindowVisible(window) || IsCloaked(window)).ToArray();
		bool removedForegroundSteamGame = _lastForegroundSteamGameWindow != 0 && removed.Contains(_lastForegroundSteamGameWindow);
		foreach (nint window in removed)
		{
			_gameWindows.TryRemove(window, out _);
			_appliedWindows.TryRemove(window, out _);
			_pendingLaunchFocus.Remove(window);
		}
		if (removedForegroundSteamGame) _lastForegroundSteamGameWindow = 0;
		if (!flag && removedForegroundSteamGame && _gameWindows.IsEmpty) QueueSteamFocusRecovery();

		return flag;
	}

	public bool TryGetQamGameProcess(nint window, out uint processId)
	{
		processId = 0;
		return _gameWindows.TryGetValue(window, out uint tracked) && TryGetWindowProcess(window, out processId, out _)
			&& processId == tracked && IsWindowVisible(window) && !IsCloaked(window);
	}

	public bool TryGetQamSource(out nint window, out uint processId)
	{
		window = GetForegroundWindow();
		if (TryGetQamGameProcess(window, out processId)) return true;
		if (!OverlayWindowTools.IsSteamForeground()) { window = 0; return false; }
		window = _lastForegroundSteamGameWindow;
		return TryGetQamGameProcess(window, out processId);
	}

	private void QueueSteamFocusRecovery()
	{
		int generation = Interlocked.Increment(ref _steamFocusRecoveryGeneration);
		CancellationToken token = _cancellation?.Token ?? CancellationToken.None;
		_ = Task.Run(async delegate
		{
			try
			{
				// Steam aggiorna lo stato della sessione qualche istante dopo che la
				// finestra del gioco scompare. Aspettare qui evita di contendere il
				// foreground durante la chiusura e non blocca il watcher principale.
				await Task.Delay(260, token);
				for (int attempt = 1; attempt <= 3; attempt++)
				{
					if (generation != Volatile.Read(ref _steamFocusRecoveryGeneration)
						|| token.IsCancellationRequested || _launchCurtainPriorityActive || !_gameWindows.IsEmpty
						|| !CanRecoverSteamFocus())
					{
						return;
					}

					bool activated = OverlayWindowTools.ActivateSteam(out string report);
					_logger.Info($"Post-game Steam focus recovery {attempt}/3: {report}");
					if (activated && OverlayWindowTools.IsSteamForeground())
					{
						return;
					}
					await Task.Delay(220, token);
				}
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				_logger.Error("Post-game Steam focus recovery failed.", exception);
			}
		}, token);
	}

	private void PrioritizeLaunchCurtainWindow(nint window)
	{
		try
		{
			// La curtain puo' essersi nascosta dopo l'enumerazione: niente SWP_SHOWWINDOW,
			// altrimenti il riordino rende di nuovo visibile la copertura nera.
			SetWindowPos(window, HwndTopMost, 0, 0, 0, 0, 531u);
		}
		catch (Exception exception)
		{
			_logger.Error($"Failed to prioritize Launch Curtain window {window}.", exception);
		}
	}

	private static bool TryGetLaunchCurtainWindow(nint window, out LaunchCurtainWindow launchCurtainWindow)
	{
		launchCurtainWindow = default(LaunchCurtainWindow);
		if (window == 0 || !IsWindowVisible(window) || IsIconic(window) || IsCloaked(window))
		{
			return false;
		}
		string title = GetWindowTitle(window);
		if (!IgnoredTitleFragments.Any((string fragment) => title.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		if (!TryGetWindowProcess(window, out uint _, out string processName) || !IsLaunchCurtainHostProcess(processName))
		{
			return false;
		}
		launchCurtainWindow = new LaunchCurtainWindow(window, !title.Contains("Black Cover", StringComparison.OrdinalIgnoreCase));
		return true;
	}

	private static bool IsLaunchCurtainHostProcess(string processName)
	{
		if (!processName.Equals("powershell", StringComparison.OrdinalIgnoreCase))
		{
			return processName.Equals("pwsh", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private void ApplyToWindow(nint window)
	{
		if (!_gameWindows.ContainsKey(window) || !IsCandidateWindow(window, out uint processId, out string processName))
		{
			return;
		}
		FullscreenWindowGeometry.Apply(() =>
		{
			nint hMonitor = MonitorFromWindow(window, 2u);
			MonitorInfo lpmi = MonitorInfo.Create();
			if (!GetMonitorInfo(hMonitor, ref lpmi) || !GetWindowRect(window, out Rect current)) return null;
			Rect monitor = lpmi.rcMonitor;
			return new FullscreenWindowBounds(
				new PhysicalWindowRect(current.Left, current.Top, current.Right, current.Bottom),
				new PhysicalWindowRect(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom));
		}, bounds =>
		{
			long num = ((IntPtr)GetWindowLongPtr(window, -16)).ToInt64() & -13565953;
			long num2 = ((IntPtr)GetWindowLongPtr(window, -20)).ToInt64() & -131586;
			PhysicalWindowRect monitor = bounds.Monitor;
			Rect rcMonitor = new() { Left = monitor.Left, Top = monitor.Top, Right = monitor.Right, Bottom = monitor.Bottom };
			bool isSteamGame = _gameWindows.ContainsKey(window);
			AppliedWindowState appliedWindowState = new AppliedWindowState(rcMonitor, num, num2, isSteamGame);
			// A window that has returned to windowed mode needs recovery even if its styles
			// match an earlier application. Actual physical bounds are the authority.
			SetWindowLongPtr(window, -16, new IntPtr(num));
			SetWindowLongPtr(window, -20, new IntPtr(num2));
			SetWindowPos(window, HwndTop, rcMonitor.Left, rcMonitor.Top, rcMonitor.Right - rcMonitor.Left, rcMonitor.Bottom - rcMonitor.Top, 628u);
			_appliedWindows[window] = appliedWindowState;
			_logger.Info($"Applied borderless fullscreen to {processName} ({processId}).");
		});
	}

	private static bool IsCandidateWindow(nint window, out uint processId, out string processName)
	{
		processId = 0u;
		processName = "";
		if (window == 0 || !IsWindowVisible(window) || IsIconic(window) || IsCloaked(window))
		{
			return false;
		}
		if (!GetWindowRect(window, out var lpRect))
		{
			return false;
		}
		int num = lpRect.Right - lpRect.Left;
		int num2 = lpRect.Bottom - lpRect.Top;
		if (num < 220 || num2 < 120)
		{
			return false;
		}
		string title = GetWindowTitle(window);
		if (IgnoredTitleFragments.Any((string fragment) => title.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
		{
			return false;
		}
		if (!TryGetWindowProcess(window, out processId, out processName))
		{
			return false;
		}
		if (SteamGameIdentityPolicy.IsSteamClient(processName) || SteamGameIdentityPolicy.IsXboxLaunchHelper(processName)
			|| FullscreenWindowGeometry.IsOverlayHostProcess(processName) || IgnoredProcesses.Contains(processName))
		{
			return false;
		}
		long num3 = ((IntPtr)GetWindowLongPtr(window, -16)).ToInt64();
		long exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
		StringBuilder className = new(128);
		GetClassName(window, className, className.Capacity);
		nint owner = GetWindow(window, 4); // GW_OWNER; rendering windows may also have an owner.
		if (IsAuxiliaryWindow(exStyle, className.ToString(), owner != 0 && !IsWindowEnabled(owner)))
		{
			return false;
		}
		if ((num3 & 0x40000000) == 0L && (num3 & 0x8000000) == 0L)
		{
			return (num3 & 0x10000000) != 0;
		}
		return false;
	}

	internal static bool IsAuxiliaryWindow(long exStyle, string className, bool disabledOwner)
	{
		// Do not expand update prompts, modal dialogs or utility windows over a game.
		// Ownership alone is not enough: emulators can use owned top-level render windows.
		const long toolWindow = 0x80L;
		const long noActivate = 0x08000000L;
		return (exStyle & (WsExDlgModalFrame | toolWindow | noActivate)) != 0
			|| className == "#32770" || disabledOwner;
	}

	private static bool TryGetWindowProcess(nint window, out uint processId, out string processName)
	{
		processId = 0u;
		processName = "";
		processId = (uint)OverlayWindowTools.WindowProcessId(window);
		if (processId == 0)
		{
			return false;
		}
		long now = Environment.TickCount64;
		if (ProcessNameCache.TryGetValue(processId, out var cached) && now - cached.Timestamp < ProcessNameCacheTtlMs)
		{
			processName = cached.Name;
			return true;
		}
		try
		{
			using Process process = Process.GetProcessById((int)processId);
			processName = process.ProcessName;
		}
		catch
		{
			return false;
		}
		if (ProcessNameCache.Count > 512)
		{
			foreach (KeyValuePair<uint, (string Name, long Timestamp)> entry in ProcessNameCache)
			{
				if (now - entry.Value.Timestamp >= ProcessNameCacheTtlMs)
				{
					ProcessNameCache.TryRemove(entry.Key, out _);
				}
			}
		}
		ProcessNameCache[processId] = (processName, now);
		return true;
	}

	private static bool IsCloaked(nint window)
		=> DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

	private static bool CanRecoverSteamFocus()
	{
		nint foreground = GetForegroundWindow();
		if (foreground == 0 || !IsWindowVisible(foreground) || IsCloaked(foreground)) return true;
		if (OverlayWindowTools.IsSteamForeground()) return true;
		StringBuilder className = new(128);
		GetClassName(foreground, className, className.Capacity);
		return className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
	[DllImport("user32.dll")]
	private static extern bool EnumChildWindows(nint window, EnumWindowsProc callback, nint parameter);

	private static IReadOnlyList<nint> EnumerateWindows()
	{
		return OverlayWindowTools.EnumerateTopLevelHandles();
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

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll")]
	private static extern nint GetWindow(nint window, uint command);

	[DllImport("user32.dll")]
	private static extern bool IsWindowEnabled(nint window);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint window, StringBuilder text, int count);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLength(nint hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint hWnd, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);
}
