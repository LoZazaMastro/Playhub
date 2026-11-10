using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Playhub.DeckyHost;

namespace GamingMode.Services;

// SCHERMO NERO CON L'AUDIO DOPO AVER ACCESO LA TV.
//
// Se il PC parte in Gaming Mode con la TV spenta (in standby), Windows vede
// comunque il monitor ma in SDR a 8 bit; Steam crea la Big Picture su quella
// superficie. Quando la TV si accende Windows ricrea il monitor e passa in HDR
// a 10 bit: la Big Picture continua a funzionare (si sentono i suoni) ma la sua
// superficie grafica non viene ricreata e sullo schermo resta il nero.
// Report del 21/09/2026: avvio con la TV spenta = display SDR 8 bit, avvio con
// la TV accesa = HDR 10 bit, stesso 3840x2160 a 120 Hz.
//
// Questo servizio osserva la configurazione dei monitor. Quando cambia davvero
// (monitor ricreato, comparso, sparito, o stato HDR diverso) e la Big Picture
// e' in primo piano, riavvia il solo processo grafico di Steam: Chromium lo
// rilancia da solo e ridisegna tutto su una superficie nuova. Steam non si
// chiude, la sessione e i giochi non vengono toccati.
public sealed class SteamDisplayRecoveryService : IDisposable
{
	private static readonly TimeSpan IdleFallbackInterval = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan PendingInterval = TimeSpan.FromMilliseconds(1500);
	private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(2500);
	private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);
	private static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(20);
	private static readonly string[] CoverWindowTitles = { "Launch Curtain Black Cover", "Launch Curtain" };

	private static readonly TimeSpan GpuRestartWindow = TimeSpan.FromHours(1);
	private const int MaxGpuRestartsPerWindow = 2;

	private readonly List<DateTime> _gpuRestarts = new();
	private readonly FileLogger _logger;
	private readonly Func<bool> _isGamingMode;
	private readonly Func<string?> _readSteamExecutable;
	private readonly object _sync = new();
	private CancellationTokenSource? _cancellation;
	private Task? _worker;
	private readonly BackgroundWorkSignal _changes = new();
	private bool _eventsSubscribed;
	private long _snapshotCount;
	internal long SnapshotCount => Interlocked.Read(ref _snapshotCount);

	public SteamDisplayRecoveryService(FileLogger logger, Func<bool> isGamingMode, Func<string?> readSteamExecutable)
	{
		_logger = logger;
		_isGamingMode = isGamingMode;
		_readSteamExecutable = readSteamExecutable;
	}

	public void Start()
	{
		lock (_sync)
		{
			if (_worker != null && !_worker.IsCompleted) return;
			_cancellation = new CancellationTokenSource();
			CancellationToken token = _cancellation.Token;
			try
			{
				SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
				SystemEvents.PowerModeChanged += OnPowerModeChanged;
				_eventsSubscribed = true;
			}
			catch (Exception exception)
			{
				SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
				SystemEvents.PowerModeChanged -= OnPowerModeChanged;
				_logger.Error("Display notifications unavailable; using bounded fallback.", exception);
			}
			_worker = Task.Run(() => RunAsync(token));
		}
	}

	public void Stop()
	{
		CancellationTokenSource? cancellation;
		Task? worker;
		lock (_sync)
		{
			cancellation = _cancellation;
			worker = _worker;
			_cancellation = null;
			if (_eventsSubscribed)
			{
				SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
				SystemEvents.PowerModeChanged -= OnPowerModeChanged;
				_eventsSubscribed = false;
			}
			_worker = null;
		}
		if (cancellation == null) return;
		try
		{
			cancellation.Cancel();
			worker?.Wait(TimeSpan.FromMilliseconds(500));
		}
		catch
		{
		}
		finally
		{
			cancellation.Dispose();
		}
	}

	public void Dispose() => Stop();

	private void OnDisplaySettingsChanged(object? sender, EventArgs args) => _changes.Notify();
	private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
	{
		if (args.Mode == PowerModes.Resume) _changes.Notify();
	}

	private async Task RunAsync(CancellationToken token)
	{
		Interlocked.Increment(ref _snapshotCount);
		DisplaySnapshot baseline = Capture() ?? DisplaySnapshot.Empty;
		_logger.Info("Display watch started: " + baseline.Describe() + ".");
		DisplaySnapshot? candidate = null;
		DateTime candidateSince = DateTime.MinValue;
		DisplayChangeKind pending = DisplayChangeKind.None;
		DateTime pendingSince = DateTime.MinValue;
		DateTime lastRecovery = DateTime.MinValue;
		bool waitingLogged = false;

		while (!token.IsCancellationRequested)
		{
			try
			{
				await _changes.WaitAsync(candidate is not null || pending != DisplayChangeKind.None
					? PendingInterval : IdleFallbackInterval, token);
			}
			catch (OperationCanceledException)
			{
				break;
			}

			try
			{
				Interlocked.Increment(ref _snapshotCount);
				DisplaySnapshot? current = Capture();
				if (current == null) continue;
				DateTime now = DateTime.UtcNow;

				if (!current.SameAs(baseline))
				{
					// Aspetta che la configurazione smetta di muoversi: accendendo
					// una TV Windows attraversa piu' stati in pochi secondi.
					if (candidate == null || !current.SameAs(candidate))
					{
						candidate = current;
						candidateSince = now;
						continue;
					}
					if (now - candidateSince < SettleTime) continue;

					DisplayChangeKind kind = DisplayChangePolicy.Classify(baseline, current);
					_logger.Info($"Display configuration changed ({kind}): prima {baseline.Describe()}; ora {current.Describe()}.");
					baseline = current;
					candidate = null;
					if (DisplayChangePolicy.NeedsRecovery(kind))
					{
						pending = DisplayChangePolicy.Strongest(pending, kind);
						pendingSince = now;
						waitingLogged = false;
					}
				}
				else
				{
					candidate = null;
				}

				if (pending == DisplayChangeKind.None) continue;

				if (now - pendingSince > PendingLifetime)
				{
					_logger.Info("Display recovery dropped: Steam was not in the foreground within 10 minutes.");
					pending = DisplayChangeKind.None;
					continue;
				}
				if (!_isGamingMode())
				{
					pending = DisplayChangeKind.None;
					continue;
				}
				// Nessun monitor: non c'e' niente su cui ridisegnare. Si aspetta
				// che ne arrivi uno (sara' un nuovo hotplug).
				if (current.Monitors.Count == 0) continue;
				var owner = ReadRecoveryOwner();
				if (owner is null)
				{
					if (!waitingLogged)
					{
						_logger.Info("Display recovery waiting for a verified idle Steam foreground.");
						waitingLogged = true;
					}
					continue;
				}
				if (now - lastRecovery < MinimumGap) continue;

				bool? black = SampleScreenBlack();
				if (!DisplayChangePolicy.ShouldRecover(pending, black))
				{
					_logger.Info($"Display recovery not needed ({pending}): Steam is drawing (black={Describe(black)}).");
					pending = DisplayChangeKind.None;
					continue;
				}

				DisplayChangeKind reason = pending;
				pending = DisplayChangeKind.None;
				lastRecovery = now;
				await RecoverAsync(reason, black, owner, token);
				lastRecovery = DateTime.UtcNow;
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception exception)
			{
				_logger.Error("Display watch iteration failed.", exception);
			}
		}
	}

	private async Task RecoverAsync(DisplayChangeKind reason, bool? blackBefore, DeckyProcessTree.Identity owner, CancellationToken token)
	{
		_logger.Info($"Display recovery started ({reason}, black={Describe(blackBefore)}).");
		DateTime now = DateTime.UtcNow;
		_gpuRestarts.RemoveAll(time => now - time > GpuRestartWindow);
		bool StillSafe() => !token.IsCancellationRequested && ReadRecoveryOwner() == owner;
		var outcome = await RecoverWhileSafeAsync(StillSafe, RestartGpu,
			() => RestartHelper(owner, DisplayChangePolicy.IsBrowserProcess, StillSafe),
			SampleScreenBlack, Task.Delay, token);
		if (outcome == RecoveryOutcome.BrowserRestarted) _gpuRestarts.Clear();
		_logger.Info($"Display recovery result: {outcome}.");

		bool RestartGpu()
		{
			if (_gpuRestarts.Count >= MaxGpuRestartsPerWindow) return false;
			if (!RestartHelper(owner, DisplayChangePolicy.IsGpuProcess, StillSafe)) return false;
			_gpuRestarts.Add(now);
			return true;
		}
	}

	internal enum RecoveryOutcome { ContextChanged, GpuRestarted, BrowserRestarted, NoVerifiedHelper, ScreenUnconfirmed }

	internal static async Task<RecoveryOutcome> RecoverWhileSafeAsync(Func<bool> stillSafe, Func<bool> restartGpu,
		Func<bool> restartBrowser, Func<bool?> readBlack, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		if (!stillSafe()) return RecoveryOutcome.ContextChanged;
		bool gpuRestarted = restartGpu();
		if (gpuRestarted) await delay(TimeSpan.FromSeconds(8), token);
		token.ThrowIfCancellationRequested();
		if (!stillSafe()) return RecoveryOutcome.ContextChanged;
		// Never escalate to the browser when there is no fresh evidence of a black screen.
		if (readBlack() != true) return gpuRestarted ? RecoveryOutcome.GpuRestarted : RecoveryOutcome.ScreenUnconfirmed;
		if (!stillSafe()) return RecoveryOutcome.ContextChanged;
		return restartBrowser() ? RecoveryOutcome.BrowserRestarted : RecoveryOutcome.NoVerifiedHelper;
	}

	private DeckyProcessTree.Identity? ReadRecoveryOwner()
	{
		if (!_isGamingMode() || _readSteamExecutable() is not string executable) return null;
		executable = Path.GetFullPath(executable);
		nint foreground = GetForegroundWindow();
		if (foreground == 0 || GetWindowThreadProcessId(foreground, out uint pid) == 0) return null;
		var owner = FindSteamOwner(DeckyProcessTree.Read((int)pid), executable, DeckyProcessTree.Read);
		using var currentUser = WindowsIdentity.GetCurrent();
		if (owner is null || owner.Sid != currentUser.User?.Value) return null;
		bool? active = OverlaySteamArtworkResolver.HasActiveGameForRecovery(Path.GetDirectoryName(owner.Path)!,
			DateTime.FromFileTime(owner.Birth));
		return active == false && _isGamingMode() && GetForegroundWindow() == foreground &&
			GetWindowThreadProcessId(foreground, out uint currentPid) != 0 && currentPid == pid ? owner : null;
	}

	internal static DeckyProcessTree.Identity? FindSteamOwner(DeckyProcessTree.Identity? process, string executable,
		Func<int, DeckyProcessTree.Identity?> read)
	{
		string cefRoot = Path.Combine(Path.GetDirectoryName(executable)!, "bin", "cef") + Path.DirectorySeparatorChar;
		if (!Path.GetFileName(executable).Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) return null;
		for (int hop = 0; process is not null && hop < 4; hop++)
		{
			if (process.Path.Equals(executable, StringComparison.OrdinalIgnoreCase)) return process;
			if (!process.Path.StartsWith(cefRoot, StringComparison.OrdinalIgnoreCase) ||
				!Path.GetFileName(process.Path).Equals("steamwebhelper.exe", StringComparison.OrdinalIgnoreCase)) return null;
			var parent = read(process.ParentPid);
			if (parent is null || parent.Pid == process.Pid || parent.Birth > process.Birth ||
				parent.Sid != process.Sid || parent.Session != process.Session) return null;
			process = parent;
		}
		return null;
	}

	private static bool RestartHelper(DeckyProcessTree.Identity owner, Func<string?, bool> match, Func<bool> stillSafe)
	{
		var helpers = Process.GetProcessesByName("steamwebhelper");
		try
		{
			Process? selected = null;
			DeckyProcessTree.Identity? selectedIdentity = null;
			foreach (var helper in helpers)
			{
				try
				{
					var identity = DeckyProcessTree.Read(helper.SafeHandle, helper.Id);
					if (FindSteamOwner(identity, owner.Path, DeckyProcessTree.Read) != owner ||
						!match(ReadCommandLine(helper.SafeHandle.DangerousGetHandle()))) continue;
					if (selected is not null) return false;
					selected = helper;
					selectedIdentity = identity;
				}
				catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
				{
					// Processes can exit while Chromium is rebuilding its UI.
				}
			}
			// Recheck both context and identity immediately before terminating the retained handle.
			return selected is not null && stillSafe() && DeckyProcessTree.Read(selected.SafeHandle, selected.Id) == selectedIdentity &&
				TerminateProcess(selected.SafeHandle, uint.MaxValue);
		}
		finally
		{
			foreach (var helper in helpers) helper.Dispose();
		}
	}

	private static string Describe(bool? value) => value.HasValue ? (value.Value ? "si" : "no") : "non verificabile";

	// ------------------------------------------------------------------
	// Lettura della configurazione dei monitor.
	// ------------------------------------------------------------------

	internal static DisplaySnapshot? Capture()
	{
		try
		{
			Dictionary<string, (bool? Hdr, int Bits)> color = ReadAdvancedColor();
			var monitors = new List<DisplayMonitorEntry>();
			MonitorEnumProc callback = (nint monitor, nint hdc, ref Rect rect, nint data) =>
			{
				MonitorInfoEx info = MonitorInfoEx.Create();
				if (GetMonitorInfoW(monitor, ref info))
				{
					string device = info.szDevice ?? "";
					color.TryGetValue(device, out var state);
					monitors.Add(new DisplayMonitorEntry(
						device,
						monitor,
						info.rcMonitor.Right - info.rcMonitor.Left,
						info.rcMonitor.Bottom - info.rcMonitor.Top,
						state.Hdr,
						state.Bits));
				}
				return true;
			};
			if (!EnumDisplayMonitors(0, 0, callback, 0)) return null;
			GC.KeepAlive(callback);
			return new DisplaySnapshot(monitors);
		}
		catch
		{
			return null;
		}
	}

	private static Dictionary<string, (bool? Hdr, int Bits)> ReadAdvancedColor()
	{
		var result = new Dictionary<string, (bool? Hdr, int Bits)>(StringComparer.OrdinalIgnoreCase);
		try
		{
			for (int attempt = 0; attempt < 3; attempt++)
			{
				if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return result;
				var paths = new DisplayConfigPathInfo[pathCount];
				var modes = new DisplayConfigModeInfo[modeCount];
				int status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
				if (status == ErrorInsufficientBuffer) continue;
				if (status != 0) return result;
				for (int i = 0; i < pathCount; i++)
				{
					DisplayConfigPathInfo path = paths[i];
					var source = new DisplayConfigSourceDeviceName
					{
						header = new DisplayConfigDeviceInfoHeader
						{
							type = DisplayConfigDeviceInfoGetSourceName,
							size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
							adapterId = path.sourceInfo.adapterId,
							id = path.sourceInfo.id
						}
					};
					if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;
					var advanced = new DisplayConfigGetAdvancedColorInfo
					{
						header = new DisplayConfigDeviceInfoHeader
						{
							type = DisplayConfigDeviceInfoGetAdvancedColorInfo,
							size = (uint)Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo>(),
							adapterId = path.targetInfo.adapterId,
							id = path.targetInfo.id
						}
					};
					(bool? Hdr, int Bits) state = (null, 0);
					if (DisplayConfigGetDeviceInfo(ref advanced) == 0)
					{
						state = ((advanced.value & 0x2) != 0, (int)advanced.bitsPerColorChannel);
					}
					result[source.viewGdiDeviceName ?? ""] = state;
				}
				return result;
			}
		}
		catch
		{
		}
		return result;
	}

	// ------------------------------------------------------------------
	// Lo schermo e' nero? Legge una griglia di punti dal desktop composto.
	// ------------------------------------------------------------------

	internal static bool? SampleScreenBlack()
	{
		try
		{
			foreach (string title in CoverWindowTitles)
			{
				nint cover = FindWindowW(null, title);
				// Una tendina nera voluta (avvio di un gioco) non e' un guasto.
				if (cover != 0 && IsWindowVisible(cover)) return false;
			}
			using var dpi = PhysicalDpiScope.Enter();
			if (dpi is null) return null;
			nint foreground = GetForegroundWindow();
			nint monitor = MonitorFromWindow(foreground, 0);
			var info = MonitorInfoEx.Create();
			if (monitor == 0 || !GetMonitorInfoW(monitor, ref info)) return null;
			int width = info.rcMonitor.Right - info.rcMonitor.Left;
			int height = info.rcMonitor.Bottom - info.rcMonitor.Top;
			if (width <= 0 || height <= 0) return null;
			nint dc = GetDC(0);
			if (dc == 0) return null;
			try
			{
				var samples = new List<uint>();
				for (int row = 1; row <= 5; row++)
				{
					for (int column = 1; column <= 9; column++)
					{
						samples.Add(GetPixel(dc, info.rcMonitor.Left + width * column / 10,
							info.rcMonitor.Top + height * row / 6));
					}
				}
				return GetForegroundWindow() == foreground && MonitorFromWindow(foreground, 0) == monitor
					? DisplayChangePolicy.IsBlack(samples) : null;
			}
			finally
			{
				ReleaseDC(0, dc);
			}
		}
		catch
		{
			return null;
		}
	}

	// ------------------------------------------------------------------
	// Riga di comando di un processo (per distinguere i processi Chromium).
	// ------------------------------------------------------------------

	private static string? ReadCommandLine(nint handle)
	{
		int size = 4096;
		for (int attempt = 0; attempt < 4; attempt++)
		{
			nint buffer = Marshal.AllocHGlobal(size);
			try
			{
				int status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, size, out int needed);
				if (status == 0)
				{
					ushort length = (ushort)Marshal.ReadInt16(buffer);
					nint text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
					return text == 0 || length == 0 ? "" : Marshal.PtrToStringUni(text, length / 2);
				}
				if (needed <= size) return null;
				size = needed;
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}
		return null;
	}

	// ------------------------------------------------------------------
	// Win32
	// ------------------------------------------------------------------

	private const uint QdcOnlyActivePaths = 0x2;
	private const int ErrorInsufficientBuffer = 122;
	private const uint DisplayConfigDeviceInfoGetSourceName = 1;
	private const uint DisplayConfigDeviceInfoGetAdvancedColorInfo = 9;
	private const int ProcessCommandLineInformation = 60;

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MonitorInfoEx
	{
		public uint cbSize;
		public Rect rcMonitor;
		public Rect rcWork;
		public uint dwFlags;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string szDevice;

		public static MonitorInfoEx Create() => new() { cbSize = (uint)Marshal.SizeOf<MonitorInfoEx>(), szDevice = "" };
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Luid
	{
		public uint LowPart;
		public int HighPart;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DisplayConfigRational
	{
		public uint Numerator;
		public uint Denominator;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DisplayConfigPathSourceInfo
	{
		public Luid adapterId;
		public uint id;
		public uint modeInfoIdx;
		public uint statusFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DisplayConfigPathTargetInfo
	{
		public Luid adapterId;
		public uint id;
		public uint modeInfoIdx;
		public uint outputTechnology;
		public uint rotation;
		public uint scaling;
		public DisplayConfigRational refreshRate;
		public uint scanLineOrdering;
		public int targetAvailable;
		public uint statusFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DisplayConfigPathInfo
	{
		public DisplayConfigPathSourceInfo sourceInfo;
		public DisplayConfigPathTargetInfo targetInfo;
		public uint flags;
	}

	// DISPLAYCONFIG_MODE_INFO: 64 byte. Il contenuto non serve, ma la dimensione
	// deve essere esatta perche' QueryDisplayConfig scrive nell'array.
	[StructLayout(LayoutKind.Sequential, Size = 64)]
	private struct DisplayConfigModeInfo
	{
		public uint infoType;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DisplayConfigDeviceInfoHeader
	{
		public uint type;
		public uint size;
		public Luid adapterId;
		public uint id;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DisplayConfigSourceDeviceName
	{
		public DisplayConfigDeviceInfoHeader header;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string viewGdiDeviceName;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DisplayConfigGetAdvancedColorInfo
	{
		public DisplayConfigDeviceInfoHeader header;
		public uint value;
		public uint colorEncoding;
		public uint bitsPerColorChannel;
	}

	private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect rect, nint data);

	[DllImport("user32.dll")]
	private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoEx info);

	[DllImport("user32.dll")]
	private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

	[DllImport("user32.dll")]
	private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths, ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, nint topologyId);

	[DllImport("user32.dll")]
	private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

	[DllImport("user32.dll")]
	private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo request);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern nint FindWindowW(string? className, string windowName);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint window);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint window, uint flags);

	[DllImport("user32.dll")]
	private static extern nint GetDC(nint window);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(nint window, nint dc);

	[DllImport("gdi32.dll")]
	private static extern uint GetPixel(nint dc, int x, int y);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

	[DllImport("kernel32.dll")]
	private static extern bool TerminateProcess(SafeProcessHandle handle, uint exitCode);

	[DllImport("ntdll.dll")]
	private static extern int NtQueryInformationProcess(nint process, int informationClass, nint information, int length, out int returnLength);
}
