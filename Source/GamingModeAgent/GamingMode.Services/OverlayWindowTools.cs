using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace GamingMode.Services;

public sealed record OverlayWindowInfo(
	nint Handle,
	int ProcessId,
	string Title,
	string ProcessName,
	string Path,
	ImageSource? Icon,
	bool IsMinimized);

public static class OverlayWindowTools
{
	private delegate bool EnumWindowsProc(nint window, nint parameter);

	private struct Rect
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct ShellFileInfo
	{
		public nint Icon;
		public int IconIndex;
		public uint Attributes;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		public string DisplayName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
		public string TypeName;
	}

	private const uint GwOwner = 4;
	private const uint WmClose = 0x0010;
	private const int SwRestore = 9;
	private const byte VkMenu = 0x12;
	private const uint KeyeventfKeyup = 0x0002;
	private const uint ShgfiIcon = 0x000000100;
	private const uint ShgfiSmallIcon = 0x000000001;
	private const uint ShgfiLargeIcon = 0x000000000;
	private const uint PwRenderFullContent = 0x00000002;
	private const int Srccopy = 0x00CC0020;
	private const int Halftone = 4;
	private const int DwmwaExtendedFrameBounds = 9;

	private static readonly HashSet<string> HiddenProcesses = new(StringComparer.OrdinalIgnoreCase)
	{
		"GamingMode", "PluginLoader", "PluginLoader_noconsole", "conhost", "cmd", "powershell", "pwsh",
		"TextInputHost", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost"
	};

	// "Program Manager" e' la finestra del desktop di Explorer: non e'
	// un'applicazione e non ha senso mostrarla fra le finestre aperte.
	private static readonly HashSet<string> HiddenTitles = new(StringComparer.OrdinalIgnoreCase)
	{
		"Program Manager", "Windows Input Experience", "Esperienza input di Windows"
	};

	private static bool GetEffectiveBounds(nint window, out Rect bounds)
	{
		if (IsIconic(window))
		{
			WindowPlacement placement = new() { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
			if (GetWindowPlacement(window, ref placement))
			{
				bounds = placement.NormalPosition;
				return true;
			}
		}
		return GetWindowRect(window, out bounds);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WindowPlacement
	{
		public uint Length;
		public uint Flags;
		public uint ShowCommand;
		public int MinX;
		public int MinY;
		public int MaxX;
		public int MaxY;
		public Rect NormalPosition;
	}

	[DllImport("user32.dll")]
	private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);

	public static IReadOnlyList<OverlayWindowInfo> Enumerate()
	{
		List<OverlayWindowInfo> windows = new();
		EnumWindows(delegate(nint window, nint _)
		{
			if (window == 0 || !IsWindowVisible(window) || GetWindow(window, GwOwner) != 0)
			{
				return true;
			}
			// FINESTRE NASCOSTE DA DWM ("cloaked").
			//
			// Windows tiene in vita la finestra di una app del Microsoft Store
			// anche dopo che l'utente l'ha chiusa: resta con lo stile WS_VISIBLE,
			// resta dentro lo schermo, risponde a tutto. Semplicemente non viene
			// piu' disegnata. E' il motivo per cui "Impostazioni" continuava a
			// comparire nell'elenco dopo essere stata chiusa. L'unico modo per
			// accorgersene e' chiederlo a DWM: e' lo stesso controllo che fa il
			// Task Switcher di Windows.
			if (IsCloaked(window)) return true;

			// Finestre di servizio: barre degli strumenti mobili e finestre non
			// attivabili non compaiono in Alt+Tab e non devono comparire qui.
			long exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
			if ((exStyle & WsExNoActivate) != 0) return true;
			if ((exStyle & WsExToolWindow) != 0 && (exStyle & WsExAppWindow) == 0) return true;

			string title = GetTitle(window);
			if (HiddenTitles.Contains(title.Trim())) return true;
			// Una finestra ridotta a icona ha un rettangolo fuori schermo: va
			// misurata sulla posizione che avrebbe da ripristinata, altrimenti
			// sparisce dall'elenco (ed e' proprio il caso della Big Picture
			// mentre la Dashboard e' aperta).
			if (string.IsNullOrWhiteSpace(title) || !GetEffectiveBounds(window, out Rect rect)
				|| rect.Right - rect.Left < 180 || rect.Bottom - rect.Top < 100)
			{
				return true;
			}
			GetWindowThreadProcessId(window, out uint processId);
			// APP DEL MICROSOFT STORE.
			// La cornice appartiene ad ApplicationFrameHost, non alla app: presa
			// cosi' com'e' si otteneva una voce con il nome e l'icona sbagliati
			// accanto a quella giusta. Il processo vero e' quello della finestra
			// interna, che sta dentro la cornice.
			uint hosted = FindHostedProcess(window, processId);
			if (hosted != 0) processId = hosted;
			if (processId == 0 || processId == Environment.ProcessId)
			{
				return true;
			}
			try
			{
				using Process process = Process.GetProcessById((int)processId);
				string processName = process.ProcessName;
				if (HiddenProcesses.Contains(processName))
				{
					return true;
				}
				string path = "";
				try { path = process.MainModule?.FileName ?? ""; } catch { }
				windows.Add(new OverlayWindowInfo(
					window,
					(int)processId,
					title.Trim(),
					processName,
					path,
					LoadIcon(path),
					IsIconic(window)));
			}
			catch
			{
			}
			return true;
		}, 0);
		return windows
			.OrderByDescending(item => item.Handle == GetForegroundWindow())
			.ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
			.ToArray();
	}

	// La finestra e' viva ma non viene disegnata? Allora per l'utente non esiste.
	private static bool IsCloaked(nint window)
	{
		try
		{
			if (DwmGetWindowAttribute(window, DwmwaCloaked, out int cloaked, sizeof(int)) != 0) return false;
			return cloaked != 0;
		}
		catch
		{
			return false;
		}
	}

	// Dentro la cornice di una app del Microsoft Store c'e' una finestra che
	// appartiene al processo vero: se la si trova, si usa quella per nome e
	// icona. Se non c'e' (cornice vuota) la voce non va mostrata affatto.
	private static uint FindHostedProcess(nint frame, uint frameProcessId)
	{
		try
		{
			string className = GetClassNameOf(frame);
			if (!string.Equals(className, "ApplicationFrameWindow", StringComparison.Ordinal)) return 0;
			uint found = 0;
			EnumChildWindows(frame, delegate(nint child, nint _)
			{
				GetWindowThreadProcessId(child, out uint childProcess);
				if (childProcess != 0 && childProcess != frameProcessId)
				{
					found = childProcess;
					return false;
				}
				return true;
			}, 0);
			return found;
		}
		catch
		{
			return 0;
		}
	}

	private static string GetClassNameOf(nint window)
	{
		StringBuilder buffer = new(160);
		int length = GetClassName(window, buffer, buffer.Capacity);
		return length > 0 ? buffer.ToString(0, length) : "";
	}

	private const int DwmwaCloaked = 14;
	private const int GwlExStyle = -20;
	private const long WsExTopmost = 0x00000008;
	private const long WsExToolWindow = 0x00000080L;
	private const long WsExAppWindow = 0x00040000L;
	private const long WsExNoActivate = 0x08000000L;

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern nint GetWindowLongPtr(nint window, int index);

	[DllImport("user32.dll")]
	private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint window, StringBuilder text, int count);

	// Serve al confine con il plugin: sapere quale finestra e' davanti senza
	// esporre l'importazione di sistema.
	public static nint GetForegroundWindowHandle() => GetForegroundWindow();

	public static bool Activate(nint window)
	{
		if (window == 0 || !IsWindow(window)) return false;
		if (IsIconic(window)) ShowWindow(window, SwRestore);
		// Worker threads used by the local API do not necessarily own a Win32
		// message queue. AttachThreadInput silently fails in that case.
		PeekMessage(out _, 0, 0, 0, 0);
		uint currentThread = GetCurrentThreadId();
		uint targetThread = GetWindowThreadProcessId(window, out _);
		uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
		bool targetAttached = false;
		bool foregroundAttached = false;
		try
		{
			// Nessun tap di ALT: era una pressione di tasto vera consegnata alla
			// finestra che stava per tornare in primo piano (di solito Steam).
			if (targetThread != 0 && targetThread != currentThread)
			{
				targetAttached = AttachThreadInput(currentThread, targetThread, true);
			}
			if (foregroundThread != 0 && foregroundThread != currentThread && foregroundThread != targetThread)
			{
				foregroundAttached = AttachThreadInput(currentThread, foregroundThread, true);
			}
			bool raised = BringWindowToTop(window);
			SetForegroundWindow(window);
			if (GetForegroundWindow() == window) return true;

			// Windows can reject SetForegroundWindow when the agent has not
			// received recent user input. SwitchToThisWindow performs the same
			// foreground hand-off used by the shell without manufacturing an ALT
			// key press that could leak into Steam or the running game.
			SwitchToThisWindow(window, true);
			AllowSetForegroundWindow(unchecked((uint)-1));
			LockSetForegroundWindow(2); // LSFW_UNLOCK
			SetWindowPos(window, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
			SetWindowPos(window, new nint(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
			raised = BringWindowToTop(window) || raised;
			SetForegroundWindow(window);
			if (GetForegroundWindow() == window) return true;

			// GameInputSvc can temporarily own the foreground with an invisible
			// service window. In that specific lock state Windows only releases
			// the foreground privilege after a complete ALT transition. Keep this
			// as the last resort so ordinary Dashboard navigation never injects a
			// key into the application being left.
			keybd_event(VkMenu, 0, 0, 0);
			SetForegroundWindow(window);
			keybd_event(VkMenu, 0, KeyeventfKeyup, 0);
			if (GetForegroundWindow() == window) return true;

			// GameInputSvc may immediately reclaim GetForegroundWindow even though
			// the requested application is now visually on top. Tell that window
			// to repaint its active state (Steam uses it for controller hover) and
			// report the successful Z-order hand-off to avoid repeated switching.
			PostMessage(window, 0x0006, new nint(1), 0); // WM_ACTIVATE / WA_ACTIVE
			PostMessage(window, 0x0007, 0, 0);           // WM_SETFOCUS
			// At this point the requested window has been restored, raised and sent
			// the activation messages. GameInputSvc can still make the Win32 return
			// values lie about the foreground owner, so acknowledge the completed
			// hand-off and prevent the Dashboard from repeating it three times.
			return true;
		}
		finally
		{
			if (foregroundAttached) AttachThreadInput(currentThread, foregroundThread, false);
			if (targetAttached) AttachThreadInput(currentThread, targetThread, false);
		}
	}

	public static bool RequestClose(nint window)
	{
		return window != 0 && IsWindow(window) && PostMessage(window, WmClose, 0, 0);
	}

	// I collegamenti (.lnk) vanno risolti al programma a cui puntano: l'icona di
	// un collegamento porta con se' la freccetta di sistema, e non e' quella che
	// si vuole vedere fra le app preferite.
	public static ImageSource? LoadFileIcon(string path) => LoadIcon(ResolveShortcut(path));

	public static string ResolveShortcut(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path;
		try
		{
			Type? type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"));
			if (type is null) return path;
			object? instance = Activator.CreateInstance(type);
			if (instance is not IPersistFile persist || instance is not IShellLink link) return path;
			persist.Load(path, 0);
			System.Text.StringBuilder target = new(260);
			link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
			string resolved = target.ToString();
			return string.IsNullOrWhiteSpace(resolved) ? path : resolved;
		}
		catch
		{
			return path;
		}
	}

	[ComImport]
	[Guid("000214F9-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellLink
	{
		void GetPath([Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, nint findData, uint flags);
		void GetIDList(out nint list);
		void SetIDList(nint list);
		void GetDescription([Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
		void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
		void GetWorkingDirectory([Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory, int maxPath);
		void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
		void GetArguments([Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments, int maxArguments);
		void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
		void GetHotkey(out short hotkey);
		void SetHotkey(short hotkey);
		void GetShowCmd(out int command);
		void SetShowCmd(int command);
		void GetIconLocation([Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder icon, int maxIcon, out int index);
		void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
		void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
		void Resolve(nint window, uint flags);
		void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
	}

	[ComImport]
	[Guid("0000010b-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPersistFile
	{
		void GetClassID(out Guid classId);
		[PreserveSig] int IsDirty();
		void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
		void Save([MarshalAs(UnmanagedType.LPWStr)] string? file, [MarshalAs(UnmanagedType.Bool)] bool remember);
		void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string? file);
		void GetCurFile([Out][MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file);
	}

	// Finestre di primo livello appartenenti a Steam (client e UI gamepad).
	// Sospende l'interazione di Steam mentre la Dashboard e' aperta.
	//
	// Steam Input applica la configurazione della Big Picture finche' quella
	// finestra e' in esecuzione e a schermo: notificarle la perdita di
	// attivazione non basta, continua a interpretare il pad e a navigare sotto.
	// Riducendola a icona, Steam passa alla configurazione desktop e smette di
	// consumare il controller. Allo scorrere della Dashboard viene ripristinata
	// esattamente com'era.
	private const int ShowMinimizeNoActivate = 7;
	private const int ShowRestore = 9;
	private const int ShowRestoreNoActivate = 4;

	public static IReadOnlyList<nint> MinimizeSteamWindows()
	{
		List<nint> minimized = new();
		try
		{
			foreach (nint window in FindSteamWindows())
			{
				if (IsIconic(window)) continue;
				if (!GetWindowRect(window, out Rect bounds)) continue;
				// Solo le finestre grandi: la lista amici o un popup non
				// intercettano il controller e non vanno toccati.
				if (bounds.Right - bounds.Left < 900 || bounds.Bottom - bounds.Top < 600) continue;
				if (ShowWindowAsync(window, ShowMinimizeNoActivate)) minimized.Add(window);
			}
		}
		catch
		{
		}
		return minimized;
	}

	// Il ripristino NON attiva le finestre: se Steam tornasse in primo piano
	// dopo la chiusura della Dashboard ruberebbe l'attivazione all'applicazione
	// scelta dall'utente, e il borderless (che agisce solo sulla finestra in
	// primo piano, per le app che non sono giochi) non verrebbe mai applicato.
	public static void RestoreWindows(IReadOnlyList<nint> windows)
	{
		foreach (nint window in windows)
		{
			try { ShowWindowAsync(window, ShowRestoreNoActivate); } catch { }
		}
	}

	public static IReadOnlyList<nint> FindSteamWindows()
	{
		List<nint> windows = new();
		try
		{
			EnumWindows(delegate (nint window, nint _)
			{
				if (window == 0 || !IsWindowVisible(window)) return true;
				GetWindowThreadProcessId(window, out uint processId);
				if (processId == 0) return true;
				try
				{
					using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById((int)processId);
					string name = process.ProcessName;
					if (name.Equals("steam", StringComparison.OrdinalIgnoreCase)
						|| name.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase))
					{
						windows.Add(window);
					}
				}
				catch
				{
				}
				return true;
			}, 0);
		}
		catch
		{
		}
		return windows;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeMessage
	{
		public nint Window;
		public uint Message;
		public nuint WParam;
		public nint LParam;
		public uint Time;
		public int PointX;
		public int PointY;
		public uint Private;
	}

	// The Big Picture compositor is the largest usable top-level Steam window.
	// Keeping this selection in one place ensures activation and DWM mirroring
	// always target the same HWND.
	public static nint FindSteamUiWindow()
	{
		nint best = 0;
		long bestArea = 0;
		foreach (nint window in FindSteamWindows())
		{
			if (!IsWindow(window) || IsCloaked(window)) continue;
			if (!GetWindowRect(window, out Rect bounds)) continue;
			long area = (long)Math.Max(0, bounds.Right - bounds.Left)
				* Math.Max(0, bounds.Bottom - bounds.Top);
			if (area <= bestArea) continue;
			bestArea = area;
			best = window;
		}
		return best;
	}

	public static bool IsUsableWindow(nint window)
	{
		try { return window != 0 && IsWindow(window) && !IsHungAppWindow(window); }
		catch { return false; }
	}

	public static string DescribeWindowHandle(nint window) => DescribeWindow(window);

	public static bool IsSteamWindow(nint window)
	{
		if (window == 0) return false;
		try
		{
			GetWindowThreadProcessId(window, out uint processId);
			using Process process = Process.GetProcessById((int)processId);
			return process.ProcessName.Equals("steam", StringComparison.OrdinalIgnoreCase)
				|| process.ProcessName.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	// Porta avanti Steam. Serve quando la Dashboard viene aperta con la
	// combinazione da tastiera mentre in primo piano c'e' un'altra finestra: la
	// pagina si apre dentro Steam, ma se Steam resta dietro non la si vede.
	//
	// Si riusa Activate, che aggancia il thread del primo piano invece di
	// simulare tasti: nessuna pressione finisce dentro Steam, che e' la
	// scorciatoia che a suo tempo faceva arrivare Invio alla Big Picture.
	//
	// Fra le finestre di Steam si sceglie la piu' grande: la Big Picture occupa
	// lo schermo, mentre le altre sono pannelli e finestre di servizio.
	public static bool ActivateSteam() => ActivateSteam(out _);

	// Steam e' gia' davanti? Se lo e', non c'e' niente da fare: attivare una
	// finestra che e' gia' attiva e' lavoro sprecato, e ogni intromissione in
	// piu' e' un'occasione in piu' di disturbare qualcun altro.
	public static bool IsSteamForeground()
	{
		try
		{
			nint foreground = GetForegroundWindow();
			if (foreground == 0) return false;
			GetWindowThreadProcessId(foreground, out uint processId);
			if (processId == 0) return false;
			using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById((int)processId);
			string name = process.ProcessName;
			return name.Equals("steam", StringComparison.OrdinalIgnoreCase)
				|| name.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	public static bool ActivateSteam(out string report)
	{
		report = "";
		try
		{
			nint before = GetForegroundWindow();
			string beforeName = DescribeWindow(before);

			nint best = FindSteamUiWindow();
			int candidates = FindSteamWindows().Count;

			if (best == 0)
			{
				report = $"nessuna finestra di Steam utilizzabile (candidate={candidates}, prima='{beforeName}')";
				return false;
			}

			bool ok = Activate(best);
			bool foreground = IsSteamForeground();
			report = $"scelta 0x{best:X} candidate={candidates}, prima='{beforeName}', dopo='{DescribeWindow(GetForegroundWindow())}', esito={(foreground ? "in primo piano" : ok ? "attivazione richiesta" : "negato da Windows")}";
			return foreground;
		}
		catch (Exception exception)
		{
			report = "eccezione: " + exception.Message;
			return false;
		}
	}

	// FOTOGRAFIA DELLO SCHERMO, PER IL LOG.
	//
	// Serve a rispondere alla domanda che finora si poteva solo tirare a
	// indovinare: in questo istante, chi e' davanti, chi e' sempre in primo
	// piano, e la nostra finestra c'e' ancora? Senza questi tre dati un guasto
	// visivo e' indistinguibile da un guasto logico.
	public static string DescribeScreen(nint ourWindow)
	{
		try
		{
			nint foreground = GetForegroundWindow();
			string state = $"davanti='{DescribeWindow(foreground)}'";

			if (ourWindow != 0 && IsWindow(ourWindow))
			{
				long ex = GetWindowLongPtr(ourWindow, GwlExStyle).ToInt64();
				bool topmost = (ex & WsExTopmost) != 0;
				state += $", nostra finestra: visibile={IsWindowVisible(ourWindow)} sempreDavanti={topmost}";
			}
			else if (ourWindow != 0)
			{
				state += ", nostra finestra: non esiste piu'";
			}

			// Chi altro pretende di stare sopra a tutto. E' l'informazione che
			// mancava quando Big Picture restava sepolta: qui si vede subito.
			List<string> topmosts = new();
			try
			{
				EnumWindows(delegate (nint window, nint _)
				{
					if (window == ourWindow || !IsWindowVisible(window) || IsCloaked(window)) return true;
					long ex = GetWindowLongPtr(window, GwlExStyle).ToInt64();
					if ((ex & WsExTopmost) == 0) return true;
					if (!GetWindowRect(window, out Rect bounds)) return true;
					// Solo finestre grandi: le notifiche e i suggerimenti non
					// interessano e riempirebbero il log.
					if (bounds.Right - bounds.Left < 400 || bounds.Bottom - bounds.Top < 300) return true;
					if (topmosts.Count < 5) topmosts.Add(DescribeWindow(window));
					return true;
				}, 0);
			}
			catch
			{
			}

			state += topmosts.Count == 0
				? ", nessun'altra finestra sempre davanti"
				: ", sempre davanti anche: " + string.Join(" ", topmosts);

			return state;
		}
		catch (Exception exception)
		{
			return "fotografia non riuscita: " + exception.Message;
		}
	}

	// Serve solo al log: senza sapere chi era davanti prima e chi dopo, un
	// tentativo fallito di portare avanti Steam e' indistinguibile da uno
	// riuscito che poi viene scavalcato.
	private static string DescribeWindow(nint window)
	{
		if (window == 0) return "nessuna";
		try
		{
			GetWindowThreadProcessId(window, out uint processId);
			using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById((int)processId);
			return $"{process.ProcessName}({processId})";
		}
		catch
		{
			return $"0x{window:X}";
		}
	}

	public static ImageSource? CapturePreview(nint window, int maximumWidth = 720, int maximumHeight = 405)
	{
		if (window == 0 || !IsWindow(window) || IsHungAppWindow(window)) return null;
		Rect bounds;
		if (DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out bounds, Marshal.SizeOf<Rect>()) != 0
			&& !GetWindowRect(window, out bounds))
		{
			return null;
		}
		int sourceWidth = Math.Clamp(bounds.Right - bounds.Left, 1, 8192);
		int sourceHeight = Math.Clamp(bounds.Bottom - bounds.Top, 1, 8192);
		double scale = Math.Min(1d, Math.Min((double)maximumWidth / sourceWidth, (double)maximumHeight / sourceHeight));
		int targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
		int targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

		nint screenDc = 0;
		nint sourceDc = 0;
		nint sourceBitmap = 0;
		nint sourcePrevious = 0;
		nint targetDc = 0;
		nint targetBitmap = 0;
		nint targetPrevious = 0;
		try
		{
			screenDc = GetDC(0);
			if (screenDc == 0) return null;
			sourceDc = CreateCompatibleDC(screenDc);
			sourceBitmap = CreateCompatibleBitmap(screenDc, sourceWidth, sourceHeight);
			if (sourceDc == 0 || sourceBitmap == 0) return null;
			sourcePrevious = SelectObject(sourceDc, sourceBitmap);

			// PrintWindow frequently reports success while returning an all-black
			// surface for Chromium, DirectX and Vulkan windows. The foreground is
			// physically visible when the Dashboard hotkey is handled, so copy that
			// exact frame first; use PrintWindow only for an occluded window.
			bool foreground = GetForegroundWindow() == window && !IsIconic(window);
			bool captured = foreground
				? BitBlt(sourceDc, 0, 0, sourceWidth, sourceHeight, screenDc, bounds.Left, bounds.Top, Srccopy)
				: PrintWindow(window, sourceDc, PwRenderFullContent);
			if (!captured && !IsIconic(window))
			{
				captured = BitBlt(sourceDc, 0, 0, sourceWidth, sourceHeight, screenDc, bounds.Left, bounds.Top, Srccopy);
			}
			if (!captured) return null;

			targetDc = CreateCompatibleDC(screenDc);
			targetBitmap = CreateCompatibleBitmap(screenDc, targetWidth, targetHeight);
			if (targetDc == 0 || targetBitmap == 0) return null;
			targetPrevious = SelectObject(targetDc, targetBitmap);
			SetStretchBltMode(targetDc, Halftone);
			SetBrushOrgEx(targetDc, 0, 0, 0);
			if (!StretchBlt(targetDc, 0, 0, targetWidth, targetHeight, sourceDc, 0, 0, sourceWidth, sourceHeight, Srccopy))
			{
				return null;
			}

			BitmapSource preview = Imaging.CreateBitmapSourceFromHBitmap(
				targetBitmap,
				0,
				Int32Rect.Empty,
				BitmapSizeOptions.FromWidthAndHeight(targetWidth, targetHeight));
			preview.Freeze();
			return preview;
		}
		catch
		{
			return null;
		}
		finally
		{
			if (targetPrevious != 0 && targetDc != 0) SelectObject(targetDc, targetPrevious);
			if (sourcePrevious != 0 && sourceDc != 0) SelectObject(sourceDc, sourcePrevious);
			if (targetBitmap != 0) DeleteObject(targetBitmap);
			if (sourceBitmap != 0) DeleteObject(sourceBitmap);
			if (targetDc != 0) DeleteDC(targetDc);
			if (sourceDc != 0) DeleteDC(sourceDc);
			if (screenDc != 0) ReleaseDC(0, screenDc);
		}
	}

	private static ImageSource? LoadIcon(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;
		// Le icone devono essere NITIDE anche su schermi 4K. Prima veniva estratta
		// la variante piccola (16 px) e poi ingrandita: il risultato era sfocato.
		// Qui si chiede prima la risorsa piu' grande disponibile (256 px, poi 128
		// e 64) e si conserva la risoluzione nativa, senza riscalarla in fase di
		// creazione. Solo se l'eseguibile non espone icone grandi si ripiega
		// sull'icona di sistema, chiedendo comunque quella grande.
		foreach (int size in new[] { 256, 128, 64 })
		{
			nint large = 0;
			nint small = 0;
			try
			{
				uint requested = (uint)((size & 0xFFFF) | (32 << 16));
				if (SHDefExtractIcon(path, 0, 0, out large, out small, requested) == 0 && large != 0)
				{
					ImageSource highResolution = Imaging.CreateBitmapSourceFromHIcon(
						large,
						System.Windows.Int32Rect.Empty,
						System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
					highResolution.Freeze();
					return highResolution;
				}
			}
			catch
			{
			}
			finally
			{
				if (large != 0) DestroyIcon(large);
				if (small != 0) DestroyIcon(small);
			}
		}

		nint icon = 0;
		try
		{
			ShellFileInfo info = new();
			SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon | ShgfiLargeIcon);
			icon = info.Icon;
			if (icon == 0) return null;
			ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
				icon,
				System.Windows.Int32Rect.Empty,
				System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
			source.Freeze();
			return source;
		}
		catch
		{
			return null;
		}
		finally
		{
			if (icon != 0) DestroyIcon(icon);
		}
	}

	private static string GetTitle(nint window)
	{
		int length = GetWindowTextLength(window);
		if (length <= 0) return "";
		StringBuilder title = new(length + 1);
		GetWindowText(window, title, title.Capacity);
		return title.ToString();
	}

	[DllImport("user32.dll")]
	private static extern bool ShowWindowAsync(nint window, int command);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint window);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(nint window);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint window);

	[DllImport("user32.dll")]
	private static extern nint GetWindow(nint window, uint command);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint window, out Rect rect);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint window);

	[DllImport("user32.dll")]
	private static extern void SwitchToThisWindow(nint window, bool altTab);

	[DllImport("user32.dll")]
	private static extern bool AllowSetForegroundWindow(uint processId);

	[DllImport("user32.dll")]
	private static extern bool LockSetForegroundWindow(uint lockCode);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll")]
	private static extern bool PeekMessage(out NativeMessage message, nint window, uint min, uint max, uint remove);

	[DllImport("user32.dll")]
	private static extern bool BringWindowToTop(nint window);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint window, int command);

	[DllImport("user32.dll")]
	private static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetWindowTextLength(nint window);

	[DllImport("user32.dll")]
	private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern nint SHGetFileInfo(string path, uint attributes, ref ShellFileInfo info, uint infoSize, uint flags);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out nint largeIcon, out nint smallIcon, uint iconSize);

	[DllImport("user32.dll")]
	private static extern bool DestroyIcon(nint icon);

	[DllImport("user32.dll")]
	private static extern bool IsHungAppWindow(nint window);

	[DllImport("user32.dll")]
	private static extern nint GetDC(nint window);

	[DllImport("user32.dll")]
	private static extern int ReleaseDC(nint window, nint deviceContext);

	[DllImport("user32.dll")]
	private static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

	[DllImport("gdi32.dll")]
	private static extern nint CreateCompatibleDC(nint deviceContext);

	[DllImport("gdi32.dll")]
	private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

	[DllImport("gdi32.dll")]
	private static extern nint SelectObject(nint deviceContext, nint value);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(nint value);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteDC(nint deviceContext);

	[DllImport("gdi32.dll")]
	private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int operation);

	[DllImport("gdi32.dll")]
	private static extern bool StretchBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);

	[DllImport("gdi32.dll")]
	private static extern int SetStretchBltMode(nint deviceContext, int mode);

	[DllImport("gdi32.dll")]
	private static extern bool SetBrushOrgEx(nint deviceContext, int x, int y, nint previousPoint);

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(nint window, int attribute, out Rect value, int valueSize);
}
